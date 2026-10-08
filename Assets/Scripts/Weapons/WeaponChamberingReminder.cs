using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Haptics;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Подсказка ручного досылания прежних (legacy) стволов; единственный писатель Action-визуала. Механика оружия не меняется.
    /// На стволах <see cref="WeaponSystem"/> компонента нет: подсказку ведёт машина (<see cref="WeaponFeedbackExecutor"/>).
    /// </summary>
    [RequireComponent(typeof(UxrFirearmWeapon))]
    public sealed class WeaponChamberingReminder : MonoBehaviour
    {
        [SerializeField] private int _triggerIndex;
        [SerializeField] private GameObject _proximitySignal;
        [SerializeField] private GameObject _visual;
        [SerializeField, Range(0f, 1f)] private float _hapticAmplitude = 0.15f;
        [SerializeField, Min(0f)] private float _hapticSeconds = 0.08f;
        [SerializeField, Min(0f)] private float _hapticCooldown = 0.6f;

        private UxrFirearmWeapon _firearm;
        private UxrGrabbableObject _triggerGrip;
        private int _triggerGripPoint;
        private UxrGrabber _reminderHand;
        private UxrGrabber _hapticHand;
        private bool _latched;
        private float _nextHapticTime;

        public GameObject Visual => _visual;
        public GameObject ProximitySignal => _proximitySignal;
        public bool IsReminding => _latched && _firearm != null && _firearm.CanUse;

        private void Awake() => _firearm = GetComponent<UxrFirearmWeapon>();

        private void OnEnable()
        {
            if (_firearm == null) _firearm = GetComponent<UxrFirearmWeapon>();
            _firearm.ChamberingRequired += HandleChamberingRequired;
            if (_firearm.TryGetTriggerGrip(_triggerIndex, out _triggerGrip, out _triggerGripPoint))
            {
                _triggerGrip.Released += HandleGripReleased;
                _triggerGrip.Placed += HandleGripPlaced;
            }
            // SDK уже выбрал proximity-кандидата и обработал оружие к этому событию.
            UxrManager.AvatarsUpdated += UpdateFeedback;
        }

        private void OnDisable()
        {
            if (_firearm != null) _firearm.ChamberingRequired -= HandleChamberingRequired;
            if (_triggerGrip != null)
            {
                _triggerGrip.Released -= HandleGripReleased;
                _triggerGrip.Placed -= HandleGripPlaced;
            }
            _triggerGrip = null;
            UxrManager.AvatarsUpdated -= UpdateFeedback;
            _latched = false;
            _reminderHand = null;
            _hapticHand = null;
            _nextHapticTime = 0f;
            SetVisible(false);
        }

        // Страховка при прекращении avatar update, снятии магазина и выключении manager.
        private void LateUpdate() => UpdateFeedback();

        private void HandleChamberingRequired(int triggerIndex, UxrGrabber hand)
        {
            if (triggerIndex != _triggerIndex || !_firearm.CanUse ||
                !_firearm.NeedsManualChambering(triggerIndex) || !IsTriggerHand(hand)) return;

            LatchAndPulse(hand, true, _hapticAmplitude, _hapticSeconds, _hapticCooldown);
        }

        private void LatchAndPulse(UxrGrabber hand, bool hapticEnabled, float amplitude, float seconds, float cooldown)
        {
            _latched = true;
            _reminderHand = hand;
            UpdateFeedback();
            if (_hapticHand != hand) { _hapticHand = hand; _nextHapticTime = 0f; }
            if (!hapticEnabled || Time.unscaledTime < _nextHapticTime) return;
            _nextHapticTime = Time.unscaledTime + Mathf.Max(0f, cooldown);
            if (hand.Avatar.ControllerInput != null && amplitude > 0f && seconds > 0f)
            {
                hand.Avatar.ControllerInput.SendHapticFeedback(hand.Side, 0f, amplitude, seconds, UxrHapticMode.Mix);
            }
        }

        private void HandleGripReleased(object sender, UxrManipulationEventArgs e)
        {
            if (e.GrabPointIndex != _triggerGripPoint) return;
            ClearReminder();
            UpdateFeedback();
        }

        private void HandleGripPlaced(object sender, UxrManipulationEventArgs e)
        {
            ClearReminder();
            UpdateFeedback();
        }

        private void ClearReminder()
        {
            _latched = false;
            _reminderHand = null;
        }

        private bool IsTriggerHand(UxrGrabber hand)
        {
            return hand != null && hand.Avatar != null && hand.Avatar.AvatarMode == UxrAvatarMode.Local &&
                   UxrGrabManager.HasInstance && _firearm.TryGetTriggerGrip(_triggerIndex, out var grip, out int point) &&
                   UxrGrabManager.Instance.GetGrabbingHand(grip, point, out UxrGrabber current) && current == hand &&
                   StateEventAuthority.IsAuthorOfItem(_firearm);
        }

        private void UpdateFeedback()
        {
            if (_latched && (!_firearm.NeedsManualChambering(_triggerIndex) || !IsTriggerHand(_reminderHand)))
            {
                _latched = false;
                _reminderHand = null;
            }
            bool near = UxrManager.HasInstance && UxrManager.Instance.isActiveAndEnabled &&
                        UxrGrabManager.HasInstance && UxrGrabManager.Instance.isActiveAndEnabled &&
                        (UxrGrabManager.Instance.Features & UxrManipulationFeatures.Affordances) != 0 &&
                        _proximitySignal != null && _proximitySignal.activeInHierarchy;
            SetVisible(near || IsReminding);
        }

        private void SetVisible(bool visible)
        {
            if (_visual != null && _visual.activeSelf != visible) _visual.SetActive(visible);
        }
    }
}
