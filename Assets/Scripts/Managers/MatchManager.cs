using System.Linq;
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
    /// передаёт ему команды и ждёт события GameMode.MatchEnded.
    ///
    /// Вся логика матча (сеты, раунды, таймеры, счёт) живёт внутри конкретного GameMode.
    /// MatchManager не знает о структуре режима — только Start/Stop и результат.
    /// </summary>
    public class MatchManager : NetworkBehaviour
    {
        public static MatchManager Instance { get; private set; }

        /// <summary>Матч завершён. Null = ничья.</summary>
        public event Action<TeamData> MatchEnded;

        [SyncVar] private bool _matchActive;

        private GameMode _gameMode;
        private GameObject _gameModeInstance;

        public bool IsMatchActive => _matchActive;

        // ── Отображение в Inspector (только чтение, обновляются каждый кадр) ──

        [Header("Состояние матча (только чтение)")]
        [Tooltip("Идёт ли матч прямо сейчас.")]
        [SerializeField] private bool _matchActiveDisplay;

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
            _matchActiveDisplay = _matchActive;

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
        public void StartMatch()
        {
            if (_matchActive)
            {
                GameLog.Warning(GameSettings.Instance.LogLevelMatch, "[MatchManager] Матч уже идёт");
                return;
            }

            GameModeData gameModeData = GameManager.Instance != null
                ? GameManager.Instance.SelectedGameModeData
                : null;

            if (GameManager.Instance == null)
            {
                GameLog.Error("[MatchManager] StartMatch: GameManager.Instance == null. " +
                              "Убедитесь что GameManager добавлен на MirrorNetworkManager в сцене Offline.");
                return;
            }

            if (gameModeData == null)
            {
                GameLog.Error($"[MatchManager] StartMatch: режим не найден. " +
                              $"SelectedModeId='{GameManager.Instance.SelectedModeId}', " +
                              $"SelectedMapScene='{GameManager.Instance.SelectedMapScene}'.");
                return;
            }

            if (gameModeData.modePrefab == null)
            {
                GameLog.Error($"[MatchManager] GameModeData '{gameModeData.modeId}' не содержит modePrefab");
                return;
            }

            if (gameModeData.teams == null || gameModeData.teams.Length < 2)
            {
                GameLog.Error($"[MatchManager] GameModeData '{gameModeData.modeId}' содержит менее 2 команд");
                return;
            }

            // Инстанцируем и спавним через Mirror — клиенты увидят объект
            _gameModeInstance = Instantiate(gameModeData.modePrefab, transform);
            NetworkServer.Spawn(_gameModeInstance);

            _gameMode = _gameModeInstance.GetComponent<GameMode>();
            if (_gameMode == null)
            {
                GameLog.Error($"[MatchManager] Префаб '{gameModeData.modeId}' не содержит компонент GameMode");
                NetworkServer.UnSpawn(_gameModeInstance);
                Destroy(_gameModeInstance);
                _gameModeInstance = null;
                return;
            }

            _gameMode.Initialize(gameModeData.teams);
            _gameMode.MatchEnded += OnMatchEnded;

            _matchActive = true;

            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[MatchManager] Запуск режима: {gameModeData.modeId} ({gameModeData.displayName})");

            _gameMode.StartMatch();
        }

        /// <summary>
        /// Принудительно останавливает матч без определения победителя.
        /// Используется администратором для возврата в Lobby.
        /// </summary>
        [Server]
        public void StopMatch()
        {
            if (!_matchActive)
            {
                GameLog.Warning(GameSettings.Instance.LogLevelMatch, "[MatchManager] StopMatch: матч не активен");
                return;
            }

            _gameMode?.StopMatch();
            CleanupGameMode();

            _matchActive = false;
            GameLog.Info(GameSettings.Instance.LogLevelMatch, "[MatchManager] Матч остановлен администратором");
            RpcOnMatchStopped();
        }

        [Server]
        private void OnMatchEnded(TeamData winner)
        {
            CleanupGameMode();
            _matchActive = false;

            string winnerName = winner != null ? winner.displayName : "ничья";
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[MatchManager] Матч завершён, победитель: {winnerName}");

            MatchEnded?.Invoke(winner);
            RpcOnMatchEnded(winnerName);
        }

        [Server]
        private void CleanupGameMode()
        {
            if (_gameMode != null)
            {
                _gameMode.MatchEnded -= OnMatchEnded;
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
        private void RpcOnMatchEnded(string winnerName)
        {
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[MatchManager] Матч завершён (клиент), победитель: {winnerName}");
        }

        [ClientRpc]
        private void RpcOnMatchStopped()
        {
            GameLog.Info(GameSettings.Instance.LogLevelMatch, "[MatchManager] Матч остановлен (клиент)");
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
