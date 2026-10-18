// Каждый Cancel/Stop/Restore имеет CreateNew+Flush intent. Существующий intent не разрешает replay.
const string Root = "tasks/bots-fix/reports/cc1-visual-consolidated-20261018-r1";
const string Marker = "bots-fix.cc1-visual-consolidated-20261018-r1";
if (!System.IO.File.Exists(Root + "/launch-identity.json"))
    return new { executionCompleted = true, inspectOnly = true, reason = "Нет exact launch receipt; ownership не выводится из stand. Нужен durable inspect." };
var receipt = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(Root + "/launch-identity.json"));
string expectedLaunch = (string)receipt["launchRunId"];
if (string.IsNullOrEmpty(expectedLaunch) || (string)receipt["root"] != Root || (string)receipt["owner"] != "BotCombatStand")
    return new { executionCompleted = true, inspectOnly = true, reason = "Launch receipt не соответствует пакету." };
var stand = VrBattlegrounds.EditorTools.BotCombatStandEditor.Status();
var launch = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
bool sameLaunch = launch.RunId == expectedLaunch;
bool terminal = sameLaunch && !EditorApplication.isPlayingOrWillChangePlaymode && launch.State == "Idle" && launch.CleanupPassed;
bool ownStand = (bool)stand["owned"] && (string)stand["launchId"] == expectedLaunch;
if (!terminal && (!sameLaunch || launch.Owner != "BotCombatStand" || !ownStand))
    return new { executionCompleted = true, inspectOnly = true, reason = "Нет exact launch+owner+stand match; чужой Play не трогать." };
Action<string, object> intent = (path, value) => {
    var bytes = System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(value));
    using (var file = new System.IO.FileStream(path, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.Read)) {
        file.Write(bytes, 0, bytes.Length); file.Flush(true);
    }
};
var cleanup = AppDomain.CurrentDomain.GetData(Marker + ".detach") as Action;
if (terminal) {
    // После явно подтверждённого terminal допустима только идемпотентная собственная cleanup.
    if (cleanup != null) cleanup();
    if (ownStand) {
        if (System.IO.File.Exists(Root + "/restore-intent.json"))
            return new { executionCompleted = true, inspectOnly = true, terminal, reason = "Restore intent уже есть: не replay; B5." };
        intent(Root + "/restore-intent.json", new { expectedLaunch, utc = DateTime.UtcNow.ToString("o") });
        VrBattlegrounds.EditorTools.BotCombatStandEditor.Restore();
    }
    return new { executionCompleted = true, terminal = true, ownCleanupOnly = true, next = "B5 owned=false; затем finish/receive." };
}
if (System.IO.File.Exists(Root + "/stop-intent.json"))
    return new { executionCompleted = true, inspectOnly = true, reason = "Stop intent уже есть: только B5/durable state до terminal, не повторять Stop." };
var data = System.IO.File.Exists(Root + "/visual-state.json") ? Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(Root + "/visual-state.json")) : null;
bool visualAttempt = System.IO.File.Exists(Root + "/visual-start-intent.json") || data != null;
string runId = visualAttempt ? (data != null ? (string)data["runId"] : null) : (string)stand["runId"];
string phase = visualAttempt ? "G" : "warmup";
if (string.IsNullOrEmpty(runId))
    return new { executionCompleted = true, inspectOnly = true, reason = "Точный " + phase + " runId неизвестен: inspect, не выводить из другого stand run." };
if (visualAttempt && ((string)data["launchRunId"] != expectedLaunch))
    return new { executionCompleted = true, inspectOnly = true, reason = "Visual run принадлежит другому launch." };
var status = VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.GetStatus(runId);
string expectedReport = Root + (visualAttempt ? "/runtime/G-ghost-respawn-ak105/summary.json" : "/warmup/summary.json");
if (status == null || string.IsNullOrEmpty(status.ReportPath) || !string.Equals(System.IO.Path.GetFullPath(status.ReportPath), System.IO.Path.GetFullPath(expectedReport), StringComparison.OrdinalIgnoreCase))
    return new { executionCompleted = true, inspectOnly = true, reason = "Exact runId/report root не подтверждены; только inspect." };
if (!status.IsFinished) {
    string cancelPath = Root + "/stand-cancel-intent.json";
    if (System.IO.File.Exists(cancelPath))
        return new { executionCompleted = true, inspectOnly = true, reason = "Cancel intent уже есть: B5 до terminal; не replay." };
    intent(cancelPath, new { runId, launchRunId = expectedLaunch, phase, reason = "own B6", utc = DateTime.UtcNow.ToString("o") });
    VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.Cancel(runId);
    return new { executionCompleted = true, cancelRequested = true, next = "B5 до exact run IsFinished; затем B6 продолжит Stop без повторного Cancel." };
}
if (cleanup != null) cleanup();
intent(Root + "/stop-intent.json", new { runId, launchRunId = expectedLaunch, phase, utc = DateTime.UtcNow.ToString("o") });
VrBattlegrounds.EditorTools.BotCombatStandEditor.StopOwnRun();
return new { executionCompleted = true, stopRequested = true, next = "B5 до exact launch Idle+CleanupPassed, playing=false/owned=false; затем finish." };
