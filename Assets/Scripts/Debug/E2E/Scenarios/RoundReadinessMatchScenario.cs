// Ярус C (два процесса) — сценарий round-readiness-match: сквозной прогон раунда (T-29, T-09).
#if !VRBG_NO_E2E
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    /// Сценарий <c>round-readiness-match</c> — задача <b>T-29</b>, дефект <b>RDY-01</b>,
    /// плюс последний непокрытый пункт <b>T-09</b>.
    ///
    /// <para>
    /// <b>Зачем он есть.</b> Ярус C доходил до фазы <c>Equipment</c> и вставал: выход
    /// из неё требовал физически стоять в зоне спавна и взять жетон рукой, а рук
    /// в прогоне без шлема нет. Целый раунд — от закупки до экрана итогов и следующего
    /// раунда — не проверялся живой игрой ни разу. Серверный <c>ServerSetReady</c> это
    /// открыл: готовность стала явным состоянием, которое сервер вправе выставить сам.
    /// </para>
    ///
    /// <para>
    /// <b>Что доказывает.</b> Три разные вещи, и все три — только на живом матче:
    /// </para>
    /// <list type="number">
    /// <item><b>RDY-01.</b> Готовность объявлена за одного игрока из двух — стена
    ///       арсенала обязана остаться открытой и на сервере, и у обоих клиентов.
    ///       До T-29 первый же взятый жетон закрывал общую стену всем, и второй игрок
    ///       оставался без снаряжения.</item>
    /// <item><b>Сквозной раунд.</b> <c>Equipment → Countdown → Combat →</c> настоящая
    ///       смерть <c>→ Resolution → Scoreboard →</c> следующий раунд, целиком,
    ///       на двух процессах.</item>
    /// <item><b>T-09, длительности фаз.</b> <c>Resolution</c> (3 с) и <c>Scoreboard</c>
    ///       (5 с) до сих пор были проверены только в тиках EditMode — то есть в модельном
    ///       времени. Здесь они замеряются по настоящим часам
    ///       (<c>Time.realtimeSinceStartup</c>).</item>
    /// </list>
    ///
    /// <para>
    /// <b>Смерть настоящая.</b> Сервер бьёт аватары через <c>UxrActor.ReceiveDamage</c> —
    /// тот же вход, которым пользуется оружие (см. <c>player-death-signal</c>).
    /// Раунд заканчивается штатной цепочкой <c>UxrActor.Died → PlayerController →
    /// GameplayManager → EliminationMode.OnPlayerDied → RoundManager.RequestRoundEnd</c>,
    /// а не подсунутым «раунд окончен».
    /// </para>
    ///
    /// <para>
    /// Роли: <c>server</c> ведёт матч и выносит вердикт о раунде; оба клиента наблюдают
    /// общую стену и фазы, а <b>RDY-01</b> проверяют с той стороны, с которой он и болит, —
    /// со стороны игрока, который ещё не готов. Запускать с <c>-Clients 2</c>: без игрока
    /// в каждой команде матч не выходит из <c>WaitingForPlayers</c>.
    /// </para>
    /// </summary>
    public class RoundReadinessMatchScenario : IE2EScenario
    {
        public string Name => "round-readiness-match";

        // ── Имена проверок сервера ────────────────────────────────────────

        private const string CheckDedicated  = "сервер поднят как выделенный (ServerOnly, не хост)";
        private const string CheckClients    = "клиенты подключились и сервер создал сессии";
        private const string CheckMap        = "карта загружена, стена арсенала и матч на месте";
        private const string CheckRdy04      = "игрок в базе противника не считается стоящим в своей зоне (RDY-04)";
        private const string CheckRdy04Back  = "контроль: вернувшись к себе на спавн, игрок снова считается в зоне";
        private const string CheckEquipment  = "матч дошёл до фазы Equipment и ждёт готовности";
        private const string CheckRdy01      = "готов один из двух — стена арсенала осталась открытой (RDY-01)";
        private const string CheckPending    = "сервер выложил состав неготовых и в нём ровно тот, кого ждут";
        private const string CheckCountdown  = "готовы все — раунд ушёл в Countdown, стена закрылась";
        private const string CheckCombat     = "раунд дошёл до боя";
        private const string CheckDeath      = "настоящий урон убил команду и раунд перешёл в Resolution";
        private const string CheckResolution = "Resolution прожил свои 3 с по настоящим часам (T-09)";
        private const string CheckScoreboard = "Scoreboard прожил свои 5 с по настоящим часам (T-09)";
        private const string CheckNextRound  = "начался следующий раунд, готовность сброшена и её ждут заново";

        // ── Имена проверок клиента ────────────────────────────────────────

        private const string CheckClientConnected = "клиент подключился к серверу";
        private const string CheckClientSession   = "сервер создал сессию для клиента";
        private const string CheckClientMap       = "клиент переехал на карту вместе с сервером";
        private const string CheckClientWallOpen  = "стена арсенала открыта у этого клиента";
        private const string CheckClientRdy01     = "пока готов один игрок, арсенал у этого клиента остаётся открытым (RDY-01)";
        private const string CheckClientPending   = "состав неготовых доехал до клиента состоянием";
        private const string CheckClientWallShut  = "стена закрылась и здесь, когда готовы стали все (NET-07)";
        private const string CheckClientPhases    = "клиент увидел весь раунд: Countdown, Combat, Resolution, Scoreboard";

        // ── Сроки ─────────────────────────────────────────────────────────

        /// <summary>Сколько сервер ждёт, пока матч дойдёт до фазы закупки.</summary>
        private const float EquipmentWait = 150f;

        /// <summary>
        /// Сколько держим состояние «готов ровно один». Срок не диагностический,
        /// а содержательный: именно в это окно стена обязана оставаться открытой.
        /// Восьми секунд хватает и серверу, и обоим клиентам, чтобы записать свою
        /// проверку по RDY-01, и они заведомо больше любой задержки репликации.
        /// </summary>
        private const float Rdy01Hold = 8f;

        /// <summary>Сколько ждём фазу, которая обязана наступить сразу после действия.</summary>
        private const float PhaseWait = 60f;

        /// <summary>Сколько ждём аватары, перенос игрока и отклик зоны спавна (RDY-04).</summary>
        private const float RdyZoneWait = 60f;

        /// <summary>
        /// Пауза после переноса игрока, секунды. Признак зоны едет через
        /// <c>OnTriggerStay</c>, то есть через физический тик, а не через кадр
        /// отрисовки: сразу после <c>Respawn</c> зона о переезде ещё не знает.
        /// </summary>
        private const float ZoneSettleHold = 3f;

        /// <summary>
        /// Допуск «аватар доехал до зоны», метры. Зоны на карте разведены на десятки
        /// метров, поэтому полтора метра заведомо не путают одну базу с другой,
        /// а аватар за это время успевает осесть на коллайдер пола.
        /// </summary>
        private const float ZoneReachTolerance = 1.5f;

        /// <summary>Сколько клиент ждёт события, которое сервер вот-вот вызовет.</summary>
        private const float ClientPhaseWait = 180f;

        /// <summary>Сколько сервер ждёт отчётов клиентов, прежде чем погасить процесс.</summary>
        private const float ClientVerdictWait = 60f;

        /// <summary>
        /// Допуск замера длительности фазы, секунды. Фазу двигает сумма
        /// <c>Time.deltaTime</c>, то есть то же реальное время; расхождение даёт только
        /// последний, неполный кадр. Полсекунды — запас на любую заминку сборщика мусора,
        /// и при этом вдвое меньше разницы между 3 с и 5 с, так что перепутать фазы
        /// проверка не может.
        /// </summary>
        private const float PhaseDurationTolerance = 0.5f;

        // ── Наблюдение ────────────────────────────────────────────────────

        /// <summary>Фазы и момент их наступления по реальным часам этой машины.</summary>
        private readonly List<PhaseMark> _timeline = new List<PhaseMark>();

        private readonly struct PhaseMark
        {
            public readonly RoundState Phase;
            public readonly float At;

            public PhaseMark(RoundState phase, float at)
            {
                Phase = phase;
                At = at;
            }
        }


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
            result.Declare(CheckDedicated, CheckClients, CheckMap, CheckRdy04, CheckRdy04Back,
                           CheckEquipment, CheckRdy01, CheckPending, CheckCountdown, CheckCombat,
                           CheckDeath, CheckResolution, CheckScoreboard, CheckNextRound);

            // ── 1. Выделенный сервер ──────────────────────────────────────
            float deadline = Now + 60f;
            while (!NetworkServer.active && Now < deadline)
                yield return null;

            bool dedicated = NetworkServer.active && !NetworkClient.active;
            result.Set(CheckDedicated, dedicated,
                dedicated
                    ? "NetworkServer.active=true, NetworkClient.active=false — это ServerOnly"
                    : $"NetworkServer.active={NetworkServer.active}, NetworkClient.active={NetworkClient.active}. " +
                      "Сервер обязан идти с -batchmode -nographics: роль выбирается по Mirror.Utils.IsHeadless().");

            if (!dedicated)
            {
                result.Summary = "конфигурация не выделенный сервер, прогон недействителен";
                yield break;
            }

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
                      "Смотри client-*.log рядом с этим файлом.");

            if (!clientsOk)
            {
                result.Summary = "клиенты не подключились, матч не собрать";
                yield break;
            }

            // ── 3. Карта, команды, матч ───────────────────────────────────
            SessionManager sessionManager = SessionManager.Instance;
            if (sessionManager == null || MapManager.Instance == null)
            {
                result.Set(CheckMap, false,
                    $"SessionManager.Instance={(sessionManager == null ? "null" : "есть")}, " +
                    $"MapManager.Instance={(MapManager.Instance == null ? "null" : "есть")}");
                result.Summary = "менеджеры не поднялись, прогон недействителен";
                yield break;
            }

            sessionManager.SetSession(context.Map, "elimination");
            yield return null;

            GameLog.Debug.Info($"[E2E] Команды распределены: {AssignTeams(sessionManager)}");

            MapManager.Instance.LoadMap(context.Map);

            deadline = Now + 120f;
            while ((SceneManager.GetActiveScene().name != context.Map || GameplayManager.Instance == null)
                   && Now < deadline)
                yield return null;

            ArsenalWallController[] walls = new ArsenalWallController[0];
            deadline = Now + 30f;
            while (walls.Length == 0 && Now < deadline)
            {
                walls = Object.FindObjectsByType<ArsenalWallController>(FindObjectsInactive.Include);
                if (walls.Length == 0)
                    yield return null;
            }

            bool mapOk = SceneManager.GetActiveScene().name == context.Map
                         && GameplayManager.Instance != null
                         && walls.Length > 0;

            result.Set(CheckMap, mapOk,
                mapOk
                    ? $"сцена='{SceneManager.GetActiveScene().name}', стен арсенала: {walls.Length}, GameplayManager на месте"
                    : $"сцена='{SceneManager.GetActiveScene().name}' (ждали '{context.Map}'), " +
                      $"GameplayManager={(GameplayManager.Instance == null ? "null" : "есть")}, " +
                      $"стен арсенала: {walls.Length}");

            if (!mapOk)
            {
                result.Summary = "карта не собралась, матч гонять негде";
                yield break;
            }

            // ── 3a. RDY-04: чужая база не засчитывается за свою зону ──────
            // Проверка стоит до старта матча намеренно: в фазе Equipment идёт предел
            // ожидания готовности (45 с, AutoReady), и прогулка игрока по чужой базе
            // съедала бы его — фаза сменилась бы сама, а не по готовности.
            yield return CheckForeignSpawnZone(result);

            // Фазы пишем в ленту с момента старта матча: замер длительностей ниже
            // опирается именно на неё.
            EliminationMode.OnRoundStateChangedLocal += MarkPhase;

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
                    result.Set(CheckEquipment, false,
                        "за 60 с GameplayManager.ActiveGameMode не стал EliminationMode — матч не запустился. " +
                        $"Режим из SessionManager: '{sessionManager.SelectedModeId}'.");
                    result.Summary = "матч не запустился";
                    yield break;
                }

                // ── 4. Фаза закупки ───────────────────────────────────────
                E2EWaitOutcome equipment = new E2EWaitOutcome();
                yield return E2EWait.Until(equipment,
                    "матч дошёл до фазы Equipment",
                    EquipmentWait,
                    () => elimination.CurrentRoundState == RoundState.Equipment,
                    () => DescribeMatch(elimination, walls),
                    () => NetworkServer.connections.Count > 0
                        ? null
                        : "на сервере не осталось подключений — играть уже не с кем");

                result.Set(CheckEquipment, equipment.Succeeded, equipment.Diagnosis);

                if (!equipment.Succeeded)
                {
                    result.Summary = "матч не дошёл до фазы закупки";
                    yield break;
                }

                // Ждём открытой стены: закупка только что началась, анимация открытия
                // занимает свои кадры, а проверка RDY-01 читает именно «стена открыта».
                //
                // Общую стену ищем в цикле: Mirror спавнит объекты сцены не в тот же кадр,
                // в который сцена стала активной, а до спавна у стены нет netId — и выбрать
                // из восьми стен ту, которую все процессы понимают одинаково, нечем.
                ArsenalWallController shared = null;
                deadline = Now + 60f;
                while (shared == null && Now < deadline)
                {
                    shared = PickSharedWall(walls);
                    if (shared == null)
                        yield return null;
                }

                if (shared == null)
                {
                    result.Set(CheckRdy01, false,
                        $"за 60 с ни одна из {walls.Length} стен не получила netId — общий объект " +
                        "не выбрать. Похоже на NET-14: у стен сцены нет sceneId.");
                    result.Summary = "стены не заспавнены, RDY-01 не проверить";
                    yield return WaitForClientVerdicts(context);
                    yield break;
                }

                E2EWaitOutcome opened = new E2EWaitOutcome();
                yield return E2EWait.Until(opened,
                    "стена арсенала открылась в фазе закупки",
                    PhaseWait,
                    () => shared != null && shared.CurrentState == ArsenalWallController.ArsenalState.Open,
                    () => DescribeMatch(elimination, walls));

                if (!opened.Succeeded)
                {
                    result.Set(CheckRdy01, false,
                        opened.Diagnosis + " Стена так и не открылась — проверять, останется ли она " +
                        "открытой при одном готовом игроке, попросту не на чем.");
                    result.Summary = "стена арсенала не открылась в фазе закупки";
                    yield break;
                }

                // ── 5. RDY-01: готов ровно один ───────────────────────────
                List<PlayerSession> all = AllSessions();
                if (all.Count < 2)
                {
                    result.Set(CheckRdy01, false,
                        $"сессий на сервере {all.Count}, а для RDY-01 нужно минимум две: " +
                        "весь дефект в том, что готовность одного игрока задевает другого.");
                    result.Summary = "игроков меньше двух, RDY-01 не проверить";
                    yield break;
                }

                PlayerSession first = all[0];
                PlayerSession rest = all[1];

                GameLog.Debug.Info(
                    $"[E2E] Объявляю готовность за {first.PlayerName} и держу {Rdy01Hold:F0} с. " +
                    $"{rest.PlayerName} остаётся неготовым — стена обязана остаться открытой.");

                first.ServerSetReady(true, "e2e: первый игрок объявил готовность");

                yield return E2EWait.Hold(Rdy01Hold);

                bool wallStillOpen = shared.CurrentState == ArsenalWallController.ArsenalState.Open;
                bool stillEquipment = elimination.CurrentRoundState == RoundState.Equipment;
                bool restStillNotReady = !rest.ReadyState;

                result.Set(CheckRdy01, wallStillOpen && stillEquipment && restStillNotReady,
                    wallStillOpen && stillEquipment && restStillNotReady
                        ? $"{first.PlayerName} готов, {rest.PlayerName} нет — за {Rdy01Hold:F0} с стена " +
                          $"netId={shared.netId} осталась Open, фаза осталась {elimination.CurrentRoundState}. " +
                          "Арсенал закрывается по общей готовности, а не по первому жетону"
                        : !wallStillOpen
                            ? $"стена netId={shared.netId} ушла в {shared.CurrentState}, хотя готов только " +
                              $"{first.PlayerName}. Это RDY-01: после T-15 стена одна на всех, и закрытие " +
                              $"по первому готовому оставляет {rest.PlayerName} без снаряжения. " +
                              $"Фаза при этом: {elimination.CurrentRoundState}"
                            : !stillEquipment
                                ? $"фаза ушла в {elimination.CurrentRoundState}, хотя готов только {first.PlayerName}. " +
                                  "Раунд не вправе выходить из закупки, пока готовы не все живые игроки"
                                : $"{rest.PlayerName} оказался готов сам собой (ReadyState=true) — " +
                                  "проверка недействительна: готовность обязан объявлять игрок, а не появляться из воздуха");

                // ── 6. Состав неготовых — состояние ───────────────────────
                bool pendingOk = PendingContainsOnly(elimination, rest);
                result.Set(CheckPending, pendingOk,
                    pendingOk
                        ? $"сервер выложил состав неготовых: [{DescribePending(elimination)}] — это ровно {rest.PlayerName}"
                        : $"состав неготовых: [{DescribePending(elimination)}], ждали ровно одного — " +
                          $"{rest.PlayerName} (netId={rest.netId}). Без этого списка вновь подключившийся " +
                          "клиент не узнает, кого ждут: сам он не видит ни состав команд, ни кто жив.");

                // ── 7. Готовы все → отсчёт, стена закрывается ─────────────
                foreach (PlayerSession session in all)
                    session.ServerSetReady(true, "e2e: игрок объявил готовность");

                E2EWaitOutcome countdown = new E2EWaitOutcome();
                yield return E2EWait.Until(countdown,
                    "раунд ушёл в Countdown после готовности всех",
                    PhaseWait,
                    () => elimination.CurrentRoundState != RoundState.Equipment,
                    () => DescribeMatch(elimination, walls));

                // Стена начинает закрываться в тот же кадр, что и смена фазы, но
                // анимация закрытия доигрывает несколько кадров: ждём именно ухода
                // из Open, а не мгновенного Closed.
                E2EWaitOutcome wallShut = new E2EWaitOutcome();
                yield return E2EWait.Until(wallShut,
                    "стена арсенала вышла из Open",
                    PhaseWait,
                    () => shared.CurrentState != ArsenalWallController.ArsenalState.Open,
                    () => DescribeMatch(elimination, walls));

                result.Set(CheckCountdown, countdown.Succeeded && wallShut.Succeeded,
                    countdown.Succeeded && wallShut.Succeeded
                        ? $"готовы все — фаза стала {elimination.CurrentRoundState} за {countdown.Elapsed:F2} с, " +
                          $"стена netId={shared.netId} перешла в {shared.CurrentState}"
                        : !countdown.Succeeded
                            ? countdown.Diagnosis + " Готовность объявлена за всех, а раунд остался в закупке."
                            : wallShut.Diagnosis + " Фаза сменилась, а стена осталась открытой — " +
                              "к бою арсенал обязан быть закрыт.");

                // ── 8. Бой ────────────────────────────────────────────────
                E2EWaitOutcome combat = new E2EWaitOutcome();
                yield return E2EWait.Until(combat,
                    "раунд дошёл до фазы Combat",
                    PhaseWait,
                    () => elimination.CurrentRoundState == RoundState.Combat,
                    () => DescribeMatch(elimination, walls));

                result.Set(CheckCombat, combat.Succeeded, combat.Diagnosis);

                if (!combat.Succeeded)
                {
                    result.Summary = "раунд не дошёл до боя";
                    yield return WaitForClientVerdicts(context);
                    yield break;
                }

                // ── 9. Настоящая смерть → Resolution ──────────────────────
                string killed = KillTeamOf(rest);
                GameLog.Debug.Info($"[E2E] Убиваю команду игрока {rest.PlayerName}: {killed}");

                E2EWaitOutcome resolution = new E2EWaitOutcome();
                yield return E2EWait.Until(resolution,
                    "раунд перешёл в Resolution после гибели команды",
                    PhaseWait,
                    () => elimination.CurrentRoundState == RoundState.Resolution,
                    () => DescribeMatch(elimination, walls));

                result.Set(CheckDeath, resolution.Succeeded,
                    resolution.Succeeded
                        ? $"{killed}; раунд перешёл в Resolution за {resolution.Elapsed:F2} с — " +
                          "цепочка UxrActor.Died → PlayerController → EliminationMode.OnPlayerDied → " +
                          "RoundManager.RequestRoundEnd отработала целиком"
                        : resolution.Diagnosis + $" Убито: {killed}. Раунд не заканчивается по гибели команды: " +
                          "смотри, доходит ли смерть до GameplayManager.OnPlayerDied.");

                if (!resolution.Succeeded)
                {
                    result.Summary = "раунд не закончился по гибели команды";
                    yield return WaitForClientVerdicts(context);
                    yield break;
                }

                // ── 10. Длительности фаз по настоящим часам (T-09) ────────
                E2EWaitOutcome scoreboard = new E2EWaitOutcome();
                yield return E2EWait.Until(scoreboard,
                    "раунд перешёл в Scoreboard",
                    PhaseWait,
                    () => elimination.CurrentRoundState == RoundState.Scoreboard,
                    () => DescribeMatch(elimination, walls));

                float resolutionSpan = MeasurePhase(RoundState.Resolution);
                result.Set(CheckResolution, WithinTolerance(resolutionSpan, RoundManager.ResolutionDuration),
                    DescribeSpan("Resolution", resolutionSpan, RoundManager.ResolutionDuration));

                E2EWaitOutcome nextRound = new E2EWaitOutcome();
                yield return E2EWait.Until(nextRound,
                    "начался следующий раунд (фаза вернулась в Setup)",
                    PhaseWait,
                    () => elimination.CurrentRoundNumber >= 2,
                    () => DescribeMatch(elimination, walls));

                float scoreboardSpan = MeasurePhase(RoundState.Scoreboard);
                result.Set(CheckScoreboard, WithinTolerance(scoreboardSpan, RoundManager.ScoreboardDuration),
                    DescribeSpan("Scoreboard", scoreboardSpan, RoundManager.ScoreboardDuration));

                // ── 11. Следующий раунд снова ждёт готовности ─────────────
                E2EWaitOutcome equipmentAgain = new E2EWaitOutcome();
                yield return E2EWait.Until(equipmentAgain,
                    "второй раунд дошёл до фазы Equipment",
                    PhaseWait,
                    () => elimination.CurrentRoundState == RoundState.Equipment,
                    () => DescribeMatch(elimination, walls));

                // Держим фазу закупки: если бы готовность прошлого раунда дожила
                // до этого, раунд проскочил бы закупку прямо сейчас.
                yield return E2EWait.Hold(Rdy01Hold);

                bool readinessReset = true;
                foreach (PlayerSession session in all)
                {
                    if (session != null && session.ReadyState) readinessReset = false;
                }

                bool waitsAgain = equipmentAgain.Succeeded
                                  && elimination.CurrentRoundState == RoundState.Equipment
                                  && readinessReset;

                result.Set(CheckNextRound, waitsAgain,
                    waitsAgain
                        ? $"раунд {elimination.CurrentRoundNumber}: готовность сброшена у всех, " +
                          $"закупка держится {Rdy01Hold:F0} с и ждёт её заново. " +
                          $"Полная лента фаз: {DescribeTimeline()}"
                        : !equipmentAgain.Succeeded
                            ? equipmentAgain.Diagnosis
                            : !readinessReset
                                ? "готовность пережила конец раунда: " + DescribeReadiness(all) +
                                  ". Тогда фаза закупки второго раунда кончается, не начавшись, — " +
                                  "арсенал открывается и тут же закрывается."
                                : $"второй раунд не удержался в закупке: фаза {elimination.CurrentRoundState}. " +
                                  $"Лента фаз: {DescribeTimeline()}");

                yield return WaitForClientVerdicts(context);

                result.Summary = result.AllChecksGreen
                    ? "раунд прожит целиком на двух процессах; RDY-01 не воспроизводится, " +
                      $"Resolution={resolutionSpan:F2} с, Scoreboard={scoreboardSpan:F2} с по настоящим часам"
                    : "есть красные проверки, см. detail";
            }
            finally
            {
                EliminationMode.OnRoundStateChangedLocal -= MarkPhase;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль клиента
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            result.Declare(CheckClientConnected, CheckClientSession, CheckClientMap,
                           CheckClientWallOpen, CheckClientRdy01, CheckClientPending,
                           CheckClientWallShut, CheckClientPhases);

            DisableDebugOrchestrator();

            EliminationMode.OnRoundStateChangedLocal += MarkPhase;

            try
            {
                float deadline = Now + 30f;
                while (!NetworkClient.isConnected && Now < deadline)
                    yield return null;

                if (!NetworkClient.isConnected && !string.IsNullOrEmpty(context.ServerAddress))
                {
                    GameLog.Debug.Info($"[E2E] Discovery молчит 30 с, подключаюсь напрямую к {context.ServerAddress}");

                    Mirror.Discovery.NetworkDiscovery discovery =
                        Object.FindFirstObjectByType<Mirror.Discovery.NetworkDiscovery>();
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
                        ? $"подключён к {(NetworkManager.singleton != null ? NetworkManager.singleton.networkAddress : "?")}"
                        : $"за 60 с подключиться не удалось: ни Discovery, ни прямое подключение к '{context.ServerAddress}'");

                if (!connected)
                {
                    result.Summary = "клиент не подключился";
                    yield break;
                }

                deadline = Now + 60f;
                while (PlayerSession.LocalSession == null && Now < deadline)
                    yield return null;

                PlayerSession local = PlayerSession.LocalSession;
                result.Set(CheckClientSession, local != null,
                    local != null
                        ? $"PlayerSession.LocalSession netId={local.netId}"
                        : "за 60 с сервер не создал PlayerSession для этого клиента");

                if (local == null)
                {
                    result.Summary = "сессии нет, отчитываться нечем";
                    yield break;
                }

                deadline = Now + 150f;
                while (SceneManager.GetActiveScene().name != context.Map && Now < deadline)
                    yield return null;

                bool onMap = SceneManager.GetActiveScene().name == context.Map;
                result.Set(CheckClientMap, onMap,
                    onMap
                        ? $"активная сцена='{SceneManager.GetActiveScene().name}'"
                        : $"за 150 с клиент не переехал на '{context.Map}', остался в '{SceneManager.GetActiveScene().name}'");

                if (!onMap)
                {
                    result.Summary = "клиент не на карте";
                    ReportVerdictReady();
                    yield break;
                }

                // ── Общая стена ───────────────────────────────────────────
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
                    result.Set(CheckClientWallOpen, false,
                        "за 90 с на клиенте не нашлось ни одной заспавненной стены арсенала — " +
                        "либо карта не загрузилась, либо у стены нет sceneId (NET-14).");
                    result.Summary = "клиент не увидел стену арсенала";
                    ReportVerdictReady();
                    yield break;
                }

                // netId запоминаем заранее. При обрыве связи Mirror зовёт
                // NetworkIdentity.Reset, и обращение к netId на живой ссылке уже падает —
                // вердикт, читающий его в конце, обрушил бы сценарий вместо диагноза.
                uint wallNetId = shared.netId;

                E2EWaitOutcome wallOpen = new E2EWaitOutcome();
                yield return E2EWait.Until(wallOpen,
                    $"стена netId={wallNetId} открылась у этого клиента",
                    ClientPhaseWait,
                    () => shared.CurrentState == ArsenalWallController.ArsenalState.Open,
                    () => DescribeClient(shared),
                    () => NetworkClient.isConnected ? null : "связь с сервером пропала");

                result.Set(CheckClientWallOpen, wallOpen.Succeeded, wallOpen.Diagnosis);

                if (!wallOpen.Succeeded)
                {
                    result.Summary = "клиент не увидел открытую стену";
                    ReportVerdictReady();
                    yield break;
                }

                // ── RDY-01 со стороны неготового игрока ───────────────────
                // Ждём состояния «готов ровно один» — его объявляет сервер. Условие
                // читается по реплицированным сессиям, а не по договорённости о времени:
                // синхронизировать два клиентских процесса по часам было бы гаданием.
                E2EWaitOutcome oneReady = new E2EWaitOutcome();
                yield return E2EWait.Until(oneReady,
                    "ровно один игрок объявил готовность",
                    ClientPhaseWait,
                    () => ReadyCount() == 1,
                    () => DescribeClient(shared),
                    () => NetworkClient.isConnected ? null : "связь с сервером пропала");

                EliminationMode elimination = Object.FindFirstObjectByType<EliminationMode>();

                if (!oneReady.Succeeded)
                {
                    result.Set(CheckClientRdy01, false,
                        oneReady.Diagnosis + " Состояние «готов ровно один» до клиента не доехало, " +
                        "и проверять на нём RDY-01 нельзя.");
                    result.Set(CheckClientPending, false,
                        "состав неготовых снимается в том же окне и по той же причине не снят.");
                }
                else
                {
                    // Стена обязана оставаться открытой всё то время, пока готов не каждый.
                    // Ждём не события, а его отсутствия, поэтому здесь именно выдержка.
                    //
                    // В этом же окне снимается состав неготовых: как только сервер объявит
                    // готовность за всех, список опустеет — и проверять, доезжал ли он
                    // вообще, станет нечем.
                    float until = Now + Rdy01Hold * 0.5f;
                    bool stayedOpen = true;
                    bool pendingSeen = false;
                    string pendingSnapshot = "пусто";

                    while (Now < until)
                    {
                        if (ReadyCount() > 1) break;   // сервер уже объявил готовность за всех

                        if (!pendingSeen && elimination != null && elimination.PendingReadiness.Count > 0)
                        {
                            pendingSeen = true;
                            pendingSnapshot = DescribePending(elimination);
                        }

                        if (shared.CurrentState != ArsenalWallController.ArsenalState.Open)
                        {
                            stayedOpen = false;
                            break;
                        }

                        yield return null;
                    }

                    result.Set(CheckClientRdy01, stayedOpen,
                        stayedOpen
                            ? $"готовность объявил один игрок из {SessionCountLocal()}, а стена netId={wallNetId} " +
                              "у этого клиента осталась открытой — экипироваться ещё можно"
                            : $"стена netId={wallNetId} закрылась ({shared.CurrentState}), пока готов был только " +
                              "один игрок. Это RDY-01: стена общая, и первый объявивший готовность оставляет " +
                              "остальных без снаряжения.");

                    result.Set(CheckClientPending, pendingSeen,
                        pendingSeen
                            ? $"клиент видит состав неготовых: [{pendingSnapshot}] — " +
                              "HUD может показать, кого ждут, не спрашивая сервер"
                            : elimination == null
                                ? "на клиенте не нашлось EliminationMode — состав неготовых читать неоткуда"
                                : "список неготовых у клиента пуст всё окно ожидания, хотя готовность объявил " +
                                  "только один игрок. SyncList не доехал: состав неготовых обязан быть " +
                                  "состоянием, а не событием.");
                }

                // ── Стена закрылась, когда готовы стали все (NET-07) ──────
                E2EWaitOutcome wallShut = new E2EWaitOutcome();
                yield return E2EWait.Until(wallShut,
                    $"стена netId={wallNetId} вышла из Open после готовности всех",
                    ClientPhaseWait,
                    () => shared.CurrentState != ArsenalWallController.ArsenalState.Open,
                    () => DescribeClient(shared),
                    () => NetworkClient.isConnected ? null : "связь с сервером пропала");

                result.Set(CheckClientWallShut, wallShut.Succeeded,
                    wallShut.Succeeded
                        ? wallShut.Diagnosis + " Состояние стены общее: закрытие объявил сервер, и оно доехало сюда."
                        : wallShut.Diagnosis + " Сервер закрыл стену, а сюда это не доехало — сверься с server.json.");

                // ── Весь раунд, увиденный клиентом ────────────────────────
                E2EWaitOutcome phases = new E2EWaitOutcome();
                yield return E2EWait.Until(phases,
                    "клиент увидел Countdown, Combat, Resolution и Scoreboard",
                    ClientPhaseWait,
                    () => SawPhase(RoundState.Countdown) && SawPhase(RoundState.Combat)
                          && SawPhase(RoundState.Resolution) && SawPhase(RoundState.Scoreboard),
                    () => "лента фаз у клиента: " + DescribeTimeline(),
                    () => NetworkClient.isConnected ? null : "связь с сервером пропала");

                result.Set(CheckClientPhases, phases.Succeeded,
                    phases.Succeeded
                        ? "клиент прожил весь раунд вместе с сервером: " + DescribeTimeline()
                        : phases.Diagnosis + " Фаза раунда — состояние (SyncVar с хуком), " +
                          "и до клиента обязана доезжать каждая.");

                result.Summary = result.AllChecksGreen
                    ? "клиент увидел открытый арсенал при одном готовом игроке и прожил раунд целиком"
                    : "есть красные проверки, см. detail";

                ReportVerdictReady();
            }
            finally
            {
                EliminationMode.OnRoundStateChangedLocal -= MarkPhase;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Барьер клиентских вердиктов
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Держит серверный процесс живым, пока клиенты не запишут вердикты. Гасить
        /// сервер раньше — значит обрывать клиентские ожидания на середине и получать
        /// мигающие ворота (TEST-01).
        /// </summary>
        private IEnumerator WaitForClientVerdicts(E2EContext context)
        {
            E2EWaitOutcome barrier = new E2EWaitOutcome();

            yield return E2EWait.Until(barrier,
                $"все {context.ExpectedClients} клиент(а) отчитались, что записали вердикт",
                ClientVerdictWait,
                () => PollVerdicts() >= context.ExpectedClients,
                () => $"отчитались {PollVerdicts()} из {context.ExpectedClients}; " +
                      $"подключений на сервере: {NetworkServer.connections.Count}");

            GameLog.Debug.Info($"[E2E] Барьер клиентских вердиктов: {barrier.Diagnosis}");
        }

        /// <summary>
        /// Обратный канал «мой вердикт записан». Именно <c>HasGrabbedDogTag</c>: с T-29
        /// это чистый жест, фазу раунда он больше не двигает вовсе, поэтому служебный
        /// сигнал сценария не может испортить сам матч.
        /// </summary>
        private static void ReportVerdictReady()
        {
            PlayerSession local = PlayerSession.LocalSession;
            if (local == null)
            {
                GameLog.Debug.Info("[E2E] Отчитаться о вердикте нечем: локальной сессии нет");
                return;
            }

            GameLog.Debug.Info("[E2E] Вердикт записан — отчитываюсь серверу");
            local.CmdSetDogTagGrabbed(true);
        }

        /// <summary>
        /// Кто уже отчитался. Накопительно: начало нового раунда сбрасывает
        /// <c>HasGrabbedDogTag</c> вместе с готовностью, а клиент отчитывается раньше —
        /// сразу после экрана итогов. Мгновенный снимок флагов увидел бы ноль
        /// и продержал бы сервер весь срок барьера впустую.
        /// </summary>
        private readonly HashSet<uint> _reportedVerdicts = new HashSet<uint>();

        private int PollVerdicts()
        {
            if (PlayersManager.Instance == null) return _reportedVerdicts.Count;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.HasGrabbedDogTag) _reportedVerdicts.Add(session.netId);
            }

            return _reportedVerdicts.Count;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Лента фаз и замер длительностей
        // ══════════════════════════════════════════════════════════════════

        private void MarkPhase(RoundState phase)
        {
            _timeline.Add(new PhaseMark(phase, Now));
            GameLog.Debug.Info($"[E2E] Фаза {phase} в {Now:F2} с");
        }

        private bool SawPhase(RoundState phase)
        {
            foreach (PhaseMark mark in _timeline)
            {
                if (mark.Phase == phase) return true;
            }

            return false;
        }

        /// <summary>
        /// Сколько реального времени прожила фаза: от отметки о входе в неё до отметки
        /// о следующей фазе. Отрицательное значение означает, что фазы в ленте нет
        /// или она ещё идёт.
        /// </summary>
        private float MeasurePhase(RoundState phase)
        {
            for (int i = 0; i < _timeline.Count - 1; i++)
            {
                if (_timeline[i].Phase == phase)
                    return _timeline[i + 1].At - _timeline[i].At;
            }

            return -1f;
        }

        private static bool WithinTolerance(float measured, float expected)
        {
            return measured >= 0f && Mathf.Abs(measured - expected) <= PhaseDurationTolerance;
        }

        private string DescribeSpan(string phase, float measured, float expected)
        {
            if (measured < 0f)
                return $"фазу {phase} не удалось замерить: в ленте нет пары «вход — следующая фаза». " +
                       $"Лента: {DescribeTimeline()}";

            bool ok = WithinTolerance(measured, expected);
            return ok
                ? $"{phase} прожил {measured:F2} с при заявленных {expected:F1} с " +
                  $"(допуск ±{PhaseDurationTolerance:F1} с) — по настоящим часам, а не по тикам EditMode"
                : $"{phase} прожил {measured:F2} с вместо {expected:F1} с (допуск ±{PhaseDurationTolerance:F1} с). " +
                  $"Лента фаз: {DescribeTimeline()}";
        }

        private string DescribeTimeline()
        {
            if (_timeline.Count == 0) return "(пусто)";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _timeline.Count; i++)
            {
                if (i > 0) sb.Append(" -> ");
                sb.Append(_timeline[i].Phase);

                if (i < _timeline.Count - 1)
                    sb.Append($"({_timeline[i + 1].At - _timeline[i].At:F2}с)");
            }

            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════════════
        //  RDY-04: кому зона засчитывает «в зоне»
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Уводит игрока в базу противника и спрашивает, продолжает ли сервер считать
        /// его стоящим у себя на спавне.
        ///
        /// <para>
        /// <b>Что доказывает.</b> <c>TeamSpawnZone</c> писала признак «в зоне» любому
        /// вошедшему, не спрашивая команду, — хотя соседние методы того же класса команду
        /// проверяли. Игрок, забежавший в базу противника, считался стоящим у себя и
        /// сохранял право объявить готовность к раунду оттуда (T-29).
        /// </para>
        ///
        /// <para>
        /// <b>Почему проверок две.</b> «Флаг не взвёлся» само по себе ничего не значит:
        /// так выглядело бы и полностью сломанное определение зоны. Поэтому игрока
        /// возвращают к себе на спавн и требуют флаг обратно. А чтобы «в чужой базе»
        /// не оказалось «нигде», физическое нахождение подтверждается самой зоной —
        /// <c>TeamSpawnZone.IsPlayerFullyInZone</c>, тем же методом, которым она считает
        /// своих.
        /// </para>
        ///
        /// <para>
        /// Замер читает только <c>PlayerSession.IsInSpawnZone</c> — то, что существует
        /// и до правки, и после. Иначе прогон «до правки» было бы нечем собрать.
        /// </para>
        /// </summary>
        private IEnumerator CheckForeignSpawnZone(E2EResult result)
        {
            E2EWaitOutcome avatarsUp = new E2EWaitOutcome();
            yield return E2EWait.Until(avatarsUp,
                "у обеих сессий появился аватар на карте",
                RdyZoneWait,
                AllSessionsHaveAvatar,
                DescribeSpawnZones,
                () => NetworkServer.connections.Count > 0 ? null : "подключений не осталось");

            List<PlayerSession> all = AllSessions();
            PlayerSession traveller = all.Count > 0 ? all[0] : null;

            if (!avatarsUp.Succeeded || traveller == null || traveller.Team == null)
            {
                string why = avatarsUp.Diagnosis +
                             $" Сессий: {all.Count}, команда первой: " +
                             $"{(traveller == null || traveller.Team == null ? "не назначена" : traveller.Team.displayName)}.";
                result.Set(CheckRdy04, false, why);
                result.Set(CheckRdy04Back, false, why);
                yield break;
            }

            TeamSpawnZone ownZone = AvatarSpawnPointResolver.FindZone(traveller.Team);
            TeamSpawnZone enemyZone = FindForeignZone(traveller.Team);

            if (ownZone == null || enemyZone == null)
            {
                string why = $"на карте '{SceneManager.GetActiveScene().name}' не нашлось пары зон: " +
                             $"своя={(ownZone == null ? "нет" : ownZone.name)}, " +
                             $"чужая={(enemyZone == null ? "нет" : enemyZone.name)}. {DescribeSpawnZones()}";
                result.Set(CheckRdy04, false, why);
                result.Set(CheckRdy04Back, false, why);
                yield break;
            }

            // Контроль до опыта: если зона не срабатывает вовсе, красный ниже означал бы
            // «триггеры молчат», а не находку.
            E2EWaitOutcome atHome = new E2EWaitOutcome();
            yield return E2EWait.Until(atHome,
                $"{traveller.PlayerName} числится в зоне своей команды на спавне",
                RdyZoneWait,
                () => traveller.IsInSpawnZone,
                () => DescribeTraveller(traveller, ownZone, enemyZone),
                () => NetworkServer.connections.Count > 0 ? null : "подключений не осталось");

            if (!atHome.Succeeded)
            {
                string why = atHome.Diagnosis +
                             " Игрок появляется в зоне своей команды (AvatarSpawnPointResolver), " +
                             "и зона обязана его засчитать. Раз не засчитала — триггеры зоны " +
                             "в этом прогоне не срабатывают вовсе, и вердикт по RDY-04 недостоверен.";
                result.Set(CheckRdy04, false, why);
                result.Set(CheckRdy04Back, false, why);
                yield break;
            }

            // ── В базу противника ─────────────────────────────────────────
            yield return DisplaceAvatar(traveller, enemyZone.transform.position);

            PlayerController avatar = traveller.ActiveAvatar;
            bool physicallyInside = avatar != null && enemyZone.IsPlayerFullyInZone(avatar);
            bool countedAsHome = traveller.IsInSpawnZone;

            result.Set(CheckRdy04, physicallyInside && !countedAsHome,
                physicallyInside && !countedAsHome
                    ? $"{traveller.PlayerName} (команда '{traveller.Team.displayName}') стоит внутри зоны " +
                      $"'{enemyZone.name}' и в своей зоне не числится. {DescribeTraveller(traveller, ownZone, enemyZone)}"
                    : !physicallyInside
                        ? "увести игрока в базу противника не удалось — измерять нечего. " +
                          DescribeTraveller(traveller, ownZone, enemyZone)
                        : $"{traveller.PlayerName} стоит в базе противника ('{enemyZone.name}'), а сервер " +
                          "считает его стоящим в своей зоне. Это RDY-04: TeamSpawnZone пишет признак " +
                          "любому вошедшему, без проверки команды, — и такой игрок вправе объявить " +
                          "готовность к раунду из чужой базы (T-29). " +
                          DescribeTraveller(traveller, ownZone, enemyZone));

            // ── Обратно к себе ────────────────────────────────────────────
            yield return DisplaceAvatar(traveller, ownZone.transform.position);

            E2EWaitOutcome backHome = new E2EWaitOutcome();
            yield return E2EWait.Until(backHome,
                $"{traveller.PlayerName} снова числится в зоне своей команды",
                RdyZoneWait,
                () => traveller.IsInSpawnZone,
                () => DescribeTraveller(traveller, ownZone, enemyZone),
                () => NetworkServer.connections.Count > 0 ? null : "подключений не осталось");

            result.Set(CheckRdy04Back, backHome.Succeeded,
                backHome.Succeeded
                    ? $"игрок вернулся на свой спавн и снова числится в зоне: {backHome.Diagnosis}"
                    : backHome.Diagnosis +
                      " Без этого контроля зелёная проверка выше ничего не значит: «в зоне не числится» " +
                      "так же выглядело бы, если бы зона перестала засчитывать вообще кого-либо.");
        }

        /// <summary>Зона спавна любой команды, кроме заданной.</summary>
        private static TeamSpawnZone FindForeignZone(TeamData ownTeam)
        {
            foreach (TeamSpawnZone zone in Object.FindObjectsByType<TeamSpawnZone>(FindObjectsSortMode.InstanceID))
            {
                if (zone != null && zone.Team != null && zone.Team != ownTeam)
                    return zone;
            }

            return null;
        }

        private static bool AllSessionsHaveAvatar()
        {
            if (PlayersManager.Instance == null || PlayersManager.Instance.Sessions.Count == 0)
                return false;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null || session.ActiveAvatar == null) return false;
            }

            return true;
        }

        private static string DescribeSpawnZones()
        {
            List<string> parts = new List<string>();
            foreach (TeamSpawnZone zone in Object.FindObjectsByType<TeamSpawnZone>(FindObjectsSortMode.InstanceID))
            {
                if (zone == null) continue;
                parts.Add($"'{zone.name}' команды " +
                          $"'{(zone.Team != null ? zone.Team.displayName : "не назначена")}' в {Fmt(zone.transform.position)}");
            }

            return parts.Count > 0 ? "Зоны: " + string.Join("; ", parts.ToArray()) : "Зон спавна на сцене нет.";
        }

        private static string DescribeTraveller(PlayerSession session, TeamSpawnZone ownZone, TeamSpawnZone enemyZone)
        {
            PlayerController avatar = session != null ? session.ActiveAvatar : null;
            if (avatar == null) return $"{(session != null ? session.PlayerName : "?")}: аватара нет.";

            Vector3 position = avatar.transform.position;

            return $"{session.PlayerName} в {Fmt(position)}: IsInSpawnZone={session.IsInSpawnZone}, " +
                   $"до своей зоны '{ownZone.name}' {Vector3.Distance(position, ownZone.transform.position):F2} м " +
                   $"(целиком внутри={ownZone.IsPlayerFullyInZone(avatar)}), " +
                   $"до чужой '{enemyZone.name}' {Vector3.Distance(position, enemyZone.transform.position):F2} м " +
                   $"(целиком внутри={enemyZone.IsPlayerFullyInZone(avatar)}).";
        }

        /// <summary>
        /// Уводит игрока в заданную точку игровым способом — <c>PlayerController.Respawn</c>.
        ///
        /// <para>
        /// Прямая запись <c>transform.position</c> на сервере ненадёжна:
        /// <c>NetworkTransform</c> на аватарах стоит с <c>syncDirection = ClientToServer</c>,
        /// и владелец вернёт свою позицию поверх серверной ближайшим же пакетом. Поэтому
        /// сначала <c>ServerDevTeleport</c> — он рассылает <c>RpcDevTeleport</c>, и настоящий переезд
        /// делает сам владелец, — а прямая запись остаётся запасным вариантом.
        /// </para>
        /// </summary>
        private static IEnumerator DisplaceAvatar(PlayerSession session, Vector3 target)
        {
            PlayerController avatar = session.ActiveAvatar;
            if (avatar == null) yield break;

            GameObject marker = new GameObject("E2E_DisplaceTarget");
            marker.transform.SetPositionAndRotation(target, avatar.transform.rotation);

            avatar.ServerDevTeleport(marker.transform.position, marker.transform.rotation);

            E2EWaitOutcome moved = new E2EWaitOutcome();
            yield return E2EWait.Until(moved,
                $"серверная копия аватара доехала до {Fmt(target)}",
                RdyZoneWait,
                () => session.ActiveAvatar != null &&
                      Vector3.Distance(session.ActiveAvatar.transform.position, target) <= ZoneReachTolerance,
                () => session.ActiveAvatar == null
                    ? "аватар исчез"
                    : $"аватар в {Fmt(session.ActiveAvatar.transform.position)}, до цели " +
                      $"{Vector3.Distance(session.ActiveAvatar.transform.position, target):F2} м",
                () => NetworkServer.connections.Count > 0 ? null : "подключений не осталось");

            GameLog.Debug.Info($"[E2E] Перенос игрока: {moved.Diagnosis}");

            if (!moved.Succeeded && session.ActiveAvatar != null)
            {
                GameLog.Debug.Info("[E2E] Владелец не отчитался — ставлю аватар на место записью на сервере");
                session.ActiveAvatar.transform.position = target;
            }

            // Триггерам нужен физический тик, а признак зоны едет через OnTriggerStay.
            yield return E2EWait.Hold(ZoneSettleHold);
        }

        private static string Fmt(Vector3 v)
        {
            return $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное
        // ══════════════════════════════════════════════════════════════════

        private static float Now => Time.realtimeSinceStartup;

        private static int SessionCount()
        {
            return PlayersManager.Instance != null ? PlayersManager.Instance.Sessions.Count : 0;
        }

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

        /// <summary>
        /// Сколько сессий на этой машине объявили готовность. На клиенте
        /// <c>PlayersManager</c> пуст (он серверный), поэтому сессии ищутся по сцене:
        /// сами объекты Mirror спавнит всем наблюдателям, и <c>ReadyState</c> —
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

        private static int SessionCountLocal()
        {
            return Object.FindObjectsByType<PlayerSession>(FindObjectsInactive.Include).Length;
        }

        private static bool PendingContainsOnly(EliminationMode mode, PlayerSession expected)
        {
            if (mode == null || expected == null) return false;
            if (mode.PendingReadiness.Count != 1) return false;

            return mode.PendingReadiness[0] == expected.netId;
        }

        private static string DescribePending(EliminationMode mode)
        {
            if (mode == null) return "режим не найден";
            if (mode.PendingReadiness.Count == 0) return "пусто";

            List<string> parts = new List<string>();
            foreach (uint netId in mode.PendingReadiness)
                parts.Add($"netId={netId}");

            return string.Join(", ", parts.ToArray());
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
        /// Бьёт насмерть всех живых игроков команды указанной сессии. Урон настоящий —
        /// <c>UxrActor.ReceiveDamage</c>, тот же вход, которым пользуется оружие:
        /// присвоение <c>Life = 0</c> доставило бы клиентам число, но не запустило бы
        /// <c>DieInternal</c> и всю цепочку конца раунда.
        /// </summary>
        private static string KillTeamOf(PlayerSession member)
        {
            List<string> killed = new List<string>();

            foreach (PlayerSession session in AllSessions())
            {
                if (session.TeamIndex != member.TeamIndex) continue;

                PlayerController avatar = session.ActiveAvatar;
                if (avatar == null || !avatar.IsAlive || avatar._actor == null) continue;

                avatar._actor.ReceiveDamage(1000f);
                killed.Add($"{session.PlayerName} (команда {session.TeamIndex})");
            }

            return killed.Count == 0 ? "убивать было некого" : string.Join(", ", killed.ToArray());
        }

        private static string DescribeMatch(EliminationMode mode, ArsenalWallController[] walls)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("фаза=").Append(mode != null ? mode.CurrentRoundState.ToString() : "нет режима")
              .Append(", матч=").Append(mode != null ? mode.CurrentMatchState.ToString() : "?")
              .Append(", раунд=").Append(mode != null ? mode.CurrentRoundNumber : -1)
              .Append(", неготовы=[").Append(DescribePending(mode)).Append("]");

            if (walls != null && walls.Length > 0)
            {
                sb.Append(", стены=");
                for (int i = 0; i < walls.Length; i++)
                {
                    if (i > 0) sb.Append('/');
                    sb.Append(walls[i].CurrentState);
                }
            }

            sb.Append(", готовность: ").Append(DescribeReadiness(AllSessions()));
            return sb.ToString();
        }

        private static string DescribeClient(ArsenalWallController wall)
        {
            return $"стена={(wall != null ? wall.CurrentState.ToString() : "нет")}, " +
                   $"готовых сессий={ReadyCount()} из {SessionCountLocal()}, " +
                   $"связь={(NetworkClient.isConnected ? "есть" : "нет")}, " +
                   $"сцена='{SceneManager.GetActiveScene().name}'";
        }

        /// <summary>
        /// Стена, которую все процессы понимают одинаково: с наименьшим ненулевым
        /// <c>netId</c>. Порядок <c>FindObjectsByType</c> в разных процессах разный,
        /// а netId сетевого объекта сцены раздаёт сервер — он общий.
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

        /// <summary>
        /// Раскладывает сессии по командам режима по кругу. Нужно и ради матча
        /// (он не выйдет из <c>WaitingForPlayers</c>, пока в каждой команде нет игрока),
        /// и ради <c>AvatarManager.ChangeAvatar</c>: он молча выходит, если
        /// <c>TeamRegistry</c> не знает индекс команды.
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

            orchestrator.enabled = false;
            GameLog.Debug.Info("[E2E] DebugOrchestrator отключён: дирижёром прогона выступает сценарий");
        }
    }
}
#endif
