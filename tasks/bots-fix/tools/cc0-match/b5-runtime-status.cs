// B5 READ-ONLY статус прогона cc0-match. Повтор безопасен (ничего не пишет, ничего не меняет).
const string Root = "tasks/bots-fix/reports/cc0-match";
const string Run = "M1";          // M1 | M2 | M3 | M8 | M10 | S8 (клиенты Spectator) | S8P (клиенты Player)
const string Attempt = "attempt-1";
string RunRoot = Root + "/runtime/" + Attempt + "/" + Run;
object launch = null;
string launchError = null;
try {
    var status = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
    launch = new { status.RunId, status.Owner, status.State, status.Ready, status.Playing, status.CleanupPassed, status.Error,
        status.LogArchiveDirectory,
        participants = (status.Participants ?? new VrBattlegrounds.EditorTools.TestStand.StandParticipantState[0]).Select(p => (object)new {
            p.ParticipantId, p.ProcessId, p.NetworkRole, p.Connected, p.Scene, p.MapPlayable, p.MapRunValid, p.GameRole, p.ServerSessions,
            age = Math.Round(p.LastSeenAgeSeconds, 1), p.StartupError }).ToArray() };
} catch (Exception error) { launchError = error.Message; }
object watch = null;
if (System.IO.File.Exists(RunRoot + "/watch.json")) {
    try {
        var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(RunRoot + "/watch.json"));
        var transitions = json["transitions"] as Newtonsoft.Json.Linq.JArray;
        watch = new { state = (string)json["state"], stopReason = (string)json["stopReason"], mapState = (string)json["mapState"],
            round = (int?)json["round"], phase = (string)json["phase"], bots = (int?)json["bots"], liveSeconds = (double?)json["liveSeconds"],
            goLiveCalled = (bool?)json["goLiveCalled"], updatedUtc = (string)json["updatedUtc"],
            lastTransitions = transitions == null ? new string[0] : transitions.Skip(Math.Max(0, transitions.Count - 4)).Select(t => (string)t).ToArray() };
    } catch (Exception error) { watch = "watch.json unreadable: " + error.Message; }
}
var probeDir = RunRoot + "/probe";
var probes = !System.IO.Directory.Exists(probeDir) ? new object[0] : System.IO.Directory.GetFiles(probeDir, "*.json").Select(path => {
    try {
        var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
        var match = json["match"];
        var console = json["console"];
        var client = json["clientNetwork"];
        var sessions = match != null ? match["sessions"] as Newtonsoft.Json.Linq.JArray : null;
        var timeline = match != null ? match["timeline"] as Newtonsoft.Json.Linq.JArray : null;
        return (object)new { file = System.IO.Path.GetFileName(path), updatedUtc = (string)json["updatedUtc"], final = (bool?)json["final"],
            errors = console != null ? (int?)console["errors"] : null, exceptions = console != null ? (int?)console["exceptions"] : null,
            relayErrors = console != null ? (int?)console["relayErrors"] : null,
            round = match != null ? (int?)match["round"] : null, phase = match != null ? (string)match["phase"] : null,
            bots = match != null ? (int?)match["bots"] : null, shotsBots = match != null ? (int?)match["shotsBots"] : null,
            kills = match != null ? (int?)match["kills"] : null, timeline = timeline != null ? (int?)timeline.Count : null,
            humansInModeTeam = sessions == null ? (int?)null : sessions.Count(s => (bool?)s["bot"] == false && (bool?)s["inModeTeam"] == true),
            clientInBytes = client != null ? (long?)client["inBytesTotal"] : null };
    } catch (Exception error) { return (object)new { file = System.IO.Path.GetFileName(path), error = error.Message }; }
}).ToArray();
bool playing = EditorApplication.isPlaying;
var referee = playing ? VrBattlegrounds.Managers.MapReferee.Instance : null;
var mode = referee != null ? referee.ActiveGameMode as VrBattlegrounds.GameModes.EliminationMode : null;
return new {
    executionCompleted = true, readOnly = true,
    editor = new { playing, willChange = EditorApplication.isPlayingOrWillChangePlaymode, compiling = EditorApplication.isCompiling },
    launch, launchError, watch,
    live = referee == null ? null : new { mapState = referee.CurrentState.ToString(), round = mode != null ? mode.CurrentRoundNumber : -1,
        phase = mode != null ? mode.CurrentRoundPhase.ToString() : null,
        bots = VrBattlegrounds.Bots.BotDirector.Instance != null ? VrBattlegrounds.Bots.BotDirector.Instance.Bots.Count : 0 },
    watchMarker = AppDomain.CurrentDomain.GetData("bots-fix.cc0-match.watch." + Attempt + "." + Run) != null,
    probeRequest = System.IO.File.Exists(VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.RequestPath),
    probeActive = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.Active != null,
    probes, launchMarker = System.IO.File.Exists(RunRoot + "/launch.json")
};
