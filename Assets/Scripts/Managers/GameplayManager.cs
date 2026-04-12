using System.Linq;
// Forced compilation trigger
using System;
using Mirror;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;
using UltimateXR.Mechanics.Weapons;

using VrBattlegrounds.Player.Avatars;
namespace VrBattlegrounds.Managers
{
    public enum GameplayState
    {
        NotActive,
        Active,
        Paused
    }

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

        [SyncVar] private GameplayState _currentState = GameplayState.NotActive;

        private GameMode _gameMode;
        private GameObject _gameModeInstance;

        public GameplayState CurrentState => _currentState;
        public bool IsGameplayActive => _currentState == GameplayState.Active;

        public GameMode ActiveGameMode => _gameMode;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Update()
        {

            // Управляем доступностью оружия через UxrWeaponManager
            if (UxrWeaponManager.HasInstance)
            {
                bool weaponsEnabled = true;
                if (_gameMode is EliminationMode elim)
                {
                    weaponsEnabled = elim.CurrentRoundState == RoundState.Combat;
                }

                if (UxrWeaponManager.Instance.WeaponSystemEnabled != weaponsEnabled)
                {
                    UxrWeaponManager.Instance.SetWeaponSystemEnabled(weaponsEnabled);
                }
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
            if (_currentState != GameplayState.NotActive)
            {
                GameLog.Warning(GameSettings.Instance.LogLevelMatch, "[GameplayManager] Матч уже идёт или на паузе");
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

            _currentState = GameplayState.Active;

            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[GameplayManager] Запуск режима: {gameModeData.modeId} ({gameModeData.displayName})");

            _gameMode.StartGameplayWhenReady();
        }

        /// <summary>
        /// Принудительно останавливает матч без определения победителя.
        /// Используется администратором для возврата в Lobby.
        /// </summary>
        [Server]
        public void StopGameplay()
        {
            if (_currentState == GameplayState.NotActive)
            {
                GameLog.Warning(GameSettings.Instance.LogLevelMatch, "[GameplayManager] StopGameplay: матч не активен");
                return;
            }

            _gameMode?.StopGameplay();
            CleanupGameMode();

            _currentState = GameplayState.NotActive;
            GameLog.Info(GameSettings.Instance.LogLevelMatch, "[GameplayManager] Матч остановлен администратором");
            RpcOnMatchStopped();
        }

        [Server]
        public void PauseGameplay()
        {
            if (_currentState == GameplayState.Active)
            {
                _currentState = GameplayState.Paused;
                GameLog.Info(GameSettings.Instance.LogLevelMatch, "[GameplayManager] Матч поставлен на паузу");
            }
        }

        [Server]
        public void ResumeGameplay()
        {
            if (_currentState == GameplayState.Paused)
            {
                _currentState = GameplayState.Active;
                GameLog.Info(GameSettings.Instance.LogLevelMatch, "[GameplayManager] Матч снят с паузы");
            }
        }

        [Server]
        private void OnGameplayEnded(TeamData winner)
        {
            CleanupGameMode();
            _currentState = GameplayState.NotActive;

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

        /// <summary>Глобальное серверное событие: Игрок запросил смену команды/скина.</summary>
        public static event Action<PlayerSession, int, int> OnPlayerTeamChangeRequested;

        /// <summary>
        /// Централизованный вход для обработки смены команды и скина сервером.
        /// Обеспечивает вызов всех необходимых хуков (сброс статы) перед физической сменой.
        /// </summary>
        [Server]
        public void ProcessTeamChangeRequest(PlayerSession session, int newTeamId, int newAvatarId)
        {
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[GameplayManager] Игрок {session.PlayerName} запросил смену: Команда {newTeamId}, Скин {newAvatarId}");

            // 1. Уведомляем другие системы (GameMode, Stats)
            OnPlayerTeamChangeRequested?.Invoke(session, newTeamId, newAvatarId);

            // 2. Делегируем фактическую смену AvatarManager (там происходит Spawn нового префаба)
            if (AvatarManager.Instance != null)
            {
                AvatarManager.Instance.ChangeAvatar(session.connectionToClient, session, newTeamId, newAvatarId);
            }
        }
    }
}
