using System;
using UltimateXR.Audio;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>Независимая реакция на одну причину отказа; получатели не изменяют готовность.</summary>
    [Serializable]
    public sealed class WeaponAttemptReaction
    {
        [SerializeField] private bool _audioEnabled = true;
        [SerializeField] private UxrAudioSample _audioOverride;
        [SerializeField] private bool _additionalHapticEnabled;
        [SerializeField, Range(0f, 1f)] private float _hapticAmplitude = 0.15f;
        [SerializeField, Min(0f)] private float _hapticSeconds = 0.08f;
        [SerializeField, Min(0f)] private float _hapticCooldown = 0.6f;
        [SerializeField] private bool _reminderHapticEnabled = true;
        [SerializeField] private bool _useExistingReminderParameters = true;
        [SerializeField] private bool _highlightEnabled;
        [SerializeField] private Color _highlightColor = Color.red;
        [SerializeField] private bool _chamberReminderEnabled = true;

        public bool AudioEnabled => _audioEnabled;
        public UxrAudioSample AudioOverride => _audioOverride;
        public bool AdditionalHapticEnabled => _additionalHapticEnabled;
        public float HapticAmplitude => _hapticAmplitude;
        public float HapticSeconds => _hapticSeconds;
        public float HapticCooldown => _hapticCooldown;
        public bool ReminderHapticEnabled => _reminderHapticEnabled;
        public bool UseExistingReminderParameters => _useExistingReminderParameters;
        public bool HighlightEnabled => _highlightEnabled;
        public Color HighlightColor => _highlightColor;
        public bool ChamberReminderEnabled => _chamberReminderEnabled;
    }

    /// <summary>Общий default либо полный override конкретного оружия; production ссылки задаёт producer.</summary>
    [CreateAssetMenu(menuName = "VR Battlegrounds/Weapons/Feedback Profile")]
    public sealed class WeaponFeedbackProfile : ScriptableObject
    {
        [SerializeField] private WeaponAttemptReaction _noMagazine = new WeaponAttemptReaction();
        [SerializeField] private WeaponAttemptReaction _emptyMagazine = new WeaponAttemptReaction();
        [SerializeField] private WeaponAttemptReaction _chamberingRequired = new WeaponAttemptReaction();

        public WeaponAttemptReaction GetReaction(UxrFirearmNotReadyReason reason) => reason switch
        {
            UxrFirearmNotReadyReason.NoMagazine => _noMagazine,
            UxrFirearmNotReadyReason.EmptyMagazine => _emptyMagazine,
            UxrFirearmNotReadyReason.ChamberingRequired => _chamberingRequired,
            _ => null
        };
        public bool TryValidate(out string error)
        {
            foreach (UxrFirearmNotReadyReason reason in Enum.GetValues(typeof(UxrFirearmNotReadyReason)))
            {
                WeaponAttemptReaction reaction = GetReaction(reason);
                if (reaction == null || float.IsNaN(reaction.HapticAmplitude) || float.IsInfinity(reaction.HapticAmplitude) ||
                    float.IsNaN(reaction.HapticSeconds) || float.IsInfinity(reaction.HapticSeconds) ||
                    float.IsNaN(reaction.HapticCooldown) || float.IsInfinity(reaction.HapticCooldown) ||
                    reaction.HapticAmplitude < 0f || reaction.HapticAmplitude > 1f || reaction.HapticSeconds < 0f || reaction.HapticCooldown < 0f ||
                    (reason == UxrFirearmNotReadyReason.ChamberingRequired && reaction.AdditionalHapticEnabled))
                { error = "Невалидная реакция; ChamberingRequired haptic принадлежит единственному Reminder."; return false; }
            }
            error = null; return true;
        }
    }
}
