using UnityEngine;

namespace VrBattlegrounds.Weapons.Sights
{
    /// <summary>Общие дистанции калибровки; не изменяет выстрел и префабы.</summary>
    [CreateAssetMenu(fileName = "WeaponSightCalibrationSettings", menuName = "VR Battlegrounds/Weapons/Sight Calibration Settings")]
    public sealed class WeaponSightCalibrationSettings : ScriptableObject
    {
        public const float ArenaMaxDistance = 20f;
        public const float InitialZeroDistance = 15f;

        [Tooltip("Дистанция сведения по умолчанию в метрах от ShotSource. Изменение требует повторной калибровки.")]
        [SerializeField] private float _defaultZeroDistance = InitialZeroDistance;

        public float DefaultZeroDistance => _defaultZeroDistance;

        public static bool IsValidDistance(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f && value <= ArenaMaxDistance;
        }
    }
}
