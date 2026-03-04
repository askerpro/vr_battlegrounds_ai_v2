using Mirror;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Управляет матчем: 5 карт, счёт команд по сетам, победитель.
    /// Выполняется только на сервере (Host = администратор арены).
    /// </summary>
    public class MatchManager : NetworkBehaviour
    {
        public static MatchManager Instance { get; private set; }

        [SyncVar] public int terroristsScore;
        [SyncVar] public int specialForcesScore;

        private SetManager _setManager;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        [Server]
        public void StartMatch()
        {
            terroristsScore = 0;
            specialForcesScore = 0;
            _setManager = GetComponent<SetManager>();
            _setManager.StartSet(Team.Terrorists, Team.SpecialForces);
        }

        [Server]
        public void OnSetFinished(Team winner)
        {
            if (winner == Team.Terrorists) terroristsScore++;
            else if (winner == Team.SpecialForces) specialForcesScore++;

            // TODO: проверить 10 сетов и определить победителя матча
        }
    }
}