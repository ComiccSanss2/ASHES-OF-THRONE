using System.Collections.Generic;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>Smoke particles for the Smoker (alpha-blended soft puffs + additive embers).</summary>
    public static class SmokeFx
    {
        static Material Alpha => Fx.Instance ? Fx.Instance.alphaMaterial : null;
        static Material Add => Fx.Instance ? Fx.Instance.additiveMaterial : null;

        static Gradient FadeOut(float a0 = 0.85f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.75f, 0.75f, 0.75f), 1) },
                      new[] { new GradientAlphaKey(0f, 0), new GradientAlphaKey(a0, 0.12f), new GradientAlphaKey(a0 * 0.6f, 0.6f), new GradientAlphaKey(0f, 1) });
            return g;
        }

        static ParticleSystem NewPS(string name, Transform parent, Vector3 pos, bool alpha, int max, float life)
        {
            var go = new GameObject(name);
            if (parent) { go.transform.SetParent(parent, false); go.transform.localPosition = pos; }
            else go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.loop = parent != null; main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = max;
            var em = ps.emission; em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = alpha ? Alpha : Add; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (life > 0f) Object.Destroy(go, life);
            return ps;
        }

        /// <summary>A looping emitter attached to a transform (pipe bowl, mouth, a spirit's body).</summary>
        public static ParticleSystem Emitter(Transform parent, Vector3 local, float radius, Color c, float rate, float life, float size0, float size1)
        {
            var ps = NewPS("Smoke", parent, local, true, 300, 0f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(size0, size0 * 1.6f);
            main.startColor = c; main.gravityModifier = -0.04f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeOut();
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, size1 / Mathf.Max(0.01f, size0)));
            var rol = ps.rotationOverLifetime; rol.enabled = true; rol.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var no = ps.noise; no.enabled = true; no.strength = 0.25f; no.frequency = 0.6f;
            ps.Play();
            return ps;
        }

        /// <summary>A billowing cloud pushed along 'dir'.</summary>
        public static void Cloud(Vector3 pos, Vector3 dir, float radius, Color c, int count)
        {
            if (Alpha == null) return;
            var ps = NewPS("SmokeCloud", null, pos, true, count + 10, 5f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.5f, radius * 1.1f);
            main.startColor = c; main.gravityModifier = -0.05f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius * 0.4f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(dir.x * 1.6f); vel.y = new ParticleSystem.MinMaxCurve(dir.y * 1.6f + 0.2f); vel.z = new ParticleSystem.MinMaxCurve(dir.z * 1.6f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeOut(0.8f);
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.8f));
            var no = ps.noise; no.enabled = true; no.strength = 0.4f; no.frequency = 0.5f;
            ps.Emit(count);
        }

        public static void EmberSparks(Vector3 pos, Color c, int count)
        {
            if (Add == null) return;
            var ps = NewPS("Embers", null, pos, false, count + 4, 2f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = c; main.gravityModifier = -0.2f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.08f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeOut(1f);
            ps.Emit(count);
        }

        /// <summary>Choking Gust: a cone of black smoke rolling out of the mouth, embers tumbling in it.</summary>
        public static void Gust(Vector3 from, Vector3 dir, float range, float halfAngle, Color c, Color ember)
        {
            if (Alpha == null) return;
            var ps = NewPS("SmokeGust", null, from, true, 220, 4f);
            ps.transform.rotation = Quaternion.LookRotation(dir);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(range * 0.8f, range * 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startColor = Color.Lerp(c, Color.black, 0.45f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = halfAngle * 0.8f; sh.radius = 0.2f;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 1.2f; lim.dampen = 0.12f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeOut(0.9f);
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 3.2f));
            var no = ps.noise; no.enabled = true; no.strength = 0.6f; no.frequency = 0.4f;
            ps.Emit(170);
            var eps = NewPS("GustEmbers", null, from, false, 60, 3f);
            eps.transform.rotation = Quaternion.LookRotation(dir);
            var em = eps.main;
            em.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1f);
            em.startSpeed = new ParticleSystem.MinMaxCurve(range * 0.6f, range * 1.2f);
            em.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            em.startColor = ember;
            var esh = eps.shape; esh.shapeType = ParticleSystemShapeType.Cone; esh.angle = halfAngle * 0.8f; esh.radius = 0.15f;
            var elim = eps.limitVelocityOverLifetime; elim.enabled = true; elim.limit = 1f; elim.dampen = 0.1f;
            eps.Emit(50);
        }

        /// <summary>A ring of smoke rolling out along the ground (fist impacts, spirit bursts).</summary>
        public static void GroundBurst(Vector3 pos, float radius, Color c, int count)
        {
            if (Alpha == null) return;
            var ps = NewPS("SmokeBurst", null, pos + Vector3.up * 0.3f, true, count + 10, 4f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 1.2f, radius * 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.25f, radius * 0.55f);
            main.startColor = c; main.gravityModifier = -0.03f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.3f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.5f; lim.dampen = 0.15f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeOut(0.85f);
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.7f, 1, 2f));
            ps.Emit(count);
        }
    }

    // ======================================================================================
    /// <summary>The Smoker's puff: a homing ball of smoke with an ember core.</summary>
    public class SmokeBolt : MonoBehaviour
    {
        Vector3 _vel;
        Transform _target;
        PlayerController _caster;
        bool _authoritative;
        float _damage, _poise, _kb, _life;
        Color _smoke, _ember;
        Light _light;
        ParticleSystem _wake;
        readonly RaycastHit[] _hits = new RaycastHit[12];

        class ByDistance : IComparer<RaycastHit>
        {
            public static readonly ByDistance Instance = new ByDistance();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        public static SmokeBolt Fire(Vector3 from, Vector3 vel, Transform target, PlayerController caster, bool authoritative, float dmg, float poise, float kb, Color smoke, Color ember)
        {
            var go = new GameObject("SmokeBolt");
            go.transform.position = from;
            var b = go.AddComponent<SmokeBolt>();
            b._vel = vel; b._target = target; b._caster = caster; b._authoritative = authoritative;
            b._damage = dmg; b._poise = poise; b._kb = kb; b._smoke = smoke; b._ember = ember;
            b.Build();
            return b;
        }

        void Build()
        {
            _wake = SmokeFx.Emitter(transform, Vector3.zero, 0.12f, _smoke, 0f, 0.9f, 0.35f, 0.9f);
            var em = _wake.emission; em.rateOverDistance = 9f;
            var core = SmokeFx.Emitter(transform, Vector3.zero, 0.05f, _smoke, 30f, 0.25f, 0.5f, 0.6f);
            var cm = core.main; cm.simulationSpace = ParticleSystemSimulationSpace.Local;
            var tr = gameObject.AddComponent<TrailRenderer>();
            tr.time = 0.12f; tr.widthMultiplier = 0.08f; tr.sharedMaterial = Fx.Instance ? Fx.Instance.additiveMaterial : null;
            tr.colorGradient = new Gradient { colorKeys = new[] { new GradientColorKey(_ember, 0), new GradientColorKey(_ember, 1) }, alphaKeys = new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0f, 1) } };
            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point; _light.color = _ember; _light.range = 3.5f; _light.intensity = 2f; _light.shadows = LightShadows.None;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _life += dt;
            if (_life > 3f) { Burst(transform.position, false); return; }
            if (_target != null)
            {
                var lot = _target.GetComponentInParent<LockOnTarget>();
                Vector3 aim = lot ? lot.Point : _target.position + Vector3.up;
                Vector3 want = (aim - transform.position).normalized * _vel.magnitude;
                if (Vector3.Angle(_vel, want) < 80f) _vel = Vector3.RotateTowards(_vel, want, 2.6f * dt, 0f);
            }
            Vector3 step = _vel * dt;
            float len = step.magnitude;
            if (len > 1e-5f)
            {
                int n = Physics.SphereCastNonAlloc(transform.position, 0.22f, step / len, _hits, len, ~(1 << 2), QueryTriggerInteraction.Collide);
                System.Array.Sort(_hits, 0, n, ByDistance.Instance);
                for (int i = 0; i < n; i++)
                {
                    var h = _hits[i];
                    if (h.collider == null) continue;
                    Vector3 point = h.distance <= 0f ? transform.position : h.point;
                    var pl = h.collider.GetComponentInParent<PlayerController>();
                    if (pl != null)
                    {
                        if (pl == _caster || !PlayerController.CanPvp(_caster, pl)) continue;
                        if (_authoritative) pl.ReceiveHitRpc(_damage * PlayerController.PvpDamageMul, _poise, point - (step / len) * 3f, (byte)HitFlags.None, _caster.NetworkObject);
                        Burst(point, true);
                        return;
                    }
                    var enemy = h.collider.GetComponentInParent<EnemyBase>();
                    if (enemy != null)
                    {
                        if (!enemy.IsAlive) continue;
                        if (_authoritative && _caster != null)
                        {
                            Vector3 from = point - MathUtil.Flat(step / len).normalized * 3f;
                            enemy.LocalHitReact(point, false, from, step / len);
                            enemy.TakeRangedHitRpc(_damage, _poise, point, from, _kb);
                            PlayerUltimate.LocalDealt(_damage);
                        }
                        Burst(point, true);
                        return;
                    }
                    if (h.collider.isTrigger) continue;
                    Burst(point, false);
                    return;
                }
            }
            transform.position += step;
            if (_light) _light.intensity = 2f + Mathf.Sin(_life * 30f) * 0.5f;
        }

        void Burst(Vector3 point, bool hit)
        {
            SmokeFx.Cloud(point, -_vel.normalized * 0.3f, hit ? 0.7f : 0.45f, _smoke, hit ? 18 : 10);
            SmokeFx.EmberSparks(point, _ember, hit ? 10 : 4);
            Sfx.Play(SfxId.SpellImpact, point, hit ? 0.6f : 0.35f, Random.Range(0.55f, 0.7f));
            foreach (var ps in GetComponentsInChildren<ParticleSystem>()) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            if (_light) _light.enabled = false;
            var tr = GetComponent<TrailRenderer>(); if (tr) tr.emitting = false;
            enabled = false;
            Destroy(gameObject, 1.2f);
        }
    }

    // ======================================================================================
    /// <summary>
    /// A smoke spirit: a hunched wisp with ember eyes and long trailing arms. It drifts to the
    /// nearest foe around its smoker and claws at it; idle, it circles him. Owner copy deals damage
    /// (through SmokerCaster.SpiritHit); the copies on the other machines only look the same.
    /// </summary>
    public class SmokeSpirit : MonoBehaviour
    {
        static readonly List<SmokeSpirit> _all = new List<SmokeSpirit>();

        SmokerCaster _owner;
        bool _auth;
        float _born, _life, _nextStrike, _lungeUntil;
        Transform _target, _forced;
        Vector3 _vel;
        float _phase;
        ParticleSystem _body, _trail;
        Light _light;
        bool _dying;

        public static int CountFor(SmokerCaster s) { int n = 0; foreach (var x in _all) if (x && x._owner == s && !x._dying) n++; return n; }

        public static void Spawn(SmokerCaster owner, Vector3 from, Vector3 dir, int seed, bool authoritative)
        {
            var go = new GameObject("SmokeSpirit");
            go.transform.position = from;
            var s = go.AddComponent<SmokeSpirit>();
            s._owner = owner; s._auth = authoritative; s._born = Time.time; s._life = owner.spiritLife;
            s._phase = (seed % 97) * 0.37f;
            s._vel = dir * 7f + Vector3.up * 1.5f;
            s._nextStrike = Time.time + 0.6f;
            s.Build(owner.Smoke, owner.Ember);
            _all.Add(s);
        }

        /// <summary>Keeps only the newest 'max' spirits of a smoker.</summary>
        public static void Trim(SmokerCaster owner, int max)
        {
            var mine = _all.FindAll(x => x && x._owner == owner && !x._dying);
            mine.Sort((a, b) => a._born.CompareTo(b._born));
            for (int i = 0; i < mine.Count - max; i++) mine[i].Dissolve();
        }

        public static void DismissAll(SmokerCaster owner)
        {
            foreach (var x in _all.ToArray()) if (x && x._owner == owner) x.Dissolve();
        }

        public static void CommandAll(SmokerCaster owner, Transform target)
        {
            foreach (var x in _all) if (x && x._owner == owner && !x._dying) { x._forced = target; x._lungeUntil = Time.time + 0.6f; x._nextStrike = Time.time + 0.35f; }
        }

        void Build(Color smoke, Color ember)
        {
            _body = SmokeFx.Emitter(transform, new Vector3(0f, 0.2f, 0f), 0.35f, Color.Lerp(smoke, Color.black, 0.25f), 46f, 0.7f, 0.45f, 0.8f);
            _trail = SmokeFx.Emitter(transform, Vector3.zero, 0.15f, smoke, 0f, 1.3f, 0.3f, 0.9f);
            var em = _trail.emission; em.rateOverDistance = 6f;
            // ember eyes
            var eyes = new GameObject("Eyes"); eyes.transform.SetParent(transform, false); eyes.transform.localPosition = new Vector3(0f, 0.4f, 0.25f);
            var eps = eyes.AddComponent<ParticleSystem>();
            eps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = eps.main; m.loop = true; m.startLifetime = 0.12f; m.startSpeed = 0f; m.startSize = 0.11f; m.startColor = ember;
            m.simulationSpace = ParticleSystemSimulationSpace.Local; m.maxParticles = 10;
            var e = eps.emission; e.rateOverTime = 0f;
            e.SetBursts(new[] { new ParticleSystem.Burst(0f, 2, 2, 999, 0.1f) });
            var sh = eps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.18f, 0f, 0f); sh.radiusMode = ParticleSystemShapeMultiModeValue.BurstSpread;
            var r = eyes.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = Fx.Instance ? Fx.Instance.additiveMaterial : null;
            eps.Play();
            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point; _light.color = ember; _light.range = 3f; _light.intensity = 0.9f; _light.shadows = LightShadows.None;
        }

        Transform PickTarget()
        {
            if (_forced && IsAliveTarget(_forced)) return _forced;
            _forced = null;
            Vector3 home = _owner ? _owner.transform.position : transform.position;
            Transform best = null; float bestD = 16f;
            foreach (var e in EnemyBase.All)
            {
                if (e == null || !e.IsAlive) continue;
                float d = Vector3.Distance(e.transform.position, home);
                if (d < bestD) { bestD = d; best = e.transform; }
            }
            if (_owner && _owner.pc)
                foreach (var pl in PlayerController.All)
                {
                    if (pl == null || pl == _owner.pc || pl.IsDead || !PlayerController.CanPvp(_owner.pc, pl)) continue;
                    float d = Vector3.Distance(pl.transform.position, home);
                    if (d < bestD) { bestD = d; best = pl.transform; }
                }
            return best;
        }

        static bool IsAliveTarget(Transform t)
        {
            var e = t.GetComponent<EnemyBase>(); if (e) return e.IsAlive;
            var p = t.GetComponent<PlayerController>(); return p && !p.IsDead;
        }

        void Update()
        {
            if (_dying) return;
            if (_owner == null || (_owner.pc && _owner.pc.IsDead) || Time.time - _born > _life) { Dissolve(); return; }
            float dt = Time.deltaTime;
            if (_target == null || !IsAliveTarget(_target) || Time.frameCount % 15 == 0) _target = PickTarget();
            Vector3 want;
            float speed;
            if (_target)
            {
                Vector3 aim = _target.position + Vector3.up * 1.1f;
                Vector3 side = Quaternion.AngleAxis(_phase * 57f, Vector3.up) * Vector3.forward * 1.1f;
                want = aim + side - transform.position;
                speed = Time.time < _lungeUntil ? 14f : 6.5f;
                if (Vector3.Distance(transform.position, aim) < 1.9f && Time.time >= _nextStrike)
                {
                    bool lunge = Time.time < _lungeUntil;
                    _nextStrike = Time.time + 1.1f;
                    _vel += (aim - transform.position).normalized * 6f;
                    if (_auth && _owner) _owner.SpiritHit(_target, transform.position, lunge ? 1.8f : 1f);
                    SmokeFx.EmberSparks(aim, _owner.Ember, 6);
                    if (Random.value < 0.4f) Sfx.Play(SfxId.Swing, aim, 0.35f, Random.Range(1.6f, 1.9f));
                }
            }
            else
            {
                // circle the smoker's shoulders
                float a = Time.time * 1.3f + _phase;
                Vector3 c = _owner.transform.position + Vector3.up * 2.1f + new Vector3(Mathf.Cos(a), Mathf.Sin(a * 1.7f) * 0.15f, Mathf.Sin(a)) * 1.6f;
                want = c - transform.position;
                speed = 4f;
            }
            Vector3 v = want.sqrMagnitude > 0.01f ? want.normalized * Mathf.Min(speed, want.magnitude * 3f) : Vector3.zero;
            _vel = Vector3.Lerp(_vel, v, 1f - Mathf.Exp(-3.5f * dt));
            transform.position += (_vel + Vector3.up * Mathf.Sin(Time.time * 3f + _phase) * 0.25f) * dt;
            Vector3 f = MathUtil.Flat(_target ? _target.position - transform.position : _vel);
            if (f.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(f), 1f - Mathf.Exp(-8f * dt));
            float left = _life - (Time.time - _born);
            if (_light) _light.intensity = left < 2f ? left * 0.45f : 0.9f + Mathf.Sin(Time.time * 7f + _phase) * 0.2f;
        }

        public void Dissolve()
        {
            if (_dying) return;
            _dying = true;
            _all.Remove(this);
            if (_owner) SmokeFx.Cloud(transform.position, Vector3.up * 0.3f, 0.8f, _owner.Smoke, 16);
            foreach (var ps in GetComponentsInChildren<ParticleSystem>()) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            if (_light) _light.enabled = false;
            Destroy(gameObject, 1.6f);
        }

        void OnDestroy() => _all.Remove(this);
    }

    // ======================================================================================
    /// <summary>
    /// The Great Exhale: a colossus of smoke as tall as a tower rises behind the smoker, pounds the
    /// nearest foes with its fists (one blow every ~1.2 s, the target spot is marked) and ends with a
    /// two-handed crash. Owner copy deals the damage; the others are visual.
    /// </summary>
    public class SmokeColossus : MonoBehaviour
    {
        public const float Duration = 9.5f, RiseTime = 1.2f, BlowEvery = 1.25f, Windup = 0.55f, SlamAt = 8.2f;
        const float Reach = 15f, BlowRadius = 3.2f, SlamRadius = 7f;

        PlayerController _pc;
        bool _auth;
        float _t, _mul, _nextBlow;
        Color _smoke, _ember;
        Transform _torso, _head, _armL, _armR, _fistL, _fistR;
        Vector3 _blowAt;
        bool _blowLeft, _blowPending, _slammed;
        float _blowStart;
        readonly List<ParticleSystem> _parts = new List<ParticleSystem>();
        Light _eyeLight;

        public static void Spawn(Vector3 at, Vector3 fwd, PlayerController pc, bool authoritative, float mul, Color smoke, Color ember)
        {
            var go = new GameObject("SmokeColossus");
            go.transform.SetPositionAndRotation(at - MathUtil.Flat(fwd).normalized * 2.5f, Quaternion.LookRotation(MathUtil.Flat(fwd).normalized));
            var c = go.AddComponent<SmokeColossus>();
            c._pc = pc; c._auth = authoritative; c._mul = mul; c._smoke = smoke; c._ember = ember;
            c._nextBlow = RiseTime + 0.3f;
            c.Build();
        }

        Transform Part(string name, Transform parent, Vector3 local, float radius, float rate, float size, float dark)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = local;
            var ps = SmokeFx.Emitter(t, Vector3.zero, radius, Color.Lerp(_smoke, Color.black, dark), rate, 1.1f, size, size * 1.5f);
            var m = ps.main; m.maxParticles = 160;
            _parts.Add(ps);
            return t;
        }

        void Build()
        {
            var root = transform;
            Part("Column", root, new Vector3(0f, 1.2f, 0f), 1.6f, 40f, 1.4f, 0.3f);
            _torso = Part("Torso", root, new Vector3(0f, 5.2f, 0f), 2.2f, 70f, 2f, 0.35f);
            Part("Chest", _torso, new Vector3(0f, 1.6f, 0.2f), 1.8f, 50f, 1.8f, 0.25f);
            _head = Part("Head", _torso, new Vector3(0f, 3.6f, 0.6f), 1.05f, 40f, 1.3f, 0.4f);
            _armL = new GameObject("ShoulderL").transform; _armL.SetParent(_torso, false); _armL.localPosition = new Vector3(-2.6f, 2.2f, 0f);
            _armR = new GameObject("ShoulderR").transform; _armR.SetParent(_torso, false); _armR.localPosition = new Vector3(2.6f, 2.2f, 0f);
            foreach (var (arm, s) in new[] { (_armL, -1f), (_armR, 1f) })
            {
                Part("Upper", arm, new Vector3(s * 0.8f, -1.2f, 0.4f), 0.9f, 30f, 1.1f, 0.3f);
                Part("Fore", arm, new Vector3(s * 1.2f, -3f, 1.4f), 0.8f, 30f, 1f, 0.3f);
                var fist = Part("Fist", arm, new Vector3(s * 1.3f, -4.6f, 2.4f), 1.1f, 40f, 1.2f, 0.5f);
                if (s < 0) _fistL = fist; else _fistR = fist;
            }
            // two ember eyes in the head, and the glow of a pipe-coal heart in the chest
            var eyes = new GameObject("Eyes"); eyes.transform.SetParent(_head, false); eyes.transform.localPosition = new Vector3(0f, 0.15f, 0.9f);
            var eps = eyes.AddComponent<ParticleSystem>();
            eps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = eps.main; m.loop = true; m.startLifetime = 0.12f; m.startSpeed = 0f; m.startSize = 0.45f; m.startColor = _ember;
            m.simulationSpace = ParticleSystemSimulationSpace.Local; m.maxParticles = 10;
            var e = eps.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 2, 2, 999, 0.1f) });
            var sh = eps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.7f, 0f, 0f); sh.radiusMode = ParticleSystemShapeMultiModeValue.BurstSpread;
            eyes.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fx.Instance ? Fx.Instance.additiveMaterial : null;
            eps.Play();
            _eyeLight = eyes.AddComponent<Light>(); _eyeLight.type = LightType.Point; _eyeLight.color = _ember; _eyeLight.range = 9f; _eyeLight.intensity = 0f; _eyeLight.shadows = LightShadows.None;
            var heart = new GameObject("Heart"); heart.transform.SetParent(_torso, false); heart.transform.localPosition = new Vector3(0f, 1.2f, 1.2f);
            var hl = heart.AddComponent<Light>(); hl.type = LightType.Point; hl.color = _ember; hl.range = 12f; hl.intensity = 6f; hl.shadows = LightShadows.None;
            heart.AddComponent<FlickerLight>();
            _torso.localScale = Vector3.one * 0.1f;
            SmokeFx.GroundBurst(transform.position, 4f, _smoke, 60);
            Sfx.Play(SfxId.Roar, transform.position + Vector3.up * 6f, 0.9f, 0.5f);
            Sfx.Play(SfxId.MirrorVortex, transform.position + Vector3.up * 3f, 1.2f, 0.6f);
        }

        Transform PickTarget()
        {
            Transform best = null; float bestD = Reach;
            foreach (var en in EnemyBase.All)
            {
                if (en == null || !en.IsAlive) continue;
                float d = MathUtil.Flat(en.transform.position - transform.position).magnitude;
                if (d < bestD && Mathf.Abs(en.transform.position.y - transform.position.y) < 6f) { bestD = d; best = en.transform; }
            }
            if (_pc)
                foreach (var pl in PlayerController.All)
                {
                    if (pl == null || pl == _pc || pl.IsDead || !PlayerController.CanPvp(_pc, pl)) continue;
                    float d = MathUtil.Flat(pl.transform.position - transform.position).magnitude;
                    if (d < bestD) { bestD = d; best = pl.transform; }
                }
            return best;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _t += dt;
            // rise, then breathe; fade out at the end
            float grow = Mathf.SmoothStep(0.1f, 1f, Mathf.Clamp01(_t / RiseTime));
            float fade = Mathf.Clamp01((Duration - _t) / 0.8f);
            _torso.localScale = Vector3.one * grow * Mathf.Lerp(0.6f, 1f, fade);
            _torso.localPosition = new Vector3(0f, 5.2f * grow + Mathf.Sin(_t * 1.4f) * 0.25f, 0f);
            if (_eyeLight) _eyeLight.intensity = 8f * grow * fade;

            // the blows
            if (!_slammed && _t < SlamAt - 0.6f && _t >= _nextBlow && !_blowPending)
            {
                var tgt = PickTarget();
                _nextBlow = _t + BlowEvery;
                if (tgt)
                {
                    _blowAt = BossFx.Ground(tgt.position);
                    _blowLeft = !_blowLeft;
                    _blowPending = true; _blowStart = _t;
                    BossFx.GroundWarning(_blowAt, BlowRadius, Windup, _ember);
                    Vector3 f = MathUtil.Flat(_blowAt - transform.position);
                    if (f.sqrMagnitude > 0.1f) transform.rotation = Quaternion.LookRotation(f.normalized);
                }
            }
            Quaternion restL = Quaternion.Euler(Mathf.Sin(_t * 1.3f) * 6f, 0f, -8f), restR = Quaternion.Euler(Mathf.Sin(_t * 1.3f + 1f) * 6f, 0f, 8f);
            Quaternion wantL = restL, wantR = restR;
            if (_blowPending)
            {
                float u = (_t - _blowStart) / Windup;
                Quaternion up = Quaternion.Euler(-150f, 0f, 0f), down = Quaternion.Euler(30f, 0f, 0f);
                Quaternion pose = u < 1f ? Quaternion.Slerp(Quaternion.identity, up, Mathf.SmoothStep(0f, 1f, u)) : Quaternion.Slerp(up, down, Mathf.Clamp01((u - 1f) * 5f));
                if (_blowLeft) wantL = pose; else wantR = pose;
                // the colossus leans toward the spot (its reach), the fist lands on the mark
                float lean = Mathf.Clamp(MathUtil.Flat(_blowAt - transform.position).magnitude - 6f, 0f, 9f);
                _torso.localPosition += new Vector3(0f, 0f, lean * Mathf.Clamp01(u));
                if (u >= 1.2f)
                {
                    _blowPending = false;
                    Impact(_blowAt, BlowRadius, 45f, 75f, false);
                }
            }
            if (!_slammed && _t >= SlamAt - 0.7f)
            {
                float u = (_t - (SlamAt - 0.7f)) / 0.7f;
                Quaternion both = u < 0.75f ? Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-160f, 0f, 0f), u / 0.75f) : Quaternion.Euler(40f, 0f, 0f);
                wantL = both; wantR = both;
                if (_t >= SlamAt)
                {
                    _slammed = true;
                    Impact(BossFx.Ground(transform.position + transform.forward * 6f), SlamRadius, 85f, 140f, true);
                }
            }
            _armL.localRotation = Quaternion.Slerp(_armL.localRotation, wantL, 1f - Mathf.Exp(-14f * dt));
            _armR.localRotation = Quaternion.Slerp(_armR.localRotation, wantR, 1f - Mathf.Exp(-14f * dt));

            if (_t >= Duration)
            {
                foreach (var ps in _parts) if (ps) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                SmokeFx.Cloud(_torso.position, Vector3.up, 4f, _smoke, 50);
                enabled = false;
                Destroy(gameObject, 1.6f);
            }
        }

        void Impact(Vector3 at, float r, float dmg, float poise, bool big)
        {
            SmokeFx.GroundBurst(at, r, _smoke, big ? 90 : 45);
            SmokeFx.EmberSparks(at + Vector3.up * 0.5f, _ember, big ? 40 : 18);
            BossFx.RingWave(at, r + 1f, 16f, false);
            BossFx.Debris(at, r * 0.4f, big ? 20 : 10);
            Sfx.Play(SfxId.Slam, at, big ? 1.3f : 1f, big ? 0.6f : 0.8f);
            Sfx.Play(SfxId.Quake, at, big ? 1f : 0.6f, 0.9f);
            if (PlayerController.Local && Vector3.Distance(PlayerController.Local.transform.position, at) < 22f) SoulsCamera.Shake(big ? 0.14f : 0.07f, big ? 0.45f : 0.25f);
            if (!_auth || _pc == null) return;
            foreach (var e in EnemyBase.All)
            {
                if (e == null || !e.IsAlive) continue;
                Vector3 to = e.transform.position - at;
                if (MathUtil.Flat(to).magnitude > r + 0.5f || Mathf.Abs(to.y) > 3.5f) continue;
                Vector3 point = e.transform.position + Vector3.up * 1.1f;
                e.LocalHitReact(point, true, at, MathUtil.Flat(to).normalized + Vector3.up * 0.3f);
                e.TakeRangedHitRpc(dmg * _mul, poise, point, at, big ? 8f : 5f);
            }
            foreach (var pl in PlayerController.All)
            {
                if (pl == null || pl == _pc || pl.IsDead || !PlayerController.CanPvp(_pc, pl)) continue;
                Vector3 to = pl.transform.position - at;
                if (MathUtil.Flat(to).magnitude > r + 0.4f || Mathf.Abs(to.y) > 3.5f) continue;
                pl.ReceiveHitRpc(dmg * _mul * PlayerController.PvpDamageMul * 0.6f, poise, at, (byte)HitFlags.Heavy, _pc.NetworkObject);
            }
        }
    }
}
