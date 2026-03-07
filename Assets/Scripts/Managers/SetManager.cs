using Mirror;
using System;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Чистая серверная логика сета: проводит N раундов, считает очки, определяет победителя.
    /// Не является MonoBehaviour — создаётся через new SetManager() из EliminationMode.
    ///
    /// Тик делегируется в RoundManager.Tick() из EliminationMode.Update().
    /// Сетевая синхронизация (SyncVar, ClientRpc) — в EliminationMode.
    /// </summary>
    public class SetManager
    {
        /// <summary>Срабатывает при завершении сета. Null = ничья.</summary>
        public event Action<TeamData> SetEnded;

        private readonly RoundManager _roundManager;

        private int _roundsPerSet;
        private int _currentRound;

        private int _teamAIndex;
        private int _teamBIndex;
        private int _teamARoundScore;
        private int _teamBRoundScore;

        private EliminationMode _eliminationMode;
        private float _countdownDuration;
        private float _roundDuration;

        public int TeamARoundScore => _teamARoundScore;
        public int TeamBRoundScore => _teamBRoundScore;

        public SetManager(RoundManager roundManager)
        {
            _roundManager = roundManager;
        }

        public void StartSet(TeamData teamA, TeamData teamB, EliminationMode mode,
                             int roundsPerSet, float countdownDuration, float roundDuration)
        {
            _teamAIndex = teamA != null ? teamA.teamIndex : 0;
            _teamBIndex = teamB != null ? teamB.teamIndex : 0;
            _eliminationMode = mode;
            _roundsPerSet = roundsPerSet;
            _countdownDuration = countdownDuration;
            _roundDuration = roundDuration;
            _teamARoundScore = 0;
            _teamBRoundScore = 0;
            _currentRound = 0;

            _roundManager.RoundEnded += OnRoundEnded;

            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[SetManager] Сет начат: {teamA} vs {teamB}, раундов: {_roundsPerSet}");

            StartNextRound();
        }

        private void StartNextRound()
        {
            _currentRound++;
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[SetManager] Раунд {_currentRound}/{_roundsPerSet}");
            _roundManager.StartRound(_eliminationMode, _countdownDuration, _roundDuration);
        }

        private void OnRoundEnded(TeamData winner)
        {
            _roundManager.RoundEnded -= OnRoundEnded;

            if (winner != null)
            {
                if (winner.teamIndex == _teamAIndex) _teamARoundScore++;
                else if (winner.teamIndex == _teamBIndex) _teamBRoundScore++;
            }

            TeamData teamA = TeamRegistry.Instance?.GetByIndex(_teamAIndex);
            TeamData teamB = TeamRegistry.Instance?.GetByIndex(_teamBIndex);
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[SetManager] Счёт раундов: {teamA} {_teamARoundScore} : {_teamBRoundScore} {teamB}");

            int roundsToWin = _roundsPerSet / 2 + 1;
            if (_teamARoundScore >= roundsToWin)
            {
                FinishSet(teamA);
            }
            else if (_teamBRoundScore >= roundsToWin)
            {
                FinishSet(teamB);
            }
            else if (_currentRound >= _roundsPerSet)
            {
                TeamData setWinner = _teamARoundScore > _teamBRoundScore ? teamA
                    : _teamBRoundScore > _teamARoundScore ? teamB
                    : null;
                FinishSet(setWinner);
            }
            else
            {
                _roundManager.RoundEnded += OnRoundEnded;
                _roundManager.StartNextRound(_eliminationMode);
            }
        }

        private void FinishSet(TeamData winner)
        {
            string winnerName = winner != null ? winner.displayName : "ничья";
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[SetManager] Сет завершён, победитель: {winnerName}");
            SetEnded?.Invoke(winner);
        }

        /// <summary>Меняет команды A и B местами для следующего сета (смена сторон).</summary>
        public void SwapTeams()
        {
            (_teamAIndex, _teamBIndex) = (_teamBIndex, _teamAIndex);
            TeamData a = TeamRegistry.Instance?.GetByIndex(_teamAIndex);
            TeamData b = TeamRegistry.Instance?.GetByIndex(_teamBIndex);
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[SetManager] Смена сторон: A={a}, B={b}");
        }

        /// <summary>Принудительно останавливает сет. Вызывается EliminationMode.StopMatch().</summary>
        public void ForceStop()
        {
            _roundManager.RoundEnded -= OnRoundEnded;
            _roundManager.ForceStop();
            GameLog.Info(GameSettings.Instance.LogLevelMatch, "[SetManager] Сет принудительно остановлен");
        }
    }
}
