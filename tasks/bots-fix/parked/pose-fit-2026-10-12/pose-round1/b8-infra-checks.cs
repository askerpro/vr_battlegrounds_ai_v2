// B8 READ-ONLY (Edit mode, после B2 и до Play): проверки владельца avatar-ik (4098) для Final IK #1 (asmdef RootMotion).
// (1) сборки: RootMotion/RootMotion.Editor загружены, IKSolverLimb/VRIK в RootMotion, не в Assembly-CSharp; AvatarVrikDriver
//     в Assembly-CSharp; VrBattlegrounds ссылается на RootMotion;
// (2) Missing Script: Assets/Prefabs/Player/Experimental/Optimized_MEF_Player_VRIK.prefab и все префабы, ссылающиеся на GUID
//     скриптов RootMotion (LoadPrefabContents, без сохранения); сцены с такими GUID — только список;
// (3) компиляция player-скриптов под Android: AndroidCompileGate.Run() (Temp/, активная платформа не меняется).
// Пишет infra-checks.json под Root. Повтор безопасен.
const string Root = "tasks/bots-fix/reports/pose-round1";
const string VrikPrefab = "Assets/Prefabs/Player/Experimental/Optimized_MEF_Player_VRIK.prefab";
var failures = new List<string>();
var assemblies = AppDomain.CurrentDomain.GetAssemblies();
Func<string, Assembly> named = n => assemblies.FirstOrDefault(a => a.GetName().Name == n);
var rootMotion = named("RootMotion");
var rootMotionEditor = named("RootMotion.Editor");
var csharp = named("Assembly-CSharp");
var bots = typeof(VrBattlegrounds.Bots.BotCombatAssets).Assembly;
var assemblyCheck = new {
    rootMotion = rootMotion != null, rootMotionEditor = rootMotionEditor != null,
    limbInRootMotion = rootMotion != null && rootMotion.GetType("RootMotion.FinalIK.IKSolverLimb") != null,
    vrikInRootMotion = rootMotion != null && rootMotion.GetType("RootMotion.FinalIK.VRIK") != null,
    finalIkInCsharp = csharp != null && csharp.GetType("RootMotion.FinalIK.VRIK") != null,
    vrikDriverInCsharp = csharp != null && csharp.GetType("VRBattlegrounds.Integration.AvatarVrikDriver") != null,
    botsReferenceRootMotion = bots.GetReferencedAssemblies().Any(a => a.Name == "RootMotion")
};
if (!assemblyCheck.rootMotion || !assemblyCheck.rootMotionEditor || !assemblyCheck.limbInRootMotion || !assemblyCheck.vrikInRootMotion ||
    assemblyCheck.finalIkInCsharp || !assemblyCheck.vrikDriverInCsharp || !assemblyCheck.botsReferenceRootMotion)
    failures.Add("Сборки RootMotion/Assembly-CSharp не по контракту Final IK #1");
// GUID скриптов RootMotion: из .cs.meta пакета.
var scriptGuids = new HashSet<string>(StringComparer.Ordinal);
foreach (var meta in System.IO.Directory.GetFiles("Assets/ThirdParty/RootMotion", "*.cs.meta", System.IO.SearchOption.AllDirectories)) {
    var line = System.IO.File.ReadLines(meta).FirstOrDefault(l => l.StartsWith("guid: "));
    if (line != null) scriptGuids.Add(line.Substring(6).Trim());
}
Func<string, bool> refersRootMotion = path => {
    var text = System.IO.File.ReadAllText(path);
    foreach (var guid in scriptGuids) if (text.Contains(guid)) return true;
    return false;
};
var prefabs = System.IO.Directory.GetFiles("Assets", "*.prefab", System.IO.SearchOption.AllDirectories)
    .Select(p => p.Replace('\\', '/')).Where(p => !p.StartsWith("Assets/ThirdParty/RootMotion/")).Where(refersRootMotion).ToList();
if (!prefabs.Contains(VrikPrefab)) prefabs.Insert(0, VrikPrefab);
var prefabRows = new List<object>();
int missingTotal = 0;
Type vrikType = rootMotion != null ? rootMotion.GetType("RootMotion.FinalIK.VRIK") : null;
foreach (var path in prefabs) {
    GameObject contents = null;
    try {
        if (!System.IO.File.Exists(path)) { prefabRows.Add(new { path, exists = false }); failures.Add("Нет " + path); continue; }
        contents = PrefabUtility.LoadPrefabContents(path);
        int missing = contents.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
        int vrik = vrikType != null ? contents.GetComponentsInChildren(vrikType, true).Length : -1;
        missingTotal += missing;
        if (missing > 0) failures.Add("Missing Script " + missing + ": " + path);
        prefabRows.Add(new { path, missingScripts = missing, vrikComponents = vrik });
    } catch (Exception error) { failures.Add("Префаб " + path + ": " + error.Message); prefabRows.Add(new { path, error = error.Message }); }
    finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); }
}
var scenes = System.IO.Directory.GetFiles("Assets", "*.unity", System.IO.SearchOption.AllDirectories)
    .Select(p => p.Replace('\\', '/')).Where(p => !p.StartsWith("Assets/ThirdParty/RootMotion/")).Where(refersRootMotion).ToArray();
var android = VrBattlegrounds.EditorTools.AndroidCompileGate.Run();
if (!android.Passed) failures.Add(android.Summary);
System.IO.Directory.CreateDirectory(Root);
var reportPath = Root + "/infra-checks.json";
System.IO.File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
    executionCompleted = true, passed = failures.Count == 0, failures, assemblyCheck,
    rootMotionScriptGuids = scriptGuids.Count, prefabs = prefabRows, missingScriptsTotal = missingTotal,
    scenesReferencingRootMotion = scenes,
    android = new { android.Passed, android.Summary, errors = android.Errors.Take(20).ToArray(), errorCount = android.Errors.Count },
    utc = DateTime.UtcNow.ToString("o"),
    scope = "Read-only: префабы открыты без сохранения; Android — компиляция player-скриптов в Temp/, без сборки и смены платформы."
}, Newtonsoft.Json.Formatting.Indented));
return new { executionCompleted = true, passed = failures.Count == 0, failures = failures.Take(10).ToArray(), assemblyCheck,
    prefabsChecked = prefabRows.Count, missingScriptsTotal = missingTotal, scenes = scenes.Length,
    androidPassed = android.Passed, androidErrors = android.Errors.Count, reportPath };
