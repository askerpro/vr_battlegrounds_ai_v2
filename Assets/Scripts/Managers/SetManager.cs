using Mirror;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Управляет сетом: смена сторон, счёт сетов.
    /// Выполняется только на сервере.
    /// </summary>
    public class SetManager : NetworkBehaviour
    {
        [SyncVar] public Team currentTerrorists;
        [SyncVar] public Team currentSpecialForces;

        private RoundManager _roundManager;

        [Server]
        public void StartSet(Team terrorists, Team specialForces)
        {
            currentTerrorists = terrorists;
            currentSpecialForces = specialForces;
            _roundManager = GetComponent<RoundManager>();
            _roundManager.StartRound();
        }

        [Server]
        public void OnRoundFinished(Team winner)
        {
            // TODO: логика завершения сета и смены команд
        }

        /// <summary>Меняет команды местами после завершения сета.</summary>
        [Server]
        public void SwapTeams()
        {
            (currentTerrorists, currentSpecialForces) = (currentSpecialForces, currentTerrorists);
        }
    }
}