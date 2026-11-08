using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DarkFantasy.UI
{
    /// <summary>
    /// Co-op HUD: the TAB scoreboard (name, class, weapon, kills / deaths / revives, status),
    /// the "you are down" panel (bleed-out timer, revive progress, hold SPACE to give up), the
    /// spectator banner after falling in a boss fight, and markers over fallen allies.
    /// </summary>
    public partial class GameUI
    {
        RectTransform _scoreRoot, _downRoot, _specRoot;
        Text _downTitle, _downSub, _downHint, _specText;
        UIBar _downBar;
        Image _giveUpFill;
        readonly List<Text[]> _scoreRows = new List<Text[]>();
        readonly Dictionary<PlayerController, (RectTransform root, Text text)> _downMarks = new Dictionary<PlayerController, (RectTransform, Text)>();
        // column left edges and widths (row-local); labels are pivoted on their LEFT edge
        static readonly float[] ColX = { 0, 160, 226, 392, 438, 486, 542, 588, 650 };
        static readonly float[] ColW = { 156, 64, 162, 44, 46, 54, 44, 58, 76 };
        static readonly string[] ColHead = { "PLAYER", "CLASS", "WEAPON", "KILLS", "DEATHS", "REVIVES", "PVP", "PING", "STATUS" };
        const int ColStatus = 8, ColPvp = 6, ColPing = 7;

        Text Cell(Transform row, int c, string text, int size, Color color)
        {
            var rt = UIFactory.Rect("C" + c, row, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(ColX[c], 0), new Vector2(ColW[c], 30));
            var t = UIFactory.Text(rt, text, size, color, c < 3 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.resizeTextForBestFit = true; t.resizeTextMinSize = 9; t.resizeTextMaxSize = size;
            return t;
        }

        bool _devBoard;
        Text _pvpButtonText;
        RectTransform _pvpBadge;

        void TogglePvp()
        {
            var me = PlayerController.Local;
            if (me == null || me.vitals == null) return;
            me.vitals.SetPvp(!me.vitals.PvpOn);
            RefreshPvpButton(!me.vitals.PvpOn);
        }

        void RefreshPvpButton(bool on)
        {
            if (_pvpButtonText) { _pvpButtonText.text = on ? "PvP: ON" : "PvP: OFF"; _pvpButtonText.color = on ? skin.hp2 : skin.bone1; }
        }

        void OnPvpChanged(PlayerVitals v, bool on)
        {
            var me = PlayerController.Local;
            bool mine = me != null && me.vitals == v;
            if (mine) ShowMessage(on ? "PVP ENABLED" : "PVP DISABLED", on ? "Others with PvP on can now strike you, and you them" : "You can no longer hurt or be hurt by other players", on ? skin.hp2 : skin.bone0, 3f, 40);
            else ShowMessage(on ? "A RIVAL EMERGES" : "A TRUCE", v.DisplayName + (on ? " enabled PvP" : " disabled PvP"), on ? skin.hp2 : skin.bone1, 2.5f, 32);
        }

        void OnPvpKill(PlayerController killer, PlayerController victim)
        {
            var me = PlayerController.Local;
            string title = killer == me ? "RIVAL SLAIN" : victim == me ? "SLAIN BY A RIVAL" : "BLOOD SPILLED";
            ShowMessage(title, $"{killer.DisplayName} killed {victim.DisplayName}", skin.hp1, 3f, 40);
        }

        /// <summary>Development: "1"/"0" turns the local player's PvP on/off.</summary>
        public void DevPvp(string on) { var me = PlayerController.Local; if (me && me.vitals) { me.vitals.SetPvp(on == "1"); RefreshPvpButton(on == "1"); } }
        /// <summary>Development: "1" shows the scoreboard without holding TAB.</summary>
        public void DevScoreboard(string on) => _devBoard = on == "1";
        /// <summary>Development (bridge): sends a chat line as the local player.</summary>
        public void DevChat(string text) => PlayerController.Local?.vitals?.SendChat(text);

        void BuildCoopHud()
        {
            // ---- PvP badge under the bars (only while it is on)
            _pvpBadge = UIFactory.Rect("PvpBadge", _hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(58, -70), new Vector2(60, 18));
            UIFactory.Image(_pvpBadge, null, false, new Color(0.35f, 0.02f, 0.03f, 0.85f));
            UIFactory.Text(UIFactory.Stretch("T", _pvpBadge), "PvP ON", 13, skin.hp2);
            _pvpBadge.gameObject.SetActive(false);

            // ---- scoreboard
            _scoreRoot = UIFactory.Rect("Scoreboard", _hud, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(830, 320));
            UIFactory.Image(_scoreRoot, skin.windowFleur);
            UIFactory.Label(_scoreRoot, "THE COMPANY", new Vector2(0.5f, 1f), new Vector2(0, -26), new Vector2(400, 26), 24, skin.bone0, TextAnchor.MiddleCenter, true);
            var hdr = UIFactory.Rect("Header", _scoreRoot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -64), new Vector2(742, 18));
            for (int c = 0; c < ColHead.Length; c++) Cell(hdr, c, ColHead[c], 12, skin.gold2);
            var line = UIFactory.Rect("Line", _scoreRoot, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -84), new Vector2(740, 1));
            UIFactory.Image(line, null, false, new Color(skin.bone4.r, skin.bone4.g, skin.bone4.b, 0.8f));
            for (int i = 0; i < 4; i++)
            {
                var row = UIFactory.Rect("Row" + i, _scoreRoot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -104 - i * 42), new Vector2(742, 36));
                if (i % 2 == 0) UIFactory.Image(row, null, false, new Color(1f, 0.9f, 0.7f, 0.05f));
                var cells = new Text[ColHead.Length];
                for (int c = 0; c < ColHead.Length; c++) cells[c] = Cell(row, c, "", c == 0 ? 16 : 14, skin.bone1);
                _scoreRows.Add(cells);
            }
            UIFactory.Label(_scoreRoot, "hold TAB", new Vector2(0.5f, 0f), new Vector2(0, 26), new Vector2(200, 14), 10, skin.bone4);
            _scoreRoot.gameObject.SetActive(false);

            // ---- downed panel (local player on the ground)
            _downRoot = UIFactory.Rect("Downed", _hud, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(520, 150));
            UIFactory.Image(UIFactory.Stretch("Band", _downRoot, 0, 10, 0, 10), null, false, new Color(0.05f, 0f, 0f, 0.55f));
            _downTitle = UIFactory.Text(UIFactory.Rect("Title", _downRoot, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(500, 44)), "YOU ARE DOWN", 40, skin.hp1, TextAnchor.MiddleCenter, true);
            _downSub = UIFactory.Text(UIFactory.Rect("Sub", _downRoot, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -62), new Vector2(500, 20)), "", 16, skin.bone1);
            _downBar = UIBar.Create(_downRoot, skin.barFrameHp, skin.barFillHp, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -88), 300);
            var hint = UIFactory.Rect("Hint", _downRoot, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(460, 24));
            var key = UIFactory.Rect("Key", hint, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(40, 0), new Vector2(64, 24));
            UIFactory.Image(key, skin.keycap);
            var fill = UIFactory.Rect("Fill", key, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(64, 24));
            _giveUpFill = UIFactory.Image(fill, null, false, new Color(1f, 0.6f, 0.25f, 0.45f));
            UIFactory.Text(UIFactory.Stretch("K", key, 0, 3, 0, 6), "SPACE", 12, skin.bone0);
            _downHint = UIFactory.Text(UIFactory.Rect("T", hint, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(114, 0), new Vector2(340, 22)), "hold: give up and return to the last flame", 14, skin.bone3, TextAnchor.MiddleLeft);
            _downRoot.gameObject.SetActive(false);

            // ---- spectator banner
            _specRoot = UIFactory.Rect("Spectate", _hud, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(560, 56));
            UIFactory.Image(_specRoot, null, false, new Color(0, 0, 0, 0.55f));
            _specText = UIFactory.Text(UIFactory.Stretch("T", _specRoot, 10, 4, 10, 4), "", 16, skin.bone1);
            _specRoot.gameObject.SetActive(false);
        }

        void UpdateCoopHud()
        {
            var me = PlayerController.Local;
            bool hudOn = me != null && !_paused && !InventoryOpen;
            bool pvpOn = me != null && me.PvpOn;
            if (_pvpBadge.gameObject.activeSelf != pvpOn) _pvpBadge.gameObject.SetActive(pvpOn);
            if (_paused && Time.frameCount % 10 == 0) RefreshPvpButton(pvpOn);

            // ---- scoreboard (hold TAB)
            bool board = hudOn && (GameInput.ScoreboardHeld || _devBoard);
            if (_scoreRoot.gameObject.activeSelf != board) _scoreRoot.gameObject.SetActive(board);
            if (board)
            {
                var list = new List<PlayerController>();
                foreach (var p in PlayerController.All) if (p && p.IsSpawned) list.Add(p);
                list.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
                for (int i = 0; i < _scoreRows.Count; i++)
                {
                    var cells = _scoreRows[i];
                    bool has = i < list.Count;
                    cells[0].transform.parent.gameObject.SetActive(has);
                    if (!has) continue;
                    var p = list[i]; var v = p.vitals;
                    bool mine = p == me;
                    cells[0].text = (mine ? "<color=#f8b74c>»</color> " : "") + p.DisplayName;
                    cells[1].text = ClassName(p.playerClass);
                    cells[2].text = Inventory.Name(p.playerClass, p.EquippedWeapon);
                    cells[2].color = p.EquippedWeapon == Inventory.Radiant ? new Color(1f, 0.95f, 0.75f) : p.EquippedWeapon == Inventory.Dragon ? new Color(1f, 0.42f, 0.25f) : p.EquippedWeapon == Inventory.Relic ? skin.gold2 : skin.bone1;
                    if (p.WeaponDormant) cells[2].text += " <color=#7d6a5e>(dormant)</color>";
                    int ping = v ? v.Ping : 0;
                    bool host = p.OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId;
                    cells[ColPing].text = host ? "host" : ping + " ms";
                    cells[ColPing].color = host ? skin.gold2 : ping < 80 ? new Color(0.55f, 0.85f, 0.55f) : ping < 160 ? skin.gold2 : skin.hp2;
                    cells[3].text = v ? v.Kills.ToString() : "-";
                    cells[4].text = v ? v.Deaths.ToString() : "-";
                    cells[5].text = v ? v.Revives.ToString() : "-";
                    string st; Color sc;
                    if (v == null) { st = ""; sc = skin.bone3; }
                    else if (v.IsDowned) { st = v.ReviveState == 2 ? "reviving" : $"down {Mathf.CeilToInt(v.BleedLeft)}s"; sc = skin.hp2; }
                    else if (v.IsDead) { st = "fallen"; sc = skin.bone4; }
                    else { st = "alive"; sc = new Color(0.55f, 0.85f, 0.55f); }
                    cells[ColStatus].text = st; cells[ColStatus].color = sc;
                    bool pvp = v && v.PvpOn;
                    cells[ColPvp].text = pvp ? "ON" : "off"; cells[ColPvp].color = pvp ? skin.hp2 : skin.bone4;
                }
            }

            // ---- downed (local)
            bool down = hudOn && me.IsDowned;
            if (_downRoot.gameObject.activeSelf != down) _downRoot.gameObject.SetActive(down);
            if (down)
            {
                var v = me.vitals;
                if (v.ReviveState == 2)
                {
                    _downTitle.text = "BEING REVIVED"; _downTitle.color = new Color(0.55f, 1f, 0.6f);
                    _downSub.text = $"{Mathf.RoundToInt(v.Revive01 * 100)}%  ·  the bleeding is held back";
                    _downBar.SetValue(v.Revive01);
                }
                else
                {
                    _downTitle.text = "YOU ARE DOWN"; _downTitle.color = skin.hp1;
                    _downSub.text = v.ReviveState == 1
                        ? $"An ally is coming… bleeding out in {v.BleedLeft:0.0} s"
                        : $"Bleeding out in {v.BleedLeft:0.0} s  ·  an ally can revive you (E)";
                    _downBar.SetValue(v.BleedLeft / PlayerVitals.BleedOutTime);
                }
                var fr = _giveUpFill.rectTransform; fr.sizeDelta = new Vector2(64f * me.GiveUp01, 24f);
            }

            // ---- spectating after falling in a boss fight
            bool spec = me != null && me.IsSpectating;
            if (_specRoot.gameObject.activeSelf != spec) _specRoot.gameObject.SetActive(spec);
            if (spec)
            {
                var t = me.SpectateTarget;
                _specText.text = t != null
                    ? $"<b>YOU HAVE FALLEN</b> · watching <color=#f8b74c>{t.DisplayName}</color>\n<size=12>Click: next ally · you rise at the flame when the battle ends</size>"
                    : "<b>YOU HAVE FALLEN</b>\n<size=12>you rise at the flame when the battle ends</size>";
            }

            UpdateDownedMarkers(me);
        }

        static string ClassName(PlayerClass c) => c == PlayerClass.Archer ? "Archer" : c == PlayerClass.Bard ? "Bard" : "Knight";

        /// <summary>Over every fallen ally: name, bleed-out countdown and the revive hint.</summary>
        void UpdateDownedMarkers(PlayerController me)
        {
            var cam = SoulsCamera.Instance;
            var rect = _root.rect;
            List<PlayerController> gone = null;
            foreach (var kv in _downMarks) if (kv.Key == null || !kv.Key.IsSpawned) (gone ??= new List<PlayerController>()).Add(kv.Key);
            if (gone != null) foreach (var g in gone) { if (_downMarks[g].root) Destroy(_downMarks[g].root.gameObject); _downMarks.Remove(g); }
            foreach (var p in PlayerController.All)
            {
                if (p == null || p == me) continue;
                bool show = p.IsDowned && cam && cam.Cam && !_paused;
                Vector3 vp = show ? cam.Cam.WorldToViewportPoint(p.transform.position + Vector3.up * 1.2f) : Vector3.zero;
                show &= vp.z > 0f && vp.z < 60f;
                if (!_downMarks.TryGetValue(p, out var m))
                {
                    if (!show) continue;
                    var rt = UIFactory.Rect("DownMark", _overheadRoot, Vector2.zero, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(200, 40));
                    var t = UIFactory.Text(UIFactory.Stretch("T", rt), "", 14, skin.hp2);
                    m = (rt, t);
                    _downMarks[p] = m;
                }
                m.root.gameObject.SetActive(show);
                if (!show) continue;
                m.root.anchoredPosition = new Vector2(Mathf.Round(vp.x * rect.width), Mathf.Round(vp.y * rect.height));
                var v = p.vitals;
                string state = v.ReviveState == 2 ? $"<color=#8cf59a>reviving {Mathf.RoundToInt(v.Revive01 * 100)}%</color>"
                             : v.ReviveState == 1 ? $"stand in the circle! {v.BleedLeft:0}s" : $"E: revive · {v.BleedLeft:0}s";
                m.text.text = $"<b>{p.DisplayName}</b>\n{state}";
            }
        }
    }
}
