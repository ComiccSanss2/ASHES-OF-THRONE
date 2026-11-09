using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// The bard's projectiles (local objects, like arrows): every client flies the same bolt from the
    /// same origin/velocity; only the caster's copy is authoritative and asks the server for damage.
    ///  - Arcane: a pale-blue note of light that bends gently toward its target (damage).
    ///  - Dirge:  a violet orb that seeks its victim and curses it (no damage).
    /// Swept sphere each frame; passes through players; bursts on walls.
    /// </summary>
    public class MagicBolt : MonoBehaviour
    {
        public enum Kind : byte { Arcane, Dirge }

        Kind _kind;
        Vector3 _vel;
        Transform _target;
        PlayerController _caster;
        bool _authoritative;
        byte _tier;
        float _damage, _poise, _knockback, _life, _maxLife = 3.2f, _turn;
        float _curseDuration;
        Light _light;
        ParticleSystem _core;
        TrailRenderer _trail;
        readonly RaycastHit[] _hits = new RaycastHit[12];

        public static MagicBolt Fire(Kind kind, Vector3 from, Vector3 velocity, Transform target, PlayerController caster, bool authoritative,
                                     float damage, float poise, float knockback, byte tier, float curseDuration = 0f)
        {
            var go = new GameObject(kind == Kind.Arcane ? "ArcaneBolt" : "DirgeOrb");
            go.transform.position = from;
            var b = go.AddComponent<MagicBolt>();
            b._kind = kind; b._vel = velocity; b._target = target; b._caster = caster; b._authoritative = authoritative;
            b._damage = damage; b._poise = poise; b._knockback = knockback; b._tier = tier; b._curseDuration = curseDuration;
            b._turn = kind == Kind.Dirge ? 7f : 2.2f;
            b.Build();
            return b;
        }

        Color Col => _kind == Kind.Dirge ? SpellFx.Curse : ArcaneColor(_tier);

        /// <summary>The colour of the notes each mandolin plays: cold arcane blue, the Pyre's sun-gold, the wyrm's fire.</summary>
        public static Color ArcaneColor(byte tier) => tier == Inventory.Radiant ? new Color(1f, 0.95f, 0.8f) : tier == Inventory.Dragon ? new Color(1f, 0.3f, 0.08f) : tier == Inventory.Relic ? new Color(1f, 0.8f, 0.42f) : SpellFx.Arcane;

        void Build()
        {
            var mat = SpellFx.Instance && SpellFx.Instance.glowMaterial ? SpellFx.Instance.glowMaterial : Fx.Instance ? Fx.Instance.additiveMaterial : null;
            float size = _kind == Kind.Dirge ? 0.55f : 0.42f;
            // core: a few big soft sprites that shimmer
            var go = new GameObject("core"); go.transform.SetParent(transform, false);
            _core = go.AddComponent<ParticleSystem>();
            _core.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _core.main;
            main.loop = true; main.startLifetime = 0.25f; main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size);
            main.startColor = Color.Lerp(Col, Color.white, 0.35f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 120;
            var em = _core.emission; em.rateOverTime = 70f;
            var sh = _core.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.06f;
            var col = _core.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Col, 1) }, new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0f, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _core.Play();
            // trail
            _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.time = _kind == Kind.Dirge ? 0.35f : 0.22f;
            _trail.widthMultiplier = size * 0.55f;
            _trail.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0));
            _trail.sharedMaterial = mat;
            _trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.Lerp(Col, Color.white, 0.3f), 0), new GradientColorKey(Col, 1) },
                alphaKeys = new[] { new GradientAlphaKey(0.8f, 0), new GradientAlphaKey(0f, 1) }
            };
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (_kind == Kind.Arcane) BuildNotes(mat, size);
            if (_kind == Kind.Arcane && _tier == Inventory.Dragon && Fx.Instance)
            {
                Fx.CreateFlame(transform, Vector3.zero, 1.4f, Fx.Instance.additiveMaterial);   // the dragon's notes burn
                size *= 1.15f;
            }
            if (_kind == Kind.Arcane && _tier == Inventory.Radiant) SpellFx.SparkleTrail(transform);   // the Seraph's notes glitter
            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point; _light.color = Col; _light.range = 5f; _light.intensity = 3f; _light.shadows = LightShadows.None;
        }

        /// <summary>The bard plays his bolts: a big glowing note at the head and small notes left fluttering behind.</summary>
        void BuildNotes(Material glow, float size)
        {
            var noteMat = SpellFx.Instance && SpellFx.Instance.noteMaterial ? SpellFx.Instance.noteMaterial : glow;
            // the head: one big note, always facing the camera
            var hg = new GameObject("note_head"); hg.transform.SetParent(transform, false);
            var head = hg.AddComponent<ParticleSystem>();
            head.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = head.main; m.loop = true; m.startLifetime = 0.1f; m.startSpeed = 0f; m.startSize = size * 2.9f;   // big and readable from across the arena
            m.startColor = Color.Lerp(Col, Color.white, 0.45f); m.simulationSpace = ParticleSystemSimulationSpace.Local; m.maxParticles = 8;
            m.startRotation = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            var e = head.emission; e.rateOverTime = 40f;
            var sh = head.shape; sh.enabled = false;
            var hr = hg.GetComponent<ParticleSystemRenderer>(); hr.sharedMaterial = noteMat; hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            head.Play();
            // the wake: little notes that tumble and fade
            var tg = new GameObject("note_wake"); tg.transform.SetParent(transform, false);
            var wake = tg.AddComponent<ParticleSystem>();
            wake.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var wm = wake.main; wm.loop = true; wm.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
            wm.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.7f); wm.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size * 1.3f);
            wm.startRotation = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f); wm.gravityModifier = -0.12f;
            wm.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(Col, Color.white, 0.3f), Col);
            wm.simulationSpace = ParticleSystemSimulationSpace.World; wm.maxParticles = 60;
            var we = wake.emission; we.rateOverTime = 0f; we.rateOverDistance = 2.6f;
            var ws = wake.shape; ws.shapeType = ParticleSystemShapeType.Sphere; ws.radius = 0.12f;
            var wc = wake.colorOverLifetime; wc.enabled = true;
            var wg = new Gradient();
            wg.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0.95f, 0), new GradientAlphaKey(0f, 1) });
            wc.color = wg;
            var rol = wake.rotationOverLifetime; rol.enabled = true; rol.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            var wr = tg.GetComponent<ParticleSystemRenderer>(); wr.sharedMaterial = noteMat; wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wake.Play();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _life += dt;
            if (_life > _maxLife) { Burst(transform.position, false); return; }
            // gentle homing
            if (_target != null)
            {
                var lot = _target.GetComponentInParent<LockOnTarget>();
                Vector3 aim = lot ? lot.Point : _target.position + Vector3.up;
                Vector3 want = (aim - transform.position).normalized * _vel.magnitude;
                if (Vector3.Angle(_vel, want) < 75f) _vel = Vector3.RotateTowards(_vel, want, _turn * dt, 0f);
            }
            Vector3 step = _vel * dt;
            float len = step.magnitude;
            if (len > 1e-5f)
            {
                int n = Physics.SphereCastNonAlloc(transform.position, 0.18f, step / len, _hits, len, ~(1 << 2), QueryTriggerInteraction.Collide);
                System.Array.Sort(_hits, 0, n, ByDistance.Instance);
                for (int i = 0; i < n; i++)
                {
                    var h = _hits[i];
                    if (h.collider == null) continue;
                    var pl = h.collider.GetComponentInParent<PlayerController>();
                    if (pl != null)
                    {
                        if (_kind != Kind.Arcane || pl == _caster || !PlayerController.CanPvp(_caster, pl)) continue;
                        if (_authoritative)
                            pl.ReceiveHitRpc(_damage * PlayerController.PvpDamageMul, _poise, h.point - (step / len) * 3f, (byte)HitFlags.None, _caster.NetworkObject);
                        Burst(h.distance <= 0f ? transform.position : h.point, true);
                        return;
                    }
                    var enemy = h.collider.GetComponentInParent<EnemyBase>();
                    Vector3 point = h.distance <= 0f ? transform.position : h.point;
                    if (enemy != null)
                    {
                        if (!enemy.IsAlive) continue;
                        HitEnemy(enemy, point, step / len);
                        return;
                    }
                    if (h.collider.isTrigger) continue;
                    Burst(point, false);
                    return;
                }
            }
            transform.position += step;
            if (_light) _light.intensity = 3f + Mathf.Sin(_life * 40f) * 0.6f;
        }

        void HitEnemy(EnemyBase enemy, Vector3 point, Vector3 dir)
        {
            if (_authoritative && _caster != null)
            {
                Vector3 from = point - MathUtil.Flat(dir).normalized * 3f;
                if (_kind == Kind.Arcane)
                {
                    enemy.LocalHitReact(point, false, from, dir);
                    enemy.TakeRangedHitRpc(_damage, _poise, point, from, _knockback);
                PlayerUltimate.LocalDealt(_damage);
                }
                else enemy.CurseRpc(_curseDuration);
            }
            if (_kind == Kind.Dirge)
            {
                var cc = enemy.GetComponent<CharacterController>();
                SpellFx.CurseHit(enemy.transform.position, cc ? cc.height : 2f);
                Sfx.Play(SfxId.SpellCurse, point, 1f, 0.8f);
            }
            Burst(point, true);
        }

        void Burst(Vector3 point, bool hit)
        {
            if (_kind == Kind.Arcane)
            {
                Fx.Spawn(FxId.HitSparks, point);
                Sfx.Play(SfxId.SpellImpact, point, hit ? 0.9f : 0.6f, Random.Range(1.1f, 1.3f));
            }
            // let the trail and the last particles fade out
            foreach (var ps in GetComponentsInChildren<ParticleSystem>()) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            var nh = transform.Find("note_head"); if (nh) nh.gameObject.SetActive(false);
            if (_kind == Kind.Arcane) SpellFx.NoteBurst(point, Col);
            if (_light) _light.enabled = false;
            if (_trail) _trail.emitting = false;
            enabled = false;
            Destroy(gameObject, 0.5f);
            // a small flash at the impact
            var lg = new GameObject("bolt_flash"); lg.transform.position = point;
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = Col; l.range = 4f; l.intensity = 4f; l.shadows = LightShadows.None;
            lg.AddComponent<LightFade>().Begin(0.25f);
            Destroy(lg, 0.4f);
        }

        class ByDistance : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly ByDistance Instance = new ByDistance();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
