using System.Collections;
using System.Text.Json;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools.E2E;

// Каркас смок-теста серии (e2e-smoke-kit): топология, журнал шагов, барьер без Mirror, ожидания.
// Сетевая обвязка Mirror (E2EBarrier) здесь не собирается — её проверяет реальный мультипроцесс.
internal static class E2EKitTests
{
    internal static void Run(Action<string, Action> test)
    {
        TopologyTests(test);
        JournalTests(test);
        RulesTests(test);
        ClientCoreTests(test);
        ServerCoreTests(test);
        WaitTests(test);
    }

    private static E2EContext Parse(params string[] args) => E2EContext.FromArgs(args);

    private static void TopologyTests(Action<string, Action> test)
    {
        test("e2e: client-2 из 3 с поздним client-3 — индекс 2, не поздний", () =>
        {
            var ctx = Parse("player.exe", "-e2eScenario", "s", "-e2eRole", "client-2", "-e2eClients", "3", "-e2eLate", "client-3");
            Check(ctx.ClientIndex == 2 && ctx.ClientCount == 3 && ctx.LateRole == "client-3" && !ctx.IsLateClient &&
                  ctx.TopologyError == null && !ctx.IsServerSide, "контекст client-2: " + ctx);
        });
        test("e2e: поздний клиент видит IsLateClient, сервер — индекс 0", () =>
        {
            var late = Parse("p", "-e2eScenario", "s", "-e2eRole", "client-3", "-e2eClients", "3", "-e2eLate", "client-3");
            var server = Parse("p", "-e2eScenario", "s", "-e2eRole", "server", "-e2eClients", "3", "-e2eLate", "client-3");
            var host = Parse("p", "-e2eScenario", "s", "-e2eRole", "host", "-e2eClients", "1");
            Check(late.IsLateClient && late.ClientIndex == 3, "поздний");
            Check(server.ClientIndex == 0 && server.IsServerSide && !server.IsLateClient && server.LateRole == "client-3", "сервер");
            Check(host.ClientIndex == 0 && host.IsServerSide && !host.IsServerRole && host.LateRole == null, "хост");
        });
        test("e2e: роль вне схемы client-N — индекс -1, без позднего клиента ошибки нет", () =>
        {
            var ctx = Parse("p", "-e2eScenario", "s", "-e2eRole", "client");
            Check(ctx.ClientIndex == -1 && ctx.TopologyError == null && ctx.LateRole == null && ctx.ClientCount == 2, "client без индекса");
            Check(Parse("p", "-e2eScenario", "s", "-e2eRole", "client-01").ClientIndex == -1, "ведущий ноль");
        });
        test("e2e: -e2eLate без роли или вне client-1..N — ошибка топологии", () =>
        {
            var noValue = Parse("p", "-e2eScenario", "s", "-e2eRole", "server", "-e2eLate", "-e2eClients", "2");
            var outside = Parse("p", "-e2eScenario", "s", "-e2eRole", "server", "-e2eClients", "2", "-e2eLate", "client-3");
            var server = Parse("p", "-e2eScenario", "s", "-e2eRole", "server", "-e2eClients", "2", "-e2eLate", "server");
            Check(noValue.TopologyError != null && noValue.LateRole == null, "без значения");
            Check(outside.TopologyError != null && outside.TopologyError.Contains("client-3") && outside.LateRole == null, "вне набора");
            Check(server.TopologyError != null, "сервер поздним");
            Check(outside.ToString().Contains("ошибка топологии"), "ошибка видна в старте харнесса");
        });
        test("e2e: Create отклоняет позднего вне набора, -e2eArg прежний", () =>
        {
            Throws<ArgumentException>(() => E2EContext.Create("s", "server", expectedClients: 2, lateRole: "client-5"));
            var ctx = Parse("p", "-e2eScenario", "s", "-e2eArg", "maps=TestMap1;TestMap2", "-e2eArg", "rounds=2");
            Check(ctx.ArgError == null && ctx.Arg("maps") == "TestMap1;TestMap2" && ctx.Arg("rounds") == "2" && ctx.Arg("x", "d") == "d", "Arg");
        });
    }

