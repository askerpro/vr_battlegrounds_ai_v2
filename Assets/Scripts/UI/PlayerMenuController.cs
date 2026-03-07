using Mirror;
using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.UI
{
    /// <summary>
    /// Меню игрока. Доступно всем подключённым клиентам.
    /// Позволяет выбрать команду и запустить калибровку VR-шлема.
    ///
    /// Выбор команды отправляется на сервер через Command (CmdRequestTeam).
    /// Финальное назначение команды остаётся за сервером (GameNetworkManager).
    /// </summary>
    public class PlayerMenuController : MonoBehaviour
    {
        [Header("Зависимости")]
        [SerializeField] private VrCalibrationController _calibration;

        private PlayerController _localPlayer;

        private void Start()
        {
            // Ищем PlayerController локального игрока через UxrAvatar или NetworkClient
            // Заполняется после полной инициализации сети
            // TODO: подписаться на событие LocalAvatarStarted для надёжной инициализации
        }

        /// <summary>
        /// Запрос на смену команды на "Террористы".
        /// Вызывается кнопкой UI.
        /// </summary>
        public void OnSelectTerroristsPressed()
        {
            RequestTeam(Team.Terrorists);
        }

        /// <summary>
        /// Запрос на смену команды на "Спецназ".
        /// Вызывается кнопкой UI.
        /// </summary>
        public void OnSelectSpecialForcesPressed()
        {
            RequestTeam(Team.SpecialForces);
        }

        /// <summary>
        /// Запускает процедуру калибровки VR-шлема.
        /// Вызывается кнопкой UI.
        /// </summary>
        public void OnCalibratePressed()
        {
            _calibration?.Calibrate();
        }

        /// <summary>
        /// Отправляет запрос на смену команды.
        /// Сервер принимает решение — принять или отклонить запрос.
        /// </summary>
        private void RequestTeam(Team team)
        {
            if (_localPlayer == null)
            {
                Debug.LogWarning("[PlayerMenuController] Локальный PlayerController не найден");
                return;
            }

            // TODO: реализовать CmdRequestTeam в PlayerController
            // _localPlayer.CmdRequestTeam(team);
        }
    }
}
