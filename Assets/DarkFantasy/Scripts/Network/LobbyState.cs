using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    public struct LobbyMember : INetworkSerializable, IEquatable<LobbyMember>
    {
        public ulong clientId;
        public FixedString32Bytes name;
        public byte playerClass;   // PlayerClass

        public PlayerClass Class => (PlayerClass)playerClass;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref clientId);
            s.SerializeValue(ref name);
            s.SerializeValue(ref playerClass);
        }

        public bool Equals(LobbyMember other) => clientId == other.clientId && name.Equals(other.name) && playerClass == other.playerClass;
    }

    /// <summary>
    /// The pre-game lobby (spawned by the host when the session starts): lobby name, size and the
    /// list of knights who joined. The host presses "Begin the Journey" → the world and the players
    /// are spawned. Players who join after the start go straight into the game.
    /// </summary>
    public class LobbyState : NetworkBehaviour
    {
        public static LobbyState Instance { get; private set; }
        public static event Action Changed;
        public static event Action GameStarted;

        public readonly NetworkList<LobbyMember> Members = new NetworkList<LobbyMember>();
        public readonly NetworkVariable<FixedString64Bytes> LobbyName = new NetworkVariable<FixedString64Bytes>();
        public readonly NetworkVariable<byte> MaxPlayers = new NetworkVariable<byte>(4);
        public readonly NetworkVariable<bool> Started = new NetworkVariable<bool>(false);

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Members.OnListChanged += _ => Changed?.Invoke();
            LobbyName.OnValueChanged += (a, b) => Changed?.Invoke();
            Started.OnValueChanged += (a, b) => { Changed?.Invoke(); if (b) GameStarted?.Invoke(); };
            if (IsServer)
            {
                var nb = NetworkBootstrap.Instance;
                LobbyName.Value = new FixedString64Bytes(nb ? nb.PendingLobbyName : "Lobby");
                MaxPlayers.Value = (byte)(nb ? nb.maxPlayers : 4);
            }
            string n = string.IsNullOrWhiteSpace(MenuSettings.PlayerName) ? "Knight " + (NetworkManager.LocalClientId + 1) : MenuSettings.PlayerName;
            RegisterRpc(new FixedString32Bytes(n.Length > 28 ? n.Substring(0, 28) : n), (byte)MenuSettings.PlayerClass);
            Changed?.Invoke();
            if (Started.Value) GameStarted?.Invoke();   // joined a game already in progress
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            Changed?.Invoke();
        }

        [Rpc(SendTo.Server)]
        void RegisterRpc(FixedString32Bytes name, byte playerClass, RpcParams p = default)
        {
            ulong id = p.Receive.SenderClientId;
            ServerRemove(id);
            Members.Add(new LobbyMember { clientId = id, name = name, playerClass = playerClass });
            // joined a game already in progress: spawn straight in, with the chosen class
            if (Started.Value) NetworkBootstrap.Instance?.ServerSpawnPlayer(id, (PlayerClass)playerClass);
        }

        /// <summary>Changes the local player's class while waiting in the lobby.</summary>
        public void SetLocalClass(PlayerClass c)
        {
            MenuSettings.PlayerClass = c;
            if (IsSpawned && !Started.Value) SetClassRpc((byte)c);
        }

        [Rpc(SendTo.Server)]
        void SetClassRpc(byte playerClass, RpcParams p = default)
        {
            if (Started.Value) return;
            ulong id = p.Receive.SenderClientId;
            for (int i = 0; i < Members.Count; i++)
                if (Members[i].clientId == id) { var m = Members[i]; m.playerClass = playerClass; Members[i] = m; }
        }

        /// <summary>Server: class chosen by a client (Knight if unknown).</summary>
        public PlayerClass ClassOf(ulong clientId)
        {
            foreach (var m in Members) if (m.clientId == clientId) return m.Class;
            return PlayerClass.Knight;
        }

        public void ServerRemove(ulong id)
        {
            if (!IsServer) return;
            for (int i = Members.Count - 1; i >= 0; i--)
                if (Members[i].clientId == id) Members.RemoveAt(i);
        }

        /// <summary>Host only: closes the lobby and starts the game for everyone.</summary>
        [Rpc(SendTo.Server)]
        public void RequestStartRpc(RpcParams p = default)
        {
            if (p.Receive.SenderClientId != NetworkManager.ServerClientId || Started.Value) return;
            Started.Value = true;
            NetworkBootstrap.Instance?.ServerStartGame();
        }

        public string DisplayLobbyName => LobbyName.Value.Length > 0 ? LobbyName.Value.ToString() : "Lobby";
    }
}
