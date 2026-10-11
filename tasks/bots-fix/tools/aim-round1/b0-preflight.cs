// B0 READ-ONLY preflight Unity-раунда I наведения (этапы 0-2). Повтор безопасен: ничего не меняет в Unity,
// пишет только новый preflight-<ticks>.json под output root тикета.
const string Root = "tasks/bots-fix/reports/aim-round1";
var bodies = new[] {
    "Assets/Resources/Bots/ClipBodies/Optimized_MEF_Player_Black_ClipBody.prefab",
    "Assets/Resources/Bots/ClipBodies/Optimized_MEF_Player_ClipBody.prefab",
    "Assets/Resources/Bots/ClipBodies/PlayerControllersCyborgAvatar_ClipBody.prefab" };
var sources = new Dictionary<string, string> {
    { "310c90ae0d962f344a1eddbe48a429c9", "Assets/Prefabs/Player/Optimized_MEF_Player_Black.prefab" },
    { "b8146035f3f436a4799882b3a0d1dd45", "Assets/Prefabs/Player/Optimized_MEF_Player.prefab" },
    { "8e0e06323e4754f479a9f19788554652", "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab" } };
const string ScenePath = "Assets/Scenes/Debug/BotCombatStand.unity";
var blockers = new List<string>();
var notes = new List<string>();
Func<byte[], string> sha = bytes => {
    using (var h = System.Security.Cryptography.SHA256.Create())
        return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
};
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
var stamp = typeof(VrBattlegrounds.EditorTools.BotClipComposition).GetMethod("StampAssetIdentity",
    BindingFlags.NonPublic | BindingFlags.Static);
if (stamp == null) blockers.Add("В загруженной сборке нет BotClipComposition.StampAssetIdentity — не тот input.");
// Input раунда I: общая метрика BotAimMath, вычислитель BotAim и aims[] в BotCombatAssets скомпилированы.
var aimMath = typeof(VrBattlegrounds.Bots.BotCombatAssets).Assembly.GetType("VrBattlegrounds.Bots.BotAimMath");
var aimRuntime = typeof(VrBattlegrounds.Bots.BotCombatAssets).Assembly.GetType("VrBattlegrounds.Bots.BotAim");
var tryGetAim = typeof(VrBattlegrounds.Bots.BotCombatAssets).GetMethod("TryGetAim");
var sampleAim = typeof(VrBattlegrounds.EditorTools.BotClipComposition).GetMethod("SampleAim", BindingFlags.NonPublic | BindingFlags.Static);
if (aimMath == null || aimRuntime == null || tryGetAim == null || sampleAim == null)
    blockers.Add("В загруженных сборках нет BotAimMath/BotAim/TryGetAim/SampleAim — не input раунда I.");
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
var library = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Bots.BotCombatAssets>("Assets/Resources/Bots/BotCombatAssets.asset");
object libraryCounts = null;
if (library == null) blockers.Add("Нет BotCombatAssets.");
else {
    libraryCounts = new { models = library.clipBodies == null ? 0 : library.clipBodies.Length,
        sockets = library.sockets == null ? 0 : library.sockets.Length, motions = library.motions == null ? 0 : library.motions.Length,
        aims = library.aims == null ? 0 : library.aims.Length };
    foreach (var pair in sources) {
        GameObject source;
        string reason;
        bool canonical = library.TryGetCanonicalSource(pair.Key, out source);
        if (!canonical || AssetDatabase.GetAssetPath(source) != pair.Value ||
            !VrBattlegrounds.Player.Avatars.AvatarManager.ValidateBotAvatarSource(source, out reason))
            notes.Add("До A canonical source ещё не разрешён (ожидаемо до authoring): " + pair.Value);
    }
}
var files = bodies.Select(p => System.IO.File.Exists(p)
    ? (object)new { path = p, sha256 = sha(System.IO.File.ReadAllBytes(p)) }
    : new { path = p, sha256 = (string)null }).ToArray();
bool authoringDone = System.IO.File.Exists(Root + "/result.json");
if (authoringDone) notes.Add("result.json уже существует: A выполнен ранее, B1 не повторять.");
System.IO.Directory.CreateDirectory(Root);
var reportPath = Root + "/preflight-" + DateTime.UtcNow.Ticks + ".json";
System.IO.File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
    executionCompleted = true, passed = blockers.Count == 0, blockers, notes, dirtyScenes, stampAssetIdentityPresent = stamp != null,
    aimInputPresent = aimMath != null && aimRuntime != null && tryGetAim != null && sampleAim != null,
    stand, launchState, libraryCounts, clipBodyFilesBeforeA = files, authoringDone,
    activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
    unity = Application.unityVersion, scope = "Read-only preflight; no assets/scenes/play state changed."
}, Newtonsoft.Json.Formatting.Indented));
return new { executionCompleted = true, passed = blockers.Count == 0, blockers = blockers.Take(10).ToArray(),
    notes = notes.Count, authoringDone, launchState, reportPath };
