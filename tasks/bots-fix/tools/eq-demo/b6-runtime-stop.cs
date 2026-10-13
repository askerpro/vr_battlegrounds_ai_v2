// B6 MUTATION: выход из Play через собственный маршрут стенда (не manage_editor stop).
// Если очередь ещё идёт — сначала штатный Cancel текущего RunCases (стенд пишет прерванный case как InvalidFixture),
// затем BotCombatStandEditor.StopOwnRun() -> PlayLaunch.Cancel. Restore() вызывает сам стенд при EnteredEditMode.
// НЕ ПОВТОРЯТЬ после таймаута: сначала B5 (editor.playing=false и stand.owned=false -> уже остановлено).
const string Root = "tasks/bots-fix/reports/eq-demo";
if (!EditorApplication.isPlaying)
    return new { executionCompleted = true, alreadyStopped = true,
        standOwned = VrBattlegrounds.EditorTools.BotCombatStandEditor.Status()["owned"],
        hint = "Если standOwned=true вне Play: вызвать BotCombatStandEditor.Restore() отдельным execute_code." };
var stand = VrBattlegrounds.EditorTools.BotCombatStandEditor.Status();
if (!(bool)stand["owned"]) throw new InvalidOperationException("Play не принадлежит стенду — не останавливать чужой Play.");
string queueState = null, cancelledRun = null;
const string Phase = "p1";
var queuePath = Root + "/runtime/" + Phase + "/queue.json";
if (System.IO.File.Exists(queuePath)) {
    var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(queuePath));
    queueState = (string)json["state"];
    var runId = (string)json["currentRunId"];
    if (queueState == "running" && !string.IsNullOrEmpty(runId)) {
        VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.Cancel(runId);
        cancelledRun = runId;
    }
}
VrBattlegrounds.EditorTools.BotCombatStandEditor.StopOwnRun();
return new { executionCompleted = true, stopRequested = true, queueStateAtStop = queueState, cancelledRun,
    next = "B5 до editor.playing=false, launch.state=Idle, stand.owned=false; затем finish." };
