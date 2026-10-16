// B5: только чтение, компактный статус. Ничего не пишет и не завершает.
const string Root = "tasks/bots-fix/reports/cc2-lifecycle-round2";
const string Attempt = "attempt-1";
const string Run = "M2";
const string Owner = "bots-fix.cc2-lifecycle";
string runRoot = Root + "/runtime/" + Attempt + "/" + Run;
var status = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
object watch = null;
string watchPath = runRoot + "/watch.json";
if (System.IO.File.Exists(watchPath)) {
    try {
        var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(watchPath));
        var transitions = json["transitions"] as Newtonsoft.Json.Linq.JArray;
        watch = new {
            state = (string)json["state"], completed = (bool?)json["completed"], reason = (string)json["finishReason"],
            elapsed = (double?)json["elapsed"], firstMaxRound = (int?)json["firstMaxRound"], secondMaxRound = (int?)json["secondMaxRound"],
            firstEpoch = (ulong?)json["firstEpoch"], warmupEpoch = (ulong?)json["warmupEpoch"], secondEpoch = (ulong?)json["secondEpoch"],
            firstNaturalProof = (bool?)json["firstNaturalProof"], sameMindProof = (bool?)json["sameMindProof"],
            firstCompletionKind = (string)json["firstCompletionKind"], naturalEndProof = (bool?)json["naturalEndProof"],
            naturalEndEvidence = json["naturalEndEvidence"],
            firstBaseProof = (bool?)json["firstBaseProof"], firstScoredRounds = json["firstScoredRounds"],
            firstObservedBeganRounds = json["firstObservedBeganRounds"], firstRoundWonEvents = (int?)json["firstRoundWonEvents"],
            firstFinishObserved = (bool?)json["firstFinishObserved"], firstFinishLatch = json["firstFinishLatch"], firstFinishError = (string)json["firstFinishError"],
            firstMinds = json["firstMinds"], secondMinds = json["secondMinds"], sessionIds = json["sessionIds"],
            stopIntent = (bool?)json["stopIntent"], goLiveIntent = (bool?)json["goLiveIntent"],
            runtimeErrors = (int?)json["runtimeErrors"], transitionsDropped = (int?)json["transitionsDropped"],
            updatedUtc = (string)json["updatedUtc"], probePath = (string)json["probePath"],
            recent = transitions == null ? new object[0] : transitions.Skip(Math.Max(0, transitions.Count - 3)).Cast<object>().ToArray()
        };
    } catch (Exception e) { watch = new { error = e.Message }; }
}
string probeRoot = runRoot + "/probe";
var probes = !System.IO.Directory.Exists(probeRoot) ? new object[0] : System.IO.Directory.GetFiles(probeRoot, "*.json").Select(path => {
    try {
        var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
        var journal = json["directorEquip"];
        var events = journal != null ? journal["events"] as Newtonsoft.Json.Linq.JArray : null;
        var inventory = journal != null ? journal["inventory"] as Newtonsoft.Json.Linq.JArray : null;
        var match = json["match"];
        return (object)new {
            path, final = (bool?)json["final"], updatedUtc = (string)json["updatedUtc"], console = json["console"],
            directorEquipEnabled = (bool?)json["directorEquipEnabled"],
            completed = journal != null ? (bool?)journal["completed"] : null,
            incomplete = journal != null ? (bool?)journal["incomplete"] : null,
            droppedEvents = journal != null ? (int?)journal["droppedEvents"] : null,
            droppedInventory = journal != null ? (int?)journal["droppedInventory"] : null,
            errors = journal != null ? (int?)journal["errors"] : null,
            events = events != null ? events.Count : 0, inventory = inventory != null ? inventory.Count : 0,
            round = match != null ? (int?)match["round"] : null, phase = match != null ? (string)match["phase"] : null,
            timelineDropped = match != null ? (int?)match["timelineDropped"] : null,
            eventKinds = events == null ? new object[0] : events.GroupBy(e => (string)e["kind"]).Select(g => (object)new { kind = g.Key, count = g.Count() }).ToArray()
        };
    } catch (Exception error) { return (object)new { path, error = error.Message }; }
}).ToArray();
var referee = EditorApplication.isPlaying ? VrBattlegrounds.Managers.MapReferee.Instance : null;
var mode = referee != null ? referee.ActiveGameMode as VrBattlegrounds.GameModes.EliminationMode : null;
var authority = EditorApplication.isPlaying ? VrBattlegrounds.Maps.Runtime.MapRunAuthority.Instance : null;
var snapshot = authority != null ? authority.Current : default(VrBattlegrounds.Maps.Runtime.MapRunSnapshot);
return new {
    executionCompleted = true, readOnly = true,
    editor = new { playing = EditorApplication.isPlaying, willChange = EditorApplication.isPlayingOrWillChangePlaymode, compiling = EditorApplication.isCompiling },
    launch = new { status.RunId, status.Owner, status.State, status.Ready, status.Playing, status.CleanupPassed, status.Error, status.LogArchiveDirectory },
    watchPath, watch, watcherPresent = AppDomain.CurrentDomain.GetData(Owner + ".watch." + Attempt + "." + Run) != null,
    probeRoot, probes, probeActive = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.Active != null,
    probeRequest = System.IO.File.Exists(VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.RequestPath),
    live = new { mapState = referee != null ? referee.CurrentState.ToString() : null, round = mode != null ? mode.CurrentRoundNumber : -1,
        phase = mode != null ? mode.CurrentRoundPhase.ToString() : null, ready = snapshot.IsReady, key = snapshot.Key.ToString(), epoch = snapshot.ModeEpoch },
    intents = new { stop = System.IO.File.Exists(runRoot + "/admin-stop-intent.json"), goLive = System.IO.File.Exists(runRoot + "/admin-golive-intent.json"),
        finish = System.IO.File.Exists(runRoot + "/finish-intent.json"), cancel = System.IO.File.Exists(runRoot + "/cancel-intent.json") }
};
