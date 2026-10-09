// Ярус C (два процесса) — сценарий map-run-relay-barrier: барьер начального снимка Relay (map-runtime-bootstrap, задача 5).
#if !VRBG_NO_E2E
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    /// Сценарий <c>map-run-relay-barrier</c>: удалённый клиент получает свежий снимок UltimateXR только для своего
    /// запуска карты и только после его локальной готовности.
    ///
    /// <para>
    /// <b>Ход.</b> Выделенный сервер ждёт первого клиента, начинает серию из одной карты и ждёт server Ready.
    /// Второй клиент приходит позже, на уже идущую карту (дирижёр: <c>-LateClientDelay</c>): его снимок снят при
    /// живых аватаре и предметах первого. Затем сервер перезагружает ту же карту: новый <see cref="MapRunKey"/>,
    /// ответ старого запуска клиенту больше не годится.
    /// </para>
    ///
    /// <para>
    /// <b>Вердикт клиента.</b> На карте открывается <see cref="MapRunAdmission.IsLocalPlayable"/>, канал Relay открыт
    /// ровно для ключа принятого run и descriptor; после перезагрузки — для нового, большего ключа; ни одного адресата
    /// снимка без регистрации (<c>InitialStateInventory</c>). Сценарий только наблюдает.
    /// </para>
    ///
    /// <para>
    /// <b>Вариант <c>map-run-startup-route</c>.</b> Сервер до старта сети запрашивает первую сцену через
    /// <see cref="ServerStartupRoute"/> (карта прогона, режим elimination) и стартует сразу в неё, минуя Lobby; клиенты
    /// подключаются уже к идущей карте, второй — позже. Дальше — тот же поздний клиент и перезагрузка.
    /// </para>
    /// </summary>
    public class MapRunRelayBarrierScenario : IE2EScenario, IE2EServerStartup
    {
        private readonly bool _startupRoute;

        public MapRunRelayBarrierScenario() : this(false) { }

        public MapRunRelayBarrierScenario(bool startupRoute) => _startupRoute = startupRoute;

        public string Name => _startupRoute ? "map-run-startup-route" : "map-run-relay-barrier";

        private const string StartupMode = "elimination";
        private const string CheckStartupRoute = "сервер стартовал сразу в карту маршрутом старта, без Lobby, с режимом серии";

        // Подготовка и прогон — разные экземпляры (E2ERunner.Resolve), поэтому общее состояние процесса статично.
        private static readonly List<string> ServerScenes = new List<string>();
        private static string _routeError;

        public string BeforeNetworkStart(E2EContext context)
        {
            if (!_startupRoute) return null;
            ServerScenes.Clear();
            SceneManager.sceneLoaded += (scene, mode) => ServerScenes.Add(scene.name);
            _routeError = ServerStartupRoute.TryRequest(context.Map, StartupMode, "e2e-" + Name, out _, out string error)
                ? null : error;
            return _routeError;
        }

        private const string CheckDedicated = "сервер поднят как выделенный (ServerOnly)";
        private const string CheckFirstClient = "первый клиент подключился";
        private const string CheckMapReady = "карта собрана MapBootstrap и открыла server Ready";
        private const string CheckLateClient = "поздний клиент вошёл на идущую карту";
        private const string CheckReload = "перезагрузка той же карты дала новый запуск и server Ready";

        private const string CheckConnected = "клиент подключился к серверу";
        private const string CheckFirstRun = "на карте LocalPlayable открыт, снимок применён для ключа принятого run";
        private const string CheckTargets = "все адресаты снимков зарегистрированы на клиенте";
        private const string CheckReloadRun = "после перезагрузки канал открыт для нового, большего ключа";

        private const string MissingTargetsMarker = "не зарегистрированы на клиенте";

        private int _missingTargetErrors;

        private static float Now => Time.realtimeSinceStartup;

        public IEnumerator Run(E2EContext context, E2EResult result) =>
            context.IsServerRole ? RunServer(context, result) : RunClient(context, result);

        // ── Сервер ───────────────────────────────────────────────────────────

        private IEnumerator RunServer(E2EContext context, E2EResult result)
        {
            if (_startupRoute) result.Declare(CheckDedicated, CheckStartupRoute, CheckFirstClient, CheckMapReady, CheckLateClient, CheckReload);
            else result.Declare(CheckDedicated, CheckFirstClient, CheckMapReady, CheckLateClient, CheckReload);

            float deadline = Now + 60f;
            while (!NetworkServer.active && Now < deadline) yield return null;
            bool dedicated = NetworkServer.active && !NetworkClient.active;
            result.Set(CheckDedicated, dedicated, $"NetworkServer.active={NetworkServer.active}, NetworkClient.active={NetworkClient.active}");
            if (!dedicated) { result.Summary = "не выделенный сервер, прогон недействителен"; yield break; }

            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");

            SessionManager session = SessionManager.Instance;
            var ready = new E2EWaitOutcome();
            MapRunKey firstKey;
            if (_startupRoute)
            {
                // Карта грузится сама — первой сценой сервера; клиенты придут уже в неё.
                yield return E2EWait.Until(ready, "server Ready карты", 120f, () => ServerReadyKey(context.Map).IsValid,
                    () => $"сцена '{SceneManager.GetActiveScene().name}', запуск {DescribeRun()}, маршрут {ServerStartupRoute.State}");
                firstKey = ServerReadyKey(context.Map);
                result.Set(CheckMapReady, ready.Succeeded, ready.Succeeded ? $"запуск {firstKey}" : ready.Diagnosis);
                if (!ready.Succeeded) { result.Summary = "карта не собралась"; yield break; }

                Series series = Series.Instance;
                string mode = series != null ? series.CapturedModeId : null;
                bool routed = _routeError == null && !ServerScenes.Contains("Lobby") && ServerScenes.Contains(context.Map)
                              && mode == StartupMode && ServerStartupRoute.State == StartupRouteState.None;
                result.Set(CheckStartupRoute, routed, $"запрос: {_routeError ?? "принят"}; сцены сервера: " +
                           $"{string.Join(" → ", ServerScenes)}; режим серии '{mode}'; маршрут {ServerStartupRoute.State}");
                if (!routed) { result.Summary = "сервер стартовал не маршрутом"; yield break; }

                var first = new E2EWaitOutcome();
                yield return E2EWait.Until(first, "первая сессия", 120f, () => SessionCount() >= 1, () => $"сессий {SessionCount()}");
                result.Set(CheckFirstClient, first.Succeeded, first.Diagnosis);
                if (!first.Succeeded) { result.Summary = "клиентов нет"; yield break; }
                AssignTeams(session);
            }
            else
            {
                var first = new E2EWaitOutcome();
                yield return E2EWait.Until(first, "первая сессия", 90f, () => SessionCount() >= 1, () => $"сессий {SessionCount()}");
                result.Set(CheckFirstClient, first.Succeeded, first.Diagnosis);
                if (!first.Succeeded) { result.Summary = "клиентов нет"; yield break; }

                session.SetSession(context.Map, "elimination");
                AssignTeams(session);
                session.StartSession();

                yield return E2EWait.Until(ready, "server Ready карты", 120f, () => ServerReadyKey(context.Map).IsValid,
                    () => $"сцена '{SceneManager.GetActiveScene().name}', запуск {DescribeRun()}");
                firstKey = ServerReadyKey(context.Map);
                result.Set(CheckMapReady, ready.Succeeded, ready.Succeeded ? $"запуск {firstKey}" : ready.Diagnosis);
                if (!ready.Succeeded) { result.Summary = "карта не собралась"; yield break; }
            }

            var late = new E2EWaitOutcome();
            yield return E2EWait.Until(late, "поздний клиент", 180f, () => SessionCount() >= context.ExpectedClients,
                () => $"сессий {SessionCount()} из {context.ExpectedClients}");
            AssignTeams(session);
            result.Set(CheckLateClient, late.Succeeded, late.Succeeded
                ? $"сессий {SessionCount()} на запуске {ServerReadyKey(context.Map)}" : late.Diagnosis);
            if (!late.Succeeded) { result.Summary = "поздний клиент не пришёл"; yield break; }

            // Клиентам — время принять снимок первого запуска и отчитаться в своём вердикте.
            yield return E2EWait.Hold(20f);

            bool accepted = MapLoader.Instance != null && MapLoader.Instance.LoadMap(context.Map);
            var reload = new E2EWaitOutcome();
            yield return E2EWait.Until(reload, "server Ready перезагруженной карты", 120f,
                () => accepted && ServerReadyKey(context.Map).IsValid && ServerReadyKey(context.Map) != firstKey,
                () => $"загрузка принята={accepted}, запуск {DescribeRun()}");
            result.Set(CheckReload, reload.Succeeded, reload.Succeeded
                ? $"запуск {firstKey} → {ServerReadyKey(context.Map)}" : reload.Diagnosis);

            // Клиенты проверяют второй запуск — сервер не гасит процесс раньше них.
            yield return E2EWait.Hold(40f);
            // Итог (зелёный или нет) пишет E2ERunner после завершения: здесь статус ещё «started», и Passed ложен.
        }

        private static MapRunKey ServerReadyKey(string map)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != map) return default;
            MapBootstrap bootstrap = MapBootstrap.ForScene(scene);
            return bootstrap != null && bootstrap.IsServerReady ? bootstrap.LocalRunKey : default;
        }

        private static string DescribeRun()
        {
            MapRunAuthority authority = MapRunAuthority.Instance;
            return authority != null ? $"{authority.Current.Key} {authority.Current.Status}" : "authority нет";
        }

        private static int SessionCount() => PlayersManager.Instance != null ? PlayersManager.Instance.Sessions.Count : 0;

        /// <summary>Команды по кругу: без индекса команды AvatarManager не создаёт тело.</summary>
        private static void AssignTeams(SessionManager session)
        {
            GameModeData mode = session.SelectedGameModeData;
            if (mode == null || mode.teams == null || mode.teams.Length == 0) return;
            IReadOnlyList<PlayerSession> sessions = PlayersManager.Instance.Sessions;
            for (int i = 0; i < sessions.Count; i++)
                if (sessions[i].TeamIndex == 0 && mode.teams[i % mode.teams.Length] != null)
                    sessions[i].TeamIndex = mode.teams[i % mode.teams.Length].teamIndex;
        }

        // ── Клиент ───────────────────────────────────────────────────────────

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            result.Declare(CheckConnected, CheckFirstRun, CheckTargets, CheckReloadRun);
            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");
            Application.logMessageReceived += CountMissingTargets;
            try
            {
                var connected = new E2EWaitOutcome();
                yield return E2EWait.Until(connected, "подключение", 240f, () => NetworkClient.isConnected);
                result.Set(CheckConnected, connected.Succeeded, connected.Diagnosis);
                if (!connected.Succeeded) { result.Summary = "клиент не подключился"; yield break; }

                var firstRun = new E2EWaitOutcome();
                yield return E2EWait.Until(firstRun, "LocalPlayable на карте", 240f,
                    () => PlayableKey(context.Map).IsValid, () => DescribeClient());
                MapRunKey firstKey = PlayableKey(context.Map);
                bool sameAsDescriptor = MapRunAuthority.Instance != null && MapRunAuthority.Instance.Current.Key == firstKey;
                result.Set(CheckFirstRun, firstRun.Succeeded && sameAsDescriptor,
                    firstRun.Succeeded ? $"запуск {firstKey}, совпадает с descriptor: {sameAsDescriptor}" : firstRun.Diagnosis);

                var reloadRun = new E2EWaitOutcome();
                yield return E2EWait.Until(reloadRun, "LocalPlayable перезагруженной карты", 300f,
                    () => PlayableKey(context.Map).IsValid && PlayableKey(context.Map).LoadSequence > firstKey.LoadSequence,
                    () => DescribeClient());
                result.Set(CheckReloadRun, reloadRun.Succeeded,
                    reloadRun.Succeeded ? $"запуск {firstKey} → {PlayableKey(context.Map)}" : reloadRun.Diagnosis);

                result.Set(CheckTargets, _missingTargetErrors == 0,
                    $"ошибок «адресат снимка не зарегистрирован»: {_missingTargetErrors}");
            }
            finally
            {
                Application.logMessageReceived -= CountMissingTargets;
            }
        }

        /// <summary>Ключ запуска карты, для которого на этом клиенте открыт LocalPlayable; иначе пустой.</summary>
        private static MapRunKey PlayableKey(string map)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != map || !MapRunAdmission.ComputeLocalPlayable(scene)) return default;
            MapBootstrap bootstrap = MapBootstrap.ForScene(scene);
            NetworkStateRelay relay = NetworkStateRelay.Instance;
            if (bootstrap == null || relay == null) return default;
            MapRunKey key = bootstrap.LocalRunKey;
            return relay.HasInitialState(key) ? key : default;
        }

        private static string DescribeClient()
        {
            Scene scene = SceneManager.GetActiveScene();
            MapBootstrap bootstrap = MapBootstrap.ForScene(scene);
            return $"сцена '{scene.name}', run {(bootstrap != null ? bootstrap.LocalRunKey.ToString() : "нет")}, " +
                   $"descriptor {DescribeRun()}, LocalPlayable {MapRunAdmission.ComputeLocalPlayable(scene)}";
        }

        private void CountMissingTargets(string message, string stackTrace, LogType type)
        {
            if (message != null && message.Contains(MissingTargetsMarker)) _missingTargetErrors++;
        }
    }
}
#endif
