using VrBattlegrounds;

using Mirror;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
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

        [SyncVar] private int _teamAFrags;
        [SyncVar] private int _teamBFrags;
        [SyncVar] private float _timeRemaining;
        [SyncVar] private bool _matchActive;

        /// <summary>Оставшееся время матча.</summary>
        public float TimeRemaining => _timeRemaining;

        // ── Реализация GameMode ──────────────────────────────────────────────

        public override bool CanRespawn() => true;

        public override int GetScore(TeamData team)
        {
            if (team == null || Teams.Length < 2) return 0;
            if (team.teamIndex == Teams[0].teamIndex) return _teamAFrags;
            if (team.teamIndex == Teams[1].teamIndex) return _teamBFrags;
            return 0;
        }

        [Server]
        public override void StartMatch()
        {
            _teamAFrags = 0;
            _teamBFrags = 0;
            _timeRemaining = _matchDuration;
            _matchActive = true;

            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[RespawnMode] Матч начат: {Teams[0]} vs {Teams[1]}, время: {_matchDuration}с");
        }

        [Server]
        public override void StopMatch()
        {
            _matchActive = false;
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[RespawnMode] Матч остановлен. Счёт фрагов: {_teamAFrags}:{_teamBFrags}");
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
            if (!_matchActive || killer == null || Teams.Length < 2) return;

            TeamData killerTeam = TeamRegistry.Instance?.GetByIndex(killer.TeamIndex);
            if (killerTeam == null) return;

            // Не считаем фраг за убийство союзника
            if (victim != null && victim.TeamIndex == killer.TeamIndex) return;

            if (killerTeam.teamIndex == Teams[0].teamIndex)
                _teamAFrags++;
            else if (killerTeam.teamIndex == Teams[1].teamIndex)
                _teamBFrags++;

            GameLog.Verbose(GameSettings.Instance.LogLevelMatch,
                $"[RespawnMode] Фраг: {killer.name} ({killerTeam.displayName}). Счёт: {_teamAFrags}:{_teamBFrags}");
        }

        [Server]
        private void EndByTimer()
        {
            TeamData winner = _teamAFrags > _teamBFrags ? Teams[0]
                : _teamBFrags > _teamAFrags ? Teams[1]
                : null;

            string winnerName = winner != null ? winner.displayName : "ничья";
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[RespawnMode] Таймер истёк. Победитель: {winnerName}. Счёт: {_teamAFrags}:{_teamBFrags}");

            RaiseMatchEnded(winner);
        }
    }
}

