// B0 READ-ONLY preflight раунда pf-round3 (этап pf-prototype: бот = обычный аватар игрока, режим PlayerFlow).
// Повтор безопасен: ничего не меняет в Unity, пишет только новый preflight-<ticks>.json под output root.
const string Root = "tasks/bots-fix/reports/pf-round3";
const string ScenePath = "Assets/Scenes/Debug/BotCombatStand.unity";
const string MefGuid = "b8146035f3f436a4799882b3a0d1dd45";      // Optimized_MEF_Player
const string MefBlackGuid = "310c90ae0d962f344a1eddbe48a429c9"; // Optimized_MEF_Player_Black (цель стенда)
var blockers = new List<string>();
var notes = new List<string>();
bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
if (playing || EditorApplication.isCompiling || EditorApplication.isUpdating)
    blockers.Add("Editor не в idle EditMode (playing/compiling/updating).");
if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null)
    blockers.Add("Открыт prefab stage.");
var dirtyScenes = new List<string>();
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) {
    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
    if (scene.isDirty) dirtyScenes.Add(scene.path);
}
if (dirtyScenes.Count > 0) blockers.Add("Есть несохранённые сцены: " + string.Join(", ", dirtyScenes.ToArray()));
// Вход этапа pf-prototype: переключатель стенда, замер, адаптер P (прямые ссылки — payload не скомпилируется без них).
var playerFlowField = typeof(VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions).GetField("PlayerFlow");
var probeType = typeof(VrBattlegrounds.DevTools.BotCombatStand.BotStandPlayerFlowProbe);
var adapterTypes = new[] { typeof(VrBattlegrounds.Bots.BotAvatarDriver), typeof(VrBattlegrounds.Bots.BotHands),
    typeof(VrBattlegrounds.Bots.BotWeaponAim), typeof(VrBattlegrounds.Bots.BotRuntimeController) };
var playerFlowMode = Enum.GetNames(typeof(VrBattlegrounds.Bots.BotBodyMode)).Contains("PlayerFlow");
var addBotMode = typeof(VrBattlegrounds.Bots.BotDirector).GetMethod("AddBot", new[] { typeof(VrBattlegrounds.Bots.BotBodyMode) });
if (playerFlowField == null || !playerFlowMode || addBotMode == null)
    blockers.Add("В загруженных сборках нет BotStandRunOptions.PlayerFlow / BotBodyMode.PlayerFlow / BotDirector.AddBot(mode) — не вход pf-prototype.");
var avatarSourceGuid = typeof(VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions).GetField("AvatarSourceGuid");
if (avatarSourceGuid != null) notes.Add("BotStandRunOptions.AvatarSourceGuid ещё существует (клиповый путь?) — пакет его не использует.");
Dictionary<string, object> stand = null;
string launchState = null;
try {
    stand = VrBattlegrounds.EditorTools.BotCombatStandEditor.Status();
    if ((bool)stand["owned"]) blockers.Add("Стенд уже владеет запуском (owned=true).");
    var launch = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(
        VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
    launchState = launch.State;
    if (launch.State != "Idle") blockers.Add("PlayLaunch не Idle: " + launch.State + " " + launch.Error);
    // Тот же план, что строит BotCombatStandEditor.Prepare; Plan только вычисляет план.
    var configuration = new VrBattlegrounds.DevTools.PlayLaunchConfiguration {
        Role = "server", SceneSource = "scene", ScenePath = ScenePath,
        ModeId = "respawn", AutoGoLive = false, BotCount = 0, ClientCount = 0,
        PauseOnFocusLoss = false, HostIsAdmin = false, Readiness = "map-playable" };
    var plan = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.ResolvedPlayPlan>(
        VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Plan(JsonUtility.ToJson(configuration)));
    if (!plan.Passed) blockers.Add("PlayLaunch.Plan стенда отказал: " + plan.Code + " " + plan.Message);
} catch (Exception error) { blockers.Add("Stand/PlayLaunch preflight: " + error.Message); }
// Оружие стенда: сцена должна ссылаться на canonical PPK/AK105 (поиск по GUID, без открытия сцены).
var sceneText = System.IO.File.Exists(ScenePath) ? System.IO.File.ReadAllText(ScenePath) : "";
foreach (var weapon in new[] { "PPK", "AK105" }) {
    var path = "Assets/Data/Weapons/" + weapon + "_Weapon.asset";
    var guid = AssetDatabase.AssetPathToGUID(path);
    var info = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Arsenal.WeaponInfo>(path);
    if (info == null || string.IsNullOrEmpty(guid) || !sceneText.Contains(guid))
        blockers.Add("Стенд не ссылается на " + path);
    else if (info.name != weapon + "_Weapon") blockers.Add("Имя WeaponInfo не совпадает с WeaponName стенда: " + info.name);
}
// Штатный выбор тела (TeamAvatarStrategy): скин 0 команды. Бот с AvatarIndex=-1 получает BotSkin.PickHittable — первый
// скин с твёрдым коллайдером. Ожидание пакета: Terrorists[0] = MEF (бот), CounterTerrorists[0] = MEF Black (цель).
var teams = new List<object>();
foreach (var teamGuid in AssetDatabase.FindAssets("t:TeamData", new[] { "Assets/Data/Teams" })) {
    var team = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.TeamData>(AssetDatabase.GUIDToAssetPath(teamGuid));
    if (team == null) continue;
    var skins = new List<object>();
    for (int i = 0; team.avatars != null && i < team.avatars.Count; i++) {
        var prefab = team.GetAvatarPrefab(i);
        var prefabPath = prefab != null ? AssetDatabase.GetAssetPath(prefab) : null;
        skins.Add(new { index = i, prefab = prefabPath, guid = prefabPath != null ? AssetDatabase.AssetPathToGUID(prefabPath) : null,
            hittable = VrBattlegrounds.Bots.BotSkin.HasSolidCollider(prefab) });
    }
    teams.Add(new { name = team.name, team.teamIndex, skins });
}
bool mefSkin = teams.Count > 0 && Newtonsoft.Json.JsonConvert.SerializeObject(teams).Contains(MefGuid);
if (!mefSkin) blockers.Add("Ни одна команда не даёт скин Optimized_MEF_Player — AvatarIndex для MEF не определён.");
if (!Newtonsoft.Json.JsonConvert.SerializeObject(teams).Contains(MefBlackGuid)) notes.Add("Нет скина MEF Black ни у одной команды.");
bool runtimeDone = System.IO.File.Exists(Root + "/runtime/launch.json");
if (runtimeDone) notes.Add("runtime/launch.json уже существует: B3 выполнен ранее, не повторять.");
System.IO.Directory.CreateDirectory(Root);
var reportPath = Root + "/preflight-" + DateTime.UtcNow.Ticks + ".json";
System.IO.File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
    executionCompleted = true, passed = blockers.Count == 0, blockers, notes, dirtyScenes,
    playerFlowInput = new { optionField = playerFlowField != null, mode = playerFlowMode, addBotMode = addBotMode != null,
        probe = probeType.FullName, adapter = adapterTypes.Select(t => t.FullName).ToArray(), avatarSourceGuidField = avatarSourceGuid != null },
    stand, launchState, teams, runtimeDone,
    activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
    unity = Application.unityVersion, scope = "Read-only preflight; no assets/scenes/play state changed."
}, Newtonsoft.Json.Formatting.Indented));
return new { executionCompleted = true, passed = blockers.Count == 0, blockers = blockers.Take(10).ToArray(),
    notes = notes.ToArray(), launchState, teams, reportPath };
