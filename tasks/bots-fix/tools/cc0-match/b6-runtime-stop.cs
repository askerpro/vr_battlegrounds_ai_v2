// B6 MUTATION: выход из Play своего запуска (если сторож B4 ещё не остановил) и уборка заявки пробы.
// Порядок: финальный отчёт пробы главного процесса → PlayLaunch.Cancel(runId, свой owner) (клоны пишут финальные отчёты при
// выходе) → удаление заявки. Чужой Play (другой owner) не останавливается. Идемпотентен: при Idle только убирает заявку
// и пишет stop.json (с LogArchives).
// НЕ ПОВТОРЯТЬ после таймаута: сначала B5 (editor.playing=false, launch.State=Idle, CleanupPassed=true).
const string Root = "tasks/bots-fix/reports/cc0-match";
const string Run = "M1";          // M1 | M2 | M3 | M8 | M10 | S8 (клиенты Spectator) | S8P (клиенты Player)
const string Owner = "bots-fix.cc0-match";
const string Attempt = "attempt-1";
string RunRoot = Root + "/runtime/" + Attempt + "/" + Run;
var status = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
System.IO.Directory.CreateDirectory(RunRoot);
if (status.State == "Idle" && !EditorApplication.isPlaying) {
    bool cleared = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.ClearRequest();
    System.IO.File.WriteAllText(RunRoot + "/stop.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
        stopRequestedUtc = DateTime.UtcNow.ToString("o"), alreadyStopped = true, requestCleared = cleared, status.CleanupPassed,
        status.LogArchiveDirectory, logArchives = status.LogArchives }, Newtonsoft.Json.Formatting.Indented));
    return new { executionCompleted = true, alreadyStopped = true, probeRequestCleared = cleared, status.CleanupPassed, status.LogArchiveDirectory };
}
if (status.Owner != Owner) throw new InvalidOperationException("Запуск принадлежит " + status.Owner + " — не останавливать чужой Play.");
string probeReport = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.FinishActive("b6");
var after = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Cancel(status.RunId, Owner));
bool requestCleared = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.ClearRequest();
System.IO.File.WriteAllText(RunRoot + "/stop.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
    stopRequestedUtc = DateTime.UtcNow.ToString("o"), probeReport, runId = status.RunId, stateAfter = after.State, requestCleared },
    Newtonsoft.Json.Formatting.Indented));
return new { executionCompleted = true, stopRequested = true, probeReport, stateAfter = after.State, requestCleared,
    next = "B5 до editor.playing=false, launch.state=Idle, CleanupPassed=true; затем B6 ещё раз (запишет LogArchives) или finish." };
