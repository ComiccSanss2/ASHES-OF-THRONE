using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using IKey = UnityEngine.InputSystem.Key;
using UnityEngine.InputSystem.Controls;

namespace DarkFantasy
{
    /// <summary>Every rebindable action of the game.</summary>
    public enum GameAction
    {
        MoveForward, MoveBack, MoveLeft, MoveRight,
        Sprint, Roll, Crouch,
        Attack, Heavy, Block, Parry, LockOn,
        Interact, Flask,
        Slot1, Slot2, Slot3, Slot4,
        Inventory, Skills, Scoreboard,
    }

    /// <summary>
    /// The player's key map: one keyboard/mouse input and one gamepad button per action, saved in
    /// PlayerPrefs. Keyboard inputs are a <see cref="Key"/> (≥ 0) or a mouse button (−1 left … −5
    /// forward); gamepad inputs are a <see cref="GamepadButton"/>. Movement keeps the arrows and the
    /// left stick as fixed extras; Esc / Start (pause) and Enter (chat) are not rebindable.
    /// </summary>
    public static class InputBindings
    {
        public const int None = -100;
        public const int MouseLeft = -1, MouseRight = -2, MouseMiddle = -3, MouseBack = -4, MouseForward = -5;

        static readonly int[] _kb = new int[Count], _pad = new int[Count];
        static bool _loaded;

        /// <summary>Bumped on every change (glyphs and labels refresh on it).</summary>
        public static int Version { get; private set; }
        public static int Count => Enum.GetValues(typeof(GameAction)).Length;

        public static int DefaultKey(GameAction a) => a switch
        {
            GameAction.MoveForward => (int)IKey.W, GameAction.MoveBack => (int)IKey.S, GameAction.MoveLeft => (int)IKey.A, GameAction.MoveRight => (int)IKey.D,
            GameAction.Sprint => (int)IKey.LeftShift, GameAction.Roll => (int)IKey.Space, GameAction.Crouch => (int)IKey.LeftCtrl,
            GameAction.Attack => MouseLeft, GameAction.Heavy => (int)IKey.F, GameAction.Block => MouseRight, GameAction.Parry => (int)IKey.Q,
            GameAction.LockOn => MouseMiddle, GameAction.Interact => (int)IKey.E, GameAction.Flask => (int)IKey.R,
            GameAction.Slot1 => (int)IKey.Digit1, GameAction.Slot2 => (int)IKey.Digit2, GameAction.Slot3 => (int)IKey.Digit3, GameAction.Slot4 => (int)IKey.Digit4,
            GameAction.Inventory => (int)IKey.I, GameAction.Skills => (int)IKey.K, GameAction.Scoreboard => (int)IKey.Tab,
            _ => None,
        };

        public static int DefaultPad(GameAction a) => a switch
        {
            GameAction.Sprint => (int)GamepadButton.East, GameAction.Roll => (int)GamepadButton.East, GameAction.Crouch => (int)GamepadButton.LeftStick,
            GameAction.Attack => (int)GamepadButton.RightShoulder, GameAction.Heavy => (int)GamepadButton.RightTrigger,
            GameAction.Block => (int)GamepadButton.LeftShoulder, GameAction.Parry => (int)GamepadButton.LeftTrigger,
            GameAction.LockOn => (int)GamepadButton.RightStick, GameAction.Interact => (int)GamepadButton.South, GameAction.Flask => (int)GamepadButton.West,
            GameAction.Slot1 => (int)GamepadButton.DpadUp, GameAction.Slot2 => (int)GamepadButton.DpadRight,
            GameAction.Slot3 => (int)GamepadButton.DpadDown, GameAction.Slot4 => (int)GamepadButton.DpadLeft,
            GameAction.Inventory => (int)GamepadButton.Select,
            _ => None,
        };

        /// <summary>Movement has no gamepad button (the left stick moves).</summary>
        public static bool HasPad(GameAction a) => a > GameAction.MoveRight && a != GameAction.Skills && a != GameAction.Scoreboard;

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            for (int i = 0; i < Count; i++)
            {
                var a = (GameAction)i;
                _kb[i] = PlayerPrefs.GetInt("df_kb_" + a, DefaultKey(a));
                _pad[i] = PlayerPrefs.GetInt("df_pad_" + a, DefaultPad(a));
            }
        }

        public static int KeyOf(GameAction a) { Load(); return _kb[(int)a]; }
        public static int PadOf(GameAction a) { Load(); return _pad[(int)a]; }

