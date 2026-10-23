using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Категория отклика оружия: какой ассет дефолтов звука, вибрации и вылета у ствола (решения пользователя 2026-10-09:
    /// автомат, дробовик, пистолет; затем снайперская винтовка). Значения сериализованы в ассетах — новые только в конец.
    /// </summary>
    public enum WeaponFeedbackCategory { Rifle, Shotgun, Pistol, Sniper }

    /// <summary>
    /// Дефолты звука, вибрации и вылета (патрон, гильза — этап ejection) одной категории оружия (автомат, дробовик, пистолет). Ствол (хост <see cref="WeaponSystem"/>)
    /// хранит ссылку на ассет своей категории, а в своих <see cref="WeaponAudioSet"/>/<see cref="WeaponHapticSet"/> — только
    /// оверрайды. Пустое поле ствола — дефолт категории. Подставляют исполнители звука и вибрации в момент проигрывания
    /// (<see cref="WeaponAudioSet.Resolve"/>, <see cref="WeaponHapticSet.Resolve"/>), поэтому правка ассета сразу меняет
    /// все стволы категории без пересборки префабов.
    ///
    /// <para>
    /// Звуковые дефолты вне машины (решение пользователя 2026-10-23: дефолты — данные в ассетах, не константы в коде):
    /// запасная пара гнезда магазина (<see cref="MagazineInsert"/>, <see cref="MagazineTakeOut"/>) — подставляет
    /// <c>AnchorSound</c> гнезда и окна приёма патрона, когда у них нет своего клипа; звуки касания вылета
    /// (<see cref="EjectaImpacts"/>) — <see cref="WeaponEjecta"/> без своих клипов. «Нет патронов» — <see cref="WeaponAudioSet.DryFire"/>:
    /// его играет исполнитель, а сборщик копирует клип отсюда в поле спуска SDK (щелчок упёртого ствола у <c>BarrelObstruction</c>).
    /// </para>
    ///
    /// <para>
    /// Сборщик <c>WeaponSystemAuthoring</c> дефолты в ствол не копирует: он проставляет категорию и ссылку и печатает,
    /// откуда берётся каждый сигнал. Ассеты лежат в <c>Assets/Data/WeaponSystem/</c>; начальные значения создаёт он же,
    /// существующий ассет не перезаписывает — дальше это данные дизайнера.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "VR Battlegrounds/Weapons/Feedback Defaults")]
    public sealed class WeaponFeedbackDefaults : ScriptableObject
    {
        [SerializeField] private WeaponFeedbackCategory _category;

        [Tooltip("Звуки механизма категории по сигналам машины.")]
        [SerializeField] private WeaponAudioSet _audio = new WeaponAudioSet();

        [Tooltip("Вибрации категории по сигналам машины.")]
        [SerializeField] private WeaponHapticSet _haptics = new WeaponHapticSet();

        [Tooltip("Вылет категории: живой патрон при извлечении и гильза при выстреле (этап ejection).")]
        [SerializeField] private WeaponEjectionSet _ejection = new WeaponEjectionSet();

        [Tooltip("Запасной звук вставки магазина в гнездо (и патрона в окно приёма) ствола категории, если у гнезда нет своего " +
                 "клипа (нарезки пака). Пусто — дефолт роли гнезда и общий звук вставки MagazineAnchorSoundDefaults.")]
        [SerializeField] private AudioClip _magazineInsert;

        [Tooltip("Запасной звук снятия (выпадения) магазина из гнезда ствола категории, если у гнезда нет своего клипа. " +
                 "Пусто — дефолт роли гнезда из MagazineAnchorSoundDefaults; нет и его — снятие без звука.")]
        [SerializeField] private AudioClip _magazineTakeOut;

        [Tooltip("Звуки первого касания вылетевших патронов и гильз ствола категории (случайный). Свои клипы префаба вылета " +
                 "(WeaponEjecta) важнее. Пусто — касание без звука.")]
        [SerializeField] private AudioClip[] _ejectaImpacts = new AudioClip[0];

        public WeaponFeedbackCategory Category => _category;
        public WeaponAudioSet Audio => _audio;
        public WeaponHapticSet Haptics => _haptics;
        public WeaponEjectionSet Ejection => _ejection;

        /// <summary>Запасной звук вставки в гнездо магазина; null — у категории его нет. Подставляет <c>AnchorSound</c>.</summary>
        public AudioClip MagazineInsert => _magazineInsert;

        /// <summary>Запасной звук снятия магазина; null — у категории его нет. Подставляет <c>AnchorSound</c>.</summary>
        public AudioClip MagazineTakeOut => _magazineTakeOut;

        /// <summary>Звуки касания вылета категории (не null); подставляет <see cref="WeaponEjecta"/> без своих клипов.</summary>
        public AudioClip[] EjectaImpacts => _ejectaImpacts ?? System.Array.Empty<AudioClip>();
    }
}
