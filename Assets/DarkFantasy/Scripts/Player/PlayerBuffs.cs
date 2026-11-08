using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Bard magic received by a player (on every player prefab).
    ///  - War Anthem: +damage dealt, -damage taken, faster stamina; server-written end time so
    ///    everybody sees the golden aura and the HUD icon with the same countdown.
    ///  - Mending Hymn: the server asks the owner to heal (health is owner-authoritative).
    /// </summary>
    public class PlayerBuffs : NetworkBehaviour
    {
        public const float AnthemDamage = 1.25f, AnthemTaken = 0.82f, AnthemRegen = 1.4f;
        public static readonly List<PlayerBuffs> All = new List<PlayerBuffs>();

        readonly NetworkVariable<double> _anthemEnd = new NetworkVariable<double>(0);
        PlayerVitals _vitals;
        PlayerController _pc;
        GameObject _aura;

        double Now => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
        public bool AnthemActive => IsSpawned && Now < _anthemEnd.Value && (_vitals == null || !_vitals.IsDead);
        public float AnthemRemaining => Mathf.Max(0f, (float)(_anthemEnd.Value - Now));
        public float DamageDealtMul => AnthemActive ? AnthemDamage : 1f;
        public float DamageTakenMul => AnthemActive ? AnthemTaken : 1f;

        void Awake() { _vitals = GetComponent<PlayerVitals>(); _pc = GetComponent<PlayerController>(); }
        public override void OnNetworkSpawn() { if (!All.Contains(this)) All.Add(this); }
        public override void OnNetworkDespawn() { All.Remove(this); if (_aura) SpellFx.Release(_aura); _aura = null; }
        public override void OnDestroy() { All.Remove(this); base.OnDestroy(); }

        void Update()
        {
            if (!IsSpawned) return;
            bool on = AnthemActive;
            if (on && _aura == null) _aura = SpellFx.AnthemAura(transform);
            else if (!on && _aura != null) { SpellFx.Release(_aura); _aura = null; }
            if (IsOwner && _vitals) _vitals.BuffRegenMultiplier = on ? AnthemRegen : 1f;
        }

        // ------------------------------------------------------------------ server
        public void ServerAnthem(float duration)
        {
            if (!IsServer) return;
            _anthemEnd.Value = Now + duration;
        }

        /// <summary>Heals a fraction of the player's max health (applied by the owner).</summary>
        public void ServerHeal(float fraction)
        {
            if (!IsServer) return;
            HealOwnerRpc(fraction);
            HealFxRpc();
        }

        [Rpc(SendTo.Owner)]
        void HealOwnerRpc(float fraction) { if (_vitals) _vitals.Heal(fraction); }

        [Rpc(SendTo.Everyone)]
        void HealFxRpc()
        {
            SpellFx.HealBurst(transform);
            Sfx.Play(SfxId.SpellHeal, transform.position + Vector3.up, 0.55f, Random.Range(1.15f, 1.3f));
        }
    }
}