    private static void JournalTests(Action<string, Action> test)
    {
        test("e2e: журнал steps в JSON — UTC, порядок, прежние поля целы", () =>
        {
            var result = new E2EResult { Status = E2EResult.StatusCompleted };
            result.Declare("first"); result.Set("first", true, "done");
            var at = new DateTime(2026, 10, 14, 9, 30, 0, DateTimeKind.Utc);
            result.AddStep("map-1-ready", "client-1", "TestMap1 \"q\"", at, E2EStepOutcome.Reached);
            result.AddStep("map-1-ready", "server", "", at.AddSeconds(1), E2EStepOutcome.BarrierPassed);
            using var document = JsonDocument.Parse(result.ToJson());
            var root = document.RootElement;
            var steps = root.GetProperty("steps");
            Check(steps.GetArrayLength() == 2, "две записи");
            var first = steps[0];
            Check(first.GetProperty("name").GetString() == "map-1-ready" && first.GetProperty("role").GetString() == "client-1" &&
                  first.GetProperty("data").GetString() == "TestMap1 \"q\"" && first.GetProperty("outcome").GetString() == "reached" &&
                  first.GetProperty("utc").GetString() == "2026-10-14T09:30:00.0000000Z", "запись шага: " + first);
            Check(steps[1].GetProperty("outcome").GetString() == "barrier-passed", "исход барьера");
            Check(root.GetProperty("stepsDropped").GetInt32() == 0 && root.GetProperty("checks").GetArrayLength() == 1 &&
                  root.GetProperty("knownNoise").GetArrayLength() == 0 && root.GetProperty("passed").GetBoolean(), "прежние поля");
        });
        test("e2e: журнал шагов ограничен, лишнее считается в stepsDropped", () =>
        {
            var result = new E2EResult();
            for (int i = 0; i < E2EResult.MaxStepRecords + 3; i++)
                result.AddStep("s" + i, "server", "", DateTime.UtcNow, E2EStepOutcome.Reached);
            using var document = JsonDocument.Parse(result.ToJson());
            Check(result.Steps.Count == E2EResult.MaxStepRecords && result.StepsDropped == 3 &&
                  document.RootElement.GetProperty("stepsDropped").GetInt32() == 3, "лимит журнала");
        });
        test("e2e: локальное время журнала переводится в UTC", () =>
        {
            var result = new E2EResult();
            var local = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Local);
            result.AddStep("x", "server", "", local, E2EStepOutcome.Reached);
            Check(result.Steps[0].Utc.Kind == DateTimeKind.Utc && result.Steps[0].Utc == local.ToUniversalTime(), "UTC");
        });
    }

    private static void RulesTests(Action<string, Action> test)
    {
        test("e2e: лимиты длины имени и data шага", () =>
        {
            Check(E2EStepRules.ValidateStep(new string('a', 64), new string('d', 256)) == null, "на пределе");
            Check(E2EStepRules.ValidateStep(new string('a', 65), null) != null, "имя 65");
            Check(E2EStepRules.ValidateStep("ok", new string('d', 257)) != null, "data 257");
            Check(E2EStepRules.ValidateStep("", null) != null && E2EStepRules.ValidateStep(null, null) != null, "пустое имя");
        });
        test("e2e: роли клиента и ожидаемый набор без позднего", () =>
        {
            Check(E2EStepRules.ValidateClientRole("client-2", 2) == null, "client-2 из 2");
            Check(E2EStepRules.ValidateClientRole("client-3", 2) != null && E2EStepRules.ValidateClientRole("server", 2) != null &&
                  E2EStepRules.ValidateClientRole("client-0", 2) != null && E2EStepRules.ValidateClientRole(new string('c', 65), 2) != null, "вне набора");
            Check(string.Join(",", E2EStepRules.ExpectedClientRoles(3, "client-3", false)) == "client-1,client-2", "без позднего");
            Check(string.Join(",", E2EStepRules.ExpectedClientRoles(3, "client-3", true)) == "client-1,client-2,client-3", "с поздним");
        });
    }

    private static (E2EBarrierCore core, E2EContext ctx, E2EResult journal) Client(string role = "client-1", int clients = 2)
    {
        var core = new E2EBarrierCore();
        var ctx = E2EContext.Create("smoke", role, expectedClients: clients);
        var journal = new E2EResult();
        core.Configure(ctx, journal);
        return (core, ctx, journal);
    }

    private static void ClientCoreTests(Action<string, Action> test)
    {
        test("e2e: клиент копит шаги до e2e-ready и шлёт их после", () =>
        {
            var (core, ctx, journal) = Client();
            var outgoing = new List<E2EStepState>();
            Check(core.Reach(ctx, "lobby", "a", DateTime.UtcNow), "шаг принят");
            Check(!core.TakeOutgoing(outgoing) && outgoing.Count == 0, "до рукопожатия ничего не уходит");
            core.AcceptReady("run-1");
            Check(core.TakeOutgoing(outgoing) && outgoing.Count == 1 && outgoing[0].Name == "lobby", "после e2e-ready уходит");
            outgoing.Clear();
            Check(core.Reach(ctx, "lobby", "a", DateTime.UtcNow) && !core.TakeOutgoing(outgoing), "повтор с тем же data — без отправки");
            core.Reach(ctx, "lobby", "b", DateTime.UtcNow);
            Check(core.TakeOutgoing(outgoing) && outgoing[0].Data == "b", "новое data уходит");
            var outcomes = string.Join(",", journal.Steps.Select(s => s.Outcome));
            Check(outcomes == "reached,updated", "журнал клиента: " + outcomes);
        });
        test("e2e: после потери рукопожатия клиент пересылает все свои шаги", () =>
        {
            var (core, ctx, _) = Client();
            var outgoing = new List<E2EStepState>();
            core.AcceptReady("run-1");
            core.Reach(ctx, "map-1", null, DateTime.UtcNow);
            core.TakeOutgoing(outgoing); outgoing.Clear();
            core.HandshakeLost();
            core.Reach(ctx, "map-2", null, DateTime.UtcNow);
            Check(!core.TakeOutgoing(outgoing), "без рукопожатия копит");
            core.AcceptReady("run-1");
            Check(core.TakeOutgoing(outgoing) && string.Join(",", outgoing.Select(s => s.Name)) == "map-1,map-2", "пересылка всех");
        });
        test("e2e: новый запуск сервера сбрасывает его прежние шаги и abort", () =>
        {
            var (core, _, journal) = Client();
            core.AcceptReady("run-1");
            core.AcceptServerStep("go", "1", DateTime.UtcNow);
            core.AcceptAbort("boom", DateTime.UtcNow);
            Check(core.TryServerStep("go", out string data) && data == "1" && core.AbortReason == "boom", "приняты");
            core.AcceptReady("run-2");
            Check(!core.TryServerStep("go", out _) && core.AbortReason == null, "сброшены");
            Check(journal.Steps.Any(s => s.Outcome == E2EStepOutcome.Received && s.Role == "server") &&
                  journal.Steps.Any(s => s.Outcome == E2EStepOutcome.Aborted), "журнал приёма");
        });
        test("e2e: лимиты шагов клиента — именованная ошибка, шаг отброшен", () =>
        {
            var (core, ctx, _) = Client();
            int before = GameLog.ErrorCount;
            Check(!core.Reach(ctx, new string('n', 65), null, DateTime.UtcNow), "длинное имя");
            Check(!core.Reach(ctx, "x", new string('d', 257), DateTime.UtcNow), "длинное data");
            for (int i = 0; i < E2EStepRules.MaxStepsPerConnection; i++)
                Check(core.Reach(ctx, "s" + i, null, DateTime.UtcNow), "шаг " + i);
            Check(!core.Reach(ctx, "one-more", null, DateTime.UtcNow), "513-й шаг");
            Check(GameLog.ErrorCount >= before + 3, "ошибки в журнале: " + (GameLog.ErrorCount - before));
        });
        test("e2e: роль клиента вне схемы — шаги не уходят, ServerStep у клиента запрещён", () =>
        {
            var (core, ctx, _) = Client("client");
            var outgoing = new List<E2EStepState>();
            core.AcceptReady("run-1");
            core.Reach(ctx, "x", null, DateTime.UtcNow);
            Check(core.OwnRoleError != null && !core.TakeOutgoing(outgoing), "не уходят");
            Throws<InvalidOperationException>(() => core.ReachServer(ctx, "go", null, DateTime.UtcNow));
            Throws<InvalidOperationException>(() => core.Reach(E2EContext.Create("smoke", "client-1"), "x", null, DateTime.UtcNow));
        });
    }

    private static (E2EBarrierCore core, E2EContext ctx, E2EResult journal) Server(int clients = 2, string late = null)
    {
        var core = new E2EBarrierCore();
        var ctx = E2EContext.Create("smoke", "server", expectedClients: clients, lateRole: late);
        var journal = new E2EResult();
        core.Configure(ctx, journal);
        return (core, ctx, journal);
    }

    private static void ServerCoreTests(Action<string, Action> test)
    {
        test("e2e: сервер дедуплицирует, хранит первое достижение и последнее data", () =>
        {
            var (core, _, journal) = Server();
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Check(core.AcceptClientStep(1, "client-1", "ready", "a", t0, 0f) == E2EStepAccept.Accepted, "первый");
            Check(core.AcceptClientStep(1, "client-1", "ready", "a", t0.AddSeconds(5), 0f) == E2EStepAccept.Duplicate, "повтор");
            Check(core.AcceptClientStep(1, "client-1", "ready", "b", t0.AddSeconds(9), 0f) == E2EStepAccept.Accepted, "новое data");
            Check(core.TryClientStep("client-1", "ready", out string data) && data == "b", "последнее data");
            Check(journal.Steps.Count == 2 && journal.Steps[0].Utc == t0 && journal.Steps[0].Outcome == "received" &&
                  journal.Steps[1].Outcome == "updated", "журнал сервера");
        });
        test("e2e: барьер сервера ждёт всех, кроме позднего, и отдаёт роль → data", () =>
        {
            var (core, ctx, _) = Server(3, "client-3");
            var regular = E2EStepRules.ExpectedClientRoles(ctx.ClientCount, ctx.LateRole, false);
            var all = E2EStepRules.ExpectedClientRoles(ctx.ClientCount, ctx.LateRole, true);
            core.AcceptClientStep(1, "client-1", "map-1", "x1", DateTime.UtcNow, 0f);
            Check(!core.ClientsReached("map-1", regular, out string failure) && failure == null, "client-2 ещё нет");
            Check(core.DescribeClients("map-1", regular, ctx.LateRole).Contains("нет client-2"), "диагноз");
            core.AcceptClientStep(2, "client-2", "map-0", "round=3", DateTime.UtcNow, 0f);
            string described = core.DescribeClients("map-1", regular, ctx.LateRole);
            Check(described.Contains("client-1 [x1]") && described.Contains("client-2 (последний шаг map-0 [round=3])"),
                  "диагноз с data: " + described);
            core.AcceptClientStep(2, "client-2", "map-1", "x2", DateTime.UtcNow, 0f);
            Check(core.ClientsReached("map-1", regular, out _), "без позднего прошли");
            Check(!core.ClientsReached("map-1", all, out _), "с поздним — ждём");
            var map = core.ClientData("map-1", regular);
            Check(map.Count == 2 && map["client-1"] == "x1" && map["client-2"] == "x2", "роль → data");
        });
        test("e2e: шаг failed ожидаемого клиента валит барьер сразу", () =>
        {
            var (core, _, journal) = Server();
            core.AcceptClientStep(1, "client-1", E2EStepRules.FailedStep, "клиент упал", DateTime.UtcNow, 0f);
            var expected = E2EStepRules.ExpectedClientRoles(2, null, false);
            Check(!core.ClientsReached("map-1", expected, out string failure) && failure.Contains("client-1 провалился"), "провал: " + failure);
            Check(journal.Steps[0].Outcome == E2EStepOutcome.Failed, "исход failed");
        });
        test("e2e: роль вне набора и смена роли — ошибка запуска, барьер падает", () =>
        {
            var (core, _, _) = Server();
            Check(core.AcceptClientStep(1, "client-3", "x", null, DateTime.UtcNow, 0f) == E2EStepAccept.Rejected, "client-3 из 2");
            core.AcceptClientStep(2, "client-1", "x", null, DateTime.UtcNow, 0f);
            Check(core.AcceptClientStep(2, "client-2", "x", null, DateTime.UtcNow, 0f) == E2EStepAccept.Rejected, "смена роли");
            Check(core.ProtocolErrors.Count == 2, "ошибок " + core.ProtocolErrors.Count);
            Check(!core.ClientsReached("x", new List<string> { "client-1" }, out string failure) && failure.StartsWith("ошибка запуска"), "барьер: " + failure);
        });
        test("e2e: одна роль — одно живое соединение; переподключение освобождает роль", () =>
        {
            var (core, _, _) = Server();
            core.AcceptClientStep(1, "client-1", "a", null, DateTime.UtcNow, 0f);
            Check(core.AcceptClientStep(2, "client-1", "a", null, DateTime.UtcNow, 1f) == E2EStepAccept.Conflict, "дубль — конфликт");
            core.Tick(5f);
            Check(core.ProtocolErrors.Count == 0, "в пределах отсрочки — не ошибка");
            var rehandshake = new List<int>();
            core.ConnectionGone(1, rehandshake);
            Check(rehandshake.Count == 1 && rehandshake[0] == 2, "ждавшему — повторный e2e-ready");
            Check(core.AcceptClientStep(2, "client-1", "a", null, DateTime.UtcNow, 6f) == E2EStepAccept.Duplicate, "роль перешла новому соединению");
        });
        test("e2e: дубль роли дольше отсрочки — именованная ошибка запуска", () =>
        {
            var (core, _, _) = Server();
            core.AcceptClientStep(1, "client-2", "a", null, DateTime.UtcNow, 0f);
            core.AcceptClientStep(2, "client-2", "a", null, DateTime.UtcNow, 0f);
            core.Tick(E2EBarrierCore.RoleConflictGraceSeconds + 1f);
            core.Tick(E2EBarrierCore.RoleConflictGraceSeconds + 2f);
            Check(core.ProtocolErrors.Count == 1 && core.ProtocolErrors[0].Contains("два живых соединения"), "ошибка: " + string.Join(";", core.ProtocolErrors));
        });
        test("e2e: не больше 512 шагов на соединение и лимит сообщений", () =>
        {
            var (core, _, _) = Server();
            for (int i = 0; i < E2EStepRules.MaxStepsPerConnection; i++)
                Check(core.AcceptClientStep(1, "client-1", "s" + i, null, DateTime.UtcNow, 0f) == E2EStepAccept.Accepted, "шаг " + i);
            Check(core.AcceptClientStep(1, "client-1", "extra", null, DateTime.UtcNow, 0f) == E2EStepAccept.Rejected, "513-й");
            Check(core.AcceptClientStep(1, "client-1", "s0", "new", DateTime.UtcNow, 0f) == E2EStepAccept.Accepted, "обновление прежнего");
            Check(core.AcceptClientStep(1, "client-1", new string('n', 65), null, DateTime.UtcNow, 0f) == E2EStepAccept.Rejected, "длинное имя");
            var (flood, _, _) = Server();
            for (int i = 0; i < E2EStepRules.MaxMessagesPerConnection; i++)
                flood.AcceptClientStep(7, "client-2", "same", null, DateTime.UtcNow, 0f);
            Check(flood.AcceptClientStep(7, "client-2", "same", "x", DateTime.UtcNow, 0f) == E2EStepAccept.Rejected, "лимит сообщений");
        });
        test("e2e: шаги и abort сервера — журнал и рассылка, Step сервера только в журнал", () =>
        {
            var (core, ctx, journal) = Server();
            var broadcast = new List<E2EStepState>();
            Check(core.ReachServer(ctx, "series-start", "TestMap1", DateTime.UtcNow), "шаг сервера");
            Check(core.Reach(ctx, "local-note", null, DateTime.UtcNow), "Step сервера");
            core.TakeServerBroadcast(broadcast);
            Check(broadcast.Count == 1 && broadcast[0].Name == "series-start" && core.ServerSteps.Count == 1, "рассылается только ServerStep");
            core.Abort("barrier map-1: нет client-2", DateTime.UtcNow);
            core.Abort("второй", DateTime.UtcNow);
            Check(core.AbortReason.StartsWith("barrier map-1") && journal.Steps.Count(s => s.Outcome == E2EStepOutcome.Aborted) == 1, "abort один");
            Check(core.ServerRun != null && core.ServerRun.Length == 12, "id запуска");
            string first = core.ServerRun;
            core.Configure(ctx, journal);
            Check(core.ServerRun != first && core.ServerSteps.Count == 0 && core.AbortReason == null, "новый запуск — чистые таблицы");
        });
    }

    private static void WaitTests(Action<string, Action> test)
    {
        test("e2e: исключение условия — именованный провал ожидания, не обрыв прогона", () =>
        {
            int before = GameLog.ErrorCount;
            var outcome = new E2EWaitOutcome();
            var stepper = new E2EScenarioStepper(E2EWait.Until(outcome, "карта загружена", 60f, () => throw new InvalidOperationException("нет сцены")));
            while (stepper.MoveNext(out _)) { }
            Check(!outcome.Succeeded && outcome.Faulted && outcome.Aborted && outcome.Diagnosis.Contains("карта загружена") &&
                  outcome.Diagnosis.Contains("InvalidOperationException") && GameLog.ErrorCount == before + 1, "провал: " + outcome.Diagnosis);
        });
        test("e2e: исключение abortIf — тоже именованный провал", () =>
        {
            var outcome = new E2EWaitOutcome();
            var stepper = new E2EScenarioStepper(E2EWait.Until(outcome, "x", 60f, () => false, abortIf: () => throw new Exception("abort упал")));
            while (stepper.MoveNext(out _)) { }
            Check(outcome.Faulted && outcome.AbortReason.Contains("abortIf"), "abortIf: " + outcome.AbortReason);
        });
        test("e2e: исполнитель получает управление каждый кадр внутри длинного ожидания", () =>
        {
            UnityEngine.Time.realtimeSinceStartup = 100f;
            var outcome = new E2EWaitOutcome();
            var stepper = new E2EScenarioStepper(Scenario(outcome));
            Check(stepper.MoveNext(out object yielded) && yielded == null && stepper.Depth == 2, "кадр внутри ожидания");
            Check(E2EWait.CurrentWait != null && E2EWait.CurrentWait.Contains("долгое"), "текущее ожидание видно: " + E2EWait.CurrentWait);
            UnityEngine.Time.realtimeSinceStartup = 150f;
            Check(stepper.MoveNext(out _) && stepper.Depth == 2, "ещё ждёт");
            stepper.Dispose();
            Check(E2EWait.CurrentWait == null && Disposed, "обрыв закрывает итераторы и их finally");
        });
        test("e2e: исключение во вложенном итераторе и провал барьера доходят до исполнителя", () =>
        {
            var stepper = new E2EScenarioStepper(Nested(() => throw new E2EScenarioFailure("barrier map-1", "дошли никто; нет client-1")));
            try { while (stepper.MoveNext(out _)) { } throw new Exception("провал не дошёл"); }
            catch (E2EScenarioFailure failure) { Check(failure.Check == "barrier map-1" && failure.Detail.Contains("client-1"), "провал"); }
            var plain = new E2EScenarioStepper(Nested(() => throw new FormatException("bad")));
            Throws<FormatException>(() => { while (plain.MoveNext(out _)) { } });
        });
        test("e2e: вложенные итераторы прокручиваются, прочее уходит Unity как есть", () =>
        {
            var marker = new object();
            var stepper = new E2EScenarioStepper(Yields(marker));
            var seen = new List<object>();
            while (stepper.MoveNext(out object y)) seen.Add(y);
            Check(seen.Count == 2 && seen[0] == marker && seen[1] == null, "отдано Unity: " + seen.Count);
        });
    }

    private static bool Disposed;

    private static IEnumerator Scenario(E2EWaitOutcome outcome)
    {
        Disposed = false;
        try { yield return E2EWait.Until(outcome, "долгое", 300f, () => false); }
        finally { Disposed = true; }
    }

    private static IEnumerator Nested(Action fail)
    {
        yield return Inner(fail);
    }

    private static IEnumerator Inner(Action fail)
    {
        yield return null;
        fail();
    }

    private static IEnumerator Yields(object marker)
    {
        yield return Child(marker);
        yield return null;
    }

    private static IEnumerator Child(object marker)
    {
        yield return marker;
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("ожидалось " + typeof(T).Name);
    }

    private static void Check(bool condition, string what)
    {
        if (!condition) throw new Exception("нарушено: " + what);
    }
}
