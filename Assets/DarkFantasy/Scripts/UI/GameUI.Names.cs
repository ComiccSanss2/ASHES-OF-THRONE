using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DarkFantasy.UI
{
    /// <summary>
    /// Name tags over the other players: nickname (class colour), a slim health bar, and a red
    /// crossed-swords mark when you and they are both in PvP (a possible opponent).
    /// </summary>
    public partial class GameUI
    {
        class NameTag { public RectTransform root; public Text name; public Image hpFill, hpBack; public Text pvp; }
        readonly Dictionary<PlayerController, NameTag> _nameTags = new Dictionary<PlayerController, NameTag>();

        NameTag MakeNameTag()
        {
            var t = new NameTag();
            t.root = UIFactory.Rect("NameTag", _overheadRoot, Vector2.zero, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(180, 30));
            t.name = UIFactory.Text(UIFactory.Rect("N", t.root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, 0), new Vector2(180, 20)), "", 15, skin.bone0, TextAnchor.MiddleCenter, true);
            var sh = t.name.gameObject.AddComponent<Outline>(); sh.effectColor = new Color(0f, 0f, 0f, 0.85f); sh.effectDistance = new Vector2(1.2f, -1.2f);
            var bar = UIFactory.Rect("Hp", t.root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 3), new Vector2(64, 5));
            t.hpBack = UIFactory.Image(bar, MakeSolid(), false, new Color(0f, 0f, 0f, 0.65f));
            var fill = UIFactory.Stretch("Fill", bar, 1, 1, 1, 1);
            t.hpFill = UIFactory.Image(fill, MakeSolid(), false, new Color(0.75f, 0.12f, 0.1f, 0.95f));
            t.hpFill.type = Image.Type.Filled; t.hpFill.fillMethod = Image.FillMethod.Horizontal;
            t.pvp = UIFactory.Text(UIFactory.Rect("Pvp", t.root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, 16), new Vector2(120, 14)), "PvP", 11, new Color(1f, 0.35f, 0.3f), TextAnchor.MiddleCenter, true);
            return t;
        }

        void UpdateNameTags(SoulsCamera cam, Rect canvasRect)
        {
            var local = PlayerController.Local;
            foreach (var p in PlayerController.All)
            {
                if (p == null || p == local) continue;
                if (!_nameTags.TryGetValue(p, out var tag)) { tag = MakeNameTag(); _nameTags[p] = tag; }
                Vector3 head = p.transform.position + Vector3.up * (p.Crouched ? 1.55f : 2.15f);
                Vector3 vp = cam.Cam.WorldToViewportPoint(head);
                bool show = p.IsSpawned && vp.z > 0.3f && vp.z < 45f && !_paused && _hud.gameObject.activeSelf;
                if (tag.root.gameObject.activeSelf != show) tag.root.gameObject.SetActive(show);
                if (!show) continue;
                tag.root.anchoredPosition = new Vector2(Mathf.Round(vp.x * canvasRect.width), Mathf.Round(vp.y * canvasRect.height));
                float near = Mathf.Clamp01(1.3f - vp.z / 35f);
                tag.root.localScale = Vector3.one * Mathf.Lerp(0.75f, 1f, near);
                var v = p.vitals;
                string nm = v ? v.DisplayName : "Knight";
                if (tag.name.text != nm) tag.name.text = nm;
                Color c = p.IsArcher ? new Color(0.62f, 0.85f, 0.55f) : p.IsBard ? new Color(0.62f, 0.78f, 1f) : p.IsReaper ? new Color(0.8f, 0.62f, 1f) : p.IsSmoker ? new Color(0.78f, 0.74f, 0.68f) : new Color(0.95f, 0.86f, 0.62f);
                bool foe = local != null && PlayerController.CanPvp(local, p);
                c.a = near * 0.6f + 0.4f;
                tag.name.color = foe ? new Color(1f, 0.45f, 0.38f, c.a) : c;
                tag.pvp.gameObject.SetActive(foe);
                float hp = v ? v.Health01 : 1f;
                tag.hpFill.fillAmount = hp;
                tag.hpFill.color = v && v.IsDowned ? new Color(0.9f, 0.6f, 0.15f, 0.95f) : new Color(0.75f, 0.12f, 0.1f, 0.95f);
            }
            // tags of players who left
            List<PlayerController> gone = null;
            foreach (var kv in _nameTags) if (kv.Key == null) (gone ??= new List<PlayerController>()).Add(kv.Key);
            if (gone != null) foreach (var g in gone) { if (_nameTags[g].root) Destroy(_nameTags[g].root.gameObject); _nameTags.Remove(g); }
        }
    }
}
