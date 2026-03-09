using System;
using System.Linq;
using Mirror;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Режим "Ликвидация": матч → сеты → раунды.
    /// Побеждает команда, выигравшая больше сетов.
    ///
    /// SetManager и RoundManager — чистые C# классы (не NetworkBehaviour).
    /// EliminationMode создаёт их через new и управляет тиком в Update().
    /// Вся сетевая синхронизация (SyncVar, ClientRpc) — здесь.
    ///
    /// Не возрождает игроков между раундами.
    /// </summary>
    public class EliminationMode : GameMode
    {
        [Header("Настройки")]
        [Tooltip("Максимум сетов в матче (нечётное число рекомендуется).")]
        [SerializeField] private int _maxSets = 5;

        [Tooltip("Раундов в одном сете.")]
        [SerializeField] private int _roundsPerSet = 3;

        [Tooltip("Длительность обратного отсчёта перед раундом (сек).")]
        [SerializeField] private float _countdownDuration = 3f;

        [Tooltip("Максимальная длительность раунда (сек).")]
        [SerializeField] private float _roundDuration = 90f;

        // Счёт матча теперь синхронизируется через базовый класс GameMode

        // Состояние раунда — синхронизируется для UI (таймер, countdown)
        [SyncVar] private RoundState _roundState = RoundState.Ended;
        [SyncVar] private float _roundTimer;
        [SyncVar] private float _countdownTimer;

        // Серверные машины состояний — создаются при StartGameplay, не требуют NetworkBehaviour
        private SetManager _setManager;
        private RoundManager _roundManager;

        // ── Глобальные семантические события для UI (Клиент) ───────────────

        public static event Action<int> OnSetStartedLocal;
        public static event Action<TeamData> OnSetEndedLocal;
        public static event Action<int> OnRoundStartedLocal;
        public static event Action<TeamData> OnRoundEndedLocal;
        public static event Action<RoundState> OnRoundStateChangedLocal;

        // ── Публичные свойства для UI ────────────────────────────────────────

        public RoundState CurrentRoundState => _roundState;
        public float RoundTimeRemaining => Mathf.Max(0f, _roundDuration - _roundTimer);
        public float CountdownTimeRemaining => Mathf.Max(0f, _countdownDuration - _countdownTimer);

        // ── Реализация GameMode ──────────────────────────────────────────────

        public override bool CanRespawn() => false;

        [Server]
        public override void StartGameplay()
        {

            // Создаём менеджеры как обычные C# объекты — без GameObject, без NetworkBehaviour
            _roundManager = new RoundManager();
            _setManager = new SetManager(_roundManager);
            _setManager.SetEnded += OnSetEnded;

            string teamsStr = string.Join(" vs ", Teams.Select(t => t != null ? t.displayName : "null"));
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[EliminationMode] Матч начат: {teamsStr}, " +
                $"сетов: {_maxSets}, раундов в сете: {_roundsPerSet}");

            StartNextSet(swapSides: false);
        }

        [Server]
        public override void StopGameplay()
        {
            if (_setManager != null)
            {
                _setManager.SetEnded -= OnSetEnded;
                _setManager.ForceStop();
            }
            _roundManager = null;
            _setManager = null;

            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[EliminationMode] Матч остановлен.");
        }

        // ── Тик (делегирует в RoundManager) ─────────────────────────────────

        private void Update()
        {
            if (!isServer || _roundManager == null) return;

            // Тик логики — если состояние изменилось, синхронизируем SyncVar
            bool changed = _roundManager.Tick(Time.deltaTime);

            // Обновляем SyncVar каждый тик для плавного таймера на клиентах
            _roundState = _roundManager.State;
            _roundTimer = _roundDuration - _roundManager.RoundTimeRemaining;
            _countdownTimer = _countdownDuration - _roundManager.CountdownTimeRemaining;

            if (changed)
                RpcOnRoundStateChanged(_roundState);
        }

        // ── Внутренняя логика ────────────────────────────────────────────────

        [Server]
        private void StartNextSet(bool swapSides)
        {
            if (swapSides)
                _setManager.SwapTeams();

            _setManager.SetEnded += OnSetEnded;
            
            int currentSet = 1 + _teamStates.Values.Sum(s => s.Score);
            RpcOnSetStarted(currentSet);

            _setManager.StartSet(Teams, this, _roundsPerSet, _countdownDuration, _roundDuration);
        }

        [Server]
        private void OnSetEnded(TeamData winner)
        {
            _setManager.SetEnded -= OnSetEnded;

            if (winner != null && _teamStates.TryGetValue(winner.teamIndex, out TeamRuntimeData winnerState))
            {
                winnerState.AddScore(1);
            }

            int setsToWin = _maxSets / 2 + 1;
            TeamData matchWinner = null;
            int totalSetsPlayed = 0;
            int highestSets = 0;

            foreach (var state in _teamStates.Values)
            {
                totalSetsPlayed += state.Score;
                if (state.Score > highestSets)
                {
                    highestSets = state.Score;
                    matchWinner = state.Team;
                }
                else if (state.Score == highestSets)
                {
                    matchWinner = null; // tie
                }

                if (state.Score >= setsToWin)
                {
                    matchWinner = state.Team;
                    break;
                }
            }

            if (highestSets >= setsToWin || totalSetsPlayed >= _maxSets)
            {
                RaiseGameplayEnded(matchWinner);
            }
            else
            {
                StartNextSet(swapSides: true);
            }
        }

        /// <summary>
        /// Вызывается из RoundManager при гибели игрока.
        /// Возвращает победителя раунда или null если раунд продолжается.
        /// </summary>
        public TeamData CheckRoundWinCondition()
        {
            TeamData lastAlive = null;
            int aliveTeamsCount = 0;

            foreach (var state in TeamStates.Values)
            {
                if (state.HasAlivePlayers())
                {
                    lastAlive = state.Team;
                    aliveTeamsCount++;
                }
            }

            if (aliveTeamsCount > 1) return null;
            return aliveTeamsCount == 1 ? lastAlive : null;
        }

        /// <summary>
        /// Вызывается PlayerController при гибели игрока в этом режиме.
        /// </summary>
        [Server]
        public void OnPlayerDied(PlayerController player)
        {
            _roundManager?.OnPlayerDied(player);
        }

        [ClientRpc]
        private void RpcOnRoundStateChanged(RoundState newState)
        {
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[EliminationMode] Состояние раунда (клиент): {newState}");
            OnRoundStateChangedLocal?.Invoke(newState);
        }

        [ClientRpc]
        public void RpcOnSetStarted(int setNum)
        {
            OnSetStartedLocal?.Invoke(setNum);
        }

        [ClientRpc]
        public void RpcOnSetEnded(int winnerIndex)
        {
            TeamData winner = winnerIndex >= 0 ? TeamRegistry.Instance.GetByIndex(winnerIndex) : null;
            OnSetEndedLocal?.Invoke(winner);
        }

        [ClientRpc]
        public void RpcOnRoundStarted(int roundNum)
        {
            OnRoundStartedLocal?.Invoke(roundNum);
        }

        [ClientRpc]
        public void RpcOnRoundEnded(int winnerIndex)
        {
            TeamData winner = winnerIndex >= 0 ? TeamRegistry.Instance.GetByIndex(winnerIndex) : null;
            OnRoundEndedLocal?.Invoke(winner);
        }
    }
}

