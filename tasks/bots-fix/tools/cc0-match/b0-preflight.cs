// B0 READ-ONLY preflight пакета cc0-match (clip-cutover cc-0: база матча гибрида, BotStandMatchProbe).
// Повтор безопасен: ничего не меняет в Unity, пишет только новый preflight-<ticks>.json под output root.
// PlayLaunch.Plan только вычисляет план (Play не запускает) — проверяются все прогоны: M1/M3/M8/M10 (ПК-хост), S8 (server + 2 клиента Spectator) и S8P (server + 2 клиента Player).
const string Root = "tasks/bots-fix/reports/cc0-match";
const string Attempt = "attempt-1";
const string ScenePath = "Assets/Scenes/Maps/TestMap1.unity";
var blockers = new List<string>();
var notes = new List<string>();
if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
    blockers.Add("Editor не в idle EditMode (playing/compiling/updating).");
if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null) blockers.Add("Открыт prefab stage.");
var dirtyScenes = new List<string>();
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) {
    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
    if (scene.isDirty) dirtyScenes.Add(scene.path);
}
if (dirtyScenes.Count > 0) blockers.Add("Есть несохранённые сцены: " + string.Join(", ", dirtyScenes.ToArray()));
// Вход пакета: проба матча (прямая ссылка — payload не скомпилируется без неё).
var probeType = typeof(VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbe);
string requestPath = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.RequestPath;
if (System.IO.File.Exists(requestPath)) notes.Add("Есть заявка пробы от прошлого запуска: " + requestPath + " (B3 перепишет; B6 удаляет).");
VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverRequest observerRequest;
string observerReason;
if (VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverGate.TryReadRequest(out observerRequest, out observerReason))
    blockers.Add("Действующая заявка BotStandNetObserver (" + observerRequest.RunTag + ") — наблюдатель стенда включился бы в клонах.");
else notes.Add("Заявка наблюдателя стенда: " + observerReason);
var runs = new[] {
    new { id = "M1", role = "host", clients = 0, bots = 1 }, new { id = "M2", role = "host", clients = 0, bots = 2 }, new { id = "M3", role = "host", clients = 0, bots = 3 },
    new { id = "M8", role = "host", clients = 0, bots = 8 }, new { id = "M10", role = "host", clients = 0, bots = 10 },
    new { id = "S8", role = "server", clients = 2, bots = 8 }, new { id = "S8P", role = "server", clients = 2, bots = 8 } };
var plans = new Dictionary<string, object>();
string launchState = null;
try {
    var launch = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
    launchState = launch.State;
    if (launch.State != "Idle") blockers.Add("PlayLaunch не Idle: " + launch.State + " " + launch.Error);
    foreach (var run in runs) {
        bool spectators = run.id == "S8";
        int humans = (run.role == "host" ? 1 : 0) + (spectators ? 0 : run.clients);
        var configuration = new VrBattlegrounds.DevTools.PlayLaunchConfiguration {
            Enabled = true, Role = run.role, ClientCount = run.clients, SceneSource = "scene", ScenePath = ScenePath,
            ModeId = "elimination", AutoGoLive = true, MinPlayers = humans + run.bots, BotCount = run.bots,
            PauseOnFocusLoss = false, HostIsAdmin = false, Readiness = spectators ? "connected" : "map-playable",
            GameRole = spectators ? "Spectator" : "Player", NetworkPortPolicy = "auto", TimeoutSeconds = 180 };
        var plan = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.ResolvedPlayPlan>(
            VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Plan(JsonUtility.ToJson(configuration)));
        plans[run.id] = new { plan.Passed, plan.Code, plan.Message, plan.SceneKind, plan.StartScenePath, plan.TargetScenePath, plan.PlanHash };
        if (!plan.Passed) blockers.Add(run.id + ": PlayLaunch.Plan отказал: " + plan.Code + " " + plan.Message);
        else if (plan.SceneKind != "Game") blockers.Add(run.id + ": SceneKind=" + plan.SceneKind + " (нужен Game).");
    }
} catch (Exception error) { blockers.Add("PlayLaunch preflight: " + error.Message); }
// Карта и режим: TestMap1 первая в реестре (её берёт и BotMatchStarter), maxPlayers 10.
var mapData = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Maps.MapData>("Assets/Data/Maps/MapData_TestMap1.asset");
if (mapData == null) blockers.Add("Нет MapData_TestMap1.");
else notes.Add("TestMap1: maxPlayers=" + mapData.maxPlayers);
var vp = System.IO.Path.Combine(VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverGate.ProjectRoot, "Library/VP");
notes.Add("Клоны MPPM: " + (System.IO.Directory.Exists(vp) ? string.Join(", ", System.IO.Directory.GetDirectories(vp).Select(System.IO.Path.GetFileName).ToArray()) : "папки Library/VP нет (создаст MPPM)"));
System.IO.Directory.CreateDirectory(Root + "/" + Attempt);
var reportPath = Root + "/" + Attempt + "/preflight-" + DateTime.UtcNow.Ticks + ".json";
System.IO.File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
    executionCompleted = true, passed = blockers.Count == 0, blockers, notes, dirtyScenes, plans, launchState,
    probe = new { type = probeType.FullName, requestPath },
    activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
    unity = Application.unityVersion, scope = "Read-only preflight; no assets/scenes/play state changed."
}, Newtonsoft.Json.Formatting.Indented));
return new { executionCompleted = true, passed = blockers.Count == 0, blockers = blockers.Take(10).ToArray(), notes = notes.ToArray(),
    plans, launchState, reportPath };
