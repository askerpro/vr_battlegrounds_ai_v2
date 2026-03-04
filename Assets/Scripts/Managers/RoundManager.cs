using Mirror;
using VrBattlegrounds.GameModes;

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
            // TODO: проверить готовность всех игроков
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