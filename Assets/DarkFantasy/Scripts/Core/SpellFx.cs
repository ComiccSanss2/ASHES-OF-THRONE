using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// The bard's magic, seen by everyone: glowing musical notes, rings of light on the ground,
    /// auras on buffed allies and cursed enemies. All built from code (materials from the builder).
    /// Colours: heal = green, anthem = gold, curse = violet, crescendo / bolt = pale blue.
    /// </summary>
    public class SpellFx : MonoBehaviour
    {
        public static SpellFx Instance { get; private set; }
        [Tooltip("Additive, texture = a glowing quaver (musical note).")]
        public Material noteMaterial;
        [Tooltip("Additive, texture = a soft ring.")]
        public Material ringMaterial;
        [Tooltip("Additive, texture = soft round glow.")]
        public Material glowMaterial;

        public static readonly Color Heal = new Color(0.45f, 1f, 0.55f), Anthem = new Color(1f, 0.78f, 0.3f),
                                     Curse = new Color(0.68f, 0.32f, 1f), Arcane = new Color(0.5f, 0.82f, 1f);

        void Awake() => Instance = this;

        static Material Glow => Instance && Instance.glowMaterial ? Instance.glowMaterial : Fx.Instance ? Fx.Instance.additiveMaterial : null;
        static Material Note => Instance && Instance.noteMaterial ? Instance.noteMaterial : Glow;
        static Material Ring => Instance && Instance.ringMaterial ? Instance.ringMaterial : Glow;

        // ------------------------------------------------------------------ building blocks
        static ParticleSystem NewPS(string name, Transform parent, Vector3 pos, Material mat, bool loop, float duration = 1f)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = loop; main.duration = duration; main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission; em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static Gradient Fade(Color c, float peak = 1f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.Lerp(c, Color.white, 0.4f), 0f), new GradientColorKey(c, 0.4f), new GradientColorKey(c * 0.6f, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, 0.12f), new GradientAlphaKey(peak * 0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        /// <summary>Notes rising and drifting (burst, or looping when rate &gt; 0).</summary>
        static ParticleSystem Notes(Transform parent, Vector3 pos, Color c, int burst, float rate, float radius, float rise, float size, bool loop, float life = 1.6f)
        {
            var ps = NewPS("notes", parent, pos, Note, loop, loop ? 1f : 0.5f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(rise * 0.5f, rise);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            main.startColor = c;
            main.gravityModifier = -0.05f;
            main.maxParticles = 200;
            var em = ps.emission; em.rateOverTime = rate;
            if (burst > 0) em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burst) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius; sh.rotation = new Vector3(-90f, 0f, 0f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f); vel.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f); vel.y = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(c);
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.4f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0.7f)));
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.8f;
            ps.Play();
            return ps;
        }

        static ParticleSystem Sparks(Transform parent, Vector3 pos, Color c, int burst, float rate, float radius, float speed, float size, bool loop, float life = 0.9f)
        {
            var ps = NewPS("sparks", parent, pos, Glow, loop, loop ? 1f : 0.3f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.5f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startColor = c;
            main.maxParticles = 300;
            var em = ps.emission; em.rateOverTime = rate;
            if (burst > 0) em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burst) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(c);
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1f, 0f)));
            ps.Play();
            return ps;
        }

        public static readonly Color Radiant = new Color(1f, 0.9f, 0.6f);

        /// <summary>A spray of white-gold glitter (radiant weapons).</summary>
        public static void Sparkles(Vector3 pos, float scale)
        {
            Kill(Sparks(null, pos, Radiant, Mathf.RoundToInt(26 * scale), 0f, 0.15f * scale, 4.5f * scale, 0.14f * scale, false, 0.8f), 1.2f);
        }

        /// <summary>Glitter left behind by something moving (radiant notes).</summary>
        public static void SparkleTrail(Transform t)
        {
            var ps = Sparks(t, t.position, Radiant, 0, 0f, 0.12f, 0.6f, 0.1f, true, 0.7f);
            var em = ps.emission; em.rateOverDistance = 10f;
            var main = ps.main; main.gravityModifier = 0.2f;
        }

        /// <summary>A ring of light on the ground, growing from r0 to r1.</summary>
        public static void GroundRing(Vector3 pos, float r0, float r1, Color c, float life, float alpha = 0.9f)
        {
            if (Ring == null) return;
            var ps = NewPS("ring", null, pos + Vector3.up * 0.06f, Ring, false, 0.1f);
            var main = ps.main;
            main.startLifetime = life; main.startSpeed = 0f; main.startSize = r1 * 2f; main.startColor = new Color(c.r, c.g, c.b, alpha);
            var em = ps.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var sh = ps.shape; sh.enabled = false;
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, r0 / r1, 0f, 3f), new Keyframe(1f, 1f, 0f, 0f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ps.Play();
            Destroy(ps.gameObject, life + 0.2f);
        }

        static void Flash(Vector3 pos, Color c, float intensity, float range, float life)
        {
            var go = new GameObject("spell_light");
            go.transform.position = pos;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            go.AddComponent<LightFade>().Begin(life);
        }

        static void Kill(ParticleSystem ps, float after) { if (ps) Destroy(ps.gameObject, after); }

        // ------------------------------------------------------------------ spells
        /// <summary>Glow gathering at the staff's crystal while casting.</summary>
        public static void StaffCharge(Transform tip, Color c, float duration)
        {
            if (tip == null) return;
            var ps = Sparks(tip, tip.position, c, 0, 60f, 0.35f, -1.2f, 0.12f, true, 0.35f);
            var sh = ps.shape; sh.radius = 0.4f;
            Kill(ps, duration);
            var go = new GameObject("charge_light"); go.transform.SetParent(tip, false);
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = 3f; l.range = 4f; l.shadows = LightShadows.None;
            go.AddComponent<LightFade>().Begin(duration);
        }

        /// <summary>A bolt bursting: notes scattered in every direction.</summary>
        public static void NoteBurst(Vector3 pos, Color c)
        {
            Kill(Notes(null, pos, c, 11, 0f, 0.35f, 1.5f, 0.5f, false, 1.0f), 1.6f);
        }

        /// <summary>Mending Hymn, at the bard: green ring to the spell radius and a fountain of notes.</summary>
        public static void HealCast(Vector3 pos, float radius)
        {
            GroundRing(pos, 0.3f, radius, Heal, 0.9f);
            GroundRing(pos, 0.2f, radius * 0.6f, Heal, 1.2f, 0.6f);
            Kill(Notes(null, pos + Vector3.up * 0.4f, Heal, 34, 0f, 1.4f, 2.4f, 0.5f, false), 3f);
            Kill(Sparks(null, pos + Vector3.up * 1f, Heal, 40, 0f, 0.6f, 3f, 0.14f, false), 2f);
            Flash(pos + Vector3.up * 1.5f, Heal, 6f, 10f, 0.9f);
        }

        /// <summary>On every ally healed: a soft green column and notes swirling up the body.</summary>
        public static void HealBurst(Transform target)
        {
            if (target == null) return;
            Vector3 p = target.position;
            Kill(Notes(target, p + Vector3.up * 0.2f, Heal, 16, 0f, 0.5f, 1.8f, 0.36f, false, 1.4f), 2.5f);
            var col = Sparks(target, p + Vector3.up * 0.2f, Heal, 30, 0f, 0.45f, 0.4f, 0.16f, false, 1.2f);
            var v = col.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World; v.y = new ParticleSystem.MinMaxCurve(1.5f, 3f); v.x = 0f; v.z = 0f;
            var sh = col.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.5f; sh.rotation = new Vector3(-90, 0, 0);
            Kill(col, 2f);
            GroundRing(p, 0.2f, 0.9f, Heal, 0.7f);
            Flash(p + Vector3.up * 1f, Heal, 3f, 4f, 0.6f);
        }

        /// <summary>War Anthem, at the bard: golden ring and a burst of notes.</summary>
        public static void AnthemCast(Vector3 pos, float radius)
        {
            GroundRing(pos, 0.3f, radius, Anthem, 1.0f);
            GroundRing(pos, radius * 0.9f, radius, Anthem, 1.6f, 0.5f);
            Kill(Notes(null, pos + Vector3.up * 0.4f, Anthem, 36, 0f, 1.6f, 2.8f, 0.5f, false), 3f);
            Kill(Sparks(null, pos + Vector3.up * 1.2f, Anthem, 60, 0f, 0.3f, 5f, 0.12f, false), 2f);
            Flash(pos + Vector3.up * 1.5f, Anthem, 7f, 11f, 1f);
        }

        /// <summary>Looping golden aura on a buffed player (ring at the feet, sparks and notes rising).</summary>
        public static GameObject AnthemAura(Transform target)
        {
            var root = new GameObject("AnthemAura");
            root.transform.SetParent(target, false);
            var s = Sparks(root.transform, target.position + Vector3.up * 0.1f, Anthem, 0, 18f, 0.45f, 0.2f, 0.09f, true, 1.1f);
            var v = s.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World; v.y = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            var sh = s.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.5f; sh.rotation = new Vector3(-90, 0, 0);
            var main = s.main; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var n = Notes(root.transform, target.position + Vector3.up * 1.0f, Anthem, 0, 3f, 0.45f, 0.6f, 0.26f, true, 1.4f);
            var ring = NewPS("aura_ring", root.transform, target.position + Vector3.up * 0.05f, Ring, true, 1f);
            var rm = ring.main; rm.startLifetime = 1.2f; rm.startSpeed = 0f; rm.startSize = 1.3f; rm.startColor = new Color(Anthem.r, Anthem.g, Anthem.b, 0.45f);
            rm.simulationSpace = ParticleSystemSimulationSpace.Local;
            var re = ring.emission; re.rateOverTime = 1.6f;
            var rs = ring.shape; rs.enabled = false;
            ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            var rsol = ring.sizeOverLifetime; rsol.enabled = true; rsol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.6f), new Keyframe(1f, 1.15f)));
            var rcol = ring.colorOverLifetime; rcol.enabled = true; rcol.color = Fade(Anthem, 0.6f);
            ring.Play();
            var lg = new GameObject("aura_light"); lg.transform.SetParent(root.transform, false); lg.transform.localPosition = new Vector3(0, 0.8f, 0);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = Anthem; l.intensity = 1.2f; l.range = 3f; l.shadows = LightShadows.None;
            return root;
        }

        /// <summary>Dirge of Ruin: violet wisps orbiting the victim and notes falling around it.</summary>
        public static GameObject CurseAura(Transform target, float height)
        {
            var root = new GameObject("CurseAura");
            root.transform.SetParent(target, false);
            float k = Mathf.Max(1f, height / 2f);
            var s = Sparks(root.transform, target.position + Vector3.up * height * 0.5f, Curse, 0, 26f * k, 0.6f * k, 0.3f, 0.12f * k, true, 1.2f);
            var main = s.main; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var orbit = s.velocityOverLifetime; orbit.enabled = true; orbit.space = ParticleSystemSimulationSpace.Local;
            orbit.orbitalY = new ParticleSystem.MinMaxCurve(2.5f, 4f); orbit.y = new ParticleSystem.MinMaxCurve(-0.6f, -0.2f);
            var sh = s.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.7f * k; sh.rotation = new Vector3(-90, 0, 0);
            var n = Notes(root.transform, target.position + Vector3.up * (height + 0.3f), Curse, 0, 3f * k, 0.5f * k, -0.4f, 0.22f * k, true, 1.8f);
            var nm = n.main; nm.gravityModifier = 0.12f;
            var ring = NewPS("curse_ring", root.transform, target.position + Vector3.up * 0.06f, Ring, true, 1f);
            var rm = ring.main; rm.startLifetime = 1.4f; rm.startSpeed = 0f; rm.startSize = 1.6f * k; rm.startColor = new Color(Curse.r, Curse.g, Curse.b, 0.5f);
            rm.simulationSpace = ParticleSystemSimulationSpace.Local;
            var re = ring.emission; re.rateOverTime = 1.2f;
            var rs = ring.shape; rs.enabled = false;
            ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            var rsol = ring.sizeOverLifetime; rsol.enabled = true; rsol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1.1f), new Keyframe(1f, 0.5f)));
            var rcol = ring.colorOverLifetime; rcol.enabled = true; rcol.color = Fade(Curse, 0.7f);
            ring.Play();
            var lg = new GameObject("curse_light"); lg.transform.SetParent(root.transform, false); lg.transform.localPosition = new Vector3(0, height * 0.6f, 0);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = Curse; l.intensity = 1.6f * k; l.range = 3.5f * k; l.shadows = LightShadows.None;
            return root;
        }

        /// <summary>The curse lands on its victim.</summary>
        public static void CurseHit(Vector3 pos, float height)
        {
            float k = Mathf.Max(1f, height / 2f);
            GroundRing(pos, 2.2f * k, 0.3f, Curse, 0.6f);
            Kill(Sparks(null, pos + Vector3.up * height * 0.5f, Curse, 50, 0f, 0.4f * k, 4f, 0.15f * k, false), 2f);
            Kill(Notes(null, pos + Vector3.up * height, Curse, 10, 0f, 0.6f * k, -0.6f, 0.3f * k, false), 2.5f);
            Flash(pos + Vector3.up * height * 0.5f, Curse, 5f, 7f * k, 0.6f);
        }

        /// <summary>Thunderous Crescendo: three shock rings, a pillar of light, notes thrown outward.</summary>
        public static void Crescendo(Vector3 pos, float radius)
        {
            GroundRing(pos, 0.3f, radius, Arcane, 0.55f);
            GroundRing(pos, 0.3f, radius * 1.25f, Color.white, 0.8f, 0.6f);
            GroundRing(pos, radius * 0.4f, radius * 1.5f, Arcane, 1.1f, 0.4f);
            var burst = Sparks(null, pos + Vector3.up * 0.4f, Arcane, 90, 0f, 0.5f, 9f, 0.18f, false, 0.8f);
            var bs = burst.shape; bs.shapeType = ParticleSystemShapeType.Hemisphere; bs.rotation = new Vector3(-90, 0, 0);
            Kill(burst, 2f);
            var pillar = Sparks(null, pos, Color.Lerp(Arcane, Color.white, 0.5f), 70, 0f, 0.6f, 0.5f, 0.3f, false, 0.7f);
            var pv = pillar.velocityOverLifetime; pv.enabled = true; pv.space = ParticleSystemSimulationSpace.World; pv.y = new ParticleSystem.MinMaxCurve(6f, 12f);
            Kill(pillar, 2f);
            Kill(Notes(null, pos + Vector3.up * 0.5f, Arcane, 24, 0f, radius * 0.5f, 3.5f, 0.4f, false, 1.2f), 2.5f);
            Fx.Spawn(FxId.ShockDust, pos);
            Flash(pos + Vector3.up * 1.2f, Arcane, 14f, radius * 3f, 0.5f);
        }

        /// <summary>The ground glyph shown while the crescendo gathers (the area that will be struck).</summary>
        public static void CrescendoMark(Vector3 pos, float radius, float delay)
        {
            GroundRing(pos, radius, radius, Arcane, delay + 0.1f, 0.55f);
            GroundRing(pos, radius * 0.2f, radius, Arcane, delay, 0.35f);
            Kill(Notes(null, pos + Vector3.up * 0.1f, Arcane, 0, 18f, radius * 0.8f, 0.8f, 0.24f, true, 0.9f), delay + 1f);
        }

        public static void Release(GameObject aura)
        {
            if (aura == null) return;
            foreach (var ps in aura.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            foreach (var l in aura.GetComponentsInChildren<Light>()) l.enabled = false;
            aura.transform.SetParent(null, true);
            Destroy(aura, 2f);
        }
    }
}
