using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Экран выбора команды для игрока.
    /// Перенесен из устаревшего PlayerMenuController в новую архитектуру MenuScreen.
    /// </summary>
    public class MenuTeamSelection : MenuScreen
    {
        private PlayerController _localPlayer;

        public override void Show()
        {
            base.Show();
            CacheLocalPlayer();
        }

        private void CacheLocalPlayer()
        {
            if (_localPlayer == null && NetworkClient.localPlayer != null)
            {
                _localPlayer = NetworkClient.localPlayer.GetComponent<PlayerController>();
            }
        }

        /// <summary>
        /// Запрос на смену команды по teamIndex.
        /// Вызывается кнопкой UI (UnityEvent).
        /// </summary>
        public void OnSelectTeamPressed(int teamIndex)
        {
            TeamData team = TeamRegistry.Instance?.GetByIndex(teamIndex);
            if (team == null)
            {
                Debug.LogWarning($"[MenuTeamSelection] Команда с teamIndex={teamIndex} не найдена в TeamRegistry");
                return;
            }
            RequestTeam(team);
        }

        private void RequestTeam(TeamData team)
        {
            CacheLocalPlayer();

            if (_localPlayer == null)
            {
                Debug.LogWarning("[MenuTeamSelection] Локальный PlayerController не найден. Невозможно отправить запрос на сервер.");
                return;
            }

            // TODO: реализовать CmdRequestTeam в PlayerController, если его еще нет
            // _localPlayer.CmdRequestTeam(team.teamIndex);
            Debug.Log($"[MenuTeamSelection] Запрошена смена команды на: {team.name} (Index: {team.teamIndex})");
        }
    }
}
