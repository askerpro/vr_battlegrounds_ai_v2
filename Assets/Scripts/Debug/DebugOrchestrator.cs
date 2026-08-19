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
    /// сценарий из DebugBootstrapConfig: равномерно распределяет игроков по командам,
    /// загружает карту, запускает матч.
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

        // Трекаем уже заспавненные сессии на текущей карте (для фильтрации смены скина)
        private HashSet<uint> _initializedSessions = new HashSet<uint>();

        // Трекаем девайсы, которым мы уже назначили стартовую команду, чтобы не ломать её при реконнекте/смене карты
        private HashSet<string> _assignedDevices = new HashSet<string>();

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
            AvatarManager.OnAvatarSpawned += HandleAvatarSpawned;
            GameNetworkManager.ServerSceneChanged += OnServerSceneChanged;

            // Подписка вместо угадывания. Оркестратор матча живёт в сцене карты и
            // появляется позже нас; раньше это обходилось повторной попыткой «через кадр».
            GameplayManager.SubscribeToInstance(HandleGameplayManagerReady);
        }

        private void OnDisable()
        {
            PlayersManager.OnSessionConnected -= HandlePlayerConnected;
            PlayersManager.OnSessionDisconnected -= HandlePlayerDisconnected;
            AvatarManager.OnAvatarSpawned -= HandleAvatarSpawned;
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

            GameLog.Verbose(GameSettings.Instance.LogLevelDebug,
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
                GameLog.Verbose(GameSettings.Instance.LogLevelDebug,
                    "[DebugOrchestrator] HandlePlayerConnected: сервер не активен, пропуск.");
                return;
            }

            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                $"[DebugOrchestrator] HandlePlayerConnected: сессия={session.PlayerName}");

            TryAssignTeam(session);
            TryStartGameplay();
        }

        private void HandleAvatarSpawned(PlayerController avatar)
        {
            if (_config == null || !_config.enabled) return;

            if (avatar == null) return;

            // Если игрок уже был первично инициализирован на этой карте, не трогаем (например, при смене скина)
            if (_initializedSessions.Contains(avatar.SessionNetId))
            {
                GameLog.Verbose(GameSettings.Instance.LogLevelDebug, $"[DebugOrchestrator] Аватар для {avatar.name} (SessionNetId={avatar.SessionNetId}) уже был инициализирован. Пропускаем телепорт на базу.");
                return;
            }

            _initializedSessions.Add(avatar.SessionNetId);
            TryTeleportToSpawnZone(avatar);
        }

        private void TryTeleportToSpawnZone(PlayerController player)
        {
            if (player.Session.Team == null) return;

            // Ищем спавн зону для назначенной команды
            TeamSpawnZone targetZone = null;
            TeamSpawnZone[] zones = Object.FindObjectsByType<TeamSpawnZone>(FindObjectsSortMode.None);
            foreach (var zone in zones)
            {
                if (zone.Team == player.Session.Team)
                {
                    targetZone = zone;
                    break;
                }
            }

            if (targetZone != null)
            {
                GameLog.Info(GameSettings.Instance.LogLevelDebug, $"[DebugOrchestrator] {player.name} начинает в зоне спавна команды {player.Session.Team.displayName}");
                player.Respawn(targetZone.transform);
            }
        }

        private void HandlePlayerDisconnected(PlayerSession session)
        {
            if (_config == null || !_config.enabled) return;

            GameLog.Verbose(GameSettings.Instance.LogLevelDebug,
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
                GameLog.Verbose(GameSettings.Instance.LogLevelDebug,
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
                GameLog.Verbose(GameSettings.Instance.LogLevelDebug,
                    "[DebugOrchestrator] TryStartGameplay: карта ещё не загружена. " +
                    "Матч запустится по сигналу GameplayManager.");
                return;
            }

            if (matchManager.IsGameplayActive)
            {
                GameLog.Verbose(GameSettings.Instance.LogLevelDebug,
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
                GameLog.Info(GameSettings.Instance.LogLevelDebug,
                    $"[DebugOrchestrator] TryStartGameplay: недостаточно игроков ({playersManager.Sessions.Count}/{minPlayers}). Ждем остальных.");
                return;
            }

            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                "[DebugOrchestrator] TryStartGameplay: попытка запустить матч (условия по игрокам выполнены).");
            matchManager.StartGameplay();

        }

        private void TryAssignTeam(PlayerSession session)
        {
            if (_config.teamsForAutoAssign == null || _config.teamsForAutoAssign.Count == 0 || session == null)
            {
                GameLog.Verbose(GameSettings.Instance.LogLevelDebug,
                    "[DebugOrchestrator] TryAssignTeam: teamsForAutoAssign пуст — команда не назначается.");
                return;
            }

            // Если игрок уже подключался ранее и ему бала назначена команда, оставляем её (восстановится из snapshot).
            if (!string.IsNullOrEmpty(session.DeviceToken) && _assignedDevices.Contains(session.DeviceToken))
            {
                GameLog.Info(GameSettings.Instance.LogLevelDebug,
                    $"[DebugOrchestrator] Игроку {session.PlayerName} (Device: {session.DeviceToken}) команда уже назначалась ранее. Пропускаем автобалансировку.");
                return;
            }

            PlayersManager pm = PlayersManager.Instance;
            TeamData bestTeam = null;
            int bestCount = int.MaxValue;

            foreach (TeamData team in _config.teamsForAutoAssign)
            {
                if (team == null) continue;

                // Считаем сколько сессий в этой команде
                int count = 0;
                if (pm != null)
                {
                    count = pm.GetPlayers(team).Count();
                }

                if (count < bestCount)
                {
                    bestCount = count;
                    bestTeam = team;
                }
            }

            if (bestTeam == null) return;

            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                $"[DebugOrchestrator] Команда назначена сессии {session.PlayerName}: {bestTeam.displayName}");

            session.TeamIndex = (byte)bestTeam.teamIndex;
            session.AvatarIndex = 0; // Скин по умолчанию

            if (!string.IsNullOrEmpty(session.DeviceToken))
            {
                _assignedDevices.Add(session.DeviceToken);
            }
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

            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                $"[DebugOrchestrator] OnServerSceneChanged: сцена='{sceneName}'");

            TryAutoLoadMap();

            // При смене сцены очищаем трекер спавнов, так как все аватары будут пересозданы
            _initializedSessions.Clear();

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
                GameLog.Verbose(GameSettings.Instance.LogLevelDebug,
                    "[DebugOrchestrator] Карта уже была запрошена, повторный вызов игнорируется.");
                return;
            }

            // Устанавливаем режим через SessionManager до загрузки карты — GameplayManager прочитает его при старте.
            if (!string.IsNullOrEmpty(_config.autoGameModeId) && SessionManager.Instance != null)
            {
                SessionManager.Instance.SetSession(_config.autoLoadMapScene, _config.autoGameModeId);
                GameLog.Info(GameSettings.Instance.LogLevelDebug,
                    $"[DebugOrchestrator] Сессия установлена: карта={_config.autoLoadMapScene}, режим={_config.autoGameModeId}");
            }

            _mapLoadRequested = true;
            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                $"[DebugOrchestrator] Автозагрузка карты: {_config.autoLoadMapScene}");
            MapManager.Instance?.LoadMap(_config.autoLoadMapScene);
        }
    }
}
