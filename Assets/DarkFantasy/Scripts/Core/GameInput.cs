using UnityEngine;
using UnityEngine.InputSystem;

namespace DarkFantasy
{
    /// <summary>
    /// Lettura centralizzata dell'input (Input System) per tastiera/mouse e gamepad.
    /// Il progetto usa "Input System Package (New)" come gestore attivo, quindi la classe
    /// legacy UnityEngine.Input NON va usata da nessuna parte.
    ///
    /// Tastiera/Mouse                       Gamepad
    ///  WASD/frecce muovi                   Levetta sinistra
    ///  Mouse       camera                  Levetta destra
    ///  Shift       corsa (tieni)           B / Cerchio (tieni)
    ///  Spazio      schivata (roll)         B / Cerchio (tocca)
    ///  Tasto sx    attacco leggero         R1
    ///  F           attacco pesante (tieni = carica)   R2
    ///  Tasto dx    parata (tieni)          L1
    ///  Q           parry                   L2
    ///  Rotellina      lock-on              R3
    ///  Tab (tieni)     classifica           Select+L1
    ///  R           fiaschetta              X / Quadrato
    ///  E           interagisci             A / Croce
    ///  Esc         pausa                   Start
    /// </summary>
    public static class GameInput
    {
        /// <summary>Quando true (menu aperti, morte, ecc.) il gameplay ignora l'input.</summary>
        static bool _blocked;
        /// <summary>Menus open (pause, inventory, F9) or the chat being typed: no gameplay input.</summary>
        public static bool GameplayBlocked { get => _blocked || ChatOpen; set => _blocked = value; }
        /// <summary>The chat box has the keyboard.</summary>
        public static bool ChatOpen;


        public static float MouseSensitivity = 1f;
        public static float GamepadSensitivity = 1f;
        public static bool InvertY;

        const float StickDeadzone = 0.15f;

        static Keyboard KB => Keyboard.current;
        static Mouse MS => Mouse.current;
        static Gamepad GP => Gamepad.current;

