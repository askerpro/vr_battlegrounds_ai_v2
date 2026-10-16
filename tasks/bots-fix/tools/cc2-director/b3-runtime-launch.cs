// B3: immutable DirectorEquip-заявка до Play и штатный PlayLaunch server M2. Не повторять после timeout.
const string Root = "tasks/bots-fix/reports/cc2-lifecycle-round2";
const string Attempt = "attempt-1";
const string Run = "M2";
const string Owner = "bots-fix.cc2-lifecycle";
const string ScenePath = "Assets/Scenes/Maps/TestMap1.unity";
string runRoot = Root + "/runtime/" + Attempt + "/" + Run;
string tag = "cc2-lifecycle-round2/" + Attempt + "/" + Run;
if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
    throw new InvalidOperationException("Нужен idle EditMode.");
if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null) throw new InvalidOperationException("Открыт prefab stage.");
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Есть несохранённая сцена.");
if (VrBattlegrounds.Bots.BotDirector.Instance != null && VrBattlegrounds.Bots.BotDirector.Instance.Bots.Count != 0)
    throw new InvalidOperationException("Есть чужие боты.");
if (System.IO.Directory.Exists(runRoot)) return new { executionCompleted = true, refused = true, reason = "Каталог прогона уже есть; неизвестный исход B3 не повторять." };
string requestPath = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.RequestPath;
if (System.IO.File.Exists(requestPath)) throw new InvalidOperationException("Есть MatchProbe request; чужую/старую заявку не перезаписывать.");
VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverRequest observer;
string observerReason;
if (VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverGate.TryReadRequest(out observer, out observerReason))
    throw new InvalidOperationException("Есть NetObserver: " + observer.RunTag);
var status = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
if (status.State != "Idle") throw new InvalidOperationException("PlayLaunch не Idle: " + status.State);
var config = new VrBattlegrounds.DevTools.PlayLaunchConfiguration {
    Enabled = true, Role = "server", ClientCount = 0, SceneSource = "scene", ScenePath = ScenePath,
    ModeId = "elimination", AutoGoLive = true, MinPlayers = 2, BotCount = 2,
    PauseOnFocusLoss = false, HostIsAdmin = false, Readiness = "map-playable", GameRole = "Player",
    NetworkPortPolicy = "auto", TimeoutSeconds = 180
};
string configJson = JsonUtility.ToJson(config);
var plan = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.ResolvedPlayPlan>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Plan(configJson));
if (!plan.Passed || plan.SceneKind != "Game") throw new InvalidOperationException("Plan: " + plan.Code + " " + plan.Message);
string preflightRoot = Root + "/" + Attempt;
var preflights = System.IO.Directory.Exists(preflightRoot) ? System.IO.Directory.GetFiles(preflightRoot, "preflight-*.json") : new string[0];
bool admitted = preflights.Any(path => {
    var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
    return (bool?)json["passed"] == true && (string)json["owner"] == Owner && (string)json["run"] == Run && (string)json["planHash"] == plan.PlanHash;
});
if (!admitted) throw new InvalidOperationException("Нет PASS B0 с тем же PlanHash.");
System.IO.Directory.CreateDirectory(runRoot);
string reports = System.IO.Path.GetFullPath(runRoot + "/probe");
var request = new VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeRequest {
    RunTag = tag, OutputDirectory = reports, ExpiresUtc = DateTime.UtcNow.AddMinutes(25).ToString("o"),
    WriteSeconds = 5f, DirectorEquip = true
};
Action<string, object> durable = (path, value) => {
    byte[] bytes = new System.Text.UTF8Encoding(false).GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n"));
    using (var stream = new System.IO.FileStream(path, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.Read)) {
        stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
    }
};
// Marker создаётся до WriteRequest/Play. Даже при неизвестном исходе этот B3 более не запускается.
durable(runRoot + "/launch.json", new {
    requestedUtc = DateTime.UtcNow.ToString("o"), owner = Owner, run = Run, attempt = Attempt,
    configuration = config, planHash = plan.PlanHash, reportsDirectory = reports, requestPath, request,
    seed = "UNKNOWN/notControlled", scope = "FUNCTIONAL ServerOnly M2; adminStop либо exact naturalEnd различаются; один GoLive"
});
VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.WriteRequest(request);
// Точная копия штатной заявки: далее ни B4, ни Stop/GoLive её не меняют.
durable(runRoot + "/probe-request-evidence.json", request);
System.IO.File.WriteAllText(runRoot + "/probe-request-immutable.json", System.IO.File.ReadAllText(requestPath), new System.Text.UTF8Encoding(false));
var launch = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(
    VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Play(configJson, Owner, plan.PlanHash));
durable(runRoot + "/launch-status.json", launch);
return new { executionCompleted = true, started = true, run = Run, runId = launch.RunId, state = launch.State, error = launch.Error,
    requestPath, reports, next = "B5 до Ready+probeActive; затем B4 один раз. При неизвестном исходе — B5, не повтор B3." };
