// B5 READ-ONLY статус раунда net-round1. Повтор безопасен (ничего не пишет, ничего не меняет).
// Для ожидания Ready/очереди и проверки durable state после таймаута любого шага. Topology/Phase — как в B3/B4.
const string Root = "tasks/bots-fix/reports/net-round1";
const string Topology = "S2C";   // S2C | H1C
const string Phase = "p1";       // p1 | p2
const string Attempt = "attempt-6"; // новая папка попытки: прежние runtime/<topology>-<phase> не мешают B3
const string Variant = "A1";     // attempt-6 A/B: A1 — ClientNoRecoil=false, A2 — ClientNoRecoil=true; "" — без варианта
string RunRoot = Root + "/runtime/" + Attempt + "/" + Topology + "-" + Phase + (Variant.Length > 0 ? "-" + Variant : "");
object launch = null;
string launchError = null;
try {
    var status = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
    launch = new { status.RunId, status.Owner, status.State, status.Ready, status.Playing, status.CleanupPassed, status.Error,
        participants = (status.Participants ?? new VrBattlegrounds.EditorTools.TestStand.StandParticipantState[0]).Select(p => (object)new {
            p.ParticipantId, p.ProcessId, p.NetworkRole, p.Server, p.Client, p.Connected, p.ConnectionEpoch, p.Scene, p.MapPlayable,
            p.MapRunValid, p.GameRole, p.ServerSessions, age = Math.Round(p.LastSeenAgeSeconds, 1), p.StartupError }).ToArray() };
} catch (Exception error) { launchError = error.Message; }
object queue = null;
var queuePath = RunRoot + "/queue.json";
if (System.IO.File.Exists(queuePath)) {
    try {
        var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(queuePath));
        var combos = json["combos"] as Newtonsoft.Json.Linq.JArray;
        var standCommands = json["stand"] as Newtonsoft.Json.Linq.JArray;
        queue = new {
            state = (string)json["state"], error = (string)json["error"], step = (string)json["step"], currentRunId = (string)json["currentRunId"],
            liveStage = (string)json["liveStage"], updatedUtc = (string)json["updatedUtc"],
            combos = combos == null ? new object[0] : combos.Select(c => (object)new {
                combo = (string)c["combo"], runId = (string)c["runId"], botToken = (string)c["botToken"],
                passed = c["status"] != null ? (bool?)c["status"]["Passed"] : null,
                completed = c["status"] != null ? (int?)c["status"]["Completed"] : null,
                invalidFixture = c["status"] != null ? (int?)c["status"]["InvalidFixture"] : null,
                watchdog = c["watchdogCancelUtc"] != null }).ToArray(),
            standCommands = standCommands == null ? new object[0] : standCommands.Select(c => (object)new {
                action = (string)c["action"], combo = (string)c["combo"], requestId = (string)c["requestId"], error = (string)c["error"],
                code = c["reply"] != null ? (string)c["reply"]["code"] : null, passed = c["reply"] != null ? (bool?)c["reply"]["passed"] : null }).ToArray() };
    } catch (Exception error) { queue = "queue.json unreadable: " + error.Message; }
}
// Отчёты наблюдателей клиентов: имя файла, время обновления, число тел, final.
var clientsDir = RunRoot + "/clients";
var clients = !System.IO.Directory.Exists(clientsDir) ? new object[0] : System.IO.Directory.GetFiles(clientsDir, "*.json").Select(path => {
    try {
        var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
        return (object)new { file = System.IO.Path.GetFileName(path), participant = (string)json["participant"], updatedUtc = (string)json["updatedUtc"],
            final = (bool?)json["final"], epoch = (int?)json["role"]?["epoch"], bodies = (json["bodies"] as Newtonsoft.Json.Linq.JArray)?.Count,
            errors = (int?)json["console"]?["errors"] };
    } catch (Exception error) { return (object)new { file = System.IO.Path.GetFileName(path), error = error.Message }; }
}).ToArray();
bool playing = EditorApplication.isPlaying;
return new {
    executionCompleted = true, readOnly = true,
    editor = new { playing, willChange = EditorApplication.isPlayingOrWillChangePlaymode, compiling = EditorApplication.isCompiling },
    launch, launchError,
    liveStage = playing && Mirror.NetworkServer.active ? VrBattlegrounds.Bots.BotSenses.Stage().ToString() : null,
    bots = playing && VrBattlegrounds.Bots.BotDirector.Instance != null ? VrBattlegrounds.Bots.BotDirector.Instance.Bots.Count : 0,
    queueMarker = AppDomain.CurrentDomain.GetData("bots-fix.net-round1.queue." + Attempt + "." + Topology + "." + Phase + "." + Variant) != null,
    queue, clients,
    observerRequest = System.IO.File.Exists(VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverGate.RequestPath),
    files = new {
        launchMarker = System.IO.File.Exists(RunRoot + "/launch.json"),
        summaries = System.IO.Directory.Exists(RunRoot) ? System.IO.Directory.GetFiles(RunRoot, "summary.json", System.IO.SearchOption.AllDirectories).Length : 0 }
};
