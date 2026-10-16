// B6: cleanup только собственного owner/run ячейки cc-3. Использует 1-аргументный FinishActive(string) при раннем stop, затем Cancel один раз.
// Никаких force exit, прямого isPlaying=false, удаления ботов или повторов неизвестного Cancel.
const string Root = "tasks/bots-fix/reports/cc3-perf";
const string Attempt = "attempt-1";
const string Cell = "C8"; // H8 | C8 | H10 | C10 | RH | S8
const string Owner = "bots-fix.cc3-perf";
string runRoot = Root + "/runtime/" + Attempt + "/" + Cell;
if (!System.IO.File.Exists(runRoot + "/launch.json"))
    throw new InvalidOperationException("Нет своего B3 marker; не менять launch/request.");
var status = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
string expectedRunId = null;
if (System.IO.File.Exists(runRoot + "/launch-status.json"))
    expectedRunId = (string)Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(runRoot + "/launch-status.json"))["RunId"];
bool idle = status.State == "Idle" && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode;
if (!idle && (status.Owner != Owner || string.IsNullOrEmpty(expectedRunId) || status.RunId != expectedRunId))
    throw new InvalidOperationException("Чужой owner/RunId либо неизвестный исход B3: cleanup не вызывает Cancel.");
Action<string, object> durable = (path, value) => {
    byte[] bytes = new System.Text.UTF8Encoding(false).GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n"));
    using (var stream = new System.IO.FileStream(path, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.Read)) {
        stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
    }
};
bool usedWatcher = false, cancellationAlreadyAttempted = false;
if (!idle) {
    var control = AppDomain.CurrentDomain.GetData(Owner + ".watch." + Attempt + "." + Cell) as Dictionary<string, object>;
    if (control != null && control.ContainsKey("owner") && (string)control["owner"] == Owner && (string)control["runId"] == expectedRunId) {
        usedWatcher = true;
        ((Action<string, bool>)control["finish"])("b6-early-stop", false);
    } else if (System.IO.File.Exists(runRoot + "/cancel-intent.json")) {
        cancellationAlreadyAttempted = true;
    } else {
        if (!System.IO.File.Exists(runRoot + "/finish-intent.json")) {
            durable(runRoot + "/finish-intent.json", new { utc = DateTime.UtcNow.ToString("o"), runId = expectedRunId, owner = Owner, cell = Cell, reason = "b6-before-watch-or-watch-lost", completed = false });
            var active = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.Active;
            string expectedRoot = System.IO.Path.GetFullPath(runRoot + "/probe") + System.IO.Path.DirectorySeparatorChar;
            if (active != null && !System.IO.Path.GetFullPath(active.ReportPath).StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Active probe чужой: не FinishActive.");
            string path = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.FinishActive("b6-before-watch-or-watch-lost");
            durable(runRoot + "/finish-return.json", new { utc = DateTime.UtcNow.ToString("o"), probePath = path, completed = false });
        }
        durable(runRoot + "/cancel-intent.json", new { utc = DateTime.UtcNow.ToString("o"), runId = expectedRunId, owner = Owner, cell = Cell, reason = "b6" });
        var cancelled = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Cancel(expectedRunId, Owner));
        durable(runRoot + "/cancel-return.json", cancelled);
    }
}
var after = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
bool cleanupReady = after.State == "Idle" && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode;
bool requestCleared = false;
string requestPath = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.RequestPath;
string cleanupWarning = null;
if (cleanupReady && System.IO.File.Exists(requestPath)) {
    string immutable = runRoot + "/probe-request-immutable.json";
    if (System.IO.File.Exists(immutable) && System.IO.File.ReadAllText(requestPath) == System.IO.File.ReadAllText(immutable))
        requestCleared = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.ClearRequest();
    else cleanupWarning = "Заявка отличается либо immutable копии нет; чужой request не удаляется.";
}
var archivedLogs = new List<object>();
if (cleanupReady && after.CleanupPassed && !string.IsNullOrEmpty(expectedRunId)) {
    try {
        if (after.LogArchives == null || after.LogArchives.Length == 0)
            throw new InvalidOperationException("PlayLaunch не вернул полные архивы логов собственного RunId.");
        string ownLogRoot = System.IO.Path.GetFullPath("Logs/VRBattlegroundsTestStand/" + expectedRunId) + System.IO.Path.DirectorySeparatorChar;
        foreach (string path in after.LogArchives ?? new string[0]) {
            string fullPath = System.IO.Path.GetFullPath(path);
            if (!fullPath.StartsWith(ownLogRoot, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(fullPath))
                throw new InvalidOperationException("Архив вне собственного RunId либо отсутствует.");
            string copyRoot = runRoot + "/playlaunch-logs";
            System.IO.Directory.CreateDirectory(copyRoot);
            string copy = System.IO.Path.Combine(copyRoot, System.IO.Path.GetFileName(fullPath));
            using (var sha = System.Security.Cryptography.SHA256.Create()) {
                string sourceHash, copyHash;
                using (var input = System.IO.File.OpenRead(fullPath)) sourceHash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                if (!System.IO.File.Exists(copy)) System.IO.File.Copy(fullPath, copy, false);
                using (var input = System.IO.File.OpenRead(copy)) copyHash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                if (sourceHash != copyHash) throw new InvalidOperationException("SHA256 архива не совпадает; существующая копия не перезаписывается.");
                archivedLogs.Add(new { source = fullPath, path = copy, sha256 = copyHash, bytes = new System.IO.FileInfo(copy).Length });
            }
        }
    } catch (Exception error) { cleanupWarning = (cleanupWarning == null ? "" : cleanupWarning + "; ") + "log archive: " + error.Message; }
}
System.IO.File.WriteAllText(runRoot + "/stop.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
    utc = DateTime.UtcNow.ToString("o"), owner = Owner, cell = Cell, expectedRunId, usedWatcher, cancellationAlreadyAttempted,
    cleanupReady, requestCleared, cleanupWarning, after.State, after.CleanupPassed, after.Error, after.LogArchiveDirectory, after.LogArchives, archivedLogs
}, Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n"), new System.Text.UTF8Encoding(false));
return new { executionCompleted = true, cell = Cell, cleanupReady, requestCleared, cleanupWarning, cancellationAlreadyAttempted,
    state = after.State, after.CleanupPassed, after.LogArchiveDirectory,
    next = cleanupReady ? "B5 проверить Idle/CleanupPassed/final; main finish/receive." : "B5 до Idle; B6 повторять только для request cleanup, Cancel intent не переисполняется." };
