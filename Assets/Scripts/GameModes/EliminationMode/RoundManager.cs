using UnityEngine;
using System;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Player;
using VrBattlegrounds.Managers;

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
            _roundState = RoundState.Countdown;
            GameLog.Info(GameSettings.Instance.LogLevelMatch, "[RoundManager] Раунд начат, запущен обратный отсчёт");
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
            PlayersManager pm = PlayersManager.Instance;
            if (pm == null) return false;
            foreach (TeamData team in _eliminationMode.Teams)
            {
                if (team == null) continue;
                foreach (PlayerController _ in pm.GetAlivePlayers(team))
                    return false;
            }
            return true;
        }
    }

    public enum RoundState { Countdown, Active, Ended }
}
