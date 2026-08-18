// Ярус C (два процесса) — сценарий dedicated-server-arsenal, находка NET-06.
#if !VRBG_NO_E2E
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    /// Сценарий <c>dedicated-server-arsenal</c> — находка <b>NET-06</b>.
    ///
    /// Что доказывает. Смена фазы раунда рассылается через
    /// <c>[ClientRpc] EliminationMode.RpcOnRoundStateChanged</c>, который поднимает
    /// статическое событие <see cref="EliminationMode.OnRoundStateChangedLocal"/>.
    /// В режиме <c>ServerOnly</c> ClientRpc локально не исполняется, поэтому на
    /// выделенном сервере событие не срабатывает, и
    /// <c>ArsenalWallController.HandleRoundStateChanged</c> не вызывается никогда:
    /// стена не открывается и слоты не пополняются. На хосте баг не виден —
    /// там сервер сам является клиентом и Rpc исполняется локально.
    ///
    /// Роли:
    /// <list type="bullet">
    /// <item><c>server</c> — гонит матч и выносит вердикт;</item>
    /// <item><c>client-N</c> — подключается и занимает место в команде,
    ///       без двух игроков матч не стартует (<c>minPlayersToStart = 2</c>,
    ///       плюс требование «в каждой команде есть игрок»).</item>
    /// </list>
    ///
    /// Ожидаемый результат на текущем коде — <b>красный</b>: зелёные проверки 1–5
    /// подтверждают, что конфигурация действительно собралась и матч пошёл,
    /// красные 6–7 — это сама находка. Зелёный итог означал бы, что сценарий
    /// ничего не проверяет.
    /// </summary>
    public class DedicatedServerArsenalScenario : IE2EScenario
    {
        public string Name => "dedicated-server-arsenal";

        // ── Имена проверок (они же ключи в JSON) ───────────────────────────

        private const string CheckDedicated = "сервер поднят как выделенный (ServerOnly, не хост)";
        private const string CheckClients   = "клиенты подключились и сервер создал сессии";
        private const string CheckMap       = "карта загружена, стена арсенала найдена на сервере";
        private const string CheckInitial   = "OnStartServer заполнил слоты арсенала на сервере";
        private const string CheckPhase     = "серверная машина раунда сменила фазу Setup -> Equipment";
        private const string CheckEvent     = "смена фазы раунда дошла до локальных подписчиков на сервере";
        private const string CheckArsenal   = "стена арсенала открылась в фазе Equipment на сервере";

        private const string CheckClientConnected = "клиент подключился к серверу";
        private const string CheckClientSession   = "сервер создал сессию для клиента";
        private const string CheckClientMap       = "клиент переехал на карту вместе с сервером";
        private const string CheckClientEvent     = "смена фазы раунда дошла до клиента (контроль к NET-06)";

        // ── Наблюдение ────────────────────────────────────────────────────

        /// <summary>Фазы, пришедшие через <c>OnRoundStateChangedLocal</c> (то, что ловит арсенал).</summary>
        private readonly List<RoundState> _eventPhases = new List<RoundState>();

        /// <summary>Фазы, увиденные опросом SyncVar (то, что реально происходит на сервере).</summary>
        private readonly List<RoundState> _syncVarPhases = new List<RoundState>();

        private LogLevel Log => GameSettings.Instance.LogLevelDebug;

        public IEnumerator Run(E2EContext context, E2EResult result)
        {
            if (context.IsServerRole)
                return RunServer(context, result);

            return RunClient(context, result);
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль сервера
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunServer(E2EContext context, E2EResult result)
        {
            result.Declare(CheckDedicated, CheckClients, CheckMap, CheckInitial,
                           CheckPhase, CheckEvent, CheckArsenal);

            // ── 1. Выделенный сервер ──────────────────────────────────────
            float deadline = Now + 60f;
            while (!NetworkServer.active && Now < deadline)
                yield return null;

            if (!NetworkServer.active)
            {
                result.Set(CheckDedicated, false,
                    "NetworkServer.active так и не стал true за 60 с — сервер не поднялся. " +
                    "Проверь, что процесс запущен с -batchmode -nographics: роль выбирается по Mirror.Utils.IsHeadless().");
                result.Summary = "сервер не поднялся, прогон недействителен";
                yield break;
            }

            bool dedicated = NetworkServer.active && !NetworkClient.active;
            result.Set(CheckDedicated, dedicated,
                dedicated
                    ? "NetworkServer.active=true, NetworkClient.active=false — это ServerOnly"
                    : "процесс поднялся хостом (NetworkClient.active=true) — в такой конфигурации NET-06 не воспроизводится, " +
                      "сценарий бессмыслен. Убедись, что сервер запущен с -batchmode -nographics.");

            if (!dedicated)
            {
                result.Summary = "конфигурация не выделенный сервер, прогон недействителен";
                yield break;
            }

            // Дирижёром прогона должен быть сценарий, а не DebugOrchestrator:
            // его настройки лежат в общем ассете DebugBootstrapConfig, который
            // правится под текущую отладку, и результат прогона от них зависеть не должен.
            DisableDebugOrchestrator();

            // ── 2. Клиенты ────────────────────────────────────────────────
            deadline = Now + 90f;
            while (SessionCount() < context.ExpectedClients && Now < deadline)
                yield return null;

            int sessions = SessionCount();
            bool clientsOk = sessions >= context.ExpectedClients;
            result.Set(CheckClients, clientsOk,
                clientsOk
                    ? $"сессий на сервере: {sessions} (ждали {context.ExpectedClients})"
                    : $"за 90 с подключилось сессий: {sessions} из {context.ExpectedClients}. " +
                      "Клиенты не нашли сервер (Mirror NetworkDiscovery — UDP-броадкаст) " +
                      "или их процессы упали — смотри client-*.log рядом с этим файлом.");

            if (!clientsOk)
            {
                result.Summary = "клиенты не подключились, вердикт по NET-06 вынести нельзя";
                yield break;
            }

            // ── 3. Команды и карта ────────────────────────────────────────
            SessionManager sessionManager = SessionManager.Instance;
            if (sessionManager == null)
            {
                result.Set(CheckMap, false, "SessionManager.Instance == null — некому выбрать карту и режим");
                result.Summary = "SessionManager отсутствует, прогон недействителен";
                yield break;
            }

            sessionManager.SetSession(context.Map, "elimination");
            yield return null;

            string teamsReport = AssignTeams(sessionManager);
            GameLog.Info(Log, $"[E2E] Команды распределены: {teamsReport}");

            if (MapManager.Instance == null)
            {
                result.Set(CheckMap, false, "MapManager.Instance == null — некому загрузить карту");
                result.Summary = "MapManager отсутствует, прогон недействителен";
                yield break;
            }

            MapManager.Instance.LoadMap(context.Map);

            deadline = Now + 90f;
            while ((SceneManager.GetActiveScene().name != context.Map || GameplayManager.Instance == null)
                   && Now < deadline)
                yield return null;

            if (SceneManager.GetActiveScene().name != context.Map || GameplayManager.Instance == null)
            {
                result.Set(CheckMap, false,
                    $"за 90 с карта не собралась: активная сцена='{SceneManager.GetActiveScene().name}' " +
                    $"(ждали '{context.Map}'), GameplayManager.Instance={(GameplayManager.Instance == null ? "null" : "есть")}");
                result.Summary = "карта не загрузилась, вердикт по NET-06 вынести нельзя";
                yield break;
            }

            // Стена арсенала — сетевой объект сцены: Mirror поднимает его
            // в SpawnObjects() после смены сцены, а OnStartServer идёт уже там.
            ArsenalWallController[] walls = new ArsenalWallController[0];
            deadline = Now + 30f;
            while (walls.Length == 0 && Now < deadline)
            {
                walls = Object.FindObjectsByType<ArsenalWallController>(FindObjectsInactive.Include);
                if (walls.Length == 0)
                    yield return null;
            }

            result.Set(CheckMap, walls.Length > 0,
                walls.Length > 0
                    ? $"активная сцена='{SceneManager.GetActiveScene().name}', стен арсенала на карте: {walls.Length}"
                    : $"на карте '{context.Map}' не нашлось ни одного ArsenalWallController — " +
                      "проверить нечего, возьми другую карту через -e2eMap");

            if (walls.Length == 0)
            {
                result.Summary = "на карте нет стены арсенала, вердикт по NET-06 вынести нельзя";
                yield break;
            }

            // ── 4. Начальное пополнение (OnStartServer) ───────────────────
            // Занятость слота смотрим по WeaponComponent под якорем, а не по
            // ArsenalSlotController.IsItemPresent: последний читает
            // UxrGrabbableObjectAnchor.CurrentPlacedObject, а AssignNetworkItem
            // только перепарентит объект и через UxrGrabManager.PlaceObject
            // не проходит — значит CurrentPlacedObject остаётся null всегда
            // и IsItemPresent врёт независимо от роли процесса.
            int slotsTotal = 0;
            int configured = 0;
            int withWeapon = 0;
            int reportedPresent = 0;
            foreach (ArsenalWallController wall in walls)
            {
                foreach (ArsenalSlotController slot in wall.GetComponentsInChildren<ArsenalSlotController>(true))
                {
                    slotsTotal++;
                    if (slot.IsConfigured) configured++;
                    if (slot.IsItemPresent) reportedPresent++;
                    if (CountWeapons(slot) > 0) withWeapon++;
                }
            }

            bool initialOk = configured > 0 && withWeapon > 0;
            result.Set(CheckInitial, initialOk,
                $"слотов на всех стенах: {slotsTotal}, настроено оружием: {configured}, " +
                $"с реально заспавненным оружием: {withWeapon} " +
                $"(IsItemPresent при этом показывает {reportedPresent}: AssignNetworkItem только перепарентит объект " +
                "и не проходит через UxrGrabManager, поэтому CurrentPlacedObject остаётся null). " +
                (initialOk
                    ? "ReplenishWeaponsNetwork(true) из OnStartServer отработал — значит харнесс видит состояние слотов"
                    : configured == 0
                        ? "ни один слот не настроен WeaponInfo/WeaponPrefab — на этой карте пополнять нечего, возьми другую карту через -e2eMap"
                        : "слоты настроены, но оружия под якорями нет: не сработало даже начальное пополнение из OnStartServer"));

            if (!initialOk)
            {
                result.Summary = "состояние слотов непригодно для проверки, вердикт по NET-06 вынести нельзя";
                yield break;
            }

            string arsenalStatesBefore = DescribeWallStates(walls);

            // ── 5. Матч и фазы раунда ─────────────────────────────────────
            EliminationMode.OnRoundStateChangedLocal += OnRoundStateEvent;

            try
            {
                GameplayManager.Instance.StartGameplay();

                EliminationMode elimination = null;
                deadline = Now + 60f;
                while (elimination == null && Now < deadline)
                {
                    elimination = GameplayManager.Instance.ActiveGameMode as EliminationMode;
                    if (elimination == null)
                        yield return null;
                }

                if (elimination == null)
                {
                    result.Set(CheckPhase, false,
                        "за 60 с GameplayManager.ActiveGameMode не стал EliminationMode — матч не запустился. " +
                        $"Режим из SessionManager: '{sessionManager.SelectedModeId}'.");
                    result.Summary = "матч не запустился, вердикт по NET-06 вынести нельзя";
                    yield break;
                }

                RoundState observed = elimination.CurrentRoundState;
                _syncVarPhases.Add(observed);

                // Ждём первую смену фазы: Setup длится 1 с (RoundManager.SetupDuration),
                // но до неё матч должен выйти из WaitingForPlayers — а это требует
                // игрока в каждой команде.
                deadline = Now + 90f;
                while (elimination.CurrentRoundState == RoundState.Setup && Now < deadline)
                    yield return null;

                observed = elimination.CurrentRoundState;
                if (_syncVarPhases.Count == 0 || _syncVarPhases[_syncVarPhases.Count - 1] != observed)
                    _syncVarPhases.Add(observed);

                bool phaseOk = observed != RoundState.Setup;
                result.Set(CheckPhase, phaseOk,
                    phaseOk
                        ? $"фаза сменилась на {observed}; матч={elimination.CurrentMatchState}; " +
                          $"наблюдённые фазы (SyncVar): {Join(_syncVarPhases)}"
                        : $"за 90 с фаза осталась Setup; матч={elimination.CurrentMatchState}; " +
                          $"команды: {teamsReport}. Матч не вышел из WaitingForPlayers: " +
                          "EliminationMode.IsPlayersReady требует, чтобы игрок был в каждой команде.");

                if (!phaseOk)
                {
                    result.Summary = "фаза раунда не сменилась, вердикт по NET-06 вынести нельзя";
                    yield break;
                }

                // Даём серверу время отреагировать на смену фазы: анимация открытия
                // стены запускается из HandleRoundStateChanged синхронно, но пусть
                // пройдёт заведомо больше кадров, чем нужно.
                float settle = Now + 5f;
                while (Now < settle)
                {
                    RoundState current = elimination.CurrentRoundState;
                    if (_syncVarPhases[_syncVarPhases.Count - 1] != current)
                        _syncVarPhases.Add(current);

                    yield return null;
                }

                // ── 6. Дошло ли событие до локальных подписчиков ──────────
                bool eventOk = _eventPhases.Count > 0;
                result.Set(CheckEvent, eventOk,
                    eventOk
                        ? $"EliminationMode.OnRoundStateChangedLocal сработало {_eventPhases.Count} раз(а): {Join(_eventPhases)}"
                        : "EliminationMode.OnRoundStateChangedLocal не сработало ни разу, хотя SyncVar-фазы менялись: " +
                          $"{Join(_syncVarPhases)}. Это NET-06: событие поднимается только внутри " +
                          "[ClientRpc] RpcOnRoundStateChanged, а в режиме ServerOnly ClientRpc локально не исполняется.");

                // ── 7. Открылась ли хотя бы одна стена ───────────────────
                int opened = 0;
                foreach (ArsenalWallController wall in walls)
                {
                    if (wall.CurrentState != ArsenalWallController.ArsenalState.Closed)
                        opened++;
                }

                string arsenalStatesAfter = DescribeWallStates(walls);
                bool arsenalOk = opened > 0;
                result.Set(CheckArsenal, arsenalOk,
                    arsenalOk
                        ? $"открылось стен: {opened} из {walls.Length}. Состояния: {arsenalStatesBefore} -> {arsenalStatesAfter}"
                        : $"ни одна из {walls.Length} стен не вышла из Closed: {arsenalStatesBefore} -> {arsenalStatesAfter}, " +
                          $"хотя фаза раунда прошла {Join(_syncVarPhases)}. " +
                          "ArsenalWallController.HandleRoundStateChanged на выделенном сервере не вызывается " +
                          "(следствие NET-06), поэтому ни OpenArsenal, ни ReplenishWeaponsNetwork(false) не выполняются.");

                result.Summary = result.AllChecksGreen
                    ? "все проверки зелёные — NET-06 больше не воспроизводится"
                    : (!eventOk || !arsenalOk)
                        ? "NET-06 подтверждена: конфигурация собралась (проверки 1-5 зелёные), " +
                          "но смена фазы раунда до серверной логики не доходит"
                        : "есть красные проверки, см. detail";
            }
            finally
            {
                EliminationMode.OnRoundStateChangedLocal -= OnRoundStateEvent;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль клиента
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            result.Declare(CheckClientConnected, CheckClientSession, CheckClientMap, CheckClientEvent);

            DisableDebugOrchestrator();

            // Контроль к NET-06. Клиент подписывается на то же самое статическое
            // событие, что и стена арсенала на сервере. Если на клиенте оно
            // сработает, а на сервере нет, отличие изолировано до роли процесса:
            // код, событие и способ наблюдения одни и те же.
            EliminationMode.OnRoundStateChangedLocal += OnRoundStateEvent;

            // Штатный путь — Mirror NetworkDiscovery (UDP-броадкаст), его запускает
            // GameNetworkDiscovery. Если броадкаст не доехал (частая беда на одной
            // машине с несколькими сетевыми интерфейсами) — подключаемся напрямую.
            float deadline = Now + 30f;
            while (!NetworkClient.isConnected && Now < deadline)
                yield return null;

            if (!NetworkClient.isConnected && !string.IsNullOrEmpty(context.ServerAddress))
            {
                GameLog.Info(Log, $"[E2E] Discovery молчит 30 с, подключаюсь напрямую к {context.ServerAddress}");

                Mirror.Discovery.NetworkDiscovery discovery = Object.FindFirstObjectByType<Mirror.Discovery.NetworkDiscovery>();
                if (discovery != null)
                    discovery.StopDiscovery();

                if (NetworkManager.singleton != null && !NetworkClient.active)
                {
                    NetworkManager.singleton.networkAddress = context.ServerAddress;
                    NetworkManager.singleton.StartClient();
                }

                deadline = Now + 30f;
                while (!NetworkClient.isConnected && Now < deadline)
                    yield return null;
            }

            bool connected = NetworkClient.isConnected;
            result.Set(CheckClientConnected, connected,
                connected
                    ? $"подключён к {NetworkManager.singleton?.networkAddress}"
                    : "за 60 с подключиться не удалось: ни Discovery, ни прямое подключение " +
                      $"к '{context.ServerAddress}' не сработали");

            if (!connected)
            {
                result.Summary = "клиент не подключился";
                yield break;
            }

            deadline = Now + 60f;
            while (PlayerSession.LocalSession == null && Now < deadline)
                yield return null;

            bool hasSession = PlayerSession.LocalSession != null;
            result.Set(CheckClientSession, hasSession,
                hasSession
                    ? $"PlayerSession.LocalSession netId={PlayerSession.LocalSession.netId}"
                    : "за 60 с сервер не создал PlayerSession для этого клиента " +
                      "(GamePlayerConnectMessage не дошёл или PlayersManager его не обработал)");

            deadline = Now + 120f;
            while (SceneManager.GetActiveScene().name != context.Map && Now < deadline)
                yield return null;

            bool onMap = SceneManager.GetActiveScene().name == context.Map;
            result.Set(CheckClientMap, onMap,
                onMap
                    ? $"активная сцена='{SceneManager.GetActiveScene().name}'"
                    : $"за 120 с клиент не переехал на '{context.Map}', " +
                      $"остался в '{SceneManager.GetActiveScene().name}'");

            // Матч на сервере стартует только когда подключатся все клиенты,
            // поэтому событие приходит позже остальных проверок.
            deadline = Now + 120f;
            while (_eventPhases.Count == 0 && Now < deadline)
                yield return null;

            EliminationMode.OnRoundStateChangedLocal -= OnRoundStateEvent;

            bool eventOk = _eventPhases.Count > 0;
            result.Set(CheckClientEvent, eventOk,
                eventOk
                    ? $"OnRoundStateChangedLocal сработало {_eventPhases.Count} раз(а): {Join(_eventPhases)}. " +
                      "На клиенте ClientRpc исполняется, значит красная проверка на сервере — " +
                      "не дефект харнесса, а разница ролей (NET-06)"
                    : "за 120 с OnRoundStateChangedLocal не сработало и на клиенте. " +
                      "Тогда сигнал теряется раньше, чем в ClientRpc: смотри server.log на предмет " +
                      "смены фазы раунда вообще");

            result.Summary = result.AllChecksGreen
                ? "клиент подключился, получил сессию, карту и увидел смену фазы раунда"
                : "клиент не дошёл до состояния, в котором сервер может выносить вердикт";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное
        // ══════════════════════════════════════════════════════════════════

        private static float Now => Time.realtimeSinceStartup;

        private void OnRoundStateEvent(RoundState state)
        {
            _eventPhases.Add(state);
            GameLog.Info(Log, $"[E2E] OnRoundStateChangedLocal -> {state}");
        }

        /// <summary>
        /// Сколько единиц оружия реально лежит в слоте. Смотрим на
        /// <see cref="WeaponComponent"/> под якорем слота: именно его навешивает
        /// <c>ArsenalSlotController.AssignNetworkItem</c>. Публичное
        /// <c>IsItemPresent</c> для этого не годится — оно читает
        /// <c>UxrGrabbableObjectAnchor.CurrentPlacedObject</c>, а тот
        /// заполняется только через <c>UxrGrabManager</c>, мимо которого
        /// пополнение арсенала и работает.
        /// </summary>
        private static int CountWeapons(ArsenalSlotController slot)
        {
            if (slot == null || slot.ItemAnchor == null)
                return 0;

            return slot.ItemAnchor.GetComponentsInChildren<WeaponComponent>(true).Length;
        }

        /// <summary>Сводка состояний стен вида «Closed x16» — чтобы detail не разросся.</summary>
        private static string DescribeWallStates(ArsenalWallController[] walls)
        {
            Dictionary<ArsenalWallController.ArsenalState, int> counts =
                new Dictionary<ArsenalWallController.ArsenalState, int>();

            foreach (ArsenalWallController wall in walls)
            {
                counts.TryGetValue(wall.CurrentState, out int count);
                counts[wall.CurrentState] = count + 1;
            }

            List<string> parts = new List<string>();
            foreach (KeyValuePair<ArsenalWallController.ArsenalState, int> pair in counts)
                parts.Add($"{pair.Key} x{pair.Value}");

            return string.Join(", ", parts.ToArray());
        }

        private static int SessionCount()
        {
            return PlayersManager.Instance != null ? PlayersManager.Instance.Sessions.Count : 0;
        }

        /// <summary>
        /// Раскладывает подключившиеся сессии по командам режима по кругу.
        /// EliminationMode.IsPlayersReady требует игрока в каждой команде,
        /// иначе матч навсегда останется в WaitingForPlayers.
        /// </summary>
        private static string AssignTeams(SessionManager sessionManager)
        {
            GameModeData mode = sessionManager.SelectedGameModeData;
            if (mode == null || mode.teams == null || mode.teams.Length == 0)
                return "режим не отдал список команд — команды не назначены";

            IReadOnlyList<PlayerSession> sessions = PlayersManager.Instance.Sessions;
            List<string> report = new List<string>();

            for (int i = 0; i < sessions.Count; i++)
            {
                TeamData team = mode.teams[i % mode.teams.Length];
                if (team == null) continue;

                sessions[i].TeamIndex = team.teamIndex;
                report.Add($"{sessions[i].PlayerName}->{team.displayName}({team.teamIndex})");
            }

            return string.Join(", ", report.ToArray());
        }

        private static void DisableDebugOrchestrator()
        {
            DebugOrchestrator orchestrator = Object.FindFirstObjectByType<DebugOrchestrator>();
            if (orchestrator == null || !orchestrator.enabled)
                return;

            // enabled=false вызывает OnDisable, а он снимает все подписки орchestrator-а.
            orchestrator.enabled = false;
            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                "[E2E] DebugOrchestrator отключён: дирижёром прогона выступает сценарий");
        }

        private static string Join(List<RoundState> states)
        {
            if (states.Count == 0)
                return "(пусто)";

            string[] names = new string[states.Count];
            for (int i = 0; i < states.Count; i++)
                names[i] = states[i].ToString();

            return string.Join(" -> ", names);
        }
    }
}
#endif
