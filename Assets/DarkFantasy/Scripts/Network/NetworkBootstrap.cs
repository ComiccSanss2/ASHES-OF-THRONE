using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.Networking;

namespace DarkFantasy
{
    /// <summary>
    /// Avvio della sessione multiplayer LAN / IP diretto (Netcode for GameObjects + Unity Transport).
    ///  - Host: ascolta su 0.0.0.0:porta, mostra IP locali (e pubblico, se raggiungibile).
    ///  - Join: si connette a IP:porta inseriti nel menu.
    ///  - Il server (host) genera nemici, boss e arena; il prefab giocatore è spawnato da NGO.
    /// Per giocare via Internet l'host deve aprire/inoltrare la porta UDP sul router.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class NetworkBootstrap : MonoBehaviour
    {
        public static NetworkBootstrap Instance { get; private set; }

        [Header("Prefab di rete")]
        public GameObject playerPrefab;
        [Tooltip("Player prefab of the Archer class (the Knight is playerPrefab).")]
        public GameObject archerPrefab;
        public GameObject bardPrefab;
        public GameObject guardianPrefab;
        public GameObject bossPrefab;
        public GameObject arenaPrefab;
        public GameObject lobbyPrefab;
        [Tooltip("Reward chest spawned in the throne room when the boss falls.")]
        public GameObject chestPrefab;
        [Tooltip("The wyrm's hoard: chest of dragon weapons, spawned on the terrace when Vaelthrax falls.")]
        public GameObject dragonChestPrefab;
        [Header("Castle")]
        public GameObject progressPrefab;
        public GameObject gaolerPrefab;
        public GameObject bowmanPrefab;
        public GameObject wardenPrefab;
        public GameObject dragonPrefab;
        public GameObject dragonArenaPrefab;
        [Header("Beyond the bridge")]
        public GameObject radiantChestPrefab;
        [Tooltip("Chance, rolled once when the world is created, that the prison vault holds the radiant weapons.")]
        [Range(0, 1)] public float radiantChance = 0.35f;
        public GameObject wraithPrefab, revenantPrefab, mirrorPrefab, mirrorArenaPrefab;

        public ushort defaultPort = 7777;
        public int maxPlayers = 4;

        public NetworkManager Manager { get; private set; }
        public UnityTransport Transport { get; private set; }
        public string PublicIp { get; private set; } = "…";
        public event Action<string> StatusChanged;
        public event Action SessionStarted;
        public event Action SessionEnded;

        bool _worldSpawned;
        public string PendingLobbyName { get; private set; } = "Lobby";
        public ushort Port { get; private set; } = 7777;
        public bool GameStarted => LobbyState.Instance != null && LobbyState.Instance.Started.Value;

        void Awake()
        {
            Instance = this;
            Manager = GetComponent<NetworkManager>();
            Transport = GetComponent<UnityTransport>();
            if (Manager.NetworkConfig == null) Manager.NetworkConfig = new NetworkConfig();
            var cfg = Manager.NetworkConfig;
            cfg.NetworkTransport = Transport;
            cfg.PlayerPrefab = playerPrefab;
            cfg.EnableSceneManagement = false;       // scena unica già caricata su tutti
            cfg.ConnectionApproval = true;
            // I prefab di rete (nemici, boss, arena) sono registrati dal builder in una
            // NetworkPrefabsList assegnata al NetworkManager; il giocatore è PlayerPrefab.
            if (cfg.Prefabs.NetworkPrefabsLists.Count == 0)
                Debug.LogWarning("[DF] No NetworkPrefabsList assigned: run 'Dark Fantasy/Build Everything'.");

            Manager.ConnectionApprovalCallback = ApproveConnection;
            Manager.OnServerStarted += OnServerStarted;
            Manager.OnClientConnectedCallback += OnClientConnected;
            Manager.OnClientDisconnectCallback += OnClientDisconnected;
            Application.runInBackground = true;
            if (!GetComponent<LanDiscovery>()) gameObject.AddComponent<LanDiscovery>();
        }

        void Start()
        {
            StartCoroutine(FetchPublicIp());
            StartCoroutine(AutoStartFromCommandLine());
        }

        /// <summary>
        /// Avvio rapido senza passare dal menu (utile per test e per un host "fisso"):
        ///   DarkFantasy.exe -host [-port 7777]
        ///   DarkFantasy.exe -join 192.168.1.10 [-port 7777]
        /// oppure, solo nella build, un file "autohost.txt" accanto all'eseguibile avvia l'host.
        /// </summary>
        IEnumerator AutoStartFromCommandLine()
        {
            yield return null; // lascia costruire la UI
            var args = Environment.GetCommandLineArgs();
            ushort port = defaultPort;
            string join = null;
            bool host = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-host") host = true;
                else if (args[i] == "-online") { StartOnlineHost(args.Length > i + 1 && !args[i + 1].StartsWith("-") ? args[i + 1] : "Online Test", maxPlayers); yield break; }
                else if (args[i] == "-join" && i + 1 < args.Length) join = args[i + 1];
                else if (args[i] == "-port" && i + 1 < args.Length) ushort.TryParse(args[i + 1], out port);
            }
            if (!Application.isEditor)
            {
                string flag = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath) ?? ".", "autohost.txt");
                if (System.IO.File.Exists(flag)) host = true;
            }
            if (host)
            {
                // quick start for tests / dedicated hosts: lobby + immediate start
                if (StartHost(port, "Quick Game", maxPlayers))
                {
                    yield return new WaitForSeconds(0.5f);
                    if (LobbyState.Instance) LobbyState.Instance.RequestStartRpc();
                }
            }
            else if (!string.IsNullOrEmpty(join)) StartClient(join, port);
        }

        void OnDestroy()
        {
            if (Manager == null) return;
            Manager.OnServerStarted -= OnServerStarted;
            Manager.OnClientConnectedCallback -= OnClientConnected;
            Manager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        // ------------------------------------------------------------------ API menu
        public bool StartHost(ushort port) => StartHost(port, "Lobby", maxPlayers);

        /// <summary>Creates a lobby: the host waits there with the others until "Begin the Journey".</summary>
        public bool StartHost(ushort port, string lobbyName, int max)
        {
            PendingLobbyName = string.IsNullOrWhiteSpace(lobbyName) ? "Lobby" : lobbyName.Trim();
            maxPlayers = Mathf.Clamp(max, 1, 4);
            Port = port;
            Transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            _worldSpawned = false;
            bool ok = Manager.StartHost();
            SetStatus(ok ? $"Lobby open on port {port}" : "Could not start the host (port in use?)");
            if (ok)
            {
                SessionStarted?.Invoke();
                var lan = GetComponent<LanDiscovery>();
                if (lan) lan.StartBroadcast(() =>
                {
                    var ls = LobbyState.Instance;
                    string host = string.IsNullOrWhiteSpace(MenuSettings.PlayerName) ? "Knight" : MenuSettings.PlayerName;
                    return LanDiscovery.MakeBeacon(ls ? ls.DisplayLobbyName : PendingLobbyName, host, Port,
                        Manager.ConnectedClientsIds.Count, maxPlayers, ls && ls.Started.Value);
                });
            }
            return ok;
        }

        public bool StartClient(string ip, ushort port)
        {
            ip = string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip.Trim();
            if (!IPAddress.TryParse(ip, out _))
            {
                // supporto ai nomi host (es. "pc-salotto.local")
                try { var addrs = Dns.GetHostAddresses(ip); foreach (var a in addrs) if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a.ToString(); break; } }
                catch { SetStatus("Invalid address"); return false; }
            }
            Transport.SetConnectionData(ip, port);
            Port = port;
            bool ok = Manager.StartClient();
            SetStatus(ok ? $"Connecting to {ip}:{port}…" : "Could not start the client");
            if (ok) StartCoroutine(ConnectTimeout());
            return ok;
        }

        /// <summary>Creates a public online lobby (Unity Lobby + Relay): visible to everyone, no ports to open.</summary>
        public async void StartOnlineHost(string lobbyName, int max)
        {
            if (Manager.IsListening || _onlineBusy) return;
            _onlineBusy = true;
            PendingLobbyName = string.IsNullOrWhiteSpace(lobbyName) ? "Lobby" : lobbyName.Trim();
            maxPlayers = Mathf.Clamp(max, 1, 4);
            _worldSpawned = false;
            SetStatus("Opening the online lobby…");
            string host = string.IsNullOrWhiteSpace(MenuSettings.PlayerName) ? "Knight" : MenuSettings.PlayerName;
            bool ok = await OnlineLobby.CreateAsync(PendingLobbyName, maxPlayers, host);
            _onlineBusy = false;
            if (ok && Manager.IsListening)
            {
                SetStatus("Online lobby open");
                SessionStarted?.Invoke();
            }
            else
            {
                if (ok) OnlineLobby.Leave();
                SetStatus(string.IsNullOrEmpty(OnlineLobby.LastError) ? "Could not open the online lobby" : OnlineLobby.LastError);
            }
        }

        /// <summary>Joins a public online lobby found in the browser.</summary>
        public async void JoinOnline(string sessionId)
        {
            if (Manager.IsListening || _onlineBusy) return;
            _onlineBusy = true;
            SetStatus("Joining…");
            bool ok = await OnlineLobby.JoinAsync(sessionId);
            _onlineBusy = false;
            if (ok) StartCoroutine(ConnectTimeout());
            else SetStatus(string.IsNullOrEmpty(OnlineLobby.LastError) ? "Could not join" : OnlineLobby.LastError);
        }

        bool _onlineBusy;
        public bool OnlineBusy => _onlineBusy;

        public void Shutdown()
        {
            OnlineLobby.Leave();
            if (Manager != null && Manager.IsListening) Manager.Shutdown();
            GetComponent<LanDiscovery>()?.StopBroadcast();
            _worldSpawned = false;
            SessionEnded?.Invoke();
        }

        IEnumerator ConnectTimeout()
        {
            float t = 0f;
            while (t < 10f)
            {
                if (Manager.IsConnectedClient) { SessionStarted?.Invoke(); yield break; }
                if (!Manager.IsListening) yield break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            SetStatus("Timed out: host unreachable");
            Shutdown();
        }

        void ApproveConnection(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse resp)
        {
            int count = Manager.ConnectedClientsIds.Count;
            resp.Approved = count < maxPlayers;
            // in the lobby nobody has a body yet; late joiners are spawned (with their class)
            // as soon as they register in the LobbyState (LobbyState.RegisterRpc)
            resp.CreatePlayerObject = lobbyPrefab == null && GameStarted;
            resp.Reason = resp.Approved ? "" : "The lobby is full";
        }

        // ------------------------------------------------------------------ server
        void OnServerStarted()
        {
            if (lobbyPrefab != null) SpawnNet(lobbyPrefab, Vector3.zero, Quaternion.identity);
            else ServerStartGame();   // no lobby prefab (old scene): start immediately
        }

        /// <summary>Server: spawns the world and a knight for everyone in the lobby.</summary>
        public void ServerStartGame()
        {
            if (!Manager.IsServer) return;
            SpawnWorld();
            foreach (var id in Manager.ConnectedClientsIds)
                ServerSpawnPlayer(id, LobbyState.Instance ? LobbyState.Instance.ClassOf(id) : PlayerClass.Knight);
            OnlineLobby.MarkStarted();
            Debug.Log("[DF] The journey begins");
        }

        /// <summary>Server: gives a client its character (Knight or Archer), once.</summary>
        public void ServerSpawnPlayer(ulong clientId, PlayerClass cls)
        {
            if (!Manager.IsServer) return;
            if (!Manager.ConnectedClients.TryGetValue(clientId, out var cc) || cc.PlayerObject != null) return;
            var prefab = cls == PlayerClass.Archer && archerPrefab != null ? archerPrefab
                       : cls == PlayerClass.Bard && bardPrefab != null ? bardPrefab : playerPrefab;
            if (prefab == null) return;
            Instantiate(prefab).GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
            Debug.Log($"[DF] client {clientId} enters as {cls}");
        }

        /// <summary>Server: the reward chest rises in front of the throne a few seconds after the boss dies.</summary>
        public void ServerSpawnRewardChest(float delay = 3f)
        {
            if (!Manager.IsServer || chestPrefab == null || RewardChest.Instance != null) return;
            StartCoroutine(SpawnChestLater(delay));
        }

        IEnumerator SpawnChestLater(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (RewardChest.Instance != null || !Manager.IsServer) yield break;
            var lv = LevelRefs.Instance;
            Transform anchor = lv ? lv.bossSpawn : null;
            Vector3 pos = anchor ? anchor.position + anchor.forward * 4f : Vector3.zero;
            Quaternion rot = anchor ? Quaternion.LookRotation(MathUtil.Flat(anchor.forward)) : Quaternion.identity;
            if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 10f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                pos = hit.point;
            SpawnNet(chestPrefab, pos, rot);
            Debug.Log("[DF] Reward chest spawned at " + pos);
        }

        /// <summary>Server: when the dragon dies, its hoard (a chest of dragon weapons) rises on the terrace.</summary>
        public void ServerSpawnDragonChest(Vector3 pos, Quaternion rot, float delay = 4f)
        {
            if (!Manager.IsServer || dragonChestPrefab == null || RewardChest.DragonInstance != null) return;
            StartCoroutine(SpawnDragonChestLater(pos, rot, delay));
        }

        IEnumerator SpawnDragonChestLater(Vector3 pos, Quaternion rot, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (RewardChest.DragonInstance != null || !Manager.IsServer) yield break;
            if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 10f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                pos = hit.point;
            SpawnNet(dragonChestPrefab, pos, rot);
            Debug.Log("[DF] Dragon hoard spawned at " + pos);
        }

        /// <summary>Server: the radiant chest (prison vault, or anywhere for the F9 admin tool).</summary>
        public void ServerSpawnRadiantChest(Vector3 pos, Quaternion rot)
        {
            if (!Manager.IsServer || radiantChestPrefab == null) return;
            if (RewardChest.RadiantInstance != null && RewardChest.RadiantInstance.IsSpawned) RewardChest.RadiantInstance.NetworkObject.Despawn(true);
            if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out var hit, 6f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) pos = hit.point;
            SpawnNet(radiantChestPrefab, pos, rot);
            Debug.Log("[DF] Radiant chest spawned at " + pos);
        }

        void SpawnWorld()
        {
            if (_worldSpawned) return;
            _worldSpawned = true;
            var lv = LevelRefs.Instance;
            if (lv == null) { Debug.LogWarning("[DF] LevelRefs missing: no enemies spawned"); return; }

            _respawnable.Clear();
            if (guardianPrefab != null && lv.enemySpawns != null)
                foreach (var sp in lv.enemySpawns)
                    if (sp) SpawnEnemy(guardianPrefab, sp.position, sp.rotation);
            if (bossPrefab != null && lv.bossSpawn) SpawnNet(bossPrefab, lv.bossSpawn.position, lv.bossSpawn.rotation);
            if (arenaPrefab != null && lv.arenaRoot) SpawnNet(arenaPrefab, lv.arenaRoot.position, lv.arenaRoot.rotation);

            // ---- castle chapter
            if (progressPrefab != null) SpawnNet(progressPrefab, Vector3.zero, Quaternion.identity);
            void Many(GameObject prefab, Transform[] pts) { if (prefab != null && pts != null) foreach (var sp in pts) if (sp) SpawnEnemy(prefab, sp.position, sp.rotation); }
            Many(gaolerPrefab, lv.castleKnights);
            Many(bowmanPrefab, lv.castleArchers);
            Many(wardenPrefab, lv.wardens);
            if (dragonPrefab != null && lv.dragonSpawn) SpawnNet(dragonPrefab, lv.dragonSpawn.position, lv.dragonSpawn.rotation);
            if (dragonArenaPrefab != null && lv.dragonGate) SpawnNet(dragonArenaPrefab, lv.dragonGate.position, lv.dragonGate.rotation);

            // ---- the Thornwood and the Glass Temple
            Many(wraithPrefab, lv.shardWraiths);
            Many(revenantPrefab, lv.glassRevenants);
            if (mirrorPrefab != null && lv.mirrorSpawn) SpawnNet(mirrorPrefab, lv.mirrorSpawn.position, lv.mirrorSpawn.rotation);
            if (mirrorArenaPrefab != null && lv.mirrorGate) SpawnNet(mirrorArenaPrefab, lv.mirrorGate.position, lv.mirrorGate.rotation);

            // ---- the dice: does the forgotten vault hold the radiant weapons this time?
            float roll = UnityEngine.Random.value;
            bool lucky = roll < radiantChance;
            Debug.Log($"[DF] Vault dice: {roll:0.00} (needs < {radiantChance:0.00}) → {(lucky ? "the radiant chest waits in the vault" : "the vault is empty this time")}");
            if (lucky && lv.vaultChest) ServerSpawnRadiantChest(lv.vaultChest.position, lv.vaultChest.rotation);
            Debug.Log("[DF] World spawned by the server");
        }

        static NetworkObject SpawnNet(GameObject prefab, Vector3 pos, Quaternion rot)
        {
            var go = Instantiate(prefab, pos, rot);
            var no = go.GetComponent<NetworkObject>();
            no.Spawn(true);
            return no;
        }

        // ordinary enemies, restored when someone rests at a flame (not the bosses)
        class Respawnable { public GameObject prefab; public Vector3 pos; public Quaternion rot; public NetworkObject inst; }
        readonly System.Collections.Generic.List<Respawnable> _respawnable = new System.Collections.Generic.List<Respawnable>();

        void SpawnEnemy(GameObject prefab, Vector3 pos, Quaternion rot)
            => _respawnable.Add(new Respawnable { prefab = prefab, pos = pos, rot = rot, inst = SpawnNet(prefab, pos, rot) });

        /// <summary>Server: every ordinary enemy is back at its post, at full health (dead or alive).</summary>
        public void ServerRespawnEnemies()
        {
            if (!Manager.IsServer) return;
            int n = 0;
            foreach (var r in _respawnable)
            {
                if (r.inst != null && r.inst.IsSpawned) r.inst.Despawn(true);
                r.inst = SpawnNet(r.prefab, r.pos, r.rot);
                n++;
            }
            Debug.Log($"[DF] {n} enemies restored");
        }

        void OnClientConnected(ulong id)
        {
            if (Manager.IsServer) SetStatus($"Players connected: {Manager.ConnectedClientsIds.Count}");
            else if (id == Manager.LocalClientId) SetStatus("Connected");
        }

        void OnClientDisconnected(ulong id)
        {
            if (Manager.IsServer)
            {
                if (id != Manager.LocalClientId) SetStatus($"A player left ({Manager.ConnectedClientsIds.Count} remaining)");
                LobbyState.Instance?.ServerRemove(id);
                return;
            }
            // siamo un client: l'host ci ha lasciato o rifiutato
            string reason = Manager.DisconnectReason;
            if (string.IsNullOrEmpty(reason) || reason.StartsWith("[Disconnect Event]"))
                reason = reason != null && reason.Contains("Timeout") ? "Lost connection to the host" : "Disconnected from the host";
            SetStatus(reason);
            OnlineLobby.Leave();
            _worldSpawned = false;
            SessionEnded?.Invoke();
        }

        void SetStatus(string s)
        {
            Debug.Log("[DF] " + s);
            StatusChanged?.Invoke(s);
        }

        // ------------------------------------------------------------------ IP
        public static List<string> GetLocalIPv4()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                            list.Add(ua.Address.ToString());
                }
            }
            catch
            {
                try { foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList) if (a.AddressFamily == AddressFamily.InterNetwork) list.Add(a.ToString()); }
                catch { /* ignora */ }
            }
            if (list.Count == 0) list.Add("127.0.0.1");
            return list;
        }

        IEnumerator FetchPublicIp()
        {
            using (var req = UnityWebRequest.Get("https://api.ipify.org"))
            {
                req.timeout = 5;
                yield return req.SendWebRequest();
                PublicIp = req.result == UnityWebRequest.Result.Success ? req.downloadHandler.text.Trim() : "unavailable";
            }
        }
    }
}
