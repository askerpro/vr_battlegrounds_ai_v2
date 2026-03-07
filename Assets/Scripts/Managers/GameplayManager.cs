using System.Linq;
// Forced compilation trigger
using System;
using Mirror;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Тонкий оркестратор матча: инстанцирует префаб режима через NetworkServer.Spawn,
    /// передаёт ему команды и ждёт события GameMode.GameplayEnded.
    ///
    /// Вся логика матча (сеты, раунды, таймеры, счёт) живёт внутри конкретного GameMode.
    /// GameplayManager не знает о структуре режима — только Start/Stop и результат.
    /// </summary>
    public class GameplayManager : NetworkBehaviour
    {
        public static GameplayManager Instance { get; private set; }

        /// <summary>Матч завершён. Null = ничья.</summary>
        public event Action<TeamData> GameplayEnded;

        [SyncVar] private bool _gameplayActive;

        private GameMode _gameMode;
        private GameObject _gameModeInstance;

        public bool IsGameplayActive => _gameplayActive;

        // ── Отображение в Inspector (только чтение, обновляются каждый кадр) ──

        [Header("Состояние матча (только чтение)")]
        [Tooltip("Идёт ли матч прямо сейчас.")]
        [SerializeField] private bool _gameplayActiveDisplay;

        [Tooltip("Активный игровой режим.")]
        [SerializeField] private string _gameModeDisplay = "—";

        [Tooltip("Состояние раунда (только EliminationMode).")]
        [SerializeField] private string _roundStateDisplay = "—";

        [Tooltip("Оставшееся время раунда или обратного отсчёта (сек).")]
        [SerializeField] private float _timerDisplay;

        [Tooltip("Счёт команды A (сеты или фраги).")]
        [SerializeField] private int _scoreADisplay;

        [Tooltip("Счёт команды B.")]
        [SerializeField] private int _scoreBDisplay;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Update()
        {
            // Обновляем display-поля в реальном времени — видны в Inspector во время Play Mode
            _gameplayActiveDisplay = _gameplayActive;

            if (_gameMode == null)
            {
                _gameModeDisplay  = "—";
                _roundStateDisplay = "—";
                _timerDisplay     = 0f;
                _scoreADisplay    = 0;
                _scoreBDisplay    = 0;
                return;
            }

            _gameModeDisplay = _gameMode.GetType().Name;

            TeamData[] teams = _gameMode.Teams;
            _scoreADisplay = teams != null && teams.Length > 0 ? _gameMode.GetScore(teams[0]) : 0;
            _scoreBDisplay = teams != null && teams.Length > 1 ? _gameMode.GetScore(teams[1]) : 0;

            // Таймер и состояние раунда — специфичны для EliminationMode
            if (_gameMode is EliminationMode elimination)
            {
                switch (elimination.CurrentRoundState)
                {
                    case RoundState.Countdown:
                        _roundStateDisplay = "Countdown";
                        _timerDisplay      = elimination.CountdownTimeRemaining;
                        break;
                    case RoundState.Active:
                        _roundStateDisplay = "Active";
                        _timerDisplay      = elimination.RoundTimeRemaining;
                        break;
                    default:
                        _roundStateDisplay = "Ended";
                        _timerDisplay      = 0f;
                        break;
                }
            }
            else
            {
                _roundStateDisplay = "—";
                _timerDisplay      = 0f;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        [Server]
        public void StartGameplay()
        {
            if (_gameplayActive)
            {
                GameLog.Warning(GameSettings.Instance.LogLevelMatch, "[GameplayManager] Матч уже идёт");
                return;
            }

            GameModeData gameModeData = SessionManager.Instance != null
                ? SessionManager.Instance.SelectedGameModeData
                : null;

            if (SessionManager.Instance == null)
            {
                GameLog.Error("[GameplayManager] StartGameplay: SessionManager.Instance == null. " +
                              "Убедитесь что SessionManager добавлен на MirrorNetworkManager в сцене Offline.");
                return;
            }

            if (gameModeData == null)
            {
                GameLog.Error($"[GameplayManager] StartGameplay: режим не найден. " +
                              $"SelectedModeId='{SessionManager.Instance.SelectedModeId}', " +
                              $"SelectedMapScene='{SessionManager.Instance.SelectedMapScene}'.");
                return;
            }

            if (gameModeData.modePrefab == null)
            {
                GameLog.Error($"[GameplayManager] GameModeData '{gameModeData.modeId}' не содержит modePrefab");
                return;
            }

            if (gameModeData.teams == null || gameModeData.teams.Length < 2)
            {
                GameLog.Error($"[GameplayManager] GameModeData '{gameModeData.modeId}' содержит менее 2 команд");
                return;
            }

            // Инстанцируем и спавним через Mirror — клиенты увидят объект
            _gameModeInstance = Instantiate(gameModeData.modePrefab, transform);
            NetworkServer.Spawn(_gameModeInstance);

            _gameMode = _gameModeInstance.GetComponent<GameMode>();
            if (_gameMode == null)
            {
                GameLog.Error($"[GameplayManager] Префаб '{gameModeData.modeId}' не содержит компонент GameMode");
                NetworkServer.UnSpawn(_gameModeInstance);
                Destroy(_gameModeInstance);
                _gameModeInstance = null;
                return;
            }

            _gameMode.Initialize(gameModeData.teams);
            _gameMode.GameplayEnded += OnGameplayEnded;

            _gameplayActive = true;

            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[GameplayManager] Запуск режима: {gameModeData.modeId} ({gameModeData.displayName})");

            _gameMode.StartGameplay();
        }

        /// <summary>
        /// Принудительно останавливает матч без определения победителя.
        /// Используется администратором для возврата в Lobby.
        /// </summary>
        [Server]
        public void StopGameplay()
        {
            if (!_gameplayActive)
            {
                GameLog.Warning(GameSettings.Instance.LogLevelMatch, "[GameplayManager] StopGameplay: матч не активен");
                return;
            }

            _gameMode?.StopGameplay();
            CleanupGameMode();

            _gameplayActive = false;
            GameLog.Info(GameSettings.Instance.LogLevelMatch, "[GameplayManager] Матч остановлен администратором");
            RpcOnMatchStopped();
        }

        [Server]
        private void OnGameplayEnded(TeamData winner)
        {
            CleanupGameMode();
            _gameplayActive = false;

            string winnerName = winner != null ? winner.displayName : "ничья";
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[GameplayManager] Матч завершён, победитель: {winnerName}");

            GameplayEnded?.Invoke(winner);
            RpcOnGameplayEnded(winnerName);
        }

        [Server]
        private void CleanupGameMode()
        {
            if (_gameMode != null)
            {
                _gameMode.GameplayEnded -= OnGameplayEnded;
                _gameMode = null;
            }

            if (_gameModeInstance != null)
            {
                NetworkServer.UnSpawn(_gameModeInstance);
                Destroy(_gameModeInstance);
                _gameModeInstance = null;
            }
        }

        [ClientRpc]
        private void RpcOnGameplayEnded(string winnerName)
        {
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[GameplayManager] Матч завершён (клиент), победитель: {winnerName}");
        }

        [ClientRpc]
        private void RpcOnMatchStopped()
        {
            GameLog.Info(GameSettings.Instance.LogLevelMatch, "[GameplayManager] Матч остановлен (клиент)");
        }

        /// <summary>
        /// Вызывается PlayerController при гибели игрока.
        /// Делегирует в активный GameMode — каждый режим обрабатывает гибель по-своему.
        /// </summary>
        [Server]
        public void OnPlayerDied(PlayerController player)
        {
            if (_gameMode is EliminationMode elimination)
                elimination.OnPlayerDied(player);
        }
    }
}
