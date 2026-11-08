using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Everything the players see of the dragon (runs on every client):
    ///  - plays the model's clips (idle / walk / run blend, hover, fly) from the replicated speed and move;
    ///  - layers the move's procedural pose on top (DragonMoves): neck and head for bites, roars and
    ///    the breath sweep, tail coil/whip, body rearing up for the stomp, collapse on death, head
    ///    tracking the target when idle;
    ///  - the fire breath (particle stream + light) aimed exactly where the server deals the damage;
    ///  - footsteps, wing beats, the breath roar.
    /// The model's own clips walk in place; the root bone's horizontal drift is cancelled.
    /// </summary>
    public class DragonVisuals : MonoBehaviour
    {
        public const string Locomotion = "Locomotion", Hover = "Hover", Fly = "Fly";

        public DragonAI ai;
        public Animator animator;
        [Tooltip("Visual root under the dragon (offset/roll for lunges, rearing, collapse).")]
        public Transform modelRoot;
        public Transform rootBone, frontBody, head;
        public Transform[] neck;   // from the body to the head
        public Transform[] tail;   // from the body to the tip
        public Transform[] feet;
        public ParticleSystem breath;
        public Light mouthLight;
        [Tooltip("Model scale (1 = the 17 m dragon of the source file).")]
        public float scale = 1f;

        static readonly float[] NeckW = { 0.18f, 0.24f, 0.27f, 0.31f };
        static readonly float[] TailW = { 0.22f, 0.3f, 0.28f, 0.2f };

        string _clip;
        float _speed, _lookYaw, _stepTimer, _wingTimer, _fireSfxTimer, _smokeTimer, _breathe;
        Vector3 _rootRest;
        Vector3 _modelRest;
        DragonPose _pose;

        void Awake()
        {
            if (rootBone) _rootRest = rootBone.localPosition;
            if (modelRoot) _modelRest = modelRoot.localPosition;
            if (breath == null && head) breath = CreateBreath();
        }

        public Vector3 HeadPosition => head ? head.position : transform.position + Vector3.up * 4f * scale + transform.forward * 4f * scale;

        /// <summary>Direction of the fire for a pose (same maths on the server).</summary>
        public Vector3 BreathDirection(DragonPose p)
        {
            Vector3 f = Quaternion.AngleAxis(p.neckYaw, Vector3.up) * transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f).normalized;
            return Quaternion.AngleAxis(13f, right) * f;
        }

        public Vector3 MouthPosition(DragonPose p) => HeadPosition + BreathDirection(p) * 0.9f * scale;

        void Update()
        {
            if (ai == null || animator == null) return;
            // ---- clip
            var m = ai.CurrentMove;
            string clip = ai.IsAliveVisual ? DragonMoves.Clip(m, ai.MoveTime) : Locomotion;
            if (clip != _clip)
            {
                _clip = clip;
                animator.CrossFadeInFixedTime(clip, 0.35f, 0);
            }
            float target = ai.LocoSpeed;
            if (m == DragonMove.Charge) { float mt = ai.MoveTime; target = mt >= DragonMoves.ChargeRun && mt < DragonMoves.ChargeRunEnd ? 13f : mt < DragonMoves.ChargeRun ? 0f : ai.LocoSpeed; }
            _speed = Mathf.MoveTowards(_speed, target, Time.deltaTime * (m == DragonMove.Charge ? 30f : 10f));
            animator.SetFloat("Speed", _speed / Mathf.Max(0.1f, scale));
            animator.speed = !ai.IsAliveVisual && ai.MoveTime > 1.2f ? 0f : 1f;

            // ---- sounds
            if (ai.IsAliveVisual && _clip == Locomotion && _speed > 0.6f)
            {
                _stepTimer -= Time.deltaTime * Mathf.Clamp(_speed / 3f, 0.6f, 2.6f);
                if (_stepTimer <= 0f)
                {
                    _stepTimer = 0.62f;
                    Transform ft = feet != null && feet.Length > 0 ? feet[Random.Range(0, feet.Length)] : null;
                    Vector3 fp = ft ? ft.position : transform.position;
                    Sfx.Play(SfxId.DragonStep, fp, 0.8f, Random.Range(0.55f, 0.7f));
                    var cam = SoulsCamera.Instance;
                    if (cam && Vector3.Distance(cam.transform.position, transform.position) < 18f) SoulsCamera.Shake(0.012f, 0.12f);
                }
            }
            // smoke and sparks from the nostrils (stronger when enraged or about to breathe fire)
            if (ai.IsAliveVisual && ai.FightActive && head)
            {
                _smokeTimer -= Time.deltaTime;
                float heat = Mathf.Max(ai.Phase >= 2 ? 0.6f : 0.2f, _pose.glow);
                if (_smokeTimer <= 0f)
                {
                    _smokeTimer = Mathf.Lerp(1.6f, 0.25f, heat);
                    BossFx.Embers(HeadPosition + transform.forward * 0.6f * scale, 0.15f * scale, Mathf.RoundToInt(4 + heat * 10f), 1.5f);
                }
            }
            if (_clip == Hover || _clip == Fly)
            {
                _wingTimer -= Time.deltaTime;
                if (_wingTimer <= 0f) { _wingTimer = 0.72f; Sfx.Play(SfxId.DragonWing, transform.position + Vector3.up * 3f * scale, 1f, Random.Range(0.45f, 0.55f)); }
            }
        }

        void LateUpdate()
        {
            if (ai == null) return;
            _pose = ai.CurrentPose;
            // the beast breathes: the chest heaves, the neck rises and falls (strongest when idle)
            float idleW = ai.CurrentMove == DragonMove.None && ai.IsAliveVisual ? 1f : ai.IsAliveVisual ? 0.35f : 0f;
            _breathe += Time.deltaTime * (ai.Phase >= 2 ? 2.1f : 1.5f);
            float br = Mathf.Sin(_breathe);
            _pose.bodyDrop += br * 0.05f * idleW;
            _pose.neckPitch += br * 3.5f * idleW;
            _pose.headPitch += Mathf.Sin(_breathe * 0.5f + 1f) * 3f * idleW;
            _pose.bodyPitch += br * 1.2f * idleW;

            // head follows the target when not busy
            float look = 0f;
            var t = ai.VisualTarget;
            if (t && ai.CurrentMove == DragonMove.None && ai.IsAliveVisual)
            {
                Vector3 to = MathUtil.Flat(t.transform.position - transform.position);
                look = Mathf.Clamp(Vector3.SignedAngle(MathUtil.Flat(transform.forward), to, Vector3.up), -50f, 50f);
            }
            _lookYaw = Mathf.MoveTowards(_lookYaw, look, Time.deltaTime * 60f);
            float neckYaw = _pose.neckYaw + _lookYaw;

            if (modelRoot)
            {
                modelRoot.localPosition = _modelRest + new Vector3(0f, _pose.bodyDrop * scale, _pose.lunge * scale);
                modelRoot.localRotation = Quaternion.Euler(0f, 0f, _pose.bodyRoll);
            }
            // cancel the clips' horizontal root drift (the AI moves the dragon), measured in the
            // model's own frame so it doesn't depend on the FBX bone axes
            if (rootBone && rootBone.parent && modelRoot)
            {
                Vector3 cur = modelRoot.InverseTransformPoint(rootBone.position);
                Vector3 rest = modelRoot.InverseTransformPoint(rootBone.parent.TransformPoint(_rootRest));
                rootBone.position = modelRoot.TransformPoint(new Vector3(rest.x, cur.y, rest.z));
            }
            Vector3 right = transform.right, up = Vector3.up;
            if (frontBody && Mathf.Abs(_pose.bodyPitch) > 0.01f)
                frontBody.rotation = Quaternion.AngleAxis(_pose.bodyPitch, right) * frontBody.rotation;
            if (neck != null)
                for (int i = 0; i < neck.Length; i++)
                {
                    if (!neck[i]) continue;
                    float w = NeckW[Mathf.Min(i, NeckW.Length - 1)];
                    neck[i].rotation = Quaternion.AngleAxis(neckYaw * w, up) * Quaternion.AngleAxis(_pose.neckPitch * w, right) * neck[i].rotation;
                }
            if (head && Mathf.Abs(_pose.headPitch) > 0.01f) head.rotation = Quaternion.AngleAxis(_pose.headPitch, right) * head.rotation;
            if (tail != null)
                for (int i = 0; i < tail.Length; i++)
                {
                    if (!tail[i]) continue;
                    float w = TailW[Mathf.Min(i, TailW.Length - 1)];
                    float sway = Mathf.Sin(Time.time * 1.3f + i * 0.7f) * 4f;   // a living tail
                    tail[i].rotation = Quaternion.AngleAxis((_pose.tailYaw + sway) * w, up) * tail[i].rotation;
                }

            // ---- fire
            var p = _pose; p.neckYaw = neckYaw;
            if (breath)
            {
                breath.transform.SetPositionAndRotation(MouthPosition(p), Quaternion.LookRotation(BreathDirection(p)));
                var em = breath.emission;
                em.enabled = _pose.breathing && ai.IsAliveVisual;
                if (_pose.breathing)
                {
                    _fireSfxTimer -= Time.deltaTime;
                    if (_fireSfxTimer <= 0f) { _fireSfxTimer = 0.45f; Sfx.Play(SfxId.DragonFire, breath.transform.position, 1f, Random.Range(0.75f, 0.9f)); }
                }
            }
            if (mouthLight)
            {
                mouthLight.transform.position = HeadPosition + BreathDirection(p) * 0.6f * scale;
                float g = _pose.breathing ? 6f + Mathf.PerlinNoise(Time.time * 9f, 0f) * 3f : _pose.glow * 3f;
                mouthLight.intensity = ai.IsAliveVisual ? g : 0f;
            }
        }

        ParticleSystem CreateBreath()
        {
            var go = new GameObject("FireBreath");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(15f * scale, 20f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f * scale, 0.9f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.82f, 0.45f, 0.95f), new Color(1f, 0.45f, 0.12f, 0.9f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.gravityModifier = -0.15f;
            var em = ps.emission; em.rateOverTime = 170f; em.enabled = false;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 9f; shape.radius = 0.15f * scale;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.4f), new Keyframe(0.5f, 2.4f), new Keyframe(1f, 4f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.75f), 0), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.35f), new GradientColorKey(new Color(0.25f, 0.08f, 0.04f), 1f) },
                      new[] { new GradientAlphaKey(0.95f, 0), new GradientAlphaKey(0.75f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var noise = ps.noise; noise.enabled = true; noise.strength = 1.2f * scale; noise.frequency = 0.6f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (Fx.Instance) r.material = Fx.Instance.additiveMaterial;
            ps.Play();
            return ps;
        }
    }
}
