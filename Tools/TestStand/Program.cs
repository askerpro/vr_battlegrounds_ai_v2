#if STAND_PROTOCOL_PRESENT
using VrBattlegrounds.EditorTools.TestStand;
#endif

// Проверки инфраструктуры адресации. Unity и сетевые копии здесь не имитируются.
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
#if !STAND_PROTOCOL_PRESENT
        Console.WriteLine("FAIL: адресный исполнитель отсутствует; корректная команда не может быть выполнена");
        return 1;
#else
        Test("корректная цель исполняется и подтверждает свой адрес", () =>
        {
            int calls = 0;
            var gate = Gate();
            var reply = gate.Execute(Request(), r => { calls++; return StandReply.Ok("marker-created", "probe", 1); });
            Check(reply.Passed && calls == 1 && reply.RunId == "run" && reply.ParticipantId == "client-1" && reply.ProcessSessionId == "session");
        });
        foreach (string field in new[] { "run", "participant", "session" })
            Test("неверный адрес " + field + " не исполняется", () =>
            {
                int calls = 0;
                var request = Request();
                if (field == "run") request.RunId = "old";
                if (field == "participant") request.ParticipantId = "other";
                if (field == "session") request.ProcessSessionId = "old";
                var reply = Gate().Execute(request, r => { calls++; return StandReply.Ok("bad"); });
                Check(!reply.Passed && calls == 0 && reply.Code == "wrong-" + field);
            });
        Test("повтор RequestId возвращает прежний результат без эффекта", () =>
        {
            int calls = 0;
            var gate = Gate();
            var first = gate.Execute(Request(), r => { calls++; return StandReply.Ok("created", "probe", calls); });
            var second = gate.Execute(Request(), r => { calls++; return StandReply.Ok("bad"); });
            Check(calls == 1 && second.Passed && second.Code == first.Code && second.MarkerCount == 1);
        });
        Test("тот же RequestId с другим payload отклоняется", () =>
        {
            int calls = 0;
            var gate = Gate();
            gate.Execute(Request(), r => { calls++; return StandReply.Ok("created"); });
            var changed = Request(); changed.MarkerName = "other";
            var reply = gate.Execute(changed, r => { calls++; return StandReply.Ok("bad"); });
            Check(calls == 1 && !reply.Passed && reply.Code == "request-conflict");
        });
        Test("отозванная область отклоняет новый и старый запрос", () =>
        {
            var gate = Gate();
            gate.Execute(Request(), r => StandReply.Ok("created"));
            gate.Revoke();
            int calls = 0;
            var old = gate.Execute(Request(), r => { calls++; return StandReply.Ok("bad"); });
            var request = Request(); request.RequestId = "new";
            var fresh = gate.Execute(request, r => { calls++; return StandReply.Ok("bad"); });
            Check(calls == 0 && old.Code == "inactive" && fresh.Code == "inactive");
        });
        Test("неполный запрос отклоняется", () => Check(Gate().Execute(null, r => StandReply.Ok("bad")).Code == "invalid-request"));
        Test("невалидное имя маркера отклоняется", () =>
        {
            var request = Request(); request.MarkerName = "a|b";
            Check(Gate().Execute(request, r => StandReply.Ok("bad")).Code == "invalid-marker");
        });
        Test("лимит не вытесняет выполненные RequestId", () =>
        {
            var gate = Gate(1); int calls = 0;
            gate.Execute(Request(), r => { calls++; return StandReply.Ok("created"); });
            var next = Request(); next.RequestId = "next";
            var denied = gate.Execute(next, r => { calls++; return StandReply.Ok("bad"); });
            var duplicate = gate.Execute(Request(), r => { calls++; return StandReply.Ok("bad"); });
            Check(calls == 1 && denied.Code == "capacity" && duplicate.Passed);
        });
        Test("ошибка исполнителя не вызывает повтор эффекта", () =>
        {
            var gate = Gate(); int calls = 0;
            var first = gate.Execute(Request(), r => { calls++; throw new InvalidOperationException("after-effect"); });
            var duplicate = gate.Execute(Request(), r => { calls++; return StandReply.Ok("bad"); });
            Check(calls == 1 && first.Code == "execution-error" && duplicate.Code == first.Code);
        });
        Test("невалидный бюджет отклоняется", () =>
        {
            var request = Request(); request.TimeBudgetMs = 0;
            Check(Gate().Execute(request, r => StandReply.Ok("bad")).Code == "invalid-budget");
        });
        ProfileTests.Run(Test);
        ResultTests.Run(Test);
        Test("превышение бюджета не повторяет частичный эффект", () =>
        {
            var gate = Gate(); int calls = 0;
            var request = Request(); request.TimeBudgetMs = 1;
            var first = gate.Execute(request, r => { calls++; System.Threading.Thread.Sleep(30); return StandReply.Ok("done"); });
            var duplicate = gate.Execute(request, r => { calls++; return StandReply.Ok("bad"); });
            Check(calls == 1 && !first.Passed && first.Code == "execution-timeout" && duplicate.Code == first.Code);
        });
        Console.WriteLine($"passed={_passed} failed={_failed}");
        return _failed == 0 ? 0 : 1;
#endif
    }

#if STAND_PROTOCOL_PRESENT
    private static StandRequestGate Gate(int capacity = 256) => new StandRequestGate("run", "client-1", "session", capacity);
    private static StandRequest Request() => new StandRequest { RunId = "run", ParticipantId = "client-1", ProcessSessionId = "session", RequestId = "request-1", Action = "create-marker", MarkerName = "probe", TimeBudgetMs = 5000 };
#endif
    private static void Test(string name, Action action)
    {
        try { action(); _passed++; Console.WriteLine("PASS: " + name); }
        catch (Exception e) { _failed++; Console.WriteLine("FAIL: " + name + ": " + e.Message); }
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("нарушено ожидание протокола"); }
}
