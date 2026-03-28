using UnityEngine;
using System;
using System.Linq;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Player;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Чистая серверная логика раунда: FSM состояний, обратный отсчёт, таймер.
    /// Не является MonoBehaviour — создаётся через new RoundManager() из EliminationMode.
    ///
    /// Тик обновляется вызовом Tick(deltaTime) из EliminationMode.Update().
    /// Сетевая синхронизация (SyncVar, ClientRpc) — в EliminationMode.
    /// </summary>
    public class RoundManager
    {
        /// <summary>Срабатывает при завершении раунда. Null = ничья.</summary>
        public event Action<TeamData> RoundEnded;

        private float _countdownDuration;
        private float _roundDuration;
        private float _countdownTimer;
        private float _roundTimer;
        private TeamSpawnZone[] _spawnZones;
        private RoundState _roundState = RoundState.Ended;

        private EliminationMode _eliminationMode;

        public RoundState State => _roundState;
        public float RoundTimeRemaining => Math.Max(0f, _roundDuration - _roundTimer);
        public float CountdownTimeRemaining => Math.Max(0f, _countdownDuration - _countdownTimer);

        public void StartRound(EliminationMode mode, float countdownDuration, float roundDuration)
        {
            _eliminationMode = mode;
            _countdownDuration = countdownDuration;
            _roundDuration = roundDuration;
            _countdownTimer = 0f;
            _roundTimer = 0f;
            _spawnZones = UnityEngine.Object.FindObjectsByType<TeamSpawnZone>(FindObjectsSortMode.None);
            
            _roundState = RoundState.WaitingForPlayers;
            GameLog.Info(GameSettings.Instance.LogLevelMatch, "[RoundManager] Ожидание готовности игроков для старта раунда (заход в spawn-зоны)");
            
            _eliminationMode.PrepareNextRound();
        }

        /// <summary>Продолжить сет — запустить следующий раунд с теми же настройками.</summary>
        public void StartNextRound(EliminationMode mode)
        {
            StartRound(mode, _countdownDuration, _roundDuration);
        }

        public void EndRound(TeamData winner)
        {
            if (_roundState == RoundState.Ended) return;
            _roundState = RoundState.Ended;
            string winnerName = winner != null ? winner.displayName : "ничья";
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[RoundManager] Раунд завершён, победитель: {winnerName}");
            
            _eliminationMode?.RpcOnRoundEnded(winner != null ? winner.teamIndex : -1);

            RoundEnded?.Invoke(winner);
        }

        /// <summary>
        /// Вызывается EliminationMode при гибели игрока.
        /// Проверяет условие победы и завершает раунд если нужно.
        /// </summary>
        public void OnPlayerDied(PlayerController player)
        {
            if (_roundState != RoundState.Active) return;

            GameLog.Verbose(GameSettings.Instance.LogLevelMatch,
                $"[RoundManager] Игрок {player.name} погиб — проверяем условие победы");

            TeamData winner = _eliminationMode?.CheckRoundWinCondition();
            bool roundOver = _eliminationMode == null || winner != null || IsAllTeamsDead();
            if (roundOver) EndRound(winner);
        }

        /// <summary>
        /// Тик логики раунда. Вызывается из EliminationMode.Update() только на сервере.
        /// Возвращает true если состояние изменилось (для синхронизации SyncVar в EliminationMode).
        /// </summary>
        public bool Tick(float deltaTime)
        {
            switch (_roundState)
            {
                case RoundState.WaitingForPlayers:
                    if (AreAllPlayersReady())
                    {
                        _roundState = RoundState.Countdown;
                        GameLog.Info(GameSettings.Instance.LogLevelMatch,
                            "[RoundManager] Все игроки в зонах — запущен обратный отсчёт");
                        return true;
                    }
                    return false;

                case RoundState.Countdown:
                    _countdownTimer += deltaTime;
                    if (_countdownTimer >= _countdownDuration)
                    {
                        _countdownTimer = _countdownDuration;
                        _roundState = RoundState.Active;
                        GameLog.Info(GameSettings.Instance.LogLevelMatch,
                            "[RoundManager] Обратный отсчёт завершён — раунд активен");
                        return true;
                    }
                    return false;

                case RoundState.Active:
                    _roundTimer += deltaTime;
                    if (_roundTimer >= _roundDuration)
                    {
                        GameLog.Info(GameSettings.Instance.LogLevelMatch,
                            "[RoundManager] Время раунда истекло — ничья");
                        EndRound(null);
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        public void ForceStop()
        {
            _roundState = RoundState.Ended;
            _eliminationMode = null;
            GameLog.Info(GameSettings.Instance.LogLevelMatch, "[RoundManager] Раунд принудительно остановлен");
        }

        private bool IsAllTeamsDead()
        {
            if (_eliminationMode == null) return false;
            foreach (var state in _eliminationMode.TeamStates.Values)
            {
                if (state.HasAlivePlayers()) return false;
            }
            return true;
        }

        private bool AreAllPlayersReady()
        {
            // 1. Проверяем что все игроки в своих spawn зонах (используя System.Linq)
            if (_spawnZones != null && _spawnZones.Length > 0)
            {
                if (!_spawnZones.All(zone => zone.AreAllTeamPlayersInZone()))
                    return false;
            }

            // 2. Проверяем что все игроки во всех командах живы
            if (_eliminationMode != null)
            {
                if (!_eliminationMode.TeamStates.Values.All(state => state.AreAllPlayersAlive()))
                    return false;
            }

            // 3. В будущем здесь могут быть другие проверки (например, выбор оружия)

            return true;
        }
    }

    public enum RoundState { WaitingForPlayers, Countdown, Active, Ended }
}
