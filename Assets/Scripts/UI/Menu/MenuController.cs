using System.Collections.Generic;
using UnityEngine;
using UltimateXR.Avatar;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Глобальный контроллер меню: открыть/закрыть планшет перед игроком и навигация по экранам.
    ///
    /// <para>
    /// Навигация — стек (<see cref="MenuNavigation"/>): раздел левой колонки — корень стека
    /// (<see cref="OpenTab"/>), вложенный экран — <see cref="Push"/>, «Назад» — <see cref="Back"/>;
    /// на корне «Назад» становится «Закрыть». Каркас (<see cref="MenuFrame"/>) рисует колонку,
    /// прокручиваемую область и полосу главного действия; экран — только содержимое (T-32).
    /// </para>
    /// </summary>
    public class MenuController : MonoBehaviour
    {
        public static MenuController Instance { get; private set; }

        [Header("Positioning (Smart Drop)")]
        [Tooltip("Distance from the camera when the menu is dropped.")]
        [SerializeField] private float _dropDistance = 1.0f;
        [Tooltip("Height offset from the camera's eye level.")]
        [SerializeField] private float _heightOffset = -0.2f;

        [Tooltip("Как часто пересчитывать видимость разделов по правам, пока меню открыто, с.")]
        [SerializeField] private float _tabsRefreshInterval = 0.5f;

        private readonly Dictionary<MenuScreenType, MenuScreen> _screens = new Dictionary<MenuScreenType, MenuScreen>();
        private readonly MenuNavigation _navigation = new MenuNavigation();
        private bool _isOpen = false;
        private MenuView _currentView;
        private float _tabsTimer;

        // Разделы колонки объявляет активный режим (GameModeData.menuTabs) — пересобираются при его смене.
        private List<MenuTab> _tabs = new List<MenuTab>();
        private GameModeData _tabsMode;
        private bool _tabsBuilt;

        public bool IsOpen => _isOpen;
        public MenuFrame Frame => _currentView != null ? _currentView.Frame : null;
        public MenuScreenType CurrentScreen => _navigation.Current;
        public MenuNavigation Navigation => _navigation;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                GameLog.UI.Warning("[MenuController] Новое меню заменяет старый (ещё не удалённый) инстанс.");
            }
            Instance = this;
        }

        public void Initialize(MenuView view)
        {
            if (view == null)
            {
                GameLog.Error("[MenuController] Cannot initialize with a null MenuView.");
                return;
            }

            // Clean up old state if re-initialized
            if (_isOpen) CloseMenu();
            _screens.Clear();
            _navigation.Clear();

            _currentView = view;

            if (_currentView.MenuRoot == null)
            {
                GameLog.Error("[MenuController] MenuView.MenuRoot is not assigned!");
                return;
            }

            // Cache all MenuScreens within the root
            MenuScreen[] foundScreens = _currentView.MenuRoot.GetComponentsInChildren<MenuScreen>(true);
            foreach (var screen in foundScreens)
            {
                if (screen.ScreenType == MenuScreenType.None)
                {
                    GameLog.UI.Warning($"[MenuController] MenuScreen '{screen.name}' has no type assigned (None). Ignored.");
                    continue;
                }

                if (!_screens.ContainsKey(screen.ScreenType))
                {
                    _screens.Add(screen.ScreenType, screen);
                    if (Frame != null) MenuScreen.ApplyContentLayout(screen.gameObject);
                }
                else
                {
                    GameLog.UI.Warning($"[MenuController] Duplicate MenuScreenType {screen.ScreenType} found on '{screen.name}'. Ignored.");
                }
            }

            _tabsBuilt = false;
            RebuildTabsForMode();
            if (Frame == null)
            {
                GameLog.UI.Warning($"[MenuController] У планшета '{view.name}' нет каркаса MenuFrame — навигация без колонки.");
            }

            // Initialize screens so they don't overlap
            OpenTab(DefaultTab());

            // State is closed initially (GameObject is deactivated by LocalMenuManager)
            _isOpen = false;
        }

        public void ToggleMenu()
        {
            if (_isOpen)
                CloseMenu();
            else
                OpenMenu();
        }

        public void OpenMenu()
        {
            if (_isOpen || _currentView == null) return;
            _isOpen = true;

            // Positioning logic: Smart Drop before the player (teleport the ENTIRE tablet, not just the canvas)
            if (UxrAvatar.LocalAvatar != null && UxrAvatar.LocalAvatar.CameraComponent != null)
            {
                Transform camTransform = UxrAvatar.LocalAvatar.CameraComponent.transform;

                // Calculate position: in front of the camera, slightly down
                Vector3 forward = camTransform.forward;
                forward.y = 0; // Keep it perfectly flat horizontally if desired
                forward.Normalize();

                Vector3 targetPosition = camTransform.position + forward * _dropDistance;

                // Workaround for XR tracking bugs creating massive coordinates when headset is sleeping
                if (targetPosition.sqrMagnitude > 1000000f)
                {
                    targetPosition = transform.parent != null ? transform.parent.position : Vector3.zero;
                    targetPosition += Vector3.forward * _dropDistance;
                }

                targetPosition.y += _heightOffset;

                Transform targetTransform = _currentView.transform;
                targetTransform.position = targetPosition;
                targetTransform.rotation = Quaternion.LookRotation(forward);
            }

            _currentView.gameObject.SetActive(true);

            RebuildTabsForMode();
            OpenTab(DefaultTab());
        }

        public void CloseMenu()
        {
            if (!_isOpen) return;
            _isOpen = false;

            // Deactivate all screens
            foreach (var screen in _screens.Values)
            {
                screen.Hide();
            }

            // Hide the UI canvas and the entire menu prefab
            if (_currentView != null)
            {
                _currentView.gameObject.SetActive(false);
            }
        }

        // ── Навигация ────────────────────────────────────────────────────

        /// <summary>Открыть раздел левой колонки: стек сбрасывается до него.</summary>
        public void OpenTab(MenuScreenType screenType)
        {
            if (!_screens.ContainsKey(screenType))
            {
                GameLog.UI.Warning($"[MenuController] Attempted to switch to unknown screen type: {screenType}");
                return;
            }

            _navigation.OpenTab(screenType);
            ShowCurrent();
        }

        /// <summary>Вложенный экран текущего раздела.</summary>
        public void Push(MenuScreenType screenType)
        {
            if (!_screens.ContainsKey(screenType))
            {
                GameLog.UI.Warning($"[MenuController] Attempted to switch to unknown screen type: {screenType}");
                return;
            }

            if (IsTab(screenType)) _navigation.OpenTab(screenType);
            else _navigation.Push(screenType);
            ShowCurrent();
        }

        /// <summary>Переход по старым кнопкам: раздел — открыть раздел, иначе — вложенный экран.</summary>
        public void SwitchTo(MenuScreenType screenType) => Push(screenType);

        /// <summary>«Назад»: сначала шаг внутри экрана, потом стек; на корне — закрыть меню.</summary>
        public void Back()
        {
            if (_screens.TryGetValue(_navigation.Current, out MenuScreen current) && current.HandleBack())
            {
                RefreshBackButton();
                return;
            }

            if (_navigation.Back()) ShowCurrent();
            else CloseMenu();
        }

        /// <summary>Надпись левого нижнего слота: «Закрыть» на корне, иначе «Назад».</summary>
        public void RefreshBackButton()
        {
            if (Frame == null) return;
            bool innerBack = _screens.TryGetValue(_navigation.Current, out MenuScreen current) && current.HasInnerBack;
            Frame.SetBack(_navigation.IsAtRoot && !innerBack, Back);
        }

        private bool IsTab(MenuScreenType screenType) => _tabs.Exists(t => t.Screen == screenType);

        /// <summary>Активный режим карты — его данные объявляют разделы колонки. Нет режима — разделы по умолчанию.</summary>
        private static GameModeData ActiveModeData()
        {
            GameMode mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;
            return mode != null ? mode.ModeData : null;
        }

        /// <summary>Пересобрать колонку, если сменился режим. <c>true</c> — пересобрана.</summary>
        private bool RebuildTabsForMode()
        {
            GameModeData mode = ActiveModeData();
            if (_tabsBuilt && mode == _tabsMode) return false;

            _tabsMode = mode;
            _tabsBuilt = true;
            _tabs = MenuTabs.Resolve(mode != null ? mode.menuTabs : null, s => _screens.ContainsKey(s));
            if (_tabs.Count > MenuTabs.Max)
                GameLog.UI.Warning($"[MenuController] Режим '{(mode != null ? mode.modeId : "-")}': разделов {_tabs.Count} > {MenuTabs.Max}, колонка переполнится.");
            if (Frame != null) Frame.BuildTabs(_tabs, OpenTab);
            return true;
        }

        /// <summary>Раздел при открытии: заданный в MenuView, если он есть у режима, иначе первый раздел режима.</summary>
        private MenuScreenType DefaultTab()
        {
            if (_currentView != null && IsTab(_currentView.DefaultScreen)) return _currentView.DefaultScreen;
            return _tabs.Count > 0 ? _tabs[0].Screen : (_currentView != null ? _currentView.DefaultScreen : MenuScreenType.Main);
        }

        private void ShowCurrent()
        {
            MenuScreenType target = _navigation.Current;

            foreach (var kvp in _screens)
            {
                if (kvp.Key != target && kvp.Value.gameObject.activeSelf) kvp.Value.Hide();
            }

            if (Frame != null)
            {
                Frame.ClearActions();
                RefreshTabs();
            }

            if (_screens.TryGetValue(target, out MenuScreen screen)) screen.Show();

            if (Frame != null)
            {
                RefreshBackButton();
                Frame.ScrollToTop();
            }
        }

        private void Update()
        {
            if (!_isOpen || Frame == null) return;

            _tabsTimer += Time.unscaledDeltaTime;
            if (_tabsTimer < _tabsRefreshInterval) return;
            _tabsTimer = 0f;
            // Сменился режим на карте — свой набор разделов; текущего раздела в нём нет — на раздел по умолчанию.
            if (RebuildTabsForMode() && !IsTab(_navigation.Root))
            {
                OpenTab(DefaultTab());
                return;
            }
            RefreshTabs();

            // Раздел стал недоступен (сняли права, выключили отладку) — на раздел по умолчанию.
            bool admin = MenuPermissions.ShowAdminUi();
            if (IsTab(_navigation.Root) && !Frame.IsTabVisible(_navigation.Root, admin, DebugMode.Enabled))
                OpenTab(DefaultTab());
        }

        private void RefreshTabs()
        {
            if (Frame == null) return;
            Frame.RefreshTabs(MenuPermissions.ShowAdminUi(), DebugMode.Enabled, _navigation.Root);
        }
    }
}
