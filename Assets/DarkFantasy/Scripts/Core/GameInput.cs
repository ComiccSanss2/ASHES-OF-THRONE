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
        public static bool GameplayBlocked { get => _blocked || ChatOpen || InputBindings.Listening; set => _blocked = value; }
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
                    if (InputBindings.KeyHeld(GameAction.MoveForward) || KB.upArrowKey.isPressed) v.y += 1;
                    if (InputBindings.KeyHeld(GameAction.MoveBack) || KB.downArrowKey.isPressed) v.y -= 1;
                    if (InputBindings.KeyHeld(GameAction.MoveRight) || KB.rightArrowKey.isPressed) v.x += 1;
                    if (InputBindings.KeyHeld(GameAction.MoveLeft) || KB.leftArrowKey.isPressed) v.x -= 1;
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
        // gamepad: when sprint and roll share a button (B by default) a hold sprints and a tap rolls
        static bool PadShared => InputBindings.PadOf(GameAction.Sprint) == InputBindings.PadOf(GameAction.Roll);
        public static bool SprintHeld => !GameplayBlocked && (InputBindings.KeyHeld(GameAction.Sprint) || (InputBindings.PadHeld(GameAction.Sprint) && (!PadShared || _eastHeldTime > 0.25f)));
        public static bool RollPressed => !GameplayBlocked && (InputBindings.KeyDown(GameAction.Roll) || (PadShared ? InputBindings.PadUp(GameAction.Roll) && _eastHeldTime < 0.3f : InputBindings.PadDown(GameAction.Roll)));
        public static bool LightAttackPressed => !GameplayBlocked && (InputBindings.KeyDown(GameAction.Attack) || InputBindings.PadDown(GameAction.Attack));
        /// <summary>Held: attacks keep coming (a full combo, or a stream of arrows / notes / puffs).</summary>
        public static bool LightAttackHeld => !GameplayBlocked && (InputBindings.KeyHeld(GameAction.Attack) || InputBindings.PadHeld(GameAction.Attack));
        public static bool HeavyPressed => !GameplayBlocked && (InputBindings.KeyDown(GameAction.Heavy) || InputBindings.PadDown(GameAction.Heavy));
        public static bool HeavyHeld => !GameplayBlocked && (InputBindings.KeyHeld(GameAction.Heavy) || InputBindings.PadHeld(GameAction.Heavy));
        public static bool BlockHeld => !GameplayBlocked && (InputBindings.KeyHeld(GameAction.Block) || InputBindings.PadHeld(GameAction.Block));
        public static bool ParryPressed => !GameplayBlocked && (InputBindings.KeyDown(GameAction.Parry) || InputBindings.PadDown(GameAction.Parry));
        public static bool LockOnPressed => !GameplayBlocked && (InputBindings.KeyDown(GameAction.LockOn) || InputBindings.PadDown(GameAction.LockOn));
        /// <summary>Hold: the scoreboard (players, class, weapon, kills / deaths / revives).</summary>
        public static bool ScoreboardHeld => InputBindings.KeyHeld(GameAction.Scoreboard) || (GP != null && GP.selectButton.isPressed && GP.leftShoulder.isPressed);
        /// <summary>Downed: hold to give up and go back to the last flame.</summary>
        public static bool GiveUpHeld => !GameplayBlocked && (InputBindings.KeyHeld(GameAction.Roll) || (GP != null && GP.buttonNorth.isPressed));
        /// <summary>Crouch toggle: Ctrl or C, gamepad left stick press.</summary>
        public static bool CrouchPressed => !GameplayBlocked && (InputBindings.KeyDown(GameAction.Crouch) || (KB != null && KB.cKey.wasPressedThisFrame && InputBindings.KeyOf(GameAction.Crouch) == (int)Key.LeftCtrl) || InputBindings.PadDown(GameAction.Crouch));
        public static bool FlaskPressed => !GameplayBlocked && (InputBindings.KeyDown(GameAction.Flask) || InputBindings.PadDown(GameAction.Flask));
        /// <summary>Bard spells: keys 1-4, gamepad D-pad up/right/down/left.</summary>
        public static bool SpellPressed(int i)
        {
            if (GameplayBlocked) return false;
            var a = GameAction.Slot1 + Mathf.Clamp(i, 0, 3);
            if (InputBindings.KeyDown(a) || InputBindings.PadDown(a)) return true;
            if (KB != null)
            {
                var n = i == 0 ? KB.numpad1Key : i == 1 ? KB.numpad2Key : i == 2 ? KB.numpad3Key : KB.numpad4Key;
                if (n.wasPressedThisFrame) return true;
            }
            return false;
        }
        public static bool InteractPressed => !GameplayBlocked && (InputBindings.KeyDown(GameAction.Interact) || InputBindings.PadDown(GameAction.Interact));

        // Queste funzionano anche a gameplay bloccato (servono per aprire/chiudere menu)
        public static bool PausePressed => (KB != null && KB.escapeKey.wasPressedThisFrame) || (GP != null && GP.startButton.wasPressedThisFrame);
        public static bool HelpPressed => KB != null && KB.f1Key.wasPressedThisFrame;
        /// <summary>Opens/closes the inventory (works while the inventory itself blocks gameplay).</summary>
        /// <summary>Skill tree.</summary>
        public static bool SkillsPressed => !InputBindings.Listening && !ChatOpen && InputBindings.KeyDown(GameAction.Skills);
        public static bool InventoryPressed => !InputBindings.Listening && !ChatOpen && (InputBindings.KeyDown(GameAction.Inventory) || InputBindings.PadDown(GameAction.Inventory));

        public static bool UsingGamepadLast { get; private set; }

        // Gamepad: B tenuto = corsa, B toccato = roll. Tracciamo la durata della pressione.
        static float _eastHeldTime;

        /// <summary>Da chiamare una volta per frame (lo fa InputUpdater).</summary>
        public static void Tick()
        {
            if (GP != null)
            {
                if (InputBindings.PadHeld(GameAction.Roll)) _eastHeldTime += Time.unscaledDeltaTime;
                else if (!InputBindings.PadUp(GameAction.Roll)) _eastHeldTime = 0f;
                // the prompts follow the device last touched (sticks or any button)
                if (GP.wasUpdatedThisFrame && (GP.leftStick.ReadValue().sqrMagnitude > 0.1f || GP.rightStick.ReadValue().sqrMagnitude > 0.1f
                    || GP.buttonSouth.isPressed || GP.buttonEast.isPressed || GP.buttonWest.isPressed || GP.buttonNorth.isPressed
                    || GP.leftShoulder.isPressed || GP.rightShoulder.isPressed || GP.leftTrigger.isPressed || GP.rightTrigger.isPressed
                    || GP.dpad.ReadValue().sqrMagnitude > 0.1f || GP.startButton.isPressed || GP.selectButton.isPressed))
                    UsingGamepadLast = true;
            }
            if ((MS != null && (MS.delta.ReadValue().sqrMagnitude > 4f || MS.leftButton.wasPressedThisFrame)) || (KB != null && KB.anyKey.wasPressedThisFrame)) UsingGamepadLast = false;
        }

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }

}
