using UnityEngine;
using Mirror;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Управляет раундом: FSM состояний, обратный отсчёт, таймер раунда.
    /// Выполняется только на сервере (SetManager создаёт и запускает раунды).
    /// </summary>
    public class RoundManager : NetworkBehaviour
    {
        /// <summary>Уведомляет SetManager о завершении раунда. Null = ничья.</summary>
        public event System.Action<TeamData> RoundEnded;

        [Header("Настройки раунда")]
        [SerializeField] private float _countdownDuration = 3f;
        [SerializeField] private float _roundDuration = 90f;

        [SyncVar] private RoundState _roundState = RoundState.Ended;
        [SyncVar] private float _countdownTimer;
        [SyncVar] private float _roundTimer;

        private GameMode _activeGameMode;

        /// <summary>Текущее состояние раунда (синхронизировано на клиентах).</summary>
        public RoundState State => _roundState;

        /// <summary>Оставшееся время раунда в секундах.</summary>
        public float RoundTimeRemaining => Mathf.Max(0f, _roundDuration - _roundTimer);

        /// <summary>Оставшееся время обратного отсчёта в секундах.</summary>
        public float CountdownTimeRemaining => Mathf.Max(0f, _countdownDuration - _countdownTimer);

        [Server]
        public void StartRound(GameMode gameMode)
        {
            _activeGameMode = gameMode;
            _countdownTimer = 0f;
            _roundTimer = 0f;
            SetState(RoundState.Countdown);
            RpcOnRoundStarted();
            GameLog.Info(GameSettings.Instance.LogLevelMatch, "[RoundManager] Раунд начат, запущен обратный отсчёт");
        }

        [Server]
        public void EndRound(TeamData winner)
        {
            if (_roundState == RoundState.Ended)
                return;

            _activeGameMode?.OnRoundEnd();
            SetState(RoundState.Ended);
            string winnerName = winner != null ? winner.displayName : "ничья";
            RpcOnRoundEnded(winnerName);
            RoundEnded?.Invoke(winner);
            GameLog.Info(GameSettings.Instance.LogLevelMatch, $"[RoundManager] Раунд завершён, победитель: {winnerName}");
        }

        /// <summary>
        /// Вызывается PlayerController при гибели игрока.
        /// Делегирует проверку условий победы активному GameMode.
        /// </summary>
        [Server]
        public void OnPlayerDied(PlayerController player)
        {
            if (_roundState != RoundState.Active)
                return;

            GameLog.Verbose(GameSettings.Instance.LogLevelMatch, $"[RoundManager] Игрок {player.name} погиб — проверяем условие победы");

            TeamData winner = _activeGameMode?.CheckWinCondition();

            // null при активном режиме = раунд продолжается; null без режима = сразу ничья
            bool roundOver = _activeGameMode == null || winner != null || IsAllTeamsDead();

            if (roundOver)
                EndRound(winner);
        }

        private void Update()
        {
            if (!isServer)
                return;

            switch (_roundState)
            {
                case RoundState.Countdown:
                    UpdateCountdown();
                    break;
                case RoundState.Active:
                    UpdateRoundTimer();
                    break;
            }
        }

        [Server]
        private void UpdateCountdown()
        {
            _countdownTimer += Time.deltaTime;

            if (_countdownTimer >= _countdownDuration)
            {
                _countdownTimer = _countdownDuration;
                SetState(RoundState.Active);
                GameLog.Info(GameSettings.Instance.LogLevelMatch, "[RoundManager] Обратный отсчёт завершён — раунд активен");
            }
        }

        [Server]
        private void UpdateRoundTimer()
        {
            _roundTimer += Time.deltaTime;

            if (_roundTimer >= _roundDuration)
            {
                GameLog.Info(GameSettings.Instance.LogLevelMatch, "[RoundManager] Время раунда истекло — ничья");
                EndRound(null);
            }
        }

        /// <summary>Все команды активного режима мертвы — раунд должен завершиться ничьей.</summary>
        [Server]
        private bool IsAllTeamsDead()
        {
            if (_activeGameMode == null)
                return false;

            PlayersManager pm = PlayersManager.Instance;
            if (pm == null)
                return false;

            foreach (TeamData team in _activeGameMode.Teams)
            {
                if (team == null)
                    continue;

                foreach (PlayerController _ in pm.GetAlivePlayers(team))
                    return false;
            }
            return true;
        }

        [Server]
        private void SetState(RoundState newState)
        {
            _roundState = newState;
        }

        [ClientRpc]
        private void RpcOnRoundStarted()
        {
            GameLog.Info(GameSettings.Instance.LogLevelMatch, "[RoundManager] Раунд начат (клиент)");
        }

        [ClientRpc]
        private void RpcOnRoundEnded(string winnerName)
        {
            GameLog.Info(GameSettings.Instance.LogLevelMatch, $"[RoundManager] Раунд завершён (клиент), победитель: {winnerName}");
        }
    }

    /// <summary>Состояния раунда.</summary>
    public enum RoundState
    {
        Countdown,
        Active,
        Ended
    }
}
