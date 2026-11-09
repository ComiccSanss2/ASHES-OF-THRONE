using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// The Smoker (fifth class): a vagrant sage with a churchwarden pipe as long as a staff.
    ///   Left click (hold)  — Puff: a homing ball of smoke with an ember at its heart.
    ///   Right click (hold) — Draw on the pipe: mana burns into breath (up to 3). Let go to exhale:
    ///                        one smoke spirit per breath rises and hunts the nearest foe (max 4 alive).
    ///   F (heavy)          — Choking Gust: a cone of black smoke (damage, poise) and every spirit
    ///                        lunges at the first foe caught in it.
    ///   1 (ultimate)       — The Great Exhale: a colossus of smoke rises behind him and pounds the
    ///                        field with its fists, then crashes down with both (PlayerUltimate).
    /// Damage is owner-authoritative (like bolts and arrows); the others see the same spirits as
    /// visual copies, driven by the same rules.
    /// </summary>
    public class SmokerCaster : NetworkBehaviour
    {
        public static SmokerCaster Local { get; private set; }

        public PlayerController pc;
        public PlayerVitals vitals;
        [Tooltip("Bowl of the pipe: smoke, embers and the puffs come from here.")]
        public Transform bowl;
        [Tooltip("Head bone: the exhaled smoke leaves from the mouth.")]
        public Transform mouth;
        public Light emberLight;
        [Tooltip("Spell animation played on exhale.")]
        public AttackData exhaleCast;

        [Header("Puff (left click)")]
        public float boltSpeed = 17f;
        [Header("Breath (right click)")]
        public float inhaleManaPerSecond = 20f, breathPerSecond = 0.9f, minManaToDraw = 15f;
        public int maxBreath = 3, maxSpirits = 3;
        public float spiritLife = 12f, spiritDamage = 6f, spiritPoise = 5f;
        [Tooltip("After an exhale he must catch his breath before drawing again.")]
        public float exhaleCooldown = 4f;
        [Header("Choking Gust (F)")]
        public float gustMana = 30f, gustCooldown = 5f, gustRange = 8f, gustHalfAngle = 30f, gustDamage = 20f, gustPoise = 40f;

        float _breath;
        bool _inhaling;
        float _failFlash, _nextPuff = 3f, _drawReadyAt, _gustReadyAt;
        ParticleSystem _mouthSmoke, _bowlSmoke;
        int _spawnSeq;

        public static event System.Action<string> CastFailed;

        public bool Inhaling => _inhaling;
        public float Breath => _breath;
        public int Spirits => SmokeSpirit.CountFor(this);
        public float FailFlash => _failFlash;
        /// <summary>Seconds until he can draw on the pipe again (0 = ready).</summary>
        public float DrawCooldown => Mathf.Max(0f, _drawReadyAt - Time.time);
        public float GustCooldown => Mathf.Max(0f, _gustReadyAt - Time.time);
        byte Tier => pc ? pc.WeaponTier : (byte)0;
        byte Power => pc ? pc.PowerTier : (byte)0;
        /// <summary>Better pipes: the smoke is cheaper and hits harder (on top of the weapon multiplier).</summary>
        public float ManaMul => Power == Inventory.Radiant ? 0.65f : Power == Inventory.Dragon ? 0.7f : Power == Inventory.Relic ? 0.8f : 1f;
        float DmgMul => pc ? pc.WeaponDamageMul * pc.DamageDealtMul : 1f;

        public static Color SmokeColor(byte tier) =>
            tier == Inventory.Radiant ? new Color(0.95f, 0.9f, 0.75f) : tier == Inventory.Dragon ? new Color(0.42f, 0.3f, 0.28f)
          : tier == Inventory.Relic ? new Color(0.62f, 0.56f, 0.5f) : new Color(0.55f, 0.55f, 0.58f);
        public static Color EmberColor(byte tier) =>
            tier == Inventory.Radiant ? new Color(1f, 0.9f, 0.55f) : tier == Inventory.Dragon ? new Color(1f, 0.32f, 0.08f)
          : tier == Inventory.Relic ? new Color(1f, 0.62f, 0.3f) : new Color(1f, 0.5f, 0.2f);
        public Color Smoke => SmokeColor(Tier);
        public Color Ember => EmberColor(Tier);

        public Vector3 BowlPosition => bowl ? bowl.position : transform.position + Vector3.up * 1.9f + transform.forward * 0.5f;
        public Vector3 MouthPosition => mouth ? mouth.position + transform.forward * 0.18f : transform.position + Vector3.up * 1.6f + transform.forward * 0.2f;

        public override void OnNetworkSpawn() { if (IsOwner) Local = this; }
        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
            SmokeSpirit.DismissAll(this);
        }

        void Start()
        {
            _mouthSmoke = SmokeFx.Emitter(mouth ? mouth : transform, Vector3.zero, 0.1f, Smoke, 0f, 1.6f, 0.25f, 0.9f);
            _bowlSmoke = SmokeFx.Emitter(bowl ? bowl : transform, Vector3.zero, 0.05f, Smoke, 3f, 2.2f, 0.12f, 0.55f);
        }

        void Update()
        {
            if (_failFlash > 0f) _failFlash -= Time.deltaTime;
            bool dead = pc && pc.IsDead;
            if (emberLight)
            {
                float want = dead ? 0f : _inhaling ? 2.5f + _breath * 1.6f + Mathf.Sin(Time.time * 18f) * 0.4f : 1.1f + Mathf.Sin(Time.time * 2.3f) * 0.25f;
                emberLight.intensity = Mathf.MoveTowards(emberLight.intensity, want, Time.deltaTime * 14f);
                emberLight.color = Ember;
            }
            if (_bowlSmoke)
            {
                var em = _bowlSmoke.emission; em.rateOverTime = dead ? 0f : _inhaling ? 0f : 3f;
                var m = _bowlSmoke.main; m.startColor = Smoke;
            }
            // between fights the smoker puffs on his pipe: a slow cloud from the mouth every few seconds
            _nextPuff -= Time.deltaTime;
            if (_nextPuff <= 0f && !dead && !_inhaling)
            {
                _nextPuff = Random.Range(4f, 7f);
                if (_mouthSmoke) { var m = _mouthSmoke.main; m.startColor = Smoke; _mouthSmoke.Emit(10); }
            }
        }

        void Fail(string why)
        {
            _failFlash = 0.6f;
            CastFailed?.Invoke(why);
            Sfx.Play2D(SfxId.SpellFail, 0.5f);
        }

        // ================================================================== breath (owner, Free state)
        /// <summary>Owner, every frame in the Free state. Returns true while drawing on the pipe.</summary>
        /// <summary>Bridge tests: behaves as if the draw button were held.</summary>
        public static bool DevHold;

        public bool OwnerInhale(bool held, float dt)
        {
            held |= DevHold;
            if (!IsOwner || vitals == null) return false;
            if (!_inhaling)
            {
                if (!held) return false;
                if (Time.time < _drawReadyAt) { if (_failFlash <= 0f) Fail("Catching breath..."); return false; }
                if (vitals.Mana < minManaToDraw * ManaMul) { if (_failFlash <= 0f) Fail("Not enough mana"); return false; }
                _inhaling = true; _breath = 0f;
                InhaleRpc(true);
            }
            if (held)
            {
                if (_breath < maxBreath)
                {
                    float cost = inhaleManaPerSecond * ManaMul * dt;
                    if (vitals.Mana < cost) { Exhale(); return false; }     // out of mana: he lets it go
                    vitals.UseMana(cost);
                    _breath = Mathf.Min(maxBreath, _breath + breathPerSecond * dt);
                }
                return true;
            }
            Exhale();
            return false;
        }

        void Exhale()
        {
            int n = Mathf.FloorToInt(_breath + 0.001f);
            _inhaling = false; _breath = 0f;
            if (n >= 1) _drawReadyAt = Time.time + exhaleCooldown;
            InhaleRpc(false);
            if (n < 1) { PuffFxRpc(MouthPosition, transform.forward, 0); return; }   // too short a draw: just a wisp
            if (exhaleCast != null && pc.StartSpell(0, exhaleCast, () => ReleaseSpirits(n))) return;
            ReleaseSpirits(n);
        }

        void ReleaseSpirits(int n)
        {
            Vector3 from = MouthPosition;
            int seed = (int)(OwnerClientId * 1000) + _spawnSeq;
            _spawnSeq += n;
            SpawnSpirits(from, transform.forward, n, seed, true);
            SpiritsRpc(from, transform.forward, (byte)n, seed);
            SoulsCamera.Shake(0.02f, 0.15f);
        }

        void SpawnSpirits(Vector3 from, Vector3 fwd, int n, int seed, bool authoritative)
        {
            Sfx.Play(SfxId.MirrorVortex, from, 0.55f, 1.6f);
            Sfx.Play(SfxId.FogPass, from, 0.8f, 1.3f);
            SmokeFx.Cloud(from + fwd * 0.6f, fwd, 1.2f, Smoke, 40);
            for (int k = 0; k < n; k++)
            {
                float a = (k - (n - 1) * 0.5f) * 38f;
                Vector3 dir = Quaternion.AngleAxis(a, Vector3.up) * MathUtil.Flat(fwd).normalized;
                SmokeSpirit.Spawn(this, from, dir, seed + k, authoritative);
            }
            SmokeSpirit.Trim(this, maxSpirits);
        }

        [Rpc(SendTo.NotMe)]
        void SpiritsRpc(Vector3 from, Vector3 fwd, byte n, int seed) => SpawnSpirits(from, fwd, n, seed, false);

        [Rpc(SendTo.Everyone)]
        void InhaleRpc(bool on)
        {
            if (!IsOwner) _inhaling = on;
            if (on) { Sfx.Play(SfxId.FogPass, BowlPosition, 0.6f, 0.6f); if (bowl) SmokeFx.EmberSparks(BowlPosition, Ember, 8); }
        }

        [Rpc(SendTo.Everyone)]
        void PuffFxRpc(Vector3 from, Vector3 fwd, byte _)
        {
            SmokeFx.Cloud(from + fwd * 0.3f, fwd, 0.5f, Smoke, 14);
            Sfx.Play(SfxId.FogPass, from, 0.4f, 1.6f);
        }

        // ================================================================== puff (left click)
        public void FireBolt(AttackData a)
        {
            // the puff leaves his mouth (the pipe has just been at his lips)
            Vector3 from = MouthPosition + transform.forward * 0.25f;
            Vector3 chest = transform.position + Vector3.up * 1.4f;
            if (Physics.Linecast(chest, from, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) from = chest;
            var tgt = FindTarget(55f);
            Vector3 aim = tgt ? tgt.Point : pc.PredictAimPoint(from);
            Vector3 vel = (aim - from).normalized * boltSpeed * (Power == Inventory.BaseWeapon ? 1f : 1.2f);
            NetworkObjectReference r = default;
            if (tgt && tgt.GetComponentInParent<NetworkObject>() is NetworkObject no) r = no;
            SmokeBolt.Fire(from, vel, tgt ? tgt.transform : null, pc, true, a.damage, a.poiseDamage, a.knockback, Smoke, Ember);
            BoltRpc(from, vel, r);
            Sfx.Play(SfxId.FogPass, from, 0.7f, Random.Range(1.4f, 1.7f));
            SoulsCamera.Shake(0.01f, 0.08f);
        }

        [Rpc(SendTo.NotMe)]
        void BoltRpc(Vector3 from, Vector3 vel, NetworkObjectReference target)
        {
            Transform t = target.TryGet(out NetworkObject no) ? no.transform : null;
            SmokeBolt.Fire(from, vel, t, pc, false, 0f, 0f, 0f, Smoke, Ember);
            Sfx.Play(SfxId.FogPass, from, 0.5f, 1.5f);
        }

        // ================================================================== Choking Gust (F)
        public bool CanGust()
        {
            if (Time.time < _gustReadyAt) { Fail($"Choking Gust ready in {GustCooldown:0.0}s"); return false; }
            if (vitals && vitals.Mana >= gustMana * ManaMul) return true;
            Fail("Not enough mana");
            return false;
        }

        public void Gust(AttackData a)
        {
            if (vitals) vitals.UseMana(gustMana * ManaMul);
            _gustReadyAt = Time.time + gustCooldown;
            Vector3 o = transform.position;
            Vector3 dir = MathUtil.Flat(transform.forward).normalized;
            float dmg = gustDamage * DmgMul;
            Transform first = null; float firstD = 99f;
            foreach (var e in EnemyBase.All)
            {
                if (e == null || !e.IsAlive) continue;
                Vector3 to = e.transform.position - o;
                float d = MathUtil.Flat(to).magnitude;
                var cc = e.GetComponent<CharacterController>();
                if (d > gustRange + (cc ? cc.radius : 0.4f) || Mathf.Abs(to.y) > 3f) continue;
                if (d > 1f && Vector3.Angle(dir, MathUtil.Flat(to)) > gustHalfAngle) continue;
                Vector3 point = e.transform.position + Vector3.up * 1.1f;
                e.LocalHitReact(point, true, o, dir);
                e.TakeRangedHitRpc(dmg, gustPoise, point, o, 4f);
                PlayerUltimate.LocalDealt(dmg);
                if (d < firstD) { firstD = d; first = e.transform; }
            }
            foreach (var pl in PlayerController.All)
            {
                if (pl == null || pl == pc || pl.IsDead || !PlayerController.CanPvp(pc, pl)) continue;
                Vector3 to = pl.transform.position - o;
                float d = MathUtil.Flat(to).magnitude;
                if (d > gustRange || Mathf.Abs(to.y) > 3f || (d > 1f && Vector3.Angle(dir, MathUtil.Flat(to)) > gustHalfAngle)) continue;
                pl.ReceiveHitRpc(dmg * PlayerController.PvpDamageMul, gustPoise, o, (byte)HitFlags.Heavy, pc.NetworkObject);
                if (d < firstD) { firstD = d; first = pl.transform; }
            }
            NetworkObjectReference r = default;
            if (first && first.GetComponentInParent<NetworkObject>() is NetworkObject no) r = no;
            GustRpc(MouthPosition, dir, r);
            SoulsCamera.Shake(0.04f, 0.2f);
        }

        [Rpc(SendTo.Everyone)]
        void GustRpc(Vector3 from, Vector3 dir, NetworkObjectReference target)
        {
            SmokeFx.Gust(from, dir, gustRange, gustHalfAngle, Smoke, Ember);
            Sfx.Play(SfxId.DragonFire, from, 0.55f, 1.5f);
            Sfx.Play(SfxId.FogPass, from, 1f, 0.8f);
            Transform t = target.TryGet(out NetworkObject no) ? no.transform : null;
            if (t) SmokeSpirit.CommandAll(this, t);
        }

        // ================================================================== spirit hits (owner-authoritative)
        public void SpiritHit(Transform victim, Vector3 from, float mul)
        {
            if (!IsOwner || victim == null) return;
            float dmg = spiritDamage * mul * DmgMul;
            var e = victim.GetComponent<EnemyBase>();
            if (e != null)
            {
                if (!e.IsAlive) return;
                Vector3 point = e.transform.position + Vector3.up * 1.1f;
                e.LocalHitReact(point, mul > 1.5f, from, MathUtil.Flat(point - from).normalized);
                e.TakeRangedHitRpc(dmg, spiritPoise * mul, point, from, mul > 1.5f ? 3f : 0.8f);
                PlayerUltimate.LocalDealt(dmg);
                return;
            }
            var pl = victim.GetComponent<PlayerController>();
            if (pl && pl != pc && !pl.IsDead && PlayerController.CanPvp(pc, pl))
                pl.ReceiveHitRpc(dmg * PlayerController.PvpDamageMul, spiritPoise * mul, from, (byte)HitFlags.None, pc.NetworkObject);
        }

        /// <summary>Locked target, else the foe closest to the crosshair, within range and in sight.</summary>
        LockOnTarget FindTarget(float range)
        {
            var cam = SoulsCamera.Instance;
            if (cam && cam.IsLocked && cam.CurrentTarget && cam.CurrentTarget.IsValid) return cam.CurrentTarget;
            Vector3 from = BowlPosition;
            Vector3 aimDir = cam && cam.Cam ? cam.AimRay.direction : transform.forward;
            LockOnTarget best = null; float bestA = 22f;
            foreach (var t in LockOnTarget.All)
            {
                if (t == null || !t.IsValid) continue;
                Vector3 to = t.Point - from;
                if (to.magnitude > range) continue;
                float ang = Vector3.Angle(MathUtil.Flat(aimDir), MathUtil.Flat(to));
                if (ang >= bestA) continue;
                if (Physics.Linecast(from, t.Point, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) continue;
                bestA = ang; best = t;
            }
            return best;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Bridge tests: "n" = exhale n spirits at once; "gust" = the heavy; "puff" = a bolt.</summary>
        public void DevSmoke(string a)
        {
            if (a == "gust") { if (vitals) vitals.RestoreAtFlame(); Gust(pc.heavyAttack); return; }
            if (a == "puff") { FireBolt(pc.lightCombo[0]); return; }
            if (a == "debug") { SmokerPose.Debug = true; return; }
            if (a == "hold") { DevHold = true; if (vitals) vitals.RestoreAtFlame(); return; }
            if (a == "release") { DevHold = false; return; }
            ReleaseSpirits(Mathf.Clamp(int.Parse(a), 1, 3));
        }
#endif
    }
}
