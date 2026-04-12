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
    public class RoundManager
    {
        public event Action<TeamData> RoundEnded;

        private float _countdownDuration;
        private float _roundDuration;
        
        private float _stateTimer;
        private float _roundTimer;
        
        private TeamSpawnZone[] _spawnZones;
        private RoundState _roundState = RoundState.Setup;

        private EliminationMode _eliminationMode;

        // Configurations for new phases
        private const float SetupDuration = 1.0f;
        private const float ResolutionDuration = 3.0f;
        private const float ScoreboardDuration = 5.0f;

        public RoundState State => _roundState;
        public float RoundTimeRemaining => Math.Max(0f, _roundDuration - _roundTimer);
        
        public float CountdownTimeRemaining 
        {
            get 
            {
                if (_roundState == RoundState.Countdown) return Math.Max(0f, _countdownDuration - _stateTimer);
                if (_roundState == RoundState.Scoreboard) return Math.Max(0f, ScoreboardDuration - _stateTimer);
                if (_roundState == RoundState.Resolution) return Math.Max(0f, ResolutionDuration - _stateTimer);
                return 0f;
            }
        }

        public void StartRound(EliminationMode mode, float countdownDuration, float roundDuration)
        {
            _eliminationMode = mode;
            _countdownDuration = countdownDuration;
            _roundDuration = roundDuration;
            
            _stateTimer = 0f;
            _roundTimer = 0f;

            if (_spawnZones == null || _spawnZones.Length == 0)
                _spawnZones = UnityEngine.Object.FindObjectsByType<TeamSpawnZone>(FindObjectsSortMode.None);
            
            _roundState = RoundState.Setup;
            GameLog.Info(GameSettings.Instance.LogLevelMatch, "[RoundManager] Выполняем очистку и телепортацию (Setup phase)");
            
            _eliminationMode.PrepareNextRound();
        }

        public void StartNextRound(EliminationMode mode)
        {
            StartRound(mode, _countdownDuration, _roundDuration);
        }

        public void EndRound(TeamData winner)
        {
            if (_roundState == RoundState.Resolution || _roundState == RoundState.Scoreboard) return;
            
            _roundState = RoundState.Resolution;
            _stateTimer = 0f;
            
            string winnerName = winner != null ? winner.displayName : "ничья";
            GameLog.Info(GameSettings.Instance.LogLevelMatch, $"[RoundManager] Раунд математически завершён, фаза Resolution. Победитель: {winnerName}");
            
            _eliminationMode?.RpcOnRoundEnded(winner != null ? winner.teamIndex : -1);
            RoundEnded?.Invoke(winner);
        }

        public void OnPlayerDied(PlayerController player)
        {
            if (_roundState != RoundState.Combat) return;

            GameLog.Verbose(GameSettings.Instance.LogLevelMatch, $"[RoundManager] Игрок {player.name} погиб — проверяем условие победы");

            TeamData winner = _eliminationMode?.CheckRoundWinCondition();
            bool roundOver = _eliminationMode == null || winner != null || IsAllTeamsDead();
            if (roundOver) EndRound(winner);
        }

        public bool Tick(float deltaTime)
        {
            switch (_roundState)
            {
                case RoundState.Setup:
                    _stateTimer += deltaTime;
                    if (_stateTimer >= SetupDuration)
                    {
                        _stateTimer = 0f;
                        _roundState = RoundState.Equipment;
                        GameLog.Info(GameSettings.Instance.LogLevelMatch, "[RoundManager] Setup завершен. Фаза закупки (Equipment)");
                        return true;
                    }
                    return false;

                case RoundState.Equipment:
                    if (AreAllPlayersReady())
                    {
                        _roundState = RoundState.Countdown;
                        _stateTimer = 0f;
                        GameLog.Info(GameSettings.Instance.LogLevelMatch, "[RoundManager] Все условия оборудования выполнены. Countdown (FreezeTime) запущен");
                        return true;
                    }
                    return false;

                case RoundState.Countdown:
                    _stateTimer += deltaTime;
                    if (_stateTimer >= _countdownDuration)
                    {
                        _stateTimer = 0f;
                        _roundState = RoundState.Combat;
                        GameLog.Info(GameSettings.Instance.LogLevelMatch, "[RoundManager] Countdown завершен. Бой начался (Combat)!");
                        return true;
                    }
                    return false;

                case RoundState.Combat:
                    _roundTimer += deltaTime;
                    if (_roundTimer >= _roundDuration)
                    {
                        GameLog.Info(GameSettings.Instance.LogLevelMatch, "[RoundManager] Время раунда истекло — ничья");
                        EndRound(null);
                        return true;
                    }
                    return false;

                case RoundState.Resolution:
                    _stateTimer += deltaTime;
                    if (_stateTimer >= ResolutionDuration)
                    {
                        _stateTimer = 0f;
                        _roundState = RoundState.Scoreboard;
                        GameLog.Info(GameSettings.Instance.LogLevelMatch, "[RoundManager] Resolution завершено. Scoreboard");
                        return true;
                    }
                    return false;

                case RoundState.Scoreboard:
                    _stateTimer += deltaTime;
                    if (_stateTimer >= ScoreboardDuration)
                    {
                        GameLog.Info(GameSettings.Instance.LogLevelMatch, "[RoundManager] Цикл завершен. Начинаем следующий раунд.");
                        StartNextRound(_eliminationMode);
                        return true; // We changed state back to Setup
                    }
                    return false;

                default:
                    return false;
            }
        }

        public void ForceStop()
        {
            _roundState = RoundState.Resolution;
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
            if (_eliminationMode != null)
            {
                int totalAlive = 0;
                int falseReasonCount = 0;
                foreach (var state in _eliminationMode.TeamStates.Values)
                {
                    // Ожидаем готовности только от тех, кто жив (участвует в текущем раунде)
                    var alivePlayers = PlayersManager.Instance.GetAlivePlayers(state.Team);
                    totalAlive += alivePlayers.Count();
                    foreach (var s in alivePlayers) 
                    {
                        if (!s.IsReadyForRound) falseReasonCount++;
                    }
                }
                
                // Если ещё никто не успел заспавниться, мы не готовы переходить к отсчёту.
                if (totalAlive == 0)
                {
                    GameLog.Verbose(GameSettings.Instance.LogLevelMatch, "[RoundManager DEBUG] AreAllPlayersReady: totalAlive == 0, waiting for players to spawn.");
                    return false;
                }
                
                if (falseReasonCount > 0)
                {
                    GameLog.Verbose(GameSettings.Instance.LogLevelMatch, $"[RoundManager DEBUG] AreAllPlayersReady: {falseReasonCount} players are NOT ready.");
                    return false;
                }
            }

            GameLog.Info(GameSettings.Instance.LogLevelMatch, "[RoundManager DEBUG] AreAllPlayersReady: True!");
            return true;
        }

#if UNITY_EDITOR
        public string GetPendingReadinessStatus()
        {
            if (_eliminationMode == null) return "No elimination mode";
            
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            bool allReady = true;

            foreach (var state in _eliminationMode.TeamStates.Values)
            {
                var alivePlayers = PlayersManager.Instance.GetAlivePlayers(state.Team);
                foreach (var s in alivePlayers)
                {
                    if (!s.IsReadyForRound)
                    {
                        allReady = false;
                        sb.AppendLine($"- {s.PlayerName}:");
                        if (!s.IsInSpawnZone) sb.AppendLine("   [!] Not in spawn zone");
                        if (!s.HasGrabbedDogTag) sb.AppendLine("   [!] Dog tag not grabbed");
                    }
                }
            }

            if (allReady) return "All Players Ready!";
            return sb.ToString();
        }
#endif
    }

    public enum RoundState 
    { 
        /// <summary>Техническая микрофаза. Очистка, телепортация.</summary>
        Setup, 
        /// <summary>Основное время закупки. Арсенал открыт.</summary>
        Equipment, 
        /// <summary>Все готовы. Идет таймер 3-5 секунд. Арсенал закрывается, патроны спавнятся.</summary>
        Countdown, 
        /// <summary>Активный бой. Урон включен.</summary>
        Combat, 
        /// <summary>Кто-то победил. Короткая пауза (SlowMo).</summary>
        Resolution, 
        /// <summary>Вывод итогов (Scoreboard) на несколько секунд.</summary>
        Scoreboard 
    }
}
