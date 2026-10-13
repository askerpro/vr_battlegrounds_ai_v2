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
        LogCaptureTests(test);
    }

    // Вердикт по логам (quest-device-run r4): захват в процессе и новые поля JSON.
    private static void LogCaptureTests(Action<string, Action> test)
    {
        test("лог: Log и Warning не попадают в logIssues и не аллоцируют", () =>
        {
            var capture = new E2ELogCapture();
            capture.Handle("info", "stack", UnityEngine.LogType.Log); // прогрев JIT
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                capture.Handle("info " + "x", "stack", UnityEngine.LogType.Log);
                capture.Handle("warn", "stack", UnityEngine.LogType.Warning);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (capture.Snapshot().Count != 0 || capture.Dropped != 0) throw new Exception("Log/Warning попали в захват");
            if (allocated != 0) throw new Exception("Log/Warning аллоцируют: " + allocated + " Б");
        });
        test("лог: повтор ошибки увеличивает count, типы различаются", () =>
        {
            var capture = new E2ELogCapture();
            capture.Handle("boom\nвторая строка", "Frame1\nFrame2", UnityEngine.LogType.Error);
            capture.Handle("boom\nдругая вторая строка", "Other", UnityEngine.LogType.Error);
            capture.Handle("boom", "", UnityEngine.LogType.Exception);
            capture.Handle("assert", "", UnityEngine.LogType.Assert);
            var issues = capture.Snapshot();
            if (issues.Count != 3) throw new Exception("ожидалось 3 записи, есть " + issues.Count);
            if (issues[0].Type != "Error" || issues[0].Count != 2 || issues[0].Message != "boom")
                throw new Exception("повтор не агрегирован: " + issues[0].Type + "/" + issues[0].Count + "/" + issues[0].Message);
            if (issues[0].Stack != "вторая строка\nFrame1\nFrame2") throw new Exception("стек первого появления: " + issues[0].Stack);
            if (issues[1].Type != "Exception" || issues[2].Type != "Assert") throw new Exception("типы потеряны");
        });
        test("лог: идентификаторы в первой строке не дробят запись", () =>
        {
            var capture = new E2ELogCapture();
            capture.Handle("Id is eeec2ab6-1c6c-d19e-2c9e-1e550eb0d3bf. netId=91 Spine01", "", UnityEngine.LogType.Error);
            capture.Handle("Id is 7ae781cf-765a-5fea-988a-74cbcd5ccdad. netId=174 Spine01", "", UnityEngine.LogType.Error);
            capture.Handle("Id is 7ae781cf-765a-5fea-988a-74cbcd5ccdad. netId=174 Spine02", "", UnityEngine.LogType.Error);
            var issues = capture.Snapshot();
            if (issues.Count != 2 || issues[0].Count != 2) throw new Exception("ожидалось 2 записи (2+1), есть " + issues.Count);
            if (!issues[0].Message.Contains("eeec2ab6")) throw new Exception("исходный текст первого появления потерян");
        });
        test("лог: стек обрезается до 5 строк", () =>
        {
            var capture = new E2ELogCapture();
            capture.Handle("deep", "f1\nf2\nf3\nf4\nf5\nf6\nf7", UnityEngine.LogType.Error);
            if (capture.Snapshot()[0].Stack.Split('\n').Length != E2ELogCapture.StackLines) throw new Exception("стек не обрезан");
        });
        test("лог: лимит уникальных записей и dropped", () =>
        {
            var capture = new E2ELogCapture(3);
            // Разные тексты: числа в ключе нормализуются, поэтому «err 0..4» были бы одной записью.
            string[] names = { "alpha", "beta", "gamma", "delta", "omega" };
            foreach (string name in names) capture.Handle("err " + name, "", UnityEngine.LogType.Error);
            capture.Handle("err alpha", "", UnityEngine.LogType.Error);
            var issues = capture.Snapshot();
            if (issues.Count != 3 || capture.Dropped != 2 || issues[0].Count != 2) throw new Exception("лимит: " + issues.Count + "/" + capture.Dropped);
        });
        test("лог: подписка ставится заглушкой и снимается финальной записью", () =>
        {
            string path = Path.Combine(Path.GetTempPath(), "vrbg-harness-log-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                int baseline = UnityEngine.Application.LogSubscriberCount;
                var result = new E2EResult { Status = E2EResult.StatusStarted };
                result.WriteTo(path);
                result.WriteTo(path); // повторная заглушка — без второй подписки
                if (UnityEngine.Application.LogSubscriberCount != baseline + 1 || !result.LogCapture.IsInstalled) throw new Exception("подписка не поставлена один раз");
                UnityEngine.Application.RaiseLog("during", "", UnityEngine.LogType.Error);
                result.Status = E2EResult.StatusCompleted;
                result.WriteTo(path);
                UnityEngine.Application.RaiseLog("after", "", UnityEngine.LogType.Error);
                if (UnityEngine.Application.LogSubscriberCount != baseline || result.LogCapture.IsInstalled) throw new Exception("подписка не снята");
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var issues = document.RootElement.GetProperty("logIssues");
                if (issues.GetArrayLength() != 1 || issues[0].GetProperty("message").GetString() != "during") throw new Exception("в файле не тот захват");

                // Второй результат в процессе вытесняет подписку первого.
                var first = new E2EResult { Status = E2EResult.StatusStarted };
                var second = new E2EResult { Status = E2EResult.StatusStarted };
                first.WriteTo(null); second.WriteTo(null);
                if (first.LogCapture.IsInstalled || !second.LogCapture.IsInstalled || UnityEngine.Application.LogSubscriberCount != baseline + 1)
                    throw new Exception("второй захват не вытеснил первый");
                second.Status = E2EResult.StatusError; second.WriteTo(null);
                if (UnityEngine.Application.LogSubscriberCount != baseline) throw new Exception("подписка второго не снята");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        });
        test("лог: JSON содержит logIssues, logIssuesDropped и knownNoise", () =>
        {
            var result = new E2EResult { Status = E2EResult.StatusCompleted };
            result.Declare("first"); result.Set("first", true, "done");
            result.LogCapture.Handle("Ошибка \"в кавычках\"", "at Foo.Bar()", UnityEngine.LogType.Exception);
            if (!result.DeclareKnownNoise("shader-null", @"ArgumentNullException: .*\n.*shader", "наблюдалось в headless")) throw new Exception("шум не принят");
            if (result.DeclareKnownNoise("bad", "([", "неверный regex")) throw new Exception("неверный regex принят");
            if (result.DeclareKnownNoise("", "x", "пустой id")) throw new Exception("пустой id принят");
            result.DeclareKnownNoise("shader-null", "shader", "замена по id");
            Check(result, true);
            using var document = JsonDocument.Parse(result.ToJson());
            var root = document.RootElement;
            var issue = root.GetProperty("logIssues")[0];
            if (issue.GetProperty("type").GetString() != "Exception" || issue.GetProperty("message").GetString() != "Ошибка \"в кавычках\"" ||
                issue.GetProperty("stack").GetString() != "at Foo.Bar()" || issue.GetProperty("count").GetInt32() != 1 ||
                issue.GetProperty("firstAtSeconds").ValueKind != JsonValueKind.Number)
                throw new Exception("запись logIssues неверна");
            if (root.GetProperty("logIssuesDropped").GetInt32() != 0) throw new Exception("logIssuesDropped неверен");
            var noise = root.GetProperty("knownNoise");
            if (noise.GetArrayLength() != 1 || noise[0].GetProperty("id").GetString() != "shader-null" ||
                noise[0].GetProperty("regex").GetString() != "shader" || noise[0].GetProperty("reason").GetString() != "замена по id")
                throw new Exception("knownNoise неверен");
            if (root.GetProperty("checks").GetArrayLength() != 1) throw new Exception("прежние поля сломаны");
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
