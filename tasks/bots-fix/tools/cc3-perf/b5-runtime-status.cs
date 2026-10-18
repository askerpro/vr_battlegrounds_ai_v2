// B5: только чтение, компактный статус ячейки cc-3. Ничего не пишет и не завершает.
const string Root = "tasks/bots-fix/reports/cc3-perf";
const string Attempt = "attempt-1";
const string Cell = "C8"; // H8 | C8 | H10 | C10 | RH | S8
const string Owner = "bots-fix.cc3-perf";
string runRoot = Root + "/runtime/" + Attempt + "/" + Cell;
var status = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
object watch = null;
string watchPath = runRoot + "/watch.json";
if (System.IO.File.Exists(watchPath)) {
    try {
        var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(watchPath));
        var transitions = json["transitions"] as Newtonsoft.Json.Linq.JArray;
        watch = new {
            state = (string)json["state"], completed = (bool?)json["completed"], reason = (string)json["finishReason"],
            completionKind = (string)json["completionKind"], elapsed = (double?)json["elapsed"],
            maxRound = (int?)json["maxRound"], scoredRounds = json["scoredRounds"], observedBeganRounds = json["observedBeganRounds"],
            fourRoundsScored = (bool?)json["fourRoundsScored"], naturalEndProof = (bool?)json["naturalEndProof"],
            gcRecorderValid = (bool?)json["gcRecorderValid"], markerValid = json["markerValid"],
            stopIntent = (bool?)json["stopIntent"], runtimeErrors = (int?)json["runtimeErrors"],
            transitionsDropped = (int?)json["transitionsDropped"], updatedUtc = (string)json["updatedUtc"],
            recent = transitions == null ? new object[0] : transitions.Skip(Math.Max(0, transitions.Count - 3)).Cast<object>().ToArray()
        };
    } catch (Exception e) { watch = new { error = e.Message }; }
}
string probeRoot = runRoot + "/probe";
var probes = !System.IO.Directory.Exists(probeRoot) ? new object[0] : System.IO.Directory.GetFiles(probeRoot, "*.json").Select(path => {
    try {
        var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
        var match = json["match"];
        var frame = json["frame"];
        return (object)new {
            path, participant = (string)json["participant"], final = (bool?)json["final"],
            finishReason = (string)json["finishReason"], updatedUtc = (string)json["updatedUtc"],
            console = json["console"],
            gcRecorderValid = frame != null ? (bool?)frame["gcRecorderValid"] : null,
            markerValid = frame != null ? frame["markerValid"] : null,
            round = match != null ? (int?)match["round"] : null,
            phase = match != null ? (string)match["phase"] : null,
            timelineDropped = match != null ? (int?)match["timelineDropped"] : null
        };
    } catch (Exception error) { return (object)new { path, error = error.Message }; }
}).ToArray();
var referee = EditorApplication.isPlaying ? VrBattlegrounds.Managers.MapReferee.Instance : null;
var mode = referee != null ? referee.ActiveGameMode as VrBattlegrounds.GameModes.EliminationMode : null;
return new {
    executionCompleted = true, readOnly = true, cell = Cell,
    editor = new { playing = EditorApplication.isPlaying, willChange = EditorApplication.isPlayingOrWillChangePlaymode, compiling = EditorApplication.isCompiling },
    launch = new { status.RunId, status.Owner, status.State, status.Ready, status.Playing, status.CleanupPassed, status.Error, status.LogArchiveDirectory },
    seed = new { status.RandomSeedApplied, status.AppliedRandomSeed, status.RandomSeedAppliedBeforeBots, status.RandomSeedAppliedScene },
    watchPath, watch, watcherPresent = AppDomain.CurrentDomain.GetData(Owner + ".watch." + Attempt + "." + Cell) != null,
    probeRoot, probes, probeActive = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.Active != null,
    probeRequest = System.IO.File.Exists(VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.RequestPath),
    live = new { mapState = referee != null ? referee.CurrentState.ToString() : null, round = mode != null ? mode.CurrentRoundNumber : -1,
        phase = mode != null ? mode.CurrentRoundPhase.ToString() : null },
    intents = new { stop = System.IO.File.Exists(runRoot + "/admin-stop-intent.json"),
        finish = System.IO.File.Exists(runRoot + "/finish-intent.json"), cancel = System.IO.File.Exists(runRoot + "/cancel-intent.json") }
};
