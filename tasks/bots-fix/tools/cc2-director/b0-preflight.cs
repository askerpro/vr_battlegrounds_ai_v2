// B0: только Plan/чтение Unity и новый отчёт preflight; запусков и изменений ассетов нет. Payload C# 6.
const string Root = "tasks/bots-fix/reports/cc2-lifecycle-round2";
const string Attempt = "attempt-1";
const string Run = "M2";
const string Owner = "bots-fix.cc2-lifecycle";
const string ScenePath = "Assets/Scenes/Maps/TestMap1.unity";
string runRoot = Root + "/runtime/" + Attempt + "/" + Run;
var blockers = new List<string>();
if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
    blockers.Add("Нужен idle EditMode.");
if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null) blockers.Add("Открыт prefab stage.");
var dirtyScenes = new List<string>();
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) {
    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
    if (scene.isDirty) dirtyScenes.Add(scene.path);
}
if (dirtyScenes.Count != 0) blockers.Add("Есть несохранённые сцены.");
if (VrBattlegrounds.Bots.BotDirector.Instance != null && VrBattlegrounds.Bots.BotDirector.Instance.Bots.Count != 0)
    blockers.Add("Есть чужие боты.");
if (System.IO.Directory.Exists(runRoot)) blockers.Add("Каталог прогона уже существует: новая попытка требует нового immutable пакета.");
string requestPath = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.RequestPath;
if (System.IO.File.Exists(requestPath)) blockers.Add("Есть заявка MatchProbe: B0/B3 её не перезаписывают.");
VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverRequest observer;
string observerReason;
if (VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverGate.TryReadRequest(out observer, out observerReason))
    blockers.Add("Есть действующая заявка NetObserver: " + observer.RunTag);
var config = new VrBattlegrounds.DevTools.PlayLaunchConfiguration {
    Enabled = true, Role = "server", ClientCount = 0, SceneSource = "scene", ScenePath = ScenePath,
    ModeId = "elimination", AutoGoLive = true, MinPlayers = 2, BotCount = 2,
    PauseOnFocusLoss = false, HostIsAdmin = false, Readiness = "map-playable", GameRole = "Player",
    NetworkPortPolicy = "auto", TimeoutSeconds = 180
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
// Поле и overload проверяются компиляцией payload, без запуска пробы.
var probeShape = new VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeRequest { DirectorEquip = true };
System.IO.Directory.CreateDirectory(Root + "/" + Attempt);
string reportPath = Root + "/" + Attempt + "/preflight-" + DateTime.UtcNow.Ticks + ".json";
System.IO.File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
    executionCompleted = true, passed = blockers.Count == 0, owner = Owner, run = Run, attempt = Attempt,
    configuration = config, planHash, plan = planEvidence, launchState, blockers, dirtyScenes,
    probeType = probeType.FullName, directorEquip = probeShape.DirectorEquip, requestPath,
    scope = "FUNCTIONAL ServerOnly M2, humans=0; seed UNKNOWN/notControlled; не perf/Quest/естественный конец матча"
}, Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n"), new System.Text.UTF8Encoding(false));
return new { executionCompleted = true, passed = blockers.Count == 0, blockers = blockers.Take(10).ToArray(), plan = planEvidence, reportPath };
