using System;
using System.Collections.Generic;
using UltimateXR.Haptics;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Weapons
{
    /// <summary>Один audio owner и additive readonly receivers. Policy/боезапас здесь не изменяются.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(WeaponTriggerAttemptRouter))]
    public sealed class WeaponAttemptFeedback : MonoBehaviour
    {
        [SerializeField] private WeaponFeedbackProfile _commonProfile;
        [SerializeField] private WeaponFeedbackProfile _weaponOverride;
        private WeaponTriggerAttemptRouter _router;
        private readonly WeaponAttemptReaction _fallback = new WeaponAttemptReaction();
        private readonly Dictionary<(UxrGrabber, UxrFirearmNotReadyReason), float> _nextHaptic = new Dictionary<(UxrGrabber, UxrFirearmNotReadyReason), float>();
        public event Action<WeaponTriggerAttemptContext, UxrFirearmNotReadyReason, WeaponAttemptReaction> FeedbackRequested;
        public WeaponFeedbackProfile EffectiveProfile => _weaponOverride != null ? _weaponOverride : _commonProfile;

        private void OnEnable()
        {
            _router = GetComponent<WeaponTriggerAttemptRouter>();
            _router.NotReadyAttempted += HandleAttempt;
        }
        private void OnDisable()
        {
            if (_router != null) _router.NotReadyAttempted -= HandleAttempt;
            _router = null;
            _nextHaptic.Clear();
        }
        public bool Configure(WeaponFeedbackProfile common, WeaponFeedbackProfile weaponOverride, out string error)
        {
            if ((common != null && !common.TryValidate(out error)) ||
                (weaponOverride != null && !weaponOverride.TryValidate(out error))) return false;
            _commonProfile = common; _weaponOverride = weaponOverride; error = null; return true;
        }

        public WeaponAttemptReaction GetReaction(UxrFirearmNotReadyReason reason) => EffectiveProfile != null ? EffectiveProfile.GetReaction(reason) : _fallback;

        private void HandleAttempt(WeaponTriggerAttemptContext context, UxrFirearmNotReadyReason reason)
        {
            WeaponAttemptReaction reaction = GetReaction(reason);
            if (reaction == null) return;
            if (reaction.AudioEnabled)
            {
                try
                {
                    Vector3 position = context.Firearm.GetTriggerNoAmmoSoundPosition(context.TriggerIndex, context.MainGrabber);
                    if (reaction.AudioOverride?.Clip != null) reaction.AudioOverride.Play(position);
                    else context.Firearm.PlayTriggerNoAmmoSound(context.TriggerIndex, position);
                }
                catch (Exception exception) { GameLog.WeaponSystem.Error("Ошибка audio receiver попытки спуска: " + exception, this); }
            }
            if (reason != UxrFirearmNotReadyReason.ChamberingRequired && reaction.AdditionalHapticEnabled &&
                context.MainGrabber != null && context.MainGrabber.Avatar != null &&
                context.MainGrabber.Avatar.ControllerInput != null && reaction.HapticAmplitude > 0f && reaction.HapticSeconds > 0f)
            {
                var key = (context.MainGrabber, reason);
                if (!_nextHaptic.TryGetValue(key, out float next) || Time.unscaledTime >= next)
                {
                    _nextHaptic[key] = Time.unscaledTime + reaction.HapticCooldown;
                    try { context.MainGrabber.Avatar.ControllerInput.SendHapticFeedback(context.Side, 0f,
                        reaction.HapticAmplitude, reaction.HapticSeconds, UxrHapticMode.Mix); }
                    catch (Exception exception) { GameLog.WeaponSystem.Error("Ошибка haptic receiver попытки спуска: " + exception, this); }
                }
            }
            if (FeedbackRequested == null) return;
            foreach (Action<WeaponTriggerAttemptContext, UxrFirearmNotReadyReason, WeaponAttemptReaction> receiver in FeedbackRequested.GetInvocationList())
                try { receiver(context, reason, reaction); }
                catch (Exception exception) { GameLog.WeaponSystem.Error("Ошибка feedback receiver попытки спуска: " + exception, this); }
        }
    }
}
