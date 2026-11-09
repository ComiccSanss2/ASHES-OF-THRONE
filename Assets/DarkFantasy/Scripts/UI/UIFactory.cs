using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DarkFantasy.UI
{
    /// <summary>Costruzione da codice dei widget con gli sprite del kit Ravenhollow.</summary>
    public static class UIFactory
    {
        public static UISkin Skin;

        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(string name, Transform parent, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static Image Image(RectTransform rt, Sprite s, bool sliced = true, Color? color = null)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = s;
            img.type = sliced && s != null && s.border.sqrMagnitude > 0 ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.color = color ?? Color.white;
            img.raycastTarget = false;
            return img;
        }

        public static Text Text(RectTransform rt, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, bool heading = false)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = heading ? Skin.headingFont : Skin.bodyFont;
            if (t.font == null) t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            // ombra a un pixel come nel kit (void-0 sotto ogni scritta)
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = Skin.void0;
            sh.effectDistance = new Vector2(1, -1);
            return t;
        }

        public static Text Label(Transform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, Color color, TextAnchor align = TextAnchor.MiddleCenter, bool heading = false)
        {
            var rt = Rect("Label", parent, anchor, new Vector2(0.5f, 0.5f), pos, size);
            return Text(rt, text, fontSize, color, align, heading);
        }

        public static Button Button(Transform parent, string label, Vector2 anchor, Vector2 pos, float width, UnityAction onClick, bool secondary = false)
        {
            var rt = Rect("Button_" + label, parent, anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(width, 28));
            var img = Image(rt, secondary ? Skin.button2Normal : Skin.buttonNormal);
            img.raycastTarget = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.SpriteSwap;
            b.spriteState = new SpriteState
            {
                highlightedSprite = secondary ? Skin.button2Hover : Skin.buttonHover,
                pressedSprite = secondary ? Skin.button2Pressed : Skin.buttonPressed,
                selectedSprite = secondary ? Skin.button2Selected : Skin.buttonSelected,
                disabledSprite = secondary ? Skin.button2Disabled : Skin.buttonDisabled,
            };
            var lrt = Stretch("Text", rt, 11, 4, 11, 6);
            Text(lrt, label, 16, Skin.bone1);
            b.onClick.AddListener(() => Sfx.Play2D(SfxId.UiClick));
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        public static InputField Field(Transform parent, string value, string placeholder, Vector2 anchor, Vector2 pos, float width, InputField.ContentType type = InputField.ContentType.Standard)
        {
            var rt = Rect("Field", parent, anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(width, 24));
            var img = Image(rt, Skin.fieldIdle);
            img.raycastTarget = true;
            var f = rt.gameObject.AddComponent<InputField>();
            f.targetGraphic = img;
            f.transition = Selectable.Transition.SpriteSwap;
            f.spriteState = new SpriteState { highlightedSprite = Skin.fieldHover, selectedSprite = Skin.fieldFocus, pressedSprite = Skin.fieldFocus, disabledSprite = Skin.fieldDisabled };
            var trt = Stretch("Text", rt, 6, 2, 6, 3);
            var txt = Text(trt, "", 16, Skin.bone1, TextAnchor.MiddleLeft);
            txt.supportRichText = false;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            var prt = Stretch("Placeholder", rt, 6, 2, 6, 3);
            var ph = Text(prt, placeholder, 16, Skin.bone3, TextAnchor.MiddleLeft);
            f.textComponent = txt;
            f.placeholder = ph;
            f.contentType = type;
            f.caretColor = Skin.bone0;
            f.customCaretColor = true;
            f.caretWidth = 1;
            f.selectionColor = new Color(Skin.velvet2.r, Skin.velvet2.g, Skin.velvet2.b, 0.8f);
            f.text = value;
            return f;
        }

        public static Slider Slider(Transform parent, Vector2 anchor, Vector2 pos, float width, float min, float max, float value, UnityAction<float> onChange)
        {
            var rt = Rect("Slider", parent, anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(width, 20));
            var s = rt.gameObject.AddComponent<Slider>();
            var track = Rect("Track", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, 9));
            Image(track, Skin.sliderTrack);
            var fillArea = Stretch("FillArea", track, 3, 3, 3, 2);
            var fill = Stretch("Fill", fillArea);
            Image(fill, Skin.sliderFill);
            var handleArea = Stretch("HandleArea", rt, 3, 0, 3, 0);
            var handle = Rect("Knob", handleArea, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            var hImg = Image(handle, Skin.sliderKnob, false);
            hImg.raycastTarget = true;
            s.fillRect = fill;
            s.handleRect = handle;
            s.targetGraphic = hImg;
            s.transition = Selectable.Transition.SpriteSwap;
            s.spriteState = new SpriteState { highlightedSprite = Skin.sliderKnobHover, pressedSprite = Skin.sliderKnobPressed, selectedSprite = Skin.sliderKnobHover };
            s.minValue = min; s.maxValue = max; s.value = value;
            var bg = rt.gameObject.AddComponent<Image>(); bg.color = new Color(0, 0, 0, 0); bg.raycastTarget = true; // area cliccabile
            if (onChange != null) s.onValueChanged.AddListener(onChange);
            return s;
        }

        public static Toggle Toggle(Transform parent, string label, Vector2 anchor, Vector2 pos, float width, bool value, UnityAction<bool> onChange)
        {
            var rt = Rect("Toggle_" + label, parent, anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(width, 22));
            var t = rt.gameObject.AddComponent<Toggle>();
            var boxRt = Rect("Box", rt, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(20, 20));
            var box = Image(boxRt, Skin.checkboxOff, false);
            box.raycastTarget = true;
            var checkRt = Rect("Check", boxRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            var check = Image(checkRt, Skin.checkboxOn, false);
            t.targetGraphic = box;
            t.graphic = check;
            t.transition = Selectable.Transition.SpriteSwap;
            t.spriteState = new SpriteState { highlightedSprite = Skin.checkboxOffHover, selectedSprite = Skin.checkboxOffHover };
            var lrt = Rect("Label", rt, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(28, 0), new Vector2(width - 28, 22));
            Text(lrt, label, 16, Skin.bone1, TextAnchor.MiddleLeft);
            t.isOn = value;
            t.onValueChanged.AddListener(_ => Sfx.Play2D(SfxId.UiClick));
            if (onChange != null) t.onValueChanged.AddListener(onChange);
            return t;
        }

        /// <summary>
        /// Finestra "fleur" del kit con titolo nella fascia superiore (title_band a y=25, alta 23 px
        /// sul canvas nativo 122x130). Ritorna il RectTransform del contenuto (inset 21, sotto il titolo).
        /// </summary>
        public static RectTransform Window(Transform parent, string title, Vector2 size, out RectTransform windowRt)
        {
            windowRt = Rect("Window_" + title, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var img = Image(windowRt, Skin.windowFleur);
            img.raycastTarget = true;
            FitToScreen(windowRt, size);
            var titleRt = Rect("Title", windowRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -25), new Vector2(size.x - 48, 23));
            Text(titleRt, title, 24, Skin.bone0, TextAnchor.MiddleCenter, true);
            return Stretch("Content", windowRt, 24, 60, 24, 24);
        }

        /// <summary>
        /// Scales a window down so it always fits the screen with a margin (the canvas is 540 units tall,
        /// its width follows the aspect ratio); it re-checks when the resolution changes.
        /// </summary>
        public static void FitToScreen(RectTransform win, Vector2 size)
        {
            var f = win.gameObject.GetComponent<FitWindow>() ?? win.gameObject.AddComponent<FitWindow>();
            f.size = size;
            f.Apply();
        }

        public static RectTransform Panel(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = Rect("Panel", parent, anchor, pivot, pos, size);
            Image(rt, Skin.panelClean);
            return rt;
        }
    }

    /// <summary>Keeps a window inside the screen (see UIFactory.FitToScreen).</summary>
    public class FitWindow : MonoBehaviour
    {
        public Vector2 size;
        float _aspect = -1f;

        public void Apply()
        {
            _aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 16f / 9f;
            float h = 540f, w = h * _aspect;
            float s = Mathf.Min(1f, (w - 24f) / Mathf.Max(1f, size.x), (h - 24f) / Mathf.Max(1f, size.y));
            transform.localScale = new Vector3(s, s, 1f);
        }

        void Update()
        {
            float a = Screen.height > 0 ? Screen.width / (float)Screen.height : _aspect;
            if (Mathf.Abs(a - _aspect) > 0.01f) Apply();
        }
    }
}
