// Новый visual input: не запускает Play и не меняет assets.
const string Root = "tasks/bots-fix/reports/cc1-visual-consolidated-20261018-r1";
var blockers = new List<string>();
if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) blockers.Add("Нужен idle EditMode.");
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) blockers.Add("Есть dirty scene.");
if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null) blockers.Add("Открыт PrefabStage.");
var stand = VrBattlegrounds.EditorTools.BotCombatStandEditor.Status();
if ((bool)stand["owned"]) blockers.Add("Стенд уже занят.");
var launch = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
if (launch.State != "Idle") blockers.Add("PlayLaunch не Idle.");
if (System.IO.Directory.Exists(Root)) blockers.Add("Root существует: выбрать новый во всех payloads.");
if (VrBattlegrounds.Editor.HandPoseReview.HandPoseFitRuntimeCapture.Busy) blockers.Add("Чужая HandPoseFit capture занята.");
var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (var path in new[] {
    "Assets/Prefabs/Player/Optimized_MEF_Player_Black.prefab", "Assets/Data/Weapons/AK105_Weapon.asset",
    "Assets/Resources/" + VrBattlegrounds.Bots.BotCombatDriver.ProxyControllerResource + ".controller",
    "Assets/Editor/VR_Battlegrounds/Bots/BotStandVisualCapture.cs", "Assets/Editor/VR_Battlegrounds/Bots/BotStandRecorder.cs",
    "Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrWeaponManager.cs",
    "Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/UxrManager.cs",
    "Assets/ThirdParty/UltimateXR/Runtime/Scripts/Avatar/Controllers/UxrStandardAvatarController.cs",
    "Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabManager.cs" }) {
    paths.Add(path);
    foreach (var dependency in AssetDatabase.GetDependencies(path, true)) paths.Add(dependency);
}
foreach (var folder in new[] { "Assets/Scripts/Bots", "Assets/Scripts/Debug/BotCombatStand", "Assets/Editor/VR_Battlegrounds/Avatars/HandPoseReview" })
    foreach (var path in System.IO.Directory.GetFiles(folder, "*.cs", System.IO.SearchOption.AllDirectories)) paths.Add(path.Replace('\\', '/'));
foreach (var path in System.IO.Directory.GetFiles("tasks/bots-fix/tools/cc1-visual", "*", System.IO.SearchOption.TopDirectoryOnly)) paths.Add(path.Replace('\\', '/'));
var hashes = new Dictionary<string, string>();
foreach (var path in paths.OrderBy(p => p)) {
    if (!System.IO.File.Exists(path)) { blockers.Add("Отсутствует input: " + path); continue; }
    using (var stream = System.IO.File.OpenRead(path)) using (var sha = System.Security.Cryptography.SHA256.Create())
        hashes[path] = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
}
var api = new[] { typeof(VrBattlegrounds.Editor.HandPoseReview.HandPoseFitSnapshot).FullName, typeof(VrBattlegrounds.EditorTools.BotStandVisualCapture).FullName };
if (blockers.Count > 0) return new { executionCompleted = true, ready = false, blockers = blockers.Take(10).ToArray() };
System.IO.Directory.CreateDirectory(Root);
System.IO.File.WriteAllText(Root + "/preflight.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
    schema = "cc1-visual-input@1", ready = true, utc = DateTime.UtcNow.ToString("o"), unity = Application.unityVersion,
    hashes, api, source = "current candidate; не exact738", ownerDecision = "UNKNOWN" }, Newtonsoft.Json.Formatting.Indented));
return new { executionCompleted = true, ready = true, hashCount = hashes.Count, manifest = Root + "/preflight.json" };
