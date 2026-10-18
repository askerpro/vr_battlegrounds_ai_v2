// Read-only: после неизвестного исхода B3/B4/B6 — inspect, не replay.
const string Root = "tasks/bots-fix/reports/cc1-visual-consolidated-20261018-r1";
const string Marker = "bots-fix.cc1-visual-consolidated-20261018-r1";
var stand = VrBattlegrounds.EditorTools.BotCombatStandEditor.Status();
var launch = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
var receipt = System.IO.File.Exists(Root + "/launch-identity.json") ? Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(Root + "/launch-identity.json")) : null;
string expectedLaunch = receipt != null ? (string)receipt["launchRunId"] : null;
bool exactLaunch = !string.IsNullOrEmpty(expectedLaunch) && launch.RunId == expectedLaunch && (string)receipt["root"] == Root && (string)receipt["owner"] == "BotCombatStand";
bool terminal = exactLaunch && !EditorApplication.isPlayingOrWillChangePlaymode && launch.State == "Idle" && launch.CleanupPassed;
object durable = null;
string runId = null;
if (System.IO.File.Exists(Root + "/visual-state.json")) {
    var data = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(Root + "/visual-state.json"));
    if ((string)data["launchRunId"] == expectedLaunch) runId = (string)data["runId"];
    var captures = data["captures"] as Newtonsoft.Json.Linq.JArray;
    durable = new { state = (string)data["state"], runId = (string)data["runId"], detached = (bool?)data["detached"],
        pendingAnalysis = (int?)data["pendingAnalysis"], captureCount = captures != null ? captures.Count : 0,
        error = (string)data["error"], sampleError = (string)data["sampleError"], ownerDecision = (string)data["ownerDecision"] };
}
else if (!System.IO.File.Exists(Root + "/visual-start-intent.json") && exactLaunch && (string)stand["launchId"] == expectedLaunch) runId = (string)stand["runId"];
object currentRun = null;
if (exactLaunch && !string.IsNullOrEmpty(runId) && EditorApplication.isPlaying) {
    var status = VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.GetStatus(runId);
    if (status != null) currentRun = new { status.RunId, status.IsFinished, status.ReportPath, status.Error };
}
var memory = AppDomain.CurrentDomain.GetData(Marker) as Dictionary<string, object>;
return new { executionCompleted = true, readOnly = true, playing = EditorApplication.isPlaying,
    launch, expectedLaunch, exactLaunch, terminal, currentRun,
    ownStandMatches = exactLaunch && (bool)stand["owned"] && (string)stand["launchId"] == expectedLaunch && launch.Owner == "BotCombatStand",
    stand, durable, marker = memory != null,
    intents = new { cancel = System.IO.File.Exists(Root + "/stand-cancel-intent.json"), stop = System.IO.File.Exists(Root + "/stop-intent.json"), restore = System.IO.File.Exists(Root + "/restore-intent.json") },
    captureCameras = UnityEngine.Resources.FindObjectsOfTypeAll<Camera>().Count(c => c != null && c.name == "BotStandCaptureCamera"),
    ownFitPending = memory != null ? memory["pendingAnalysis"] : null };
