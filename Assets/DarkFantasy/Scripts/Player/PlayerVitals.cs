using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Salute, stamina e fiaschette del giocatore. Variabili di rete scritte dal PROPRIETARIO
    /// (il giocatore risolve localmente schivate/parate, poi pubblica il risultato): tutti i
    /// client leggono questi valori per le barre della vita del gruppo.
    /// </summary>
    public class PlayerVitals : NetworkBehaviour, IHealthView
    {
        [Header("Valori base")]
        public float maxHealth = 130f;
        public float maxStamina = 100f;
        public float staminaRegen = 45f;
        public float staminaRegenDelay = 0.85f;
        public int maxFlasks = 4;
        [Tooltip("Mana for spells (0 = the class has none). Regenerates slowly, faster out of combat.")]
        public float maxMana = 0f;
        public float manaRegen = 4.5f;
        [Range(0, 1)] public float flaskHeal = 0.5f;

        readonly NetworkVariable<float> _hp = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<float> _stamina = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> _dead = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<int> _flasks = new NetworkVariable<int>(3, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<float> _mana = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        // ---- downed (co-op): on the ground, bleeding out, can be revived by an ally
        readonly NetworkVariable<bool> _downed = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<float> _bleed = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<float> _revive = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<byte> _reviveState = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        // ---- scoreboard (server writes)
        readonly NetworkVariable<bool> _pvp = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<int> _kills = new NetworkVariable<int>(0);
        readonly NetworkVariable<int> _deaths = new NetworkVariable<int>(0);
        readonly NetworkVariable<int> _revives = new NetworkVariable<int>(0);
        readonly NetworkVariable<ushort> _ping = new NetworkVariable<ushort>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<FixedString32Bytes> _name = new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        float _regenDelayTimer;

        public const float BleedOutTime = 10f, ReviveTime = 4f, ReviveRadius = 2.6f;
        public bool IsDowned => _downed.Value;
        /// <summary>Seconds left before a downed player dies for good.</summary>
        public float BleedLeft => _bleed.Value;
        public float Revive01 => Mathf.Clamp01(_revive.Value / ReviveTime);
        /// <summary>0 = nobody helping, 1 = revive started but nobody in the circle (timer runs), 2 = an ally is in the circle (timer paused).</summary>
        public byte ReviveState => _reviveState.Value;
        public int Kills => _kills.Value;
        /// <summary>Open to player-vs-player: two players who both have it ON can hurt and kill each other.</summary>
        public bool PvpOn => _pvp.Value;
        public void SetPvp(bool on) { if (IsOwner) _pvp.Value = on; }
        /// <summary>Every client: someone switched PvP (UI message).</summary>
        public static event System.Action<PlayerVitals, bool> PvpChanged;
        public int Deaths => _deaths.Value;
        public int Revives => _revives.Value;
        public float Health => _hp.Value;
        public float Stamina => _stamina.Value;
        public int Flasks => _flasks.Value;
        public bool IsDead => _dead.Value;
        public bool IsAlive => !_dead.Value;
        public float Health01 => maxHealth > 0 ? Mathf.Clamp01(_hp.Value / maxHealth) : 0;
        public float Stamina01 => maxStamina > 0 ? Mathf.Clamp01(_stamina.Value / maxStamina) : 0;
        public float Mana => _mana.Value;
        public float Mana01 => maxMana > 0 ? Mathf.Clamp01(_mana.Value / maxMana) : 0;
        public bool HasManaPool => maxMana > 0f;
        /// <summary>Multiplier on the stamina regeneration from buffs (War Anthem).</summary>
        public float BuffRegenMultiplier { get; set; } = 1f;
        public string DisplayName => _name.Value.Length > 0 ? _name.Value.ToString() : "Knight " + (OwnerClientId + 1);

        /// <summary>Round trip to the host in ms (the host itself: 0).</summary>
        public int Ping => _ping.Value;
        float _pingTimer;

        /// <summary>Every client: a chat line arrived (sender, text).</summary>
        public static event System.Action<PlayerVitals, string> ChatReceived;

        /// <summary>Owner: an Ember Flask found in the prison: one more flask for the rest of the session.</summary>
        public void AddFlaskCharge()
        {
            if (!IsOwner) return;
            maxFlasks++;
            _flasks.Value = Mathf.Min(maxFlasks, _flasks.Value + 1);
        }

        /// <summary>Owner: say something to everybody in the session.</summary>
        public void SendChat(string text)
        {
            if (!IsOwner || string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            if (text.Length > 120) text = text.Substring(0, 120);
            ChatRpc(new FixedString128Bytes(text));
        }

        [Rpc(SendTo.Everyone)]
        void ChatRpc(FixedString128Bytes text) => ChatReceived?.Invoke(this, text.ToString());

        void UpdatePing()
        {
            _pingTimer -= Time.unscaledDeltaTime;
            if (_pingTimer > 0f) return;
            _pingTimer = 1f;
            var nm = NetworkManager;
            if (nm == null || nm.IsServer) { if (_ping.Value != 0) _ping.Value = 0; return; }
            if (nm.NetworkConfig.NetworkTransport is Unity.Netcode.Transports.UTP.UnityTransport ut)
            {
                ulong rtt = ut.GetCurrentRtt(NetworkManager.ServerClientId);
                ushort v = (ushort)Mathf.Min(9999, (int)rtt);
                if (v != _ping.Value) _ping.Value = v;
            }
        }

        public override void OnNetworkSpawn()
        {
            _pvp.OnValueChanged += (a, b) => PvpChanged?.Invoke(this, b);
            if (IsOwner)
            {
                _hp.Value = maxHealth;
                _stamina.Value = maxStamina;
                _flasks.Value = maxFlasks;
                _mana.Value = maxMana;
                _dead.Value = false;
                string n = string.IsNullOrWhiteSpace(MenuSettings.PlayerName) ? "Knight " + (OwnerClientId + 1) : MenuSettings.PlayerName;
                if (n.Length > 28) n = n.Substring(0, 28);
                _name.Value = new FixedString32Bytes(n);
            }
        }

        void Update()
        {
            if (IsSpawned && IsOwner) UpdatePing();
            if (!IsSpawned || !IsOwner || IsDead) return;
            if (_regenDelayTimer > 0f) _regenDelayTimer -= Time.deltaTime;
            else if (_stamina.Value < maxStamina) _stamina.Value = Mathf.Min(maxStamina, _stamina.Value + staminaRegen * RegenMultiplier * BuffRegenMultiplier * Time.deltaTime);
            if (maxMana > 0f && _mana.Value < maxMana)
            {
                _manaTick += manaRegen * Time.deltaTime;
                if (_manaTick >= 0.5f || _mana.Value + _manaTick >= maxMana) { _mana.Value = Mathf.Min(maxMana, _mana.Value + _manaTick); _manaTick = 0f; }
            }
        }
        float _manaTick;

        public bool HasMana(float amount) => _mana.Value >= amount;

        public bool UseMana(float amount)
        {
            if (!IsOwner || _mana.Value < amount) return false;
            _mana.Value -= amount;
            return true;
        }

        /// <summary>Owner: heals an absolute amount of HP (spells).</summary>
        public void HealAmount(float hp)
        {
            if (!IsOwner || IsDead) return;
            _hp.Value = Mathf.Min(maxHealth, _hp.Value + hp);
        }

        /// <summary>Owner: rested at a flame (health, flasks, mana).</summary>
        public void RestoreAtFlame()
        {
            if (!IsOwner || IsDead) return;
            _hp.Value = maxHealth; _stamina.Value = maxStamina; _flasks.Value = maxFlasks; _mana.Value = maxMana;
        }

        /// <summary>Moltiplicatore della rigenerazione (es. più lenta mentre si para).</summary>
        public float RegenMultiplier { get; set; } = 1f;

        // ------------------------------------------------------------- API proprietario
        public bool HasStamina(float amount = 1f) => _stamina.Value >= Mathf.Min(amount, 1f);

        public void UseStamina(float amount)
        {
            if (!IsOwner) return;
            _stamina.Value = Mathf.Max(-20f, _stamina.Value - amount); // può andare un po' in negativo (penalità)
            _regenDelayTimer = staminaRegenDelay;
        }

        public void Damage(float amount, bool allowDown = true)
        {
            if (!IsOwner || IsDead || IsDowned) return;
            _hp.Value = Mathf.Max(0f, _hp.Value - amount);
            if (_hp.Value > 0f) return;
            if (allowDown && AnyAllyStanding()) GoDown();
            else Die();
        }

        /// <summary>Is any other player still on their feet (someone who could come and revive)?</summary>
        bool AnyAllyStanding()
        {
            foreach (var p in PlayerController.All)
                if (p && p.vitals != this && p.IsSpawned && !p.IsDead) return true;
            return false;
        }

        /// <summary>Development: falls to the ground even without allies (to see the downed HUD).</summary>
        public void DevGoDown() { if (IsOwner && !IsDead && !IsDowned) { _hp.Value = 0f; GoDown(); } }

        void GoDown()
        {
            _downed.Value = true; _bleed.Value = BleedOutTime; _revive.Value = 0f; _reviveState.Value = 0;
        }

        /// <summary>Owner: final death (bled out, gave up, or nobody left to help).</summary>
        public void Die()
        {
            if (!IsOwner || IsDead) return;
            _hp.Value = 0f; _downed.Value = false; _reviveState.Value = 0; _revive.Value = 0f; _dead.Value = true;
            AddDeathRpc();
        }

        /// <summary>Owner, while downed: advances the bleed-out / revive clocks. Returns true when revived.</summary>
        public bool TickDowned(float dt, bool helperInCircle)
        {
            if (!IsOwner || !IsDowned) return false;
            if (_reviveState.Value != 0) _reviveState.Value = helperInCircle ? (byte)2 : (byte)1;
            if (_reviveState.Value == 2)
            {
                _revive.Value = Mathf.Min(ReviveTime, _revive.Value + dt);
                if (_revive.Value >= ReviveTime) { ReviveUp(); return true; }
            }
            else _bleed.Value = Mathf.Max(0f, _bleed.Value - dt);
            return false;
        }

        /// <summary>Owner: an ally started reviving (pressed E next to the body).</summary>
        public void StartRevive() { if (IsOwner && IsDowned && _reviveState.Value == 0) _reviveState.Value = 1; }

        void ReviveUp()
        {
            _downed.Value = false; _reviveState.Value = 0; _revive.Value = 0f;
            _hp.Value = maxHealth * 0.35f; _stamina.Value = maxStamina * 0.5f;
        }

        // ---- scoreboard
        [Rpc(SendTo.Server)] void AddDeathRpc() => _deaths.Value++;
        [Rpc(SendTo.Server)] public void AddReviveRpc() => _revives.Value++;
        [Rpc(SendTo.Server)] public void AddKillRpc() => _kills.Value++;
        public void ServerAddKill() { if (IsServer) _kills.Value++; }

        public bool UseFlask()
        {
            if (!IsOwner || _flasks.Value <= 0 || IsDead) return false;
            _flasks.Value--;
            return true;
        }

        public void Heal(float fraction)
        {
            if (!IsOwner || IsDead) return;
            _hp.Value = Mathf.Min(maxHealth, _hp.Value + maxHealth * fraction);
        }

        public void ResetAll()
        {
            if (!IsOwner) return;
            _hp.Value = maxHealth; _stamina.Value = maxStamina; _flasks.Value = maxFlasks; _mana.Value = maxMana; _dead.Value = false;
            _downed.Value = false; _reviveState.Value = 0; _revive.Value = 0f;
        }
    }

    /// <summary>Impostazioni scelte nel menu (nome, sensibilità…), persistite in PlayerPrefs.</summary>
    public static class MenuSettings
    {
        public static string PlayerName
        {
            get => PlayerPrefs.GetString("df_name", "");
            set => PlayerPrefs.SetString("df_name", value);
        }
        public static string LastIp
        {
            get => PlayerPrefs.GetString("df_ip", "127.0.0.1");
            set => PlayerPrefs.SetString("df_ip", value);
        }
        public static int LastPort
        {
            get => PlayerPrefs.GetInt("df_port", 7777);
            set => PlayerPrefs.SetInt("df_port", value);
        }
        /// <summary>Furthest flame ever reached on this computer (1 = the throne room: the King was beaten).</summary>
        public static int UnlockedCheckpoint => PlayerPrefs.GetInt("df_cp_unlocked", 0);
        public static void UnlockCheckpoint(int i)
        {
            if (i <= UnlockedCheckpoint) return;
            PlayerPrefs.SetInt("df_cp_unlocked", i); PlayerPrefs.Save();
        }
        /// <summary>Where a new lobby starts (host): 0 = the bonfire, or an unlocked flame.</summary>
        public static int StartCheckpoint
        {
            get => Mathf.Min(PlayerPrefs.GetInt("df_start_cp", 0), UnlockedCheckpoint);
            set { PlayerPrefs.SetInt("df_start_cp", value); PlayerPrefs.Save(); }
        }
        /// <summary>Class chosen in the menu (remembered between sessions).</summary>
        public static PlayerClass PlayerClass
        {
            get => (PlayerClass)Mathf.Clamp(PlayerPrefs.GetInt("df_class", 0), 0, 2);
            set { PlayerPrefs.SetInt("df_class", (int)value); PlayerPrefs.Save(); }
        }
    }
}
