using UnityEngine;

namespace DarkFantasy
{
    public enum FxId { HitSparks, Blood, BlockSparks, ParryFlash, Dust, Embers, ShockDust, SlashImpact, SlashImpactHeavy, GlintParry, GlintDanger, Splash, Ripple, Fireball }

    /// <summary>
    /// Effetti particellari generati da codice (sparks, sangue scuro, polvere, braci).
    /// I materiali sono assegnati dal builder. Colori volutamente desaturati: niente neon.
    /// </summary>
    public class Fx : MonoBehaviour
    {
        public static Fx Instance { get; private set; }
        public Material additiveMaterial;   // scintille, braci
        public Material alphaMaterial;      // polvere, sangue
        public Material slashMaterial;      // crescent sword waves (boss)
        public Material rippleMaterial;     // water rings (soft ring texture, alpha blended)

        void Awake() => Instance = this;

        public static void Spawn(FxId id, Vector3 pos, Vector3 dir = default)
        {
            if (Instance == null) return;
            Instance.SpawnInternal(id, pos, dir == default ? Vector3.up : dir);
        }

        /// <summary>Effect with a size multiplier (fireballs, big splashes).</summary>
        public static void SpawnScaled(FxId id, Vector3 pos, float scale)
        {
            if (Instance == null) return;
            Instance.SpawnInternal(id, pos, Vector3.up, scale, null);
        }

        /// <summary>Effect that follows a transform (e.g. the glint on a raised blade).</summary>
        public static void SpawnAttached(FxId id, Transform parent, Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return;
            Instance.SpawnInternal(id, pos, Vector3.up, scale, parent);
        }

