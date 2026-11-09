using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DarkFantasy.UI
{
    /// <summary>
    /// Bard HUD: mana bar, the spell bar (bottom centre: icon, key, name, mana cost, cooldown),
    /// buff/curse status icons on the player, the party and the enemies.
    /// </summary>
    public partial class GameUI
    {
        [Header("Bard")]
        [Tooltip("Mending Hymn, War Anthem, Dirge of Ruin, Thunderous Crescendo.")]
        public Sprite[] spellIcons = new Sprite[4];

        static readonly string[] SpellShort = { "Heal allies", "Party buff", "Curse foe", "Area blast" };
        static readonly Color[] SpellColor = { new Color(0.55f, 1f, 0.6f), new Color(1f, 0.8f, 0.4f), new Color(0.78f, 0.55f, 1f), new Color(0.6f, 0.85f, 1f) };

        UIBar _manaBar;
        RectTransform _spellBar;
        Text _castFail;
        float _castFailTimer;
        RectTransform _statusRoot;
        Image _statusAnthem;
        Text _statusAnthemT;
        readonly Dictionary<EnemyBase, Image> _curseIcons = new Dictionary<EnemyBase, Image>();
        readonly Dictionary<PlayerController, Image> _partyBuff = new Dictionary<PlayerController, Image>();
        Image _bossCurse;
        Vector2 _bossBase, _promptBase;

        class Slot
        {
            public RectTransform root;
            public Image icon, cd, frame, glow;
            public Text cdText, name, desc, cost, key;
        }
        readonly Slot[] _slots = new Slot[4];

        void BuildBardHud()
        {
            _manaBar = UIBar.Create(_hud, skin.barFrameMana ? skin.barFrameMana : skin.barFrameXp, skin.barFillMana ? skin.barFillMana : skin.barFillXp,
                                    new Vector2(0, 1), new Vector2(0, 1), new Vector2(38, -50), 176, new Color(0.45f, 0.65f, 1f));
            _manaBar.gameObject.SetActive(false);
            // the smoker: breath drawn (pips) and spirits alive, under the mana bar
            _smokeInfo = UIFactory.Label(_hud, "", new Vector2(0, 1), new Vector2(225, -70), new Vector2(420, 20), 14, new Color(0.82f, 0.78f, 0.7f), TextAnchor.MiddleLeft);
            _smokeInfo.gameObject.SetActive(false);
            BuildSmokeCharge();
            SmokerCaster.CastFailed += why => { _smokeFail = why; _smokeFailT = 1.2f; };

            // buff icon row under the bars
            _statusRoot = UIFactory.Rect("Status", _hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -96), new Vector2(200, 30));
            var a = UIFactory.Rect("Anthem", _statusRoot, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(28, 28));
            _statusAnthem = UIFactory.Image(a, spellIcons != null && spellIcons.Length > 1 ? spellIcons[1] : null, false);
            _statusAnthemT = UIFactory.Label(a, "", new Vector2(1, 0.5f), new Vector2(22, 0), new Vector2(40, 18), 13, skin.gold2, TextAnchor.MiddleLeft);
            _statusRoot.gameObject.SetActive(false);

            // spell bar
            const float W = 58f, gap = 76f;
            _spellBar = UIFactory.Rect("SpellBar", _hud, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(gap * 4, 110));
            for (int i = 0; i < 4; i++)
            {
                var s = new Slot();
                float x = (i - 1.5f) * gap;
                s.root = UIFactory.Rect("Spell" + (i + 1), _spellBar, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(x, 4), new Vector2(W, W));
                var glowRt = UIFactory.Rect("Glow", s.root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W + 14, W + 14));
                s.glow = UIFactory.Image(glowRt, null, false, new Color(SpellColor[i].r, SpellColor[i].g, SpellColor[i].b, 0f));
                s.frame = UIFactory.Image(UIFactory.Stretch("Frame", s.root, -3, -3, -3, -3), skin.panelClean, true, new Color(0.9f, 0.85f, 0.8f, 1f));
                s.icon = UIFactory.Image(UIFactory.Stretch("Icon", s.root, 2, 2, 2, 2), spellIcons != null && i < spellIcons.Length ? spellIcons[i] : null, false);
                var cdRt = UIFactory.Stretch("Cooldown", s.root, 2, 2, 2, 2);
                s.cd = UIFactory.Image(cdRt, MakeSolid(), false, new Color(0f, 0f, 0f, 0.68f));
                s.cd.type = Image.Type.Filled; s.cd.fillMethod = Image.FillMethod.Radial360; s.cd.fillOrigin = (int)Image.Origin360.Top; s.cd.fillClockwise = false;
                s.cdText = UIFactory.Text(UIFactory.Stretch("CdT", s.root), "", 20, skin.bone0, TextAnchor.MiddleCenter, true);
                // key badge (top-left)
                var glyph = InputGlyph.Create(s.root, GameAction.Slot1 + i, new Vector2(0, 1), new Vector2(2, -2), 22f);
                s.key = glyph.GetComponentInChildren<Text>();
                // mana cost (bottom-right)
                s.cost = UIFactory.Label(s.root, "", new Vector2(1, 0), new Vector2(-14, 8), new Vector2(30, 14), 12, new Color(0.6f, 0.78f, 1f), TextAnchor.MiddleRight);
                // name + effect above
                s.name = UIFactory.Label(s.root, "", new Vector2(0.5f, 1), new Vector2(0, 26), new Vector2(gap + 8, 16), 13, SpellColor[i], TextAnchor.MiddleCenter, true);
                s.desc = UIFactory.Label(s.root, SpellShort[i], new Vector2(0.5f, 1), new Vector2(0, 11), new Vector2(gap - 2, 14), 11, skin.bone1, TextAnchor.MiddleCenter);
                s.desc.horizontalOverflow = HorizontalWrapMode.Overflow;
                _slots[i] = s;
            }
            _castFail = UIFactory.Label(_spellBar, "", new Vector2(0.5f, 1), new Vector2(0, 24), new Vector2(300, 20), 16, skin.hp2, TextAnchor.MiddleCenter, true);
            _spellBar.gameObject.SetActive(false);
            _bossBase = _bossRoot.anchoredPosition;
            _promptBase = _promptRt.anchoredPosition;

            var bc = UIFactory.Rect("BossCurse", _bossRoot, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(130, -18), new Vector2(26, 26));
            _bossCurse = UIFactory.Image(bc, spellIcons != null && spellIcons.Length > 2 ? spellIcons[2] : null, false);
            _bossCurse.gameObject.SetActive(false);

            BardCaster.CastFailed += OnCastFailed;
        }

        void OnCastFailed(string why) { if (_castFail) { _castFail.text = why; _castFailTimer = 1.2f; } }

        static Sprite _solid;
        static Sprite MakeSolid()
        {
            if (_solid) return _solid;
            var t = new Texture2D(4, 4); var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.white;
            t.SetPixels(px); t.Apply();
            _solid = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            return _solid;
        }

        Text _smokeInfo;
        string _smokeFail;
        float _smokeFailT;

        // ---- the smoker's charge, in the middle of the screen while he draws on the pipe
        RectTransform _chargeRt;
        CanvasGroup _chargeCg;
        Image[] _chargeFill, _chargeBack;
        Text _chargeTitle, _chargeSub;
        float _chargePulse;

        void BuildSmokeCharge()
        {
            _chargeRt = UIFactory.Rect("SmokeCharge", _hud, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -80f), new Vector2(330f, 92f));
            _chargeCg = _chargeRt.gameObject.AddComponent<CanvasGroup>(); _chargeCg.alpha = 0f; _chargeCg.blocksRaycasts = false;
            var bg = UIFactory.Image(UIFactory.Stretch("Bg", _chargeRt), _solid ? _solid : (_solid = MakeSolid()), false, new Color(0.04f, 0.035f, 0.03f, 0.72f));
            var edge = UIFactory.Image(UIFactory.Rect("Edge", _chargeRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(330f, 2f)), _solid, false, new Color(1f, 0.6f, 0.3f, 0.7f));
            _chargeTitle = UIFactory.Label(_chargeRt, "DRAWING SMOKE", new Vector2(0.5f, 1f), new Vector2(0f, -15f), new Vector2(320f, 24f), 19, new Color(1f, 0.82f, 0.62f), TextAnchor.MiddleCenter, true);
            _chargeFill = new Image[3]; _chargeBack = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var slot = UIFactory.Rect("Spirit" + i, _chargeRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 100f, -2f), new Vector2(88f, 16f));
                _chargeBack[i] = UIFactory.Image(slot, _solid, false, new Color(0.22f, 0.2f, 0.18f, 0.9f));
                var f = UIFactory.Rect("Fill", slot, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(84f, 12f));
                _chargeFill[i] = UIFactory.Image(f, _solid, false, new Color(1f, 0.55f, 0.22f));
                _chargeFill[i].type = Image.Type.Filled; _chargeFill[i].fillMethod = Image.FillMethod.Horizontal; _chargeFill[i].fillAmount = 0f;
            }
            _chargeSub = UIFactory.Label(_chargeRt, "", new Vector2(0.5f, 0f), new Vector2(0f, 15f), new Vector2(320f, 20f), 14, new Color(0.85f, 0.8f, 0.72f));
        }

        void UpdateSmokerHud(PlayerController p)
        {
            var sm = SmokerCaster.Local;
            bool on = p != null && p.IsSmoker && sm != null;
            if (_smokeInfo.gameObject.activeSelf != on) _smokeInfo.gameObject.SetActive(on);
            bool charging = on && sm.Inhaling && !p.IsDead;
            _chargeCg.alpha = Mathf.MoveTowards(_chargeCg.alpha, charging ? 1f : 0f, Time.deltaTime * (charging ? 8f : 3f));
            if (!on) return;
            // the corner line: how to use it, cooldowns, spirits alive
            string glyph = GameInput.UsingGamepadLast ? InputBindings.PadLabel(InputBindings.PadOf(GameAction.Block)) : InputBindings.KeyLabel(InputBindings.KeyOf(GameAction.Block));
            string draw = sm.DrawCooldown > 0f ? $"<color=#8a8070>breath in {sm.DrawCooldown:0.0}s</color>" : $"<color=#ffb070>[{glyph}]</color> hold: draw smoke";
            string gust = sm.GustCooldown > 0f ? $"   <color=#8a8070>gust {sm.GustCooldown:0.0}s</color>" : "";
            if (_smokeFailT > 0f) { _smokeFailT -= Time.deltaTime; _smokeInfo.text = "<color=#e07a62>" + _smokeFail + "</color>"; }
            else _smokeInfo.text = $"{draw}   Spirits {sm.Spirits}/{sm.maxSpirits}{gust}";
            if (_chargeCg.alpha <= 0.001f) return;
            float b = sm.Breath;
            int full = Mathf.FloorToInt(b + 0.001f);
            _chargePulse += Time.deltaTime;
            for (int i = 0; i < 3; i++)
            {
                bool exists = i < sm.maxBreath;
                _chargeBack[i].gameObject.SetActive(exists);
                if (!exists) continue;
                float f = Mathf.Clamp01(b - i);
                _chargeFill[i].fillAmount = f;
                float glow = f >= 1f ? 0.75f + 0.25f * Mathf.Sin(_chargePulse * 6f + i) : 0.55f;
                _chargeFill[i].color = f >= 1f ? new Color(1f, 0.62f + 0.15f * glow, 0.28f, 1f) : new Color(0.75f, 0.5f, 0.3f, 1f);
                _chargeBack[i].color = f >= 1f ? new Color(0.45f, 0.24f, 0.1f, 0.95f) : new Color(0.22f, 0.2f, 0.18f, 0.9f);
            }
            _chargeTitle.text = full >= sm.maxBreath ? "LUNGS FULL" : "DRAWING SMOKE";
            _chargeSub.text = full < 1 ? "keep holding..." : $"release to exhale  <color=#ffb070>{full} spirit{(full > 1 ? "s" : "")}</color>";
        }

        void UpdateBardHud()
        {
            var p = PlayerController.Local;
            var bard = BardCaster.Local;
            bool isBard = p != null && p.IsBard && bard != null;
            if (_spellBar.gameObject.activeSelf != isBard) _spellBar.gameObject.SetActive(isBard);
            bool mana = p != null && p.vitals != null && p.vitals.HasManaPool;
            if (_manaBar.gameObject.activeSelf != mana) _manaBar.gameObject.SetActive(mana);
            if (mana) _manaBar.SetValue(p.vitals.Mana01);
            UpdateSmokerHud(p);
            // keep the boss bar and the prompt above the spell bar
            _bossRoot.anchoredPosition = _bossBase + (isBard ? new Vector2(0, 104) : Vector2.zero);
            _promptRt.anchoredPosition = _promptBase + (isBard ? new Vector2(0, 74) : Vector2.zero);

            if (isBard)
            {
                for (int i = 0; i < 4; i++)
                {
                    var s = _slots[i];
                    var sp = bard.spells[i];
                    if (sp == null) continue;
                    s.name.text = sp.name;
                    float cost = bard.ManaCost(i);
                    s.cost.text = Mathf.RoundToInt(cost).ToString();
                    bool afford = bard.CanAfford(i);
                    float cd = bard.CooldownRemaining(i);
                    s.cd.fillAmount = bard.Cooldown01(i);
                    s.cdText.text = cd > 0.05f ? (cd >= 1f ? Mathf.CeilToInt(cd).ToString() : cd.ToString("0.0")) : "";
                    s.icon.color = afford ? Color.white : new Color(0.45f, 0.45f, 0.55f, 1f);
                    s.cost.color = afford ? new Color(0.6f, 0.78f, 1f) : skin.hp2;
                    bool casting = bard.Casting == i;
                    float pulse = casting ? 0.55f + 0.35f * Mathf.Sin(Time.time * 14f) : (cd <= 0f && afford ? 0.12f : 0f);
                    var gc = SpellColor[i]; gc.a = pulse;
                    s.glow.color = gc;
                    s.root.localScale = Vector3.one * (casting ? 1.1f : 1f);
                }
                if (_castFailTimer > 0f)
                {
                    _castFailTimer -= Time.deltaTime;
                    var c = _castFail.color; c.a = Mathf.Clamp01(_castFailTimer / 0.4f); _castFail.color = c;
                    if (_castFailTimer <= 0f) _castFail.text = "";
                }
            }

            // status: War Anthem on the local player
            var buffs = p ? p.buffs : null;
            bool anthem = buffs != null && buffs.AnthemActive;
            if (_statusRoot.gameObject.activeSelf != anthem) _statusRoot.gameObject.SetActive(anthem);
            if (anthem)
            {
                float r = buffs.AnthemRemaining;
                _statusAnthemT.text = Mathf.CeilToInt(r) + "s";
                _statusAnthem.color = new Color(1, 1, 1, r < 3f ? 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * 6f)) : 1f);
            }

            // boss curse icon
            EnemyBase boss = ReaperBossAI.Instance != null && ReaperBossAI.Instance.FightActive ? ReaperBossAI.Instance
                : MirrorAI.Instance != null && MirrorAI.Instance.FightActive ? MirrorAI.Instance
                : DragonAI.Instance != null && DragonAI.Instance.FightActive ? DragonAI.Instance : BossAI.Instance;
            bool bc = boss != null && _bossRoot.gameObject.activeSelf && boss.IsCursed;
            if (_bossCurse.gameObject.activeSelf != bc) _bossCurse.gameObject.SetActive(bc);
        }

        /// <summary>Small anthem icon before the party member's name.</summary>
        void UpdatePartyBuff(PlayerController pc, RectTransform row)
        {
            if (!_partyBuff.TryGetValue(pc, out var img) || img == null)
            {
                var rt = UIFactory.Rect("Buff", row, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(18, -8), new Vector2(16, 16));
                img = UIFactory.Image(rt, spellIcons != null && spellIcons.Length > 1 ? spellIcons[1] : null, false);
                _partyBuff[pc] = img;
            }
            bool on = pc.buffs != null && pc.buffs.AnthemActive;
            if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
        }

        /// <summary>Violet curse icon to the left of an enemy's overhead bar.</summary>
        void UpdateEnemyCurse(EnemyBase e, RectTransform barRoot)
        {
            if (!_curseIcons.TryGetValue(e, out var img) || img == null)
            {
                var rt = UIFactory.Rect("Curse", barRoot, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(-2, 0), new Vector2(16, 16));
                img = UIFactory.Image(rt, spellIcons != null && spellIcons.Length > 2 ? spellIcons[2] : null, false);
                _curseIcons[e] = img;
            }
            bool on = e.IsCursed;
            if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
        }
    }
}
