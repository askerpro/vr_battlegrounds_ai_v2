using UnityEngine;
using Mirror;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Экран управления матчем на карте. Доступен только для Host/Server.
    /// </summary>
    public class MenuMatchManager : MenuScreen
    {
        /// <summary>
        /// «Начать матч»: карта из разминки переключается на выбранный режим на месте,
        /// без перезагрузки сцены (<see cref="GameplayManager.StartMatch"/>).
        /// </summary>
        public void OnStartMatchPressed()
        {
            if (!NetworkServer.active) return;
            GameplayManager.Instance?.StartMatch();
        }

        /// <summary>
        /// «Стоп / Лобби»: серия заканчивается досрочно, все возвращаются в лобби
        /// (<see cref="MatchSeries.ServerEnd"/>). Без серии — просто лобби.
        /// </summary>
        public void OnStopMatchPressed()
        {
            if (!NetworkServer.active) return;

            if (MatchSeries.Instance != null)
            {
                MatchSeries.Instance.ServerEnd();
                return;
            }

            string lobby = SessionManager.Instance != null && SessionManager.Instance.MapRegistry != null
                ? SessionManager.Instance.MapRegistry.LobbyScene
                : "Lobby";
            MapManager.Instance?.LoadMap(lobby);
        }
    }
}
