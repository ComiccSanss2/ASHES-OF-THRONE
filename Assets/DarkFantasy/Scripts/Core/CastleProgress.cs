using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Progress of the party through the castle, shared by everybody (server-written):
    ///  - the door behind the throne opens when The Ashen King falls;
    ///  - the Warden's key is picked up somewhere in the prisons;
    ///  - the great door of the prison hall is opened with it (way up to the terrace);
    ///  - the Cinder Wyrm on the terrace is slain.
    /// Also the respawn checkpoint (the furthest place the party has reached) and a few events for
    /// the UI. Spawned by the server with the world (NetworkBootstrap.SpawnWorld). Doors, the key
    /// and the checkpoint braziers are plain scene objects that read this state.
    /// </summary>
    public class CastleProgress : NetworkBehaviour
    {
        public static CastleProgress Instance { get; private set; }

        public enum Door : byte { Throne, Great, Bridge }

        readonly NetworkVariable<bool> _throneDoor = new NetworkVariable<bool>(false);
        readonly NetworkVariable<bool> _keyFound = new NetworkVariable<bool>(false);
        readonly NetworkVariable<bool> _greatDoor = new NetworkVariable<bool>(false);
        readonly NetworkVariable<bool> _dragonSlain = new NetworkVariable<bool>(false);
        readonly NetworkVariable<bool> _mirrorSlain = new NetworkVariable<bool>(false);
        readonly NetworkVariable<bool> _reaperSlain = new NetworkVariable<bool>(false);
        /// <summary>The host began this lobby from the very first flame (skills and souls reset on joining).</summary>
        readonly NetworkVariable<bool> _freshStart = new NetworkVariable<bool>(true);
        readonly NetworkVariable<bool> _radiantFound = new NetworkVariable<bool>(false);
        readonly NetworkVariable<byte> _checkpoint = new NetworkVariable<byte>(0);
        /// <summary>Flames found so far (bit per checkpoint index).</summary>
        readonly NetworkVariable<ushort> _discovered = new NetworkVariable<ushort>(1);

        public bool ThroneDoorOpen => _throneDoor.Value;
        public bool KeyFound => _keyFound.Value;
        public bool GreatDoorOpen => _greatDoor.Value;
        public bool DragonSlain => _dragonSlain.Value;
        public bool MirrorSlain => _mirrorSlain.Value;
        public bool ReaperSlain => _reaperSlain.Value;
        public bool FreshStart => _freshStart.Value;

        /// <summary>Armour of a boss protects only once that boss has fallen in this lobby (the look is always yours).</summary>
        public static bool ArmorEarned(byte set)
        {
            var p = Instance;
            if (p == null || !p.IsSpawned) return false;
            return set switch
            {
                Inventory.ArmorAshen => p.ThroneDoorOpen,
                Inventory.ArmorWyrm => p.DragonSlain,
                Inventory.ArmorMirror => p.MirrorSlain,
                Inventory.ArmorDeath => p.ReaperSlain,
                _ => false
            };
        }

        public static string ArmorCondition(byte set) => set switch
        {
            Inventory.ArmorAshen => "defeat the Ashen King in this lobby",
            Inventory.ArmorWyrm => "slay Vaelthrax in this lobby",
            Inventory.ArmorMirror => "shatter the Mirror in this lobby",
            Inventory.ArmorDeath => "end the Reaper in this lobby",
            _ => ""
        };

        public void ServerReaperSlain()
        {
            if (!IsServer || _reaperSlain.Value) return;
            _reaperSlain.Value = true;
            AnnounceRpc("DEATH IS DEAD", "The red moon closes its eye");
        }
        public bool RadiantFound => _radiantFound.Value;

        /// <summary>
        /// Has the power of this weapon tier been earned in this session? Weapons you own stay yours
        /// (the look), but their bonus only wakes once the King (relics), the wyrm (dragon weapons) or
        /// the vault's treasure (radiant) has been conquered in the lobby you are playing.
        /// </summary>
        public static bool TierEarned(byte tier)
        {
            if (tier == Inventory.BaseWeapon) return true;
            var p = Instance;
            if (p == null || !p.IsSpawned) return false;
            return tier switch
            {
                Inventory.Relic => p.ThroneDoorOpen,
                Inventory.Dragon => p.DragonSlain,
                Inventory.Radiant => p.RadiantFound,
                _ => false
            };
        }

        public static string TierCondition(byte tier) => tier switch
        {
            Inventory.Relic => "defeat the Ashen King in this lobby",
            Inventory.Dragon => "slay Vaelthrax in this lobby",
            Inventory.Radiant => "open the radiant chest in this lobby",
            _ => ""
        };

        public void ServerRadiantFound()
        {
            if (!IsServer || _radiantFound.Value) return;
            _radiantFound.Value = true;
            AnnounceRpc("RADIANT ARMS", "The vault's light wakes every radiant weapon in the party");
        }

        public void ServerMirrorSlain()
        {
            if (!IsServer || _mirrorSlain.Value) return;
            _mirrorSlain.Value = true;
            AnnounceRpc("THE MIRROR IS SHATTERED", "Every stolen reflection flies home");
            // the Mirror's heart: if the vault kept its treasure from you, the temple gives it now
            if (!_radiantFound.Value && (RewardChest.RadiantInstance == null || !RewardChest.RadiantInstance.IsSpawned))
            {
                var lv = LevelRefs.Instance;
                Vector3 at = lv && lv.mirrorSpawn ? lv.mirrorSpawn.position + lv.mirrorSpawn.forward * 4f : new Vector3(0f, -24f, 505f);
                NetworkBootstrap.Instance?.ServerSpawnRadiantChest(at, Quaternion.LookRotation(new Vector3(0f, 0f, -1f)));
            }
        }
        public int Checkpoint => _checkpoint.Value;
        public bool IsDiscovered(int i) => i == 0 || (_discovered.Value & (1 << i)) != 0;
        public static event System.Action<int> Rested;

        /// <summary>UI events (raised on every client).</summary>
        public static event System.Action<string, string> Announce;   // title, subtitle
        public static event System.Action<int> CheckpointReached;

        public static bool IsOpen(Door d)
        {
            var p = Instance;
            if (p == null || !p.IsSpawned) return false;
            return d == Door.Throne ? p.ThroneDoorOpen : d == Door.Bridge ? p.DragonSlain : p.GreatDoorOpen;
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            _discovered.OnValueChanged += OnDiscovered;
            OnDiscovered(0, _discovered.Value);
            if (IsServer) ServerApplyStart(MenuSettings.StartCheckpoint);
        }

        void OnDiscovered(ushort before, ushort now)
        {
            for (int i = 1; i < 16; i++)
            {
                int bit = 1 << i;
                if ((now & bit) == 0) continue;
                MenuSettings.UnlockCheckpoint(i);                     // remembered: a new lobby may start here
                if ((before & bit) == 0 && before != 0) CheckpointReached?.Invoke(i);
            }
        }

        /// <summary>
        /// Server, at the start of a session: a host who already got this far may begin at a later
        /// flame (the King then lies dead, the doors on the way are open).
        /// </summary>
        void ServerApplyStart(int start)
        {
            var lv = LevelRefs.Instance;
            int max = lv != null && lv.checkpoints != null ? lv.checkpoints.Length - 1 : 0;
            start = Mathf.Clamp(Mathf.Min(start, MenuSettings.UnlockedCheckpoint), 0, max);
            _freshStart.Value = start <= 0;
            if (start <= 0) return;
            _throneDoor.Value = true;
            if (start >= 3) { _keyFound.Value = true; _greatDoor.Value = true; }
            ushort bits = 1;
            for (int i = 1; i <= start; i++) bits |= (ushort)(1 << i);
            _discovered.Value = bits;
            _checkpoint.Value = (byte)start;
            BossArena.Instance?.ServerSkipDefeated();
            if (start >= 4) { _dragonSlain.Value = true; DragonArena.Instance?.ServerSkipDefeated(); }
            if (start >= 7) { _mirrorSlain.Value = true; MirrorArena.Instance?.ServerSkipDefeated(); }
            Debug.Log($"[DF] Session starts at checkpoint {start} ({(lv != null ? lv.checkpoints[start].name : "?")})");
        }

        public override void OnNetworkDespawn() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------------ server
        public void ServerOpenThroneDoor()
        {
            if (!IsServer || _throneDoor.Value) return;
            _throneDoor.Value = true;
            ServerSetCheckpoint(1);
            AnnounceRpc("THE WAY IS OPEN", "A door groans open behind the throne");
        }

        public void ServerSetCheckpoint(int index)
        {
            if (!IsServer || index < 0 || index > 15) return;
            _discovered.Value = (ushort)(_discovered.Value | (1 << index));
            if (index > _checkpoint.Value) _checkpoint.Value = (byte)index;
        }

        /// <summary>A player rests at a flame: it becomes the party's respawn point, the dead rise again.</summary>
        [Rpc(SendTo.Server)]
        public void RestRpc(byte index, RpcParams rp = default)
        {
            _discovered.Value = (ushort)(_discovered.Value | (1 << index));
            _checkpoint.Value = index;
            NetworkBootstrap.Instance?.ServerRespawnEnemies();
            Debug.Log($"[DF] {NameOf(rp.Receive.SenderClientId)} rested at flame {index}: enemies restored");
            RestedRpc(index);
        }

        [Rpc(SendTo.Everyone)]
        void RestedRpc(byte index) => Rested?.Invoke(index);

        public void ServerDragonSlain()
        {
            if (!IsServer || _dragonSlain.Value) return;
            _dragonSlain.Value = true;
            AnnounceRpc("THE BRIDGE IS OPEN", "Beyond the gate towers, a long bridge into the sunset");
            var lv = LevelRefs.Instance;
            Vector3 at = lv && lv.dragonSpawn ? lv.dragonSpawn.position - lv.dragonSpawn.forward * 2f : new Vector3(0f, 7.6f, 186f);
            Quaternion rot = Quaternion.LookRotation(new Vector3(0f, 0f, -1f));
            NetworkBootstrap.Instance?.ServerSpawnDragonChest(at, rot);
        }

        void Update()
        {
            if (!IsSpawned || !IsServer) return;
            // checkpoints: the furthest region a living player has reached
            var lv = LevelRefs.Instance;
            if (lv == null || lv.checkpoints == null) return;
            foreach (var p in PlayerController.All)
            {
                if (p == null || p.IsDead) continue;
                for (int i = lv.checkpoints.Length - 1; i >= 1; i--)
                {
                    if (IsDiscovered(i)) continue;
                    var cp = lv.checkpoints[i];
                    if (cp.reachRadius > 0f && cp.point && Vector3.Distance(p.transform.position, cp.point.position) < cp.reachRadius
                        && (i != 1 || _throneDoor.Value))
                    { ServerSetCheckpoint(i); break; }
                }
            }
        }

        // ------------------------------------------------------------------ requests from players
        [Rpc(SendTo.Server)]
        public void TakeKeyRpc(RpcParams rp = default)
        {
            if (_keyFound.Value) return;
            _keyFound.Value = true;
            string who = NameOf(rp.Receive.SenderClientId);
            Debug.Log($"[DF] Warden's key taken by {who}");
            AnnounceRpc("WARDEN'S KEY", $"{who} found the key of the great door");
            KeyTakenFxRpc();
        }

        [Rpc(SendTo.Server)]
        public void OpenGreatDoorRpc(RpcParams rp = default)
        {
            if (!_keyFound.Value || _greatDoor.Value) return;
            _greatDoor.Value = true;
            Debug.Log("[DF] Great door opened");
            AnnounceRpc("THE GREAT DOOR", "The stairs climb toward the ramparts");
        }

        static string NameOf(ulong clientId)
        {
            foreach (var p in PlayerController.All)
                if (p && p.OwnerClientId == clientId) return p.DisplayName;
            return "A knight";
        }

        [Rpc(SendTo.Everyone)]
        void AnnounceRpc(string title, string sub) => Announce?.Invoke(title, sub);

        [Rpc(SendTo.Everyone)]
        void KeyTakenFxRpc()
        {
            var k = KeyPickup.Instance;
            Vector3 p = k ? k.transform.position : transform.position;
            Sfx.Play(SfxId.RelicReveal, p, 0.9f, 1.15f);
            Fx.Spawn(FxId.Embers, p);
        }

        /// <summary>Development (editor tools, F9 menu): "throne" | "key" | "great" | "cp N".</summary>
        public void DevProgress(string what)
        {
            if (!IsServer) return;
            if (what == "throne") ServerOpenThroneDoor();
            else if (what == "key") TakeKeyRpc();
            else if (what == "great") { _keyFound.Value = true; OpenGreatDoorRpc(); }
            else if (what.StartsWith("cp ")) ServerSetCheckpoint(int.Parse(what.Substring(3)));
            else if (what == "dragon" && !_dragonSlain.Value) { _dragonSlain.Value = true; DragonArena.Instance?.ServerSkipDefeated(); }
            else if (what == "mirror" && !_mirrorSlain.Value) { _mirrorSlain.Value = true; MirrorArena.Instance?.ServerSkipDefeated(); }
        }
    }
}
