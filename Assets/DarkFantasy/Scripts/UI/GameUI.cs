using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DarkFantasy.UI
{
    /// <summary>
    /// Tutta l'interfaccia del gioco, costruita a runtime con il kit Ravenhollow:
    /// menu principale (Host / Join via IP), HUD (vita, stamina, fiaschette), barre del gruppo,
    /// barra del boss, barre sopra i nemici, mirino del lock-on, prompt d'interazione,
    /// messaggi "SEI MORTO" / "NEMESI ABBATTUTA", menu di pausa con impostazioni, guida comandi.
    /// </summary>
    public partial class GameUI : MonoBehaviour
    {
        public UISkin skin;
        public string gameTitle = "Ashes of the Throne";

        Canvas _canvas;
        RectTransform _root, _menu, _hud, _pause, _options, _help, _promptRt, _messageRt, _overheadRoot;
        Text _statusText, _ipText, _promptText, _messageText, _messageSub, _bossName, _flaskText, _sessionInfo;
        InputField _nameField, _ipField, _portField;
        UIBar _hpBar, _staminaBar, _bossBar;
        RectTransform _bossRoot, _partyRoot, _reticle, _crosshair;
        Image _crosshairImg;
        Text _classText, _lobbyClassText;
        readonly Dictionary<PlayerController, (RectTransform root, Text name, UIBar bar)> _party = new Dictionary<PlayerController, (RectTransform, Text, UIBar)>();
        readonly Dictionary<EnemyBase, (RectTransform root, UIBar bar)> _overhead = new Dictionary<EnemyBase, (RectTransform, UIBar)>();
        float _messageTimer, _messageDuration;
        bool _paused;
        float _lastSfxPreview;
        Toggle _fullscreenToggle;

        const float W = 960f, H = 540f;

        void Awake()
        {
            UIFactory.Skin = skin;
            BuildCanvas();
            BuildMenu();
            BuildHud();
            BuildPause();
            BuildOptions();
            BuildHelp();
            BuildInventory();
            BuildSkills();
            BuildMessage();
            BuildDevMenu();
            ShowMenu(true);
            if (skin && skin.cursor) Cursor.SetCursor(skin.cursor, skin.cursorHotspot, CursorMode.Auto);
        }

        void OnEnable()
        {
            PlayerController.LocalDied += OnLocalDied;
            PlayerVitals.PvpChanged += OnPvpChanged;
            PlayerController.PvpKill += OnPvpKill;
            BossArena.FightStarted += OnFightStarted;
            BossArena.BossDefeated += OnBossDefeated;
            BossArena.FightReset += OnFightReset;
            BossAI.Ignited += OnBossIgnited;
            DragonArena.FightStarted += OnDragonStarted;
            DragonArena.Defeated += OnDragonDefeated;
            DragonArena.FightReset += OnFightReset;
            DragonAI.Enraged += OnDragonEnraged;
            MirrorArena.FightStarted += OnMirrorStarted;
            MirrorArena.Defeated += OnMirrorDefeated;
            MirrorArena.FightReset += OnFightReset;
            MirrorAI.Awakened += OnMirrorAwakened;
            ReaperArena.FightStarted += OnReaperStarted;
            ReaperArena.Defeated += OnReaperDefeated;
            ReaperArena.FightReset += OnFightReset;
            ReaperBossAI.Judgement += OnReaperJudgement;
            CastleProgress.Announce += OnAnnounce;
            CastleProgress.CheckpointReached += OnCheckpoint;
            CastleProgress.Rested += OnRested;
            RewardChest.RelicGranted += OnRelicGranted;
            LootPickup.Looted += OnLooted;
            Inventory.Changed += RefreshInventory;
        }

        void OnDisable()
        {
            PlayerController.LocalDied -= OnLocalDied;
            OnDestroyChat();
            PlayerVitals.PvpChanged -= OnPvpChanged;
            PlayerController.PvpKill -= OnPvpKill;
            BossArena.FightStarted -= OnFightStarted;
            BossArena.BossDefeated -= OnBossDefeated;
            BossArena.FightReset -= OnFightReset;
            BossAI.Ignited -= OnBossIgnited;
            DragonArena.FightStarted -= OnDragonStarted;
            DragonArena.Defeated -= OnDragonDefeated;
            DragonArena.FightReset -= OnFightReset;
            DragonAI.Enraged -= OnDragonEnraged;
            MirrorArena.FightStarted -= OnMirrorStarted;
            MirrorArena.Defeated -= OnMirrorDefeated;
            MirrorArena.FightReset -= OnFightReset;
            MirrorAI.Awakened -= OnMirrorAwakened;
            ReaperArena.FightStarted -= OnReaperStarted;
            ReaperArena.Defeated -= OnReaperDefeated;
            ReaperArena.FightReset -= OnFightReset;
            ReaperBossAI.Judgement -= OnReaperJudgement;
            CastleProgress.Announce -= OnAnnounce;
            CastleProgress.CheckpointReached -= OnCheckpoint;
            CastleProgress.Rested -= OnRested;
            RewardChest.RelicGranted -= OnRelicGranted;
            LootPickup.Looted -= OnLooted;
            Inventory.Changed -= RefreshInventory;
        }

        void Start()
        {
            var nb = NetworkBootstrap.Instance;
            if (nb != null)
            {
                nb.StatusChanged += s => { if (_statusText) _statusText.text = s; if (_directStatus) _directStatus.text = s; if (_createStatus) _createStatus.text = s; _lastStatus = s; _lastStatusAt = Time.unscaledTime; };
                // connected → lobby room; the HUD appears when the host begins the journey
                nb.SessionStarted += () => { if (!(NetworkBootstrap.Instance && NetworkBootstrap.Instance.GameStarted)) ShowScreen(_scrLobby); };
                LobbyState.GameStarted += () => ShowMenu(false);
                nb.SessionEnded += () => { ShowMenu(true); ShowScreen(_scrMain); _paused = false; _pause.gameObject.SetActive(false); _options.gameObject.SetActive(false); };
            }
        }

        // ================================================================== costruzione
        void BuildCanvas()
        {
            var go = new GameObject("GameUI_Canvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;
            _canvas.pixelPerfect = true;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(W, H);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            scaler.referencePixelsPerUnit = 100;
            go.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)go.transform;

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }
        }

        // ---- menu screens (main / create lobby / browser / join by IP / lobby room)
        RectTransform _scrMain, _scrCreate, _scrBrowse, _scrDirect, _scrLobby;
        InputField _lobbyNameField, _createPortField;
        Text _createStatus, _visText;
        bool _createOnline = true;
        string _lastStatus = "";
        float _lastStatusAt = -99f;
        // browser rows: online lobbies first, then the ones found on the local network
        readonly List<(bool online, int index)> _browseMap = new List<(bool, int)>();
        Text _maxText, _browseStatus, _lobbyTitle, _lobbyInfo, _lobbyWait, _directStatus;
        readonly List<(Button btn, Text label)> _browseRows = new List<(Button, Text)>();
        readonly List<Text> _lobbyRows = new List<Text>();
        Button _beginButton;
        int _createMax = 4;

        void BuildMenu()
        {
            _menu = UIFactory.Stretch("MainMenu", _root);
            UIFactory.Image(UIFactory.Stretch("Veil", _menu), null, false, new Color(0, 0, 0, 0.35f));

            var title = UIFactory.Label(_menu, gameTitle, new Vector2(0.5f, 1f), new Vector2(0, -52), new Vector2(800, 60), 48, skin.bone0, TextAnchor.MiddleCenter, true);
            title.GetComponent<Shadow>().effectDistance = new Vector2(2, -2);
            UIFactory.Label(_menu, "a cooperative action RPG", new Vector2(0.5f, 1f), new Vector2(0, -88), new Vector2(600, 20), 16, skin.bone3);

            // ---------------------------------------------------------- main
            {
                _scrMain = MakeScreen("Gather the Fellowship", new Vector2(340, 400), out var c);
                float y = -6;
                UIFactory.Label(c, "Name", new Vector2(0.5f, 1f), new Vector2(-104, y - 12), new Vector2(84, 22), 16, skin.bone3, TextAnchor.MiddleLeft);
                _nameField = UIFactory.Field(c, MenuSettings.PlayerName, "Nameless Knight", new Vector2(0.5f, 1f), new Vector2(38, y - 12), 196);
                _nameField.characterLimit = 20;
                y -= 32;
                UIFactory.Label(c, "Class", new Vector2(0.5f, 1f), new Vector2(-104, y - 12), new Vector2(84, 22), 16, skin.bone3, TextAnchor.MiddleLeft);
                var clsBtn = UIFactory.Button(c, "", new Vector2(0.5f, 1f), new Vector2(38, y - 12), 196, null, true);
                _classText = clsBtn.GetComponentInChildren<Text>();
                clsBtn.onClick.AddListener(ToggleClass);
                y -= 42;
                UIFactory.Button(c, "Create Lobby", new Vector2(0.5f, 1f), new Vector2(0, y - 14), 220, () => { SaveFields(); OpenCreate(); }); y -= 34;
                UIFactory.Button(c, "Find Lobbies", new Vector2(0.5f, 1f), new Vector2(0, y - 14), 220, () => { SaveFields(); ShowScreen(_scrBrowse); }); y -= 34;
                UIFactory.Button(c, "Join by IP", new Vector2(0.5f, 1f), new Vector2(0, y - 14), 220, () => { SaveFields(); ShowScreen(_scrDirect); }); y -= 40;
                UIFactory.Button(c, "Options", new Vector2(0.5f, 1f), new Vector2(-66, y - 14), 120, () => ShowOptions(true), true);
                UIFactory.Button(c, "Quit", new Vector2(0.5f, 1f), new Vector2(66, y - 14), 120, Quit, true);
                y -= 32;
                _statusText = UIFactory.Label(c, "Create a lobby, or find your friends' one.", new Vector2(0.5f, 1f), new Vector2(0, y - 12), new Vector2(290, 30), 14, skin.bone3);
                y -= 34;
                UIFactory.Button(c, "Change Character", new Vector2(0.5f, 1f), new Vector2(0, y - 14), 200, () => { SaveFields(); ShowScreen(_scrChars); }, true);
            }
            // ---------------------------------------------------------- create lobby
            {
                _scrCreate = MakeScreen("Create Lobby", new Vector2(360, 396), out var c);
                float y = -6;
                UIFactory.Label(c, "Lobby", new Vector2(0.5f, 1f), new Vector2(-112, y - 12), new Vector2(84, 22), 16, skin.bone3, TextAnchor.MiddleLeft);
                _lobbyNameField = UIFactory.Field(c, "", "The Ashen Hall", new Vector2(0.5f, 1f), new Vector2(38, y - 12), 210);
                _lobbyNameField.characterLimit = 28;
                y -= 32;
                UIFactory.Label(c, "Knights", new Vector2(0.5f, 1f), new Vector2(-112, y - 12), new Vector2(84, 22), 16, skin.bone3, TextAnchor.MiddleLeft);
                var maxBtn = UIFactory.Button(c, "", new Vector2(0.5f, 1f), new Vector2(38, y - 12), 210, null, true);
                _maxText = maxBtn.GetComponentInChildren<Text>();
                maxBtn.onClick.AddListener(() => { _createMax = _createMax % 4 + 1; RefreshMax(); });
                RefreshMax();
                y -= 32;
                UIFactory.Label(c, "Visibility", new Vector2(0.5f, 1f), new Vector2(-112, y - 12), new Vector2(84, 22), 16, skin.bone3, TextAnchor.MiddleLeft);
                var visBtn = UIFactory.Button(c, "", new Vector2(0.5f, 1f), new Vector2(38, y - 12), 210, null, true);
                _visText = visBtn.GetComponentInChildren<Text>();
                _createOnline = PlayerPrefs.GetInt("df_online", 1) == 1;
                visBtn.onClick.AddListener(() => { _createOnline = !_createOnline; PlayerPrefs.SetInt("df_online", _createOnline ? 1 : 0); RefreshVis(); });
                RefreshVis();
                y -= 32;
                UIFactory.Label(c, "Start at", new Vector2(0.5f, 1f), new Vector2(-112, y - 12), new Vector2(84, 22), 16, skin.bone3, TextAnchor.MiddleLeft);
                var startBtn = UIFactory.Button(c, "", new Vector2(0.5f, 1f), new Vector2(38, y - 12), 210, null, true);
                _startText = startBtn.GetComponentInChildren<Text>();
                startBtn.onClick.AddListener(() =>
                {
                    int max = MenuSettings.UnlockedCheckpoint;
                    MenuSettings.StartCheckpoint = max <= 0 ? 0 : (MenuSettings.StartCheckpoint + 1) % (max + 1);
                    RefreshStart();
                });
                RefreshStart();
                y -= 32;
                UIFactory.Label(c, "Port", new Vector2(0.5f, 1f), new Vector2(-112, y - 12), new Vector2(84, 22), 16, skin.bone3, TextAnchor.MiddleLeft);
                _createPortField = UIFactory.Field(c, MenuSettings.LastPort.ToString(), "7777", new Vector2(0.5f, 1f), new Vector2(38, y - 12), 210, InputField.ContentType.IntegerNumber);
                RefreshVis();
                y -= 46;
                UIFactory.Button(c, "Open the Lobby", new Vector2(0.5f, 1f), new Vector2(0, y - 14), 220, OnCreateLobby); y -= 36;
                UIFactory.Button(c, "Back", new Vector2(0.5f, 1f), new Vector2(0, y - 14), 140, () => ShowScreen(_scrMain), true);
                y -= 30;
                _createStatus = UIFactory.Label(c, "", new Vector2(0.5f, 1f), new Vector2(0, y - 12), new Vector2(320, 30), 13, skin.bone3);
            }
            // ---------------------------------------------------------- lobby browser
            {
                _scrBrowse = MakeScreen("Find a Lobby", new Vector2(460, 380), out var c);
                float y = -4;
                _browseStatus = UIFactory.Label(c, "Searching…", new Vector2(0.5f, 1f), new Vector2(0, y - 9), new Vector2(400, 18), 14, skin.bone3);
                y -= 24;
                for (int i = 0; i < 6; i++)
                {
                    var b = UIFactory.Button(c, "", new Vector2(0.5f, 1f), new Vector2(0, y - 14), 400, null, true);
                    var t = b.GetComponentInChildren<Text>();
                    t.alignment = TextAnchor.MiddleLeft; t.fontSize = 14;
                    int idx = i;
                    b.onClick.AddListener(() => JoinFound(idx));
                    b.gameObject.SetActive(false);
                    _browseRows.Add((b, t));
                    y -= 31;
                }
                y = -238;
                UIFactory.Label(c, "Online lobbies appear here for everyone. Can't see one? Use Join by IP.", new Vector2(0.5f, 1f), new Vector2(0, y - 8), new Vector2(410, 16), 12, skin.bone4);
                y -= 24;
                UIFactory.Button(c, "Join by IP", new Vector2(0.5f, 1f), new Vector2(-80, y - 14), 150, () => ShowScreen(_scrDirect));
                UIFactory.Button(c, "Back", new Vector2(0.5f, 1f), new Vector2(80, y - 14), 150, () => ShowScreen(_scrMain), true);
            }
            // ---------------------------------------------------------- join by IP
            {
                _scrDirect = MakeScreen("Join by IP", new Vector2(340, 280), out var c);
                float y = -6;
                UIFactory.Label(c, "Host IP", new Vector2(0.5f, 1f), new Vector2(-104, y - 12), new Vector2(84, 22), 16, skin.bone3, TextAnchor.MiddleLeft);
                _ipField = UIFactory.Field(c, MenuSettings.LastIp, "127.0.0.1", new Vector2(0.5f, 1f), new Vector2(38, y - 12), 196);
                y -= 32;
                UIFactory.Label(c, "Port", new Vector2(0.5f, 1f), new Vector2(-104, y - 12), new Vector2(84, 22), 16, skin.bone3, TextAnchor.MiddleLeft);
                _portField = UIFactory.Field(c, MenuSettings.LastPort.ToString(), "7777", new Vector2(0.5f, 1f), new Vector2(38, y - 12), 196, InputField.ContentType.IntegerNumber);
                y -= 44;
                UIFactory.Button(c, "Join", new Vector2(0.5f, 1f), new Vector2(0, y - 14), 200, OnJoinClicked); y -= 36;
                UIFactory.Button(c, "Back", new Vector2(0.5f, 1f), new Vector2(0, y - 14), 140, () => ShowScreen(_scrMain), true); y -= 30;
                _directStatus = UIFactory.Label(c, "Ask the host for the IP shown in their lobby.", new Vector2(0.5f, 1f), new Vector2(0, y - 12), new Vector2(290, 30), 13, skin.bone3);
            }
            // ---------------------------------------------------------- lobby room
            {
                _scrLobby = MakeScreen("Lobby", new Vector2(440, 414), out var c);
                float y = -2;
                _lobbyTitle = UIFactory.Label(c, "", new Vector2(0.5f, 1f), new Vector2(0, y - 12), new Vector2(390, 24), 22, skin.gold2, TextAnchor.MiddleCenter, true);
                y -= 32;
                for (int i = 0; i < 4; i++)
                {
                    var row = UIFactory.Panel(c, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, y - 13), new Vector2(360, 26));
                    var t = UIFactory.Text(UIFactory.Stretch("T", row, 12, 3, 12, 3), "", 15, skin.bone1, TextAnchor.MiddleLeft);
                    _lobbyRows.Add(t);
                    y -= 30;
                }
                y -= 4;
                _lobbyInfo = UIFactory.Label(c, "", new Vector2(0.5f, 1f), new Vector2(0, y - 16), new Vector2(400, 34), 12, skin.bone3);
                y -= 40;
                var lcls = UIFactory.Button(c, "", new Vector2(0.5f, 1f), new Vector2(0, y - 14), 240, null, true);
                _lobbyClassText = lcls.GetComponentInChildren<Text>();
                lcls.onClick.AddListener(ToggleClass);
                y -= 34;
                _beginButton = UIFactory.Button(c, "Begin the Journey", new Vector2(0.5f, 1f), new Vector2(-84, y - 14), 180, () => LobbyState.Instance?.RequestStartRpc());
                UIFactory.Button(c, "Leave", new Vector2(0.5f, 1f), new Vector2(110, y - 14), 120, () => NetworkBootstrap.Instance?.Shutdown(), true);
                y -= 30;
                _lobbyWait = UIFactory.Label(c, "", new Vector2(0.5f, 1f), new Vector2(0, y - 10), new Vector2(380, 18), 13, skin.bone3);
            }

            var ipPanel = UIFactory.Panel(_menu, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 14), new Vector2(520, 44));
            _ipText = UIFactory.Text(UIFactory.Stretch("IpText", ipPanel, 12, 8, 12, 8), "", 14, skin.bone1);
            UIFactory.Label(_menu, "UI: Ravenhollow Lite by Brunka Games · Font: Jacquard 12 / Jersey 10 (OFL)", new Vector2(1f, 0f), new Vector2(-170, 8), new Vector2(330, 14), 10, skin.bone4, TextAnchor.LowerRight);
            BuildCharacterScreens();
            ShowScreen(_scrChars);
            RefreshClass();
        }

        Text _startText;
        static readonly string[] StartNames = { "The Bonfire", "The Throne Room", "Hall of Chains", "The Grand Stair", "The Long Bridge", "The Thornwood", "The Glass Temple", "The Red Moon" };

        void RefreshStart()
        {
            if (_startText == null) return;
            int i = Mathf.Clamp(MenuSettings.StartCheckpoint, 0, StartNames.Length - 1);
            bool locked = MenuSettings.UnlockedCheckpoint <= 0;
            _startText.text = locked ? "The Bonfire" : StartNames[i];
        }

        static string ClassLabel(PlayerClass c) => c switch
        {
            PlayerClass.Archer => "Archer  (war-bow)",
            PlayerClass.Bard => "Bard  (mandolin & songs)",
            PlayerClass.Reaper => "Reaper  (scythe)",
            PlayerClass.Smoker => "Smoker  (pipe & smoke)",
            _ => "Knight  (greatsword)",
        };

        void ToggleClass()
        {
            // the class belongs to the character: from the menu this opens the character choice instead
            if (SaveSlots.Current >= 0) { if (LobbyState.Instance == null) ShowScreen(_scrChars); return; }
            var next = (PlayerClass)(((int)MenuSettings.PlayerClass + 1) % 5);
            if (LobbyState.Instance != null) LobbyState.Instance.SetLocalClass(next);
            else MenuSettings.PlayerClass = next;
            RefreshClass();
        }

        void RefreshClass()
        {
            var c = MenuSettings.PlayerClass;
            if (_classText) _classText.text = ClassLabel(c);
            if (_lobbyClassText) _lobbyClassText.text = "Class:  " + ClassLabel(c);
        }

        RectTransform MakeScreen(string title, Vector2 size, out RectTransform content)
        {
            var holder = UIFactory.Stretch("Screen_" + title, _menu);
            content = UIFactory.Window(holder, title, size, out var win);
            win.anchoredPosition = new Vector2(0, -22);
            return holder;
        }

        void ShowScreen(RectTransform scr)
        {
            foreach (var s in new[] { _scrMain, _scrCreate, _scrBrowse, _scrDirect, _scrLobby, _scrChars, _scrNewChar })
                if (s) s.gameObject.SetActive(s == scr);
            var lan = LanDiscovery.Instance;
            if (lan) { if (scr == _scrBrowse) lan.StartListening(); else lan.StopListening(); }
            if (scr == _scrBrowse) OnlineLobby.ForceRefresh();
            if (scr == _scrCreate && _createStatus) _createStatus.text = "";
            if (_ipText) _ipText.transform.parent.gameObject.SetActive(scr != _scrChars && scr != _scrNewChar);
        }

        void RefreshVis()
        {
            if (_visText) _visText.text = _createOnline ? "Online (everyone)" : "LAN / IP only";
            if (_createPortField) _createPortField.interactable = !_createOnline;
        }

        void RefreshMax() { if (_maxText) _maxText.text = _createMax == 1 ? "1 (solo)" : _createMax + " knights"; }

        void OpenCreate()
        {
            string n = string.IsNullOrWhiteSpace(MenuSettings.PlayerName) ? "The Ashen Hall" : MenuSettings.PlayerName + "'s Hall";
            _lobbyNameField.text = n;
            ShowScreen(_scrCreate);
        }

        void OnCreateLobby()
        {
            if (!ushort.TryParse(_createPortField.text, out ushort port)) port = 7777;
            MenuSettings.LastPort = port; PlayerPrefs.Save();
            var nb = NetworkBootstrap.Instance;
            if (nb == null) return;
            if (_createOnline) nb.StartOnlineHost(_lobbyNameField.text, _createMax);
            else nb.StartHost(port, _lobbyNameField.text, _createMax);
        }

        void JoinFound(int index)
        {
            if (index >= _browseMap.Count) return;
            var (online, i) = _browseMap[index];
            var nb = NetworkBootstrap.Instance;
            if (nb == null) return;
            if (online)
            {
                if (i >= OnlineLobby.Found.Count) return;
                var o = OnlineLobby.Found[i];
                if (o.players >= o.max) { SetBrowseMsg("That lobby is full."); return; }
                nb.JoinOnline(o.id);
                return;
            }
            var lan = LanDiscovery.Instance;
            if (lan == null || i >= lan.Found.Count) return;
            var e = lan.Found[i];
            if (e.players >= e.max) { SetBrowseMsg("That lobby is full."); return; }
            nb.StartClient(e.address, e.port);
        }

        void SetBrowseMsg(string m) { _lastStatus = m; _lastStatusAt = Time.unscaledTime; }

        void UpdateMenuScreens()
        {
            // lobby browser rows
            if (_scrBrowse.gameObject.activeSelf)
            {
                OnlineLobby.PollIfDue();
                var lan = LanDiscovery.Instance;
                _browseMap.Clear();
                for (int i = 0; i < OnlineLobby.Found.Count; i++) _browseMap.Add((true, i));
                if (lan) for (int i = 0; i < lan.Found.Count; i++) _browseMap.Add((false, i));
                int n = _browseMap.Count;
                var nbb = NetworkBootstrap.Instance;
                if (nbb && nbb.OnlineBusy) _browseStatus.text = "Joining…";
                else if (Time.unscaledTime - _lastStatusAt < 6f && !string.IsNullOrEmpty(_lastStatus)) _browseStatus.text = _lastStatus;
                else if (n > 0) _browseStatus.text = $"{n} lobb{(n == 1 ? "y" : "ies")} found — click to join";
                else if (!string.IsNullOrEmpty(OnlineLobby.LastError)) _browseStatus.text = "<color=#c46a5a>" + OnlineLobby.LastError + "</color>";
                else _browseStatus.text = OnlineLobby.HasQueried ? "No open lobbies yet — create one!" : "Searching…";
                for (int r = 0; r < _browseRows.Count; r++)
                {
                    bool on = r < n;
                    _browseRows[r].btn.gameObject.SetActive(on);
                    if (!on) continue;
                    var (online, i) = _browseMap[r];
                    string name, host, where; int pl, mx; bool started;
                    if (online) { var o = OnlineLobby.Found[i]; name = o.name; host = o.host; pl = o.players; mx = o.max; started = o.started; where = "online"; }
                    else { var e = lan.Found[i]; name = e.name; host = e.host; pl = e.players; mx = e.max; started = e.started; where = "LAN " + e.address; }
                    string state = started ? "<color=#c9a25a>in game</color>" : "waiting";
                    _browseRows[r].label.text = $"{name}  ·  {host}   {pl}/{mx}   {state}   <color=#8a8579>{where}</color>";
                }
            }
            // lobby room
            if (_scrLobby.gameObject.activeSelf)
            {
                var ls = LobbyState.Instance;
                var nm = NetworkBootstrap.Instance ? NetworkBootstrap.Instance.Manager : null;
                bool host = nm && nm.IsHost;
                _lobbyTitle.text = ls ? ls.DisplayLobbyName : "Joining…";
                int count = ls ? ls.Members.Count : 0;
                for (int i = 0; i < _lobbyRows.Count; i++)
                {
                    if (ls != null && i < count)
                    {
                        var m = ls.Members[i];
                        bool isHost = m.clientId == NetworkManager.ServerClientId;
                        bool me = nm && m.clientId == nm.LocalClientId;
                        string cls = m.Class == PlayerClass.Archer ? "<color=#9fb08a>Archer</color>" : m.Class == PlayerClass.Bard ? "<color=#a89adf>Bard</color>" : m.Class == PlayerClass.Reaper ? "<color=#b98ae0>Reaper</color>" : m.Class == PlayerClass.Smoker ? "<color=#b9b0a2>Smoker</color>" : "<color=#b0a48a>Knight</color>";
                        _lobbyRows[i].text = $"{(isHost ? "<color=#c9a25a>[Host]</color> " : "")}{m.name}   {cls}{(me ? "  <color=#8a8579>(you)</color>" : "")}";
                    }
                    else _lobbyRows[i].text = ls != null && i < ls.MaxPlayers.Value ? "<color=#5d5a52>— empty seat —</color>" : "";
                }
                var nb = NetworkBootstrap.Instance;
                if (host)
                {
                    var ips = NetworkBootstrap.GetLocalIPv4();
                    if (OnlineLobby.IsOnline)
                        _lobbyInfo.text = "Online lobby: everyone with the game sees it in Find Lobbies.\nNo ports to open — your friends just click it.";
                    else _lobbyInfo.text = $"Friends on your network see this lobby in Find Lobbies.\nOver the Internet: IP {(nb ? nb.PublicIp : "…")}  port {(nb ? nb.Port : 7777)}   (LAN: {string.Join(", ", ips)})";
                    _lobbyWait.text = count < 2 ? "You can also begin alone." : "Begin when everyone is here.";
                }
                else
                {
                    _lobbyInfo.text = "Connected to the host.";
                    _lobbyWait.text = "Waiting for the host to begin the journey…";
                }
                _beginButton.gameObject.SetActive(host);
            }
        }

        void BuildHud()
        {
            _hud = UIFactory.Stretch("HUD", _root);

            // ---- vita / stamina in alto a sinistra
            var hpIcon = UIFactory.Rect("HeartIcon", _hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -10), new Vector2(24, 24));
            UIFactory.Image(hpIcon, skin.iconHeart, false);
            _hpBar = UIBar.Create(_hud, skin.barFrameHp, skin.barFillHp, new Vector2(0, 1), new Vector2(0, 1), new Vector2(38, -14), 224);
            _staminaBar = UIBar.Create(_hud, skin.barFrameXp, skin.barFillXp, new Vector2(0, 1), new Vector2(0, 1), new Vector2(38, -32), 176, new Color(0.62f, 0.78f, 0.55f));
            var flask = UIFactory.Rect("Flask", _hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -52), new Vector2(40, 40));
            UIFactory.Image(flask, skin.panelClean);
            var flaskIcon = UIFactory.Rect("Icon", flask, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24));
            UIFactory.Image(flaskIcon, skin.iconPotion, false);
            _flaskText = UIFactory.Label(flask, "3", new Vector2(1, 0), new Vector2(-6, 8), new Vector2(20, 16), 16, skin.bone0, TextAnchor.MiddleRight);

            // ---- gruppo in alto a destra
            _partyRoot = UIFactory.Rect("Party", _hud, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -10), new Vector2(170, 200));

            // ---- boss in basso
            _bossRoot = UIFactory.Rect("BossBar", _hud, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(520, 40));
            _bossName = UIFactory.Label(_bossRoot, "", new Vector2(0, 1), new Vector2(150, -8), new Vector2(300, 22), 24, skin.bone0, TextAnchor.MiddleLeft, true);
            _bossBar = UIBar.Create(_bossRoot, skin.barFrameHp, skin.barFillHp, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 0), 520);
            _bossRoot.gameObject.SetActive(false);

            // ---- barre sopra i nemici
            _overheadRoot = UIFactory.Stretch("Overhead", _hud);

            // ---- mirino lock-on (rombo generato)
            _reticle = UIFactory.Rect("LockOn", _hud, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(9, 9));
            var ret = UIFactory.Image(_reticle, MakeDiamond(), false, new Color(0.95f, 0.88f, 0.75f, 0.95f));
            _reticle.gameObject.SetActive(false);

            // ---- archer crosshair: placed where the next arrow will really go
            _crosshair = UIFactory.Rect("Crosshair", _hud, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(11, 11));
            _crosshairImg = UIFactory.Image(_crosshair, MakeCrosshair(), false, new Color(0.95f, 0.9f, 0.8f, 0.55f));
            _crosshairImg.raycastTarget = false;
            _crosshair.gameObject.SetActive(false);

            // ---- prompt interazione
            _promptRt = UIFactory.Panel(_hud, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 142), new Vector2(260, 40));   // above the ultimate / spell bar
            var key = UIFactory.Rect("Key", _promptRt, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(36, 28));
            InputGlyph.Create(key, GameAction.Interact, new Vector2(0.5f, 0.5f), Vector2.zero, 28f);
            _promptText = UIFactory.Text(UIFactory.Rect("T", _promptRt, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(54, 0), new Vector2(196, 24)), "", 16, skin.bone1, TextAnchor.MiddleLeft);
            _promptRt.gameObject.SetActive(false);

            _sessionInfo = UIFactory.Label(_hud, "", new Vector2(0, 0), new Vector2(110, 10), new Vector2(210, 14), 10, skin.bone4, TextAnchor.LowerLeft);
            UIFactory.Label(_hud, "F1 controls (rebind) · F9 developer · Esc pause", new Vector2(1, 0), new Vector2(-100, 10), new Vector2(190, 14), 10, skin.bone4, TextAnchor.LowerRight);
            BuildBardHud();
            BuildUltHud();
            BuildCoopHud();
            BuildChat();
        }

        void BuildPause()
        {
            _pause = UIFactory.Stretch("Pause", _root);
            UIFactory.Image(UIFactory.Stretch("Veil", _pause), null, false, new Color(0, 0, 0, 0.55f)).raycastTarget = true;
            var content = UIFactory.Window(_pause, "Paused", new Vector2(300, 330), out _);
            float y = -8;
            UIFactory.Button(content, "Resume", new Vector2(0.5f, 1), new Vector2(0, y - 14), 200, () => SetPaused(false)); y -= 34;
            // PvP lives in the pause menu: a deliberate choice, never toggled by accident mid-fight
            var pvp = UIFactory.Button(content, "PvP: OFF", new Vector2(0.5f, 1), new Vector2(0, y - 14), 200, TogglePvp); y -= 26;
            _pvpButtonText = pvp.GetComponentInChildren<Text>();
            UIFactory.Label(content, "only players with PvP ON can hurt each other", new Vector2(0.5f, 1), new Vector2(0, y - 10), new Vector2(240, 14), 10, skin.bone4); y -= 26;
            UIFactory.Button(content, "Options", new Vector2(0.5f, 1), new Vector2(0, y - 14), 200, () => ShowOptions(true)); y -= 34;
            UIFactory.Button(content, "Controls", new Vector2(0.5f, 1), new Vector2(0, y - 14), 200, () => ShowControls(!ControlsOpen)); y -= 40;
            UIFactory.Button(content, "Leave session", new Vector2(0.5f, 1), new Vector2(0, y - 14), 200, () => { SetPaused(false); NetworkBootstrap.Instance?.Shutdown(); }, true);
            _pause.gameObject.SetActive(false);
        }

        /// <summary>Options window (main menu and pause): audio, display, camera.</summary>
        void BuildOptions()
        {
            _options = UIFactory.Stretch("Options", _root);
            UIFactory.Image(UIFactory.Stretch("Veil", _options), null, false, new Color(0, 0, 0, 0.6f)).raycastTarget = true;
            var content = UIFactory.Window(_options, "Options", new Vector2(380, 470), out _);
            float y = -4;
            void SliderRow(string label, float min, float max, float value, UnityEngine.Events.UnityAction<float> onChange)
            {
                UIFactory.Label(content, label, new Vector2(0.5f, 1), new Vector2(-96, y - 10), new Vector2(130, 20), 16, skin.bone3, TextAnchor.MiddleLeft);
                UIFactory.Slider(content, new Vector2(0.5f, 1), new Vector2(64, y - 10), 150, min, max, value, onChange);
                y -= 28;
            }
            UIFactory.Label(content, "AUDIO", new Vector2(0.5f, 1), new Vector2(0, y - 8), new Vector2(300, 18), 14, skin.gold2); y -= 22;
            SliderRow("Master volume", 0f, 1f, GameSettings.MasterVolume, GameSettings.SetMaster);
            SliderRow("Music", 0f, 1f, GameSettings.MusicVolume, GameSettings.SetMusic);
            SliderRow("Effects", 0f, 1f, GameSettings.SfxVolume, v => { GameSettings.SetSfx(v); if (Time.unscaledTime - _lastSfxPreview > 0.25f) { _lastSfxPreview = Time.unscaledTime; Sfx.Play2D(SfxId.UiClick); } });
            y -= 6;
            UIFactory.Label(content, "DISPLAY", new Vector2(0.5f, 1), new Vector2(0, y - 8), new Vector2(300, 18), 14, skin.gold2); y -= 24;
            _fullscreenToggle = UIFactory.Toggle(content, "Fullscreen  (F11)", new Vector2(0.5f, 1), new Vector2(0, y - 11), 240, Screen.fullScreen, GameSettings.SetFullscreen);
            y -= 26;
            bool vhs = PlayerPrefs.GetInt("df_vhs", 1) == 1;
            UIFactory.Toggle(content, "VHS / CRT effect", new Vector2(0.5f, 1), new Vector2(0, y - 11), 240, vhs, v => { PlayerPrefs.SetInt("df_vhs", v ? 1 : 0); if (ScreenPresenter.Instance) ScreenPresenter.Instance.SetVhs(v); });
            y -= 26;
            bool low = PlayerPrefs.GetInt("df_lowres", 1) == 1;
            UIFactory.Toggle(content, "PS2 resolution (448p)", new Vector2(0.5f, 1), new Vector2(0, y - 11), 240, low, v => { PlayerPrefs.SetInt("df_lowres", v ? 1 : 0); if (ScreenPresenter.Instance) ScreenPresenter.Instance.SetLowRes(v); });
            y -= 28;
            SliderRow("Brightness", 0.6f, 1.8f, ScreenPresenter.Brightness, ScreenPresenter.SetBrightness);
            SliderRow("Screen shake", 0f, 1f, GameSettings.ScreenShake, GameSettings.SetShake);
            y -= 4;
            UIFactory.Label(content, "CAMERA", new Vector2(0.5f, 1), new Vector2(0, y - 8), new Vector2(300, 18), 14, skin.gold2); y -= 22;
            float sens = PlayerPrefs.GetFloat("df_sens", 1f); GameInput.MouseSensitivity = sens; GameInput.GamepadSensitivity = sens;
            SliderRow("Sensitivity", 0.2f, 3f, sens, v => { GameInput.MouseSensitivity = v; GameInput.GamepadSensitivity = v; PlayerPrefs.SetFloat("df_sens", v); });
            UIFactory.Toggle(content, "Invert Y axis", new Vector2(0.5f, 1), new Vector2(0, y - 11), 240, PlayerPrefs.GetInt("df_invy", 0) == 1, v => { GameInput.InvertY = v; PlayerPrefs.SetInt("df_invy", v ? 1 : 0); });
            GameInput.InvertY = PlayerPrefs.GetInt("df_invy", 0) == 1;
            y -= 34;
            UIFactory.Button(content, "Back", new Vector2(0.5f, 1), new Vector2(0, y - 14), 160, () => ShowOptions(false), true);
            _options.gameObject.SetActive(false);
        }

        void ShowOptions(bool on)
        {
            _options.gameObject.SetActive(on);
            if (on && _fullscreenToggle) _fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
            if (_pause) _pause.gameObject.SetActive(!on && _paused);
        }


