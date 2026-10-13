// B6 MUTATION: выход из Play своего запуска: отмена текущего RunCases (прерванный case стенд пишет InvalidFixture),
// затем PlayLaunch.Cancel(runId, свой owner) — останавливает все процессы (клоны пишут final-отчёт наблюдателя при выходе).
// Заявка наблюдателя удаляется после Cancel. Чужой Play (другой owner) не останавливается.
// НЕ ПОВТОРЯТЬ после таймаута: сначала B5 (editor.playing=false, launch.State=Idle, CleanupPassed=true).
const string Root = "tasks/bots-fix/reports/net-round1";
const string Topology = "S2C";   // S2C | H1C
const string Phase = "p1";       // p1 | p2
const string Owner = "bots-fix.net-round1";
const string Attempt = "attempt-9"; // новая папка попытки: прежние runtime/<topology>-<phase> не мешают B3
const string Variant = "A1";     // attempt-6 A/B: A1 — ClientNoRecoil=false, A2 — ClientNoRecoil=true; "" — без варианта
string RunRoot = Root + "/runtime/" + Attempt + "/" + Topology + "-" + Phase + (Variant.Length > 0 ? "-" + Variant : "");
var status = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
if (status.State == "Idle" && !EditorApplication.isPlaying) {
    bool cleared = VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverGate.ClearRequest();
    return new { executionCompleted = true, alreadyStopped = true, observerRequestCleared = cleared, status.CleanupPassed };
}
if (status.Owner != Owner) throw new InvalidOperationException("Запуск принадлежит " + status.Owner + " — не останавливать чужой Play.");
string queueState = null, cancelledRun = null;
var queuePath = RunRoot + "/queue.json";
if (System.IO.File.Exists(queuePath) && EditorApplication.isPlaying) {
    var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(queuePath));
    queueState = (string)json["state"];
    var runId = (string)json["currentRunId"];
    if ((queueState == "running" || queueState == "golive") && !string.IsNullOrEmpty(runId)) {
        VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.Cancel(runId);
        cancelledRun = runId;
    }
}
var after = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Cancel(status.RunId, Owner));
bool requestCleared = VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverGate.ClearRequest();
System.IO.File.WriteAllText(RunRoot + "/stop.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
    stopRequestedUtc = DateTime.UtcNow.ToString("o"), queueStateAtStop = queueState, cancelledRun, runId = status.RunId, stateAfter = after.State,
    requestCleared }, Newtonsoft.Json.Formatting.Indented));
return new { executionCompleted = true, stopRequested = true, queueStateAtStop = queueState, cancelledRun, stateAfter = after.State, requestCleared,
    next = "B5 до editor.playing=false, launch.state=Idle, CleanupPassed=true; затем finish/receive и копирование отчётов." };
