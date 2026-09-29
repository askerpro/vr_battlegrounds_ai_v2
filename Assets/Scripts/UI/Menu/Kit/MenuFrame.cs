using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VrBattlegrounds.UI.Menu.Kit
{
    /// <summary>
    /// Каркас планшета (T-32) — один на планшет, экраны его не содержат:
    /// <code>
    /// Frame (фон Surface)
    /// ├── Rail          левая колонка: разделы (вкладки) сверху, «‹ Назад» / «✕ Закрыть» — внизу слева
    /// └── ContentZone   справа
    ///     ├── Body      ScrollRect → Viewport (RectMask2D) → Screens (столбец, content скролла)
    ///     └── Actions   полоса главного действия, закреплена внизу справа; только если оно есть
    /// </code>
    /// Экраны лежат в <see cref="Screens"/> и видят только своё содержимое: вылезти за планшет или
    /// поставить «Назад» не туда им негде. Всё переменное прокручивается (лучом и стиком —
    /// <see cref="MenuStickScroll"/>).
    ///
    /// <para>
    /// Каркас строится кодом (<see cref="Build"/>) из токенов <see cref="MenuTheme"/>: редакторский
    /// инструмент запекает его в <c>TabletMenuBase.prefab</c>, а без запечённого — строится при старте.
    /// </para>
    /// </summary>
    public class MenuFrame : MonoBehaviour
    {
        [SerializeField] private RectTransform _rail;
        [SerializeField] private KitButton _back;
        [SerializeField] private RectTransform _tabs;
        [SerializeField] private RectTransform _contentZone;
        [SerializeField] private ScrollRect _body;
        [SerializeField] private RectTransform _viewport;
        [SerializeField] private RectTransform _screens;
        [SerializeField] private RectTransform _actions;
        [SerializeField] private KitButton _primary;
        [SerializeField] private KitButton _secondary;

        private readonly List<(MenuTab tab, KitButton button)> _tabButtons = new List<(MenuTab, KitButton)>();
        private UnityAction _onBack, _onPrimary, _onSecondary;

        public RectTransform Screens => _screens;
        public ScrollRect Body => _body;
        public RectTransform Viewport => _viewport;
        public RectTransform Rail => _rail;
        public RectTransform ContentZone => _contentZone;
        public RectTransform ActionStrip => _actions;
        public KitButton BackButton => _back;
        public KitButton PrimaryButton => _primary;
        public KitButton SecondaryButton => _secondary;
        public bool IsBuilt => _screens != null && _body != null && _back != null && _primary != null;

        private void Awake()
        {
            if (!IsBuilt) Build();
            _back.onClick.AddListener(() => _onBack?.Invoke());
            _primary.onClick.AddListener(() => _onPrimary?.Invoke());
            _secondary.onClick.AddListener(() => _onSecondary?.Invoke());
            ClearActions();
        }

        // ── Колонка ──────────────────────────────────────────────────────

        /// <summary>Слот внизу колонки: на корне раздела — «Закрыть», во вложенном экране — «Назад».</summary>
        public void SetBack(bool atRoot, UnityAction onClick)
        {
            _back.Text = atRoot ? "×  Закрыть" : "Назад";
            _onBack = onClick;
        }

        /// <summary>Кнопки разделов. Перестраивать при каждом открытии не нужно — только при смене набора.</summary>
        public void BuildTabs(IList<MenuTab> tabs, Action<MenuScreenType> onTab)
        {
            MenuKit.Clear(_tabs);
            _tabButtons.Clear();
            if (tabs == null) return;

            foreach (MenuTab tab in tabs)
            {
                MenuScreenType target = tab.Screen;
                KitButton button = MenuKit.Button(_tabs, tab.Label, () => onTab(target), MenuButtonRole.Tab);
                button.Label.alignment = TextAlignmentOptions.MidlineLeft;
                _tabButtons.Add((tab, button));
            }
        }

        /// <summary>Видимость разделов по правам и выделение текущего.</summary>
        public void RefreshTabs(bool isAdmin, bool debugEnabled, MenuScreenType selected)
        {
            foreach ((MenuTab tab, KitButton button) in _tabButtons)
            {
                bool visible = MenuTab.IsVisible(tab.Visibility, isAdmin, debugEnabled);
                if (button.gameObject.activeSelf != visible) button.gameObject.SetActive(visible);
                button.Selected = tab.Screen == selected;
            }
        }

        public bool IsTabVisible(MenuScreenType screen, bool isAdmin, bool debugEnabled)
        {
            foreach ((MenuTab tab, KitButton _) in _tabButtons)
                if (tab.Screen == screen) return MenuTab.IsVisible(tab.Visibility, isAdmin, debugEnabled);
            return false;
        }

        // ── Действия ─────────────────────────────────────────────────────

        /// <summary>Главное действие экрана — всегда справа внизу области контента.</summary>
        public void SetPrimary(string label, UnityAction onClick, bool interactable = true,
                               MenuButtonRole role = MenuButtonRole.Primary)
        {
            _onPrimary = onClick;
            _primary.Text = label;
            _primary.SetRole(role);
            _primary.interactable = interactable;
            _primary.gameObject.SetActive(true);
            UpdateActionStrip();
        }

        /// <summary>Второе действие рядом с главным (слева от него). Остальные действия — в содержимом экрана.</summary>
        public void SetSecondary(string label, UnityAction onClick, bool interactable = true)
        {
            _onSecondary = onClick;
            _secondary.Text = label;
            _secondary.interactable = interactable;
            _secondary.gameObject.SetActive(true);
            UpdateActionStrip();
        }

        public void SetPrimaryInteractable(bool interactable)
        {
            if (_primary.interactable != interactable) _primary.interactable = interactable;
        }

        public void SetSecondaryInteractable(bool interactable)
        {
            if (_secondary.interactable != interactable) _secondary.interactable = interactable;
        }

        public void ClearActions()
        {
            _onPrimary = _onSecondary = null;
            _primary.gameObject.SetActive(false);
            _secondary.gameObject.SetActive(false);
            UpdateActionStrip();
        }

        /// <summary>Полоса действий есть — тело над ней; нет — тело на всю высоту.</summary>
        private void UpdateActionStrip()
        {
            MenuTheme t = MenuTheme.Instance;
            bool any = _primary.gameObject.activeSelf || _secondary.gameObject.activeSelf;
            _actions.gameObject.SetActive(any);
            var body = (RectTransform)_body.transform;
            body.offsetMin = new Vector2(body.offsetMin.x, any ? t.ActionStripHeight + t.ZoneGap : 0f);
        }

        // ── Экран ────────────────────────────────────────────────────────

        /// <summary>Новый экран — прокрутка в начало.</summary>
        public void ScrollToTop()
        {
            Canvas.ForceUpdateCanvases();
            _body.verticalNormalizedPosition = 1f;
            _body.velocity = Vector2.zero;
        }

        // ── Сборка ───────────────────────────────────────────────────────

        /// <summary>
        /// Строит каркас заново по токенам темы. Экраны из <see cref="Screens"/> переносятся в новый
        /// каркас, остальное старое содержимое удаляется.
        /// </summary>
        public void Build()
        {
            MenuTheme t = MenuTheme.Instance;

            var oldScreens = new List<Transform>();
            if (_screens != null) foreach (Transform s in _screens) oldScreens.Add(s);
            foreach (Transform s in oldScreens) s.SetParent(transform, false);
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (!oldScreens.Contains(child)) DestroyAny(child.gameObject);
            }

            MenuKit.Stretch(transform, 0f, 0f, 0f, 0f);
            var background = GetOrAdd<Image>(gameObject);
            background.sprite = null;
            GetOrAdd<MenuThemed>(gameObject).Set(MenuColorRole.Surface, MenuTextRole.Body);

            // ── Колонка ──
            GameObject rail = MenuKit.New("Rail", transform);
            _rail = (RectTransform)rail.transform;
            _rail.anchorMin = new Vector2(0f, 0f);
            _rail.anchorMax = new Vector2(0f, 1f);
            _rail.pivot = new Vector2(0f, 0.5f);
            _rail.offsetMin = new Vector2(t.SafeMargin, t.SafeMargin);
            _rail.offsetMax = new Vector2(t.SafeMargin + t.RailWidth, -t.SafeMargin);
            var railLayout = rail.AddComponent<VerticalLayoutGroup>();
            railLayout.spacing = t.Spacing;
            railLayout.childControlWidth = true;
            railLayout.childControlHeight = true;
            railLayout.childForceExpandWidth = true;
            railLayout.childForceExpandHeight = false;

            GameObject tabs = MenuKit.New("Tabs", _rail);
            _tabs = (RectTransform)tabs.transform;
            var tabsLayout = tabs.AddComponent<VerticalLayoutGroup>();
            tabsLayout.spacing = t.Spacing * 0.5f;
            tabsLayout.childControlWidth = true;
            tabsLayout.childControlHeight = true;
            tabsLayout.childForceExpandWidth = true;
            tabsLayout.childForceExpandHeight = false;
            tabs.AddComponent<LayoutElement>().flexibleHeight = 1f;

            GameObject divider = MenuKit.New("Divider", _rail);
            divider.AddComponent<Image>().raycastTarget = false;
            divider.AddComponent<MenuThemed>().Set(MenuColorRole.Divider, MenuTextRole.Body);
            divider.AddComponent<LayoutElement>().preferredHeight = 4f;

            _back = MenuKit.Button(_rail, "×  Закрыть", null, MenuButtonRole.Back);
            _back.name = "Btn_Back";
            _back.Label.alignment = TextAlignmentOptions.MidlineLeft;
            // Одна высота и одна базовая линия с «Начать» справа внизу.
            _back.GetComponent<LayoutElement>().minHeight = _back.GetComponent<LayoutElement>().preferredHeight = t.ActionStripHeight;


            // ── Область контента ──
            GameObject zone = MenuKit.New("ContentZone", transform);
            _contentZone = (RectTransform)zone.transform;
            MenuKit.Stretch(_contentZone, t.SafeMargin + t.RailWidth + t.SafeMargin, t.SafeMargin, t.SafeMargin, t.SafeMargin);

            GameObject body = MenuKit.New("Body", _contentZone);
            MenuKit.Stretch(body.transform, 0f, 0f, 0f, 0f);
            _body = body.AddComponent<ScrollRect>();
            _body.horizontal = false;
            _body.vertical = true;
            _body.movementType = ScrollRect.MovementType.Clamped;
            _body.inertia = true;
            _body.scrollSensitivity = 40f;

            GameObject viewport = MenuKit.New("Viewport", body.transform);
            _viewport = (RectTransform)viewport.transform;
            MenuKit.Stretch(_viewport, 0f, t.ScrollbarWidth + t.Spacing, 0f, 0f);
            viewport.AddComponent<RectMask2D>();
            // Прозрачная графика — цель луча для перетаскивания по пустому месту.
            Image hit = viewport.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);

            GameObject screens = MenuKit.New("Screens", _viewport);
            _screens = (RectTransform)screens.transform;
            _screens.anchorMin = new Vector2(0f, 1f);
            _screens.anchorMax = new Vector2(1f, 1f);
            _screens.pivot = new Vector2(0.5f, 1f);
            _screens.anchoredPosition = Vector2.zero;
            _screens.sizeDelta = Vector2.zero;
            var screensLayout = screens.AddComponent<VerticalLayoutGroup>();
            screensLayout.childControlWidth = true;
            screensLayout.childControlHeight = true;
            screensLayout.childForceExpandWidth = true;
            screensLayout.childForceExpandHeight = false;
            screens.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _body.viewport = _viewport;
            _body.content = _screens;
            _body.verticalScrollbar = BuildScrollbar(body.transform, t);
            _body.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            GameObject actions = MenuKit.New("Actions", _contentZone);
            _actions = (RectTransform)actions.transform;
            _actions.anchorMin = new Vector2(0f, 0f);
            _actions.anchorMax = new Vector2(1f, 0f);
            _actions.pivot = new Vector2(0.5f, 0f);
            _actions.anchoredPosition = Vector2.zero;
            _actions.sizeDelta = new Vector2(0f, t.ActionStripHeight);
            var actionsLayout = actions.AddComponent<HorizontalLayoutGroup>();
            actionsLayout.spacing = t.Spacing;
            actionsLayout.childAlignment = TextAnchor.MiddleRight;
            actionsLayout.childControlWidth = true;
            actionsLayout.childControlHeight = true;
            actionsLayout.childForceExpandWidth = false;
            actionsLayout.childForceExpandHeight = true;

            _secondary = MenuKit.Button(_actions, "Действие", null, MenuButtonRole.Secondary);
            _secondary.name = "Btn_Secondary";
            _primary = MenuKit.Button(_actions, "Начать", null, MenuButtonRole.Primary);
            _primary.name = "Btn_Primary";
            _primary.GetComponent<LayoutElement>().minWidth = t.PrimaryMinWidth;

            foreach (Transform s in oldScreens) s.SetParent(_screens, false);
            GetOrAdd<MenuStickScroll>(gameObject).Bind(_body);
        }

        private static Scrollbar BuildScrollbar(Transform parent, MenuTheme t)
        {
            GameObject bar = MenuKit.New("Scrollbar", parent);
            var rt = (RectTransform)bar.transform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(t.ScrollbarWidth, 0f);
            rt.anchoredPosition = Vector2.zero;
            Image track = bar.AddComponent<Image>();
            track.sprite = t.RoundedSprite;
            track.type = t.RoundedSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            bar.AddComponent<MenuThemed>().Set(MenuColorRole.SurfaceRaised, MenuTextRole.Body);

            GameObject area = MenuKit.New("Sliding Area", bar.transform);
            MenuKit.Stretch(area.transform, 0f, 0f, 0f, 0f);
            GameObject handle = MenuKit.New("Handle", area.transform);
            MenuKit.Stretch(handle.transform, 0f, 0f, 0f, 0f);
            Image handleImage = handle.AddComponent<Image>();
            handleImage.sprite = t.RoundedSprite;
            handleImage.type = t.RoundedSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            handle.AddComponent<MenuThemed>().Set(MenuColorRole.Accent, MenuTextRole.Body);

            var scrollbar = bar.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = (RectTransform)handle.transform;
            scrollbar.targetGraphic = handleImage;
            scrollbar.transition = Selectable.Transition.None;
            return scrollbar;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static void DestroyAny(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