#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Development (editor bridge): LAN lobby + immediate start, no clicks.</summary>
        public void DevQuickStart(string _)
        {
            var nb = NetworkBootstrap.Instance;
            if (nb == null || !nb.StartHost((ushort)MenuSettings.LastPort, "Test", 4)) return;
            StartCoroutine(DevBegin());
        }
        System.Collections.IEnumerator DevBegin() { yield return new WaitForSeconds(0.6f); LobbyState.Instance?.RequestStartRpc(); }
        public void DevInventory(string on) => ShowInventory(on == "1");
        public void DevPause(string on) => SetPaused(on == "1");
        /// <summary>Bridge test of the F9 menu: "open" | "fight" | "terrace".</summary>
        public void DevMenuTest(string a)
        {
            if (a == "open") SetDevMenu(true);
            else if (a == "fight") DevGo(new Vector3(0f, 7.8f, 166f), 0f, true);
            else if (a == "terrace") DevGo(new Vector3(0f, 7.8f, 162f), 0f, false);
            else if (a == "bridge") DevGo(new Vector3(0f, 8.2f, 246f), 0f, false);
            else if (a == "forest") DevGo(new Vector3(0f, -23.6f, 356f), 0f, false);
            else if (a == "temple") DevGo(new Vector3(0f, -23.3f, 476f), 0f, false);
            else if (a == "mirror") DevGo(new Vector3(0f, -23.3f, 488f), 0f, false, 2);
            else if (a == "radiant") DevSpawnRadiant();
            else if (a == "realm") DevGo(new Vector3(0f, -149.7f, 824f), 0f, false);
            else if (a == "death") DevGo(new Vector3(0f, -149.6f, 930f), 0f, false, 3);
            else if (a == "portal") { CastleProgress.Instance?.DevProgress("mirror"); DevGo(new Vector3(0f, -23.3f, 500f), 0f, false); }
            else if (a == "ult") PlayerUltimate.Local?.DevCharge("");
            else if (a == "bard") MenuSettings.PlayerClass = PlayerClass.Bard;
            else if (a == "archer") MenuSettings.PlayerClass = PlayerClass.Archer;
            else if (a == "knight") MenuSettings.PlayerClass = PlayerClass.Knight;
            else if (a == "reaper") MenuSettings.PlayerClass = PlayerClass.Reaper;
            else if (a == "smoker") MenuSettings.PlayerClass = PlayerClass.Smoker;
        }
