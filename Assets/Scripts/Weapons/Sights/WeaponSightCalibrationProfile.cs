using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Weapons.Sights
{
    /// <summary>Адресная конфигурация сведения. Это запрос калибровки, не свидетельство её принятия.</summary>
    [CreateAssetMenu(fileName = "WeaponSightCalibrationProfile", menuName = "VR Battlegrounds/Weapons/Sight Calibration Profile")]
    public sealed class WeaponSightCalibrationProfile : ScriptableObject
    {
        [SerializeField] private WeaponInfo _weapon;
        [Tooltip("Стабильный идентификатор конкретного прицела после визуальной разметки.")]
        [SerializeField] private string _sightId;
        [SerializeField] private int _triggerIndex;
        [SerializeField] private int _shotTypeIndex;
        [SerializeField] private bool _overrideZeroDistance;
        [SerializeField] private float _customZeroDistance = WeaponSightCalibrationSettings.InitialZeroDistance;

        public WeaponInfo Weapon => _weapon;
        public string SightId => _sightId;
        public int TriggerIndex => _triggerIndex;
        public int ShotTypeIndex => _shotTypeIndex;
        public bool OverridesZeroDistance => _overrideZeroDistance;
        public float CustomZeroDistance => _customZeroDistance;

        /// <summary>Невалидный общий конфиг не скрывается индивидуальным override.</summary>
        public bool TryGetZeroDistance(WeaponSightCalibrationSettings settings, out float distance)
        {
            distance = 0f;
            if (settings == null || !WeaponSightCalibrationSettings.IsValidDistance(settings.DefaultZeroDistance))
                return false;
            float requested = _overrideZeroDistance ? _customZeroDistance : settings.DefaultZeroDistance;
            if (!WeaponSightCalibrationSettings.IsValidDistance(requested)) return false;
            distance = requested;
            return true;
        }
    }
}
