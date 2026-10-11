using UltimateXR.Haptics;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Haptics
{
    /// <summary>
    /// Запасные клипы для вибраций UltimateXR без формы — один ассет проекта (<c>Resources/SdkHapticFallbacks.asset</c>,
    /// <c>tasks/haptics-system/Details.md</c> §5.3). Клипы SDK в проекте формы не имеют (выстрел, клики хвата, клики UI,
    /// сорванный хват); вместо правки чужих префабов <see cref="SdkHapticRouter" /> играет клип отсюда по типу
    /// <see cref="UxrHapticClipType" /> запроса. Сила клипа — 1: масштаб даёт сила запроса SDK. Пустой клип (без формы) —
    /// тишина для этого типа. Числа подбираются в шлеме. Без ассета — пустой конфиг (тишина) и <c>GameLog.Error</c>.
    /// </summary>
    [CreateAssetMenu(fileName = nameof(SdkHapticFallbacks), menuName = "VR Battlegrounds/Haptics/SDK Haptic Fallbacks")]
    public sealed class SdkHapticFallbacks : ScriptableObject
    {
        /// <summary>Длительность гула Rumble*, если SDK её не задал (как в корутине SDK).</summary>
        public const float DefaultRumbleSeconds = 0.5f;

        private static SdkHapticFallbacks s_instance;

        [Header("Гул (Rumble*): форма повторяется весь срок запроса")]
        [SerializeField] private UxrHapticClip _rumbleFreqVeryLow = new UxrHapticClip();
        [SerializeField] private UxrHapticClip _rumbleFreqLow = new UxrHapticClip();
        [SerializeField] private UxrHapticClip _rumbleFreqNormal = new UxrHapticClip();
        [SerializeField] private UxrHapticClip _rumbleFreqHigh = new UxrHapticClip();
        [SerializeField] private UxrHapticClip _rumbleFreqVeryHigh = new UxrHapticClip();

        [Header("Разовые: форма звучит один раз")]
        [Tooltip("Клики UI, сорванный хват, клики хвата/укладки предметов SDK.")]
        [SerializeField] private UxrHapticClip _click = new UxrHapticClip();
        [Tooltip("Выстрел (малый).")]
        [SerializeField] private UxrHapticClip _shot = new UxrHapticClip();
        [Tooltip("Выстрел: у всех стволов проекта ShotHapticClip = ShotBig.")]
        [SerializeField] private UxrHapticClip _shotBig = new UxrHapticClip();
        [Tooltip("Выстрел (большой).")]
        [SerializeField] private UxrHapticClip _shotBigger = new UxrHapticClip();
        [Tooltip("Ход механизма: помпа «уже заряжено», затвор.")]
        [SerializeField] private UxrHapticClip _slide = new UxrHapticClip();
        [Tooltip("Взрыв (UxrHapticOnImpact и т. п.).")]
        [SerializeField] private UxrHapticClip _explosion = new UxrHapticClip();

        [Header("Сырые запросы SDK (частота, сила, длительность)")]
        [Tooltip("Непрерывная манипуляция планшета, перетаскивание UI, UxrFixedHapticFeedback: форма повторяется, пока идут " +
                 "запросы, сила — из запроса.")]
        [SerializeField] private UxrHapticClip _raw = new UxrHapticClip();

        public static SdkHapticFallbacks Instance
        {
            get
            {
                if (s_instance == null) s_instance = Resources.Load<SdkHapticFallbacks>(nameof(SdkHapticFallbacks));
                if (s_instance == null)
                {
                    GameLog.Error("[SdkHapticFallbacks] Нет Resources/SdkHapticFallbacks.asset — вибрации UltimateXR без формы будут тихими.");
                    s_instance = CreateInstance<SdkHapticFallbacks>();
                }
                return s_instance;
            }
        }

        /// <summary>Клип для сырых запросов SDK.</summary>
        public UxrHapticClip Raw => _raw;

        /// <summary>Запасной клип типа или null (None и неизвестные типы).</summary>
        public UxrHapticClip Get(UxrHapticClipType type) => type switch
        {
            UxrHapticClipType.RumbleFreqVeryLow => _rumbleFreqVeryLow,
            UxrHapticClipType.RumbleFreqLow => _rumbleFreqLow,
            UxrHapticClipType.RumbleFreqNormal => _rumbleFreqNormal,
            UxrHapticClipType.RumbleFreqHigh => _rumbleFreqHigh,
            UxrHapticClipType.RumbleFreqVeryHigh => _rumbleFreqVeryHigh,
            UxrHapticClipType.Click => _click,
            UxrHapticClipType.Shot => _shot,
            UxrHapticClipType.ShotBig => _shotBig,
            UxrHapticClipType.ShotBigger => _shotBigger,
            UxrHapticClipType.Slide => _slide,
            UxrHapticClipType.Explosion => _explosion,
            _ => null,
        };

        /// <summary>Гул Rumble*: в SDK это ровная вибрация на срок запроса, а не разовый рисунок.</summary>
        public static bool IsRumble(UxrHapticClipType type) =>
            type is UxrHapticClipType.RumbleFreqVeryLow or UxrHapticClipType.RumbleFreqLow or UxrHapticClipType.RumbleFreqNormal
                or UxrHapticClipType.RumbleFreqHigh or UxrHapticClipType.RumbleFreqVeryHigh;

        /// <summary>Выстрел: эти типы гасятся, если отдачу играет само оружие (<see cref="IWeaponRecoilHapticsOwner" />).</summary>
        public static bool IsShot(UxrHapticClipType type) =>
            type is UxrHapticClipType.Shot or UxrHapticClipType.ShotBig or UxrHapticClipType.ShotBigger;
    }
}
