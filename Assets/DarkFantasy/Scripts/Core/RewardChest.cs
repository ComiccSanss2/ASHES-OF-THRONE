using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Reward chest that appears in the throne room after The Ashen King falls (spawned by the
    /// server, see NetworkBootstrap.ServerSpawnRewardChest). Any player can open it with Interact.
    ///
    /// The loot depends on who is in the game when it opens: the server counts the players by class
    /// and the chest raises one relic per player — as many Cinder Oath greatswords as knights and as
    /// many Thornwraith bows as archers (1 knight → 1 sword; 2 knights + 1 archer → 2 swords + 1 bow).
    /// Every client then adds its own class's relic to its inventory (equip it from the I menu).
    /// </summary>
    public class RewardChest : NetworkBehaviour, IInteractable
    {
        /// <summary>The radiant chest of the prison vault (tier 3), if the dice wanted it.</summary>
        public static RewardChest RadiantInstance { get; private set; }
        public static RewardChest Instance { get; private set; }
        /// <summary>The dragon's hoard on the terrace (tier 2), if it has appeared.</summary>
        public static RewardChest DragonInstance { get; private set; }
        /// <summary>Local player received a weapon (class, tier: 1 relic, 2 dragon) → UI message.</summary>
        public static event System.Action<PlayerClass, byte> RelicGranted;
        [Tooltip("1 = the King's relics, 2 = the dragon's weapons.")]
        public byte tier = 1;

        public Transform lidPivot;
        public Light glowLight;
        [Tooltip("Lid rotation when fully open (degrees around its hinge, local X).")]
        public float openAngle = -108f;
        public float interactDistance = 2.4f;
        public GameObject swordDisplay, bowDisplay, staffDisplay;

        readonly NetworkVariable<bool> _opened = new NetworkVariable<bool>(false);
        readonly NetworkVariable<byte> _swords = new NetworkVariable<byte>(0);
        readonly NetworkVariable<byte> _bows = new NetworkVariable<byte>(0);
        readonly NetworkVariable<byte> _staffs = new NetworkVariable<byte>(0);

        float _open;          // 0..1 lid animation
        float _spawnFx = 1f;  // appear effect
        public bool IsOpened => _opened.Value;

        public override void OnNetworkSpawn()
        {
            if (tier == Inventory.Radiant) { RadiantInstance = this; if (!Interactables.All.Contains(this)) Interactables.All.Add(this); }
            else if (tier == Inventory.Dragon) DragonInstance = this; else Instance = this;
            if (_opened.Value) _open = 1f;   // late joiner: already open
            if (glowLight) glowLight.intensity = 0f;
            Fx.Spawn(FxId.ShockDust, transform.position + Vector3.up * 0.2f);
            Fx.Spawn(FxId.Embers, transform.position + Vector3.up * 0.6f);
            Sfx.Play(SfxId.Slam, transform.position, 0.6f, 1.3f);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null; if (DragonInstance == this) DragonInstance = null;
            if (RadiantInstance == this) RadiantInstance = null;
            Interactables.All.Remove(this);
        }

        public bool LocalCanInteract(out string prompt)
        {
            prompt = null;
            var p = PlayerController.Local;
            if (!IsSpawned || p == null || !p.CanInteract || _opened.Value) return false;
            if (Vector3.Distance(p.transform.position, transform.position) > interactDistance) return false;
            prompt = tier == Inventory.Radiant ? "Open the radiant chest" : tier == Inventory.Dragon ? "Open the wyrm's hoard" : "Open the chest";
            return true;
        }

        void Update()
        {
            if (!IsSpawned) return;
            // rises out of the ground when it appears
            if (_spawnFx > 0f)
            {
                _spawnFx = Mathf.Max(0f, _spawnFx - Time.deltaTime * 0.8f);
                if (lidPivot && lidPivot.parent) lidPivot.parent.localPosition = Vector3.down * 0.9f * _spawnFx * _spawnFx;
            }
            float target = _opened.Value ? 1f : 0f;
            _open = Mathf.MoveTowards(_open, target, Time.deltaTime * 0.9f);
            float e = 1f - (1f - _open) * (1f - _open) * (1f - _open);   // ease out: heavy lid
            if (lidPivot) lidPivot.localRotation = Quaternion.Euler(openAngle * e, 0f, 0f);
            if (glowLight) glowLight.intensity = 3.2f * e * (0.9f + 0.1f * Mathf.Sin(Time.time * 7f));

            if (GameInput.InteractPressed && LocalCanInteract(out _)) OpenRpc();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void DevOpen(string _) => OpenRpc();
#endif

        [Rpc(SendTo.Server)]
        void OpenRpc()
        {
            if (_opened.Value) return;
            int swords = 0, bows = 0, staffs = 0;
            foreach (var p in PlayerController.All)
            {
                if (p == null || !p.IsSpawned) continue;
                if (p.IsArcher) bows++; else if (p.IsBard) staffs++; else swords++;
            }
            _swords.Value = (byte)swords; _bows.Value = (byte)bows; _staffs.Value = (byte)staffs;
            _opened.Value = true;
            if (tier == Inventory.Radiant) CastleProgress.Instance?.ServerRadiantFound();
            Debug.Log($"[DF] Reward chest (tier {tier}) opened: {swords} swords, {bows} bows, {staffs} mandolins");
            OpenedRpc((byte)swords, (byte)bows, (byte)staffs);
        }

        [Rpc(SendTo.Everyone)]
        void OpenedRpc(byte swords, byte bows, byte staffs)
        {
            Vector3 c = transform.position + Vector3.up * 0.6f;
            Sfx.Play(SfxId.ChestOpen, c, 1f);
            Sfx.Play(SfxId.RelicReveal, c, 0.9f, 1f, 0.6f);
            Fx.Spawn(FxId.Embers, c);
            Fx.Spawn(FxId.ParryFlash, c + Vector3.up * 0.3f);
            if (tier == Inventory.Dragon) { BossFx.FirePillar(transform.position, 1.2f, 5f); BossFx.Embers(c, 1f, 60, 5f); }
            if (tier == Inventory.Radiant) { SpellFx.Sparkles(c, 2.2f); SpellFx.GroundRing(transform.position + Vector3.up * 0.05f, 0.5f, 5f, SpellFx.Radiant, 1.2f); }
            // the relics rise out of the chest, fanned out: one per player, by class
            int n = swords + bows + staffs;
            for (int i = 0; i < n; i++)
            {
                var prefab = i < swords ? swordDisplay : i < swords + bows ? bowDisplay : staffDisplay;
                if (prefab == null) continue;
                float side = n == 1 ? 0f : Mathf.Lerp(-1f, 1f, i / (float)(n - 1));
                var go = Instantiate(prefab, transform.position + Vector3.up * 0.4f, transform.rotation);
                if (go.TryGetComponent(out RelicDisplay d)) d.Begin(transform, side * Mathf.Min(0.55f * (n - 1), 1.4f), i * 0.25f);
            }
            var local = PlayerController.Local;
            if (local != null)
            {
                Inventory.Grant(local.playerClass, tier);
                RelicGranted?.Invoke(local.playerClass, tier);
            }
        }
    }
}
