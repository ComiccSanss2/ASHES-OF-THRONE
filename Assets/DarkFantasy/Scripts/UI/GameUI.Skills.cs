using UnityEngine;
using UnityEngine.UI;

namespace DarkFantasy.UI
{
    /// <summary>
    /// Skill tree (K): five branches around a burning heart — Vitality, Endurance, Strength, Mind,
    /// Arcana — bought with Souls. Ranks pips, the effect so far, the next cost. Also the souls counter
    /// and the "+N souls" popups on the HUD.
    /// </summary>
    public partial class GameUI
    {
        class SkillNode { public Text name, effect, cost; public Image[] pips; public Button buy; public Image frame; }

        RectTransform _skills;
        Text _skSouls;
        readonly SkillNode[] _skNodes = new SkillNode[PlayerSkills.StatCount];
        Text _hudSouls, _soulsPopup;
        float _soulsPopupT;
        int _soulsPopupSum;

        static readonly Color[] SkillColor =
        {
            new Color(0.92f, 0.35f, 0.3f), new Color(0.55f, 0.85f, 0.45f), new Color(1f, 0.7f, 0.35f), new Color(0.45f, 0.7f, 1f), new Color(0.78f, 0.55f, 1f),
        };

        bool SkillsOpen => _skills && _skills.gameObject.activeSelf;

        void BuildSkills()
        {
            _skills = UIFactory.Stretch("Skills", _root);
            UIFactory.Image(UIFactory.Stretch("Veil", _skills), null, false, new Color(0, 0, 0, 0.6f)).raycastTarget = true;
            var c = UIFactory.Window(_skills, "Skills", new Vector2(820, 600), out var skWin);
            UIFactory.Button(skWin, "Back", new Vector2(1, 1), new Vector2(-84, -27), 120, () => ShowSkills(false), true);
            _skSouls = UIFactory.Label(c, "", new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(600, 24), 18, new Color(0.85f, 0.78f, 1f), TextAnchor.MiddleCenter, true);

            // the heart, the branches
            Vector2 centre = new Vector2(0, -290);
            var heart = UIFactory.Rect("Heart", c, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), centre, new Vector2(110, 110));
            UIFactory.Image(heart, skin.panelClean, true, new Color(0.45f, 0.3f, 0.55f));
            var hi = UIFactory.Image(UIFactory.Stretch("I", heart, 22, 22, 22, 22), skin.iconCandle ? skin.iconCandle : skin.iconHeart, false, new Color(1f, 0.85f, 0.7f));
            hi.preserveAspect = true;
            for (int i = 0; i < PlayerSkills.StatCount; i++)
            {
                float a = (90f + 72f * i) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.86f);
                Vector2 pos = centre + dir * 215f;
                // branch line
                var line = UIFactory.Rect("Branch", c, new Vector2(0.5f, 1f), new Vector2(0f, 0.5f), centre + dir * 55f, new Vector2(Vector2.Distance(centre + dir * 55f, pos - dir * 70f), 4));
                line.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                UIFactory.Image(line, MakeSolid(), false, new Color(SkillColor[i].r, SkillColor[i].g, SkillColor[i].b, 0.45f));
                line.SetAsFirstSibling();
                _skNodes[i] = MakeSkillNode(c, pos, (PlayerSkills.Stat)i);
            }
            UIFactory.Label(c, "Souls are won from every monster and boss slain by the party.\nTrained stats are lost when you join a lobby that starts from the very beginning — your weapons and armour are not.",
                new Vector2(0.5f, 0f), new Vector2(0, 26), new Vector2(760, 34), 11, skin.bone4);
            UIFactory.Label(c, "K / Esc: close", new Vector2(0.5f, 0f), new Vector2(0, 6), new Vector2(300, 14), 11, skin.bone4);
            _skills.gameObject.SetActive(false);
            PlayerSkills.Changed += RefreshSkills;
            PlayerSkills.SoulsGained += OnSoulsGained;

