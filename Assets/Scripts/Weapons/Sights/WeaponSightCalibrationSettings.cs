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

        [Tooltip("Дистанция включения, м: картинка увеличения чёткая, когда глаз не дальше этого позади окуляра вдоль оси " +
                 "прицела; дальше ещё 15 см она плавно размывается и тускнеет. Больше — чёткая картинка при оружии дальше от " +
                 "лица. В Play правка видна сразу (решение пользователя 2026-10-24: 35 см; было зашито 15 см).")]
        [SerializeField, Range(0.05f, 0.5f)] private float _scopeEnterDistance = DefaultScopeEnterDistance;

        public const float DefaultScopeEnterDistance = 0.35f;
        /// <summary>За дистанцией включения картинка плавно размывается на этом отрезке, м (решение пользователя 2026-10-24).</summary>
        public const float ScopeFadeBand = 0.15f;

        public float DefaultZeroDistance => _defaultZeroDistance;
        public float ScopeEyeTolerance => _scopeEyeTolerance;
        public float ScopeEnterDistance => _scopeEnterDistance;

        public static bool IsValidDistance(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f && value <= ArenaMaxDistance;
        }
    }
}
