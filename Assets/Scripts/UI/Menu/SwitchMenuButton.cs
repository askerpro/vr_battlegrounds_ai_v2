using UnityEngine;
using UnityEngine.UI;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.UI.Menu
{
    [RequireComponent(typeof(Button))]
    public class SwitchMenuButton : MonoBehaviour
    {
        [SerializeField] private MenuScreenType _targetScreen;
        public MenuScreenType TargetScreen { get => _targetScreen; set => _targetScreen = value; }
        private Button _button;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(OnClick);
        }

        private void OnDestroy()
        {
            if (_button != null)
                _button.onClick.RemoveListener(OnClick);
        }

        private void OnClick()
        {
            if (MenuController.Instance != null)
            {
                MenuController.Instance.SwitchTo(_targetScreen);
            }
            else
            {
                GameLog.UI.Warning($"[SwitchMenuButton] MenuController is null. Cannot switch to {_targetScreen}.");
            }
        }
    }
}