        /// <summary>Assigns an input; an action that already used it gets the old input of this one (swap).</summary>
        public static void Set(GameAction a, bool pad, int value)
        {
            Load();
            var arr = pad ? _pad : _kb;
            int old = arr[(int)a];
            for (int i = 0; i < Count; i++)
            {
                if (i == (int)a || arr[i] != value || value == None) continue;
                // the gamepad's sprint/roll share B on purpose (hold / tap): do not swap those two
                bool sprintRoll = pad && ((i == (int)GameAction.Sprint && a == GameAction.Roll) || (i == (int)GameAction.Roll && a == GameAction.Sprint));
                if (sprintRoll) continue;
                arr[i] = old;
                PlayerPrefs.SetInt((pad ? "df_pad_" : "df_kb_") + (GameAction)i, old);
            }
            arr[(int)a] = value;
            PlayerPrefs.SetInt((pad ? "df_pad_" : "df_kb_") + a, value);
            PlayerPrefs.Save();
            Version++;
        }

        public static void ResetAll()
        {
            Load();
            for (int i = 0; i < Count; i++)
            {
                var a = (GameAction)i;
                _kb[i] = DefaultKey(a); _pad[i] = DefaultPad(a);
                PlayerPrefs.DeleteKey("df_kb_" + a); PlayerPrefs.DeleteKey("df_pad_" + a);
            }
            PlayerPrefs.Save();
            Version++;
        }

        // ================================================================== reading
        static ButtonControl KbControl(int k)
        {
            var ms = Mouse.current;
            if (k >= 0) { var kb = Keyboard.current; return kb != null && k != (int)UnityEngine.InputSystem.Key.None ? kb[(IKey)k] : null; }
            if (ms == null) return null;
            return k switch { MouseLeft => ms.leftButton, MouseRight => ms.rightButton, MouseMiddle => ms.middleButton, MouseBack => ms.backButton, MouseForward => ms.forwardButton, _ => null };
        }

        static ButtonControl PadControl(int b)
        {
            var gp = Gamepad.current;
            if (gp == null || b < 0) return null;
            try { return gp[(GamepadButton)b]; } catch { return null; }
        }

        /// <summary>Mouse buttons only count while the cursor is captured (not when clicking the UI).</summary>
        static bool MouseOk(int k) => k >= 0 || Cursor.lockState == CursorLockMode.Locked;

        public static bool KeyHeld(GameAction a) { int k = KeyOf(a); var c = KbControl(k); return c != null && MouseOk(k) && c.isPressed; }
        public static bool KeyDown(GameAction a) { int k = KeyOf(a); var c = KbControl(k); return c != null && MouseOk(k) && c.wasPressedThisFrame; }
        public static bool PadHeld(GameAction a) { var c = PadControl(PadOf(a)); return c != null && c.isPressed; }
        public static bool PadDown(GameAction a) { var c = PadControl(PadOf(a)); return c != null && c.wasPressedThisFrame; }
        public static bool PadUp(GameAction a) { var c = PadControl(PadOf(a)); return c != null && c.wasReleasedThisFrame; }

        // ================================================================== listening for a new input
        static GameAction _listenAction;
        static bool _listenPad;
        static Action _listenDone;
        public static bool Listening { get; private set; }
        public static GameAction ListeningAction => _listenAction;
        public static bool ListeningPad => _listenPad;

        public static void BeginListen(GameAction a, bool pad, Action done)
        {
            _listenAction = a; _listenPad = pad; _listenDone = done; Listening = true;
            _listenFrame = Time.frameCount;
        }

        static int _listenFrame;

