using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Enemy projectile of the Glass: a straight flying splinter (Shard Wraith) or a slow homing
    /// phantom (the Mirror's "Reflection of Malice", which curses whoever it touches).
    /// Every client flies its own copy; only the server's copy deals damage.
    /// </summary>
    public class GlassShard : MonoBehaviour
    {
        public enum Kind : byte { Splinter, Phantom }

        Kind _kind;
        Vector3 _vel;
        EnemyBase _owner;
        bool _server;
        float _damage, _poise, _life, _turn;
        Transform _homing;
        byte _curse;
        float _curseTime;

        public static void Fire(Kind kind, Vector3 pos, Vector3 vel, EnemyBase owner, bool server, float damage, float poise,
                                Transform homing = null, byte curse = 0, float curseTime = 0f)
        {
            var go = new GameObject(kind == Kind.Phantom ? "phantom" : "glass_shard");
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(vel.sqrMagnitude > 0.001f ? vel : Vector3.forward));
            var s = go.AddComponent<GlassShard>();
            s._kind = kind; s._vel = vel; s._owner = owner; s._server = server; s._damage = damage; s._poise = poise;
            s._homing = homing; s._curse = curse; s._curseTime = curseTime;
            s._turn = kind == Kind.Phantom ? 2.2f : 0f;
            s.Build();
        }

        void Build()
        {
            var fx = MirrorFx.Instance;
            var add = Fx.Instance ? Fx.Instance.additiveMaterial : null;
            if (_kind == Kind.Splinter && fx && fx.shardMesh)
            {
                var vis = new GameObject("mesh"); vis.transform.SetParent(transform, false);
                vis.AddComponent<MeshFilter>().sharedMesh = fx.shardMesh;
                var mr = vis.AddComponent<MeshRenderer>(); mr.sharedMaterial = fx.shardMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                vis.transform.localScale = Vector3.one * 0.9f;
            }
            var tr = gameObject.AddComponent<TrailRenderer>();
            tr.time = _kind == Kind.Phantom ? 0.5f : 0.22f;
            tr.widthMultiplier = _kind == Kind.Phantom ? 0.7f : 0.2f;
            tr.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0));
            tr.sharedMaterial = add; tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Color c = _kind == Kind.Phantom ? (_curse == 1 ? MirrorFx.Void : MirrorFx.Pale) : MirrorFx.Pale;
            tr.colorGradient = new Gradient { colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(c, 1) }, alphaKeys = new[] { new GradientAlphaKey(0.85f, 0), new GradientAlphaKey(0f, 1) } };
            if (_kind == Kind.Phantom && add)
            {
                // a pale ghost: a cluster of soft sprites with a face-like glow
                var g = new GameObject("ghost"); g.transform.SetParent(transform, false);
                var ps = g.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var m = ps.main; m.loop = true; m.startLifetime = 0.45f; m.startSpeed = 0f; m.simulationSpace = ParticleSystemSimulationSpace.World;
                m.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f); m.startColor = new ParticleSystem.MinMaxGradient(Color.white, c); m.maxParticles = 100;
                var em = ps.emission; em.rateOverTime = 60f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.25f;
                var col = ps.colorOverLifetime; col.enabled = true;
                var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(c, 1) }, new[] { new GradientAlphaKey(0.7f, 0), new GradientAlphaKey(0f, 1) });
                col.color = gr;
                var r = g.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = MirrorFx.Instance && MirrorFx.Instance.phantomMaterial ? MirrorFx.Instance.phantomMaterial : add;
                ps.Play();
            }
            var l = gameObject.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.range = _kind == Kind.Phantom ? 5f : 2.5f; l.intensity = _kind == Kind.Phantom ? 3f : 1.4f; l.shadows = LightShadows.None;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _life += dt;
            if (_life > (_kind == Kind.Phantom ? 5f : 3f)) { End(transform.position, false); return; }
            if (_homing && _kind == Kind.Phantom)
            {
                Vector3 want = (_homing.position + Vector3.up * 1.1f - transform.position).normalized * _vel.magnitude;
                _vel = Vector3.RotateTowards(_vel, want, _turn * dt, 0f);
            }
            Vector3 step = _vel * dt;
            float len = step.magnitude;
            if (len > 0.0001f)
            {
                float radius = _kind == Kind.Phantom ? 0.45f : 0.18f;
                if (Physics.SphereCast(transform.position, radius, step / len, out var h, len, ~0, QueryTriggerInteraction.Ignore))
                {
                    var pc = h.collider.GetComponentInParent<PlayerController>();
                    if (pc != null)
                    {
                        if (_server && _owner && !pc.IsDead && !pc.IsInvulnerable)
                        {
                            _owner.DeliverProjectileHit(pc, _damage, _poise, HitFlags.None);
                            if (_curse != 0) pc.MirrorCurseRpc(_curse, _curseTime);
                        }
                        if (pc.IsInvulnerable && _kind == Kind.Splinter) { transform.position += step; return; }   // rolled through
                        End(h.point, true);
                        if (_curse != 0) MirrorFx.CurseFlash(pc.transform.position, _curse == 1);
                        return;
                    }
                    if (h.collider.GetComponentInParent<EnemyBase>() == null) { End(h.point, true); return; }
                }
            }
            transform.position += step;
            if (step.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(step);
        }

        void End(Vector3 at, bool impact)
        {
            if (impact)
            {
                MirrorFx.ShardBurst(at, _kind == Kind.Phantom ? 4 : 6, 3.5f, 0.5f);
                Sfx.Play(SfxId.ArrowImpact, at, 0.5f, 1.6f);
            }
            var tr = GetComponent<TrailRenderer>();
            if (tr) { tr.transform.SetParent(null, true); tr.emitting = false; }
            foreach (Transform c in transform) c.gameObject.SetActive(false);
            enabled = false;
            Destroy(gameObject, 0.6f);
        }
    }
}
