using UnityEngine;

namespace VrBattlegrounds.Weapons.Sights
{
    /// <summary>Общие дистанции калибровки и удобство прицеливания оптики; не изменяет выстрел и префабы.</summary>
    [CreateAssetMenu(fileName = "WeaponSightCalibrationSettings", menuName = "VR Battlegrounds/Weapons/Sight Calibration Settings")]
    public sealed class WeaponSightCalibrationSettings : ScriptableObject
    {
        public const float ArenaMaxDistance = 20f;
        public const float InitialZeroDistance = 15f;

        [Tooltip("Дистанция сведения по умолчанию в метрах от ShotSource. Изменение требует повторной калибровки.")]
        [SerializeField] private float _defaultZeroDistance = InitialZeroDistance;

        [Header("Оптика с кратностью")]
        [Tooltip("Допуск ракурса, м: зрачок выхода увеличивающего прицела. Меньше — нужен почти идеальный ракурс глаза по оси " +
                 "(как у снайперской оптики), больше — прицеливаться проще. При смещении глаза на это значение светлое поле " +
                 "сужается и уходит к краю, на 3 значения — до трети стекла. Камера прицела читает его каждый кадр: в Play " +
                 "правка видна сразу (шлем 2026-10-23: 4 мм строго, принято 5 мм).")]
        [SerializeField, Range(0.002f, 0.02f)] private float _scopeEyeTolerance = DefaultScopeEyeTolerance;

        public const float DefaultScopeEyeTolerance = 0.005f;

        public float DefaultZeroDistance => _defaultZeroDistance;
        public float ScopeEyeTolerance => _scopeEyeTolerance;

        public static bool IsValidDistance(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f && value <= ArenaMaxDistance;
        }
    }
}