            // HUD: souls counter (bottom right, above the hint line) and popups
            _hudSouls = UIFactory.Label(_hud, "", new Vector2(1, 0), new Vector2(-110, 30), new Vector2(200, 18), 14, new Color(0.85f, 0.78f, 1f), TextAnchor.LowerRight, true);
            _soulsPopup = UIFactory.Label(_hud, "", new Vector2(1, 0), new Vector2(-110, 50), new Vector2(240, 20), 16, new Color(0.9f, 0.82f, 1f), TextAnchor.LowerRight, true);
        }

        SkillNode MakeSkillNode(RectTransform parent, Vector2 pos, PlayerSkills.Stat stat)
        {
            var n = new SkillNode();
            var rt = UIFactory.Rect(stat.ToString(), parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), pos, new Vector2(190, 132));
            n.frame = UIFactory.Image(rt, skin.panelClean, true, Color.Lerp(SkillColor[(int)stat], Color.white, 0.55f));
            RectTransform At(float x, float y, float w, float h) => UIFactory.Rect("E", rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, y), new Vector2(w, h));
            n.name = UIFactory.Text(At(0, -6, 190, 22), PlayerSkills.Name(stat), 17, SkillColor[(int)stat], TextAnchor.MiddleCenter, true);
            n.pips = new Image[PlayerSkills.MaxRank];
            for (int k = 0; k < PlayerSkills.MaxRank; k++)
                n.pips[k] = UIFactory.Image(At(18 + k * 16, -32, 12, 12), MakeSolid(), false, new Color(0.2f, 0.18f, 0.18f));
            n.effect = UIFactory.Text(At(0, -48, 190, 18), "", 13, skin.bone0, TextAnchor.MiddleCenter);
            n.cost = UIFactory.Text(At(0, -68, 190, 16), "", 11, skin.bone3, TextAnchor.MiddleCenter);
            n.buy = UIFactory.Button(rt, "Train", new Vector2(0.5f, 0f), new Vector2(0, 22), 110, () =>
            {
                if (PlayerSkills.TryBuy(stat)) { Sfx.Play2D(SfxId.RelicReveal, 0.45f, 1.5f); }
            });
            { var hit = rt.GetComponent<Image>(); if (hit) hit.raycastTarget = true; }
            AddHover(rt.gameObject, () => SkillTip(stat));
            return n;
        }

        void SkillTip(PlayerSkills.Stat s)
        {
            if (!_tooltip) return;
            _tipFor = null;
            _tipName.text = PlayerSkills.Name(s); _tipName.color = SkillColor[(int)s];
            _tipKind.text = $"SKILL · RANK {PlayerSkills.Rank(s)}/{PlayerSkills.MaxRank}";
            _tipBody.text = PlayerSkills.Describe(s);
            _tipState.text = PlayerSkills.Rank(s) >= PlayerSkills.MaxRank ? "Mastered" : $"Next rank: {PlayerSkills.Cost(s)} souls";
            _tooltip.SetParent(_skills, false);
            _tooltip.gameObject.SetActive(true);
            _tooltip.SetAsLastSibling();
        }

        void ShowSkills(bool on)
        {
            _skills.gameObject.SetActive(on);
            GameInput.GameplayBlocked = on || _paused || InventoryOpen;
            GameInput.SetCursorLocked(!on && !_paused && !InventoryOpen);
            _tooltip.gameObject.SetActive(false);
            _tooltip.SetParent(on ? _skills : _inventory, false);
            if (on) RefreshSkills();
        }

        void RefreshSkills()
        {
            if (_hudSouls) _hudSouls.text = $"Souls  {PlayerSkills.Souls}";
            if (!SkillsOpen) return;
            _skSouls.text = $"Souls  {PlayerSkills.Souls}";
            for (int i = 0; i < PlayerSkills.StatCount; i++)
            {
                var s = (PlayerSkills.Stat)i;
                var n = _skNodes[i];
                int r = PlayerSkills.Rank(s);
                for (int k = 0; k < n.pips.Length; k++) n.pips[k].color = k < r ? SkillColor[i] : new Color(0.2f, 0.18f, 0.18f);
                n.effect.text = r > 0 ? PlayerSkills.Effect(s, r) : "untrained";
                n.cost.text = r >= PlayerSkills.MaxRank ? "mastered" : $"next: {PlayerSkills.Cost(s)} souls";
                n.buy.interactable = PlayerSkills.CanBuy(s);
            }
        }

        void OnSoulsGained(int n, bool boss)
        {
            _soulsPopupSum = _soulsPopupT > 0f ? _soulsPopupSum + n : n;
            _soulsPopupT = boss ? 4f : 2.2f;
            if (boss) ShowMessage("SOULS RECLAIMED", $"+{n} souls  ·  press K to train", new Color(0.85f, 0.75f, 1f), 3.5f, 36);
        }

        void UpdateSoulsHud()
        {
            if (!_hudSouls) return;
            bool show = PlayerController.Local != null && _hud.gameObject.activeSelf;
            _hudSouls.gameObject.SetActive(show);
            if (show && Time.frameCount % 15 == 0) _hudSouls.text = $"Souls  {PlayerSkills.Souls}";
            _soulsPopupT -= Time.deltaTime;
            bool pop = show && _soulsPopupT > 0f;
            _soulsPopup.gameObject.SetActive(pop);
            if (pop)
            {
                _soulsPopup.text = $"+{_soulsPopupSum} souls";
                var col = _soulsPopup.color; col.a = Mathf.Clamp01(_soulsPopupT * 2f); _soulsPopup.color = col;
            }
        }
    }
}
