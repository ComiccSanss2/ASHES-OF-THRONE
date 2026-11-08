using UnityEngine;

namespace DarkFantasy.UI
{
    /// <summary>
    /// Riferimenti agli sprite del kit "Gothic Manor Pixel UI Kit – Ravenhollow Lite" (Brunka Games).
    /// Gli sprite sono importati dal builder in modalità Point, senza compressione, con i bordi
    /// 9-slice presi da manifest.json del kit (ordine l, t, r, b).
    /// Si lavora su una tela virtuale 960x540 scalata di un fattore intero (2x a 1080p).
    /// </summary>
    [CreateAssetMenu(menuName = "Dark Fantasy/UI Skin")]
    public class UISkin : ScriptableObject
    {
        [Header("Font (Jacquard 12 titoli, Jersey 10 testo)")]
        public Font headingFont;
        public Font bodyFont;

        [Header("Contenitori")]
        public Sprite windowFleur;
        public Sprite panelClean;
        public Sprite speechOrnate;
        public Sprite nameplate;
        public Sprite ribbon;

        [Header("Pulsanti")]
        public Sprite buttonNormal, buttonHover, buttonPressed, buttonSelected, buttonDisabled;
        public Sprite button2Normal, button2Hover, button2Pressed, button2Selected, button2Disabled;

        [Header("Input")]
        public Sprite fieldIdle, fieldHover, fieldFocus, fieldDisabled, fieldCaret;
        public Sprite sliderTrack, sliderFill, sliderKnob, sliderKnobHover, sliderKnobPressed;
        public Sprite checkboxOn, checkboxOff, checkboxOnHover, checkboxOffHover;
        public Sprite keycap;

        [Header("Barre")]
        public Sprite barFrameHp, barFillHp;
        public Sprite barFrameMana, barFillMana;
        public Sprite barFrameXp, barFillXp;
        public Sprite barTicks;

        [Header("Icone")]
        public Sprite iconHeart, iconPotion, iconSkull, iconShield, iconSword, iconGear, iconCandle, iconCharacter, iconInfo, iconClose;

        [Header("Cursore")]
        public Texture2D cursor;
        public Vector2 cursorHotspot = Vector2.zero;

        [Header("Palette (manifest.palette)")]
        public Color bone0 = Hex("#fbebcf");
        public Color bone1 = Hex("#e8d0b5");
        public Color bone3 = Hex("#a88a76");
        public Color bone4 = Hex("#5b4a43");
        public Color void0 = Hex("#050408");
        public Color void1 = Hex("#0c0a0f");
        public Color velvet2 = Hex("#3f0710");
        public Color hp1 = Hex("#be2532");
        public Color hp2 = Hex("#ea6068");
        public Color gold1 = Hex("#df7d20");
        public Color gold2 = Hex("#f8b74c");
        public Color ok1 = Hex("#5c9c9b");
        public Color silver0 = Hex("#a49c9d");

        public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
    }
}
