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
    /// Быстрые отладочные сценарии редактора: подписывается на события GameNetworkManager и по личным настройкам
    /// разработчика (<see cref="DebugBootstrapSettings"/>, <c>Tools/VR Battlegrounds/Debug/Bootstrap Settings…</c>)
    /// выставляет роль, загружает карту, добавляет ботов, запускает матч. Команды раздаёт активный режим
    /// (GameMode.ServerAssignTeams), а не оркестратор.
    ///
    /// <para>
    /// Только редактор: сборка <c>VrBattlegrounds.DebugBootstrap</c> компилируется с <c>UNITY_EDITOR</c>, в сцены и
    /// префабы компонент не кладётся — его создаёт <see cref="SpawnOnPlay"/> в начале Play. Код игры знает только
    /// <see cref="DebugBootstrapGate"/>: E2E-прогон останавливает сценарий через <see cref="DebugBootstrapGate.Suppress"/>.
    /// Проверка — <c>DebugBootstrapEditorOnlyTests</c>.
    /// </para>
    ///
    /// Не меняет продакшн-код — использует те же публичные API, что и обычная игра.
    /// </summary>
    [DefaultExecutionOrder(ManagerOrder.DebugOrchestrator)]
    public class DebugOrchestrator : MonoBehaviour
    {
        /// <summary>
        /// Создаётся до загрузки первой сцены: роль и профиль хоста должны быть выставлены раньше, чем сеть
        /// стартует. Живёт до конца Play.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void SpawnOnPlay()
        {
            if (!DebugBootstrapSettings.Enabled) return;

            var go = new GameObject(nameof(DebugOrchestrator));
            DontDestroyOnLoad(go);
            go.AddComponent<DebugOrchestrator>();
        }

        // Сценарий идёт, пока его не остановил E2E-прогон (DebugBootstrapGate).
        private static bool Active => DebugBootstrapSettings.Enabled && !DebugBootstrapGate.IsSuppressed;

        // Флаг: карта уже была запрошена в этой сессии — не грузить повторно.
        private bool _mapLoadRequested;

        // Перенос аватара в зону команды при спавне (TryTeleportToSpawnZone) удалён:
        // игровая логика на телепорт не опирается. Спавн сам берёт зону команды
        // (AvatarSpawnPointResolver), откалиброванный игрок встаёт по калибровке.

        private void Awake()
        {
            if (DebugBootstrapSettings.AutoStartFallbackRole)
            {
                GameNetworkDiscovery.AppRole role = DebugBootstrapSettings.FallbackRole;
                DebugBootstrapGate.EditorRoleOverride = () => DebugBootstrapGate.IsSuppressed ? null : role;
            }

            if (DebugBootstrapSettings.HostIsAdmin)
            {
                // Запуск в качестве хоста
                LocalClientProfile.SetDebugOverride(ClientDeviceType.VR, true, GameRole.Player);
            }
        }

        private void OnEnable()
        {
            PlayersManager.SessionConnected += HandlePlayerConnected;
            PlayersManager.SessionDisconnected += HandlePlayerDisconnected;
            GameNetworkManager.ServerSceneChanged += OnServerSceneChanged;

            // Подписка вместо угадывания. Оркестратор матча живёт в сцене карты и
            // появляется позже нас; раньше это обходилось повторной попыткой «через кадр».
            MapReferee.SubscribeToInstance(HandleMapRefereeReady);
        }

        private void OnDisable()
        {
            PlayersManager.SessionConnected -= HandlePlayerConnected;
            PlayersManager.SessionDisconnected -= HandlePlayerDisconnected;
            GameNetworkManager.ServerSceneChanged -= OnServerSceneChanged;

            MapReferee.UnsubscribeFromInstance(HandleMapRefereeReady);
        }

        /// <summary>
        /// Оркестратор матча появился (загрузилась карта). Условия автостарта могли
        /// выполниться ещё в лобби — проверяем их сразу, не дожидаясь нового подключения.
        /// </summary>
        private void HandleMapRefereeReady(MapReferee manager)
        {
            if (!Active) return;
            if (!NetworkServer.active) return;

            GameLog.Debug.Verbose(
                "[DebugOrchestrator] MapReferee готов — пробуем запустить матч.");

            TryGoLive();
        }

        /// <summary>
        /// Вызывается при каждом подключении игрока (создании сессии).
        /// Назначает команду (равномерное распределение) и при необходимости стартует матч.
        /// </summary>
        private void HandlePlayerConnected(PlayerSession session)
        {
            if (!Active) return;

            if (!NetworkServer.active)
            {
                GameLog.Debug.Verbose(
                    "[DebugOrchestrator] HandlePlayerConnected: сервер не активен, пропуск.");
                return;
            }

            GameLog.Debug.Info(
                $"[DebugOrchestrator] HandlePlayerConnected: сессия={session.PlayerName}");

            // Команду оркестратор больше не назначает: её раздаёт активный режим
            // (GameMode.ServerAssignTeams): разминка даёт «Разминку» игроку без команды, режим матча
            // ждёт выбора игрока. Раньше здесь был свой автобаланс по teamsForAutoAssign,
            // и спорил бы с разминкой.
            TryGoLive();
        }

        private void HandlePlayerDisconnected(PlayerSession session)
        {
            if (!Active) return;

            GameLog.Debug.Verbose(
                $"[DebugOrchestrator] HandlePlayerDisconnected: сессия={(session != null ? session.PlayerName : "null")}");
        }

        /// <summary>
        /// Проверяет условия автостарта и запускает матч если они выполнены.
        /// Вызывается как при подключении игроков, так и после загрузки сцены карты.
        /// </summary>
        private void TryGoLive()
        {
            if (!DebugBootstrapSettings.AutoGoLive)
            {
                GameLog.Debug.Verbose(
                    "[DebugOrchestrator] TryGoLive: autoGoLive выключен.");
                return;
            }

            PlayersManager playersManager = PlayersManager.Instance;
            if (playersManager == null)
            {
                // Постоянный менеджер: если его нет после инициализации, повторная попытка
                // через кадр ничего не изменит — состав проверяет ManagerBootstrap и он же
                // об этом уже написал. Здесь просто выходим.
                GameLog.Error(
                    "[DebugOrchestrator] TryGoLive: PlayersManager.Instance пуст. " +
                    "Состав постоянных менеджеров объявлен в ManagerBootstrap, " +
                    "времена жизни — Docs/session-architecture.md.");
                return;
            }

            MapReferee matchManager = MapReferee.Instance;
            if (matchManager == null)
            {
                // Норма, а не сбой: MapReferee живёт в сцене карты, и пока игрок
                // в лобби его нет. Ждать не нужно — на его появление мы подписаны
                // (HandleMapRefereeReady), и попытка повторится сама.
                GameLog.Debug.Verbose(
                    "[DebugOrchestrator] TryGoLive: карта ещё не загружена. " +
                    "Матч запустится по сигналу MapReferee.");
                return;
            }

            if (matchManager.CurrentMap != null &&
                MapModeRules.ResolveMatchMode(matchManager.CurrentMap, null, null) == null)
            {
                // Лобби: с картой совместима только разминка, и она стартует сама
                // в MapReferee.OnStartServer. Матча здесь не бывает.
                GameLog.Debug.Verbose(
                    "[DebugOrchestrator] TryGoLive: на этой карте нет режимов матча (лобби).");
                return;
            }

            if (matchManager.IsLiveOrPaused)
            {
                GameLog.Debug.Verbose(
                    "[DebugOrchestrator] TryGoLive: матч уже активен.");
                return;
            }

            int minPlayers = 1;
            var sessionManager = VrBattlegrounds.Managers.SessionManager.Instance;
            if (sessionManager != null && sessionManager.SelectedGameModeData != null)
            {
                int minPlayersOverride = DebugBootstrapSettings.MinPlayersOverride;
                minPlayers = minPlayersOverride > 0
                    ? minPlayersOverride
                    : sessionManager.SelectedGameModeData.minPlayersToStart;
            }

            if (playersManager.Sessions.Count < minPlayers)
            {
                GameLog.Debug.Info(
                    $"[DebugOrchestrator] TryGoLive: недостаточно игроков ({playersManager.Sessions.Count}/{minPlayers}). Ждем остальных.");
                return;
            }

            GameLog.Debug.Info(
                "[DebugOrchestrator] TryGoLive: попытка запустить матч (условия по игрокам выполнены).");
            matchManager.GoLive();

        }

        /// <summary>
        /// Вызывается когда сервер завершил загрузку сцены.
        /// Если задана карта автозапуска (AutoLoadMapScene) — загружает её.
        /// После загрузки сцены карты пытается запустить матч (игроки могли подключиться раньше).
        /// </summary>
        private void OnServerSceneChanged(string sceneName)
        {
            if (!Active) return;

            if (!NetworkServer.active)
                return;

            GameLog.Debug.Info(
                $"[DebugOrchestrator] OnServerSceneChanged: сцена='{sceneName}'");

            TryAutoLoadMap();

            // Боты — как игроки, подключившиеся к серверу сразу: первая загруженная сцена.
            // EnsureCount только добавляет, повторная смена сцены лишних не создаст.
            int botCount = DebugBootstrapSettings.BotCount;
            if (botCount > 0)
            {
                VrBattlegrounds.Bots.BotDirector.EnsureInstance()?.EnsureCount(botCount);
            }

            // Матч отсюда не запускаем. Игроки могли подключиться ещё в лобби, когда
            // MapReferee не существовал, — но на его появление мы подписаны
            // (HandleMapRefereeReady), и сигнал приходит раньше этого колбэка:
            // объекты сцены просыпаются в момент её загрузки, а OnServerSceneChanged
            // Mirror зовёт уже после.
        }

        /// <summary>Совпадает ли запрошенная карта с уже открытой сценой.</summary>
        public static bool IsAlreadyLoaded(string requestedScene, string activeScene)
        {
            return !string.IsNullOrEmpty(requestedScene) &&
                   string.Equals(requestedScene, activeScene, System.StringComparison.OrdinalIgnoreCase);
        }

        private void TryAutoLoadMap()
        {
            string mapScene = DebugBootstrapSettings.AutoLoadMapScene;
            string gameModeId = DebugBootstrapSettings.AutoGameModeId;
            if (string.IsNullOrEmpty(mapScene))
                return;

            // Сцена уже та, что просят: хост поднимается прямо в onlineScene (Lobby),
            // и «загрузить Lobby» означало бы перезагрузить её второй раз.
            if (IsAlreadyLoaded(mapScene, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
            {
                _mapLoadRequested = true;
                GameLog.Debug.Verbose(
                    $"[DebugOrchestrator] Карта {mapScene} уже открыта — автозагрузка не нужна.");
                return;
            }

            // Загружаем карту только один раз за сессию.
            if (_mapLoadRequested)
            {
                GameLog.Debug.Verbose(
                    "[DebugOrchestrator] Карта уже была запрошена, повторный вызов игнорируется.");
                return;
            }

            _mapLoadRequested = true;
            GameLog.Debug.Info(
                $"[DebugOrchestrator] Автозагрузка карты: {mapScene}");

            // Карта с режимом — серия из одной карты, как из меню админа: карта стартует
            // в разминке, «Начать матч» делает автостарт (AutoGoLive).
            if (!string.IsNullOrEmpty(gameModeId) && SessionManager.Instance != null)
            {
                SessionManager.Instance.SetSession(mapScene, gameModeId);
                SessionManager.Instance.StartSession();
                return;
            }

            MapLoader.Instance?.LoadMap(mapScene);
        }
    }
}
