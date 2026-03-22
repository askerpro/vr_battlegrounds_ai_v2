using System.Collections.Generic;
using UnityEngine;
using UltimateXR.Avatar;

namespace VrBattlegrounds.UI.Menu
{
    public class MenuController : MonoBehaviour
    {
        public static MenuController Instance { get; private set; }

        [Header("Positioning (Smart Drop)")]
        [Tooltip("Distance from the camera when the menu is dropped.")]
        [SerializeField] private float _dropDistance = 1.0f;
        [Tooltip("Height offset from the camera's eye level.")]
        [SerializeField] private float _heightOffset = -0.2f;

        private Dictionary<MenuScreenType, MenuScreen> _screens = new Dictionary<MenuScreenType, MenuScreen>();
        private bool _isOpen = false;
        private MenuView _currentView;

        public bool IsOpen => _isOpen;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[MenuController] Новое меню заменяет старый (ещё не удалённый) инстанс.");
            }
            Instance = this;
        }

        public void Initialize(MenuView view)
        {
            if (view == null)
            {
                Debug.LogError("[MenuController] Cannot initialize with a null MenuView.");
                return;
            }

            // Clean up old state if re-initialized
            if (_isOpen) CloseMenu();
            _screens.Clear();

            _currentView = view;

            if (_currentView.MenuRoot == null)
            {
                Debug.LogError("[MenuController] MenuView.MenuRoot is not assigned!");
                return;
            }

            // Cache all MenuScreens within the root
            MenuScreen[] foundScreens = _currentView.MenuRoot.GetComponentsInChildren<MenuScreen>(true);
            foreach (var screen in foundScreens)
            {
                if (screen.ScreenType == MenuScreenType.None)
                {
                    Debug.LogWarning($"[MenuController] MenuScreen '{screen.name}' has no type assigned (None). Ignored.");
                    continue;
                }

                if (!_screens.ContainsKey(screen.ScreenType))
                {
                    _screens.Add(screen.ScreenType, screen);
                }
                else
                {
                    Debug.LogWarning($"[MenuController] Duplicate MenuScreenType {screen.ScreenType} found on '{screen.name}'. Ignored.");
                }
            }

            // Initialize screens so they don't overlap
            SwitchTo(_currentView.DefaultScreen);

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

            SwitchTo(_currentView.DefaultScreen);
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

        public void SwitchTo(MenuScreenType screenType)
        {
            if (!_screens.ContainsKey(screenType))
            {
                Debug.LogWarning($"[MenuController] Attempted to switch to unknown screen type: {screenType}");
                return;
            }

            foreach (var kvp in _screens)
            {
                if (kvp.Key == screenType)
                    kvp.Value.Show();
                else
                    kvp.Value.Hide();
            }
        }
    }
}
