using System.Collections.Generic;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BuildBeacon
{
    /// <summary>
    /// Beacon panel with two tabs.
    /// Trophies: two sections, Boss trophies and Mob trophies. Each lists its slotted trophies (icon, name, effect in
    /// the right column, Remove) followed by one empty slot while capacity remains. Clicking an empty slot switches
    /// the list to a picker of eligible trophies of that kind from the player's inventory.
    /// Discounts: one row per material the beacon affects: icon and name on the left; "Free" or the discount, then
    /// the icons of the trophies responsible, on the right. The active tab is not clickable, as in vanilla's
    /// crafting tabs. On the Discounts tab a filter field (with a clear button) takes the hint line's place and
    /// narrows the rows by material or trophy name as the player types.
    /// The player's inventory window opens alongside (InventoryGui.Show with no container), with its crafting panel
    /// hidden for the duration; the beacon panel stays centred. Ctrl+click on a trophy in the inventory slots it
    /// (Patches/InventoryPatches.cs); Ctrl+click on a slotted trophy row removes it, a plain click shows the Discounts
    /// tab filtered to that trophy. Closing either closes both.
    /// Lives on the plugin GameObject so Update runs; the panel itself is built lazily on first open.
    /// </summary>
    internal class BeaconUI : MonoBehaviour
    {
        private const float PanelW = 640f, PanelH = 664f;
        private const float TabW = 150f, TabH = 36f, SourceIconSize = 34f;
        private const float Margin = 20f;
        private const float RowH = 60f, HeaderRowH = 30f, IconSize = 44f, RowGap = 4f;
        private const float ListH = 410f; // down to just above the Back/Close buttons
        private const int TitleSize = 26, BodySize = 17, SmallSize = 15;

        private static BeaconUI _instance;

        private GameObject _panel;
        private Text _title, _subtitle, _hint;   // _hint: the tab's help text, bottom left beside Close
        private RectTransform _scrollRt;
        private float _listTopWithFilter, _listTopNoFilter; // the list starts under the filter row, or takes its place
        private const float BottomButtonW = 120f, BottomGap = 16f, FooterH = 56f;
        private Button _closeButton, _backButton, _tabTrophies, _tabDiscounts, _clearFilter;
        private InputField _filter;
        private string _filterText = "";
        private bool _openedInventory, _inventorySeen; // we opened InventoryGui; it has been visible since
        private bool _hidCrafting;                     // we switched off InventoryGui.m_crafting and owe it back
        private RectTransform _content;
        private ScrollRect _scroll;
        private Sprite _iconFrame;
        private RectTransform _hoverBox;   // the level bar's hover text, drawn over the list
        private Text _hoverText;
        private RectTransform _hoverTrophies;
        // Each trophy in the hover takes a cell: the icon centred, its name (up to two lines) under it.
        private const float HoverW = 316f, HoverPad = 8f, HoverTrophySize = 30f, HoverTrophyGap = 4f, HoverTrophyCell = 56f;
        private const int HoverNameMaxSize = 12, HoverNameMinSize = 9;
        private HoverTarget _hovered;

        private BeaconController _beacon;
        private TrophyKind _picking = TrophyKind.None; // None = showing slots, otherwise the pool being picked for
        private enum Tab { Trophies, Discounts }
        private Tab _tab = Tab.Trophies;

        public static bool IsOpen => _instance != null && _instance._panel != null && _instance._panel.activeSelf;

        internal static void Init(GameObject host)
        {
            if (_instance == null) _instance = host.AddComponent<BeaconUI>();
        }

        public static void Open(BeaconController beacon)
        {
            if (_instance == null || beacon == null || !beacon.IsValid || Player.m_localPlayer == null) return;
            _instance.Show(beacon);
        }

        public static void CloseIfOpen()
        {
            if (IsOpen) _instance.Close();
        }

        // ---- Lifecycle ----

        private void Show(BeaconController beacon)
        {
            EnsureBuilt();
            if (_beacon != null && _beacon != beacon) _beacon.SetInUse(false);
            _beacon = beacon;
            _beacon.SetInUse(true);
            _picking = TrophyKind.None;
            _tab = Tab.Trophies;
            _filterText = "";
            _filter.SetTextWithoutNotify("");
            _panel.SetActive(true);
            GUIManager.BlockInput(true);
            OpenInventoryAlongside();
            Refresh();
        }

        private void OpenInventoryAlongside()
        {
            var inv = InventoryGui.instance;
            _openedInventory = inv != null && !InventoryGui.IsVisible();
            _inventorySeen = false;
            if (!_openedInventory) return;
            inv.Show(null, 1);
            // Vanilla only switches the crafting panel on once, in InventoryGui.Awake, so it stays off until Close.
            var crafting = inv.m_crafting != null ? inv.m_crafting.gameObject : null;
            _hidCrafting = crafting != null && crafting.activeSelf;
            if (_hidCrafting) crafting.SetActive(false);
        }

        private void Close()
        {
            HideHover(null);
            if (_panel != null) _panel.SetActive(false);
            if (_openedInventory && InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            if (_hidCrafting && InventoryGui.instance != null && InventoryGui.instance.m_crafting != null)
                InventoryGui.instance.m_crafting.gameObject.SetActive(true);
            _hidCrafting = false;
            _openedInventory = false;
            GUIManager.BlockInput(false);
            if (_beacon != null) _beacon.SetInUse(false);
            _beacon = null;
            _picking = TrophyKind.None;
        }

        private void Update()
        {
            if (!IsOpen) return;

            var player = Player.m_localPlayer;
            if (_beacon == null || !_beacon.IsValid || player == null || !_beacon.InRangeOf(player))
            {
                Close();
                return;
            }

            _beacon.PokeStation(); // vanilla connection threads to the boss holders, as for a station in use

            // The inventory window closes itself on Tab or Escape; take the panel with it.
            if (_openedInventory)
            {
                if (InventoryGui.IsVisible()) _inventorySeen = true;
                else if (_inventorySeen) { Close(); return; }
            }

            if (ZInput.GetKeyDown(KeyCode.Escape))
            {
                if (_picking != TrophyKind.None) { _picking = TrophyKind.None; Refresh(); }
                else Close();
            }
        }

        // ---- Inventory alongside ----

        /// <summary>Is the beacon panel open for this beacon? Used by the inventory Ctrl+click patch.</summary>
        public static BeaconController OpenBeacon => IsOpen ? _instance._beacon : null;

        /// <summary>Ctrl+click on a trophy in the player's inventory while the panel is open.</summary>
        internal static void OnInventoryCtrlClick(ItemDrop.ItemData item)
        {
            if (_instance == null || _instance._beacon == null || Player.m_localPlayer == null) return;
            var player = Player.m_localPlayer;
            if (item?.m_dropPrefab == null || DiscountRules.KindOf(item.m_dropPrefab.name) == TrophyKind.None)
            {
                player.Message(MessageHud.MessageType.Center, "$xai_beacon_not_a_trophy");
                return;
            }
            if (_instance._beacon.TryInsertTrophy(player, item))
            {
                _instance._picking = TrophyKind.None;
                _instance.Refresh();
            }
        }

        /// <summary>
        /// Valheim's ButtonSfx, which Jötunn puts on its buttons, plays its select sound when the EventSystem selects
        /// the button (mouse down) and its click sound on click (mouse up), so every mouse click sounded twice. Drop
        /// the select sound on our buttons and keep the click.
        /// </summary>
        private static Button Quiet(Button button)
        {
            var sfx = button != null ? button.GetComponent<ButtonSfx>() : null;
            if (sfx != null) sfx.m_selectSfxPrefab = null;
            return button;
        }

        /// <summary>Clickable rows are plain Buttons with no sound: give them our buttons' click sound only.</summary>
        private void AddClickSound(GameObject go)
        {
            var source = _closeButton != null ? _closeButton.GetComponent<ButtonSfx>() : null;
            if (source == null || go.GetComponent<ButtonSfx>() != null) return;
            go.AddComponent<ButtonSfx>().m_sfxPrefab = source.m_sfxPrefab;
        }

        private static bool CtrlHeld() => ZInput.GetKey(KeyCode.LeftControl) || ZInput.GetKey(KeyCode.RightControl);

        // ---- Construction ----

        private void EnsureBuilt()
        {
            if (_panel != null) return;

            var gui = GUIManager.Instance;
            var font = gui.AveriaSerifBold;
            var orange = GUIManager.Instance.ValheimOrange;
            var beige = GUIManager.Instance.ValheimBeige;
            var center = new Vector2(0.5f, 0.5f);
            var topLeft = new Vector2(0f, 1f);
            var bottomLeft = new Vector2(0f, 0f);
            var bottomRight = new Vector2(1f, 0f);

            _panel = gui.CreateWoodpanel(GUIManager.CustomGUIFront.transform, center, center, Vector2.zero, PanelW, PanelH, true);
            _panel.name = "BuildBeaconPanel";
            _panel.SetActive(false);

            float innerW = PanelW - Margin * 2f;
            float y = -Margin;
            var topCenter = new Vector2(0.5f, 1f);

            // Header block: title and stats centered across the panel, hint left-aligned with the list.
            _title = gui.CreateText("", _panel.transform, topCenter, topCenter, new Vector2(0f, y), font, TitleSize, orange, true, Color.black, innerW, 34f, false).GetComponent<Text>();
            Place(_title, topCenter, new Vector2(0f, y));
            _title.alignment = TextAnchor.UpperCenter;
            y -= 38f;

            _subtitle = gui.CreateText("", _panel.transform, topCenter, topCenter, new Vector2(0f, y), font, SmallSize, beige, true, Color.black, innerW, 22f, false).GetComponent<Text>();
            Place(_subtitle, topCenter, new Vector2(0f, y));
            _subtitle.alignment = TextAnchor.UpperCenter;
            y -= 32f;

            // Tabs, centred under the stats line.
            _tabTrophies = Quiet(gui.CreateButton(L("$xai_beacon_tab_trophies"), _panel.transform, topCenter, topCenter, new Vector2(-TabW / 2f - 4f, y - TabH / 2f), TabW, TabH).GetComponent<Button>());
            _tabTrophies.onClick.AddListener(() => SetTab(Tab.Trophies));
            _tabDiscounts = Quiet(gui.CreateButton(L("$xai_beacon_tab_discounts"), _panel.transform, topCenter, topCenter, new Vector2(TabW / 2f + 4f, y - TabH / 2f), TabW, TabH).GetComponent<Button>());
            _tabDiscounts.onClick.AddListener(() => SetTab(Tab.Discounts));
            y -= TabH + 8f;

            // Discounts tab only: filter field and clear button on the row under the tabs (other tabs give it to the list).
            const float filterH = 34f, clearW = 40f;
            _filter = gui.CreateInputField(_panel.transform, topLeft, topLeft, new Vector2(Margin, y), InputField.ContentType.Standard,
                L("$xai_beacon_filter_placeholder"), BodySize, innerW - clearW - 8f, filterH).GetComponent<InputField>();
            Place(_filter, topLeft, new Vector2(Margin, y - 2f));
            _filter.onValueChanged.AddListener(v =>
            {
                _filterText = v ?? "";
                if (_tab == Tab.Discounts) Refresh();
            });
            var topRight = new Vector2(1f, 1f);
            _clearFilter = Quiet(gui.CreateButton("X", _panel.transform, topRight, topRight, new Vector2(-Margin, y), clearW, filterH).GetComponent<Button>());
            Place(_clearFilter, topRight, new Vector2(-Margin, y - 2f));
            _clearFilter.onClick.AddListener(() => _filter.text = ""); // onValueChanged refreshes
            y -= 44f;

            var handleColors = new ColorBlock
            {
                normalColor = new Color(0.8f, 0.7f, 0.5f, 0.8f),
                highlightedColor = new Color(0.9f, 0.8f, 0.6f, 1f),
                pressedColor = new Color(0.7f, 0.6f, 0.4f, 1f),
                selectedColor = new Color(0.9f, 0.8f, 0.6f, 1f),
                disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = 0.1f,
            };
            var scrollGo = gui.CreateScrollView(_panel.transform, false, true, 8f, 4f, handleColors, new Color(0f, 0f, 0f, 0.35f), innerW, ListH);
            var scrollRect = scrollGo.GetComponent<RectTransform>();
            scrollRect.anchorMin = topLeft;
            scrollRect.anchorMax = topLeft;
            scrollRect.pivot = topLeft;
            scrollRect.anchoredPosition = new Vector2(Margin, y);
            _scrollRt = scrollRect;
            _listTopWithFilter = y;
            _listTopNoFilter = y + 44f; // the Discounts tab's filter row; other tabs let the list use it
            y -= ListH + 10f;

            _scroll = scrollGo.GetComponentInChildren<ScrollRect>();
            FollowWrapperHeight(scrollRect, (RectTransform)_scroll.transform);
            _content = _scroll.content;
            var layout = _content.GetComponent<VerticalLayoutGroup>() ?? _content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = RowGap;
            layout.padding = new RectOffset(4, 4, 4, 4);
            if (_content.GetComponent<ContentSizeFitter>() == null)
            {
                var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            _closeButton = Quiet(gui.CreateButton(L("$xai_beacon_close"), _panel.transform, bottomRight, bottomRight, new Vector2(-Margin - 60f, Margin + 20f), 120f, 40f).GetComponent<Button>());
            _closeButton.onClick.AddListener(Close);

            _backButton = Quiet(gui.CreateButton(L("$xai_beacon_back"), _panel.transform, bottomLeft, bottomLeft, new Vector2(Margin + 60f, Margin + 20f), 120f, 40f).GetComponent<Button>());
            _backButton.onClick.AddListener(() => { _picking = TrophyKind.None; Refresh(); });

            // The tab's help text: bottom left, on the row of the Close button (and right of Back while it shows).
            var bottomLeftPivot = new Vector2(0f, 0.5f);
            _hint = gui.CreateText("", _panel.transform, bottomLeft, bottomLeft, new Vector2(Margin, Margin + 20f), font, SmallSize, beige, true, Color.black,
                innerW - BottomButtonW - BottomGap, FooterH, false).GetComponent<Text>();
            var hintRt = _hint.GetComponent<RectTransform>();
            hintRt.anchorMin = hintRt.anchorMax = bottomLeft;
            hintRt.pivot = bottomLeftPivot;
            _hint.alignment = TextAnchor.MiddleLeft;
            _hint.fontStyle = FontStyle.Italic;
            _hint.resizeTextForBestFit = true;
            _hint.resizeTextMinSize = 11;
            _hint.resizeTextMaxSize = SmallSize;

            _iconFrame = gui.GetSprite("item_background");
            BuildHoverBox();
        }

        // ---- Content ----

        private void Refresh()
        {
            if (_beacon == null) return;
            HideHover(null); // the rows it pointed at are about to be rebuilt

            _title.text = L(_beacon.PieceNameKey);
            _subtitle.text =
                $"{L("$xai_beacon_level")} {_beacon.Level}/{_beacon.MaxLevel}   ·   " +
                $"{L("$xai_beacon_radius")} {_beacon.Radius:0}m   ·   " +
                $"{L("$xai_beacon_boss_section")} {_beacon.BossCount}   ·   " +
                $"{L("$xai_beacon_mob_section")} {L(_beacon.MobSlotsText)}";

            _tabTrophies.interactable = _tab != Tab.Trophies;
            _tabDiscounts.interactable = _tab != Tab.Discounts;
            bool discounts = _tab == Tab.Discounts;
            _filter.gameObject.SetActive(discounts);
            _clearFilter.gameObject.SetActive(discounts);
            _clearFilter.interactable = _filterText.Length > 0;
            bool back = _tab == Tab.Trophies && _picking != TrophyKind.None;
            _backButton.gameObject.SetActive(back);

            // The list takes the filter row's place where there is no filter.
            float top = discounts ? _listTopWithFilter : _listTopNoFilter;
            _scrollRt.anchoredPosition = new Vector2(Margin, top);
            // Down to just above the footer (the help text and the Back/Close buttons), whichever tab.
            float bottomReserve = Margin + 20f + FooterH / 2f + 6f;
            _scrollRt.sizeDelta = new Vector2(_scrollRt.sizeDelta.x, PanelH + top - bottomReserve);

            // Help text beside Close, and right of Back while Back shows.
            float innerW = PanelW - Margin * 2f;
            float left = back ? BottomButtonW + BottomGap : 0f;
            var hintRt = _hint.GetComponent<RectTransform>();
            hintRt.anchoredPosition = new Vector2(Margin + left, Margin + 20f);
            hintRt.sizeDelta = new Vector2(innerW - BottomButtonW - BottomGap - left, FooterH);
            ClearRows();

            if (_tab == Tab.Discounts) BuildDiscounts();
            else if (_picking != TrophyKind.None) BuildPicker(_picking);
            else BuildSlots();

            _scroll.verticalNormalizedPosition = 1f;
        }

        private void BuildSlots()
        {
            _hint.text = L("$xai_beacon_hint_slots");

            if (_beacon.IsGreat)
            {
                // The Great Beacon's alcoves hold bosses (removable here, added with the boss picker), then any bosses on
                // linked holders (read-only; each boss counts once across both).
                AddSectionHeader($"{L("$xai_beacon_boss_section")}  {_beacon.OwnBossCount}/{_beacon.OwnBossSlots}");
                foreach (var prefab in _beacon.SlotsOf(TrophyKind.Boss)) AddTrophyRow(prefab);
                if (_beacon.OwnBossCount < _beacon.OwnBossSlots)
                    AddRow(null, L("$xai_beacon_empty_boss_slot"), L("$xai_beacon_click_to_add_boss"), null,
                        () => { _picking = TrophyKind.Boss; Refresh(); }, true);
                foreach (var (prefab, holder) in _beacon.BossEntries)
                    if (holder != null) AddTrophyRow(prefab, L("$xai_beacon_on_holder"));
            }
            else
            {
                // Bosses sit on holders; the list shows what counts. Bosses slotted in the beacon before holders existed
                // (legacy) keep counting and can be removed here, but not added.
                AddSectionHeader($"{L("$xai_beacon_boss_section")}  {_beacon.BossCount}"); // each boss counts once, so the count is the whole story
                foreach (var (prefab, holder) in _beacon.BossEntries) AddTrophyRow(prefab, holder != null ? L("$xai_beacon_on_holder") : null);
                if (_beacon.BossCount == 0)
                    AddRow(null, L("$xai_beacon_no_bosses"), L("$xai_beacon_no_bosses_hint"), null, null, true);
            }

            // Creature trophies in the beacon itself (removable here), counted against its own slots. The empty slot row
            // only offers those; with a boss taken away the beacon can hold more than its slots, and then it has none.
            // The Great Beacon has no creature slots of its own: the section shows only if it somehow holds some.
            if (_beacon.MobSlots > 0 || _beacon.MobCount > 0)
            {
                AddSectionHeader($"{L("$xai_beacon_mob_section")}  {_beacon.MobCount}/{_beacon.MobSlots}");
                foreach (var prefab in _beacon.SlotsOf(TrophyKind.Mob)) AddTrophyRow(prefab);
                if (_beacon.MobCount < _beacon.MobSlots)
                    AddRow(null, L("$xai_beacon_empty_mob_slot"), L("$xai_beacon_click_to_add_mob"), null,
                        () => { _picking = TrophyKind.Mob; Refresh(); }, true);
            }

            // Creature trophies on linked racks, counted against the racks' slots: read-only, placed and taken at the rack.
            if (_beacon.RackCount > 0)
            {
                AddSectionHeader($"{L("$xai_beacon_racks_section")}  {_beacon.RackTrophyCount}/{_beacon.RackSlots}");
                foreach (var t in _beacon.RackTrophies)
                    AddTrophyRow(t.prefab, t.counted ? L("$xai_beacon_on_rack") : $"{L("$xai_beacon_on_rack")}, {L("$xai_beacon_not_counted")}");
            }
        }

        /// <summary>A trophy row. <paramref name="placedOn"/>: where the trophy sits when not in the beacon itself ("on a
        /// holder", "on a rack"); such trophies are removed there, not here (no Remove button, no Ctrl+click
        /// removal), and the note shows after the name.</summary>
        private void AddTrophyRow(string prefab, string placedOn = null)
        {
            bool onHolder = placedOn != null;
            var captured = prefab;
            System.Action remove = onHolder ? (System.Action)null : () => { if (_beacon.TryRemoveTrophy(Player.m_localPlayer, captured)) Refresh(); };
            var displayName = DiscountRules.ItemDisplayName(prefab) ?? prefab;
            var row = AddRow(
                IconFor(prefab),
                onHolder ? $"{displayName}  <size={SmallSize}><color=#c8b89a>({placedOn})</color></size>" : displayName,
                DiscountRules.DescribeTrophy(prefab),
                onHolder ? null : L("$xai_beacon_remove"),
                remove);

            // Ctrl+click anywhere on the row removes it, mirroring Ctrl+click in the inventory. A plain click shows
            // what the trophy does: the Discounts tab, filtered to its name (the filter matches source trophies).
            var trophyName = DiscountRules.ItemDisplayName(prefab) ?? prefab;
            var bg = row.GetComponent<Image>();
            var click = row.AddComponent<Button>();
            AddClickSound(row);
            click.transition = Selectable.Transition.None;
            click.targetGraphic = bg;
            click.onClick.AddListener(() =>
            {
                if (CtrlHeld() && remove != null) remove();
                else ShowDiscountsFor(trophyName);
            });

            // Hover: the empty-slot square behind the icon; faded red while Ctrl is held (Ctrl+click removes).
            var icon = row.transform.Find("icon");
            if (icon != null)
            {
                var square = new GameObject("iconHover", typeof(RectTransform), typeof(Image));
                square.transform.SetParent(row.transform, false);
                square.transform.SetSiblingIndex(icon.GetSiblingIndex()); // drawn just before, so behind, the icon
                var src = (RectTransform)icon;
                var rt = (RectTransform)square.transform;
                rt.anchorMin = src.anchorMin;
                rt.anchorMax = src.anchorMax;
                rt.pivot = src.pivot;
                rt.sizeDelta = src.sizeDelta;
                rt.anchoredPosition = src.anchoredPosition;
                var img = square.GetComponent<Image>();
                img.sprite = _iconFrame;
                img.raycastTarget = false;
                img.enabled = false;
                var hover = row.AddComponent<RowHover>();
                hover.Square = img;
                hover.CtrlRemoves = remove != null;
            }
        }

        /// <summary>Shows <see cref="Square"/> while the pointer is over the row: grey as on empty slots, faded red
        /// while Ctrl is held.</summary>
        private sealed class RowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            private static readonly Color Grey = new Color(1f, 1f, 1f, 0.2f);
            private static readonly Color Red = new Color(1f, 0.6f, 0.6f, 0.45f);
            public Image Square;
            public bool CtrlRemoves = true; // red while Ctrl is held only where Ctrl+click removes
            private bool _over;

            public void OnPointerEnter(PointerEventData e) { _over = true; Square.enabled = true; Tint(); }
            public void OnPointerExit(PointerEventData e) { _over = false; Square.enabled = false; }
            private void OnDisable() { _over = false; if (Square != null) Square.enabled = false; }
            private void Update() { if (_over) Tint(); }
            private void Tint() => Square.color = CtrlRemoves && CtrlHeld() ? Red : Grey;
        }

        private void BuildPicker(TrophyKind kind)
        {
            _hint.text = L(kind == TrophyKind.Boss ? "$xai_beacon_hint_picker_boss" : "$xai_beacon_hint_picker_mob");

            var inventory = Player.m_localPlayer.GetInventory();
            var candidates = inventory.GetAllItems()
                .Where(i => i.m_dropPrefab != null && DiscountRules.KindOf(i.m_dropPrefab.name) == kind)
                .GroupBy(i => i.m_dropPrefab.name)
                .Select(g => (prefab: g.Key, count: g.Sum(i => i.m_stack)))
                .OrderBy(c => DiscountRules.ItemDisplayName(c.prefab) ?? c.prefab)
                .ToList();

            if (candidates.Count == 0)
            {
                AddRow(null, L(kind == TrophyKind.Boss ? "$xai_beacon_no_boss_trophies" : "$xai_beacon_no_mob_trophies"), "", null, null, true);
                return;
            }

            foreach (var c in candidates)
            {
                var captured = c.prefab;
                var blocked = _beacon.InsertBlockedReason(c.prefab);
                AddRow(
                    IconFor(c.prefab),
                    $"{DiscountRules.ItemDisplayName(c.prefab) ?? c.prefab}  x{c.count}",
                    blocked != null ? L(blocked) : DiscountRules.DescribeTrophy(c.prefab),
                    blocked != null ? null : L("$xai_beacon_add"),
                    () => { if (_beacon.TryInsertTrophy(Player.m_localPlayer, captured)) { _picking = TrophyKind.None; Refresh(); } });
            }
        }

        private void ShowDiscountsFor(string filter)
        {
            _filterText = filter;
            _filter.SetTextWithoutNotify(filter);
            SetTab(Tab.Discounts);
        }

        private void SetTab(Tab tab)
        {
            if (_tab == Tab.Discounts && tab != Tab.Discounts)
            {
                // Leaving the Discounts tab clears its filter.
                _filterText = "";
                _filter.SetTextWithoutNotify("");
            }
            _tab = tab;
            _picking = TrophyKind.None;
            Refresh();
        }

        private void BuildDiscounts()
        {
            _hint.text = L("$xai_beacon_levels_note");
            var rows = DiscountRules.Breakdown(_beacon.Trophies);
            if (rows.Count == 0)
            {
                AddRow(null, L("$xai_beacon_no_discounts"), L("$xai_beacon_no_discounts_hint"), null, null, true);
                return;
            }

            var filter = _filterText.Trim();
            var shown = filter.Length == 0 ? rows : rows.Where(d => MatchesFilter(d, filter)).ToList();
            if (shown.Count == 0)
            {
                AddRow(null, $"{L("$xai_beacon_filter_none")} \"{filter}\"", "", null, null, true);
                return;
            }
            // One trophy column for the whole list, as wide as its widest row, so the level bars line up.
            int trophyColumns = shown.Max(r => r.Sources.Count);
            foreach (var d in shown) AddDiscountRow(d, trophyColumns);
        }

        /// <summary>Level bar segments no set of creature trophies can reach for the material.</summary>
        private static readonly Color Unreachable = new Color(0.5f, 0.5f, 0.5f, 0.14f);

        /// <summary>Segment colours from level 1 to the top level: orange, yellow, green, spread over however many levels
        /// there are (LevelColor), so the top is always green. Above them sits the boss tier, in BossColor.</summary>
        private static readonly Color[] LevelColors =
        {
            new Color(0.95f, 0.52f, 0.12f),
            new Color(0.96f, 0.80f, 0.18f),
            new Color(0.32f, 0.78f, 0.26f),
        };

        /// <summary>The boss tier: every segment of the bar in this colour (AddBossBar).</summary>
        private static readonly Color BossColor = new Color(0.28f, 0.58f, 1.00f);

        /// <summary>The colour of a level (1 to top) on the LevelColors scale.</summary>
        private static Color LevelColor(int level, int top)
        {
            int last = LevelColors.Length - 1;
            if (top <= 1) return LevelColors[last];
            float t = Mathf.Clamp01((level - 1) / (float)(top - 1));
            return LevelColors[Mathf.RoundToInt(t * last)];
        }

        /// <summary>
        /// A creature discount's level as a segmented bar, right-aligned at <paramref name="pos"/>: one segment per level
        /// up to the top level, filled left to right up to the row's level, every segment in the level's colour (empty
        /// ones faint). Hovering shows the level and what it takes off, with vanilla's tooltip.
        /// </summary>
        private void AddLevelBar(Transform row, MaterialDiscount d, Vector2 pos, float width)
        {
            // Levels past the most the creature rules can give this material (MaxMobLevel, from the rules alone) are grey.
            int reachable = DiscountRules.MaxMobLevel(d.Item);
            int top = DiscountRules.TopLevel;
            int shown = Mathf.Clamp(d.Level, 0, top);
            var color = LevelColor(Mathf.Max(shown, 1), top);
            var midRight = new Vector2(1f, 0.5f);

            var bar = new GameObject("levelBar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(row, false);
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = midRight;
            rt.anchorMax = midRight;
            rt.pivot = midRight;
            rt.sizeDelta = new Vector2(width, SegmentH + 8f); // a little taller than the segments, for an easier hover
            rt.anchoredPosition = pos;
            var hit = bar.GetComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f); // invisible, but catches the pointer for the tooltip

            AddSegments(bar.transform, width, top, i =>
                i < shown ? color
                : i < reachable ? new Color(color.r, color.g, color.b, 0.22f)
                : Unreachable); // no set of creature trophies reaches this level

            var hover = row.gameObject.AddComponent<HoverTarget>();
            hover.Anchor = rt;
            hover.Topic = d.Name;
            hover.Trophies = TrophyIconsFor(d);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var pct = d.Percent.ToString("0.#", inv);
            // A discount of p% makes each item go 1 / (1 - p) times as far: 50% is 2x, 90% is 10x.
            var factor = d.Percent >= 100f ? "∞" : (100f / (100f - d.Percent)).ToString("0.##", inv);
            // "Level 1 of 2": out of the most this material can reach (its segments not greyed), not the top level.
            int most = Mathf.Max(reachable > 0 ? reachable : top, shown);
            hover.Text = string.Format(L("$xai_beacon_level_tooltip"), shown, most, pct)
                         + "\n" + string.Format(L("$xai_beacon_level_multiplier"), factor)
                         + (d.Level > top ? "\n" + string.Format(L("$xai_beacon_level_over"), d.Level, top) : "");
        }

        private const float SegmentH = 16f, SegmentGap = 3f;

        /// <summary>One segment per level under <paramref name="bar"/>, left to right across <paramref name="width"/>
        /// with a small gap between them, each in <paramref name="colorAt"/>(its index).</summary>
        private static void AddSegments(Transform bar, float width, int count, System.Func<int, Color> colorAt)
        {
            var midLeft = new Vector2(0f, 0.5f);
            float segW = (width - SegmentGap * (count - 1)) / count;
            for (int i = 0; i < count; i++)
            {
                var seg = new GameObject("segment", typeof(RectTransform), typeof(Image));
                seg.transform.SetParent(bar, false);
                var srt = seg.GetComponent<RectTransform>();
                srt.anchorMin = midLeft;
                srt.anchorMax = midLeft;
                srt.pivot = midLeft;
                srt.sizeDelta = new Vector2(segW, SegmentH);
                srt.anchoredPosition = new Vector2(i * (segW + SegmentGap), 0f);
                var img = seg.GetComponent<Image>();
                img.color = colorAt(i);
                img.raycastTarget = false;
            }
        }

        /// <summary>
        /// A boss's material: the tier above the creature levels, drawn like the level bar with every segment filled in
        /// blue, right-aligned at <paramref name="pos"/>. Hovering shows the tier ("Level 4: 95% off") and the
        /// multiplier, or that it is free.
        /// </summary>
        private void AddBossBar(Transform row, MaterialDiscount d, Vector2 pos, float width)
        {
            var midRight = new Vector2(1f, 0.5f);

            var bar = new GameObject("bossBar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(row, false);
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = midRight;
            rt.anchorMax = midRight;
            rt.pivot = midRight;
            rt.sizeDelta = new Vector2(width, SegmentH + 8f); // as the level bar: a little taller, for an easier hover
            rt.anchoredPosition = pos;
            bar.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // invisible, but catches the pointer
            AddSegments(bar.transform, width, DiscountRules.TopLevel, _ => BossColor);

            var hover = row.gameObject.AddComponent<HoverTarget>();
            hover.Anchor = rt;
            hover.Topic = d.Name;
            hover.Trophies = TrophyIconsFor(d);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            hover.Text = d.Free
                ? L("$xai_beacon_free_tooltip")
                : string.Format(L("$xai_beacon_boss_tooltip"), DiscountRules.TopLevel + 1, d.Percent.ToString("0.#", inv))
                  + "\n" + string.Format(L("$xai_beacon_level_multiplier"), DiscountRules.MultiplierText(d.Percent));
        }

        // ---- Hover text for the level bars ----
        // Vanilla's UITooltip did not show on this panel, so the panel draws its own: one dark box with a text,
        // placed above the hovered bar's right end and drawn over everything else in the panel.

        /// <summary>Put on a UI element to show <see cref="Topic"/> and <see cref="Text"/> in the hover box while the
        /// pointer is over it, with <see cref="Trophies"/> as a row of icons at the bottom.</summary>
        private sealed class HoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public string Topic, Text;
            /// <summary>Trophies that can discount the material: in the beacon's network (full) or not (greyed), and
            /// whether each is a boss trophy (starred). None when empty.</summary>
            public List<HoverTrophy> Trophies = new List<HoverTrophy>();
            public RectTransform Anchor; // the box sits above this; the target itself when null
            public void OnPointerEnter(PointerEventData eventData) { if (_instance != null) _instance.ShowHover(this); }
            public void OnPointerExit(PointerEventData eventData) { if (_instance != null) _instance.HideHover(this); }
            private void OnDisable() { if (_instance != null) _instance.HideHover(this); }
        }

        /// <summary>The trophies that can discount a row's material (DiscountRules.ContributorsFor, from the rules), each
        /// marked as in this beacon's network when the beacon counts it (its own slots, a holder or a rack).</summary>
        private List<HoverTrophy> TrophyIconsFor(MaterialDiscount d)
        {
            var list = new List<HoverTrophy>();
            foreach (var (prefab, boss) in DiscountRules.ContributorsFor(d.Item))
            {
                var icon = IconFor(prefab);
                if (icon == null) continue; // a rule naming a trophy the game does not have
                var key = DiscountRules.TrophyKey(prefab);
                bool inNetwork = _beacon.Trophies.Any(kv => kv.Value > 0 && DiscountRules.TrophyKey(kv.Key) == key);
                list.Add(new HoverTrophy { Icon = icon, Name = ShortTrophyName(prefab), InNetwork = inNetwork, Boss = boss });
            }
            return list;
        }

        private struct HoverTrophy
        {
            public Sprite Icon;
            public string Name;
            public bool InNetwork, Boss;
        }

        /// <summary>The trophy's display name without the word "trophy" ("Greydwarf trophy" → "Greydwarf").</summary>
        private static string ShortTrophyName(string prefab)
        {
            var name = DiscountRules.ItemDisplayName(prefab) ?? prefab;
            name = System.Text.RegularExpressions.Regex.Replace(name, "trophy", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();
        }

        private void BuildHoverBox()
        {
            var gui = GUIManager.Instance;
            var go = new GameObject("hoverBox", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_panel.transform, false);
            _hoverBox = go.GetComponent<RectTransform>();
            _hoverBox.pivot = new Vector2(1f, 0f);
            _hoverBox.sizeDelta = new Vector2(HoverW, 40f);
            var bg = go.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.88f);
            bg.raycastTarget = false; // never take the pointer from the bar under it

            _hoverText = gui.CreateText("", go.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(HoverPad, -6f),
                gui.AveriaSerifBold, SmallSize, gui.ValheimBeige, true, Color.black, HoverW - 2f * HoverPad, 40f, false).GetComponent<Text>();
            Place(_hoverText, new Vector2(0f, 1f), new Vector2(HoverPad, -6f));
            _hoverText.alignment = TextAnchor.UpperLeft;
            _hoverText.supportRichText = true;
            _hoverText.raycastTarget = false;

            // The trophy icons, along the bottom; filled on each show.
            var row = new GameObject("trophies", typeof(RectTransform));
            row.transform.SetParent(go.transform, false);
            _hoverTrophies = row.GetComponent<RectTransform>();
            _hoverTrophies.anchorMin = _hoverTrophies.anchorMax = _hoverTrophies.pivot = new Vector2(0f, 0f);
            _hoverTrophies.anchoredPosition = new Vector2(HoverPad, HoverPad);
            go.SetActive(false);
        }

        private void ShowHover(HoverTarget target)
        {
            if (_hoverBox == null || target == null) return;
            _hovered = target;
            var orange = ColorUtility.ToHtmlStringRGB(GUIManager.Instance.ValheimOrange);
            _hoverText.text = $"<color=#{orange}>{target.Topic}</color>\n{target.Text}";
            var textRt = _hoverText.GetComponent<RectTransform>();
            textRt.sizeDelta = new Vector2(textRt.sizeDelta.x, _hoverText.preferredHeight);

            // Trophy cells: a grid (wrapping) along the bottom, each an icon with its name under it. In-network ones
            // are full colour with beige names; the rest are greyscale, faded, with grey names. Bosses are starred.
            for (int i = _hoverTrophies.childCount - 1; i >= 0; i--) Destroy(_hoverTrophies.GetChild(i).gameObject);
            int perRow = Mathf.Max(1, Mathf.FloorToInt((HoverW - 2f * HoverPad + HoverTrophyGap) / (HoverTrophyCell + HoverTrophyGap)));
            // ShowTrophySources off: no trophies at all, so players find the sources for themselves (the row itself
            // still shows the trophies already counting). Read on each show, so a config change applies at once.
            int count = BuildBeaconPlugin.Cfg.ShowTrophySources.Value ? target.Trophies.Count : 0;
            int rows = (count + perRow - 1) / perRow;

            // Names first: one font size for all, the largest at which every word fits a cell on its own line.
            var labels = new Text[count];
            for (int i = 0; i < count; i++)
                labels[i] = HoverLabel(_hoverTrophies, target.Trophies[i].InNetwork);
            int nameSize = HoverNameMaxSize;
            if (count > 0)
                while (nameSize > HoverNameMinSize && !WordsFit(labels[0], target.Trophies.Select(t => t.Name), nameSize, HoverTrophyCell))
                    nameSize--;
            var rowH = new float[rows];
            for (int i = 0; i < count; i++)
            {
                labels[i].fontSize = nameSize;
                labels[i].text = target.Trophies[i].Name;
                rowH[i / perRow] = Mathf.Max(rowH[i / perRow], HoverTrophySize + 1f + labels[i].preferredHeight);
            }
            float rowsH = rows == 0 ? 0f : rowH.Sum() + (rows - 1) * HoverTrophyGap;
            _hoverTrophies.sizeDelta = new Vector2(HoverW - 2f * HoverPad, rowsH);

            float rowTop = rowsH;
            for (int i = 0; i < count; i++)
            {
                var t = target.Trophies[i];
                int r = i / perRow, c = i % perRow;
                if (c == 0 && r > 0) rowTop -= rowH[r - 1] + HoverTrophyGap;
                float cellX = c * (HoverTrophyCell + HoverTrophyGap);
                var pos = new Vector2(cellX + (HoverTrophyCell - HoverTrophySize) / 2f, rowTop - HoverTrophySize);
                var grey = t.InNetwork ? null : GreySprite(t.Icon);
                // Greyscale icons fade a little; if one could not be made, the colour icon fades further instead.
                var alpha = t.InNetwork ? 1f : grey != null ? 0.6f : 0.3f;
                var img = HoverImage(_hoverTrophies, "trophy", grey ?? t.Icon, pos, HoverTrophySize, new Color(1f, 1f, 1f, alpha));
                img.preserveAspect = true;

                var labelRt = labels[i].GetComponent<RectTransform>();
                labelRt.sizeDelta = new Vector2(HoverTrophyCell, labels[i].preferredHeight);
                labelRt.anchoredPosition = new Vector2(cellX, pos.y - 1f - labels[i].preferredHeight);

                if (!t.Boss) continue;
                // A gold star on the top-right corner, over a dark one a little larger so it reads on any icon.
                var corner = pos + new Vector2(HoverTrophySize - 12f, HoverTrophySize - 12f);
                HoverImage(_hoverTrophies, "starShadow", StarSprite(), corner - new Vector2(1.5f, 1.5f), 15f, new Color(0f, 0f, 0f, 0.85f * alpha));
                HoverImage(_hoverTrophies, "star", StarSprite(), corner, 12f, new Color(1f, 0.82f, 0.3f, alpha));
            }

            float gap = rows > 0 ? HoverPad : 0f;
            _hoverBox.sizeDelta = new Vector2(HoverW, 6f + _hoverText.preferredHeight + gap + rowsH + HoverPad);

            // Above the bar's right end: the bar's top-right corner, in the panel's space.
            var corners = new Vector3[4];
            (target.Anchor != null ? target.Anchor : (RectTransform)target.transform).GetWorldCorners(corners);
            var panelRt = (RectTransform)_panel.transform;
            Vector2 local = panelRt.InverseTransformPoint(corners[2]);
            _hoverBox.anchorMin = _hoverBox.anchorMax = new Vector2(0.5f, 0.5f);
            _hoverBox.anchoredPosition = local + new Vector2(0f, 4f);
            _hoverBox.SetAsLastSibling();
            _hoverBox.gameObject.SetActive(true);
        }

        /// <summary>An image in the hover box, bottom-left anchored at <paramref name="pos"/>, not catching the pointer.</summary>
        private static Image HoverImage(Transform parent, string name, Sprite sprite, Vector2 pos, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A trophy's name label in the hover box: one cell wide, centred, wrapping; beige when the trophy is
        /// in the network, grey when not. Text and size are set by the caller.</summary>
        private static Text HoverLabel(Transform parent, bool inNetwork)
        {
            var gui = GUIManager.Instance;
            var color = inNetwork ? gui.ValheimBeige : new Color(0.5f, 0.5f, 0.5f, 1f);
            var text = gui.CreateText("", parent, Vector2.zero, Vector2.zero, Vector2.zero, gui.AveriaSerifBold,
                HoverNameMaxSize, color, true, Color.black, HoverTrophyCell, 20f, false).GetComponent<Text>();
            Place(text, Vector2.zero, Vector2.zero);
            text.alignment = TextAnchor.UpperCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Whether every word of every name fits <paramref name="width"/> on its own at <paramref name="size"/>,
        /// so wrapping never has to split a word. Measured with <paramref name="probe"/>, whose text and size it leaves
        /// changed.</summary>
        private static bool WordsFit(Text probe, IEnumerable<string> names, int size, float width)
        {
            probe.fontSize = size;
            foreach (var word in names.SelectMany(n => n.Split(' ')))
            {
                probe.text = word;
                if (probe.preferredWidth > width) return false;
            }
            return true;
        }

        private static readonly Dictionary<Sprite, Sprite> s_grey = new Dictionary<Sprite, Sprite>();

        /// <summary>A greyscale copy of an icon, made once and cached; null if it cannot be made. Item icons usually sit
        /// in atlases that are not CPU-readable, so the sprite's part of the texture is drawn into a render texture on
        /// the GPU and read back from there.</summary>
        private static Sprite GreySprite(Sprite icon)
        {
            if (icon == null) return null;
            if (s_grey.TryGetValue(icon, out var cached)) return cached;
            Sprite result = null;
            RenderTexture rt = null;
            var previous = RenderTexture.active;
            try
            {
                var src = icon.texture;
                var r = icon.textureRect;
                int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
                if (src != null && w > 0 && h > 0)
                {
                    rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(src, rt, new Vector2(r.width / src.width, r.height / src.height),
                        new Vector2(r.x / src.width, r.y / src.height));
                    RenderTexture.active = rt;
                    var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    var px = tex.GetPixels32();
                    for (int i = 0; i < px.Length; i++)
                    {
                        var lum = (byte)Mathf.Clamp(Mathf.RoundToInt(0.299f * px[i].r + 0.587f * px[i].g + 0.114f * px[i].b), 0, 255);
                        px[i] = new Color32(lum, lum, lum, px[i].a);
                    }
                    tex.SetPixels32(px);
                    tex.Apply();
                    result = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), icon.pixelsPerUnit);
                }
            }
            catch (System.Exception e)
            {
                BuildBeaconPlugin.Log.LogWarning($"No greyscale icon for \"{icon.name}\" (the colour one is faded instead): {e.Message}");
                result = null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
            s_grey[icon] = result;
            return result;
        }

        private static Sprite s_star;

        /// <summary>A five-pointed star, drawn once into a small texture (the game's fonts may not have a star glyph).</summary>
        private static Sprite StarSprite()
        {
            if (s_star != null) return s_star;
            const int n = 32;
            const float outer = 15.5f, inner = 6.5f;
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float rad = i % 2 == 0 ? outer : inner;
                pts[i] = new Vector2(n / 2f + rad * Mathf.Cos(a), n / 2f + rad * Mathf.Sin(a));
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // 4x4 supersampling for soft edges
                    int hits = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            if (InPolygon(pts, new Vector2(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f))) hits++;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(hits * 255 / 16));
                }
            tex.SetPixels32(px);
            tex.Apply();
            s_star = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            return s_star;
        }

        private static bool InPolygon(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        /// <summary>Hide the box if <paramref name="target"/> is what it shows, or always when null.</summary>
        private void HideHover(HoverTarget target)
        {
            if (_hoverBox == null || (target != null && target != _hovered)) return;
            _hovered = null;
            _hoverBox.gameObject.SetActive(false);
        }

        /// <summary>
        /// A bar under a discounted material's name: how much of the next item the player's rounding savings
        /// (DiscountSavings) hold, filling towards the piece that costs one less.
        /// </summary>
        private void AddSavingsBar(Transform row, ItemDrop item, float x, float width)
        {
            float saved = DiscountSavings.Get(item.name);
            const float barH = 6f;
            var midLeft = new Vector2(0f, 0.5f);
            var back = new GameObject("savingsBar", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(row, false);
            var rt = back.GetComponent<RectTransform>();
            rt.anchorMin = midLeft;
            rt.anchorMax = midLeft;
            rt.pivot = midLeft;
            rt.sizeDelta = new Vector2(width, barH);
            rt.anchoredPosition = new Vector2(x, -RowH / 2f + 10f);
            back.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var fill = new GameObject("fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(back.transform, false);
            var frt = fill.GetComponent<RectTransform>();
            frt.anchorMin = midLeft;
            frt.anchorMax = midLeft;
            frt.pivot = midLeft;
            frt.sizeDelta = new Vector2(width * Mathf.Clamp01(saved), barH);
            frt.anchoredPosition = Vector2.zero;
            fill.GetComponent<Image>().color = GUIManager.Instance.ValheimOrange;
        }

        /// <summary>Case-insensitive substring match on the material name or any responsible trophy's name.</summary>
        private static bool MatchesFilter(MaterialDiscount d, string filter)
        {
            bool Has(string s) => s != null && s.IndexOf(filter, System.StringComparison.CurrentCultureIgnoreCase) >= 0;
            return Has(d.Name) || d.Sources.Any(src => Has(DiscountRules.ItemDisplayName(src.trophy) ?? src.trophy));
        }

        /// <summary>[icon] name ............ boss bar (Free / 95% | 20x) or level bar  [trophy][trophy]</summary>
        private void AddDiscountRow(MaterialDiscount d, int trophyColumns)
        {
            var gui = GUIManager.Instance;
            var font = gui.AveriaSerifBold;
            var beige = gui.ValheimBeige;
            var orange = gui.ValheimOrange;
            var midLeft = new Vector2(0f, 0.5f);
            var midRight = new Vector2(1f, 0.5f);

            var row = new GameObject("discount", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(_content, false);
            row.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.3f);
            var le = row.GetComponent<LayoutElement>();
            le.minHeight = RowH;
            le.preferredHeight = RowH;
            le.flexibleWidth = 1f;

            // Three columns: [icon, name, savings bar] [level bar or Free] [trophies]. The trophy column has the same
            // width on every row (trophyColumns icons), so the middle column starts at the same place on each.
            // Right side first: source trophy icons from the right edge inwards.
            float x = -8f;
            for (int i = d.Sources.Count - 1; i >= 0; i--)
            {
                var (trophy, count) = d.Sources[i];
                var img = AddIcon(row.transform, IconFor(trophy), midRight, new Vector2(x, 0f), SourceIconSize);
                if (count > 1 && !d.Boss)
                {
                    var c = gui.CreateText($"x{count}", img.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), font, 12, beige, true, Color.black, SourceIconSize, 16f, false).GetComponent<Text>();
                    Place(c, new Vector2(1f, 0f), new Vector2(2f, -2f));
                    c.alignment = TextAnchor.LowerRight;
                }
                x -= SourceIconSize + 4f;
            }
            x = -8f - Mathf.Max(trophyColumns, d.Sources.Count) * (SourceIconSize + 4f);

            const float amountW = 150f;
            if (d.Boss) AddBossBar(row.transform, d, new Vector2(x - 8f, 0f), amountW - 20f);
            else AddLevelBar(row.transform, d, new Vector2(x - 8f, 0f), amountW - 20f);

            // Left side: material icon and name, taking what the right side leaves.
            AddIcon(row.transform, d.Item != null ? SafeIcon(d.Item) : null, midLeft, new Vector2(8f, 0f), IconSize);
            float nameX = 8f + IconSize + 12f;
            float nameW = (PanelW - Margin * 2f) - nameX + (x - 8f) - amountW - 12f;
            var name = gui.CreateText(d.Name, row.transform, midLeft, midLeft, new Vector2(nameX, 0f), font, BodySize, beige, true, Color.black, nameW, RowH, false).GetComponent<Text>();
            name.GetComponent<RectTransform>().pivot = midLeft;
            name.alignment = TextAnchor.MiddleLeft;

            if (!d.Free && d.Percent > 0f && d.Item != null) AddSavingsBar(row.transform, d.Item, nameX, Mathf.Min(160f, nameW));
        }

        /// <summary>An icon anchored and pivoted at <paramref name="anchor"/>; an empty frame when there is no sprite.</summary>
        private Image AddIcon(Transform parent, Sprite sprite, Vector2 anchor, Vector2 pos, float size)
        {
            var go = new GameObject("icon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            if (sprite != null) { img.sprite = sprite; img.color = Color.white; }
            else { img.sprite = _iconFrame; img.color = new Color(1f, 1f, 1f, 0.2f); }
            img.preserveAspect = true;
            return img;
        }

        private void ClearRows()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);
        }

        private void AddSectionHeader(string text)
        {
            var gui = GUIManager.Instance;
            var row = new GameObject("header", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(_content, false);
            var le = row.GetComponent<LayoutElement>();
            le.minHeight = HeaderRowH;
            le.preferredHeight = HeaderRowH;
            le.flexibleWidth = 1f;

            var midLeft = new Vector2(0f, 0.5f);
            var t = gui.CreateText(text, row.transform, midLeft, midLeft, new Vector2(8f, 0f), gui.AveriaSerifBold, BodySize, GUIManager.Instance.ValheimOrange, true, Color.black, PanelW - Margin * 2f - 16f, HeaderRowH, false).GetComponent<Text>();
            t.GetComponent<RectTransform>().pivot = midLeft;
            t.alignment = TextAnchor.MiddleLeft;
        }

        /// <summary>
        /// One list row: [icon] name ......... effect [button]. When <paramref name="wholeRowClick"/> is set the
        /// row itself is the button (used for empty slots) and no trailing button is created.
        /// </summary>
        private GameObject AddRow(Sprite icon, string name, string effect, string buttonLabel, System.Action onClick, bool wholeRowClick = false)
        {
            var gui = GUIManager.Instance;
            var font = gui.AveriaSerifBold;
            var beige = GUIManager.Instance.ValheimBeige;
            var orange = GUIManager.Instance.ValheimOrange;
            var midLeft = new Vector2(0f, 0.5f);
            var midRight = new Vector2(1f, 0.5f);

            var row = new GameObject("row", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(_content, false);
            var bg = row.GetComponent<Image>();
            bg.color = wholeRowClick ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.3f);
            var le = row.GetComponent<LayoutElement>();
            le.minHeight = RowH;
            le.preferredHeight = RowH;
            le.flexibleWidth = 1f;

            // Icon (or an empty frame for the empty slot).
            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(row.transform, false);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = midLeft;
            iconRt.anchorMax = midLeft;
            iconRt.pivot = midLeft;
            iconRt.sizeDelta = new Vector2(IconSize, IconSize);
            iconRt.anchoredPosition = new Vector2(8f, 0f); // pivot is middle-left, so y=0 is vertically centered in the row
            var iconImg = iconGo.GetComponent<Image>();
            if (icon != null) { iconImg.sprite = icon; iconImg.color = Color.white; }
            else { iconImg.sprite = _iconFrame; iconImg.color = new Color(1f, 1f, 1f, 0.2f); }
            iconImg.preserveAspect = true;

            float buttonW = wholeRowClick || buttonLabel == null ? 0f : 96f;
            float nameX = 8f + IconSize + 12f;
            float effectW = 250f;
            float nameW = (PanelW - Margin * 2f) - nameX - effectW - buttonW - 24f;

            var nameText = gui.CreateText(name, row.transform, midLeft, midLeft, new Vector2(nameX, 0f), font, BodySize, wholeRowClick ? orange : beige, true, Color.black, nameW, RowH, false).GetComponent<Text>();
            var nameRt = nameText.GetComponent<RectTransform>();
            nameRt.pivot = midLeft;
            nameText.alignment = TextAnchor.MiddleLeft;

            float effectGap = buttonW > 0f ? buttonW + 24f : 12f;
            var effectText = gui.CreateText(effect, row.transform, midRight, midRight, new Vector2(-effectGap, 0f), font, SmallSize, beige, true, Color.black, effectW, RowH, false).GetComponent<Text>();
            var effectRt = effectText.GetComponent<RectTransform>();
            effectRt.pivot = midRight;
            effectText.alignment = TextAnchor.MiddleRight;
            effectText.resizeTextForBestFit = true;
            effectText.resizeTextMinSize = 11;
            effectText.resizeTextMaxSize = SmallSize;

            if (wholeRowClick)
            {
                if (onClick != null)
                {
                    var btn = row.AddComponent<Button>();
                    AddClickSound(row);
                    btn.targetGraphic = bg;
                    var colors = btn.colors;
                    colors.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
                    btn.colors = colors;
                    btn.onClick.AddListener(() => onClick());
                }
            }
            else if (buttonLabel != null)
            {
                var btn = Quiet(gui.CreateButton(buttonLabel, row.transform, midRight, midRight, new Vector2(-8f - buttonW / 2f, 0f), buttonW, 32f).GetComponent<Button>());
                btn.onClick.AddListener(() => onClick?.Invoke());
            }
            return row;
        }

        /// <summary>
        /// Jötunn's CreateScrollView wraps the "Scroll View" (ScrollRect) in a container and gives it, its viewport and
        /// its scrollbar fixed sizes. Stretch the scroll view over the container, and the viewport and vertical scrollbar
        /// over the scroll view's height where they are not stretched already, so resizing the container (Refresh does,
        /// per tab) resizes the list.
        /// </summary>
        private static void FollowWrapperHeight(RectTransform wrapper, RectTransform scrollView)
        {
            if (scrollView != wrapper)
            {
                scrollView.anchorMin = Vector2.zero;
                scrollView.anchorMax = Vector2.one;
                scrollView.pivot = new Vector2(0.5f, 0.5f);
                scrollView.offsetMin = Vector2.zero;
                scrollView.offsetMax = Vector2.zero;
            }
            var sr = scrollView.GetComponent<ScrollRect>();
            foreach (var rt in new[] { sr != null ? sr.viewport : null, sr != null && sr.verticalScrollbar != null ? (RectTransform)sr.verticalScrollbar.transform : null })
            {
                if (rt == null || !Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y)) continue;
                // Keep the horizontal layout; stretch vertically over the parent, flush top and bottom.
                rt.anchorMin = new Vector2(rt.anchorMin.x, 0f);
                rt.anchorMax = new Vector2(rt.anchorMax.x, 1f);
                rt.offsetMin = new Vector2(rt.offsetMin.x, 0f);
                rt.offsetMax = new Vector2(rt.offsetMax.x, 0f);
            }
        }

        /// <summary>
        /// Jötunn's CreateText leaves the default center pivot, so an anchored position at a panel corner puts the
        /// text's middle on that corner. Pin the pivot to the anchor so the position is the text's own corner.
        /// </summary>
        private static void Place(Component c, Vector2 anchorAndPivot, Vector2 anchoredPosition)
        {
            var rt = c.GetComponent<RectTransform>();
            rt.anchorMin = anchorAndPivot;
            rt.anchorMax = anchorAndPivot;
            rt.pivot = anchorAndPivot;
            rt.anchoredPosition = anchoredPosition;
        }

        private static Sprite IconFor(string prefab) => SafeIcon(DiscountRules.ItemDropFor(prefab));

        private static Sprite SafeIcon(ItemDrop drop)
        {
            if (drop == null) return null;
            try { return drop.m_itemData.GetIcon(); }
            catch { return null; }
        }

        private static string L(string key) => Localization.instance.Localize(key);
    }
}
