using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DarkFantasy.UI
{
    /// <summary>
    /// The Controls window (F1, or Pause → Controls): every action with its keyboard/mouse input and
    /// its gamepad button, both shown as real prompts. Click a cell, then press the new key / button
    /// (Esc or Start cancels); an input already in use swaps with the old one. Saved at once.
    /// </summary>
    public partial class GameUI
    {
        class BindCell { public GameAction action; public bool pad; public InputGlyph glyph; public Text wait; public Image frame; }
        readonly List<BindCell> _bindCells = new List<BindCell>();
        Text _controlsHint;

        void BuildHelp()
        {
            var content = UIFactory.Window(_root, "CONTROLS", new Vector2(940, 560), out _help);
            _bindCells.Clear();

            void Header(float x, float y, string t)
            {
                UIFactory.Text(UIFactory.Rect("H", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, y), new Vector2(420, 20)), t, 15, skin.gold2, TextAnchor.MiddleLeft, true);
                UIFactory.Text(UIFactory.Rect("HK", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x + 236, y), new Vector2(96, 20)), "Keyboard", 11, skin.bone4, TextAnchor.MiddleCenter);
                UIFactory.Text(UIFactory.Rect("HP", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x + 340, y), new Vector2(70, 20)), "Gamepad", 11, skin.bone4, TextAnchor.MiddleCenter);
            }

            void Cell(float x, float y, GameAction a, bool pad, float w)
            {
                var rt = UIFactory.Rect(pad ? "Pad" : "Key", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, y), new Vector2(w, 26));
                var img = UIFactory.Image(rt, skin.fieldIdle);
                var c = new BindCell { action = a, pad = pad, frame = img };
                if (!pad || InputBindings.HasPad(a))
                {
                    img.raycastTarget = true;
                    var btn = rt.gameObject.AddComponent<Button>();
                    btn.targetGraphic = img;
                    btn.onClick.AddListener(() => { InputBindings.BeginListen(a, pad, null); });
                    c.glyph = InputGlyph.Create(rt, a, new Vector2(0.5f, 0.5f), Vector2.zero, 20f, pad ? 1 : 0);
                    c.wait = UIFactory.Text(UIFactory.Stretch("Wait", rt, 2, 2, 2, 2), pad ? "press a button" : "press a key", 10, skin.gold2);
                    c.wait.gameObject.SetActive(false);
                }
                else
                {
                    UIFactory.Text(UIFactory.Stretch("Fixed", rt, 2, 2, 2, 2), a <= GameAction.MoveRight ? "L-stick" : "—", 11, skin.bone4);
                    img.color = new Color(1f, 1f, 1f, 0.45f);
                }
                _bindCells.Add(c);
            }

            void Row(float x, ref float y, GameAction a)
            {
                UIFactory.Text(UIFactory.Rect("N", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, y), new Vector2(230, 26)), InputBindings.Name(a), 14, skin.bone1, TextAnchor.MiddleLeft);
                Cell(x + 236, y, a, false, 96);
                Cell(x + 340, y, a, true, 70);
                y -= 27;
            }

            void Fixed(float x, ref float y, string name, string key, string pad)
            {
                UIFactory.Text(UIFactory.Rect("N", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, y), new Vector2(230, 22)), name, 13, skin.bone3, TextAnchor.MiddleLeft);
                UIFactory.Text(UIFactory.Rect("K", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x + 236, y), new Vector2(96, 22)), key, 12, skin.bone3);
                UIFactory.Text(UIFactory.Rect("P", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x + 340, y), new Vector2(70, 22)), pad, 12, skin.bone3);
                y -= 24;
            }

            float lx = 8f, rx = 470f, y = -6f;
            Header(lx, y, "MOVEMENT"); y -= 24;
            foreach (var a in new[] { GameAction.MoveForward, GameAction.MoveBack, GameAction.MoveLeft, GameAction.MoveRight, GameAction.Sprint, GameAction.Roll, GameAction.Crouch }) Row(lx, ref y, a);
            Fixed(lx, ref y, "Camera", "Mouse", "R-stick");
            y -= 6; Header(lx, y, "COMBAT"); y -= 24;
            foreach (var a in new[] { GameAction.Attack, GameAction.Heavy, GameAction.Block, GameAction.Parry, GameAction.LockOn }) Row(lx, ref y, a);
            Fixed(lx, ref y, "Switch target", "flick mouse", "R-stick");

            y = -6f;
            Header(rx, y, "ITEMS · SKILLS"); y -= 24;
            foreach (var a in new[] { GameAction.Flask, GameAction.Slot1, GameAction.Slot2, GameAction.Slot3, GameAction.Slot4, GameAction.Interact }) Row(rx, ref y, a);
            y -= 6; Header(rx, y, "MENUS"); y -= 24;
            foreach (var a in new[] { GameAction.Inventory, GameAction.Skills, GameAction.Scoreboard }) Row(rx, ref y, a);
            Fixed(rx, ref y, "Pause", "Esc", InputBindings.PlayStationPad ? "Options" : "Menu");
            Fixed(rx, ref y, "Chat", "Enter", "—");
            Fixed(rx, ref y, "Controls", "F1", "—");

            _controlsHint = UIFactory.Text(UIFactory.Rect("Hint", content, new Vector2(0, 0), new Vector2(0, 0), new Vector2(8, 4), new Vector2(560, 20)),
                "Click a box, then press the new key or button · Esc / Start cancels · a key already in use swaps places", 11, skin.bone4, TextAnchor.MiddleLeft);
            UIFactory.Button(content, "Reset to defaults", new Vector2(1, 0), new Vector2(-250, 18), 180, () => InputBindings.ResetAll(), true);
            UIFactory.Button(content, "Close", new Vector2(1, 0), new Vector2(-80, 18), 130, () => ShowControls(false));
            _help.gameObject.SetActive(false);
        }

        public void DevControls(string on) => ShowControls(on == "1");

        public bool ControlsOpen => _help != null && _help.gameObject.activeSelf;

        void ShowControls(bool on)
        {
            if (_help == null) return;
            _help.gameObject.SetActive(on);
            if (!on && InputBindings.Listening) InputBindings.TickListen();
            bool blocked = on || _paused || InventoryOpen || SkillsOpen;
            GameInput.GameplayBlocked = blocked;
            if (PlayerController.Local != null) GameInput.SetCursorLocked(!blocked);
        }

        /// <summary>Every frame: catches the key for a pending rebind and marks the cell that waits for it.</summary>
        void UpdateControls()
        {
            if (_help == null) return;
            if (!_help.gameObject.activeSelf) { if (InputBindings.Listening) InputBindings.TickListen(); return; }
            InputBindings.TickListen();
            foreach (var c in _bindCells)
            {
                if (c.glyph == null) continue;
                bool waiting = InputBindings.Listening && InputBindings.ListeningAction == c.action && InputBindings.ListeningPad == c.pad;
                if (c.wait.gameObject.activeSelf != waiting) { c.wait.gameObject.SetActive(waiting); c.glyph.gameObject.SetActive(!waiting); }
                c.frame.sprite = waiting ? skin.fieldFocus : skin.fieldIdle;
            }
        }
    }
}
