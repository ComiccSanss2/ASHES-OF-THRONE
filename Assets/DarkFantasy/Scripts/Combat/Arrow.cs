using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Flying arrow (local, not a network object). Every client simulates the same shot from the
    /// same origin and velocity (sent by the archer); only the shooter's copy is "authoritative":
    /// on contact it plays the hit locally on the same frame and asks the server for the damage,
    /// exactly like a sword blade. The other copies are visual: they vanish on enemies (the server
    /// sends the blood to everyone else) and stick into walls and floors.
    ///
    /// The mesh origin is the nock (tail); the tip is <see cref="Length"/> metres ahead on +Z.
    /// Ballistic with light gravity, swept ray each frame (never tunnels through thin targets).
    /// Arrows pass through players (no friendly fire).
    /// </summary>
    public class Arrow : MonoBehaviour
    {
        public const float Length = 0.82f;

        public float gravity = 5.5f;
        public float maxLife = 4f;
        public float stuckLife = 9f;
        public TrailRenderer trail;

        Vector3 _vel;
        PlayerController _shooter;
        EnemyBase _enemyShooter;     // arrows of the enemy bowmen (they hit players, pass through enemies)
        HitFlags _flags;
        bool _authoritative, _stuck, _heavy;
        float _damage, _poise, _knockback, _life;
        readonly RaycastHit[] _hits = new RaycastHit[12];

        public static Arrow Fire(GameObject prefab, Vector3 nock, Vector3 velocity, PlayerController shooter, bool authoritative,
                                 bool heavy, float damage, float poise, float knockback)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, nock, Quaternion.LookRotation(velocity.normalized));
            var a = go.GetComponent<Arrow>() ?? go.AddComponent<Arrow>();
            a._vel = velocity; a._shooter = shooter; a._authoritative = authoritative;
            a._heavy = heavy; a._damage = damage; a._poise = poise; a._knockback = knockback;
            if (a.trail) { a.trail.Clear(); a.trail.emitting = true; a.trail.widthMultiplier = heavy ? 1.6f : 1f; }
            return a;
        }

        /// <summary>An enemy's arrow: the server copy deals the damage to the player it reaches.</summary>
        public static Arrow FireEnemy(GameObject prefab, Vector3 nock, Vector3 velocity, EnemyBase shooter, bool authoritative, float damage, float poise, HitFlags flags)
        {
            var a = Fire(prefab, nock, velocity, null, authoritative, false, damage, poise, 0f);
            if (a == null) return null;
            a._enemyShooter = shooter; a._flags = flags;
            if (a.trail)
                a.trail.colorGradient = new Gradient
                {
                    colorKeys = new[] { new GradientColorKey(new Color(1f, 0.45f, 0.3f), 0), new GradientColorKey(new Color(0.6f, 0.2f, 0.1f), 1) },
                    alphaKeys = new[] { new GradientAlphaKey(0.7f, 0), new GradientAlphaKey(0f, 1) }
                };
            return a;
        }

        byte _tier;
        ParticleSystem _fx;
        Light _glow;

        /// <summary>
        /// Each bow fletches its own arrows: the hunter's plain shafts, Thornwraith's rune-lit arrows
        /// (pale blue trail and drifting rune sparks), Wyrmwing's burning arrows (fire trail, flames,
        /// a burst of fire where they strike).
        /// </summary>
        public void SetTier(byte tier, bool heavy)
        {
            _tier = tier;
            if (tier == 0 || !trail) return;
            if (tier == Inventory.Radiant) { BuildRadiant(heavy); return; }
            bool dragon = tier == Inventory.Dragon;
            trail.colorGradient = dragon
                ? new Gradient
                {
                    colorKeys = new[] { new GradientColorKey(new Color(1f, 0.85f, 0.45f), 0), new GradientColorKey(new Color(1f, 0.35f, 0.08f), 0.35f), new GradientColorKey(new Color(0.45f, 0.06f, 0.03f), 1) },
                    alphaKeys = new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(0f, 1) }
                }
                : new Gradient
                {
                    colorKeys = new[] { new GradientColorKey(new Color(0.6f, 0.9f, 1f), 0), new GradientColorKey(new Color(0.35f, 0.6f, 0.9f), 1) },
                    alphaKeys = new[] { new GradientAlphaKey(0.85f, 0), new GradientAlphaKey(0f, 1) }
                };
            trail.time = dragon ? 0.32f : 0.2f;
            trail.widthMultiplier *= dragon ? 1.7f : 1.35f;
            var mat = Fx.Instance ? Fx.Instance.additiveMaterial : null;
            if (mat == null) return;
            // sparks / flames riding the shaft, left behind as it flies
            var go = new GameObject("arrow_fx"); go.transform.SetParent(transform, false); go.transform.localPosition = new Vector3(0, 0, Length * 0.8f);
            _fx = go.AddComponent<ParticleSystem>();
            _fx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = _fx.main; m.loop = true; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 120;
            m.startLifetime = new ParticleSystem.MinMaxCurve(dragon ? 0.25f : 0.4f, dragon ? 0.5f : 0.8f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, dragon ? 0.8f : 0.3f);
            m.startSize = new ParticleSystem.MinMaxCurve(dragon ? 0.12f : 0.04f, dragon ? (heavy ? 0.42f : 0.3f) : 0.09f);
            m.startColor = dragon ? new ParticleSystem.MinMaxGradient(new Color(1f, 0.7f, 0.25f, 0.9f), new Color(1f, 0.3f, 0.06f, 0.8f))
                                  : new ParticleSystem.MinMaxGradient(new Color(0.65f, 0.95f, 1f, 0.9f), new Color(0.35f, 0.6f, 1f, 0.7f));
            m.gravityModifier = dragon ? -0.25f : -0.05f;
            var e = _fx.emission; e.rateOverTime = dragon ? 50f : 10f; e.rateOverDistance = dragon ? 6f : 2.5f;
            var sh = _fx.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = dragon ? 0.05f : 0.03f;
            var col = _fx.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(dragon ? new Color(0.5f, 0.1f, 0.05f) : new Color(0.3f, 0.5f, 1f), 1) },
                      new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(0f, 1) });
            col.color = g;
            if (dragon) { var sol = _fx.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, 0.2f)); }
            go.GetComponent<ParticleSystemRenderer>().material = mat;
            _fx.Play();
            _glow = go.AddComponent<Light>(); _glow.type = LightType.Point; _glow.shadows = LightShadows.None;
            _glow.color = dragon ? new Color(1f, 0.45f, 0.15f) : new Color(0.5f, 0.8f, 1f);
            _glow.range = dragon ? 4.5f : 2.5f; _glow.intensity = dragon ? 3f : 1.2f;
        }

        /// <summary>Starwoven's arrows: a white-gold comet, a long glittering wake, a starburst where they strike.</summary>
        void BuildRadiant(bool heavy)
        {
            trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f, 0.85f, 0.45f), 0.4f), new GradientColorKey(new Color(0.75f, 0.6f, 1f), 1) },
                alphaKeys = new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(0f, 1) }
            };
            trail.time = 0.4f; trail.widthMultiplier *= 1.9f;
            var mat = Fx.Instance ? Fx.Instance.additiveMaterial : null;
            if (mat == null) return;
            var go = new GameObject("arrow_fx"); go.transform.SetParent(transform, false); go.transform.localPosition = new Vector3(0, 0, Length * 0.8f);
            _fx = go.AddComponent<ParticleSystem>();
            _fx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = _fx.main; m.loop = true; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 200;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.6f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.04f, heavy ? 0.16f : 0.11f);
            m.startColor = new ParticleSystem.MinMaxGradient(Color.white, new Color(1f, 0.82f, 0.4f));
            m.gravityModifier = 0.15f;
            var e = _fx.emission; e.rateOverTime = 40f; e.rateOverDistance = 9f;
            var sh = _fx.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.06f;
            var col = _fx.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f, 0.75f, 0.35f), 1) },
                      new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().material = mat;
            _fx.Play();
            _glow = go.AddComponent<Light>(); _glow.type = LightType.Point; _glow.shadows = LightShadows.None;
            _glow.color = new Color(1f, 0.9f, 0.65f); _glow.range = 4f; _glow.intensity = 2.6f;
        }

        /// <summary>Old entry point (Thornwraith).</summary>
        public void MakeRelic() => SetTier(Inventory.Relic, false);

        void TierImpact(Vector3 point)
        {
            if (_fx) { _fx.Stop(false, ParticleSystemStopBehavior.StopEmitting); _fx.transform.SetParent(null, true); Destroy(_fx.gameObject, 1f); _fx = null; }
            if (_glow) _glow.enabled = false;
            if (_tier == Inventory.Dragon)
            {
                Fx.SpawnScaled(FxId.Fireball, point, _heavy ? 0.9f : 0.55f);
                BossFx.Embers(point, 0.3f, _heavy ? 30 : 14, 4f);
                if (_heavy) Sfx.Play(SfxId.FireBurst, point, 0.6f, 1.3f);
            }
            else if (_tier == Inventory.Relic) Fx.SpawnScaled(FxId.ParryFlash, point, 0.35f);
            else if (_tier == Inventory.Radiant) { Fx.SpawnScaled(FxId.ParryFlash, point, _heavy ? 0.9f : 0.55f); SpellFx.Sparkles(point, _heavy ? 1f : 0.6f); }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _life += dt;
            if (_stuck) { if (_life > stuckLife) Destroy(gameObject); return; }
            if (_life > maxLife) { Destroy(gameObject); return; }

            Vector3 tip = transform.position + transform.forward * Length;
            Vector3 step = _vel * dt;
            float len = step.magnitude;
            if (len > 1e-5f)
            {
                int n = Physics.RaycastNonAlloc(tip, step / len, _hits, len, ~(1 << 2), QueryTriggerInteraction.Collide);
                System.Array.Sort(_hits, 0, n, HitComparer.Instance);
                for (int i = 0; i < n; i++)
                {
                    var h = _hits[i];
                    if (h.collider == null) continue;
                    var player = h.collider.GetComponentInParent<PlayerController>();
                    if (_enemyShooter != null)
                    {
                        if (h.collider.GetComponentInParent<EnemyBase>() != null) continue;    // through the other monsters
                        if (player != null)
                        {
                            if (player.IsDead) continue;
                            if (_authoritative) _enemyShooter.DeliverProjectileHit(player, _damage, _poise, _flags);
                            Destroy(gameObject);
                            return;
                        }
                        if (h.collider.isTrigger) continue;
                        Stick(h.point, step / len);
                        return;
                    }
                    if (player != null)
                    {
                        // through players, unless both shooter and target opted into PvP
                        if (player == _shooter || !PlayerController.CanPvp(_shooter, player)) continue;
                        if (_authoritative)
                            player.ReceiveHitRpc(_damage * PlayerController.PvpDamageMul, _poise, h.point - (step / len) * 3f,
                                                 (byte)(_heavy ? HitFlags.Heavy : HitFlags.None), _shooter.NetworkObject);
                        Fx.Spawn(FxId.HitSparks, h.point);
                        Destroy(gameObject);
                        return;
                    }
                    var enemy = h.collider.GetComponentInParent<EnemyBase>();
                    if (enemy != null)
                    {
                        if (!enemy.IsAlive) continue;
                        HitEnemy(enemy, h.point, step / len);
                        return;
                    }
                    if (h.collider.isTrigger) continue;
                    Stick(h.point, step / len);
                    return;
                }
            }
            _vel += Vector3.down * gravity * dt;
            transform.position += step;
            if (_vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(_vel);
        }

        void HitEnemy(EnemyBase enemy, Vector3 point, Vector3 dir)
        {
            TierImpact(point);
            if (_authoritative && _shooter != null)
            {
                // instant reaction for the archer, then the server applies damage/poise/knockback
                Vector3 from = point - MathUtil.Flat(dir).normalized * 3f;
                enemy.LocalHitReact(point, _heavy, from, dir);
                enemy.TakeRangedHitRpc(_damage, _poise, point, from, _knockback);
                PlayerUltimate.LocalDealt(_damage);
                if (_heavy) SoulsCamera.Shake(0.04f, 0.12f);
            }
            Destroy(gameObject);
        }

        void Stick(Vector3 point, Vector3 dir)
        {
            _stuck = true;
            _life = 0f;
            transform.rotation = Quaternion.LookRotation(dir);
            transform.position = point - dir * (Length * 0.78f);   // ~18 cm of the head buried
            if (trail) trail.emitting = false;
            TierImpact(point);
            Sfx.Play(SfxId.ArrowImpact, point, _heavy ? 0.9f : 0.65f);
            Fx.Spawn(FxId.HitSparks, point, -dir);
        }

        class HitComparer : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly HitComparer Instance = new HitComparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
