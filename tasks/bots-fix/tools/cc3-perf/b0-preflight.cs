// B0: только Plan/чтение Unity и новый отчёт preflight для ячейки cc-3 (H8|C8|H10|C10|RH|S8); без запуска и изменений ассетов. Payload C# 6.
const string Root = "tasks/bots-fix/reports/cc3-perf";
const string Attempt = "attempt-1";
const string Cell = "C8"; // H8 | C8 | H10 | C10 | RH | S8
const string Owner = "bots-fix.cc3-perf";
const int RandomSeed = 20261018; // Одно зерно для всех сравниваемых H/C/RH/S8 ячеек.
const string ScenePath = "Assets/Scenes/Maps/TestMap1.unity";
const bool AllowSpectatorCell = false; // До закрытия gameplay/network 8657/8836 ячейка S8 заблокирована.
string runRoot = Root + "/runtime/" + Attempt + "/" + Cell;
var blockers = new List<string>();
if (Cell != "H8" && Cell != "C8" && Cell != "H10" && Cell != "C10" && Cell != "RH" && Cell != "S8")
    blockers.Add("Неизвестная ячейка Cell: допустимы H8|C8|H10|C10|RH|S8.");
if (Cell == "S8" && !AllowSpectatorCell)
    blockers.Add("Ячейка S8 ожидает согласования контракта пассивного наблюдателя gameplay/network (8657/8836).");
if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
    blockers.Add("Нужен idle EditMode.");
if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null)
    blockers.Add("Открыт prefab stage.");
var dirtyScenes = new List<string>();
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) {
    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
    if (scene.isDirty) dirtyScenes.Add(scene.path);
}
if (dirtyScenes.Count != 0) blockers.Add("Есть несохранённые сцены.");
if (VrBattlegrounds.Bots.BotDirector.Instance != null && VrBattlegrounds.Bots.BotDirector.Instance.Bots.Count != 0)
    blockers.Add("Есть чужие боты.");
if (System.IO.Directory.Exists(runRoot))
    blockers.Add("Каталог прогона уже существует: новая попытка требует нового immutable пакета.");
string requestPath = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.RequestPath;
if (System.IO.File.Exists(requestPath))
    blockers.Add("Есть заявка MatchProbe: B0/B3 её не перезаписывают.");
VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverRequest observer;
string observerReason;
if (VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverGate.TryReadRequest(out observer, out observerReason))
    blockers.Add("Есть действующая заявка NetObserver: " + observer.RunTag);
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
    GameRole = spectators ? "Spectator" : "Player", NetworkPortPolicy = "auto", TimeoutSeconds = 180,
    UseRandomSeed = true, RandomSeed = RandomSeed
};
object planEvidence = null;
string planHash = null;
string launchState = null;
try {
    var status = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
    launchState = status.State;
    if (status.State != "Idle") blockers.Add("PlayLaunch не Idle: " + status.State);
    var plan = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.ResolvedPlayPlan>(
        VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Plan(JsonUtility.ToJson(config)));
    planHash = plan.PlanHash;
    planEvidence = new { plan.Passed, plan.Code, plan.Message, plan.SceneKind, plan.StartScenePath, plan.TargetScenePath, plan.PlanHash };
    if (!plan.Passed || plan.SceneKind != "Game") blockers.Add("Plan отказал либо SceneKind не Game: " + plan.Code);
} catch (Exception error) { blockers.Add(error.Message); }
var probeType = typeof(VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbe);
// Проверка сигнатуры общей пробы (4 поля Request, 1 аргумент FinishActive(string)) на этапе компиляции payload.
var probeShape = new VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeRequest {
    RunTag = "cc3-perf/" + Attempt + "/" + Cell, OutputDirectory = runRoot + "/probe",
    ExpiresUtc = DateTime.UtcNow.AddMinutes(25).ToString("o"), WriteSeconds = 10f
};
Func<string, string> finishSignature = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.FinishActive;
System.IO.Directory.CreateDirectory(Root + "/" + Attempt);
string reportPath = Root + "/" + Attempt + "/preflight-" + Cell + "-" + DateTime.UtcNow.Ticks + ".json";
System.IO.File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
    executionCompleted = true, passed = blockers.Count == 0, owner = Owner, cell = Cell, attempt = Attempt,
    role, clients, bots, humans, spectators, configuration = config, planHash, plan = planEvidence, launchState,
    blockers, dirtyScenes, probeType = probeType.FullName, writeSeconds = probeShape.WriteSeconds,
    finishMethod = finishSignature.Method.Name, finishParameters = finishSignature.Method.GetParameters().Length, requestPath,
    additionalTrace = "OFF", seed = new { enabled = config.UseRandomSeed, value = config.RandomSeed, runtimeReceipt = "B5 после запуска" }
}, Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n"), new System.Text.UTF8Encoding(false));
return new { executionCompleted = true, passed = blockers.Count == 0, cell = Cell, blockers = blockers.Take(10).ToArray(), plan = planEvidence, reportPath };
