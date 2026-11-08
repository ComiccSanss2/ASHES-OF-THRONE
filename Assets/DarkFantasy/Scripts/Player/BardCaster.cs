using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// The Bard (third class): a hooded wanderer whose staff sings.
    ///   Left click  — Arcane Note: a homing bolt of pale light (slower cadence than the bow).
    ///   F (heavy)   — Triad: three notes fanned out.
    ///   Right click — aim over the shoulder (like the archer).
    ///   1 Mending Hymn      — heals the bard and every ally around him.
    ///   2 War Anthem        — allies around him deal more damage, take less, recover stamina faster.
    ///   3 Dirge of Ruin     — a seeking violet orb: the victim takes more damage and slows down.
    ///   4 Thunderous Crescendo — the staff strikes the ground: a shockwave at the aimed spot.
    /// Spells cost mana and have cooldowns. Damage/projectiles are owner-authoritative (like
    /// sword hits and arrows); heals and buffs are applied by the server.
    /// </summary>
    public class BardCaster : NetworkBehaviour
    {
        public enum SpellId { Hymn, Anthem, Dirge, Crescendo }

        [System.Serializable]
        public class Spell
        {
            public string name;
            [TextArea] public string blurb;
            public float mana = 30f;
            public float cooldown = 10f;
            public AttackData cast;
        }

        public static BardCaster Local { get; private set; }

        public PlayerController pc;
        public PlayerVitals vitals;
        [Tooltip("Crystal at the top of the staff (bolts and glows come from here).")]
        public Transform staffTip;
        public Light crystalLight;
        public Spell[] spells = new Spell[4];

        [Header("Arcane Note (left click)")]
        public float boltSpeed = 24f;
        public float triadSpread = 11f;

        [Header("Spells")]
        public float hymnRadius = 9f;
        [Range(0, 1)] public float hymnHeal = 0.35f;
        public float anthemRadius = 10f;
        public float anthemDuration = 15f;
        public float dirgeDuration = 12f;
        public float dirgeRange = 30f;
        public float crescendoRange = 16f;
        public float crescendoRadius = 4.2f;
        public float crescendoDamage = 55f;
        public float crescendoPoise = 75f;
        public float crescendoDelay = 0.35f;
        [Tooltip("Relic staff: spells cost less, damage more.")]
        public float relicManaMul = 0.8f;

        readonly float[] _readyAt = new float[4];
        float _failFlash;
        int _casting = -1;

        public static event System.Action<string> CastFailed;

        /// <summary>The look of the notes (the mandolin in his hands)…</summary>
        byte Tier => pc ? pc.WeaponTier : (byte)0;
        /// <summary>…and the power they carry (awakened only once earned in this session).</summary>
        byte Power => pc ? pc.PowerTier : (byte)0;
        bool Relic => Power != Inventory.BaseWeapon;
        public float ManaCost(int i) => spells[i].mana * (Power == Inventory.Radiant ? 0.65f : Power == Inventory.Dragon ? 0.7f : Power == Inventory.Relic ? relicManaMul : 1f);
        public float CooldownRemaining(int i) => Mathf.Max(0f, _readyAt[i] - Time.time);
        public float Cooldown01(int i) => spells[i].cooldown > 0 ? Mathf.Clamp01(CooldownRemaining(i) / spells[i].cooldown) : 0f;
        public bool CanAfford(int i) => vitals && vitals.Mana >= ManaCost(i);
        public int Casting => _casting;
        public float FailFlash => _failFlash;

        public override void OnNetworkSpawn()
        {
            if (IsOwner) Local = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
        }

        void Update()
        {
            if (_failFlash > 0f) _failFlash -= Time.deltaTime;
            // the crystal breathes; brighter while casting
            if (crystalLight)
            {
                float target = _casting >= 0 ? 4.5f : 1.3f + Mathf.Sin(Time.time * 2.2f) * 0.25f;
                crystalLight.intensity = Mathf.MoveTowards(crystalLight.intensity, pc && pc.IsDead ? 0f : target, Time.deltaTime * 12f);
                crystalLight.color = MagicBolt.ArcaneColor(Tier);
            }
        }

        // ================================================================== owner input (called by PlayerController in Free state)
        public void OwnerPollSpells()
        {
            for (int i = 0; i < 4; i++)
                if (GameInput.SpellPressed(i)) { TryCast(i); return; }
        }

        public bool TryCast(int i)
        {
            if (!IsOwner || pc == null || i < 0 || i >= spells.Length || spells[i] == null) return false;
            if (CooldownRemaining(i) > 0f) { Fail("Not ready"); return false; }
            if (!CanAfford(i)) { Fail("Not enough mana"); return false; }
            if (i == (int)SpellId.Dirge && FindTarget(dirgeRange) == null) { Fail("No target"); return false; }
            if (!pc.StartSpell(i, spells[i].cast, () => Release(i))) return false;
            vitals.UseMana(ManaCost(i));
            _readyAt[i] = Time.time + spells[i].cooldown;
            _casting = i;
            CastStartRpc((byte)i);
            return true;
        }

        void Fail(string why)
        {
            _failFlash = 0.6f;
            CastFailed?.Invoke(why);
            Sfx.Play2D(SfxId.SpellFail, 0.5f);
        }

        /// <summary>Called by PlayerController when the spell animation ends or is interrupted.</summary>
        public void OnSpellEnded() => _casting = -1;

        // ================================================================== release (owner, at the animation's release frame)
        void Release(int i)
        {
            Vector3 pos = transform.position;
            switch ((SpellId)i)
            {
                case SpellId.Hymn:
                    HymnRpc(pos);
                    break;
                case SpellId.Anthem:
                    AnthemRpc(pos);
                    break;
                case SpellId.Dirge:
                {
                    var t = FindTarget(dirgeRange);
                    Vector3 from = TipPosition;
                    Vector3 aim = t ? t.Point : pc.PredictAimPoint(from);
                    Vector3 vel = (aim - from).normalized * 15f;
                    var tr = t ? t.transform : null;
                    MagicBolt.Fire(MagicBolt.Kind.Dirge, from, vel, tr, pc, true, 0f, 0f, 0f, Tier, dirgeDuration * (Relic ? 1.2f : 1f));
                    NetworkObjectReference r = default;
                    if (t && t.GetComponentInParent<NetworkObject>() is NetworkObject no) r = no;
                    DirgeRpc(from, vel, r);
                    break;
                }
                case SpellId.Crescendo:
                {
                    Vector3 p = CrescendoPoint();
                    CrescendoMarkRpc(p);
                    StartCoroutine(CrescendoStrike(p));
                    break;
                }
            }
        }

        Vector3 TipPosition => staffTip ? staffTip.position : transform.position + Vector3.up * 1.7f + transform.forward * 0.5f;

        /// <summary>Locked target, else the enemy closest to the crosshair, within range and in sight.</summary>
        LockOnTarget FindTarget(float range)
        {
            var cam = SoulsCamera.Instance;
            if (cam && cam.IsLocked && cam.CurrentTarget && cam.CurrentTarget.IsValid) return cam.CurrentTarget;
            Vector3 from = TipPosition;
            Vector3 aimDir = cam && cam.Cam ? cam.AimRay.direction : transform.forward;
            LockOnTarget best = null; float bestA = 25f;
            foreach (var t in LockOnTarget.All)
            {
                if (t == null || !t.IsValid) continue;
                Vector3 to = t.Point - from;
                if (to.magnitude > range) continue;
                float a = Vector3.Angle(MathUtil.Flat(aimDir), MathUtil.Flat(to));
                if (a >= bestA) continue;
                if (Physics.Linecast(from, t.Point, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) continue;
                bestA = a; best = t;
            }
            return best;
        }

        Vector3 CrescendoPoint()
        {
            var cam = SoulsCamera.Instance;
            Vector3 p;
            if (cam && cam.IsLocked && cam.CurrentTarget) p = cam.CurrentTarget.transform.position;
            else p = pc.PredictAimPoint(TipPosition);
            Vector3 flat = MathUtil.Flat(p - transform.position);
            if (flat.magnitude > crescendoRange) flat = flat.normalized * crescendoRange;
            if (flat.magnitude < 2.5f) flat = MathUtil.Flat(transform.forward) * 3.5f;
            Vector3 q = transform.position + flat + Vector3.up * 3f;
            if (Physics.Raycast(q, Vector3.down, out var hit, 10f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) return hit.point;
            return transform.position + flat;
        }

        IEnumerator CrescendoStrike(Vector3 p)
        {
            yield return new WaitForSeconds(crescendoDelay);
            float dmg = crescendoDamage * pc.WeaponDamageMul * pc.DamageDealtMul;
            int hits = 0;
            foreach (var e in EnemyBase.All)
            {
                if (e == null || !e.IsAlive) continue;
                Vector3 to = e.transform.position - p;
                var cc = e.GetComponent<CharacterController>();
                float reach = crescendoRadius + (cc ? cc.radius : 0.4f);
                if (MathUtil.Flat(to).magnitude > reach || Mathf.Abs(to.y) > 3.5f) continue;
                Vector3 point = e.transform.position + Vector3.up * 1.1f;
                e.LocalHitReact(point, true, p, MathUtil.Flat(to).normalized + Vector3.up * 0.3f);
                e.TakeRangedHitRpc(dmg, crescendoPoise, point, p, 5f);
                hits++;
            }
            CrescendoRpc(p);
            SoulsCamera.Shake(0.09f, 0.3f);
            Debug.Log($"[DF] Crescendo: {hits} enemies hit ({dmg:0})");
        }

        // ================================================================== bolts (left click / heavy)
        /// <summary>Owner: fires the Arcane Note(s) on the release frame of the cast animation.</summary>
        public void FireBolt(AttackData a, bool triad)
        {
            Vector3 from = TipPosition;
            Vector3 chest = transform.position + Vector3.up * 1.4f;
            if (Physics.Linecast(chest, from, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) from = chest;
            var tgt = FindTarget(60f);
            Vector3 aim = tgt ? tgt.Point : pc.PredictAimPoint(from);
            float speed = boltSpeed * (Power == Inventory.Radiant ? 1.45f : Power == Inventory.Dragon ? 1.35f : Power == Inventory.Relic ? 1.25f : 1f);
            Vector3 dir = (aim - from).normalized;
            NetworkObjectReference r = default;
            if (tgt && tgt.GetComponentInParent<NetworkObject>() is NetworkObject no) r = no;
            int n = triad ? 3 : 1;
            for (int k = 0; k < n; k++)
            {
                float yaw = triad ? (k - 1) * triadSpread : 0f;
                Vector3 v = Quaternion.AngleAxis(yaw, Vector3.up) * dir * speed;
                MagicBolt.Fire(MagicBolt.Kind.Arcane, from, v, k == 1 || !triad ? (tgt ? tgt.transform : null) : null, pc, true, a.damage, a.poiseDamage, a.knockback, Tier);
                BoltRpc(from, v, k == 1 || !triad ? r : default);
            }
            Sfx.Play(SfxId.SpellBolt, from, triad ? 1f : 0.8f, triad ? Random.Range(0.74f, 0.8f) : Random.Range(0.95f, 1.1f));   // triad: a lower, fuller strum
            SoulsCamera.Shake(triad ? 0.03f : 0.012f, 0.1f);
        }

        // ================================================================== RPCs
        [Rpc(SendTo.Everyone)]
        void CastStartRpc(byte i)
        {
            Color c = i == 0 ? SpellFx.Heal : i == 1 ? SpellFx.Anthem : i == 2 ? SpellFx.Curse : SpellFx.Arcane;
            SpellFx.StaffCharge(staffTip, c, 0.9f);
            Sfx.Play(SfxId.SpellCast, TipPosition, 0.7f, i == 0 ? 1.12f : i == 1 ? 1f : i == 2 ? 0.7f : 1.3f);
            if (!IsOwner) _casting = i;
        }

        [Rpc(SendTo.Server)]
        void HymnRpc(Vector3 center)
        {
            int n = 0;
            foreach (var p in PlayerController.All)
            {
                if (p == null || p.IsDead || Vector3.Distance(p.transform.position, center) > hymnRadius) continue;
                var b = p.GetComponent<PlayerBuffs>();
                if (b) { b.ServerHeal(hymnHeal); n++; }
            }
            HymnFxRpc(center);
            Debug.Log($"[DF] Mending Hymn: {n} healed");
        }

        [Rpc(SendTo.Everyone)]
        void HymnFxRpc(Vector3 c)
        {
            SpellFx.HealCast(c, hymnRadius);
            Sfx.Play(SfxId.SpellHeal, c + Vector3.up, 1f);
            if (!IsOwner) _casting = -1;
        }

        [Rpc(SendTo.Server)]
        void AnthemRpc(Vector3 center)
        {
            int n = 0;
            foreach (var p in PlayerController.All)
            {
                if (p == null || p.IsDead || Vector3.Distance(p.transform.position, center) > anthemRadius) continue;
                var b = p.GetComponent<PlayerBuffs>();
                if (b) { b.ServerAnthem(anthemDuration); n++; }
            }
            AnthemFxRpc(center);
            Debug.Log($"[DF] War Anthem: {n} inspired");
        }

        [Rpc(SendTo.Everyone)]
        void AnthemFxRpc(Vector3 c)
        {
            SpellFx.AnthemCast(c, anthemRadius);
            Sfx.Play(SfxId.SpellAnthem, c + Vector3.up, 1f);
            if (!IsOwner) _casting = -1;
        }

        [Rpc(SendTo.NotMe)]
        void DirgeRpc(Vector3 from, Vector3 vel, NetworkObjectReference target)
        {
            Transform t = target.TryGet(out NetworkObject no) ? no.transform : null;
            MagicBolt.Fire(MagicBolt.Kind.Dirge, from, vel, t, pc, false, 0f, 0f, 0f, Tier);
            Sfx.Play(SfxId.SpellCurse, from, 0.6f, 1.2f);
            _casting = -1;
        }

        [Rpc(SendTo.NotMe)]
        void BoltRpc(Vector3 from, Vector3 vel, NetworkObjectReference target)
        {
            Transform t = target.TryGet(out NetworkObject no) ? no.transform : null;
            MagicBolt.Fire(MagicBolt.Kind.Arcane, from, vel, t, pc, false, 0f, 0f, 0f, Tier);
            Sfx.Play(SfxId.SpellBolt, from, 0.7f);
        }

        [Rpc(SendTo.Everyone)]
        void CrescendoMarkRpc(Vector3 p) => SpellFx.CrescendoMark(p, crescendoRadius, crescendoDelay);

        [Rpc(SendTo.Everyone)]
        void CrescendoRpc(Vector3 p)
        {
            SpellFx.Crescendo(p, crescendoRadius);
            Sfx.Play(SfxId.SpellCrescendo, p + Vector3.up, 1.2f);
            if (!IsOwner) _casting = -1;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Bridge tests: "0".."3" casts that spell.</summary>
        public void DevCast(string i) => TryCast(int.Parse(i));
        /// <summary>Development: "0" = an arcane note, "1" = the triad (straight from the mandolin, no animation).</summary>
        public void DevBolt(string i) { var pc = GetComponent<PlayerController>(); if (pc && pc.lightCombo != null && pc.lightCombo.Length > 0) FireBolt(pc.lightCombo[0], i == "1"); }
#endif
    }
}
