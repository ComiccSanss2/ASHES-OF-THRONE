using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DarkFantasy.UI
{
    /// <summary>
    /// Inventory (I): the character on the left (drag to turn, scroll to zoom), the equipment slots in
    /// the middle (helmet, armour, legs, boots, weapon), the stock on the right in three categories
    /// (Usables, Weapons, Armour) with icons; hovering an item shows its card, clicking equips / uses it.
    /// </summary>
    public partial class GameUI
    {
        [Header("Item icons (built by the Dark Fantasy builder)")]
        [Tooltip("class * 4 + tier")] public Sprite[] weaponIcons;
        [Tooltip("(set - 1) * 4 + slot")] public Sprite[] armorIcons;
        [Tooltip("Ember Flask, Iron Draught, Vigor Tonic, Soul Salve")] public Sprite[] potionIcons;

        enum InvTab { Usables, Weapons, Armour }

        class InvCell { public RectTransform root; public Image frame, icon, glow; public Text count, badge; }
        class SlotView { public Image icon, frame; public RawImage live; public Text name, state; }

        RectTransform _inventory, _invGrid, _tooltip;
        Text _invTitle, _invSouls, _invStats, _tipName, _tipKind, _tipBody, _tipState;
        RawImage _invPreviewImg;
        InventoryPreview _preview;
        InvTab _tab = InvTab.Weapons;
        int _armorFilter = -1;              // -1 all slots, else Inventory.Slot
        readonly Button[] _tabBtn = new Button[3];
        readonly SlotView[] _eqSlots = new SlotView[5];  // 0..3 armour slots, 4 weapon
        readonly List<InvCell> _cells = new List<InvCell>();
        string _tipFor;

        static readonly Color TierRelic = new Color(1f, 0.72f, 0.38f), TierDragon = new Color(1f, 0.42f, 0.25f), TierRadiant = new Color(1f, 0.96f, 0.78f);
        static readonly Color SetAshen = new Color(1f, 0.72f, 0.38f), SetWyrm = new Color(1f, 0.42f, 0.25f), SetMirror = new Color(0.86f, 0.9f, 1f), SetDeath = new Color(0.78f, 0.55f, 1f);

        void BuildInventory()
        {
            _inventory = UIFactory.Stretch("Inventory", _root);
            UIFactory.Image(UIFactory.Stretch("Veil", _inventory), null, false, new Color(0, 0, 0, 0.6f)).raycastTarget = true;
            var c = UIFactory.Window(_inventory, "Inventory", new Vector2(1080, 660), out var invWin);
            UIFactory.Button(invWin, "Back", new Vector2(1, 1), new Vector2(-84, -27), 120, () => ShowInventory(false), true);
            RectTransform At(string n, Transform parent, float x, float y, float w, float h) =>
                UIFactory.Rect(n, parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, y), new Vector2(w, h));

            // ---- left: the character
            var left = UIFactory.Panel(c, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -4), new Vector2(330, 568));
            _invTitle = UIFactory.Text(At("Title", left, 0, -6, 330, 24), "", 18, skin.gold2, TextAnchor.MiddleCenter, true);
            var view = At("View", left, 10, -34, 310, 446);
            _invPreviewImg = view.gameObject.AddComponent<RawImage>();
            _invPreviewImg.color = Color.white;
            var drag = view.gameObject.AddComponent<PreviewDrag>(); drag.ui = this;
            UIFactory.Text(At("Hint", left, 0, -484, 330, 16), "drag to turn · scroll to zoom", 11, skin.bone4);
            _invStats = UIFactory.Text(At("Stats", left, 12, -506, 306, 52), "", 11, skin.bone3, TextAnchor.UpperCenter);

            // ---- middle: equipment slots
            var mid = UIFactory.Panel(c, new Vector2(0, 1), new Vector2(0, 1), new Vector2(354, -4), new Vector2(270, 568));
            UIFactory.Text(At("T", mid, 0, -6, 270, 22), "Equipment", 16, skin.bone1, TextAnchor.MiddleCenter, true);
            string[] slotLabels = { "Helmet", "Armour", "Legs", "Boots", "Weapon" };
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                var row = At("Slot" + i, mid, 10, -34 - i * 88, 250, 84);
                var frameRt = At("Frame", row, 0, -2, 80, 80);
                var sv = new SlotView();
                sv.frame = UIFactory.Image(frameRt, skin.panelClean, true, new Color(0.75f, 0.7f, 0.65f));
                sv.icon = UIFactory.Image(UIFactory.Stretch("Icon", frameRt, 6, 6, 6, 6), null, false);
                sv.icon.preserveAspect = true;
                sv.live = UIFactory.Stretch("Live", frameRt, 3, 3, 3, 3).gameObject.AddComponent<RawImage>();
                sv.live.raycastTarget = false; sv.live.enabled = false;
                sv.frame.raycastTarget = true;   // the frame is the button's hit area
                var btn = frameRt.gameObject.AddComponent<Button>(); btn.targetGraphic = sv.frame;
                btn.onClick.AddListener(() => OnSlotClicked(idx));
                AddHover(frameRt.gameObject, () => SlotTooltip(idx));
                UIFactory.Text(At("L", row, 90, -4, 160, 16), slotLabels[i].ToUpperInvariant(), 11, skin.bone4, TextAnchor.MiddleLeft, true);
                sv.name = UIFactory.Text(At("N", row, 90, -22, 160, 36), "", 14, skin.bone0, TextAnchor.UpperLeft, true);
                sv.state = UIFactory.Text(At("S", row, 90, -58, 160, 22), "", 11, skin.bone3, TextAnchor.UpperLeft);
                _eqSlots[i] = sv;
            }
            _invSouls = UIFactory.Text(At("Souls", mid, 10, -478, 250, 20), "", 15, new Color(0.85f, 0.78f, 1f), TextAnchor.MiddleCenter, true);
            var sk = UIFactory.Button(mid, "Skills  (K)", new Vector2(0.5f, 1f), new Vector2(0, -528), 170, () => { ShowInventory(false); ShowSkills(true); });

            // ---- right: the stock
            var right = UIFactory.Panel(c, new Vector2(0, 1), new Vector2(0, 1), new Vector2(634, -4), new Vector2(432, 568));
            string[] tabs = { "Usables", "Weapons", "Armour" };
            for (int i = 0; i < 3; i++)
            {
                int t = i;
                _tabBtn[i] = UIFactory.Button(right, tabs[i], new Vector2(0, 1), new Vector2(76 + i * 140, -22), 132, () => { _tab = (InvTab)t; _armorFilter = -1; RefreshInventory(); }, true);
            }
            _invGrid = At("Grid", right, 12, -48, 408, 496);
            UIFactory.Label(c, "I / Esc: close   ·   hover an item to read it, click to equip or use", new Vector2(0.5f, 0f), new Vector2(0, 12), new Vector2(700, 16), 11, skin.bone4);

            // ---- tooltip (follows the mouse)
            _tooltip = UIFactory.Panel(_inventory, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(300, 170));
            _tooltip.GetComponent<Image>().raycastTarget = false;
            // the tooltip follows the cursor: it must never catch the clicks meant for the item under it
            var tcg = _tooltip.gameObject.AddComponent<CanvasGroup>(); tcg.blocksRaycasts = false; tcg.interactable = false;
            _tipName = UIFactory.Text(At("N", _tooltip, 12, -8, 276, 22), "", 16, skin.gold2, TextAnchor.MiddleLeft, true);
            _tipKind = UIFactory.Text(At("K", _tooltip, 12, -30, 276, 14), "", 11, skin.bone4, TextAnchor.MiddleLeft, true);
            _tipBody = UIFactory.Text(At("B", _tooltip, 12, -48, 276, 96), "", 12, skin.bone1, TextAnchor.UpperLeft);
            _tipState = UIFactory.Text(At("S", _tooltip, 12, -146, 276, 18), "", 11, skin.bone3, TextAnchor.MiddleLeft);
            foreach (var g in _tooltip.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            _tooltip.gameObject.SetActive(false);

            _inventory.gameObject.SetActive(false);
            Potions.Changed += RefreshInventory;
            PlayerSkills.Changed += RefreshInventory;
            PlayerArmor.VisualChanged += a => { if (InventoryOpen) RefreshInventory(); };
        }

        bool InventoryOpen => _inventory && _inventory.gameObject.activeSelf;

        void ShowInventory(bool on)
        {
            _inventory.gameObject.SetActive(on);
            GameInput.GameplayBlocked = on || _paused || SkillsOpen;
            GameInput.SetCursorLocked(!on && !_paused && !SkillsOpen);
            if (!on) { _tooltip.gameObject.SetActive(false); _preview?.Hide(); return; }
            if (_preview == null) _preview = InventoryPreview.Create(620, 940);
            _invPreviewImg.texture = _preview.Texture;
            RefreshInventory();
        }

        /// <summary>Development: raycasts the UI at the centre of the first cells and logs who would get the click.</summary>
        public void DevInvProbe(string _)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) { Debug.Log("[DF] probe: no EventSystem"); return; }
            int n = 0;
            foreach (var c in _cells)
            {
                if (c == null || c.frame == null || n++ >= 4) continue;
                var rt = (RectTransform)c.frame.transform;
                Vector2 sp = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
                var hits = new List<UnityEngine.EventSystems.RaycastResult>();
                es.RaycastAll(new UnityEngine.EventSystems.PointerEventData(es) { position = sp }, hits);
                Debug.Log($"[DF] probe cell {n} at {sp}: " + string.Join(" > ", hits.ConvertAll(h => h.gameObject.name + (h.gameObject.GetComponent<Button>() ? "[btn]" : "")).GetRange(0, Mathf.Min(4, hits.Count))) + $" · module {es.currentInputModule?.GetType().Name}");
            }
        }

        void UpdateInventory()
        {
            if (InventoryOpen) _preview?.Show(PlayerController.Local);
            if (_tooltip && _tooltip.gameObject.activeSelf && (InventoryOpen || SkillsOpen))
            {
                var parent = (RectTransform)_tooltip.parent;
                Vector2 mp = UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.position.ReadValue() : Vector2.zero;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, mp, null, out var lp);
                var r = parent.rect;
                Vector2 pos = lp - r.min + new Vector2(18f, -12f);
                if (pos.x + 300f > r.width) pos.x -= 336f;
                if (pos.y - 170f < 0f) pos.y = 170f;
                _tooltip.anchoredPosition = pos;
            }
        }

        // ------------------------------------------------------------------ refresh
        void RefreshInventory()
        {
            if (!_inventory || !InventoryOpen) return;
            var p = PlayerController.Local;
            var cls = p ? p.playerClass : MenuSettings.PlayerClass;
            var v = p ? p.vitals : null;
            _invTitle.text = (v ? v.DisplayName : "") + "  ·  " + cls;
            _invSouls.text = $"Souls  {PlayerSkills.Souls}";
            if (v)
                _invStats.text = $"Health {Mathf.RoundToInt(v.MaxHealth)}  ·  Stamina {Mathf.RoundToInt(v.maxStamina)}" + (v.HasManaPool ? $"  ·  Mana {Mathf.RoundToInt(v.maxMana)}" : "")
                    + $"\nDamage taken {Mathf.RoundToInt((p.armor ? p.armor.DamageTakenMul : 1f) * 100f)}%  ·  skill ranks {PlayerSkills.TotalRanks}";

            // slots
            for (int s = 0; s < 4; s++)
            {
                byte set = Inventory.EquippedArmor(cls, (Inventory.Slot)s);
                var sv = _eqSlots[s];
                sv.icon.sprite = ArmorIcon(set, s);
                sv.icon.color = set == Inventory.ArmorNone ? new Color(1f, 1f, 1f, 0.18f) : Color.white;
                var liveA = set != Inventory.ArmorNone && p ? ItemIcons.Armor(p, set, s) : null;
                sv.live.texture = liveA; sv.live.enabled = liveA != null; sv.icon.enabled = liveA == null;
                sv.name.text = set == Inventory.ArmorNone ? "<color=#8a7a70>empty</color>" : Inventory.ArmorName(set, (Inventory.Slot)s);
                sv.name.color = SetColor(set);
                sv.state.text = set == Inventory.ArmorNone ? "click: choose a piece" : CastleProgress.ArmorEarned(set) ? $"-{Inventory.ArmorReduction(set) * 100f:0.#}% damage" : "<color=#8f8a9e>dormant</color>";
                sv.frame.color = _tab == InvTab.Armour && _armorFilter == s ? skin.gold2 : new Color(0.75f, 0.7f, 0.65f);
            }
            byte w = p ? p.EquippedWeapon : Inventory.Equipped(cls);
            _eqSlots[4].icon.sprite = WeaponIcon(cls, w);
            _eqSlots[4].icon.color = Color.white;
            var liveW = p ? ItemIcons.Weapon(p, w) : null;
            _eqSlots[4].live.texture = liveW; _eqSlots[4].live.enabled = liveW != null; _eqSlots[4].icon.enabled = liveW == null;
            _eqSlots[4].name.text = Inventory.Name(cls, w);
            _eqSlots[4].name.color = TierColor(w);
            _eqSlots[4].state.text = w == Inventory.BaseWeapon ? "starting weapon" : CastleProgress.TierEarned(w) ? "awakened" : "<color=#8f8a9e>dormant</color>";
            _eqSlots[4].frame.color = _tab == InvTab.Weapons ? skin.gold2 : new Color(0.75f, 0.7f, 0.65f);

            for (int i = 0; i < 3; i++)
            {
                var img = _tabBtn[i].GetComponent<Image>();
                if (img) img.color = (int)_tab == i ? Color.white : new Color(0.65f, 0.6f, 0.58f);
            }

            // grid (rows of five, a header line above each group)
            foreach (var cell in _cells) Destroy(cell.root.gameObject);
            _cells.Clear();
            _gy = 0f; _gcol = 0;
            switch (_tab)
            {
                case InvTab.Usables:
                    GridHeader("Healing", skin.bone3);
                    AddCell(potionIcon(0), v ? $"{v.Flasks}/{v.maxFlasks}" : "", false, v && v.Flasks > 0, "flask", null, null, ItemIcons.Potion(0));
                    GridHeader("Draughts  ·  keys 2 / 3 / 4" + (p && p.IsBard ? " (bard: click here)" : ""), skin.bone3);
                    for (int k = 0; k < Potions.KindCount; k++)
                    {
                        var kind = (Potions.Kind)k;
                        AddCell(potionIcon(k + 1), Potions.Count(kind).ToString(), false, Potions.Count(kind) > 0, "potion" + k, () => { PlayerController.Local?.DrinkPotion(kind); }, null, ItemIcons.Potion(k + 1));
                    }
                    break;
                case InvTab.Weapons:
                    GridHeader(cls + " weapons", skin.bone3);
                    for (byte t = 0; t < Inventory.WeaponCount; t++)
                    {
                        byte tier = t;
                        bool owned = Inventory.Owns(cls, tier);
                        AddCell(WeaponIcon(cls, tier), "", w == tier, owned, "weapon" + tier, owned ? () => { PlayerController.Local?.Equip(tier); RefreshInventory(); } : (System.Action)null, TierColor(tier), p ? ItemIcons.Weapon(p, tier) : null);
                    }
                    break;
                case InvTab.Armour:
                    for (byte set = 1; set < Inventory.ArmorSetCount; set++)
                    {
                        GridHeader(Inventory.SetName(set) + (Inventory.OwnsArmorSet(set) ? "" : "  — locked"), Inventory.OwnsArmorSet(set) ? SetColor(set) : skin.bone4);
                        for (int s = 0; s < 4; s++)
                        {
                            if (_armorFilter >= 0 && s != _armorFilter) continue;
                            byte st = set; var slot = (Inventory.Slot)s;
                            bool owned = Inventory.OwnsArmorSet(st);
                            bool on = Inventory.EquippedArmor(cls, slot) == st;
                            AddCell(ArmorIcon(st, s), "", on, owned, $"armor{st}_{s}", owned ? () =>
                            {
                                Inventory.SetArmor(PlayerController.Local ? PlayerController.Local.playerClass : cls, slot, on ? Inventory.ArmorNone : st);
                                RefreshInventory();
                            } : (System.Action)null, SetColor(st), p ? ItemIcons.Armor(p, st, s) : null);
                        }
                    }
                    break;
            }
            if (_tipFor != null) ShowTip(_tipFor);
        }

        float _gy; int _gcol;
        const float CellStep = 80f;

        void GridHeader(string text, Color col)
        {
            if (_gcol > 0) { _gy += CellStep; _gcol = 0; }
            var h = UIFactory.Rect("H", _invGrid, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -_gy), new Vector2(408, 18));
            UIFactory.Text(h, text, 12, col, TextAnchor.MiddleLeft, true);
            _cells.Add(new InvCell { root = h });
            _gy += 20f;
        }

        Sprite potionIcon(int i) => potionIcons != null && i < potionIcons.Length && potionIcons[i] ? potionIcons[i] : skin.iconPotion;

        void AddCell(Sprite icon, string count, bool equipped, bool owned, string tipKey, System.Action click, Color? rarity = null, Texture live = null)
        {
            var rt = UIFactory.Rect("Cell", _invGrid, new Vector2(0, 1), new Vector2(0, 1), new Vector2(_gcol * 82, -_gy), new Vector2(74, 74));
            var cell = new InvCell { root = rt };
            cell.frame = UIFactory.Image(rt, skin.panelClean, true, equipped ? skin.gold2 : owned ? new Color(0.7f, 0.66f, 0.62f) : new Color(0.35f, 0.33f, 0.32f));
            if (rarity.HasValue && owned)
            {
                cell.glow = UIFactory.Image(UIFactory.Stretch("Glow", rt, 4, 4, 4, 4), MakeSolid(), false, new Color(rarity.Value.r, rarity.Value.g, rarity.Value.b, 0.12f));
                cell.glow.raycastTarget = false;
            }
            if (live != null)
            {
                // the item itself, rendered in the game (ItemIcons)
                var raw = UIFactory.Stretch("Icon", rt, 4, 4, 4, 4).gameObject.AddComponent<RawImage>();
                raw.texture = live; raw.raycastTarget = false;
                raw.color = owned ? Color.white : new Color(0.12f, 0.1f, 0.12f, 0.9f);
            }
            else
            {
                cell.icon = UIFactory.Image(UIFactory.Stretch("Icon", rt, 7, 7, 7, 7), icon, false, owned ? Color.white : new Color(0f, 0f, 0f, 0.85f));
                cell.icon.preserveAspect = true; cell.icon.raycastTarget = false;
            }
            if (!string.IsNullOrEmpty(count))
                cell.count = UIFactory.Text(UIFactory.Rect("C", rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-5, 3), new Vector2(50, 16)), count, 13, skin.bone0, TextAnchor.LowerRight, true);
            if (equipped)
                cell.badge = UIFactory.Text(UIFactory.Rect("E", rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(4, -3), new Vector2(60, 14)), "E", 12, skin.gold2, TextAnchor.UpperLeft, true);
            cell.frame.raycastTarget = true;   // the frame is the button's hit area
            var btn = rt.gameObject.AddComponent<Button>(); btn.targetGraphic = cell.frame;
            btn.onClick.AddListener(() => { click?.Invoke(); Sfx.Play2D(SfxId.UiClick, 0.5f); });
            AddHover(rt.gameObject, () => ShowTip(tipKey));
            _cells.Add(cell);
            if (++_gcol >= 5) { _gcol = 0; _gy += CellStep; }
        }

        void AddHover(GameObject go, System.Action enter)
        {
            var et = go.AddComponent<EventTrigger>();
            var e1 = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            e1.callback.AddListener(_ => enter());
            var e2 = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            e2.callback.AddListener(_ => { _tipFor = null; _tooltip.gameObject.SetActive(false); });
            et.triggers.Add(e1); et.triggers.Add(e2);
        }

        // ------------------------------------------------------------------ tooltip
        void ShowTip(string key)
        {
            _tipFor = key;
            var p = PlayerController.Local;
            var cls = p ? p.playerClass : MenuSettings.PlayerClass;
            string name = "", kind = "", body = "", state = "";
            Color col = skin.gold2;
            if (key == "flask")
            {
                name = "Ember Flask"; kind = "USABLE · HEALING";
                body = "Embers of the bonfire sealed in clay. Drink to close your wounds.\nRestores " + (p && p.vitals ? Mathf.RoundToInt(p.vitals.flaskHeal * 100f) : 50) + "% health. Refilled at every flame.";
                state = "Press R to drink";
            }
            else if (key.StartsWith("potion"))
            {
                var k = (Potions.Kind)int.Parse(key.Substring(6));
                name = Potions.Name(k); kind = "USABLE · DRAUGHT"; body = Potions.Describe(k);
                state = Potions.Count(k) > 0 ? $"Click or press {(int)k + 2} to drink  ·  carried {Potions.Count(k)}" : "None left: fallen monsters sometimes carry one";
                col = k == Potions.Kind.IronDraught ? new Color(0.8f, 0.82f, 0.9f) : k == Potions.Kind.VigorTonic ? new Color(0.6f, 1f, 0.55f) : new Color(1f, 0.9f, 0.62f);
            }
            else if (key.StartsWith("weapon"))
            {
                byte t = byte.Parse(key.Substring(6));
                bool owned = Inventory.Owns(cls, t);
                col = TierColor(t);
                kind = "WEAPON · " + (t == Inventory.Radiant ? "RADIANT" : t == Inventory.Dragon ? "DRAGON" : t == Inventory.Relic ? "RELIC" : "STARTING");
                if (owned)
                {
                    name = Inventory.Name(cls, t); body = Inventory.Description(cls, t);
                    state = (p && p.EquippedWeapon == t ? "Equipped" : "Click to equip") + (t != Inventory.BaseWeapon && !CastleProgress.TierEarned(t) ? "  ·  dormant: " + CastleProgress.TierCondition(t) : "");
                }
                else
                {
                    name = "??? — locked"; col = skin.bone4;
                    body = t == Inventory.Radiant ? "Some say a radiant chest waits where no map shows a door. Its light is not always there."
                         : t == Inventory.Dragon ? "Slay Vaelthrax, the Cinder Wyrm, and claim its hoard." : "Defeat The Ashen King and open the chest he guards.";
                }
            }
            else if (key.StartsWith("armor"))
            {
                var parts = key.Substring(5).Split('_');
                byte set = byte.Parse(parts[0]); var slot = (Inventory.Slot)int.Parse(parts[1]);
                bool owned = Inventory.OwnsArmorSet(set);
                col = SetColor(set);
                kind = "ARMOUR · " + Inventory.SlotName(slot).ToUpperInvariant() + " · " + Inventory.SetName(set).ToUpperInvariant();
                if (owned)
                {
                    name = Inventory.ArmorName(set, slot); body = Inventory.ArmorDescription(set, slot);
                    bool on = Inventory.EquippedArmor(cls, slot) == set;
                    state = (on ? "Equipped · click to take off" : "Click to wear") + (!CastleProgress.ArmorEarned(set) ? "  ·  dormant: " + CastleProgress.ArmorCondition(set) : "");
                }
                else
                {
                    name = "??? — locked"; col = skin.bone4;
                    body = "Won by every member of the party who defeats " + (set == Inventory.ArmorAshen ? "the Ashen King." : set == Inventory.ArmorWyrm ? "Vaelthrax, the Cinder Wyrm." : set == Inventory.ArmorMirror ? "the Shattered Mirror of Malice." : "the Reaper beneath the red moon.");
                }
            }
            else if (key.StartsWith("slot"))
            {
                ShowTip(SlotKey(int.Parse(key.Substring(4)))); return;
            }
            _tipName.text = name; _tipName.color = col; _tipKind.text = kind; _tipBody.text = body; _tipState.text = state;
            _tooltip.gameObject.SetActive(true);
            _tooltip.SetAsLastSibling();
        }

        string SlotKey(int i)
        {
            var p = PlayerController.Local;
            var cls = p ? p.playerClass : MenuSettings.PlayerClass;
            if (i == 4) return "weapon" + (p ? p.EquippedWeapon : Inventory.Equipped(cls));
            byte set = Inventory.EquippedArmor(cls, (Inventory.Slot)i);
            return set == Inventory.ArmorNone ? null : $"armor{set}_{i}";
        }

        void SlotTooltip(int i)
        {
            string k = SlotKey(i);
            if (k == null)
            {
                _tipFor = null;
                _tipName.text = (i == 0 ? "Helmet" : i == 1 ? "Armour" : i == 2 ? "Legs" : "Boots") + " — empty"; _tipName.color = skin.bone3;
                _tipKind.text = "ARMOUR SLOT"; _tipBody.text = "Every boss you defeat gives the whole party a set of armour in its image. Click to see the pieces for this slot.";
                _tipState.text = "";
                _tooltip.gameObject.SetActive(true);
                return;
            }
            ShowTip(k);
        }

        void OnSlotClicked(int i)
        {
            if (i == 4) { _tab = InvTab.Weapons; _armorFilter = -1; }
            else { _tab = InvTab.Armour; _armorFilter = _armorFilter == i ? -1 : i; }
            RefreshInventory();
        }

        Sprite WeaponIcon(PlayerClass c, byte tier)
        {
            int i = (int)c * 4 + tier;
            return weaponIcons != null && i < weaponIcons.Length && weaponIcons[i] ? weaponIcons[i] : skin.iconSword;
        }

        Sprite ArmorIcon(byte set, int slot)
        {
            if (set == Inventory.ArmorNone) return skin.iconShield;
            int i = (set - 1) * 4 + slot;
            return armorIcons != null && i < armorIcons.Length && armorIcons[i] ? armorIcons[i] : skin.iconShield;
        }

        static Color TierColor(byte t) => t == Inventory.Radiant ? TierRadiant : t == Inventory.Dragon ? TierDragon : t == Inventory.Relic ? TierRelic : new Color(0.95f, 0.9f, 0.82f);
        static Color SetColor(byte s) => s == Inventory.ArmorAshen ? SetAshen : s == Inventory.ArmorWyrm ? SetWyrm : s == Inventory.ArmorMirror ? SetMirror : s == Inventory.ArmorDeath ? SetDeath : new Color(0.8f, 0.75f, 0.7f);

        public void PreviewDragged(float dx) => _preview?.Drag(dx);
        public void PreviewScrolled(float d) => _preview?.Zoom(d);
    }

    /// <summary>Turns / zooms the inventory's character preview.</summary>
    public class PreviewDrag : MonoBehaviour, IDragHandler, IScrollHandler
    {
        public GameUI ui;
        public void OnDrag(PointerEventData e) => ui?.PreviewDragged(e.delta.x);
        public void OnScroll(PointerEventData e) => ui?.PreviewScrolled(e.scrollDelta.y);
    }
}