        // ---------------------------------------------------------------- movimento
        public static Vector2 Move
        {
            get
            {
                if (GameplayBlocked) return Vector2.zero;
                Vector2 v = Vector2.zero;
                if (KB != null)
                {
                    if (KB.wKey.isPressed || KB.upArrowKey.isPressed) v.y += 1;
                    if (KB.sKey.isPressed || KB.downArrowKey.isPressed) v.y -= 1;
                    if (KB.dKey.isPressed || KB.rightArrowKey.isPressed) v.x += 1;
                    if (KB.aKey.isPressed || KB.leftArrowKey.isPressed) v.x -= 1;
                }
                if (GP != null)
                {
                    Vector2 s = GP.leftStick.ReadValue();
                    if (s.magnitude > StickDeadzone) v += s;
                }
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        /// <summary>Delta camera in "gradi" già scalati per sensibilità.</summary>
        public static Vector2 Look
        {
            get
            {
                if (GameplayBlocked) return Vector2.zero;
                Vector2 v = Vector2.zero;
                if (MS != null && Cursor.lockState == CursorLockMode.Locked)
                    v += MS.delta.ReadValue() * 0.12f * MouseSensitivity;
                if (GP != null)
                {
                    Vector2 s = GP.rightStick.ReadValue();
                    if (s.magnitude > StickDeadzone) v += s * 180f * Time.unscaledDeltaTime * GamepadSensitivity;
                }
                if (InvertY) v.y = -v.y;
                return v;
            }
        }

        /// <summary>Componente X "grezza" usata per lo switch del target (flick del mouse / levetta).</summary>
        public static float TargetSwitchAxis
        {
            get
            {
                if (GameplayBlocked) return 0f;
                float x = 0f;
                if (MS != null && Cursor.lockState == CursorLockMode.Locked) x += MS.delta.ReadValue().x * 0.05f;
                if (GP != null) x += GP.rightStick.ReadValue().x;
                return x;
            }
        }

        // ---------------------------------------------------------------- azioni
        public static bool SprintHeld => !GameplayBlocked && ((KB != null && KB.leftShiftKey.isPressed) || (GP != null && GP.buttonEast.isPressed && _eastHeldTime > 0.25f));
        public static bool RollPressed => !GameplayBlocked && ((KB != null && KB.spaceKey.wasPressedThisFrame) || (GP != null && GP.buttonEast.wasReleasedThisFrame && _eastHeldTime < 0.3f));
        public static bool LightAttackPressed => !GameplayBlocked && ((MS != null && Cursor.lockState == CursorLockMode.Locked && MS.leftButton.wasPressedThisFrame) || (GP != null && GP.rightShoulder.wasPressedThisFrame));
        public static bool HeavyPressed => !GameplayBlocked && ((KB != null && KB.fKey.wasPressedThisFrame) || (GP != null && GP.rightTrigger.wasPressedThisFrame));
        public static bool HeavyHeld => !GameplayBlocked && ((KB != null && KB.fKey.isPressed) || (GP != null && GP.rightTrigger.isPressed));
        public static bool BlockHeld => !GameplayBlocked && ((MS != null && Cursor.lockState == CursorLockMode.Locked && MS.rightButton.isPressed) || (GP != null && GP.leftShoulder.isPressed));
        public static bool ParryPressed => !GameplayBlocked && ((KB != null && KB.qKey.wasPressedThisFrame) || (GP != null && GP.leftTrigger.wasPressedThisFrame));
        public static bool LockOnPressed => !GameplayBlocked && ((MS != null && MS.middleButton.wasPressedThisFrame) || (GP != null && GP.rightStickButton.wasPressedThisFrame));
        /// <summary>Hold: the scoreboard (players, class, weapon, kills / deaths / revives).</summary>
        public static bool ScoreboardHeld => (KB != null && KB.tabKey.isPressed) || (GP != null && GP.selectButton.isPressed && GP.leftShoulder.isPressed);
        /// <summary>Downed: hold to give up and go back to the last flame.</summary>
        public static bool GiveUpHeld => !GameplayBlocked && ((KB != null && KB.spaceKey.isPressed) || (GP != null && GP.buttonNorth.isPressed));
        public static bool FlaskPressed => !GameplayBlocked && ((KB != null && KB.rKey.wasPressedThisFrame) || (GP != null && GP.buttonWest.wasPressedThisFrame));
        /// <summary>Bard spells: keys 1-4, gamepad D-pad up/right/down/left.</summary>
        public static bool SpellPressed(int i)
        {
            if (GameplayBlocked) return false;
            if (KB != null)
            {
                var k = i == 0 ? KB.digit1Key : i == 1 ? KB.digit2Key : i == 2 ? KB.digit3Key : KB.digit4Key;
                if (k.wasPressedThisFrame) return true;
                var n = i == 0 ? KB.numpad1Key : i == 1 ? KB.numpad2Key : i == 2 ? KB.numpad3Key : KB.numpad4Key;
                if (n.wasPressedThisFrame) return true;
            }
            if (GP != null)
            {
                var d = i == 0 ? GP.dpad.up : i == 1 ? GP.dpad.right : i == 2 ? GP.dpad.down : GP.dpad.left;
                if (d.wasPressedThisFrame) return true;
            }
            return false;
        }
        public static bool InteractPressed => !GameplayBlocked && ((KB != null && KB.eKey.wasPressedThisFrame) || (GP != null && GP.buttonSouth.wasPressedThisFrame));

        // Queste funzionano anche a gameplay bloccato (servono per aprire/chiudere menu)
        public static bool PausePressed => (KB != null && KB.escapeKey.wasPressedThisFrame) || (GP != null && GP.startButton.wasPressedThisFrame);
        public static bool HelpPressed => KB != null && KB.f1Key.wasPressedThisFrame;
        /// <summary>Opens/closes the inventory (works while the inventory itself blocks gameplay).</summary>
        public static bool InventoryPressed => (KB != null && KB.iKey.wasPressedThisFrame) || (GP != null && GP.selectButton.wasPressedThisFrame);

        public static bool UsingGamepadLast { get; private set; }

        // Gamepad: B tenuto = corsa, B toccato = roll. Tracciamo la durata della pressione.
        static float _eastHeldTime;

        /// <summary>Da chiamare una volta per frame (lo fa InputUpdater).</summary>
        public static void Tick()
        {
            if (GP != null)
            {
                if (GP.buttonEast.isPressed) _eastHeldTime += Time.unscaledDeltaTime;
                else if (!GP.buttonEast.wasReleasedThisFrame) _eastHeldTime = 0f;
                if (GP.wasUpdatedThisFrame && (GP.leftStick.ReadValue().sqrMagnitude > 0.1f || GP.rightStick.ReadValue().sqrMagnitude > 0.1f))
                    UsingGamepadLast = true;
            }
            if (MS != null && MS.delta.ReadValue().sqrMagnitude > 4f) UsingGamepadLast = false;
        }

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }

}
