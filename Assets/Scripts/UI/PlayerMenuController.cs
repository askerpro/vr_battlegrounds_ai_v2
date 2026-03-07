using Mirror;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.UI
{
    /// <summary>
    /// Меню игрока. Доступно всем подключённым клиентам.
    /// Позволяет выбрать команду и запустить калибровку VR-шлема.
    ///
    /// Выбор команды отправляется на сервер через Command (CmdRequestTeam).
    /// Финальное назначение команды остаётся за сервером.
    ///
    /// Кнопки команд создаются динамически из TeamRegistry — добавление новой команды
    /// не требует изменений в этом классе.
    /// </summary>
    public class PlayerMenuController : MonoBehaviour
    {
        [Header("Зависимости")]
        [SerializeField] private VrCalibrationController _calibration;

        private PlayerController _localPlayer;

        private void Start()
        {
            // TODO: подписаться на событие LocalAvatarStarted для надёжной инициализации
        }

        /// <summary>
        /// Запрос на смену команды по teamIndex.
        /// Вызывается кнопкой UI — передаётся teamIndex из TeamData.
        /// </summary>
        public void OnSelectTeamPressed(int teamIndex)
        {
            TeamData team = TeamRegistry.Instance?.GetByIndex(teamIndex);
            if (team == null)
            {
                Debug.LogWarning($"[PlayerMenuController] Команда с teamIndex={teamIndex} не найдена в TeamRegistry");
                return;
            }
            RequestTeam(team);
        }

        /// <summary>
        /// Запускает процедуру калибровки VR-шлема.
        /// Вызывается кнопкой UI.
        /// </summary>
        public void OnCalibratePressed()
        {
            _calibration?.Calibrate();
        }

        private void RequestTeam(TeamData team)
        {
            if (_localPlayer == null)
            {
                Debug.LogWarning("[PlayerMenuController] Локальный PlayerController не найден");
                return;
            }

            // TODO: реализовать CmdRequestTeam в PlayerController
            // _localPlayer.CmdRequestTeam(team.teamIndex);
        }
    }
}