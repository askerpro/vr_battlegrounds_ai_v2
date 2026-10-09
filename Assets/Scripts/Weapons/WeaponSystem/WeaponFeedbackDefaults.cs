using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>Категория отклика оружия: какой ассет дефолтов звука и вибрации у ствола (решение пользователя 2026-10-09).</summary>
    public enum WeaponFeedbackCategory { Rifle, Shotgun, Pistol }

    /// <summary>
    /// Дефолты звука, вибрации и вылета (патрон, гильза — этап ejection) одной категории оружия (автомат, дробовик, пистолет). Ствол (хост <see cref="WeaponSystem"/>)
    /// хранит ссылку на ассет своей категории, а в своих <see cref="WeaponAudioSet"/>/<see cref="WeaponHapticSet"/> — только
    /// оверрайды. Пустое поле ствола — дефолт категории. Подставляют исполнители звука и вибрации в момент проигрывания
    /// (<see cref="WeaponAudioSet.Resolve"/>, <see cref="WeaponHapticSet.Resolve"/>), поэтому правка ассета сразу меняет
    /// все стволы категории без пересборки префабов.
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

        public WeaponFeedbackCategory Category => _category;
        public WeaponAudioSet Audio => _audio;
        public WeaponHapticSet Haptics => _haptics;
        public WeaponEjectionSet Ejection => _ejection;
    }
}
