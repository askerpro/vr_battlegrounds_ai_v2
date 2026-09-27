using UnityEngine;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Реестр всех карт, включая лобби. Какие режимы допустимы на карте — у самой карты
    /// (<see cref="MapData.supportedModes"/>): у лобби только разминка, поэтому в выборе
    /// карт под режим матча оно не показывается.
    /// Создать: правая кнопка в Project → Create → VR Battlegrounds → Map Registry.
    /// Один asset на проект — назначить в Inspector SessionManager и MenuSessionSetup.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MapRegistry",
        menuName  = "VR Battlegrounds/Map Registry")]
    public class MapRegistry : ScriptableObject
    {
        [Tooltip("Все карты, включая лобби.")]
        public MapData[] maps = new MapData[0];

        [Tooltip("Карта-лобби: сюда серия матча возвращает всех после последней карты " +
                 "и по кнопке администратора «Стоп / Лобби». Должна быть и в списке maps.")]
        public MapData lobby;

        /// <summary>Имя сцены лобби; «Lobby», если карта-лобби не назначена.</summary>
        public string LobbyScene => lobby != null && !string.IsNullOrEmpty(lobby.sceneName) ? lobby.sceneName : "Lobby";

        /// <summary>Найти карту по имени сцены.</summary>
        public MapData GetBySceneName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;

            foreach (MapData map in maps)
            {
                if (map != null && map.sceneName == sceneName)
                    return map;
            }
            return null;
        }
    }
}
