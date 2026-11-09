using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DarkFantasy.UI
{
    /// <summary>
    /// Chat (Enter opens the box, Enter sends, Esc closes): lines appear on the right of every
    /// player's screen and fade after a few seconds; with the box open the recent history stays.
    /// Also the NPC dialogue panel (bottom centre: speaker, text, "E · continue").
    /// </summary>
    public partial class GameUI
    {
        const int ChatLines = 9;
        const float ChatShow = 4f, ChatFade = 0.8f;
        RectTransform _chatRoot, _chatFeed;
        InputField _chatField;
        bool _chatOpen;
        int _chatOpenFrame;
        readonly List<(RectTransform rt, CanvasGroup g, float born)> _chatItems = new List<(RectTransform, CanvasGroup, float)>();

        RectTransform _dlgRoot;
        Text _dlgName, _dlgText, _dlgHint;

        void BuildChat()
        {
            _chatRoot = UIFactory.Rect("Chat", _hud, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-18f, -40f), new Vector2(380f, 330f));
            _chatFeed = UIFactory.Rect("Feed", _chatRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(380f, 290f));
            var box = UIFactory.Rect("Box", _chatRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(380f, 32f));
            UIFactory.Image(box, skin.panelClean);
            _chatField = UIFactory.Field(box, "", "Say something…  (Enter: send · Esc: close)", new Vector2(0.5f, 0.5f), Vector2.zero, 360f);
            _chatField.characterLimit = 120;
            _chatField.lineType = InputField.LineType.SingleLine;
            box.gameObject.SetActive(false);

            PlayerVitals.ChatReceived += OnChat;
            LoreNpc.Talk += OnNpcTalk;
            LoreNpc.Silence += OnNpcSilence;

            _dlgRoot = UIFactory.Rect("Dialogue", _hud, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(760f, 150f));
            UIFactory.Image(_dlgRoot, skin.speechOrnate ? skin.speechOrnate : skin.panelClean);
            _dlgName = UIFactory.Text(UIFactory.Rect("Name", _dlgRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36f, -14f), new Vector2(500f, 26f)), "", 20, skin.gold2, TextAnchor.MiddleLeft, true);
            _dlgText = UIFactory.Text(UIFactory.Stretch("Text", _dlgRoot, 36f, 44f, 36f, 30f), "", 16, skin.bone0, TextAnchor.UpperLeft);
            _dlgText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _dlgHint = UIFactory.Text(UIFactory.Rect("Hint", _dlgRoot, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 10f), new Vector2(360f, 18f)), "", 12, skin.bone3, TextAnchor.LowerRight);
            _dlgRoot.gameObject.SetActive(false);
        }

        void OnDestroyChat()
        {
            PlayerVitals.ChatReceived -= OnChat;
            LoreNpc.Talk -= OnNpcTalk;
            LoreNpc.Silence -= OnNpcSilence;
        }

        static string ClassColor(PlayerClass c) => c == PlayerClass.Archer ? "#8fd18a" : c == PlayerClass.Bard ? "#8fc2ff" : c == PlayerClass.Reaper ? "#c79bff" : c == PlayerClass.Smoker ? "#b9b0a2" : "#f8b74c";

        void OnChat(PlayerVitals from, string text)
        {
            var pc = from ? from.GetComponent<PlayerController>() : null;
            string name = from ? from.DisplayName : "?";
            string col = pc ? ClassColor(pc.playerClass) : "#e8d0b5";
            AddChatLine($"<color={col}><b>{Escape(name)}</b></color>: {Escape(text)}");
            Sfx.Play2D(SfxId.UiClick, 0.35f);
        }

        /// <summary>Plain text in, nothing the rich-text parser could eat.</summary>
        static string Escape(string s) => s.Replace("<", "‹").Replace(">", "›");

        void AddChatLine(string rich)
        {
            var rt = UIFactory.Rect("Line", _chatFeed, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(380f, 26f));
            UIFactory.Image(rt, null, false, new Color(0.05f, 0.03f, 0.03f, 0.55f));
            var t = UIFactory.Text(UIFactory.Stretch("T", rt, 8f, 2f, 8f, 2f), rich, 14, skin.bone0, TextAnchor.MiddleLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            // grow to fit two lines when needed
            float h = t.preferredHeight > 22f ? 42f : 26f;
            rt.sizeDelta = new Vector2(380f, h);
            var g = rt.gameObject.AddComponent<CanvasGroup>(); g.blocksRaycasts = false;
            _chatItems.Add((rt, g, Time.unscaledTime));
            while (_chatItems.Count > 30) { Destroy(_chatItems[0].rt.gameObject); _chatItems.RemoveAt(0); }
        }

        /// <summary>Returns true when the chat took this frame's keys (so Esc doesn't also pause).</summary>
        bool UpdateChat(bool inSession)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool enter = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
            bool used = false;
            if (!_chatOpen)
            {
                if (enter && inSession && PlayerController.Local != null && !_paused && !DevOpen && !InventoryOpen) { SetChat(true); used = true; }
            }
            else
            {
                if (kb != null && kb.escapeKey.wasPressedThisFrame) { SetChat(false); used = true; }
                else if (enter && Time.frameCount > _chatOpenFrame)
                {
                    string msg = _chatField.text;
                    if (!string.IsNullOrWhiteSpace(msg)) PlayerController.Local?.vitals?.SendChat(msg);
                    SetChat(false);
                    used = true;
                }
                else used = true;
            }
            LayoutChat();
            return used;
        }

        void SetChat(bool open)
        {
            _chatOpen = open;
            GameInput.ChatOpen = open;
            _chatField.transform.parent.gameObject.SetActive(open);
            if (open)
            {
                _chatOpenFrame = Time.frameCount;
                _chatField.text = "";
                _chatField.Select();
                _chatField.ActivateInputField();
            }
            else
            {
                _chatField.DeactivateInputField();
                if (UnityEngine.EventSystems.EventSystem.current) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }
        }

        /// <summary>Newest at the bottom; old lines fade unless the box is open.</summary>
        void LayoutChat()
        {
            float y = 0f;
            int shown = 0;
            for (int i = _chatItems.Count - 1; i >= 0; i--)
            {
                var (rt, g, born) = _chatItems[i];
                float age = Time.unscaledTime - born;
                float a = _chatOpen ? 1f : Mathf.Clamp01((ChatShow + ChatFade - age) / ChatFade);
                bool vis = a > 0.01f && shown < (_chatOpen ? ChatLines : 6);
                if (rt.gameObject.activeSelf != vis) rt.gameObject.SetActive(vis);
                if (!vis) continue;
                g.alpha = a;
                rt.anchoredPosition = new Vector2(0f, y);
                y += rt.sizeDelta.y + 4f;
                shown++;
            }
        }

        // ------------------------------------------------------------------ NPC dialogue
        void OnNpcTalk(string speaker, string text, int page, int pages)
        {
            _dlgRoot.gameObject.SetActive(true);
            _dlgName.text = speaker;
            _dlgText.text = text;
            _dlgHint.text = page + 1 < pages ? $"E · continue   ({page + 1}/{pages})" : "E · farewell";
        }

        void OnNpcSilence() { if (_dlgRoot) _dlgRoot.gameObject.SetActive(false); }
    }
}
