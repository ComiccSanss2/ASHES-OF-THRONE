using UnityEngine;

namespace DarkFantasy.UI
{
    /// <summary>
    /// Barra (vita/stamina/boss) costruita come da regola del kit: cornice 3-slice e riempimento
    /// 3-slice disegnato dentro "fill_rect" (x 15, y 4, h 7 sul canvas nativo; larghezza = w − 23).
    /// Include una "barra fantasma" che scende lentamente dopo un colpo (stile Souls).
    /// </summary>
    public class UIBar : MonoBehaviour
    {
        public RectTransform fill, ghost;
        public float innerWidth;
        float _value = 1f, _ghost = 1f, _ghostDelay;

        public static UIBar Create(Transform parent, Sprite frame, Sprite fillSprite, Vector2 anchor, Vector2 pivot, Vector2 pos, float width, Color? fillTint = null)
        {
            var rt = UIFactory.Rect("Bar", parent, anchor, pivot, pos, new Vector2(width, 15));
            var bar = rt.gameObject.AddComponent<UIBar>();
            bar.innerWidth = width - 23f;
            // la cornice ha il "pozzo" scuro opaco: va disegnata PRIMA del riempimento
            var frameRt = UIFactory.Stretch("Frame", rt);
            UIFactory.Image(frameRt, frame);
            var ghostRt = UIFactory.Rect("Ghost", rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(15, -4), new Vector2(bar.innerWidth, 7));
            UIFactory.Image(ghostRt, fillSprite, true, new Color(1f, 0.9f, 0.7f, 0.55f));
            var fillRt = UIFactory.Rect("Fill", rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(15, -4), new Vector2(bar.innerWidth, 7));
            UIFactory.Image(fillRt, fillSprite, true, fillTint ?? Color.white);
            bar.fill = fillRt; bar.ghost = ghostRt;
            return bar;
        }

        public void SetValue(float v01, bool instant = false)
        {
            v01 = Mathf.Clamp01(v01);
            if (v01 < _value - 0.001f) _ghostDelay = 0.6f;
            _value = v01;
            if (instant || _ghost < _value) _ghost = _value;
        }

        void Update()
        {
            if (_ghostDelay > 0f) _ghostDelay -= Time.unscaledDeltaTime;
            else _ghost = Mathf.MoveTowards(_ghost, _value, Time.unscaledDeltaTime * 0.6f);
            if (fill) { fill.sizeDelta = new Vector2(Mathf.Round(innerWidth * _value), 7); fill.gameObject.SetActive(_value > 0.005f); }
            if (ghost) { ghost.sizeDelta = new Vector2(Mathf.Round(innerWidth * _ghost), 7); ghost.gameObject.SetActive(_ghost > 0.005f); }
        }
    }
}
