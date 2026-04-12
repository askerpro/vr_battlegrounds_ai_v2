using UnityEngine;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Реестр всех доступных карт.
    /// Создать: правая кнопка в Project ? Create ? VrBattlegrounds ? Map Registry.
    /// Один asset на проект — назначить в Inspector GameNetworkManager или AdminMenuController.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MapRegistry",
        menuName  = "VR Battlegrounds/Map Registry")]
    public class MapRegistry : ScriptableObject
    {
        [Tooltip("Все карты, доступные для выбора в меню администратора.")]
        public MapData[] maps = new MapData[0];

        /// <summary>Найти карту по имени сцены.</summary>
        public MapData GetBySceneName(string sceneName)
        {
            foreach (MapData map in maps)
            {
                if (map.sceneName == sceneName)
                    return map;
            }
            return null;
        }
    }
}