        /// <summary>Called every frame by the UI: captures the next key / button (Esc / Start cancels).</summary>
        public static void TickListen()
        {
            if (!Listening || Time.frameCount <= _listenFrame + 1) return;
            var kb = Keyboard.current; var ms = Mouse.current; var gp = Gamepad.current;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.startButton.wasPressedThisFrame)) { Finish(); return; }
            if (!_listenPad)
            {
                if (kb != null)
                    foreach (var key in kb.allKeys)
                        if (key != null && key.wasPressedThisFrame && key.keyCode != UnityEngine.InputSystem.Key.Escape)
                        { Set(_listenAction, false, (int)key.keyCode); Finish(); return; }
                if (ms != null)
                {
                    int m = ms.leftButton.wasPressedThisFrame ? MouseLeft : ms.rightButton.wasPressedThisFrame ? MouseRight : ms.middleButton.wasPressedThisFrame ? MouseMiddle
                          : ms.backButton.wasPressedThisFrame ? MouseBack : ms.forwardButton.wasPressedThisFrame ? MouseForward : None;
                    if (m != None) { Set(_listenAction, false, m); Finish(); return; }
                }
            }
            else if (gp != null)
            {
                foreach (GamepadButton b in Enum.GetValues(typeof(GamepadButton)))
                {
                    if (b == GamepadButton.Start) continue;
                    ButtonControl c; try { c = gp[b]; } catch { continue; }
                    if (c != null && c.wasPressedThisFrame) { Set(_listenAction, true, (int)b); Finish(); return; }
                }
            }
        }

        static void Finish() { Listening = false; var d = _listenDone; _listenDone = null; d?.Invoke(); }

        // ================================================================== names
        public static string Name(GameAction a) => a switch
        {
            GameAction.MoveForward => "Move forward", GameAction.MoveBack => "Move back", GameAction.MoveLeft => "Move left", GameAction.MoveRight => "Move right",
            GameAction.Sprint => "Sprint (hold)", GameAction.Roll => "Dodge roll", GameAction.Crouch => "Crouch",
            GameAction.Attack => "Attack / quick shot / note", GameAction.Heavy => "Heavy / power shot / triad", GameAction.Block => "Block / aim",
            GameAction.Parry => "Parry", GameAction.LockOn => "Lock-on", GameAction.Interact => "Interact / revive", GameAction.Flask => "Ember Flask",
            GameAction.Slot1 => "Ultimate · spell 1", GameAction.Slot2 => "Iron Draught · spell 2", GameAction.Slot3 => "Vigor Tonic · spell 3", GameAction.Slot4 => "Soul Salve · spell 4",
            GameAction.Inventory => "Inventory", GameAction.Skills => "Skills", GameAction.Scoreboard => "Scoreboard (hold)",
            _ => a.ToString(),
        };

        public static string KeyLabel(int k)
        {
            switch (k)
            {
                case None: return "—";
                case MouseLeft: return "LMB";
                case MouseRight: return "RMB";
                case MouseMiddle: return "MMB";
                case MouseBack: return "M4";
                case MouseForward: return "M5";
            }
            var key = (IKey)k;
            switch (key)
            {
                case UnityEngine.InputSystem.Key.Space: return "Space";
                case UnityEngine.InputSystem.Key.LeftShift: return "Shift";
                case UnityEngine.InputSystem.Key.RightShift: return "R-Shift";
                case UnityEngine.InputSystem.Key.LeftCtrl: return "Ctrl";
                case UnityEngine.InputSystem.Key.RightCtrl: return "R-Ctrl";
                case UnityEngine.InputSystem.Key.LeftAlt: return "Alt";
                case UnityEngine.InputSystem.Key.Tab: return "Tab";
                case UnityEngine.InputSystem.Key.Enter: return "Enter";
                case UnityEngine.InputSystem.Key.Backspace: return "Back";
                case UnityEngine.InputSystem.Key.CapsLock: return "Caps";
            }
            var kb = Keyboard.current;
            string n = null;
            try { n = kb != null ? kb[key].displayName : null; } catch { }
            if (string.IsNullOrEmpty(n)) n = key.ToString().Replace("Digit", "");
            return n.Length == 1 ? n.ToUpperInvariant() : n;
        }

        public static bool PlayStationPad => Gamepad.current is UnityEngine.InputSystem.DualShock.DualShockGamepad;

        public static string PadLabel(int b)
        {
            if (b < 0) return "—";
            bool ps = PlayStationPad;
            return (GamepadButton)b switch
            {
                GamepadButton.South => ps ? "✕" : "A", GamepadButton.East => ps ? "○" : "B",
                GamepadButton.West => ps ? "□" : "X", GamepadButton.North => ps ? "△" : "Y",
                GamepadButton.LeftShoulder => ps ? "L1" : "LB", GamepadButton.RightShoulder => ps ? "R1" : "RB",
                GamepadButton.LeftTrigger => ps ? "L2" : "LT", GamepadButton.RightTrigger => ps ? "R2" : "RT",
                GamepadButton.LeftStick => "L3", GamepadButton.RightStick => "R3",
                GamepadButton.DpadUp => "▲", GamepadButton.DpadRight => "►", GamepadButton.DpadDown => "▼", GamepadButton.DpadLeft => "◄",
                GamepadButton.Start => ps ? "Options" : "Menu", GamepadButton.Select => ps ? "Share" : "View",
                _ => ((GamepadButton)b).ToString(),
            };
        }
    }
}
