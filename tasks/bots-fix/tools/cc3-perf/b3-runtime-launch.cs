// B3: immutable заявка общей пробы (WriteSeconds=10, без DirectorEquip) до Play и штатный PlayLaunch ячейки cc-3. Не повторять после timeout.
const string Root = "tasks/bots-fix/reports/cc3-perf";
const string Attempt = "attempt-1";
const string Cell = "C8"; // H8 | C8 | H10 | C10 | RH | S8
const string Owner = "bots-fix.cc3-perf";
const string ScenePath = "Assets/Scenes/Maps/TestMap1.unity";
const bool AllowSpectatorCell = false;
string runRoot = Root + "/runtime/" + Attempt + "/" + Cell;
string tag = "cc3-perf/" + Attempt + "/" + Cell;
if (Cell != "H8" && Cell != "C8" && Cell != "H10" && Cell != "C10" && Cell != "RH" && Cell != "S8")
    throw new InvalidOperationException("Неизвестная ячейка Cell: допустимы H8|C8|H10|C10|RH|S8.");
if (Cell == "S8" && !AllowSpectatorCell)
    throw new InvalidOperationException("Ячейка S8 заблокирована до закрытия gameplay/network 8657/8836.");
if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
    throw new InvalidOperationException("Нужен idle EditMode.");
if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null)
    throw new InvalidOperationException("Открыт prefab stage.");
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Есть несохранённая сцена.");
if (VrBattlegrounds.Bots.BotDirector.Instance != null && VrBattlegrounds.Bots.BotDirector.Instance.Bots.Count != 0)
    throw new InvalidOperationException("Есть чужие боты.");
if (System.IO.Directory.Exists(runRoot))
    return new { executionCompleted = true, refused = true, reason = "Каталог прогона уже есть; неизвестный исход B3 не повторять." };
string requestPath = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.RequestPath;
if (System.IO.File.Exists(requestPath))
    throw new InvalidOperationException("Есть MatchProbe request; чужую/старую заявку не перезаписывать.");
VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverRequest observer;
string observerReason;
if (VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverGate.TryReadRequest(out observer, out observerReason))
    throw new InvalidOperationException("Есть NetObserver: " + observer.RunTag);
var status = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
if (status.State != "Idle") throw new InvalidOperationException("PlayLaunch не Idle: " + status.State);
bool isServerRole = Cell == "S8" || Cell == "RH";
string role = isServerRole ? "server" : "host";
int clients = Cell == "S8" ? 2 : Cell == "RH" ? 1 : 0;
int bots = (Cell == "H10" || Cell == "C10") ? 10 : 8;
bool spectators = Cell == "S8";
int humans = (role == "host" ? 1 : 0) + (spectators ? 0 : clients);
var config = new VrBattlegrounds.DevTools.PlayLaunchConfiguration {
    Enabled = true, Role = role, ClientCount = clients, SceneSource = "scene", ScenePath = ScenePath,
    ModeId = "elimination", AutoGoLive = true, MinPlayers = humans + bots, BotCount = bots,
    PauseOnFocusLoss = false, HostIsAdmin = false, Readiness = spectators ? "connected" : "map-playable",
    GameRole = spectators ? "Spectator" : "Player", NetworkPortPolicy = "auto", TimeoutSeconds = 180
};
string configJson = JsonUtility.ToJson(config);
var plan = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.ResolvedPlayPlan>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Plan(configJson));
if (!plan.Passed || plan.SceneKind != "Game") throw new InvalidOperationException("Plan: " + plan.Code + " " + plan.Message);
string preflightRoot = Root + "/" + Attempt;
var preflights = System.IO.Directory.Exists(preflightRoot) ? System.IO.Directory.GetFiles(preflightRoot, "preflight-" + Cell + "-*.json") : new string[0];
bool admitted = preflights.Any(path => {
    var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
    return (bool?)json["passed"] == true && (string)json["owner"] == Owner && (string)json["cell"] == Cell && (string)json["planHash"] == plan.PlanHash;
});
if (!admitted) throw new InvalidOperationException("Нет PASS B0 для ячейки " + Cell + " с тем же PlanHash.");
System.IO.Directory.CreateDirectory(runRoot);
string reports = System.IO.Path.GetFullPath(runRoot + "/probe");
System.IO.Directory.CreateDirectory(reports);
var request = new VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeRequest {
    RunTag = tag, OutputDirectory = reports, ExpiresUtc = DateTime.UtcNow.AddMinutes(25).ToString("o"),
    WriteSeconds = 10f
};
Action<string, object> durable = (path, value) => {
    byte[] bytes = new System.Text.UTF8Encoding(false).GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n"));
    using (var stream = new System.IO.FileStream(path, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.Read)) {
        stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
    }
};
// Durable marker создаётся до WriteRequest/Play. Даже при неизвестном исходе этот B3 более не запускается.
durable(runRoot + "/launch.json", new {
    requestedUtc = DateTime.UtcNow.ToString("o"), owner = Owner, cell = Cell, attempt = Attempt,
    role, clients, bots, humans, spectators, configuration = config, planHash = plan.PlanHash,
    reportsDirectory = reports, requestPath, request, additionalTrace = "OFF",
    seed = "PENDING_8548_OR_NOT_CONTROLLED"
});
VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.WriteRequest(request);
durable(runRoot + "/probe-request-evidence.json", request);
System.IO.File.WriteAllText(runRoot + "/probe-request-immutable.json", System.IO.File.ReadAllText(requestPath), new System.Text.UTF8Encoding(false));
var launch = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(
    VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Play(configJson, Owner, plan.PlanHash));
durable(runRoot + "/launch-status.json", launch);
return new { executionCompleted = true, started = true, cell = Cell, runId = launch.RunId, state = launch.State, error = launch.Error,
    requestPath, reports, next = "B5 до Ready+probeActive; затем B4 один раз. Дополнительные трассировщики (B7/NetObserver) НЕ включать." };
