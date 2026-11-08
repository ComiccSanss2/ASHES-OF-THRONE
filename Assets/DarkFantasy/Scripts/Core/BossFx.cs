using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Client-side spectacle for the boss: ground danger zones that fill up before they
    /// explode, fire pillars, fire novas, light flashes. Pure visuals: damage is resolved by
    /// the server with exactly the same positions/timings.
    /// </summary>
    public static class BossFx
    {
        public static readonly Color FireCore = new Color(1f, 0.78f, 0.38f, 1f);
        public static readonly Color FireEdge = new Color(1f, 0.32f, 0.07f, 1f);
        public static readonly Color DangerRed = new Color(1f, 0.12f, 0.04f, 1f);
        public static readonly Color AshWhite = new Color(0.85f, 0.82f, 0.75f, 1f);

        static Material Add => Fx.Instance ? Fx.Instance.additiveMaterial : null;

        public static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 2.5f, Vector3.down, out var hit, 8f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                return hit.point;
            return p;
        }

        static ParticleSystem NewPS(string name, Vector3 pos, Quaternion rot, float life)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(pos, rot);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var em = ps.emission; em.rateOverTime = 0;
            go.GetComponent<ParticleSystemRenderer>().material = Add;
            Object.Destroy(go, life);
            return ps;
        }

        static Gradient Fade(Color a, Color b, float fadeStart = 0.4f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, fadeStart), new GradientAlphaKey(0, 1) });
            return g;
        }

        /// <summary>Glowing disc on the floor: "something is about to hit here". Fills up until it fires.</summary>
        public static void GroundWarning(Vector3 pos, float radius, float duration, Color color)
        {
            if (Fx.Instance == null) return;
            pos = Ground(pos) + Vector3.up * 0.05f;
            // outer zone (steady, faint, pulses at the end)
            var outer = NewPS("warn_zone", pos, Quaternion.identity, duration + 0.6f);
            outer.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            var col = outer.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0f, 0), new GradientAlphaKey(0.55f, 0.15f), new GradientAlphaKey(0.7f, 0.85f), new GradientAlphaKey(1f, 1f) });
            col.color = g;
            outer.Emit(new ParticleSystem.EmitParams { position = pos, startSize = radius * 2.3f, startLifetime = duration, startColor = new Color(color.r, color.g, color.b, 0.55f), velocity = Vector3.zero, applyShapeToPosition = false }, 1);
            // inner fill that grows to the edge exactly when the hit lands
            var inner = NewPS("warn_fill", pos + Vector3.up * 0.01f, Quaternion.identity, duration + 0.6f);
            inner.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            var sol = inner.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.05f, 1, 1f));
            var c2 = inner.colorOverLifetime; c2.enabled = true;
            var g2 = new Gradient();
            g2.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                       new[] { new GradientAlphaKey(0.4f, 0), new GradientAlphaKey(1f, 1f) });
            c2.color = g2;
            inner.Emit(new ParticleSystem.EmitParams { position = pos + Vector3.up * 0.01f, startSize = radius * 2f, startLifetime = duration, startColor = new Color(color.r * 1.4f, color.g * 1.4f, color.b * 1.4f, 0.9f), velocity = Vector3.zero, applyShapeToPosition = false }, 1);
        }

        /// <summary>Column of fire erupting from the ground.</summary>
        public static void FirePillar(Vector3 pos, float radius, float height)
        {
            if (Fx.Instance == null) return;
            pos = Ground(pos);
            var ps = NewPS("fire_pillar", pos, Quaternion.Euler(-90, 0, 0), 3f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(height * 1.4f, height * 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.5f, radius * 1.1f);
            main.startColor = new ParticleSystem.MinMaxGradient(FireCore, FireEdge);
            main.gravityModifier = -0.2f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6f; sh.radius = radius * 0.55f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, new Color(0.5f, 0.15f, 0.05f), 0.3f);
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, 0.3f));
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 1.2f;
            ps.Emit(90);
            Embers(pos + Vector3.up * 0.5f, radius, 40, 6f);
            Flash(pos + Vector3.up * 1.8f, FireEdge, 60f, radius * 6f, 0.55f);
            Fx.Spawn(FxId.Dust, pos);
        }

        /// <summary>Spherical fire explosion.</summary>
        public static void FireBurst(Vector3 pos, float radius)
        {
            if (Fx.Instance == null) return;
            var ps = NewPS("fire_burst", pos, Quaternion.identity, 3f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 1.6f, radius * 3.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.25f, radius * 0.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(FireCore, FireEdge);
            main.gravityModifier = -0.3f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = radius * 0.25f;
            ps.transform.rotation = Quaternion.Euler(-90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, new Color(0.45f, 0.12f, 0.04f), 0.25f);
            var limit = ps.limitVelocityOverLifetime; limit.enabled = true; limit.limit = radius * 0.6f; limit.dampen = 0.35f;
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.04f; r.lengthScale = 1.1f;
            ps.Emit(Mathf.RoundToInt(70 + radius * 18));
            Embers(pos, radius, 60, 8f);
            Flash(pos + Vector3.up, FireEdge, 90f, radius * 5f, 0.7f);
            Fx.Spawn(FxId.ShockDust, Ground(pos));
        }

        /// <summary>Floating embers (ash/sparks) bursting out and drifting up.</summary>
        public static void Embers(Vector3 pos, float radius, int count, float speed)
        {
            if (Fx.Instance == null) return;
            var ps = NewPS("embers", pos, Quaternion.Euler(-90, 0, 0), 4f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.25f), new Color(1f, 0.3f, 0.08f));
            main.gravityModifier = -0.15f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 60f; sh.radius = radius * 0.5f;
            var no = ps.noise; no.enabled = true; no.strength = 0.8f; no.frequency = 0.6f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, new Color(0.6f, 0.2f, 0.05f), 0.5f);
            ps.Emit(count);
        }

        /// <summary>Wall of flames (or dust) riding the front of an expanding shockwave ring.</summary>
        public static void RingWave(Vector3 center, float maxRadius, float speed, bool fire)
        {
            if (Fx.Instance == null) return;
            center = Ground(center) + Vector3.up * 0.1f;
            var ps = NewPS(fire ? "fire_ring" : "dust_ring", center, Quaternion.Euler(-90, 0, 0), maxRadius / speed + 2f);
            if (!fire) ps.GetComponent<ParticleSystemRenderer>().material = Fx.Instance.alphaMaterial;
            var main = ps.main;
            float life = maxRadius / speed;
            main.startLifetime = life;
            main.startSpeed = speed;
            main.startSize = new ParticleSystem.MinMaxCurve(fire ? 0.8f : 0.9f, fire ? 1.6f : 1.8f);
            main.startColor = fire ? new ParticleSystem.MinMaxGradient(FireCore, FireEdge) : new ParticleSystem.MinMaxGradient(new Color(0.5f, 0.46f, 0.4f, 0.7f), new Color(0.35f, 0.32f, 0.3f, 0.6f));
            main.gravityModifier = 0f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.4f; sh.radiusThickness = 0f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, fire ? new Color(0.6f, 0.15f, 0.05f) : Color.white, 0.6f);
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.7f, 1, 1.3f));
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local; vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0.6f, 2.2f); // rise a little (local Z = world up)
            ps.Emit(Mathf.RoundToInt(Mathf.Clamp(maxRadius * 14f, 60f, 260f)));
        }

        /// <summary>Ring of flames around a spinning boss (follows it for the duration).</summary>
        public static void FlameRing(Transform parent, float radius, float duration)
        {
            if (Fx.Instance == null || parent == null) return;
            var go = new GameObject("flame_ring");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, 0.15f, 0);
            go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.duration = duration; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1f);
            main.startColor = new ParticleSystem.MinMaxGradient(FireCore, FireEdge);
            main.gravityModifier = -0.3f;
            main.maxParticles = 600;
            var em = ps.emission; em.rateOverTime = 160f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius; sh.radiusThickness = 0.15f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, new Color(0.5f, 0.12f, 0.04f), 0.3f);
            go.GetComponent<ParticleSystemRenderer>().material = Add;
            ps.Play();
            var l = new GameObject("ring_light"); l.transform.SetParent(go.transform, false);
            var li = l.AddComponent<Light>(); li.type = LightType.Point; li.color = FireEdge; li.intensity = 35f; li.range = radius * 3f;
            l.AddComponent<FlickerLight>();
            Object.Destroy(go, duration + 0.1f);
        }

        /// <summary>Point light that flashes and fades (lights the arena on impacts).</summary>
        public static void Flash(Vector3 pos, Color color, float intensity, float range, float duration)
        {
            var go = new GameObject("fx_light");
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = color; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            go.AddComponent<LightFade>().Begin(duration);
        }

        /// <summary>The charge lane: two glowing edge lines + a fill streak from the dragon to where it will stop.</summary>
        public static void ChargeLane(Vector3 from, Vector3 dir, float length, float halfWidth, float duration)
        {
            if (Fx.Instance == null || length <= 0.5f) return;
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            var ps = NewPS("charge_lane", from, Quaternion.identity, duration + 0.5f);
            ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0f, 0), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            for (float d = 0f; d <= length; d += 0.42f)
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 p = Ground(from + dir * d + right * (halfWidth * side)) + Vector3.up * 0.07f;
                    ps.Emit(new ParticleSystem.EmitParams { position = p, startSize = 0.6f, startLifetime = duration, startColor = new Color(1f, 0.25f, 0.08f, 1f), velocity = Vector3.zero, applyShapeToPosition = false }, 1);
                }
            // arrow-heads pointing the way
            for (float d = 2f; d <= length; d += 4.4f)
            {
                Vector3 c = from + dir * d;
                for (int k = -3; k <= 3; k++)
                {
                    Vector3 p = Ground(c - dir * (Mathf.Abs(k) * 0.45f) + right * (k * 0.45f)) + Vector3.up * 0.08f;
                    ps.Emit(new ParticleSystem.EmitParams { position = p, startSize = 0.45f, startLifetime = duration, startColor = new Color(1f, 0.55f, 0.2f, 0.9f), velocity = Vector3.zero, applyShapeToPosition = false }, 1);
                }
            }
        }

        /// <summary>A patch of ground left burning (lingering flames + glow) for 'duration' seconds.</summary>
        public static void BurningGround(Vector3 pos, float radius, float duration)
        {
            if (Fx.Instance == null) return;
            var go = new GameObject("burning_ground");
            go.transform.SetPositionAndRotation(pos + Vector3.up * 0.05f, Quaternion.Euler(-90, 0, 0));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.duration = duration; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(FireCore, FireEdge);
            main.gravityModifier = -0.25f; main.maxParticles = 300;
            var em = ps.emission; em.rateOverTime = 55f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius * 0.85f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, new Color(0.45f, 0.1f, 0.03f), 0.3f);
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, 0.2f));
            go.GetComponent<ParticleSystemRenderer>().material = Add;
            ps.Play();
            // scorched glow disc under the flames
            var disc = NewPS("burn_glow", pos + Vector3.up * 0.04f, Quaternion.identity, duration + 0.6f);
            disc.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            var dc = disc.colorOverLifetime; dc.enabled = true; dc.color = Fade(Color.white, Color.white, 0.75f);
            disc.Emit(new ParticleSystem.EmitParams { position = pos + Vector3.up * 0.04f, startSize = radius * 2.4f, startLifetime = duration, startColor = new Color(1f, 0.3f, 0.06f, 0.55f), velocity = Vector3.zero, applyShapeToPosition = false }, 1);
            var l = new GameObject("burn_light"); l.transform.SetParent(go.transform, false); l.transform.localPosition = new Vector3(0, 0, 1f);
            var li = l.AddComponent<Light>(); li.type = LightType.Point; li.color = FireEdge; li.intensity = 6f; li.range = radius * 3.5f; li.shadows = LightShadows.None;
            l.AddComponent<FlickerLight>();
            var em2 = go.AddComponent<StopAfter>(); em2.Begin(ps, li, duration);
            Object.Destroy(go, duration + 1.2f);
        }

        /// <summary>A fire pillar that erupts after 'delay' (paired with a GroundWarning).</summary>
        public static void DelayedPillar(Vector3 pos, float radius, float delay)
        {
            var go = new GameObject("delayed_pillar");
            go.AddComponent<DelayedAction>().Begin(delay, () => { FirePillar(pos, radius, 6f); Sfx.Play(SfxId.FireBurst, pos, 1f, Random.Range(0.85f, 1.05f)); });
        }

        /// <summary>Chunks of stone flung up by a slam (alpha-blended, fall back with gravity).</summary>
        public static void Debris(Vector3 pos, float radius, int count)
        {
            if (Fx.Instance == null) return;
            var ps = NewPS("debris", pos + Vector3.up * 0.2f, Quaternion.Euler(-90, 0, 0), 3f);
            ps.GetComponent<ParticleSystemRenderer>().material = Fx.Instance.alphaMaterial;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.32f, 0.29f, 0.26f, 1f), new Color(0.18f, 0.16f, 0.15f, 1f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 2.2f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = radius;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, Color.white, 0.8f);
            ps.Emit(count);
        }
    }

    /// <summary>Stops a looping particle system (and fades its light) after a time.</summary>
    public class StopAfter : MonoBehaviour
    {
        ParticleSystem _ps; Light _l; float _t, _dur, _i0;
        public void Begin(ParticleSystem ps, Light l, float duration) { _ps = ps; _l = l; _dur = duration; _i0 = l ? l.intensity : 0f; }
        void Update()
        {
            _t += Time.deltaTime;
            if (_t >= _dur && _ps && _ps.isEmitting) _ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            if (_l) { var f = _l.GetComponent<FlickerLight>(); if (f && _t > _dur) f.enabled = false; if (_t > _dur) _l.intensity = Mathf.Lerp(_i0, 0f, (_t - _dur) / 1f); }
        }
    }

    /// <summary>Runs an action after a delay, then destroys itself.</summary>
    public class DelayedAction : MonoBehaviour
    {
        System.Action _a; float _t;
        public void Begin(float delay, System.Action a) { _t = delay; _a = a; }
        void Update() { _t -= Time.deltaTime; if (_t <= 0f) { _a?.Invoke(); Destroy(gameObject); } }
    }
}
