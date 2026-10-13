// B0 READ-ONLY preflight раунда net-round1 (этап pf-network: бот PlayerFlow на сервере, наблюдатели на клиентах).
// Повтор безопасен: ничего не меняет в Unity, пишет только новый preflight-<ticks>.json под output root.
// PlayLaunch.Plan только вычисляет план (Play не запускает) — проверяются обе топологии: S2C и H1C (главный = хост).
// attempt-2: клиенты — обычные игроки (GameRole=Player); зрители не получают сессию (OnSpectatorConnect не реализован).
const string Root = "tasks/bots-fix/reports/net-round1";
const string ScenePath = "Assets/Scenes/Debug/BotCombatStand.unity";
const string Attempt = "attempt-6";
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
// Вход этапа: PlayerFlow + наблюдатель (прямые ссылки — payload не скомпилируется без них).
var observerType = typeof(VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserver);
string projectRoot = VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverGate.ProjectRoot;
string requestPath = VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverGate.RequestPath;
if (System.IO.File.Exists(requestPath)) notes.Add("Есть заявка наблюдателя от прошлого запуска: " + requestPath + " (B3 перепишет; B6 удаляет).");
var playerFlowField = typeof(VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions).GetField("PlayerFlow");
var holsterField = typeof(VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions).GetField("HolsterStart");
if (playerFlowField == null || holsterField == null) blockers.Add("Нет BotStandRunOptions.PlayerFlow/HolsterStart — не вход pf-equipment.");
var plans = new Dictionary<string, object>();
string launchState = null;
Dictionary<string, object> stand = null;
try {
    stand = VrBattlegrounds.EditorTools.BotCombatStandEditor.Status();
    if ((bool)stand["owned"]) blockers.Add("Стенд (BotCombatStandEditor) владеет запуском — не наш маршрут, ждать его завершения.");
    var launch = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
    launchState = launch.State;
    if (launch.State != "Idle") blockers.Add("PlayLaunch не Idle: " + launch.State + " " + launch.Error);
    foreach (var topology in new[] { "S2C", "H1C" }) {
        // Та же конфигурация, что строит B3 (кроме портов: auto выбирается при Play).
        var configuration = new VrBattlegrounds.DevTools.PlayLaunchConfiguration {
            Enabled = true, Role = topology == "S2C" ? "server" : "host", ClientCount = topology == "S2C" ? 2 : 1,
            SceneSource = "scene", ScenePath = ScenePath, ModeId = "respawn", AutoGoLive = false, BotCount = 0,
            PauseOnFocusLoss = false, HostIsAdmin = false, Readiness = "map-playable", GameRole = "Player",
            NetworkPortPolicy = "auto" };
        var plan = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.ResolvedPlayPlan>(
            VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Plan(JsonUtility.ToJson(configuration)));
        plans[topology] = new { plan.Passed, plan.Code, plan.Message, plan.SceneKind, plan.StartScenePath, plan.TargetScenePath, plan.PlanHash };
        if (!plan.Passed) blockers.Add(topology + ": PlayLaunch.Plan отказал: " + plan.Code + " " + plan.Message);
        else if (plan.SceneKind != "Game") blockers.Add(topology + ": SceneKind=" + plan.SceneKind + " (нужен Game — сетевая сцена).");
    }
} catch (Exception error) { blockers.Add("Stand/PlayLaunch preflight: " + error.Message); }
var sceneText = System.IO.File.Exists(ScenePath) ? System.IO.File.ReadAllText(ScenePath) : "";
foreach (var weapon in new[] { "PPK", "AK105" }) {
    var path = "Assets/Data/Weapons/" + weapon + "_Weapon.asset";
    var guid = AssetDatabase.AssetPathToGUID(path);
    if (string.IsNullOrEmpty(guid) || !sceneText.Contains(guid)) blockers.Add("Стенд не ссылается на " + path);
}
// MPPM: число виртуальных игроков задаёт PlayLaunch; здесь только справка о папке клонов.
var vp = System.IO.Path.Combine(projectRoot, "Library/VP");
notes.Add("Клоны MPPM: " + (System.IO.Directory.Exists(vp) ? string.Join(", ", System.IO.Directory.GetDirectories(vp).Select(System.IO.Path.GetFileName).ToArray()) : "папки Library/VP нет (создаст MPPM)"));
System.IO.Directory.CreateDirectory(Root + "/" + Attempt);
var reportPath = Root + "/" + Attempt + "/preflight-" + DateTime.UtcNow.Ticks + ".json";
System.IO.File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
    executionCompleted = true, passed = blockers.Count == 0, blockers, notes, dirtyScenes, plans, launchState, stand,
    observer = new { type = observerType.FullName, projectRoot, requestPath },
    activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
    unity = Application.unityVersion, scope = "Read-only preflight; no assets/scenes/play state changed."
}, Newtonsoft.Json.Formatting.Indented));
return new { executionCompleted = true, passed = blockers.Count == 0, blockers = blockers.Take(10).ToArray(), notes = notes.ToArray(),
    plans, launchState, reportPath };
