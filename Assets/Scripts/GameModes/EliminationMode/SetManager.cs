using Mirror;
using System;
using System.Collections.Generic;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.GameModes
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

        private Dictionary<int, int> _teamRoundScores = new Dictionary<int, int>();

        private EliminationMode _eliminationMode;
        private float _countdownDuration;
        private float _roundDuration;

        public IReadOnlyDictionary<int, int> TeamRoundScores => _teamRoundScores;

        public SetManager(RoundManager roundManager)
        {
            _roundManager = roundManager;
        }

        public void StartSet(TeamData[] teams, EliminationMode mode,
                             int roundsPerSet, float countdownDuration, float roundDuration)
        {
            _eliminationMode = mode;
            _roundsPerSet = roundsPerSet;
            _countdownDuration = countdownDuration;
            _roundDuration = roundDuration;
            _currentRound = 0;
            
            _teamRoundScores.Clear();
            foreach (var t in teams)
            {
                if (t != null) _teamRoundScores[t.teamIndex] = 0;
            }

            _roundManager.RoundEnded += OnRoundEnded;

            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[SetManager] Сет начат, раундов: {_roundsPerSet}");
            
            StartNextRound();
        }

        private void StartNextRound()
        {
            _currentRound++;
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[SetManager] Раунд {_currentRound}/{_roundsPerSet}");
            _eliminationMode.ServerBeginRound(_currentRound);
            _roundManager.StartRound(_eliminationMode, _countdownDuration, _roundDuration);
        }

        private void OnRoundEnded(TeamData winner)
        {
            _roundManager.RoundEnded -= OnRoundEnded;

            if (winner != null && _teamRoundScores.ContainsKey(winner.teamIndex))
            {
                _teamRoundScores[winner.teamIndex]++;
            }

            int roundsToWin = _roundsPerSet / 2 + 1;
            TeamData setWinner = null;
            int highestRounds = 0;
            bool isTie = false;

            foreach (var kvp in _teamRoundScores)
            {
                if (kvp.Value > highestRounds)
                {
                    highestRounds = kvp.Value;
                    setWinner = TeamRegistry.Instance.GetByIndex(kvp.Key);
                    isTie = false;
                }
                else if (kvp.Value == highestRounds)
                {
                    isTie = true;
                }

                if (kvp.Value >= roundsToWin)
                {
                    setWinner = TeamRegistry.Instance.GetByIndex(kvp.Key);
                    isTie = false;
                    break;
                }
            }

            if (isTie) setWinner = null;

            if (highestRounds >= roundsToWin || _currentRound >= _roundsPerSet)
            {
                FinishSet(setWinner);
            }
            else
            {
                _roundManager.RoundEnded += OnRoundEnded;
                // Именно собственный StartNextRound(), а не одноимённый метод RoundManager:
                // он инкрементирует _currentRound, зовёт ServerBeginRound и сам зовёт
                // _roundManager.StartRound(...). Через RoundManager счётчик раундов не растёт,
                // и сет никогда не заканчивается по исчерпанию раундов.
                StartNextRound();
            }
        }

        private void FinishSet(TeamData winner)
        {
            string winnerName = winner != null ? winner.displayName : "ничья";
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[SetManager] Сет завершён, победитель: {winnerName}");
            
            _eliminationMode.RpcOnSetEnded(winner != null ? winner.teamIndex : -1);

            SetEnded?.Invoke(winner);
        }

        /// <summary>Смена сторон (опционально для будущих реализаций N-команд).</summary>
        public void SwapTeams()
        {
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[SetManager] Смена сторон вызвана, но физическая логика смены спавнов пока не реализована.");
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
