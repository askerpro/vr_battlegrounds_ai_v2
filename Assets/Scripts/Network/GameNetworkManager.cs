using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Сетевой менеджер игры на базе Mirror.
    /// Единственная ответственность: управление сетевыми подключениями.
    /// Логика команд и старт матча — в подписчиках событий (MapReferee, DebugOrchestrator).
    /// </summary>
    [DefaultExecutionOrder(ManagerOrder.GameNetworkManager)]
    public class GameNetworkManager : NetworkManager
    {

        /// <summary>Сервер завершил загрузку сцены.</summary>
        public static event Action<string> ServerSceneChanged;

        /// <summary>
        /// Клиент завершил загрузку сцены. Всё, что было в предыдущей, уничтожено,
        /// поэтому подписчикам нужно заново получить состояние сцены с сервера —
        /// см. <see cref="NetworkStateRelay"/>.
        /// </summary>
        public static event Action ClientSceneChanged;

        [Header("Server Context")]
        [Tooltip("Префаб SessionManager, который будет спавниться при старте сервера.")]
        [SerializeField] private GameObject _sessionContextPrefab;

        [Header("Карты")]
        [Tooltip("Центральный каталог служб и содержимого карт для MapBootstrap. Один на процесс, не на карту.")]
        [SerializeField] private Maps.Runtime.MapRuntimeCatalog _mapRuntimeCatalog;

        /// <summary>Установленный каталог запуска карт; null — менеджер не поднят или каталог не назначен.</summary>
        public static Maps.Runtime.MapRuntimeCatalog MapCatalog =>
            singleton is GameNetworkManager manager ? manager._mapRuntimeCatalog : null;

        public override void Awake()
        {
#if UNITY_EDITOR
            VrBattlegrounds.DevTools.DebugBootstrapGate.EditorBeforeNetworkAwake?.Invoke(this);
#endif
            base.Awake();
            autoCreatePlayer = false;
        }

        // ── Доступность сервера извне ───────────────────────────────────────
        //
        // Сервер поднимается без сокета (NetworkServer.listen = false): Mirror строит весь серверный граф,
        // маршрут старта грузит и собирает карту, а порт открывается один раз — когда карта готова.
        // Иначе клиент входит в тяжёлый старт сервера и рвётся по тишине KCP (trace829, discovery8).
        // Контракт: tasks/bots-validation/contracts/startup-lifecycle-design.md, S1.

        /// <summary>Доступность сервера для внешних клиентов. Единственный писатель — этот менеджер.</summary>
        public enum ServerAvailability { Stopped, PreparingWithoutSocket, Published, Failed }

        /// <summary>Текущая доступность сервера этого процесса.</summary>
        public static ServerAvailability Availability { get; private set; }

        /// <summary>Сервер открыл порт и принимает внешних клиентов. Подписчик — объявление в discovery.</summary>
        public static event Action ServerPublished;

        /// <summary>Идёт смена сцены сервера, начатая <see cref="ServerChangeScene"/>: подписчики её конца ещё не отработали.</summary>
        private bool _serverSceneChangePending;

        /// <summary>Кадр, в котором условия публикации впервые выполнились; -1 — не выполнены.</summary>
        private int _publishReadyFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAvailability()
        {
            Availability = ServerAvailability.Stopped;
            ServerPublished = null;
        }

        /// <summary>Выделенный сервер без сокета: порт откроется после готовности карты.</summary>
        public void StartServerWithoutSocket()
        {
            // Shutdown возвращает listen = true — флаг ставится перед каждым запуском.
            NetworkServer.listen = false;
            _serverSceneChangePending = false;
            StartServer();
        }

        /// <summary>
        /// Хост без сокета: локальный игрок входит сразу (соединение внутри процесса), удалённые —
        /// после публикации.
        /// </summary>
        public void StartHostWithoutSocket()
        {
            NetworkServer.listen = false;
            _serverSceneChangePending = false;
            StartHost();
        }

        public override void Update()
        {
            base.Update();
            if (Availability == ServerAvailability.PreparingWithoutSocket) TryPublishServer();
        }

        /// <summary>Шаг публикации по текущему состоянию старта.</summary>
        internal enum PublicationStep { Wait, Arm, Publish, Fail }

        /// <summary>
        /// Публиковать ли сервер — чистое решение без сцены и Mirror. Старт закончен, когда смена сцены
        /// и её подписчики отработали (там же стенд создаёт ботов) и карта закоммитила режим
        /// (<see cref="MapBootstrap.IsServerReady"/>); сцена без MapBootstrap готова сразу. Публикация —
        /// через кадр после того, как условия выполнились (<paramref name="armedFrame"/>): граница сетевого
        /// цикла после последней квитанции. Отказ сборки карты — <see cref="PublicationStep.Fail"/>.
        /// </summary>
        internal static PublicationStep DecidePublication(
            bool serverActive, bool loadingScene, bool sceneChangePending,
            bool hasBootstrap, bool bootstrapFailed, bool bootstrapReady,
            int armedFrame, int frame)
        {
            if (!serverActive || loadingScene || sceneChangePending) return PublicationStep.Wait;
            if (hasBootstrap && bootstrapFailed) return PublicationStep.Fail;
            if (hasBootstrap && !bootstrapReady) return PublicationStep.Wait;
            if (armedFrame < 0) return PublicationStep.Arm;
            return frame > armedFrame ? PublicationStep.Publish : PublicationStep.Arm;
        }

        private void TryPublishServer()
        {
            MapBootstrap bootstrap = MapBootstrap.ForScene(SceneManager.GetActiveScene());
            PublicationStep step = DecidePublication(
                NetworkServer.active, NetworkServer.isLoadingScene, _serverSceneChangePending,
                bootstrap != null, bootstrap != null && bootstrap.Stage == MapBootstrapStage.Failed,
                bootstrap != null && bootstrap.IsServerReady,
                _publishReadyFrame, Time.frameCount);

            switch (step)
            {
                case PublicationStep.Wait:
                    _publishReadyFrame = -1;
                    break;
                case PublicationStep.Arm:
                    if (_publishReadyFrame < 0) _publishReadyFrame = Time.frameCount;
                    break;
                case PublicationStep.Fail:
                    FailServerPublication($"карта не собрана ({bootstrap.FailureCode})", stop: false);
                    break;
                case PublicationStep.Publish:
                    PublishServer();
                    break;
            }
        }

        private void PublishServer()
        {
            try
            {
                if (!Transport.active.ServerActive()) Transport.active.ServerStart();
            }
            catch (Exception e)
            {
                FailServerPublication($"порт не открыт: {e.Message}", stop: true);
                return;
            }
            if (!Transport.active.ServerActive())
            {
                FailServerPublication("порт не открыт: транспорт не активен после ServerStart", stop: true);
                return;
            }

            NetworkServer.listen = true;
            Availability = ServerAvailability.Published;
#if UNITY_EDITOR
            VrBattlegrounds.DevTools.DebugBootstrapGate.EditorNetworkDiagnosticEvent?.Invoke("server-published", networkAddress);
#endif
            GameLog.Network.Info(
                $"[GameNetworkManager] Сервер опубликован: старт завершён, порт открыт, сцена '{SceneManager.GetActiveScene().name}'.");
            ServerPublished?.Invoke();
        }

        /// <summary>Именованный отказ публикации. Повторов нет: клиенты получат свой срок ожидания.</summary>
        private void FailServerPublication(string reason, bool stop)
        {
            Availability = ServerAvailability.Failed;
            GameLog.Network.Error($"[GameNetworkManager] Сервер не опубликован: {reason}.");
            if (!stop) return;
            if (NetworkClient.activeHost) StopHost();
            else StopServer();
        }

        public override void OnStartServer()
        {
#if UNITY_EDITOR
            VrBattlegrounds.DevTools.DebugBootstrapGate.EditorNetworkDiagnosticEvent?.Invoke("server-active", networkAddress);
#endif
            // listen = true — сокет открыт штатным Listen сразу: публиковать нечего.
            Availability = NetworkServer.listen ? ServerAvailability.Published : ServerAvailability.PreparingWithoutSocket;
            _publishReadyFrame = -1;
            base.OnStartServer();

            // Ники живут, пока жив сервер: новый сервер — новая встреча.
            AdminNaming.ClearNicknames();

            if (_sessionContextPrefab != null)
            {
                GameObject sessionInstance = Instantiate(_sessionContextPrefab);
                NetworkServer.Spawn(sessionInstance);
            }

            NetworkServer.RegisterHandler<GamePlayerConnectMessage>(OnGamePlayerConnect);
            NetworkServer.RegisterHandler<SpectatorConnectMessage>(OnSpectatorConnect);
            VrBattlegrounds.DevTools.StressTest.StressTestNetwork.RegisterServerHandlers();
            VrBattlegrounds.DevTools.DebugModeNetwork.RegisterServerHandlers();
            VrBattlegrounds.Bots.BotNetwork.RegisterServerHandlers();
            VrBattlegrounds.Bots.BotMatchNetwork.RegisterServerHandlers();

            // У хоста первая сцена уже загружена (Mirror зовёт OnStartServer после неё): теперь есть Series,
            // и маршрут старта захватывает режим. У выделенного сервера сцена ещё впереди — захват в ServerChangeScene.
            ServerStartupRoute.OnServerStarted();
        }

        /// <summary>
        /// Единственная точка первой смены сцены сервера и для StartServer, и для StartHost: при запросе
        /// <see cref="ServerStartupRoute"/> сервер стартует сразу в цель вместо <c>onlineScene</c>. Поле
        /// <c>onlineScene</c> не меняется (NET-21); остальные смены сцены идут без изменений.
        /// </summary>
        public override void ServerChangeScene(string newSceneName)
        {
            string resolvedScene = ServerStartupRoute.ResolveServerScene(newSceneName, onlineScene);
            _serverSceneChangePending = true;
#if UNITY_EDITOR
            VrBattlegrounds.DevTools.DebugBootstrapGate.EditorNetworkDiagnosticEvent?.Invoke("server-scene-begin", resolvedScene);
#endif
            base.ServerChangeScene(resolvedScene);
        }

        public override void OnStopServer()
        {
#if UNITY_EDITOR
            VrBattlegrounds.DevTools.DebugBootstrapGate.EditorNetworkDiagnosticEvent?.Invoke("server-stop", "");
#endif
            ServerStartupRoute.OnServerStopped();
            if (Availability != ServerAvailability.Failed) Availability = ServerAvailability.Stopped;
            _serverSceneChangePending = false;
            _publishReadyFrame = -1;
            _serverConnectedAt.Clear();
            _serverLastError.Clear();
            base.OnStopServer();
        }

        public override void OnServerSceneChanged(string sceneName)
        {
#if UNITY_EDITOR
            VrBattlegrounds.DevTools.DebugBootstrapGate.EditorNetworkDiagnosticEvent?.Invoke("server-scene-end", sceneName);
#endif
            base.OnServerSceneChanged(sceneName);
            ServerSceneChanged?.Invoke(sceneName);
            // Подписчики конца смены сцены отработали (стенд создал ботов) — публикации больше нечего ждать от сцены.
            _serverSceneChangePending = false;
        }

        /// <summary>
        /// Клиент поднялся. Сразу подменяем спавн сетевых префабов с компонентами
        /// UltimateXR: их <c>UniqueId</c> обязан совпасть с серверным, иначе ни один
        /// захват, выстрел или вставка магазина не будут применены на другой машине
        /// (находка NET-16). Подробности — <see cref="NetworkUxrIdentity"/>.
        ///
        /// Позже нельзя: спавн-сообщения приходят сразу после готовности клиента.
        /// Раньше тоже нельзя: штатную регистрацию префабов Mirror делает в
        /// <c>RegisterClientMessages</c>, то есть непосредственно перед этим вызовом.
        /// </summary>
        public override void OnStartClient()
        {
#if UNITY_EDITOR
            VrBattlegrounds.DevTools.DebugBootstrapGate.EditorNetworkDiagnosticEvent?.Invoke("client-start", networkAddress);
#endif
            base.OnStartClient();
            NetworkUxrIdentity.InstallClientSpawnHandlers();
            VrBattlegrounds.DevTools.StressTest.StressTestNetwork.RegisterClientHandlers();
            VrBattlegrounds.DevTools.DebugModeNetwork.RegisterClientHandlers();
        }

        public override void OnStopClient()
        {
#if UNITY_EDITOR
            VrBattlegrounds.DevTools.DebugBootstrapGate.EditorNetworkDiagnosticEvent?.Invoke("client-stop", "");
#endif
            GameNetworkDiscovery.NotifyClientStopped(LocalClientProfile.HasTemporaryOverride);
            NetworkUxrIdentity.UninstallClientSpawnHandlers();
            base.OnStopClient();
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            // Мы не спавним ничего автоматически. Спавн идет в OnPlayerJoinMessage
        }

        public override void OnServerReady(NetworkConnectionToClient conn)
        {
            base.OnServerReady(conn);

            // Когда клиент полностью загрузил новую сцену, Mirror уничтожил старый аватар.
            // Нам нужно возродить его, если PlayerSession остался жив.
            if (PlayersManager.Instance != null && AvatarManager.Instance != null)
            {
                var session = PlayersManager.Instance.GetSession(conn);
                if (session != null && session.Role == GameRole.Player && session.ActiveAvatar == null)
                {
                    // Спавним новый физический аватар на основе существующих данных (TeamIndex / AvatarIndex)
                    AvatarManager.Instance.ChangeAvatar(conn, session, session.TeamIndex, session.AvatarIndex);
                }
            }
        }

        // ── Журнал соединений (только для лога) ──────────────────────────────
        // Время подключения и последняя ошибка транспорта по connectionId: из них строка
        // «[Net] disconnect» берёт длительность сессии и причину. На игру не влияют.

        private readonly Dictionary<int, double> _serverConnectedAt = new Dictionary<int, double>();
        private readonly Dictionary<int, TransportError> _serverLastError = new Dictionary<int, TransportError>();

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);
            _serverConnectedAt[conn.connectionId] = Time.realtimeSinceStartupAsDouble;
            _serverLastError.Remove(conn.connectionId);
        }

        /// <summary>
        /// Ошибка транспорта по соединению. Mirror сам пишет её строкой «Server Transport Error»;
        /// здесь она только запоминается — следом обычно приходит отключение (таймаут KCP).
        /// </summary>
        public override void OnServerError(NetworkConnectionToClient conn, TransportError error, string reason)
        {
            base.OnServerError(conn, error, reason);
            if (conn != null) _serverLastError[conn.connectionId] = error;
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (PlayersManager.Instance != null)
            {
                // Аватар уничтожит Mirror в base-вызове ниже — освобождаем его заранее, как
                // при смене скина: иначе у хоста и клиентов остаётся захват мёртвой руки.
                // Снимок сессии (UnregisterSession) снимается уже после: здоровье и место
                // от сброса снаряжения не зависят.
                PlayerSession session = PlayersManager.Instance.GetSession(conn);
                LogServerDisconnect(conn, session);
                if (session != null)
                {
                    AvatarTeardown.ReleaseBeforeDestroy(session.ActiveAvatar, "отключение");
                }

                PlayersManager.Instance.UnregisterSession(conn);
            }
            else
            {
                LogServerDisconnect(conn, null);
            }

            _serverConnectedAt.Remove(conn.connectionId);
            _serverLastError.Remove(conn.connectionId);

            base.OnServerDisconnect(conn); // Mirror автоматически уничтожает Owned objects (сессию и аватар)
        }

        /// <summary>
        /// Одна строка на отключение: кто, почему (по последней ошибке транспорта), сколько был
        /// подключён и в какой момент матча. Кика в игре нет: без ошибки транспорта — «closed»
        /// (клиент вышел сам или соединение закрыл сервер).
        /// </summary>
        private void LogServerDisconnect(NetworkConnectionToClient conn, PlayerSession session)
        {
            if (!GameLog.Network.IsEnabled(LogLevel.Info)) return;

            string reason = _serverLastError.TryGetValue(conn.connectionId, out TransportError error)
                ? (error == TransportError.Timeout ? "timeout" : error.ToString())
                : "closed";
            string after = _serverConnectedAt.TryGetValue(conn.connectionId, out double connectedAt)
                ? FormatDuration(Time.realtimeSinceStartupAsDouble - connectedAt)
                : "неизвестно";
            string alive = session == null || session.Role != GameRole.Player
                ? "—"
                : session.ActiveAvatar != null && session.ActiveAvatar.IsAlive ? "да" : "нет";

            GameLog.Network.Info($"[Net] disconnect conn={conn.connectionId} player={PlayerLogName.Of(session)} " +
                                 $"reason={reason} after={after} phase={DescribeMatchPhase()} alive={alive}");
        }

        private static string FormatDuration(double seconds)
        {
            int total = Mathf.Max(0, (int)seconds);
            return $"{total / 60:00}:{total % 60:00}";
        }

        /// <summary>Карта, состояние матча и, если режим раундовый, раунд и его фаза — для строк лога.</summary>
        private static string DescribeMatchPhase()
        {
            string map = SceneManager.GetActiveScene().name;
            MapReferee referee = MapReferee.Instance;
            if (referee == null) return $"{map}/нет судьи";

            VrBattlegrounds.GameModes.GameMode mode = referee.ActiveGameMode;
            string text = $"{map}/{referee.CurrentState}/{(mode != null ? mode.GetType().Name : "нет режима")}";
            if (mode is VrBattlegrounds.GameModes.EliminationMode elimination)
                text += $" раунд {elimination.CurrentRoundNumber}/{elimination.TotalRounds} {elimination.CurrentRoundPhase}";
            return text;
        }

        // ==============================================================================
        // CUSTOM SPAWN LOGIC (Role-based)
        // ==============================================================================

        public override void OnClientSceneChanged()
        {
#if UNITY_EDITOR
            VrBattlegrounds.DevTools.DebugBootstrapGate.EditorNetworkDiagnosticEvent?.Invoke("client-scene-end", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
#endif
            base.OnClientSceneChanged();
            if (NetworkClient.ready && NetworkClient.connection.identity == null)
                SendConnectMessage();

            ClientSceneChanged?.Invoke();
        }

        public override void OnClientConnect()
        {
#if UNITY_EDITOR
            VrBattlegrounds.DevTools.DebugBootstrapGate.EditorNetworkDiagnosticEvent?.Invoke("client-connected", networkAddress);
#endif
            base.OnClientConnect();
            GameNetworkDiscovery.NotifyClientConnected();
            if (NetworkClient.ready && NetworkClient.connection.identity == null)
                SendConnectMessage();
        }

        public override void OnClientError(TransportError error, string reason)
        {
            base.OnClientError(error, reason);
            GameNetworkDiscovery.NotifyClientError($"{error}: {reason}");
        }

        public override void OnClientChangeScene(string newSceneName, SceneOperation sceneOperation, bool customHandling)
        {
#if UNITY_EDITOR
            VrBattlegrounds.DevTools.DebugBootstrapGate.EditorNetworkDiagnosticEvent?.Invoke("client-scene-begin", newSceneName);
#endif
            base.OnClientChangeScene(newSceneName, sceneOperation, customHandling);
        }

        private void SendConnectMessage()
        {
            string token = VrBattlegrounds.Core.ClientDeviceIdentity.ConnectionToken;

            if (LocalClientProfile.LocalRole == GameRole.Player)
            {
                var msg = new GamePlayerConnectMessage(
                    token, 
                    LocalClientProfile.LocalDeviceType, 
                    0, 
                    0
                );

                NetworkClient.Send(msg);
            }
            else
            {
                var msg = new SpectatorConnectMessage
                {
                    deviceToken = token,
                    deviceType = LocalClientProfile.LocalDeviceType,
                    isAdmin = LocalClientProfile.IsLocalAdmin
                };
                NetworkClient.Send(msg);
            }
        }

        // Removed SetupPlayerSession as it's now internal to PlayersManager

        private void OnGamePlayerConnect(NetworkConnectionToClient conn, GamePlayerConnectMessage msg)
        {
            if (PlayersManager.Instance != null)
            {
                PlayersManager.Instance.HandlePlayerConnect(conn, msg);
            }
            else
            {
                GameLog.Error("[GameNetworkManager] PlayersManager is missing! Cannot handle player connection.");
            }
        }

        private void OnSpectatorConnect(NetworkConnectionToClient conn, SpectatorConnectMessage msg)
        {
            if (PlayersManager.Instance != null)
            {
                PlayersManager.Instance.HandleSpectatorConnect(conn, msg);
            }
            else
            {
                GameLog.Error("[GameNetworkManager] PlayersManager is missing! Cannot handle spectator connection.");
            }
        }

    }
}