#endif

        void OnLooted(string title, string sub) => ShowMessage(title, sub, new Color(0.85f, 0.78f, 0.6f), 2.5f, 34);

        void OnRelicGranted(PlayerClass c, byte tier) => ShowMessage(tier == Inventory.Radiant ? "RADIANT WEAPON ACQUIRED" : tier == Inventory.Dragon ? "WYRM'S WEAPON ACQUIRED" : "RELIC ACQUIRED",
            Inventory.Name(c, tier) + "  —  press I to equip it", tier == Inventory.Radiant ? new Color(1f, 0.95f, 0.75f) : tier == Inventory.Dragon ? new Color(1f, 0.4f, 0.2f) : skin.gold2, 5f, 48);

        void BuildMessage()
        {
            _messageRt = UIFactory.Rect("Message", _root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(960, 140));
            UIFactory.Image(UIFactory.Stretch("Band", _messageRt, 0, 30, 0, 30), null, false, new Color(0, 0, 0, 0.6f));
            _messageText = UIFactory.Text(UIFactory.Stretch("Text", _messageRt, 0, 20, 0, 30), "", 64, skin.hp1, TextAnchor.MiddleCenter, true);
            _messageText.GetComponent<Shadow>().effectDistance = new Vector2(2, -2);
            _messageSub = UIFactory.Text(UIFactory.Rect("Sub", _messageRt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(600, 20)), "", 16, skin.bone3);
            _messageGroup = _messageRt.gameObject.AddComponent<CanvasGroup>();
            _messageGroup.blocksRaycasts = false;
            _messageRt.gameObject.SetActive(false);
        }

        CanvasGroup _messageGroup;
        static Sprite _diamond, _cross;
        static Sprite MakeCrosshair()
        {
            if (_cross) return _cross;
            var tex = new Texture2D(11, 11, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            for (int y = 0; y < 11; y++)
                for (int x = 0; x < 11; x++)
                {
                    bool arm = (x == 5 && (y <= 3 || y >= 7)) || (y == 5 && (x <= 3 || x >= 7));
                    tex.SetPixel(x, y, arm || (x == 5 && y == 5) ? Color.white : Color.clear);
                }
            tex.Apply();
            _cross = Sprite.Create(tex, new Rect(0, 0, 11, 11), new Vector2(0.5f, 0.5f), 100);
            return _cross;
        }
        static Sprite MakeDiamond()
        {
            if (_diamond) return _diamond;
            var tex = new Texture2D(9, 9, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            for (int y = 0; y < 9; y++)
                for (int x = 0; x < 9; x++)
                {
                    int d = Mathf.Abs(x - 4) + Mathf.Abs(y - 4);
                    tex.SetPixel(x, y, d <= 4 ? (d >= 3 ? Color.white : (d == 0 ? Color.white : new Color(1, 1, 1, 0.15f))) : Color.clear);
                }
            tex.Apply();
            _diamond = Sprite.Create(tex, new Rect(0, 0, 9, 9), new Vector2(0.5f, 0.5f), 100);
            return _diamond;
        }

        // ================================================================== menu
        void OnJoinClicked()
        {
            MenuSettings.LastIp = _ipField.text;
            if (int.TryParse(_portField.text, out int pp)) MenuSettings.LastPort = pp;
            SaveFields();
            if (!ushort.TryParse(_portField.text, out ushort port)) port = 7777;
            NetworkBootstrap.Instance?.StartClient(_ipField.text, port);
        }

        void SaveFields()
        {
            if (_nameField) MenuSettings.PlayerName = _nameField.text;
            if (_ipField && _scrDirect && _scrDirect.gameObject.activeSelf) MenuSettings.LastIp = _ipField.text;
            if (_portField && _scrDirect && _scrDirect.gameObject.activeSelf && int.TryParse(_portField.text, out int p)) MenuSettings.LastPort = p;
            PlayerPrefs.Save();
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void ShowMenu(bool show)
        {
            if (show && InventoryOpen) ShowInventory(false);
            _menu.gameObject.SetActive(show);
            _hud.gameObject.SetActive(!show);
            GameInput.GameplayBlocked = show;
            GameInput.SetCursorLocked(!show);
            if (show)
            {
                _bossRoot.gameObject.SetActive(false);
                foreach (var kv in _party) if (kv.Value.root) Destroy(kv.Value.root.gameObject);
                _party.Clear();
                foreach (var kv in _overhead) if (kv.Value.root) Destroy(kv.Value.root.gameObject);
                _overhead.Clear();
            }
        }

        void SetPaused(bool p)
        {
            _paused = p;
            _pause.gameObject.SetActive(p);
            if (!p && _options) _options.gameObject.SetActive(false);
            if (!p) _help.gameObject.SetActive(false);
            GameInput.GameplayBlocked = p;
            GameInput.SetCursorLocked(!p);
        }

        // ================================================================== eventi di gioco
        void OnLocalDied(PlayerController p) => ShowMessage("YOU DIED", p.IsSpectating ? "You fell in this battle · your allies fight on" : "Returning to the last flame…", skin.hp1, 4f);
        void OnFightStarted(int players) => ShowMessage("THE ASHEN KING", players > 1 ? $"rises against {players} challengers" : "rises from his throne", skin.bone0, 3f, 48);
        void OnBossDefeated() { ShowMessage("GREAT ENEMY FELLED", "The fog lifts", skin.gold2, 6f); StartCoroutine(ArmorWon(Inventory.ArmorAshen, 6.5f)); }

        /// <summary>Every member of the party wins the boss's armour set (kept on this computer).</summary>
        System.Collections.IEnumerator ArmorWon(byte set, float delay)
        {
            if (PlayerController.Local == null) yield break;
            bool had = Inventory.OwnsArmorSet(set);
            Inventory.GrantArmorSet(set);
            yield return new WaitForSeconds(delay);
            ShowMessage(had ? "ARMOUR AWAKENED" : "ARMOUR WON", Inventory.SetName(set) + "  —  press I to wear it", SetColor(set), 5f, 44);
            Sfx.Play2D(SfxId.RelicReveal, 0.7f, 0.9f);
        }
        void OnBossIgnited() => ShowMessage("THE ASHEN KING", "is consumed by the flame", new Color(1f, 0.55f, 0.2f), 3f, 40);
        void OnFightReset() { if (_bossRoot) _bossRoot.gameObject.SetActive(false); }
        void OnDragonStarted(int players) => ShowMessage("VAELTHRAX", players > 1 ? $"the Cinder Wyrm descends upon {players} challengers" : "the Cinder Wyrm", new Color(1f, 0.6f, 0.3f), 3.5f, 48);
        void OnDragonDefeated() { ShowMessage("WYRM SLAIN", "The castle falls silent", skin.gold2, 7f); StartCoroutine(ArmorWon(Inventory.ArmorWyrm, 7.5f)); }
        void OnMirrorStarted(int players) => ShowMessage("SHATTERED MIRROR OF MALICE", players > 1 ? $"it shows {players} of you what you lack" : "it shows you what you lack", new Color(0.78f, 0.62f, 1f), 3.8f, 44);
        void OnMirrorDefeated() { ShowMessage("MIRROR SHATTERED", "Every stolen reflection flies home · the glass opens", new Color(0.85f, 0.8f, 1f), 7f); StartCoroutine(ArmorWon(Inventory.ArmorMirror, 7.5f)); }
        void OnReaperStarted(int players) => ShowMessage("DEATH", players > 1 ? $"the Reaper has come for {players} souls" : "the Reaper has come for you", new Color(0.85f, 0.2f, 0.25f), 4f, 56);
        void OnReaperDefeated() { ShowMessage("DEATH IS DEAD", "The red moon closes its eye", new Color(0.95f, 0.85f, 0.8f), 8f); StartCoroutine(ArmorWon(Inventory.ArmorDeath, 8.5f)); }
        void OnReaperJudgement() => ShowMessage("THE FINAL JUDGEMENT", "its cloak burns away · only hunger remains", new Color(0.65f, 0.45f, 1f), 3.5f, 40);
        void OnMirrorAwakened() => ShowMessage("THE GLASS BREAKS", "and what was behind it wakes", new Color(0.75f, 0.45f, 1f), 3f, 40);
        void OnDragonEnraged() => ShowMessage("VAELTHRAX", "takes to the sky", new Color(1f, 0.5f, 0.2f), 3f, 40);
        void OnAnnounce(string title, string sub) => ShowMessage(title, sub, skin.gold2, 4.5f, 48);
        void OnRested(int index) => ShowMessage("RESTED", "The flame burns on · the fallen rise again", skin.gold2, 3.5f, 40);
        void OnCheckpoint(int index) { if (index > 0) ShowMessage("FLAME KINDLED", "A new place of rest", skin.bone0, 3f, 40); }

        void ShowMessage(string title, string sub, Color c, float duration, int size = 64)
        {
            _messageText.text = title;
            _messageText.color = c;
            _messageText.fontSize = size;
            _messageSub.text = sub;
            _messageTimer = 0f;
            _messageDuration = duration;
            _messageRt.gameObject.SetActive(true);
        }

        // ================================================================== developer menu (F9)
        RectTransform _dev;
        Text _devGodLabel;
        bool DevOpen => _dev && _dev.gameObject.activeSelf;

        void BuildDevMenu()
        {
            _dev = UIFactory.Stretch("DevMenu", _root);
            UIFactory.Image(UIFactory.Stretch("Veil", _dev), null, false, new Color(0, 0, 0, 0.45f)).raycastTarget = true;
            var content = UIFactory.Window(_dev, "Developer", new Vector2(320, 656), out _);
            float y = -6;
            void Btn(string label, UnityEngine.Events.UnityAction a, bool secondary = false) { UIFactory.Button(content, label, new Vector2(0.5f, 1), new Vector2(0, y - 14), 250, a, secondary); y -= 34; }
            Btn("Dragon: terrace", () => DevGo(new Vector3(0f, 7.8f, 162f), 0f, false));
            Btn("Dragon: start the fight", () => DevGo(new Vector3(0f, 7.8f, 166f), 0f, true));
            Btn("Prisons: Hall of Chains", () => DevGo(new Vector3(0f, 0.3f, 109f), 0f, false));
            Btn("Warden's Office (key)", () => DevGo(new Vector3(42f, 0.3f, 114f), 0f, false));
            Btn("Flooded Gallery", () => DevGo(new Vector3(0f, 0.3f, 70f), 0f, false));
            Btn("Mirror: the long bridge", () => DevGo(new Vector3(0f, 8.2f, 246f), 0f, false));
            Btn("Mirror: the Thornwood", () => DevGo(new Vector3(0f, -23.6f, 356f), 0f, false));
            Btn("Mirror: start the fight", () => DevGo(new Vector3(0f, -23.3f, 488f), 0f, false, 2));
            Btn("Spawn radiant chest (host)", DevSpawnRadiant);
            Btn("Unlock every weapon", () => DevUnlockWeapons(""));
            Btn("Charge the ultimate", () => { PlayerUltimate.Local?.DevCharge(""); SetDevMenu(false); });
            var god = UIFactory.Button(content, "Invincible: OFF", new Vector2(0.5f, 1), new Vector2(0, y - 14), 250, DevToggleGod, true); y -= 34;
            _devGodLabel = god.GetComponentInChildren<Text>();
            UIFactory.Label(content, "Host: also opens the doors and moves the checkpoint", new Vector2(0.5f, 1), new Vector2(0, y - 8), new Vector2(290, 16), 11, skin.bone4);
            y -= 22;
            Btn("Close  (F9)", () => SetDevMenu(false), true);
            _dev.gameObject.SetActive(false);
        }

        /// <summary>Development: owns every weapon of every class (open the inventory with I).</summary>
        public void DevUnlockWeapons(string _)
        {
            foreach (PlayerClass c in System.Enum.GetValues(typeof(PlayerClass))) { Inventory.GrantRelic(c); Inventory.GrantDragon(c); Inventory.GrantRadiant(c); }
            for (byte s = 1; s < Inventory.ArmorSetCount; s++) Inventory.GrantArmorSet(s);
            ShowMessage("ARMOURY OPENED", "Every weapon unlocked · press I", skin.gold2, 3f, 36);
        }

        /// <summary>Admin (host only): the radiant chest rises in front of you (replaces the vault's, if any).</summary>
        public void DevSpawnRadiant()
        {
            var p = PlayerController.Local;
            var nb = NetworkBootstrap.Instance;
            if (p == null || nb == null || nb.Manager == null || !nb.Manager.IsServer) { ShowMessage("HOST ONLY", "Only the host can summon the radiant chest", skin.bone3, 2.5f, 32); return; }
            Vector3 f = MathUtil.Flat(p.transform.forward).normalized;
            nb.ServerSpawnRadiantChest(p.transform.position + f * 2.6f, Quaternion.LookRotation(-f));
            SetDevMenu(false);
        }

        /// <summary>Development: "0" / "1" / "2" equips that weapon on the local player.</summary>
        public void DevEquip(string w) { PlayerController.Local?.Equip(byte.Parse(w)); }
        /// <summary>Development: wears the whole armour set "n" (0 = own clothes) on the local player.</summary>
        public void DevArmor(string set)
        {
            var p = PlayerController.Local; if (p == null) return;
            byte s = byte.Parse(set);
            for (int i = 0; i < Inventory.ArmorSlotCount; i++) Inventory.SetArmor(p.playerClass, (Inventory.Slot)i, s);
        }

        void SetDevMenu(bool open)
        {
            _dev.gameObject.SetActive(open);
            GameInput.GameplayBlocked = open || _paused;
            GameInput.SetCursorLocked(!open && !_paused);
        }

        void DevToggleGod()
        {
            var p = PlayerController.Local;
            if (p == null) return;
            p.DevGod(p.DevGodEnabled ? "0" : "1");
            if (_devGodLabel) _devGodLabel.text = p.DevGodEnabled ? "Invincible: ON" : "Invincible: OFF";
        }

        /// <summary>Teleports the local player; the host also unlocks the chapter up to there.</summary>
        void DevGo(Vector3 pos, float yaw, bool startDragon) => DevGo(pos, yaw, startDragon, 0);

        /// <param name="startBoss">0 none (or the dragon if startDragon), 2 the Mirror</param>
        void DevGo(Vector3 pos, float yaw, bool startDragon, int startBoss)
        {
            var p = PlayerController.Local;
            if (p == null || p.IsDead) return;
            var prog = CastleProgress.Instance;
            var nm = NetworkBootstrap.Instance ? NetworkBootstrap.Instance.Manager : null;
            if (prog != null && nm != null && nm.IsServer)
            {
                prog.DevProgress("throne");
                if (pos.z > 100f) prog.DevProgress("cp 2");
                if (pos.z > 150f) { prog.DevProgress("great"); prog.DevProgress("cp 3"); }
                if (pos.z > 220f) prog.DevProgress("dragon");
                if (pos.z > 700f) prog.DevProgress("mirror");
            }
            p.TeleportTo(pos, Quaternion.Euler(0f, yaw, 0f));
            if (startDragon && DragonArena.Instance != null) StartCoroutine(DevStartDragon());
            if (startBoss == 2 && MirrorArena.Instance != null) StartCoroutine(DevStartMirror());
            if (startBoss == 3 && ReaperArena.Instance != null) StartCoroutine(DevStartReaper());
            SetDevMenu(false);
        }

        System.Collections.IEnumerator DevStartReaper()
        {
            yield return new WaitForSeconds(0.5f);
            ReaperArena.Instance?.DevStartFight(null);
        }

        System.Collections.IEnumerator DevStartMirror()
        {
            yield return new WaitForSeconds(0.5f);
            MirrorArena.Instance?.DevStartFight(null);
        }

        System.Collections.IEnumerator DevStartDragon()
        {
            yield return new WaitForSeconds(0.5f);
            DragonArena.Instance?.DevStartFight(null);
        }

        // ================================================================== aggiornamento
        void Update()
        {
            bool inSession = NetworkBootstrap.Instance && NetworkBootstrap.Instance.Manager && NetworkBootstrap.Instance.Manager.IsListening;

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f11Key.wasPressedThisFrame)
            {
                bool fs = !Screen.fullScreen;
                GameSettings.SetFullscreen(fs);
                if (_fullscreenToggle) _fullscreenToggle.SetIsOnWithoutNotify(fs);
            }

            if (_menu.gameObject.activeSelf)
            {
                UpdateMenuScreens();
                if (Time.frameCount % 30 == 0)
                {
                    var ips = NetworkBootstrap.GetLocalIPv4();
                    string pub = NetworkBootstrap.Instance ? NetworkBootstrap.Instance.PublicIp : "…";
                    _ipText.text = $"Local IP: {string.Join("  ", ips)}    Public IP: {pub}\nOnline lobbies need no open ports. Only for Join by IP: forward UDP {MenuSettings.LastPort}";
                }
                return;
            }

            bool chatUsed = _chatRoot != null && UpdateChat(inSession);
            if (InputBindings.Listening) { }                      // a rebind is waiting for its key
            else if (chatUsed) { }
            else if (ControlsOpen && (GameInput.PausePressed || GameInput.HelpPressed)) ShowControls(false);
            else if (GameInput.HelpPressed) ShowControls(true);
            else if (inSession && kb != null && kb.f9Key.wasPressedThisFrame && PlayerController.Local != null) SetDevMenu(!DevOpen);
            else if (DevOpen && GameInput.PausePressed) SetDevMenu(false);
            else if (InventoryOpen && (GameInput.PausePressed || GameInput.InventoryPressed)) ShowInventory(false);
            else if (SkillsOpen && (GameInput.PausePressed || GameInput.SkillsPressed)) ShowSkills(false);
            else if (inSession && !_paused && !InventoryOpen && GameInput.SkillsPressed && PlayerController.Local != null) ShowSkills(true);
            else if (inSession && GameInput.PausePressed)
            {
                if (_options.gameObject.activeSelf) ShowOptions(false);
                else SetPaused(!_paused);
            }
            else if (inSession && !_paused && GameInput.InventoryPressed && PlayerController.Local != null) ShowInventory(true);
            UpdateControls();

            UpdateMessage();
            UpdatePlayerHud();
            UpdateParty();
            UpdateBardHud();
            UpdateUltHud();
            UpdateInventory();
            UpdateSoulsHud();
            UpdateCoopHud();
            UpdateBoss();
            UpdateOverheadAndReticle();
            UpdatePrompt();

            var nm = NetworkBootstrap.Instance ? NetworkBootstrap.Instance.Manager : null;
            if (nm && Time.frameCount % 20 == 0)
                _sessionInfo.text = nm.IsHost ? $"HOST · {nm.ConnectedClientsIds.Count} players" : nm.IsConnectedClient ? "CLIENT · connected" : "";
        }

        void UpdateMessage()
        {
            if (!_messageRt.gameObject.activeSelf) return;
            _messageTimer += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(_messageTimer / 0.8f) * Mathf.Clamp01((_messageDuration - _messageTimer) / 0.8f);
            _messageGroup.alpha = a;
            if (_messageTimer >= _messageDuration) _messageRt.gameObject.SetActive(false);
        }

        void UpdatePlayerHud()
        {
            var p = PlayerController.Local;
            if (p == null || p.vitals == null) return;
            _hpBar.SetValue(p.vitals.Health01);
            _staminaBar.SetValue(p.vitals.Stamina01);
            _flaskText.text = p.vitals.Flasks.ToString();
        }

        void UpdateParty()
        {
            // aggiunge/rimuove righe per gli altri giocatori
            foreach (var pc in PlayerController.All)
            {
                if (pc == null || pc == PlayerController.Local || _party.ContainsKey(pc)) continue;
                int idx = _party.Count;
                var row = UIFactory.Rect("Member", _partyRoot, new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -idx * 34), new Vector2(170, 32));
                var nameT = UIFactory.Text(UIFactory.Rect("Name", row, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-4, 0), new Vector2(160, 16)), pc.DisplayName, 14, skin.bone1, TextAnchor.MiddleRight);
                var bar = UIBar.Create(row, skin.barFrameHp, skin.barFillHp, new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -16), 140);
                _party[pc] = (row, nameT, bar);
            }
            List<PlayerController> remove = null;
            int i = 0;
            foreach (var kv in _party)
            {
                if (kv.Key == null || !kv.Key.IsSpawned) { (remove ??= new List<PlayerController>()).Add(kv.Key); continue; }
                kv.Value.root.anchoredPosition = new Vector2(0, -i * 34); i++;
                kv.Value.name.text = (kv.Key.PvpOn ? "<color=#ea6068>[PvP]</color> " : "") + (kv.Key.IsDowned ? kv.Key.DisplayName + $" <color=#ea6068>(down {Mathf.CeilToInt(kv.Key.vitals.BleedLeft)}s)</color>"
                                   : kv.Key.IsDead ? kv.Key.DisplayName + " (fallen)" : kv.Key.DisplayName);
                kv.Value.bar.SetValue(kv.Key.vitals ? kv.Key.vitals.Health01 : 0f);
                UpdatePartyBuff(kv.Key, kv.Value.root);
            }
            if (remove != null) foreach (var r in remove) { if (_party[r].root) Destroy(_party[r].root.gameObject); _party.Remove(r); }
        }

        void UpdateBoss()
        {
            var reaper = ReaperBossAI.Instance;
            if (reaper != null && reaper.IsSpawned && reaper.FightActive)
            {
                bool rshow = reaper.IsAlive;
                if (_bossRoot.gameObject.activeSelf != rshow) { _bossRoot.gameObject.SetActive(rshow); if (rshow) _bossBar.SetValue(1f, true); }
                if (!rshow) return;
                _bossName.text = reaper.Phase >= 2 ? "<color=#b9a0ff>" + reaper.DisplayName + "  · the Final Judgement</color>" : reaper.DisplayName;
                _bossBar.SetValue(reaper.Health01);
                return;
            }
            var mirror = MirrorAI.Instance;
            if (mirror != null && mirror.IsSpawned && mirror.FightActive)
            {
                bool mshow = mirror.IsAlive;
                if (_bossRoot.gameObject.activeSelf != mshow) { _bossRoot.gameObject.SetActive(mshow); if (mshow) _bossBar.SetValue(1f, true); }
                if (!mshow) return;
                _bossName.text = mirror.Phase >= 2 ? "<color=#cdb4ff>" + mirror.DisplayName + "</color>" : mirror.DisplayName;
                _bossBar.SetValue(mirror.Health01);
                return;
            }
            var dragon = DragonAI.Instance;
            if (dragon != null && dragon.IsSpawned && dragon.FightActive)
            {
                bool dshow = dragon.IsAlive;
                if (_bossRoot.gameObject.activeSelf != dshow) { _bossRoot.gameObject.SetActive(dshow); if (dshow) _bossBar.SetValue(1f, true); }
                if (!dshow) return;
                _bossName.text = dragon.DisplayName + (dragon.Phase >= 2 ? "  <color=#ea6068>· enraged</color>" : "");
                _bossBar.SetValue(dragon.Health01);
                return;
            }
            var boss = BossAI.Instance;
            bool show = boss != null && boss.IsSpawned && boss.FightActive && (boss.IsAlive || boss.Health01 > 0f);
            if (boss != null && !boss.IsAlive && _bossRoot.gameObject.activeSelf) show = false;
            if (_bossRoot.gameObject.activeSelf != show) { _bossRoot.gameObject.SetActive(show); if (show) _bossBar.SetValue(1f, true); }
            if (!show) return;
            string phase = boss.Phase >= 2 ? "  <color=#ea6068>· enraged</color>" : "";
            _bossName.text = boss.DisplayName + phase;
            _bossBar.SetValue(boss.Health01);
        }

        void UpdateOverheadAndReticle()
        {
            var cam = SoulsCamera.Instance;
            if (cam == null || cam.Cam == null) return;
            var canvasRect = _root.rect;

            // mirino
            var target = cam.CurrentTarget;
            if (target != null)
            {
                Vector3 vp = cam.Cam.WorldToViewportPoint(target.Point);
                bool vis = vp.z > 0;
                _reticle.gameObject.SetActive(vis);
                if (vis) _reticle.anchoredPosition = new Vector2(Mathf.Round(vp.x * canvasRect.width), Mathf.Round(vp.y * canvasRect.height));
            }
            else _reticle.gameObject.SetActive(false);

            // crosshair dell'arciere
            var lp = PlayerController.Local;
            bool cross = lp != null && lp.ShowCrosshair && !_paused;
            if (cross)
            {
                Vector3 aimAt = lp.PredictAimPoint(lp.bow ? lp.bow.GripPosition : lp.transform.position + Vector3.up * 1.5f);
                Vector3 avp = cam.Cam.WorldToViewportPoint(aimAt);
                cross = avp.z > 0f;
                if (cross)
                {
                    _crosshair.anchoredPosition = new Vector2(Mathf.Round(avp.x * canvasRect.width), Mathf.Round(avp.y * canvasRect.height));
                    bool hot = lp.IsAiming || (lp.bow && lp.bow.IsDrawn);
                    _crosshairImg.color = new Color(0.95f, 0.9f, 0.8f, hot ? 0.95f : 0.45f);
                    float sc = hot ? 1f : 0.8f;
                    _crosshair.localScale = new Vector3(sc, sc, 1f);
                }
            }
            if (_crosshair.gameObject.activeSelf != cross) _crosshair.gameObject.SetActive(cross);

            // barre sui nemici minori (non il boss): visibili se agganciati o colpiti di recente
            foreach (var e in EnemyBase.All)
            {
                if (e == null || e is BossAI || e is DragonAI || e is MirrorAI) continue;
                bool show = e.IsAlive && (Time.time - e.LastDamagedTime < 6f || e.IsCursed || (target != null && target.gameObject == e.gameObject));
                Vector3 vp = cam.Cam.WorldToViewportPoint(e.transform.position + Vector3.up * 2.25f);
                show &= vp.z > 0 && vp.z < 30f;
                if (!_overhead.TryGetValue(e, out var entry))
                {
                    if (!show) continue;
                    var rt = UIFactory.Rect("EnemyBar", _overheadRoot, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80, 15));
                    var bar = UIBar.Create(rt, skin.barFrameHp, skin.barFillHp, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, 80);
                    entry = (rt, bar);
                    _overhead[e] = entry;
                    bar.SetValue(e.Health01, true);
                }
                entry.root.gameObject.SetActive(show);
                if (!show) continue;
                entry.root.anchoredPosition = new Vector2(Mathf.Round(vp.x * canvasRect.width), Mathf.Round(vp.y * canvasRect.height));
                entry.bar.SetValue(e.Health01);
                UpdateEnemyCurse(e, entry.root);
            }
            UpdateNameTags(cam, canvasRect);
        }

        void UpdatePrompt()
        {
            var arena = BossArena.Instance;
            string prompt = null;
            bool can = false;
            var chest = RewardChest.Instance;
            if (chest != null && chest.IsSpawned) can = chest.LocalCanInteract(out prompt);
            var hoard = RewardChest.DragonInstance;
            if (prompt == null && hoard != null && hoard.IsSpawned) can = hoard.LocalCanInteract(out prompt);
            if (prompt == null) can = arena != null && arena.IsSpawned && arena.LocalCanInteract(out prompt);
            var darena = DragonArena.Instance;
            if (prompt == null && darena != null && darena.IsSpawned) can = darena.LocalCanInteract(out prompt);
            var marena = MirrorArena.Instance;
            if (prompt == null && marena != null && marena.IsSpawned) can = marena.LocalCanInteract(out prompt);
            var rarena = ReaperArena.Instance;
            if (prompt == null && rarena != null && rarena.IsSpawned) can = rarena.LocalCanInteract(out prompt);
            if (prompt == null)
                foreach (var it in Interactables.All)
                {
                    if (it == null) continue;
                    can = it.LocalCanInteract(out prompt);
                    if (prompt != null) break;
                }
            bool show = prompt != null;
            if (_promptRt.gameObject.activeSelf != show) _promptRt.gameObject.SetActive(show);
            if (show)
            {
                _promptText.text = prompt;
                _promptRt.GetChild(0).gameObject.SetActive(can);
            }
        }
    }
}
