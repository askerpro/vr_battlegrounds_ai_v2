using UnityEngine;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Данные об игровой карте.
    /// Создать: правая кнопка в Project ? Create ? VrBattlegrounds ? Map Data.
    /// Один asset на карту — добавить в список MapRegistry.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MapData_",
        menuName  = "VrBattlegrounds/Map Data")]
    public class MapData : ScriptableObject
    {
        [Header("Основное")]
        [Tooltip("Отображаемое название карты в меню.")]
        public string displayName = "";

        [Tooltip("Имя сцены Unity (должно совпадать с именем в Build Settings).")]
        public string sceneName = "";

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
    }
}
