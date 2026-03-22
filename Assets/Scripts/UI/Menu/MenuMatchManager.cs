using UnityEngine;
using Mirror;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Экран управления матчем (Start/Stop).
    /// Доступен только для Host/Server.
    /// </summary>
    public class MenuMatchManager : MenuScreen
    {
        /// <summary>
        /// Запускает матч на текущей карте.
        /// Вызывается кнопкой "Старт матча" в UI.
        /// </summary>
        public void OnStartMatchPressed()
        {
            if (!NetworkServer.active) return;
            GameplayManager.Instance?.StartGameplay();
        }

        /// <summary>
        /// Останавливает текущий матч и возвращает всех в Lobby.
        /// Вызывается кнопкой "Стоп / Выйти в лобби" в UI.
        /// </summary>
        public void OnStopMatchPressed()
        {
            if (!NetworkServer.active) return;
            GameplayManager.Instance?.StopGameplay();
            MapManager.Instance?.LoadMap("Lobby");
        }
    }
}
