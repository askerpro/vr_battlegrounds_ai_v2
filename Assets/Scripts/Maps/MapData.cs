using UnityEngine;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Данные об игровой карте.
    /// Создать: правая кнопка в Project ? Create ? VrBattlegrounds ? Map Data.
    /// Один asset на карту — добавить в список MapRegistry.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MapData_",
        menuName  = "VR Battlegrounds/Map Data")]
    public class MapData : ScriptableObject
    {
        [Header("Основное")]
        [Tooltip("Отображаемое название карты в меню.")]
        public string displayName = "";

        [Tooltip("Имя сцены Unity (должно совпадать с именем в Build Settings).")]
        public string sceneName = "";

        [Tooltip("Назначение карты для bootstrap. Legacy debugOnly пока используется прежними редакторскими проверками.")]
        public Runtime.MapRunKind kind = Runtime.MapRunKind.Combat;

        [Tooltip("Явные исключения стенда Debug; на Combat/Lobby запрещены.")]
        public Runtime.MapDebugExemptions debugExemptions = Runtime.MapDebugExemptions.None;

        [Header("Визуал")]
        [Tooltip("Превью-скриншот карты для меню выбора.")]
        public Sprite preview;

        [Tooltip("Краткое описание карты (опционально).")]
        [TextArea(2, 4)]
        public string description = "";

        [Header("Параметры арены")]
        [Tooltip("Физические размеры игровой зоны в метрах (X = ширина, Z = глубина).")]
        public Vector2 arenaSizeMeters = new Vector2(10f, 10f);

        [Tooltip("Максимальное количество игроков на карте.")]
        [Min(2)]
        public int maxPlayers = 10;

        [Header("Арсенал")]
        [Tooltip("Ассортимент станций этой карты. Применяется при следующей загрузке сцены.")]
        public VrBattlegrounds.Arsenal.ArsenalPreset arsenalPreset;

        [Header("Игровые Режимы")]
        [Tooltip("Режимы, совместимые с картой. Первая разминка в списке — режим, с которого карта " +
                 "стартует; первый режим матча — тот, что запустится, если выбранный администратором " +
                 "с картой несовместим. Лобби — только разминка. Пусто — годится любой режим реестра.")]
        public GameModeData[] supportedModes;

        [Header("Назначение")]
        [Tooltip("Стенд для отладки, не для игры (TestMap3 — стенд блоков). Грузится обычным выбором карты, " +
                 "но правила боевой карты (MapPrinciplesTests, отчёт о принципах) к нему не применяются.")]
        public bool debugOnly;
    }
}
