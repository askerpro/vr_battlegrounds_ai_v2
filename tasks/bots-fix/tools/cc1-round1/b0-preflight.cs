// B0 READ-ONLY preflight раунда cc1-round1 (подэтап cc-1 clip-cutover: player-flow без гибрида, минимальный Blaze-прокси).
// Повтор безопасен: ничего не меняет в Unity, пишет только новый preflight-<ticks>.json под output root.
// Контроллер прокси здесь только отмечается (до меню генератора его нет); проверка ассета — B1.
const string Root = "tasks/bots-fix/reports/cc1-round1";
const string ScenePath = "Assets/Scenes/Debug/BotCombatStand.unity";
var blockers = new List<string>();
var notes = new List<string>();
if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
    blockers.Add("Editor не в idle EditMode (playing/compiling/updating).");
if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null)
    blockers.Add("Открыт prefab stage.");
var dirtyScenes = new List<string>();
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) {
    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
    if (scene.isDirty) dirtyScenes.Add(scene.path);
}
if (dirtyScenes.Count > 0) blockers.Add("Есть несохранённые сцены: " + string.Join(", ", dirtyScenes.ToArray()));
// Вход cc-1: поля стенда PlayerFlow нет, BotKit есть, AddBot() без режима (прямые ссылки — payload не скомпилируется без них).
var optionsType = typeof(VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions);
bool playerFlowFieldGone = optionsType.GetField("PlayerFlow") == null;
var kitType = typeof(VrBattlegrounds.Bots.BotKit);
var addBot = typeof(VrBattlegrounds.Bots.BotDirector).GetMethod("AddBot", Type.EmptyTypes);
bool botBodyModeGone = typeof(VrBattlegrounds.Bots.BotDirector).Assembly.GetType("VrBattlegrounds.Bots.BotBodyMode") == null;
bool combatAssetsGone = typeof(VrBattlegrounds.Bots.BotDirector).Assembly.GetType("VrBattlegrounds.Bots.BotCombatAssets") == null;
if (!playerFlowFieldGone || addBot == null || !botBodyModeGone || !combatAssetsGone)
    blockers.Add("Загруженные сборки — не вход cc-1 (PlayerFlow/BotBodyMode/BotCombatAssets ещё есть или нет AddBot()).");
string controllerPath = "Assets/Resources/" + VrBattlegrounds.Bots.BotCombatDriver.ProxyControllerResource + ".controller";
bool controllerExists = System.IO.File.Exists(controllerPath);
notes.Add(controllerExists ? "Контроллер прокси уже есть: " + controllerPath : "Контроллера прокси нет (ожидаемо до меню генератора): " + controllerPath);
Dictionary<string, object> stand = null;
string launchState = null;
try {
    stand = VrBattlegrounds.EditorTools.BotCombatStandEditor.Status();
    if ((bool)stand["owned"]) blockers.Add("Стенд уже владеет запуском (owned=true).");
    var launch = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(
        VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
    launchState = launch.State;
    if (launch.State != "Idle") blockers.Add("PlayLaunch не Idle: " + launch.State + " " + launch.Error);
    var configuration = new VrBattlegrounds.DevTools.PlayLaunchConfiguration {
        Role = "server", SceneSource = "scene", ScenePath = ScenePath,
        ModeId = "respawn", AutoGoLive = false, BotCount = 0, ClientCount = 0,
        PauseOnFocusLoss = false, HostIsAdmin = false, Readiness = "map-playable" };
    var plan = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.ResolvedPlayPlan>(
        VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Plan(JsonUtility.ToJson(configuration)));
    if (!plan.Passed) blockers.Add("PlayLaunch.Plan стенда отказал: " + plan.Code + " " + plan.Message);
} catch (Exception error) { blockers.Add("Stand/PlayLaunch preflight: " + error.Message); }
var sceneText = System.IO.File.Exists(ScenePath) ? System.IO.File.ReadAllText(ScenePath) : "";
foreach (var weapon in new[] { "PPK", "AK105" }) {
    var path = "Assets/Data/Weapons/" + weapon + "_Weapon.asset";
    var guid = AssetDatabase.AssetPathToGUID(path);
    var info = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Arsenal.WeaponInfo>(path);
    if (info == null || string.IsNullOrEmpty(guid) || !sceneText.Contains(guid)) blockers.Add("Стенд не ссылается на " + path);
}
bool runtimeDone = System.IO.Directory.Exists(Root + "/runtime");
if (runtimeDone) notes.Add("runtime/ уже существует: B3 какой-то фазы выполнялся ранее — не повторять ту же фазу.");
System.IO.Directory.CreateDirectory(Root);
var reportPath = Root + "/preflight-" + DateTime.UtcNow.Ticks + ".json";
System.IO.File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
    executionCompleted = true, passed = blockers.Count == 0, blockers, notes, dirtyScenes,
    cc1Input = new { playerFlowFieldGone, botKit = kitType.FullName, addBot = addBot != null, botBodyModeGone, combatAssetsGone,
        controllerPath, controllerExists },
    stand, launchState, runtimeDone,
    activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
    unity = Application.unityVersion, scope = "Read-only preflight; no assets/scenes/play state changed."
}, Newtonsoft.Json.Formatting.Indented));
return new { executionCompleted = true, passed = blockers.Count == 0, blockers = blockers.Take(10).ToArray(), notes = notes.ToArray(),
    launchState, reportPath };
