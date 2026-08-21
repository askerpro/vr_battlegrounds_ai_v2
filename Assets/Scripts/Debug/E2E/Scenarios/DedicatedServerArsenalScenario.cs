// Ярус C (два процесса) — сценарий dedicated-server-arsenal: находки NET-06, NET-13, NET-07.
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
    /// Сценарий <c>dedicated-server-arsenal</c> — находки <b>NET-06</b>, <b>NET-13</b>, <b>NET-07</b>.
    ///
    /// <b>NET-06.</b> Смена фазы раунда рассылалась через
    /// <c>[ClientRpc] EliminationMode.RpcOnRoundStateChanged</c>, который поднимает
    /// статическое событие <see cref="EliminationMode.OnRoundStateChangedLocal"/>.
    /// В режиме <c>ServerOnly</c> ClientRpc локально не исполняется, поэтому на
    /// выделенном сервере событие не срабатывало, и стена арсенала не открывалась
    /// и не пополнялась. На хосте баг не виден — там сервер сам является клиентом.
    /// Закрыта T-13: фаза стала состоянием (<c>SyncVar</c>), а не сообщением.
    ///
    /// <b>NET-13.</b> <c>ArsenalSlotController.IsItemPresent</c> читал
    /// <c>UxrGrabbableObjectAnchor.CurrentPlacedObject</c>, который сетевая выдача
    /// оружия не заполняет. Проверка сравнивает занятость слота с фактически
    /// заспавненным под якорем оружием: до T-15 — 0 против 64.
    ///
    /// <b>NET-07.</b> Состояние стены было обычным полем: каждая машина вела её сама.
    /// Проверка кросс-процессная: <c>client-1</c> дёргает жетон на общей стене,
    /// а сервер и <c>client-2</c> обязаны увидеть, что она закрылась. До T-15
    /// закрывалась она только у инициатора.
    ///
    /// Роли:
    /// <list type="bullet">
    /// <item><c>server</c> — гонит матч и выносит вердикт;</item>
    /// <item><c>client-1</c> — подключается, занимает место в команде и берёт жетон;</item>
    /// <item><c>client-N</c> — подключается и занимает место в команде,
    ///       без двух игроков матч не стартует (<c>minPlayersToStart = 2</c>,
    ///       плюс требование «в каждой команде есть игрок»), и наблюдает за стеной.</item>
    /// </list>
    ///
    /// Общая стена выбирается по наименьшему <c>netId</c>: он одинаков во всех
    /// процессах, а <c>FindObjectsByType</c> порядок не гарантирует.
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
        private const string CheckPresence  = "слот считает себя занятым, когда оружие в нём есть (NET-13)";
        private const string CheckTagClose  = "жетон, взятый на клиенте, закрыл общую стену и на сервере (NET-07)";

        private const string CheckClientConnected = "клиент подключился к серверу";
        private const string CheckClientSession   = "сервер создал сессию для клиента";
        private const string CheckClientMap       = "клиент переехал на карту вместе с сервером";
        private const string CheckClientEvent     = "смена фазы раунда дошла до клиента (контроль к NET-06)";
        private const string CheckClientArsenal   = "стена арсенала открыта и у этого клиента";
        private const string CheckClientTagPull   = "жетон взят на этом клиенте — общая стена пошла закрываться";
        private const string CheckClientTagSeen   = "закрытие стены по чужому жетону доехало до этого клиента (NET-07)";

        /// <summary>Роль, которая дёргает жетон. Остальные клиенты только наблюдают.</summary>
        private const string TagInitiatorRole = "client-1";

        /// <summary>
        /// Сколько ждать после «стена открылась», прежде чем дёрнуть жетон.
        /// Пауза нужна, чтобы сервер успел записать свою проверку про открытую стену:
        /// закрытие, прилетевшее в середине его пятисекундной выдержки, сделало бы
        /// проверку 7 неверной по посторонней причине.
        /// </summary>
        private const float TagPullDelay = 10f;

        /// <summary>
        /// Сколько инициатор ждёт возврата состояния после захвата жетона.
        /// Срок щедрый намеренно: он покрывает круг «Command → сервер → SyncVar → клиент»
        /// с любым разумным запасом, а нестабильность TEST-01 лечится не им, а тем,
        /// что сервер больше не гасит процесс до отчёта клиентов
        /// (см. <see cref="ReportVerdictReady"/>).
        /// </summary>
        private const float TagCloseWait = 30f;

        /// <summary>Сколько наблюдатель ждёт закрытия стены по чужому жетону.</summary>
        private const float TagSeenWait = 90f;

        /// <summary>
        /// Сколько сервер ждёт отчётов клиентов, прежде чем погасить процесс.
        /// Истечение срока — не отказ проверки, а предупреждение: клиентские
        /// вердикты после него могут оказаться недостоверными.
        /// </summary>
        private const float ClientVerdictWait = 45f;

        // ── Наблюдение ────────────────────────────────────────────────────

        /// <summary>Фазы, пришедшие через <c>OnRoundStateChangedLocal</c> (то, что ловит арсенал).</summary>
        private readonly List<RoundState> _eventPhases = new List<RoundState>();

        /// <summary>Фазы, увиденные опросом SyncVar (то, что реально происходит на сервере).</summary>
        private readonly List<RoundState> _syncVarPhases = new List<RoundState>();


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
            result.Declare(CheckDedicated, CheckClients, CheckMap, CheckInitial, CheckPresence,
                           CheckPhase, CheckEvent, CheckArsenal, CheckTagClose);

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
            GameLog.Debug.Info($"[E2E] Команды распределены: {teamsReport}");

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
            // Факт «оружие в слоте есть» считаем по WeaponComponent под якорем:
            // именно его навешивает AssignNetworkItem, и от учёта UltimateXR
            // этот подсчёт не зависит вовсе. Публичное IsItemPresent считаем
            // отдельно и сравниваем с ним — это и есть проверка NET-13.
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
                $"с реально заспавненным оружием: {withWeapon}. " +
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

            // ── 5. Занятость слотов совпадает с фактом (NET-13) ───────────
            bool presenceOk = reportedPresent == withWeapon;
            result.Set(CheckPresence, presenceOk,
                $"слотов с реально заспавненным оружием: {withWeapon}, " +
                $"из них слот считает себя занятым: {reportedPresent}. " +
                (presenceOk
                    ? "IsItemPresent совпадает с фактом, значит NeedsReplenishment() не врёт " +
                      "и в фазе Setup оружие не заспавнится поверх лежащего"
                    : "IsItemPresent врёт (NET-13): занятость читается из UxrGrabbableObjectAnchor.CurrentPlacedObject, " +
                      "а сетевая выдача идёт мимо UxrGrabManager и его не заполняет. Следствие — NeedsReplenishment() " +
                      "всегда true, и ReplenishWeaponsNetwork(false) в каждой фазе Setup дублирует оружие на стене."));

            string arsenalStatesBefore = DescribeWallStates(walls);

            // ── 6. Матч и фазы раунда ─────────────────────────────────────
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

                // ── 7. Дошло ли событие до локальных подписчиков ──────────
                bool eventOk = _eventPhases.Count > 0;
                result.Set(CheckEvent, eventOk,
                    eventOk
                        ? $"EliminationMode.OnRoundStateChangedLocal сработало {_eventPhases.Count} раз(а): {Join(_eventPhases)}"
                        : "EliminationMode.OnRoundStateChangedLocal не сработало ни разу, хотя SyncVar-фазы менялись: " +
                          $"{Join(_syncVarPhases)}. Это NET-06: событие поднимается только внутри " +
                          "[ClientRpc] RpcOnRoundStateChanged, а в режиме ServerOnly ClientRpc локально не исполняется.");

                // ── 8. Открылась ли хотя бы одна стена ───────────────────
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

                if (!arsenalOk)
                {
                    result.Summary = "стена не открылась, проверять закрытие по жетону нечего";
                    yield break;
                }

                // ── 9. Жетон, взятый на клиенте, закрывает общую стену ────
                ArsenalWallController shared = PickSharedWall(walls);
                if (shared == null)
                {
                    result.Set(CheckTagClose, false,
                        $"ни одна из {walls.Length} стен не заспавнена Mirror (netId == 0 у всех) — " +
                        "общий объект не выбрать, и репликация состояния тут ни при чём. " +
                        "Похоже на NET-14: у стен нет sceneId.");
                    result.Summary = "стены не заспавнены, вердикт по NET-07 вынести нельзя";
                    yield break;
                }

                GameLog.Debug.Info(
                    $"[E2E] Жду закрытия общей стены netId={shared.netId} " +
                    $"(её должен закрыть {TagInitiatorRole}, взяв жетон)");

                deadline = Now + 60f;
                while (shared.CurrentState == ArsenalWallController.ArsenalState.Open && Now < deadline)
                    yield return null;

                RoundState phaseAtClose = elimination.CurrentRoundState;
                bool wallLeftOpen = shared.CurrentState != ArsenalWallController.ArsenalState.Open;

                // Фаза обязана остаться Equipment: выход из неё требует готовности всех
                // живых игроков, а инициатор дёргает жетон мимо PlayerSession. Если фаза
                // всё же сменилась, стену закрыл переход по фазе, и проверка ничего не значит.
                bool tagCloseOk = wallLeftOpen && phaseAtClose == RoundState.Equipment;

                result.Set(CheckTagClose, tagCloseOk,
                    tagCloseOk
                        ? $"стена netId={shared.netId} перешла в {shared.CurrentState}, фаза при этом осталась " +
                          $"{phaseAtClose} — значит закрыл её именно жетон клиента, а не переход по фазе. " +
                          "Состояние стены общее"
                        : !wallLeftOpen
                            ? $"за 60 с стена netId={shared.netId} осталась Open, хотя {TagInitiatorRole} взял жетон " +
                              "(смотри его лог и client-1.json). Это NET-07: состояние стены — обычное поле, " +
                              "и закрытие живёт только на той машине, где схватили жетон"
                            : $"стена закрылась, но фаза успела уйти в {phaseAtClose} — закрыть её мог переход по фазе. " +
                              "Проверка недействительна, а не провалена: смотри, почему раунд ушёл из Equipment.");

                // ── 10. Барьер: не гасим сервер, пока клиенты не вынесли вердикт ──
                // Свой вердикт сервер выносит в тот же кадр, в котором применил Command
                // инициатора, — а инициатору состояние возвращается только следующей
                // рассылкой SyncVar. Application.Quit сразу после вердикта обрывал связь
                // раньше эха примерно в трети прогонов: это и есть TEST-01. Барьер не
                // проверка: он ничего не утверждает об игре, он лишь удерживает процесс.
                yield return WaitForClientVerdicts(context);

                result.Summary = result.AllChecksGreen
                    ? "все проверки зелёные — NET-06, NET-13 и NET-07 больше не воспроизводятся"
                    : (!eventOk || !arsenalOk)
                        ? "NET-06 подтверждена: конфигурация собралась, " +
                          "но смена фазы раунда до серверной логики не доходит"
                        : !presenceOk && !tagCloseOk
                            ? "подтверждены NET-13 (слот не видит своё оружие) и NET-07 (состояние стены не реплицируется)"
                            : !presenceOk
                                ? "подтверждена NET-13: слот не видит лежащее в нём оружие"
                                : !tagCloseOk
                                    ? "подтверждена NET-07: состояние стены не реплицируется"
                                    : "есть красные проверки, см. detail";
            }
            finally
            {
                EliminationMode.OnRoundStateChangedLocal -= OnRoundStateEvent;
            }
        }

        /// <summary>
        /// Держит серверный процесс живым, пока каждый клиент не отчитается, что
        /// записал свою проверку по жетону (см. <see cref="ReportVerdictReady"/>).
        ///
        /// Истечение срока не красит ни одну проверку: сервер своё уже проверил,
        /// а достоверность клиентских вердиктов видна по их собственным файлам.
        /// Но в лог это обязано попасть — иначе следующий разбор снова начнётся
        /// с гадания, почему клиент увидел не то же, что сервер.
        /// </summary>
        private static IEnumerator WaitForClientVerdicts(E2EContext context)
        {
            E2EWaitOutcome barrier = new E2EWaitOutcome();

            yield return E2EWait.Until(barrier,
                $"все {context.ExpectedClients} клиент(а) отчитались, что записали свою проверку по жетону",
                ClientVerdictWait,
                () => ReportedVerdicts() >= context.ExpectedClients,
                () => $"отчитались {ReportedVerdicts()} из {context.ExpectedClients}; " +
                      $"сессии: {DescribeVerdictReports()}; " +
                      $"подключений на сервере: {NetworkServer.connections.Count}");

            if (barrier.Succeeded)
            {
                GameLog.Debug.Info(
                    $"[E2E] {barrier.Diagnosis} Сервер можно гасить — эхо SyncVar до клиентов доехало.");
                yield break;
            }

            GameLog.Debug.Info(
                $"[E2E] {barrier.Diagnosis} Гашу сервер не дождавшись: клиентские вердикты " +
                "могли не успеть — сверься с client-*.json.");
        }

        /// <summary>Сколько сессий подняли флаг «мой вердикт по жетону записан».</summary>
        private static int ReportedVerdicts()
        {
            if (PlayersManager.Instance == null)
                return 0;

            int reported = 0;
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.HasGrabbedDogTag)
                    reported++;
            }

            return reported;
        }

        /// <summary>Кто отчитался, а кто нет — для диагностики барьера.</summary>
        private static string DescribeVerdictReports()
        {
            if (PlayersManager.Instance == null)
                return "PlayersManager отсутствует";

            List<string> parts = new List<string>();
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null) continue;
                parts.Add($"{session.PlayerName}[{session.DeviceToken}]={(session.HasGrabbedDogTag ? "да" : "нет")}");
            }

            return parts.Count == 0 ? "сессий нет" : string.Join(", ", parts.ToArray());
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль клиента
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            bool isTagInitiator = context.Role == TagInitiatorRole;

            result.Declare(CheckClientConnected, CheckClientSession, CheckClientMap, CheckClientEvent,
                           CheckClientArsenal,
                           isTagInitiator ? CheckClientTagPull : CheckClientTagSeen);

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
                GameLog.Debug.Info($"[E2E] Discovery молчит 30 с, подключаюсь напрямую к {context.ServerAddress}");

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

            // ── Общая стена: та же самая во всех процессах ────────────────
            // Выбираем по наименьшему netId: FindObjectsByType порядок не гарантирует,
            // а netId сцены Mirror раздаёт с сервера, и он одинаков у всех.
            ArsenalWallController shared = null;
            deadline = Now + 90f;
            while (shared == null && Now < deadline)
            {
                shared = PickSharedWall(Object.FindObjectsByType<ArsenalWallController>(FindObjectsInactive.Include));
                if (shared == null)
                    yield return null;
            }

            if (shared == null)
            {
                result.Set(CheckClientArsenal, false,
                    "за 90 с на клиенте не нашлось ни одной заспавненной стены арсенала " +
                    $"(на карте '{context.Map}'). Либо карта не загрузилась, либо Mirror не заспавнил стены — " +
                    "у объекта сцены нет sceneId (NET-14).");
                result.Summary = "клиент не увидел стену арсенала";
                // Отчитываемся и на провальном пути: серверу незачем ждать полный
                // срок того, кто уже сдался.
                ReportVerdictReady();
                yield break;
            }

            deadline = Now + 90f;
            while (shared.CurrentState != ArsenalWallController.ArsenalState.Open && Now < deadline)
                yield return null;

            bool arsenalOpen = shared.CurrentState == ArsenalWallController.ArsenalState.Open;
            result.Set(CheckClientArsenal, arsenalOpen,
                arsenalOpen
                    ? $"стена netId={shared.netId} открыта у этого клиента"
                    : $"за 90 с стена netId={shared.netId} осталась в состоянии {shared.CurrentState}. " +
                      "Клиент не видит открытого арсенала: либо состояние до него не доехало, " +
                      "либо стена не открылась и на сервере — сверься с server.json.");

            if (!arsenalOpen)
            {
                result.Summary = "клиент не увидел открытую стену, проверять закрытие по жетону нечего";
                ReportVerdictReady();
                yield break;
            }

            if (isTagInitiator)
                yield return PullDogTag(shared, result);
            else
                yield return WatchWallClose(shared, result);

            result.Summary = result.AllChecksGreen
                ? "клиент подключился, получил сессию, карту, фазу раунда и увидел общую стену"
                : "клиент не дошёл до состояния, в котором сервер может выносить вердикт";
        }

        /// <summary>
        /// Роль инициатора: берёт жетон на общей стене. Дёргаем событие
        /// <see cref="DogTagController.OnTagGrabbed"/> напрямую — это ровно то, что
        /// поднимает <c>OnTagRemoved</c> при настоящем захвате, а руки в прогоне без
        /// шлема взяться неоткуда. Игрока передаём null: тогда готовность не уйдёт
        /// в <c>PlayerSession</c>, раунд не выйдет из <c>Equipment</c>, и стену
        /// закроет именно жетон, а не переход по фазе.
        /// </summary>
        private IEnumerator PullDogTag(ArsenalWallController shared, E2EResult result)
        {
            // Пауза, чтобы сервер успел записать свою проверку про открытую стену.
            yield return E2EWait.Hold(TagPullDelay);

            // netId запоминаем заранее. При обрыве связи Mirror зовёт NetworkIdentity.Reset,
            // и netId обнуляется прямо на живом объекте — вердикт, читающий его в конце,
            // сообщал бы про мифическую «стену netId=0».
            uint wallNetId = shared.netId;

            DogTagController dogTag = shared.GetComponentInChildren<DogTagController>(true);
            if (dogTag == null)
            {
                result.Set(CheckClientTagPull, false,
                    $"у стены netId={wallNetId} нет DogTagController — жетон брать нечем. " +
                    "Проверить закрытие по жетону на этой карте нельзя.");
                ReportVerdictReady();
                yield break;
            }

            // Состояние на момент захвата. Если стена к этому мгновению уже не Open,
            // ждать «выхода из Open» бессмысленно: условие выполнено заранее и проверка
            // стала бы зелёной, ничего не проверив.
            ArsenalWallController.ArsenalState stateAtGrab = shared.CurrentState;
            if (stateAtGrab != ArsenalWallController.ArsenalState.Open)
            {
                result.Set(CheckClientTagPull, false,
                    $"стена netId={wallNetId} была уже в {stateAtGrab} к моменту захвата жетона — " +
                    "закрывать нечего, и проверка ничего не значила бы. Смотри, кто закрыл её раньше: " +
                    "выдержка перед захватом рассчитана на то, что фаза остаётся Equipment.");
                ReportVerdictReady();
                yield break;
            }

            GameLog.Debug.Info($"[E2E] Беру жетон на стене netId={wallNetId}");
            dogTag.OnTagGrabbed?.Invoke(null);

            // Ждём не «сколько-нибудь», а именно возврата состояния: захват уходит
            // Command'ом на сервер, сервер пишет SyncVar, и обратно оно приезжает
            // ближайшей рассылкой. Пропажа связи означает, что ответа уже не будет, —
            // ждать оставшийся срок незачем, и в вердикте это должно быть названо.
            E2EWaitOutcome wait = new E2EWaitOutcome();
            yield return E2EWait.Until(wait,
                $"стена netId={wallNetId} вышла из Open после захвата жетона",
                TagCloseWait,
                () => shared.CurrentState != ArsenalWallController.ArsenalState.Open,
                () => DescribeWallWait(shared, wallNetId),
                () => NetworkClient.isConnected
                    ? null
                    : "связь с сервером пропала — SyncVar с новым состоянием вернуться уже не может " +
                      "(сервер погас раньше, чем ответил; см. TEST-01)");

            result.Set(CheckClientTagPull, wait.Succeeded,
                wait.Succeeded
                    ? $"жетон взят, стена netId={wallNetId} перешла в {shared.CurrentState} " +
                      $"за {wait.Elapsed:F2} с"
                    : wait.Diagnosis + " Закрытие не сработало даже у инициатора — " +
                      "дальше проверять нечего, красные проверки на сервере и втором клиенте " +
                      "этим и объясняются.");

            ReportVerdictReady();
        }

        /// <summary>
        /// Роль наблюдателя: жетон берёт другой клиент, а эта машина обязана увидеть,
        /// что общая стена закрылась. До T-15 закрытие жило только у инициатора — это NET-07.
        /// </summary>
        private IEnumerator WatchWallClose(ArsenalWallController shared, E2EResult result)
        {
            uint wallNetId = shared.netId;

            E2EWaitOutcome wait = new E2EWaitOutcome();
            yield return E2EWait.Until(wait,
                $"стена netId={wallNetId} вышла из Open после чужого жетона",
                TagSeenWait,
                () => shared.CurrentState != ArsenalWallController.ArsenalState.Open,
                () => DescribeWallWait(shared, wallNetId),
                () => NetworkClient.isConnected
                    ? null
                    : "связь с сервером пропала — состояние стены сюда уже не приедет " +
                      "(сервер погас раньше, чем разослал; см. TEST-01)");

            result.Set(CheckClientTagSeen, wait.Succeeded,
                wait.Succeeded
                    ? $"стена netId={wallNetId} закрылась и здесь ({shared.CurrentState}) " +
                      $"за {wait.Elapsed:F2} с, хотя жетон брал {TagInitiatorRole} — состояние стены общее"
                    : wait.Diagnosis + $" Жетон брал {TagInitiatorRole}, его вердикт — в client-1.json. " +
                      "Если тот увидел закрытие у себя, а сюда оно не доехало — это NET-07: " +
                      "состояние стены живёт только на машине инициатора.");

            ReportVerdictReady();
        }

        /// <summary>
        /// Снимок состояния для диагностики ожидания стены. Помимо самого состояния
        /// показывает связь и текущий <c>netId</c>: обнулившийся netId при живой ссылке —
        /// верный признак того, что объект отцепили от сети, а не что стена «не та».
        /// </summary>
        private static string DescribeWallWait(ArsenalWallController wall, uint expectedNetId)
        {
            string netIdNote = wall.netId == expectedNetId
                ? $"netId={wall.netId}"
                : $"netId был {expectedNetId}, стал {wall.netId} — объект отцеплён от сети";

            return $"состояние стены={wall.CurrentState}, {netIdNote}, " +
                   $"связь с сервером={(NetworkClient.isConnected ? "есть" : "нет")}, " +
                   $"сцена='{SceneManager.GetActiveScene().name}'";
        }

        /// <summary>
        /// Обратный канал «свой вердикт по жетону я вынес».
        ///
        /// Зачем. Сервер узнаёт о закрытии стены в тот же кадр, в котором применил
        /// <c>Command</c> клиента, и сразу гасит процесс — а инициатору состояние
        /// возвращается только следующей рассылкой <c>SyncVar</c>. Обрыв связи
        /// опережал эхо примерно в трети прогонов: это и была TEST-01. Флаг
        /// <c>HasGrabbedDogTag</c> используется как сигнал «мою проверку я записал»,
        /// и сервер ждёт его от каждого клиента, прежде чем выйти.
        ///
        /// Фазу раунда сигнал сдвинуть не может: <c>AreAllPlayersReady</c> требует
        /// готовности всех живых игроков, а её достигнет только последний отчитавшийся —
        /// то есть заведомо после того, как все проверки уже записаны.
        /// </summary>
        private static void ReportVerdictReady()
        {
            PlayerSession local = PlayerSession.LocalSession;
            if (local == null)
            {
                GameLog.Debug.Info("[E2E] Отчитаться о вердикте нечем: локальной сессии нет");
                return;
            }

            GameLog.Debug.Info("[E2E] Вердикт по жетону записан — отчитываюсь серверу");
            local.CmdSetDogTagGrabbed(true);
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное
        // ══════════════════════════════════════════════════════════════════

        private static float Now => Time.realtimeSinceStartup;

        private void OnRoundStateEvent(RoundState state)
        {
            _eventPhases.Add(state);
            GameLog.Debug.Info($"[E2E] OnRoundStateChangedLocal -> {state}");
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

        /// <summary>
        /// Стена, которую все процессы понимают одинаково: с наименьшим ненулевым
        /// <c>netId</c>. Порядок <c>FindObjectsByType</c> в разных процессах разный,
        /// а netId сетевого объекта сцены раздаёт сервер — он общий. Возвращает null,
        /// если ни одна стена не заспавнена.
        /// </summary>
        private static ArsenalWallController PickSharedWall(ArsenalWallController[] walls)
        {
            ArsenalWallController best = null;

            foreach (ArsenalWallController wall in walls)
            {
                if (wall == null || wall.netId == 0) continue;
                if (best == null || wall.netId < best.netId) best = wall;
            }

            return best;
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
            GameLog.Debug.Info(
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
