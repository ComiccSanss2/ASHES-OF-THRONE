using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Effects of the Glass: shards, black fire, the vortex, curse flashes. The component (on the
    /// scene's Systems) holds the meshes/materials the builder made; the helpers are static.
    /// </summary>
    public class MirrorFx : MonoBehaviour
    {
        public static MirrorFx Instance { get; private set; }
        [Tooltip("A thin splinter of glass (pointing +Z).")]
        public Mesh shardMesh;
        public Material shardMaterial;
        [Tooltip("Spectral silhouette / phantom (additive).")]
        public Material phantomMaterial;

        public static readonly Color Void = new Color(0.55f, 0.25f, 1f);
        public static readonly Color Pale = new Color(0.7f, 0.9f, 1f);
        public static readonly Color DarkFlame = new Color(0.45f, 0.08f, 0.7f);

        void Awake() => Instance = this;

        static Material Add => Fx.Instance ? Fx.Instance.additiveMaterial : null;
        static Material Alpha => Fx.Instance ? Fx.Instance.alphaMaterial : null;

        static ParticleSystem PS(string name, Vector3 pos, float life, Material mat)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 1f; main.maxParticles = 500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.material = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Destroy(go, life);
            return ps;
        }

        static Gradient Fade(Color a, Color b, float hold = 0.4f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, hold), new GradientAlphaKey(0, 1) });
            return g;
        }

        /// <summary>Glass splinters flung in every direction (mesh particles) + glints.</summary>
        public static void ShardBurst(Vector3 pos, int count, float speed, float size = 1f)
        {
            if (Instance && Instance.shardMesh && Instance.shardMaterial)
            {
                var ps = PS("shard_burst", pos, 3f, Instance.shardMaterial);
                var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = Instance.shardMesh;
                var m = ps.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
                m.startSize = new ParticleSystem.MinMaxCurve(0.4f * size, 1.1f * size);
                m.startRotation3D = true;
                m.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.startRotationY = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 6.28f);
                m.gravityModifier = 1.4f;
                var rot = ps.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
                rot.x = new ParticleSystem.MinMaxCurve(-6f, 6f); rot.y = new ParticleSystem.MinMaxCurve(-6f, 6f); rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f * size;
                var col = ps.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.bounce = 0.3f; col.dampen = 0.5f; col.collidesWith = Layers.EnvironmentMask;
                ps.Emit(count);
            }
            var glint = PS("shard_glint", pos, 1.5f, Add);
            var gm = glint.main;
            gm.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.6f); gm.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed * 1.4f);
            gm.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.18f * size); gm.startColor = new ParticleSystem.MinMaxGradient(Color.white, Pale);
            var gs = glint.shape; gs.shapeType = ParticleSystemShapeType.Sphere; gs.radius = 0.2f;
            var gc = glint.colorOverLifetime; gc.enabled = true; gc.color = Fade(Color.white, Void, 0.2f);
            glint.Emit(count * 2);
        }

        /// <summary>Black fire: dark tongues of flame with a violet core, burning for a while.</summary>
        public static void DarkFire(Vector3 pos, float radius, float duration)
        {
            if (Fx.Instance == null) return;
            var go = new GameObject("dark_fire");
            go.transform.SetPositionAndRotation(pos + Vector3.up * 0.05f, Quaternion.Euler(-90, 0, 0));
            foreach (bool core in new[] { false, true })
            {
                var child = new GameObject(core ? "core" : "smoke"); child.transform.SetParent(go.transform, false);
                var ps = child.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var m = ps.main;
                m.loop = true; m.duration = duration; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 300;
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, core ? 2.2f : 1.4f);
                m.startSize = new ParticleSystem.MinMaxCurve(core ? 0.35f : 0.6f, core ? 0.9f : 1.5f);
                m.startColor = core ? new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.35f, 1f, 0.9f), new Color(0.35f, 0.05f, 0.55f, 0.8f))
                                    : new ParticleSystem.MinMaxGradient(new Color(0.03f, 0.01f, 0.05f, 0.85f), new Color(0.1f, 0.02f, 0.12f, 0.7f));
                m.gravityModifier = -0.2f;
                var em = ps.emission; em.rateOverTime = core ? 40f : 30f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius * 0.85f;
                var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, Color.white, 0.3f);
                var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, core ? 0.2f : 1.6f));
                child.GetComponent<ParticleSystemRenderer>().material = core ? Add : Alpha;
                ps.Play();
            }
            var l = new GameObject("light"); l.transform.SetParent(go.transform, false); l.transform.localPosition = new Vector3(0, 0, 1f);
            var li = l.AddComponent<Light>(); li.type = LightType.Point; li.color = Void; li.intensity = 4f; li.range = radius * 3.5f; li.shadows = LightShadows.None;
            l.AddComponent<FlickerLight>();
            go.AddComponent<StopAfter>().Begin(go.GetComponentInChildren<ParticleSystem>(), li, duration);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) if (ps.gameObject.name == "smoke") { var s = go.AddComponent<StopAfter>(); s.Begin(ps, null, duration); }
            Destroy(go, duration + 2f);
        }

        /// <summary>A dark shock: black smoke ring rolling out + violet sparks.</summary>
        public static void DarkShock(Vector3 pos, float radius)
        {
            if (Fx.Instance == null) return;
            var smoke = PS("dark_shock", pos + Vector3.up * 0.2f, 3f, Alpha);
            var m = smoke.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f); m.startSpeed = new ParticleSystem.MinMaxCurve(radius * 1.2f, radius * 2f);
            m.startSize = new ParticleSystem.MinMaxCurve(1f, 2.4f); m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.02f, 0.01f, 0.04f, 0.9f), new Color(0.12f, 0.04f, 0.16f, 0.7f));
            var sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.4f; sh.rotation = new Vector3(-90, 0, 0);
            var lim = smoke.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 1.2f; lim.dampen = 0.15f;
            var col = smoke.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, Color.white, 0.3f);
            var sol = smoke.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.8f));
            smoke.Emit(Mathf.RoundToInt(28 + radius * 6));
            var sp = PS("dark_sparks", pos + Vector3.up * 0.3f, 2f, Add);
            var sm = sp.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1f); sm.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f); sm.gravityModifier = 0.8f;
            sm.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.2f); sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.6f, 1f), Void);
            var ss = sp.shape; ss.shapeType = ParticleSystemShapeType.Hemisphere; ss.radius = 0.5f; ss.rotation = new Vector3(-90, 0, 0);
            var sc = sp.colorOverLifetime; sc.enabled = true; sc.color = Fade(Color.white, Void, 0.2f);
            sp.Emit(50);
            BossFx.Flash(pos + Vector3.up, Void, 6f, radius * 2.5f, 0.4f);
        }

        /// <summary>Particles spiralling into a point: the Soul Vortex drinking the air.</summary>
        public static void VortexSwirl(Vector3 center, float radius, float duration)
        {
            if (Fx.Instance == null) return;
            var ps = PS("vortex", center, duration + 1.5f, Add);
            var m = ps.main;
            m.loop = true; m.duration = duration; m.startLifetime = 1.4f; m.startSpeed = 0f; m.maxParticles = 600;
            m.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.3f); m.startColor = new ParticleSystem.MinMaxGradient(Pale, Void);
            var em = ps.emission; em.rateOverTime = 160f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius; sh.radiusThickness = 0.15f; sh.rotation = new Vector3(-90, 0, 0);
            var orb = ps.velocityOverLifetime; orb.enabled = true; orb.space = ParticleSystemSimulationSpace.Local;
            orb.orbitalY = new ParticleSystem.MinMaxCurve(2.4f); orb.radial = new ParticleSystem.MinMaxCurve(-radius / 1.3f);
            orb.y = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, Void, 0.6f);
            ps.Play();
            var dust = PS("vortex_smoke", center, duration + 2f, Alpha);
            var dm = dust.main;
            dm.loop = true; dm.duration = duration; dm.startLifetime = 1.6f; dm.startSpeed = 0f; dm.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            dm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.04f, 0.01f, 0.06f, 0.6f), new Color(0.15f, 0.05f, 0.2f, 0.5f));
            var de = dust.emission; de.rateOverTime = 40f;
            var ds = dust.shape; ds.shapeType = ParticleSystemShapeType.Circle; ds.radius = radius * 1.1f; ds.rotation = new Vector3(-90, 0, 0);
            var dv = dust.velocityOverLifetime; dv.enabled = true; dv.space = ParticleSystemSimulationSpace.Local;
            dv.orbitalY = new ParticleSystem.MinMaxCurve(1.6f); dv.radial = new ParticleSystem.MinMaxCurve(-radius / 1.5f);
            var dc = dust.colorOverLifetime; dc.enabled = true; dc.color = Fade(Color.white, Color.white, 0.5f);
            dust.Play();
        }

        /// <summary>Violet-white flash around a cursed player (Reflection of Malice).</summary>
        public static void CurseFlash(Vector3 pos, bool inverted)
        {
            Color c = inverted ? Void : Pale;
            SpellFx.GroundRing(pos + Vector3.up * 0.05f, 0.3f, 1.8f, c, 0.6f);
            var ps = PS("mirror_curse", pos + Vector3.up, 2f, Add);
            var m = ps.main; m.startLifetime = 0.8f; m.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f); m.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
            m.startColor = new ParticleSystem.MinMaxGradient(Color.white, c);
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.5f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, c, 0.3f);
            ps.Emit(40);
        }

        /// <summary>A glass splinter arcing from <paramref name="from"/> to <paramref name="to"/> (shard hail), shattering on landing.</summary>
        public static void ArcShard(Vector3 from, Vector3 to, float flight, float size)
        {
            if (Instance == null || Instance.shardMesh == null) return;
            var go = new GameObject("arc_shard");
            go.transform.position = from;
            go.AddComponent<MeshFilter>().sharedMesh = Instance.shardMesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Instance.shardMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = Vector3.one * size;
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.25f; tr.widthMultiplier = 0.25f * size; tr.sharedMaterial = Add; tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.colorGradient = new Gradient { colorKeys = new[] { new GradientColorKey(Pale, 0), new GradientColorKey(Void, 1) }, alphaKeys = new[] { new GradientAlphaKey(0.8f, 0), new GradientAlphaKey(0f, 1) } };
            go.AddComponent<ArcFlight>().Begin(from, to, flight);
        }
    }

    /// <summary>Moves along a ballistic arc, points along its velocity, bursts on arrival.</summary>
    public class ArcFlight : MonoBehaviour
    {
        Vector3 _a, _b; float _t, _dur;
        public void Begin(Vector3 a, Vector3 b, float dur) { _a = a; _b = b; _dur = Mathf.Max(0.1f, dur); }
        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _dur);
            float h = Vector3.Distance(_a, _b) * 0.35f + 3f;
            Vector3 p = Vector3.Lerp(_a, _b, k) + Vector3.up * (4f * h * k * (1f - k));
            Vector3 d = p - transform.position;
            if (d.sqrMagnitude > 1e-5f) transform.rotation = Quaternion.LookRotation(d);
            transform.position = p;
            if (k >= 1f)
            {
                MirrorFx.ShardBurst(_b + Vector3.up * 0.2f, 8, 5f, 0.7f);
                Destroy(gameObject);
            }
        }
    }
}
