using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace DarkFantasy.UI
{
    /// <summary>
    /// A key / button prompt that follows the device in use: a keycap with the bound key on keyboard
    /// and mouse, or the gamepad's own button (coloured face buttons, bumpers, triggers, sticks,
    /// D-pad; Xbox or PlayStation naming) as soon as the joystick is touched. Rebinding refreshes it.
    /// </summary>
    public class InputGlyph : MonoBehaviour
    {
        public GameAction action;
        [Tooltip("Force a device (-1 follow the last used, 0 keyboard, 1 gamepad).")]
        public int forceDevice = -1;
        public float height = 26f;

        Image _bg;
        Text _label;
        int _shownVersion = -1, _shownDevice = -1, _shownPadKind = -1;
        static Sprite _circle, _pill;
        static Font _symbols;

        public static InputGlyph Create(RectTransform parent, GameAction action, Vector2 anchor, Vector2 pos, float height = 26f, int forceDevice = -1)
        {
            var rt = UIFactory.Rect("Glyph_" + action, parent, anchor, anchor, pos, new Vector2(height, height));
            var g = rt.gameObject.AddComponent<InputGlyph>();
            g.action = action; g.height = height; g.forceDevice = forceDevice;
            g.Build();
            return g;
        }

        void Build()
        {
            var rt = (RectTransform)transform;
            _bg = rt.gameObject.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
            _bg.raycastTarget = false;
            _label = UIFactory.Text(UIFactory.Stretch("L", rt, 2, 1, 2, 2), "", Mathf.RoundToInt(height * 0.55f), Color.white);
            _label.raycastTarget = false;
            Refresh(true);
        }

        void Update() => Refresh(false);

        void Refresh(bool force)
        {
            if (_bg == null) return;
            int device = forceDevice >= 0 ? forceDevice : (GameInput.UsingGamepadLast && Gamepad.current != null ? 1 : 0);
            if (device == 1 && !InputBindings.HasPad(action)) device = 0;
            int padKind = InputBindings.PlayStationPad ? 1 : 0;
            if (!force && device == _shownDevice && InputBindings.Version == _shownVersion && padKind == _shownPadKind) return;
            _shownDevice = device; _shownVersion = InputBindings.Version; _shownPadKind = padKind;
            var rt = (RectTransform)transform;
            var skin = UIFactory.Skin;
            if (device == 0)
            {
                string t = InputBindings.KeyLabel(InputBindings.KeyOf(action));
                _label.text = t;
                _label.font = skin != null && skin.bodyFont ? skin.bodyFont : _label.font;
                _label.color = skin != null ? skin.bone0 : Color.white;
                _label.fontSize = Mathf.RoundToInt(height * (t.Length > 2 ? 0.42f : 0.55f));
                _bg.sprite = skin != null ? skin.keycap : null; _bg.type = Image.Type.Sliced; _bg.color = Color.white;
                rt.sizeDelta = new Vector2(Mathf.Max(height, 12f + t.Length * height * 0.32f), height);
                return;
            }
            int b = InputBindings.PadOf(action);
            string l = InputBindings.PadLabel(b);
            _label.text = l;
            _label.font = SymbolFont;
            _label.fontStyle = FontStyle.Bold;
            var gb = (GamepadButton)b;
            bool face = gb == GamepadButton.South || gb == GamepadButton.East || gb == GamepadButton.West || gb == GamepadButton.North;
            _bg.type = Image.Type.Simple;
            if (face)
            {
                _bg.sprite = Circle;
                _bg.color = FaceColor(gb, padKind == 1);
                _label.color = Color.white;
                _label.fontSize = Mathf.RoundToInt(height * 0.6f);
                rt.sizeDelta = new Vector2(height, height);
            }
            else
            {
                _bg.sprite = Pill;
                _bg.color = new Color(0.16f, 0.15f, 0.17f, 0.95f);
                _label.color = new Color(0.95f, 0.92f, 0.85f);
                _label.fontSize = Mathf.RoundToInt(height * (l.Length > 2 ? 0.4f : 0.5f));
                rt.sizeDelta = new Vector2(Mathf.Max(height * 1.3f, 10f + l.Length * height * 0.34f), height);
            }
        }

        static Color FaceColor(GamepadButton b, bool ps) => ps
            ? b switch { GamepadButton.South => new Color(0.35f, 0.55f, 0.95f), GamepadButton.East => new Color(0.9f, 0.3f, 0.32f), GamepadButton.West => new Color(0.9f, 0.45f, 0.75f), _ => new Color(0.3f, 0.8f, 0.6f) }
            : b switch { GamepadButton.South => new Color(0.38f, 0.72f, 0.28f), GamepadButton.East => new Color(0.86f, 0.25f, 0.22f), GamepadButton.West => new Color(0.25f, 0.5f, 0.92f), _ => new Color(0.95f, 0.75f, 0.15f) };

        static Font SymbolFont => _symbols ? _symbols : (_symbols = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        static Sprite Circle => _circle ? _circle : (_circle = MakeShape(64, 64, 31f, true));
        static Sprite Pill => _pill ? _pill : (_pill = MakeShape(96, 48, 14f, false));

        /// <summary>A soft-edged disc, or a rounded rectangle with a thin light rim (CPU-made, no GPU readback).</summary>
        static Sprite MakeShape(int w, int h, float r, bool disc)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float d;
                    if (disc) d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(w * 0.5f, h * 0.5f)) - r;
                    else
                    {
                        float cx = Mathf.Clamp(x + 0.5f, r, w - r), cy = Mathf.Clamp(y + 0.5f, r, h - r);
                        d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - r + 0.5f;
                    }
                    float a = Mathf.Clamp01(0.5f - d);
                    float rim = Mathf.Clamp01(1f - Mathf.Abs(d + 2.5f) / 1.5f) * 0.35f;
                    float shade = disc ? Mathf.Lerp(0.78f, 1f, y / (float)h) : 1f;
                    byte v = (byte)(Mathf.Clamp01(shade + rim) * 255);
                    px[y * w + x] = new Color32(v, v, v, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }
    }
}
