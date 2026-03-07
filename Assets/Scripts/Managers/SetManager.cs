using Mirror;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Управляет сетом: проводит N раундов, считает очки, определяет победителя сета.
    /// Выполняется только на сервере.
    ///
    /// Команды, участвующие в сете, передаются из MatchManager (берутся из GameMode.Teams).
    /// Победитель определяется по количеству выигранных раундов.
    /// </summary>
    public class SetManager : NetworkBehaviour
    {
        /// <summary>Уведомляет MatchManager о завершении сета. Null = ничья.</summary>
        public event System.Action<TeamData> SetEnded;

        [Header("Настройки сета")]
        [SerializeField] private int _roundsPerSet = 5;

        // Текущие команды сета (индексы для SyncVar)
        [SyncVar] private int _teamAIndex;
        [SyncVar] private int _teamBIndex;

        [SyncVar] private int _teamARoundScore;
        [SyncVar] private int _teamBRoundScore;
        [SyncVar] private int _currentRound;

        private RoundManager _roundManager;
        private GameMode _activeGameMode;

        /// <summary>Команда A текущего сета (первая по порядку в GameMode.Teams).</summary>
        public TeamData TeamA => TeamRegistry.Instance?.GetByIndex(_teamAIndex);

        /// <summary>Команда B текущего сета (вторая по порядку в GameMode.Teams).</summary>
        public TeamData TeamB => TeamRegistry.Instance?.GetByIndex(_teamBIndex);

        /// <summary>Очки команды A в текущем сете (раунды).</summary>
        public int TeamARoundScore => _teamARoundScore;

        /// <summary>Очки команды B в текущем сете (раунды).</summary>
        public int TeamBRoundScore => _teamBRoundScore;

        [Server]
        public void StartSet(TeamData teamA, TeamData teamB, GameMode gameMode)
        {
            _teamAIndex = teamA != null ? teamA.teamIndex : 0;
            _teamBIndex = teamB != null ? teamB.teamIndex : 0;
            _activeGameMode = gameMode;
            _teamARoundScore = 0;
            _teamBRoundScore = 0;
            _currentRound = 0;

            _roundManager = GetComponent<RoundManager>();
            _roundManager.RoundEnded += OnRoundEnded;

            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[SetManager] Сет начат: {teamA} vs {teamB}, раундов: {_roundsPerSet}");

            StartNextRound();
        }

        [Server]
        private void StartNextRound()
        {
            _currentRound++;
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[SetManager] Раунд {_currentRound}/{_roundsPerSet}");
            _roundManager.StartRound(_activeGameMode);
        }

        [Server]
        private void OnRoundEnded(TeamData winner)
        {
            _roundManager.RoundEnded -= OnRoundEnded;

            if (winner != null)
            {
                if (winner.teamIndex == _teamAIndex)
                    _teamARoundScore++;
                else if (winner.teamIndex == _teamBIndex)
                    _teamBRoundScore++;
            }

            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[SetManager] Счёт раундов: {TeamA} {_teamARoundScore} : {_teamBRoundScore} {TeamB}");

            int roundsToWin = _roundsPerSet / 2 + 1;
            if (_teamARoundScore >= roundsToWin)
            {
                FinishSet(TeamA);
            }
            else if (_teamBRoundScore >= roundsToWin)
            {
                FinishSet(TeamB);
            }
            else if (_currentRound >= _roundsPerSet)
            {
                TeamData setWinner = _teamARoundScore > _teamBRoundScore ? TeamA
                    : _teamBRoundScore > _teamARoundScore ? TeamB
                    : null;
                FinishSet(setWinner);
            }
            else
            {
                _roundManager.RoundEnded += OnRoundEnded;
                StartNextRound();
            }
        }

        [Server]
        private void FinishSet(TeamData winner)
        {
            string winnerName = winner != null ? winner.displayName : "ничья";
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[SetManager] Сет завершён, победитель: {winnerName}");
            SetEnded?.Invoke(winner);
        }

        /// <summary>Меняет команды A и B местами для следующего сета.</summary>
        [Server]
        public void SwapTeams()
        {
            (_teamAIndex, _teamBIndex) = (_teamBIndex, _teamAIndex);
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[SetManager] Смена сторон: A={TeamA}, B={TeamB}");
        }
    }
}