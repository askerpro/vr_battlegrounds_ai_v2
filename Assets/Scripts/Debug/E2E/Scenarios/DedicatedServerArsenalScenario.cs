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
    /// Проверка кросс-процессная: сервер закрывает общую стену, а оба клиента обязаны
    /// увидеть закрытие у себя. До T-15 оно жило только на машине инициатора.
    ///
    /// <b>RDY-01 (T-29).</b> Спусковым крючком закрытия был жетон, и после T-15 это
    /// означало: первый взявший жетон закрывает арсенал <b>всем</b>. Теперь закрытие —
    /// следствие общей готовности, и сценарий проверяет обе половины правила:
    /// пока готов один игрок из двух, стена обязана оставаться открытой у всех;
    /// как только готовы все — закрыться у всех. Готовность выставляет сервер
    /// (<c>PlayerSession.ServerSetReady</c>), поэтому клиентам не нужно
    /// договариваться между собой о времени.
    ///
    /// Роли:
    /// <list type="bullet">
    /// <item><c>server</c> — гонит матч, объявляет готовность и выносит вердикт;</item>
    /// <item><c>client-N</c> — подключается, занимает место в команде (без игрока
    ///       в каждой команде матч не стартует: <c>minPlayersToStart = 2</c> плюс
    ///       требование «в каждой команде есть игрок») и наблюдает за общей стеной.</item>
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
        private const string CheckRdyOpen   = "готов один игрок из двух — общая стена осталась открытой (RDY-01)";
        private const string CheckRdyClose  = "готовы все — общая стена закрылась на сервере";

        private const string CheckClientConnected = "клиент подключился к серверу";
        private const string CheckClientSession   = "сервер создал сессию для клиента";
        private const string CheckClientMap       = "клиент переехал на карту вместе с сервером";
        private const string CheckClientEvent     = "смена фазы раунда дошла до клиента (контроль к NET-06)";
        private const string CheckClientArsenal   = "стена арсенала открыта и у этого клиента";
        private const string CheckClientRdyOpen   = "пока готов один игрок, стена у этого клиента остаётся открытой (RDY-01)";
        private const string CheckClientRdyClose  = "закрытие общей стены доехало до этого клиента (NET-07)";

        /// <summary>
        /// Сколько ждать после «стена открылась», прежде чем объявить готовность
        /// за первого игрока. Пауза нужна, чтобы сервер успел записать свою проверку
        /// про открытую стену.
        /// </summary>
        private const float ReadyDelay = 10f;

        /// <summary>
        /// Сколько держим состояние «готов ровно один». Срок содержательный, а не
        /// диагностический: именно в это окно стена обязана оставаться открытой,
        /// и его же клиенты используют, чтобы записать свою половину проверки RDY-01.
        /// </summary>
        private const float RdyHold = 10f;

        /// <summary>Сколько ждём закрытия общей стены после готовности всех.</summary>
        private const float WallCloseWait = 90f;

        /// <summary>Сколько клиент ждёт состояния, которое объявляет сервер.</summary>
        private const float ReadyStateWait = 120f;

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
                           CheckPhase, CheckEvent, CheckArsenal, CheckRdyOpen, CheckRdyClose);

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
                GameplayManager.Instance.StartMatch();

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

                // ── 9. Готовность одного игрока стену не закрывает (RDY-01) ──
                ArsenalWallController shared = PickSharedWall(walls);
                if (shared == null)
                {
                    result.Set(CheckRdyOpen, false,
                        $"ни одна из {walls.Length} стен не заспавнена Mirror (netId == 0 у всех) — " +
                        "общий объект не выбрать, и репликация состояния тут ни при чём. " +
                        "Похоже на NET-14: у стен нет sceneId.");
                    result.Summary = "стены не заспавнены, вердикт по NET-07 вынести нельзя";
                    yield break;
                }

                // Пауза, чтобы клиенты успели увидеть открытую стену и записать свою
                // проверку про неё.
                yield return E2EWait.Hold(ReadyDelay);

                List<PlayerSession> allSessions = AllSessions();
                if (allSessions.Count < 2)
                {
                    result.Set(CheckRdyOpen, false,
                        $"сессий на сервере {allSessions.Count}, а RDY-01 живёт только при двух и более: " +
                        "весь дефект в том, что готовность одного игрока задевает другого.");
                    result.Summary = "игроков меньше двух, RDY-01 не проверить";
                    yield return WaitForClientVerdicts(context);
                    yield break;
                }

                PlayerSession first = allSessions[0];
                GameLog.Debug.Info(
                    $"[E2E] Объявляю готовность за {first.PlayerName} и держу {RdyHold:F0} с — " +
                    "стена netId=" + shared.netId + " обязана остаться открытой");

                first.ServerSetReady(true, "e2e: первый игрок объявил готовность");

                yield return E2EWait.Hold(RdyHold);

                bool stillOpen = shared.CurrentState == ArsenalWallController.ArsenalState.Open;
                RoundState phaseAtHold = elimination.CurrentRoundState;

                result.Set(CheckRdyOpen, stillOpen && phaseAtHold == RoundState.Equipment,
                    stillOpen && phaseAtHold == RoundState.Equipment
                        ? $"готов 1 игрок из {allSessions.Count}, и за {RdyHold:F0} с стена netId={shared.netId} " +
                          $"осталась Open, фаза осталась {phaseAtHold}. Арсенал закрывается по общей готовности, " +
                          "а не по первому игроку"
                        : !stillOpen
                            ? $"стена netId={shared.netId} ушла в {shared.CurrentState}, хотя готов только " +
                              $"{first.PlayerName}. Это RDY-01: стена одна на всех, и закрытие по первому " +
                              "оставляет остальных без снаряжения"
                            : $"фаза ушла в {phaseAtHold}, хотя готов только {first.PlayerName} — " +
                              "раунд не вправе выходить из закупки, пока готовы не все живые игроки");

                // ── 10. Готовы все — стена закрывается у всех (NET-07) ────
                foreach (PlayerSession session in allSessions)
                    session.ServerSetReady(true, "e2e: игрок объявил готовность");

                E2EWaitOutcome closed = new E2EWaitOutcome();
                yield return E2EWait.Until(closed,
                    $"стена netId={shared.netId} вышла из Open после готовности всех",
                    WallCloseWait,
                    () => shared.CurrentState != ArsenalWallController.ArsenalState.Open,
                    () => $"состояние стены={shared.CurrentState}, фаза={elimination.CurrentRoundState}, " +
                          $"готовность: {DescribeReadiness(allSessions)}",
                    () => NetworkServer.connections.Count > 0
                        ? null
                        : "на сервере не осталось подключений");

                bool rdyCloseOk = closed.Succeeded;
                result.Set(CheckRdyClose, rdyCloseOk,
                    rdyCloseOk
                        ? closed.Diagnosis + $" Фаза при этом {elimination.CurrentRoundState}: " +
                          "готовность всех живых игроков и есть условие выхода из закупки, " +
                          "поэтому закрытие по общей готовности и закрытие по началу отсчёта — один момент."
                        : closed.Diagnosis + " Готовность объявлена за всех, а арсенал так и не закрылся: " +
                          "к бою он обязан быть закрыт.");

                // ── 11. Барьер: не гасим сервер, пока клиенты не вынесли вердикт ──
                // Свой вердикт сервер выносит в тот же кадр, в котором сменил состояние,
                // а до клиентов оно доезжает только следующей рассылкой SyncVar.
                // Application.Quit сразу после вердикта обрывал связь раньше эха
                // примерно в трети прогонов: это и есть TEST-01. Барьер не проверка:
                // он ничего не утверждает об игре, он лишь удерживает процесс.
                yield return WaitForClientVerdicts(context);

                result.Summary = result.AllChecksGreen
                    ? "все проверки зелёные — NET-06, NET-13, NET-07 и RDY-01 больше не воспроизводятся"
                    : (!eventOk || !arsenalOk)
                        ? "NET-06 подтверждена: конфигурация собралась, " +
                          "но смена фазы раунда до серверной логики не доходит"
                        : !presenceOk && !stillOpen
                            ? "подтверждены NET-13 (слот не видит своё оружие) и RDY-01 (стена закрылась по первому готовому)"
                            : !presenceOk
                                ? "подтверждена NET-13: слот не видит лежащее в нём оружие"
                                : !stillOpen
                                    ? "подтверждён RDY-01: общая стена закрывается по первому готовому игроку"
                                    : !rdyCloseOk
                                        ? "стена не закрылась даже при готовности всех"
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

        /// <summary>
        /// Кто уже отчитался. Накопительно: начало нового раунда сбрасывает
        /// <c>HasGrabbedDogTag</c> вместе с готовностью, и мгновенный снимок флагов
        /// после такого сброса увидел бы ноль.
        /// </summary>
        private static readonly HashSet<uint> ReportedVerdictIds = new HashSet<uint>();

        private static int ReportedVerdicts()
        {
            if (PlayersManager.Instance == null)
                return ReportedVerdictIds.Count;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.HasGrabbedDogTag)
                    ReportedVerdictIds.Add(session.netId);
            }

            return ReportedVerdictIds.Count;
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
            // Роли инициатора больше нет: готовность объявляет сервер, а оба клиента
            // проверяют одно и то же — что общая стена ведёт себя одинаково у всех.
            result.Declare(CheckClientConnected, CheckClientSession, CheckClientMap, CheckClientEvent,
                           CheckClientArsenal, CheckClientRdyOpen, CheckClientRdyClose);

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

            yield return WatchReadinessGate(shared, result);

            result.Summary = result.AllChecksGreen
                ? "клиент подключился, получил сессию, карту, фазу раунда и увидел общую стену"
                : "клиент не дошёл до состояния, в котором сервер может выносить вердикт";
        }

        /// <summary>
        /// Обе половины правила закрытия, увиденные с клиента.
        ///
        /// <para>
        /// Первая — <b>RDY-01</b>: пока готовность объявил один игрок из двух, стена
        /// обязана оставаться открытой. Момент «готов ровно один» определяется по
        /// реплицированному состоянию сессий, а не по договорённости о времени:
        /// синхронизировать два клиентских процесса по часам было бы гаданием.
        /// </para>
        ///
        /// <para>
        /// Вторая — <b>NET-07</b>: как только готовы все, закрытие, объявленное сервером,
        /// обязано доехать сюда. До T-15 состояние стены жило на каждой машине своё.
        /// </para>
        /// </summary>
        private IEnumerator WatchReadinessGate(ArsenalWallController shared, E2EResult result)
        {
            // netId запоминаем заранее. При обрыве связи Mirror зовёт NetworkIdentity.Reset,
            // и netId обнуляется прямо на живом объекте — вердикт, читающий его в конце,
            // сообщал бы про мифическую «стену netId=0».
            uint wallNetId = shared.netId;

            E2EWaitOutcome oneReady = new E2EWaitOutcome();
            yield return E2EWait.Until(oneReady,
                "ровно один игрок объявил готовность",
                ReadyStateWait,
                () => ReadyCount() == 1,
                () => DescribeWallWait(shared, wallNetId),
                () => NetworkClient.isConnected
                    ? null
                    : "связь с сервером пропала — состояние готовности сюда уже не приедет");

            if (!oneReady.Succeeded)
            {
                result.Set(CheckClientRdyOpen, false,
                    oneReady.Diagnosis + " Состояние «готов ровно один» до клиента не доехало, " +
                    "и проверять на нём RDY-01 нельзя.");
            }
            else
            {
                // Ждём не события, а его отсутствия, поэтому здесь именно выдержка:
                // стена обязана оставаться открытой всё то время, пока готов не каждый.
                float until = Now + RdyHold * 0.5f;
                bool stayedOpen = true;

                while (Now < until)
                {
                    if (ReadyCount() > 1) break;   // сервер уже объявил готовность за всех

                    if (shared.CurrentState != ArsenalWallController.ArsenalState.Open)
                    {
                        stayedOpen = false;
                        break;
                    }

                    yield return null;
                }

                result.Set(CheckClientRdyOpen, stayedOpen,
                    stayedOpen
                        ? $"готовность объявил один игрок, а стена netId={wallNetId} у этого клиента " +
                          "осталась открытой — экипироваться ещё можно"
                        : $"стена netId={wallNetId} закрылась ({shared.CurrentState}), пока готов был только " +
                          "один игрок. Это RDY-01: стена одна на всех, и закрытие по первому готовому " +
                          "оставляет остальных без снаряжения.");
            }

            E2EWaitOutcome wait = new E2EWaitOutcome();
            yield return E2EWait.Until(wait,
                $"стена netId={wallNetId} вышла из Open после готовности всех",
                ReadyStateWait,
                () => shared.CurrentState != ArsenalWallController.ArsenalState.Open,
                () => DescribeWallWait(shared, wallNetId),
                () => NetworkClient.isConnected
                    ? null
                    : "связь с сервером пропала — состояние стены сюда уже не приедет " +
                      "(сервер погас раньше, чем разослал; см. TEST-01)");

            result.Set(CheckClientRdyClose, wait.Succeeded,
                wait.Succeeded
                    ? $"стена netId={wallNetId} закрылась и здесь ({shared.CurrentState}) " +
                      $"за {wait.Elapsed:F2} с — состояние стены общее"
                    : wait.Diagnosis + " Сервер объявил закрытие (см. server.json), а сюда оно " +
                      "не доехало — это NET-07: состояние стены живёт только на машине сервера.");

            ReportVerdictReady();
        }

        /// <summary>
        /// Сколько сессий на этой машине объявили готовность. На клиенте
        /// <c>PlayersManager</c> пуст (он серверный), поэтому сессии ищутся по сцене:
        /// сами объекты Mirror спавнит всем наблюдателям, а <c>ReadyState</c> —
        /// обычный <c>SyncVar</c>.
        /// </summary>
        private static int ReadyCount()
        {
            int ready = 0;
            foreach (PlayerSession session in Object.FindObjectsByType<PlayerSession>(FindObjectsInactive.Include))
            {
                if (session != null && session.ReadyState) ready++;
            }

            return ready;
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
        /// Фазу раунда сигнал сдвинуть не может вовсе: с T-29 <c>HasGrabbedDogTag</c> —
        /// чистый жест, готовность живёт отдельным состоянием, и служебный флаг
        /// сценария на ход матча не влияет никак.
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
            uint bestNetId = 0;

            foreach (ArsenalWallController wall in walls)
            {
                if (wall == null) continue;

                // netId читается через NetworkIdentity, а у только что подгруженного
                // объекта сцены её ещё может не быть: Mirror связывает компоненты
                // в Awake, и до этого обращение к netId — NullReferenceException.
                // Клиент доходит сюда раньше, чем Mirror успевает заспавнить сцену.
                NetworkIdentity identity = wall.GetComponent<NetworkIdentity>();
                if (identity == null || identity.netId == 0) continue;

                if (best == null || identity.netId < bestNetId)
                {
                    best = wall;
                    bestNetId = identity.netId;
                }
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

        /// <summary>Серверный список сессий копией — по нему сценарий объявляет готовность.</summary>
        private static List<PlayerSession> AllSessions()
        {
            List<PlayerSession> list = new List<PlayerSession>();
            if (PlayersManager.Instance == null) return list;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null) list.Add(session);
            }

            return list;
        }

        private static string DescribeReadiness(List<PlayerSession> sessions)
        {
            List<string> parts = new List<string>();
            foreach (PlayerSession session in sessions)
            {
                if (session == null) continue;
                parts.Add($"{session.PlayerName}={(session.ReadyState ? "готов" : "не готов")}");
            }

            return parts.Count == 0 ? "сессий нет" : string.Join(", ", parts.ToArray());
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
