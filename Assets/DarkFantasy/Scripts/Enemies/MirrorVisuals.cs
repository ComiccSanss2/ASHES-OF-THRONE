using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Everything the players see of the Shattered Mirror (every client, from MirrorAI's pose):
    ///  - the gothic frame hovers, tilts and spins on its magic axis (Body);
    ///  - two huge demonic arms reach out of the frame: two-bone IK from the shoulders to the hands
    ///    the pose places, elbows bent outward and up like a spider's legs; claws curl into fists;
    ///  - the glass: cracked vortex texture whose glow follows the pose, fully shattered in phase 2;
    ///    the portal swirl opens for the Soul Vortex;
    ///  - shards orbit the frame (more and wilder in phase 2), black smoke bleeds from the cracks,
    ///    the hands leave black-fire wakes in phase 2; shards tremble on dashes;
    ///  - sounds: hands dragging on the stone, the low hum of the glass.
    /// </summary>
    public class MirrorVisuals : MonoBehaviour
    {
        [System.Serializable]
        public class Arm
        {
            public Transform upper, fore, hand;
            [Tooltip("Finger roots (each has one child joint: the claw).")]
            public Transform[] fingers;
            public Vector3 shoulder = new Vector3(2.7f, 3.7f, 0.2f);   // body space
            public float upperLength = 3.7f, foreLength = 3.6f;
            [HideInInspector] public ParticleSystem fire;
            [HideInInspector] public Quaternion[] fingerRest, clawRest;
            [HideInInspector] public Vector3 lastHand;
            [HideInInspector] public float dragTimer;
        }

        public MirrorAI ai;
        [Tooltip("Frame pivot (hover, tilt, spin).")]
        public Transform body;
        public Renderer glass;
        public Texture glassIntact, glassShattered;
        public Transform portal;
        public Renderer portalRenderer;
        public Light glassLight;
        public Arm left = new Arm(), right = new Arm();
        public int orbitCount = 14, orbitCountP2 = 30;

        MaterialPropertyBlock _mpb;
        Transform[] _orbit;
        Vector3[] _orbitSeed;
        ParticleSystem _smoke;
        float _hum, _spin, _glow;
        int _phaseApplied;
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap"), Emis = Shader.PropertyToID("_EmissionColor"), BaseCol = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (!ai) ai = GetComponent<MirrorAI>();
            foreach (var a in new[] { left, right })
            {
                if (a.fingers == null) continue;
                a.fingerRest = new Quaternion[a.fingers.Length];
                a.clawRest = new Quaternion[a.fingers.Length];
                for (int i = 0; i < a.fingers.Length; i++)
                {
                    if (!a.fingers[i]) continue;
                    a.fingerRest[i] = a.fingers[i].localRotation;
                    if (a.fingers[i].childCount > 0) a.clawRest[i] = a.fingers[i].GetChild(0).localRotation;
                }
            }
        }

        void Start()
        {
            BuildOrbit();
            _smoke = MakeSmoke();
            left.fire = MakeHandFire(left.hand);
            right.fire = MakeHandFire(right.hand);
        }

        // ------------------------------------------------------------------ build-time helpers
        void BuildOrbit()
        {
            var fx = MirrorFx.Instance;
            int n = orbitCountP2;
            _orbit = new Transform[n];
            _orbitSeed = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("OrbitShard");
                go.transform.SetParent(transform, false);
                if (fx && fx.shardMesh)
                {
                    go.AddComponent<MeshFilter>().sharedMesh = fx.shardMesh;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = fx.shardMaterial;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                go.transform.localScale = Vector3.one * Random.Range(1.2f, 3.2f);
                _orbit[i] = go.transform;
                // radius, height, speed
                _orbitSeed[i] = new Vector3(Random.Range(3.4f, 6.5f), Random.Range(1.2f, 8.5f), Random.Range(0.25f, 0.7f) * (Random.value < 0.5f ? -1f : 1f));
                go.SetActive(i < orbitCount);
            }
        }

        ParticleSystem MakeSmoke()
        {
            if (!body) return null;
            var go = new GameObject("CrackSmoke"); go.transform.SetParent(body, false);
            go.transform.localPosition = new Vector3(0f, ai ? ai.glassHeight : 4.2f, 0.2f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main; m.loop = true; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 400;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            m.startSize = new ParticleSystem.MinMaxCurve(1.0f, 2.4f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.05f, 0.03f, 0.07f, 0.65f), new Color(0.12f, 0.05f, 0.18f, 0.5f));
            m.gravityModifier = -0.05f;
            var em = ps.emission; em.rateOverTime = 6f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(4f, 5.6f, 0.6f);
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 1.8f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0f, 0), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1) });
            col.color = g;
            var no = ps.noise; no.enabled = true; no.strength = 0.6f; no.frequency = 0.4f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (Fx.Instance) r.material = Fx.Instance.alphaMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        ParticleSystem MakeHandFire(Transform hand)
        {
            if (!hand) return null;
            var go = new GameObject("DarkFireWake"); go.transform.SetParent(hand, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main; m.loop = true; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 500;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.4f, 1f, 1f), new Color(0.35f, 0.05f, 0.6f, 1f));
            m.gravityModifier = -0.3f;
            var em = ps.emission; em.rateOverTime = 0f; em.rateOverDistance = 0f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.7f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 1f), 0), new GradientColorKey(new Color(0.4f, 0.05f, 0.7f), 0.4f), new GradientColorKey(new Color(0.02f, 0f, 0.04f), 1) },
                      new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1) });
            col.color = g;
            var no = ps.noise; no.enabled = true; no.strength = 0.8f; no.frequency = 1f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (Fx.Instance) r.material = Fx.Instance.additiveMaterial;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ per frame
        Vector3 BodyToWorld(Vector3 local, MirrorPose p)
        {
            Vector3 l = Quaternion.Euler(0f, p.bodyYaw, 0f) * local;
            return transform.TransformPoint(l + Vector3.up * p.hover);
        }

        void LateUpdate()
        {
            if (!ai) return;
            var p = ai.CurrentPose;
            bool alive = ai.IsAlive;
            bool active = ai.FightActive || !alive;
            bool p2 = ai.Phase >= 2;
            float t = Time.time;

            // dormant: the mirror slumps on its pedestal, the glass a dull sheen
            if (!active && alive) p = MirrorMoves.Evaluate(MirrorMove.Awaken, 0f, 0f, 0f);

            // idle life: a slow breathing hover, the frame drifting on its axis
            float idleW = ai.CurrentMove == MirrorMove.None && active && alive ? 1f : 0.3f;
            p.hover += Mathf.Sin(t * 1.3f) * 0.18f * idleW * (1f - p.slump);
            p.tilt += Mathf.Sin(t * 0.7f) * 2f * idleW;
            float instability = Mathf.Max(p.shake, p2 ? 0.25f : 0f) * (alive ? 1f : 0f);

            // ---- body
            if (body)
            {
                body.localPosition = new Vector3(0f, p.hover, 0f) + Random.insideUnitSphere * 0.04f * instability;
                body.localRotation = Quaternion.Euler(0f, p.bodyYaw, 0f) * Quaternion.Euler(p.tilt + p.slump * 6f, 0f, Mathf.Sin(t * 0.9f) * 1.5f * idleW);
            }

            // ---- glass
            _glow = Mathf.MoveTowards(_glow, active ? p.glow : 0.05f, Time.deltaTime * 4f);
            if (glass)
            {
                glass.GetPropertyBlock(_mpb);
                if (_phaseApplied != (p2 ? 2 : 1) && glassShattered && glassIntact)
                {
                    _phaseApplied = p2 ? 2 : 1;
                    _mpb.SetTexture(BaseMap, p2 ? glassShattered : glassIntact);
                    _mpb.SetTexture("_EmissionMap", p2 ? glassShattered : glassIntact);
                }
                float pulse = 0.85f + Mathf.Sin(t * (p2 ? 6f : 2.2f)) * 0.15f;
                Color e = Color.Lerp(new Color(0.35f, 0.18f, 0.8f), new Color(0.85f, 0.65f, 1f), Mathf.Clamp01(_glow - 1f));
                _mpb.SetColor(Emis, e * (0.35f + _glow * 1.6f) * pulse * (alive ? 1f : 0.1f));
                glass.SetPropertyBlock(_mpb);
            }
            if (glassLight)
            {
                glassLight.intensity = alive ? (2f + _glow * 7f) * (0.85f + Mathf.PerlinNoise(t * 3f, 0f) * 0.3f) : 0f;
                glassLight.color = Color.Lerp(MirrorFx.Void, MirrorFx.Pale, Mathf.Clamp01(_glow - 1.2f));
            }
            if (portal)
            {
                float s = Mathf.Max(0f, p.portal) * (alive ? 1f : 0f);
                portal.gameObject.SetActive(s > 0.01f);
                _spin += Time.deltaTime * (240f + s * 300f);
                portal.localRotation = Quaternion.Euler(0f, 0f, -_spin);
                portal.localScale = Vector3.one * (s * 3.6f);
            }

            // ---- arms
            Quaternion yawRot = transform.rotation * Quaternion.Euler(0f, p.bodyYaw, 0f);
            SolveArm(left, -1f, p.handL, p.palmL, p.curlL, p, yawRot);
            SolveArm(right, 1f, p.handR, p.palmR, p.curlR, p, yawRot);

            // ---- orbiting shards
            if (_orbit != null)
            {
                int want = !alive ? 0 : !active ? 6 : p2 ? orbitCountP2 : orbitCount;
                Vector3 c = body ? body.position : transform.position;
                for (int i = 0; i < _orbit.Length; i++)
                {
                    var o = _orbit[i];
                    if (!o) continue;
                    bool on = i < want;
                    if (o.gameObject.activeSelf != on) o.gameObject.SetActive(on);
                    if (!on) continue;
                    var sd = _orbitSeed[i];
                    float speed = sd.z * (p2 ? 1.8f : 1f) * (1f + p.shake);
                    float ang = t * speed + i * 2.399f;
                    float r = sd.x * (p2 ? 1.15f : 1f) + Mathf.Sin(t * 0.8f + i) * 0.4f;
                    Vector3 pos = c + new Vector3(Mathf.Cos(ang) * r, sd.y - 1f + Mathf.Sin(t * 1.1f + i * 1.7f) * 0.6f, Mathf.Sin(ang) * r);
                    pos += Random.insideUnitSphere * 0.12f * instability;
                    o.position = pos;
                    o.rotation = Quaternion.Euler(t * 40f * sd.z + i * 31f, t * 25f + i * 47f, i * 13f);
                }
            }

            // ---- smoke from the cracks
            if (_smoke)
            {
                var em = _smoke.emission;
                em.rateOverTime = !alive ? 0f : !active ? 2f : p2 ? 26f : 8f + p.shake * 10f;
            }

            // ---- sounds: the glass hums, louder in phase 2
            _hum -= Time.deltaTime;
            if (alive && active && _hum <= 0f)
            {
                _hum = p2 ? 2.6f : 4.2f;
                Sfx.Play(SfxId.MirrorHum, (body ? body.position : transform.position) + Vector3.up * 4f, p2 ? 0.7f : 0.45f, Random.Range(0.55f, 0.7f));
            }
        }

        void SolveArm(Arm a, float side, Vector3 handLocal, Vector3 palm, float curl, MirrorPose p, Quaternion yawRot)
        {
            if (a.upper == null || a.fore == null || a.hand == null) return;
            Vector3 shoulderLocal = new Vector3(Mathf.Abs(a.shoulder.x) * side, a.shoulder.y, a.shoulder.z);
            // the shoulder rides the frame (tilt included), the hand goes where the pose says
            Vector3 S = body ? body.TransformPoint(shoulderLocal) : BodyToWorld(shoulderLocal, p);
            Vector3 H = BodyToWorld(handLocal, p);
            float la = a.upperLength, lb = a.foreLength;
            Vector3 d = H - S;
            float dist = Mathf.Clamp(d.magnitude, 0.3f, la + lb - 0.02f);
            Vector3 u = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.down;
            H = S + u * dist;
            // elbows: outward, up and slightly back (a spider's knees)
            Vector3 right = yawRot * Vector3.right, fwd = yawRot * Vector3.forward;
            Vector3 pole = right * side * 1.0f + Vector3.up * 1.3f - fwd * 0.35f;
            Vector3 n = pole - u * Vector3.Dot(pole, u);
            if (n.sqrMagnitude < 1e-5f) n = Vector3.up;
            n.Normalize();
            float x = (la * la - lb * lb + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, la * la - x * x));
            Vector3 E = S + u * x + n * h;

            a.upper.position = S;
            a.upper.rotation = Quaternion.LookRotation(E - S, n);
            a.fore.position = E;
            a.fore.rotation = Quaternion.LookRotation(H - E, n);
            a.hand.position = H;
            a.hand.rotation = yawRot * Quaternion.Euler(palm);

            if (a.fingers != null)
                for (int i = 0; i < a.fingers.Length; i++)
                {
                    var f = a.fingers[i];
                    if (!f) continue;
                    bool thumb = i == a.fingers.Length - 1;
                    float c = Mathf.Clamp01(curl) * (thumb ? 0.7f : 1f);
                    f.localRotation = a.fingerRest[i] * Quaternion.Euler(c * 65f, 0f, 0f);
                    if (f.childCount > 0) f.GetChild(0).localRotation = a.clawRest[i] * Quaternion.Euler(c * 80f, 0f, 0f);
                }

            // dark fire wake in phase 2 (only while the hand moves), dust when it drags on the stone
            Vector3 vel = (H - a.lastHand) / Mathf.Max(Time.deltaTime, 0.001f);
            a.lastHand = H;
            if (a.fire)
            {
                var em = a.fire.emission;
                em.rateOverDistance = ai.Phase >= 2 && ai.IsAlive ? 7f : 0f;
                em.rateOverTime = ai.Phase >= 2 && ai.IsAlive ? 6f : 0f;
            }
            float ground = transform.position.y;
            if (ai.IsAlive && ai.FightActive && H.y - ground < 1.2f && vel.magnitude > 2.5f)
            {
                a.dragTimer -= Time.deltaTime;
                if (a.dragTimer <= 0f)
                {
                    a.dragTimer = 0.16f;
                    Fx.SpawnScaled(FxId.Dust, new Vector3(H.x, ground + 0.15f, H.z), 1.6f);
                    if (Random.value < 0.35f) Sfx.Play(SfxId.StepGravel, H, 0.9f, Random.Range(0.45f, 0.6f));
                }
            }
        }
    }
}
