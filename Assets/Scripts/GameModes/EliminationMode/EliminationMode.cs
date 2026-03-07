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

        // Счёт матча — синхронизируется клиентам для UI
        [SyncVar] private int _teamASetScore;
        [SyncVar] private int _teamBSetScore;

        // Состояние раунда — синхронизируется для UI (таймер, countdown)
        [SyncVar] private RoundState _roundState = RoundState.Ended;
        [SyncVar] private float _roundTimer;
        [SyncVar] private float _countdownTimer;

        // Серверные машины состояний — создаются при StartMatch, не требуют NetworkBehaviour
        private SetManager _setManager;
        private RoundManager _roundManager;

        // ── Публичные свойства для UI ────────────────────────────────────────

        public RoundState CurrentRoundState => _roundState;
        public float RoundTimeRemaining => Mathf.Max(0f, _roundDuration - _roundTimer);
        public float CountdownTimeRemaining => Mathf.Max(0f, _countdownDuration - _countdownTimer);

        // ── Реализация GameMode ──────────────────────────────────────────────

        public override bool CanRespawn() => false;

        public override int GetScore(TeamData team)
        {
            if (team == null || Teams.Length < 2) return 0;
            if (team.teamIndex == Teams[0].teamIndex) return _teamASetScore;
            if (team.teamIndex == Teams[1].teamIndex) return _teamBSetScore;
            return 0;
        }

        [Server]
        public override void StartMatch()
        {
            _teamASetScore = 0;
            _teamBSetScore = 0;

            // Создаём менеджеры как обычные C# объекты — без GameObject, без NetworkBehaviour
            _roundManager = new RoundManager();
            _setManager = new SetManager(_roundManager);
            _setManager.SetEnded += OnSetEnded;

            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[EliminationMode] Матч начат: {Teams[0]} vs {Teams[1]}, " +
                $"сетов: {_maxSets}, раундов в сете: {_roundsPerSet}");

            StartNextSet(swapSides: false);
        }

        [Server]
        public override void StopMatch()
        {
            if (_setManager != null)
            {
                _setManager.SetEnded -= OnSetEnded;
                _setManager.ForceStop();
            }
            _roundManager = null;
            _setManager = null;

            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[EliminationMode] Матч остановлен. Счёт сетов: {_teamASetScore}:{_teamBSetScore}");
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
            _setManager.StartSet(Teams[0], Teams[1], this, _roundsPerSet, _countdownDuration, _roundDuration);
        }

        [Server]
        private void OnSetEnded(TeamData winner)
        {
            _setManager.SetEnded -= OnSetEnded;

            if (winner != null)
            {
                if (winner.teamIndex == Teams[0].teamIndex) _teamASetScore++;
                else if (winner.teamIndex == Teams[1].teamIndex) _teamBSetScore++;
            }

            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[EliminationMode] Счёт сетов: {Teams[0]} {_teamASetScore} : {_teamBSetScore} {Teams[1]}");

            int setsToWin = _maxSets / 2 + 1;
            if (_teamASetScore >= setsToWin)
            {
                RaiseMatchEnded(Teams[0]);
            }
            else if (_teamBSetScore >= setsToWin)
            {
                RaiseMatchEnded(Teams[1]);
            }
            else
            {
                int setsPlayed = _teamASetScore + _teamBSetScore;
                if (setsPlayed >= _maxSets)
                {
                    TeamData matchWinner = _teamASetScore > _teamBSetScore ? Teams[0]
                        : _teamBSetScore > _teamASetScore ? Teams[1]
                        : null;
                    RaiseMatchEnded(matchWinner);
                }
                else
                {
                    StartNextSet(swapSides: true);
                }
            }
        }

        /// <summary>
        /// Вызывается из RoundManager при гибели игрока.
        /// Возвращает победителя раунда или null если раунд продолжается.
        /// </summary>
        public TeamData CheckRoundWinCondition()
        {
            PlayersManager pm = PlayersManager.Instance;
            if (pm == null) return null;

            TeamData lastAlive = null;
            int aliveTeamsCount = 0;

            foreach (TeamData team in Teams)
            {
                if (team == null) continue;
                if (pm.GetAlivePlayers(team).Any())
                {
                    lastAlive = team;
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
        }
    }
}

