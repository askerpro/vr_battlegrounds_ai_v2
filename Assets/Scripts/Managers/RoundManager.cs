using Mirror;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Управляет раундом: готовность игроков, старт, конец.
    /// Выполняется только на сервере.
    /// </summary>
    public class RoundManager : NetworkBehaviour
    {
        [SyncVar] public bool isRoundActive;

        private GameMode _activeGameMode;

        [Server]
        public void StartRound()
        {
            isRoundActive = true;
            RpcOnRoundStarted();
        }

        [Server]
        public void EndRound(Team winner)
        {
            isRoundActive = false;
            _activeGameMode?.OnRoundEnd();
            RpcOnRoundEnded(winner);
        }

        /// <summary>
        /// Вызывается PlayerController при гибели игрока.
        /// Делегирует проверку условий победы активному GameMode.
        /// </summary>
        [Server]
        public void OnPlayerDied(PlayerController player)
        {
            if (!isRoundActive)
                return;

            Team winner = _activeGameMode != null
                ? _activeGameMode.CheckWinCondition()
                : Team.None;

            if (winner != Team.None)
                EndRound(winner);
        }

        [ClientRpc]
        private void RpcOnRoundStarted()
        {
            // TODO: уведомить клиентов о старте раунда
        }

        [ClientRpc]
        private void RpcOnRoundEnded(Team winner)
        {
            // TODO: уведомить клиентов о завершении раунда
        }
    }
}