using UnityEngine;
using UnityEngine.UI;

namespace DarkFantasy.UI
{
    /// <summary>
    /// Ultimate slot (knight, archer, reaper): bottom centre, a single big icon with the key badge,
    /// the meter filling as a dark radial veil, the name above; it blazes when ready.
    /// </summary>
    public partial class GameUI
    {
        [Header("Ultimates")]
        [Tooltip("Knight (Cinder Judgement), Archer (Storm of Arrows), Reaper (Harvest of Souls).")]
        public Sprite[] ultIcons = new Sprite[3];

        RectTransform _ultRoot;
        Image _ultIcon, _ultVeil, _ultGlow, _ultFrame;
        Text _ultName, _ultPct, _ultHint;
        float _ultReadyFlash;
        bool _ultWasReady;

        void BuildUltHud()
        {
            const float W = 66f;
            _ultRoot = UIFactory.Rect("Ultimate", _hud, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(W, W));
            var glowRt = UIFactory.Rect("Glow", _ultRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W + 22, W + 22));
            _ultGlow = UIFactory.Image(glowRt, MakeSolid(), false, new Color(1f, 0.8f, 0.4f, 0f));
            _ultFrame = UIFactory.Image(UIFactory.Stretch("Frame", _ultRoot, -3, -3, -3, -3), skin.panelClean, true, new Color(0.9f, 0.85f, 0.8f, 1f));
            _ultIcon = UIFactory.Image(UIFactory.Stretch("Icon", _ultRoot, 2, 2, 2, 2), null, false);
            _ultVeil = UIFactory.Image(UIFactory.Stretch("Veil", _ultRoot, 2, 2, 2, 2), MakeSolid(), false, new Color(0f, 0f, 0f, 0.7f));
            _ultVeil.type = Image.Type.Filled; _ultVeil.fillMethod = Image.FillMethod.Radial360; _ultVeil.fillOrigin = (int)Image.Origin360.Top; _ultVeil.fillClockwise = false;
            _ultPct = UIFactory.Text(UIFactory.Stretch("Pct", _ultRoot), "", 18, skin.bone0, TextAnchor.MiddleCenter, true);
            InputGlyph.Create(_ultRoot, GameAction.Slot1, new Vector2(0, 1), new Vector2(2, -2), 22f);
            _ultName = UIFactory.Label(_ultRoot, "", new Vector2(0.5f, 1), new Vector2(0, 30), new Vector2(220, 18), 14, skin.gold2, TextAnchor.MiddleCenter, true);
            _ultHint = UIFactory.Label(_ultRoot, "", new Vector2(0.5f, 1), new Vector2(0, 14), new Vector2(240, 14), 11, skin.bone1, TextAnchor.MiddleCenter);
            _ultHint.horizontalOverflow = HorizontalWrapMode.Overflow;
            _ultRoot.gameObject.SetActive(false);
        }

        static readonly string[] UltShort = { "Fire wall · stagger", "Arrow rain on the aim", "Whirl · reap · heal" };
        static readonly Color[] UltColor = { new Color(1f, 0.6f, 0.25f), new Color(0.55f, 1f, 0.85f), new Color(0.78f, 0.55f, 1f) };

        void UpdateUltHud()
        {
            var p = PlayerController.Local;
            var u = p ? p.ult : null;
            bool show = u != null && !p.IsBard && _hud.gameObject.activeSelf;
            if (_ultRoot.gameObject.activeSelf != show) _ultRoot.gameObject.SetActive(show);
            if (!show) return;
            int i = u.kind == PlayerUltimate.Kind.ArrowStorm ? 1 : u.kind == PlayerUltimate.Kind.Harvest ? 2 : 0;
            if (ultIcons != null && i < ultIcons.Length && _ultIcon.sprite != ultIcons[i]) _ultIcon.sprite = ultIcons[i];
            _ultName.text = u.ultName;
            _ultName.color = UltColor[i];
            _ultHint.text = UltShort[i];
            float c = u.Charge01;
            _ultVeil.fillAmount = 1f - c;
            bool ready = u.Ready;
            _ultPct.text = ready || u.Active ? "" : Mathf.FloorToInt(c * 100f) + "%";
            if (ready && !_ultWasReady) { _ultReadyFlash = 1f; Sfx.Play2D(SfxId.RelicReveal, 0.5f, 1.4f); }
            _ultWasReady = ready;
            _ultReadyFlash = Mathf.Max(0f, _ultReadyFlash - Time.deltaTime * 1.5f);
            float pulse = ready ? 0.35f + 0.25f * Mathf.Sin(Time.time * 5f) + _ultReadyFlash * 0.6f : 0f;
            var gc = UltColor[i]; gc.a = pulse;
            _ultGlow.color = gc;
            _ultIcon.color = ready || u.Active ? Color.white : new Color(0.65f, 0.65f, 0.65f, 1f);
            _ultRoot.localScale = Vector3.one * (1f + _ultReadyFlash * 0.25f);
        }
    }
}
