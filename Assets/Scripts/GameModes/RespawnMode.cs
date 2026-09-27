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
    /// Режим "Возрождение": один длинный матч без раундов и сетов.
    /// Игроки возрождаются неограниченно. Побеждает команда с наибольшим числом фрагов.
    /// Матч завершается по истечении таймера.
    /// </summary>
    public class RespawnMode : GameMode
    {
        [Header("Настройки")]
        [Tooltip("Длительность матча в секундах.")]
        [SerializeField] private float _matchDuration = 300f;
        [SyncVar] private float _timeRemaining;
        [SyncVar] private bool _matchActive;

        /// <summary>Оставшееся время матча.</summary>
        public float TimeRemaining => _timeRemaining;

        // ── Реализация GameMode ──────────────────────────────────────────────

        public override bool CanRespawn() => true;

        protected override bool CanStartGameplay()
        {
            // Ожидаем, пока на сервере появится хотя бы 1 игрок, чтобы запустить таймер,
            // и пока у каждого не будет команды режима (этап Б: выбор на карте).
            return PlayersManager.Instance != null && PlayersManager.Instance.Sessions.Count > 0
                   && AllPlayersHaveModeTeam();
        }

        /// <summary>Матч идёт — сам игрок команду больше не меняет, только админ.</summary>
        public override bool TeamChoiceLocked => _matchActive;

        [Server]
        protected override void StartGameplay()
        {
            _timeRemaining = _matchDuration;
            _matchActive = true;

            string teamsStr = string.Join(", ", Teams.Select(t => t != null ? t.displayName : "null"));
            GameLog.Match.Info(
                $"[RespawnMode] Матч начат: {teamsStr}, время: {_matchDuration}с");
            
            RpcOnMatchStarted();
        }

        [Server]
        public override void StopGameplay()
        {
            _matchActive = false;
            GameLog.Match.Info(
                $"[RespawnMode] Матч остановлен.");
        }

        // ── Внутренняя логика ────────────────────────────────────────────────

        private void Update()
        {
            if (!isServer || !_matchActive) return;

            _timeRemaining -= Time.deltaTime;

            if (_timeRemaining <= 0f)
            {
                _timeRemaining = 0f;
                _matchActive = false;
                EndByTimer();
            }
        }

        /// <summary>
        /// Вызывается PlayerController при гибели игрока.
        /// Начисляет фраг команде убийцы.
        /// </summary>
        [Server]
        public void OnPlayerKilled(PlayerController victim, PlayerController killer)
        {
            if (!_matchActive || killer == null) return;

            TeamData killerTeam = TeamRegistry.Instance?.GetByIndex(killer.TeamIndex);
            if (killerTeam == null) return;

            if (victim != null && victim.TeamIndex == killer.TeamIndex) return;

            if (_teamStates.TryGetValue(killerTeam.teamIndex, out TeamRuntimeData killerState))
            {
                killerState.AddScore(1);
            }

            GameLog.Match.Verbose(
                $"[RespawnMode] Фраг: {killer.name} ({killerTeam.displayName}).");
        }

        [Server]
        private void EndByTimer()
        {
            TeamData winner = null;
            int maxFrags = -1;
            bool isTie = false;

            foreach (var state in _teamStates.Values)
            {
                if (state.Score > maxFrags)
                {
                    maxFrags = state.Score;
                    winner = state.Team;
                    isTie = false;
                }
                else if (state.Score == maxFrags)
                {
                    isTie = true;
                }
            }

            if (isTie) winner = null;

            string winnerName = winner != null ? winner.displayName : "ничья";
            GameLog.Match.Info(
                $"[RespawnMode] Таймер истёк. Победитель: {winnerName}. Макс. фрагов: {maxFrags}");

            RaiseGameplayEnded(winner);
        }
    }
}

