// B5 READ-ONLY статус раунда III наведения (структура прогона B). Повтор безопасен (ничего не пишет, ничего не меняет).
// Используется для ожидания Play/warmup/очереди и для проверки durable state после таймаута любого шага.
const string Root = "tasks/bots-fix/reports/aim-round3";
string launchState = null, launchError = null; bool? launchReady = null, launchPlaying = null;
Dictionary<string, object> stand = null;
string standError = null;
try {
    var launch = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(
        VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
    launchState = launch.State; launchError = launch.Error; launchReady = launch.Ready; launchPlaying = launch.Playing;
} catch (Exception error) { launchError = "status: " + error.Message; }
try { stand = VrBattlegrounds.EditorTools.BotCombatStandEditor.Status(); }
catch (Exception error) { standError = error.Message; }
object queue = null;
object currentRun = null;
var queuePath = Root + "/runtime/queue.json";
if (System.IO.File.Exists(queuePath)) {
    try {
        var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(queuePath));
        var combos = json["combos"] as Newtonsoft.Json.Linq.JArray;
        queue = new {
            state = (string)json["state"], error = (string)json["error"], currentCombo = (string)json["currentCombo"],
            currentRunId = (string)json["currentRunId"], liveStage = (string)json["liveStage"], updatedUtc = (string)json["updatedUtc"],
            samplerErrors = json["samplerErrors"] is Newtonsoft.Json.Linq.JArray ? ((Newtonsoft.Json.Linq.JArray)json["samplerErrors"]).Count : 0,
            combos = combos == null ? new object[0] : combos.Select(c => (object)new {
                combo = (string)c["combo"], runId = (string)c["runId"],
                passed = c["status"] != null ? (bool?)c["status"]["Passed"] : null,
                completed = c["status"] != null ? (int?)c["status"]["Completed"] : null,
                failed = c["status"] != null ? (int?)c["status"]["Failed"] : null,
                needsReview = c["status"] != null ? (int?)c["status"]["NeedsReview"] : null,
                invalidFixture = c["status"] != null ? (int?)c["status"]["InvalidFixture"] : null,
                watchdog = c["watchdogCancelUtc"] != null }).ToArray() };
        var runId = (string)json["currentRunId"];
        if (!string.IsNullOrEmpty(runId) && EditorApplication.isPlaying) {
            var status = VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.GetStatus(runId);
            if (status != null) currentRun = new { status.RunId, status.CurrentCase, status.Completed, status.Total, status.IsFinished, status.Error };
        }
    } catch (Exception error) { queue = "queue.json unreadable: " + error.Message; }
}
bool playing = EditorApplication.isPlaying;
return new {
    executionCompleted = true, readOnly = true,
    editor = new { playing, willChange = EditorApplication.isPlayingOrWillChangePlaymode, compiling = EditorApplication.isCompiling },
    launch = new { state = launchState, ready = launchReady, playing = launchPlaying, error = launchError },
    stand, standError,
    liveStage = playing ? VrBattlegrounds.Bots.BotSenses.Stage().ToString() : null,
    bots = playing && VrBattlegrounds.Bots.BotDirector.Instance != null ? VrBattlegrounds.Bots.BotDirector.Instance.Bots.Count : 0,
    queueMarker = AppDomain.CurrentDomain.GetData("bots-fix.aim-round3.queue") != null,
    queue, currentRun,
    files = new {
        preflight = System.IO.Directory.Exists(Root) ? System.IO.Directory.GetFiles(Root, "preflight-*.json").Length : 0,
        authoringResult = System.IO.File.Exists(Root + "/result.json"),
        identityA = System.IO.File.Exists(Root + "/identity-a.json"),
        identityPostAuthoring = System.IO.File.Exists(Root + "/identity-post-authoring.json"),
        launchMarker = System.IO.File.Exists(Root + "/runtime/launch.json"),
        identityPostPlay = System.IO.File.Exists(Root + "/identity-post-play.json") }
};
