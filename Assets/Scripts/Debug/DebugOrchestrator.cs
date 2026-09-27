using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Оркестратор быстрой инициализации для отладки.
    /// Подписывается на события GameNetworkManager и выполняет заскриптованный
    /// сценарий из DebugBootstrapConfig: загружает карту, запускает матч. Команды раздаёт
    /// активный режим (GameMode.ServerAssignTeams), а не оркестратор.
    ///
    /// Не меняет продакшн-код — использует те же публичные API, что и обычная игра.
    ///
    /// Как использовать:
    ///   1. Добавить этот компонент на любой GameObject в сцене (например "DebugOrchestrator").
    ///   2. Назначить DebugBootstrapConfig в поле Config.
    ///   3. Чтобы отключить — деактивировать GameObject.
    /// </summary>
    [DefaultExecutionOrder(ManagerOrder.DebugOrchestrator)]
    public class DebugOrchestrator : MonoBehaviour
    {
        [SerializeField] private DebugBootstrapConfig _config;
        public DebugBootstrapConfig Config => _config;

        // Флаг: карта уже была запрошена в этой сессии — не грузить повторно.
        private bool _mapLoadRequested;

        // Перенос аватара в зону команды при спавне (TryTeleportToSpawnZone) удалён:
        // игровая логика на телепорт не опирается. Спавн сам берёт зону команды
        // (AvatarSpawnPointResolver), откалиброванный игрок встаёт по калибровке.

        private void Awake()
        {
            if (_config == null || !_config.enabled)
            {
                return;
            }

            if (_config.hostIsAdmin)
            {
                // Запуск в качестве хоста
                LocalClientProfile.SetDebugOverride(ClientDeviceType.VR, true, GameRole.Player);
            }
        }

        private void OnEnable()
        {
            PlayersManager.OnSessionConnected += HandlePlayerConnected;
            PlayersManager.OnSessionDisconnected += HandlePlayerDisconnected;
            GameNetworkManager.ServerSceneChanged += OnServerSceneChanged;

            // Подписка вместо угадывания. Оркестратор матча живёт в сцене карты и
            // появляется позже нас; раньше это обходилось повторной попыткой «через кадр».
            GameplayManager.SubscribeToInstance(HandleGameplayManagerReady);
        }

        private void OnDisable()
        {
            PlayersManager.OnSessionConnected -= HandlePlayerConnected;
            PlayersManager.OnSessionDisconnected -= HandlePlayerDisconnected;
            GameNetworkManager.ServerSceneChanged -= OnServerSceneChanged;

            GameplayManager.UnsubscribeFromInstance(HandleGameplayManagerReady);
        }

        /// <summary>
        /// Оркестратор матча появился (загрузилась карта). Условия автостарта могли
        /// выполниться ещё в лобби — проверяем их сразу, не дожидаясь нового подключения.
        /// </summary>
        private void HandleGameplayManagerReady(GameplayManager manager)
        {
            if (_config == null || !_config.enabled) return;
            if (!NetworkServer.active) return;

            GameLog.Debug.Verbose(
                "[DebugOrchestrator] GameplayManager готов — пробуем запустить матч.");

            TryStartGameplay();
        }

        private void Start()
        {
            if (_config == null || !_config.enabled)
                return;
        }

        /// <summary>
        /// Вызывается при каждом подключении игрока (создании сессии).
        /// Назначает команду (равномерное распределение) и при необходимости стартует матч.
        /// </summary>
        private void HandlePlayerConnected(PlayerSession session)
        {
            if (_config == null || !_config.enabled) return;

            if (!NetworkServer.active)
            {
                GameLog.Debug.Verbose(
                    "[DebugOrchestrator] HandlePlayerConnected: сервер не активен, пропуск.");
                return;
            }

            GameLog.Debug.Info(
                $"[DebugOrchestrator] HandlePlayerConnected: сессия={session.PlayerName}");

            // Команду оркестратор больше не назначает: её раздаёт активный режим
            // (GameMode.ServerAssignTeams) — в лобби команду «Лобби», на карте автобалансом
            // по командам матча. Раньше здесь был свой автобаланс по teamsForAutoAssign,
            // и в лобби он спорил бы с лобби-режимом.
            TryStartGameplay();
        }

        private void HandlePlayerDisconnected(PlayerSession session)
        {
            if (_config == null || !_config.enabled) return;

            GameLog.Debug.Verbose(
                $"[DebugOrchestrator] HandlePlayerDisconnected: сессия={(session != null ? session.PlayerName : "null")}");
        }

        /// <summary>
        /// Проверяет условия автостарта и запускает матч если они выполнены.
        /// Вызывается как при подключении игроков, так и после загрузки сцены карты.
        /// </summary>
        private void TryStartGameplay()
        {
            if (!_config.autoStartGameplay)
            {
                GameLog.Debug.Verbose(
                    "[DebugOrchestrator] TryStartGameplay: autoStartGameplay выключен.");
                return;
            }

            PlayersManager playersManager = PlayersManager.Instance;
            if (playersManager == null)
            {
                // Постоянный менеджер: если его нет после инициализации, повторная попытка
                // через кадр ничего не изменит — состав проверяет ManagerBootstrap и он же
                // об этом уже написал. Здесь просто выходим.
                GameLog.Error(
                    "[DebugOrchestrator] TryStartGameplay: PlayersManager.Instance пуст. " +
                    "Состав постоянных менеджеров объявлен в ManagerBootstrap, " +
                    "времена жизни — Docs/session-architecture.md.");
                return;
            }

            GameplayManager matchManager = GameplayManager.Instance;
            if (matchManager == null)
            {
                // Норма, а не сбой: GameplayManager живёт в сцене карты, и пока игрок
                // в лобби его нет. Ждать не нужно — на его появление мы подписаны
                // (HandleGameplayManagerReady), и попытка повторится сама.
                GameLog.Debug.Verbose(
                    "[DebugOrchestrator] TryStartGameplay: карта ещё не загружена. " +
                    "Матч запустится по сигналу GameplayManager.");
                return;
            }

            if (matchManager.HasSceneGameMode)
            {
                // Режим сцены (лобби) стартует сам в GameplayManager.OnStartServer —
                // автостарт матча здесь не нужен и выбором матча его не перебить.
                GameLog.Debug.Verbose(
                    "[DebugOrchestrator] TryStartGameplay: у сцены свой режим, он стартует сам.");
                return;
            }

            if (matchManager.IsGameplayActive)
            {
                GameLog.Debug.Verbose(
                    "[DebugOrchestrator] TryStartGameplay: матч уже активен.");
                return;
            }

            int minPlayers = 1;
            var sessionManager = VrBattlegrounds.Managers.SessionManager.Instance;
            if (sessionManager != null && sessionManager.SelectedGameModeData != null)
            {
                minPlayers = _config.minPlayersOverride > 0
                    ? _config.minPlayersOverride
                    : sessionManager.SelectedGameModeData.minPlayersToStart;
            }

            if (playersManager.Sessions.Count < minPlayers)
            {
                GameLog.Debug.Info(
                    $"[DebugOrchestrator] TryStartGameplay: недостаточно игроков ({playersManager.Sessions.Count}/{minPlayers}). Ждем остальных.");
                return;
            }

            GameLog.Debug.Info(
                "[DebugOrchestrator] TryStartGameplay: попытка запустить матч (условия по игрокам выполнены).");
            matchManager.StartGameplay();

        }

        /// <summary>
        /// Вызывается когда сервер завершил загрузку сцены.
        /// Если задан autoLoadMapScene — загружает карту.
        /// После загрузки сцены карты пытается запустить матч (игроки могли подключиться раньше).
        /// </summary>
        private void OnServerSceneChanged(string sceneName)
        {
            if (_config == null || !_config.enabled) return;

            if (!NetworkServer.active)
                return;

            GameLog.Debug.Info(
                $"[DebugOrchestrator] OnServerSceneChanged: сцена='{sceneName}'");

            TryAutoLoadMap();

            // Матч отсюда не запускаем. Игроки могли подключиться ещё в лобби, когда
            // GameplayManager не существовал, — но на его появление мы подписаны
            // (HandleGameplayManagerReady), и сигнал приходит раньше этого колбэка:
            // объекты сцены просыпаются в момент её загрузки, а OnServerSceneChanged
            // Mirror зовёт уже после.
        }

        private void TryAutoLoadMap()
        {
            if (string.IsNullOrEmpty(_config.autoLoadMapScene))
                return;

            // Загружаем карту только один раз за сессию.
            if (_mapLoadRequested)
            {
                GameLog.Debug.Verbose(
                    "[DebugOrchestrator] Карта уже была запрошена, повторный вызов игнорируется.");
                return;
            }

            // Устанавливаем режим через SessionManager до загрузки карты — GameplayManager прочитает его при старте.
            if (!string.IsNullOrEmpty(_config.autoGameModeId) && SessionManager.Instance != null)
            {
                SessionManager.Instance.SetSession(_config.autoLoadMapScene, _config.autoGameModeId);
                GameLog.Debug.Info(
                    $"[DebugOrchestrator] Сессия установлена: карта={_config.autoLoadMapScene}, режим={_config.autoGameModeId}");
            }

            _mapLoadRequested = true;
            GameLog.Debug.Info(
                $"[DebugOrchestrator] Автозагрузка карты: {_config.autoLoadMapScene}");
            MapManager.Instance?.LoadMap(_config.autoLoadMapScene);
        }
    }
}
