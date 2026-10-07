using System.Text.Json;
using VrBattlegrounds.DevTools.E2E;

internal static class ResultTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("пустой завершённый результат не зелёный", () =>
            Check(new E2EResult { Status = E2EResult.StatusCompleted }, false));
        test("непроверенная часть сценария не зелёная", () =>
        {
            var result = new E2EResult { Status = E2EResult.StatusCompleted };
            result.Declare("first", "pending"); result.Set("first", true, "done");
            Check(result, false);
        });
        test("обрыв и таймаут сохраняют отдельные статусы", () =>
        {
            foreach (string status in new[] { E2EResult.StatusError, E2EResult.StatusTimeout })
            {
                var result = new E2EResult { Status = status };
                result.Declare("first", "pending"); result.Set("first", true, "done");
                result.AbortPending(status);
                Check(result, false);
                if (!result.Checks[1].Detail.Contains(status)) throw new Exception("причина обрыва потеряна");
            }
        });
        test("полный завершённый результат зелёный", () =>
        {
            var result = new E2EResult { Status = E2EResult.StatusCompleted };
            result.Declare("first", "second"); result.Set("first", true, "done"); result.Set("second", true, "done");
            Check(result, true);
        });
    }

    private static void Check(E2EResult result, bool expected)
    {
        using var document = JsonDocument.Parse(result.ToJson());
        if (result.Passed != expected || document.RootElement.GetProperty("passed").GetBoolean() != expected ||
            document.RootElement.GetProperty("status").GetString() != result.Status)
            throw new Exception("неверный машиночитаемый вердикт");
    }
}