        ParticleSystem SpawnInternal(FxId id, Vector3 pos, Vector3 dir, float scale = 1f, Transform parent = null)
        {
            var go = new GameObject("fx_" + id);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(dir);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.duration = 0.5f;
            var em = ps.emission;
            em.rateOverTime = 0;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.05f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = additiveMaterial;

            int count = 12;
            switch (id)
            {
                case FxId.HitSparks:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
                    main.startColor = new Color(0.85f, 0.55f, 0.3f);
                    main.gravityModifier = 1.5f;
                    count = 14;
                    break;
                case FxId.Blood:
                    r.material = alphaMaterial;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
                    main.startColor = new Color(0.28f, 0.03f, 0.04f, 0.9f);
                    main.gravityModifier = 2f;
                    count = 16;
                    break;
                case FxId.BlockSparks:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
                    main.startColor = new Color(0.9f, 0.8f, 0.6f);
                    main.gravityModifier = 1f;
                    shape.angle = 60f;
                    count = 20;
                    break;
                case FxId.ParryFlash:
                    main.startLifetime = 0.18f;
                    main.startSpeed = 0f;
                    main.startSize = 1.4f;
                    main.startColor = new Color(0.9f, 0.85f, 0.7f, 0.8f);
                    count = 1;
                    var sol = ps.sizeOverLifetime; sol.enabled = true;
                    sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.3f, 1, 1.4f));
                    break;
                case FxId.Dust:
                case FxId.ShockDust:
                    r.material = alphaMaterial;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, id == FxId.ShockDust ? 4f : 1.5f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.4f, id == FxId.ShockDust ? 1.6f : 0.9f);
                    main.startColor = new Color(0.35f, 0.33f, 0.3f, 0.45f);
                    main.gravityModifier = -0.05f;
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = id == FxId.ShockDust ? 1.5f : 0.4f;
                    go.transform.rotation = Quaternion.Euler(-90, 0, 0);
                    count = id == FxId.ShockDust ? 40 : 10;
                    var col = ps.colorOverLifetime; col.enabled = true;
                    var g = new Gradient();
                    g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                              new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
                    col.color = g;
                    break;
                case FxId.Embers:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2.5f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
                    main.startColor = new Color(0.8f, 0.35f, 0.12f);
                    main.gravityModifier = -0.2f;
                    shape.angle = 50f;
                    count = 30;
                    break;
            }
            if (id == FxId.Splash)
            {
                // droplets thrown up and out of the water (dir.magnitude = strength)
                float k = Mathf.Clamp(dir.magnitude, 0.3f, 2f);
                r.material = alphaMaterial;
                go.transform.rotation = Quaternion.Euler(-90, 0, 0);
                shape.angle = 28f; shape.radius = 0.12f * scale;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f * k, 2.8f * k);
                main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
                main.startColor = new Color(0.62f, 0.66f, 0.66f, 0.75f);
                main.gravityModifier = 2.4f;
                ps.Emit(Mathf.RoundToInt(10 * k));
                Destroy(go, 1.5f);
                return ps;
            }
            if (id == FxId.Ripple)
            {
                // a flat ring spreading on the surface
                float k = Mathf.Clamp(dir.magnitude, 0.3f, 3f);
                r.material = rippleMaterial ? rippleMaterial : alphaMaterial;
                r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                shape.enabled = false;
                main.startLifetime = 0.9f;
                main.startSpeed = 0f;
                main.startSize = 0.9f * k;
                main.startColor = new Color(0.7f, 0.75f, 0.75f, 0.5f);
                var sol = ps.sizeOverLifetime; sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.15f, 1, 1f));
                var col = ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                          new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
                col.color = g;
                ps.Emit(1);
                main.startSize = 0.5f * k; main.startLifetime = 0.6f;
                ps.Emit(1);
                Destroy(go, 1.5f);
                return ps;
            }
            if (id == FxId.Fireball)
            {
                // fiery burst (dragon fireball impact): flame puffs + sparks
                go.transform.rotation = Quaternion.Euler(-90, 0, 0);
                shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.6f * scale;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2f * scale, 7f * scale);
                main.startSize = new ParticleSystem.MinMaxCurve(0.6f * scale, 1.6f * scale);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.2f, 0.9f), new Color(0.9f, 0.25f, 0.06f, 0.8f));
                main.gravityModifier = -0.3f;
                var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f));
                var col = ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.55f), 0), new GradientColorKey(new Color(0.5f, 0.12f, 0.04f), 1) },
                          new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
                col.color = g;
                ps.Emit(Mathf.RoundToInt(36 * Mathf.Clamp(scale, 0.5f, 2f)));
                Destroy(go, 2f);
                return ps;
            }
            if (id == FxId.SlashImpact || id == FxId.SlashImpactHeavy)
            {
                // a bright streak along the swing + a short white flash at the contact point
                bool heavy = id == FxId.SlashImpactHeavy;
                Vector3 d = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.right;
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.lengthScale = 1f; r.velocityScale = heavy ? 0.16f : 0.12f;
                var ep = new ParticleSystem.EmitParams { position = pos, applyShapeToPosition = false };
                for (int i = 0; i < 3; i++)
                {
                    ep.velocity = (d + Random.insideUnitSphere * 0.12f).normalized * (heavy ? 26f : 20f) * (1f - i * 0.18f);
                    ep.position = pos - d * (0.25f + i * 0.08f);
                    ep.startSize = (heavy ? 0.16f : 0.11f) * (1f - i * 0.25f);
                    ep.startLifetime = 0.09f + i * 0.02f;
                    ep.startColor = new Color(1f, 0.93f, 0.8f, 1f);
                    ps.Emit(ep, 1);
                }
                var col = ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f, 0.6f, 0.3f), 1) },
                          new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
                col.color = g;
                main.gravityModifier = 0f;
                // flash
                var fgo = new GameObject("fx_flash"); fgo.transform.position = pos;
                var fps = fgo.AddComponent<ParticleSystem>(); fps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var fm = fps.main; fm.playOnAwake = false; fm.loop = false; fm.startSpeed = 0f; fm.simulationSpace = ParticleSystemSimulationSpace.World;
                fm.startLifetime = 0.07f; fm.startSize = heavy ? 1.1f : 0.7f; fm.startColor = new Color(1f, 0.95f, 0.85f, 0.9f);
                var fe = fps.emission; fe.rateOverTime = 0;
                var fsol = fps.sizeOverLifetime; fsol.enabled = true; fsol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.4f, 1, 1.2f));
                fgo.GetComponent<ParticleSystemRenderer>().material = additiveMaterial;
                fps.Emit(1);
                Destroy(fgo, 1f);
                Destroy(go, 1.5f);
                return ps;
            }
            if (id == FxId.GlintParry || id == FxId.GlintDanger)
            {
                bool danger = id == FxId.GlintDanger;
                main.startLifetime = 0.32f;
                main.startSpeed = 0f;
                main.startSize = 0.9f * scale;
                main.startColor = danger ? new Color(1f, 0.18f, 0.08f, 1f) : new Color(1f, 0.85f, 0.45f, 1f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI);
                var sol = ps.sizeOverLifetime; sol.enabled = true;
                var curve = new AnimationCurve(new Keyframe(0, 0.1f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f));
                sol.size = new ParticleSystem.MinMaxCurve(1f, curve);
                shape.enabled = false;
                if (parent) { go.transform.SetParent(parent, true); main.simulationSpace = ParticleSystemSimulationSpace.Local; }
                // a second, larger soft halo
                ps.Emit(1);
                main.startSize = 1.8f * scale;
                main.startColor = danger ? new Color(0.9f, 0.1f, 0.05f, 0.5f) : new Color(0.9f, 0.7f, 0.3f, 0.45f);
                ps.Emit(1);
                Destroy(go, 1.5f);
                return ps;
            }
            ps.Emit(count);
            Destroy(go, 3f);
            return ps;
        }

        /// <summary>Crea un sistema di braci/fiamma persistente (torce, falò, bracieri).</summary>
        public static ParticleSystem CreateFlame(Transform parent, Vector3 localPos, float scale, Material mat)
        {
            var go = new GameObject("Flame");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.rotation = Quaternion.Euler(-90, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f * scale, 1.2f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f * scale, 0.3f * scale);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.45f, 0.15f, 0.8f), new Color(0.6f, 0.2f, 0.08f, 0.7f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            var em = ps.emission; em.rateOverTime = 28f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 8f; shape.radius = 0.06f * scale;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.1f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.8f, 0.5f), 0), new GradientColorKey(new Color(0.6f, 0.15f, 0.05f), 1) },
                      new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().material = mat;
            ps.Play();
            return ps;
        }
    }


}
