// B2 MUTATION (Edit mode): идемпотентность идентичности ClipBody после A.
// Действие: снимок -> SaveAssets (сброс отложенных правок в памяти) -> ImportAsset(ForceUpdate) трёх ClipBody
// -> загрузка -> AssetDatabase.SaveAssets() -> снимок.
// Критерий PASS: SHA256 файлов, строки _uxrUniqueId и загруженные UniqueId совпадают с identity-a.json на всех шагах,
// флаги UXR без расхождений, _assetId каноничен. Dirty и UniqueId вне диска — диагностика (warnings).
// Нет baseline -> passed=null (Unknown).
// НЕ ПОВТОРЯТЬ после таймаута: сначала проверить наличие identity-post-authoring.json (payload сам отказывает).
const string Root = "tasks/bots-fix/reports/aim-round2";
const string Phase = "post-authoring";
var reportPath = Root + "/identity-" + Phase + ".json";
if (System.IO.File.Exists(reportPath))
    return new { executionCompleted = true, refused = true, reason = "Отчёт фазы уже существует; не повторять.", reportPath };
if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
    throw new InvalidOperationException("Нужен idle EditMode.");
var bodies = new[] {
    "Assets/Resources/Bots/ClipBodies/Optimized_MEF_Player_Black_ClipBody.prefab",
    "Assets/Resources/Bots/ClipBodies/Optimized_MEF_Player_ClipBody.prefab",
    "Assets/Resources/Bots/ClipBodies/PlayerControllersCyborgAvatar_ClipBody.prefab" };
Func<byte[], string> sha = bytes => {
    using (var h = System.Security.Cryptography.SHA256.Create())
        return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
};
// --- Общий read-only снимок идентичности (тот же код в B1/B7) ---
Func<string, Dictionary<string, object>> identitySnapshot = path => {
    var row = new Dictionary<string, object> { { "path", path } };
    if (!System.IO.File.Exists(path)) { row["exists"] = false; return row; }
    var bytes = System.IO.File.ReadAllBytes(path);
    row["exists"] = true; row["sha256"] = sha(bytes); row["bytes"] = bytes.Length;
    var text = System.Text.Encoding.UTF8.GetString(bytes);
    row["crlf"] = text.Contains("\r\n");
    var lines = text.Replace("\r\n", "\n").Split('\n');
    var uxrIds = new List<string>();
    var assetIdValues = new List<string>();
    int prefabGuidOverrides = 0, isInPrefabOverrides = 0;
    string header = null, target = null;
    for (int i = 0; i < lines.Length; i++) {
        string line = lines[i], trimmed = lines[i].Trim();
        if (line.StartsWith("--- ")) header = trimmed;
        if (trimmed.StartsWith("- target:")) target = trimmed;
        if (trimmed == "propertyPath: __prefabGuid") prefabGuidOverrides++;
        if (trimmed == "propertyPath: __isInPrefab") isInPrefabOverrides++;
        string next = i + 1 < lines.Length ? lines[i + 1].Trim() : "";
        if (trimmed == "propertyPath: _assetId") assetIdValues.Add(target + " | " + next);
        else if (trimmed.StartsWith("_assetId:")) assetIdValues.Add(header + " | " + trimmed);
        if (!line.Contains("_uxrUniqueId")) continue;
        uxrIds.Add(trimmed.StartsWith("propertyPath:") ? target + " | " + trimmed + " | " + next : header + " | " + trimmed);
    }
    row["uxrUniqueIdLineCount"] = uxrIds.Count;
    row["uxrUniqueIdLinesSha256"] = sha(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", uxrIds.ToArray())));
    row["uxrUniqueIdLines"] = uxrIds;
    row["prefabGuidOverrides"] = prefabGuidOverrides; row["isInPrefabOverrides"] = isInPrefabOverrides;
    row["assetIdFileValues"] = assetIdValues;
    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    if (go == null) { row["loaded"] = false; return row; }
    row["loaded"] = true;
    var onDisk = VrBattlegrounds.EditorTools.VersionControl.UxrUniqueIdPersister.ReadIdsOnDisk(path);
    var loadedIds = new List<string>();
    int flagMismatches = 0, unpersisted = 0, emptyIds = 0, dirty = EditorUtility.IsDirty(go) ? 1 : 0;
    foreach (var component in go.GetComponentsInChildren<UltimateXR.Core.Components.UxrComponent>(true)) {
        var id = component.UniqueId;
        loadedIds.Add(AnimationUtility.CalculateTransformPath(component.transform, go.transform) + ":" + component.GetType().Name + "=" + id.ToString());
        bool inPrefab; string prefabGuid;
        if (VrBattlegrounds.EditorTools.VersionControl.UxrUniqueIdPersister.HasMismatch(component, out inPrefab, out prefabGuid)) flagMismatches++;
        if (id == Guid.Empty) emptyIds++; else if (!onDisk.Contains(id.ToString())) unpersisted++;
        if (EditorUtility.IsDirty(component)) dirty++;
    }
    loadedIds.Sort(StringComparer.Ordinal);
    var networkIdentity = go.GetComponent<Mirror.NetworkIdentity>();
    uint? serializedAssetId = null;
    if (networkIdentity != null) {
        var property = new SerializedObject(networkIdentity).FindProperty("_assetId");
        if (property != null) serializedAssetId = property.uintValue;
        if (EditorUtility.IsDirty(networkIdentity)) dirty++;
    }
    uint expectedAssetId = Mirror.NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(path)));
    row["loadedUxrComponents"] = loadedIds.Count;
    row["loadedIdsSha256"] = sha(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", loadedIds.ToArray())));
    row["uxrFlagMismatches"] = flagMismatches; row["unpersistedIds"] = unpersisted; row["emptyIds"] = emptyIds;
    row["dirtyObjects"] = dirty;
    row["serializedAssetId"] = serializedAssetId; row["expectedAssetId"] = expectedAssetId;
    row["assetIdCanonical"] = serializedAssetId.HasValue && serializedAssetId.Value == expectedAssetId;
    return row;
};
Func<Dictionary<string, object>, string, string> field = (row, key) => row.ContainsKey(key) && row[key] != null ? row[key].ToString() : null;
var baseline = new Dictionary<string, Newtonsoft.Json.Linq.JToken>();
var baselinePath = Root + "/identity-a.json";
if (System.IO.File.Exists(baselinePath))
    foreach (var token in (Newtonsoft.Json.Linq.JArray)Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(baselinePath))["rows"])
        baseline[(string)token["path"]] = token;
var dirtyScenes = new List<string>();
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) dirtyScenes.Add(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path);
var before = bodies.Select(identitySnapshot).ToList();
// Сначала сбрасываем то, что OnValidate/Play могли исправить только в памяти: иначе ForceUpdate отбросит это молча.
AssetDatabase.SaveAssets();
var afterFlush = bodies.Select(identitySnapshot).ToList();
foreach (var body in bodies) AssetDatabase.ImportAsset(body, ImportAssetOptions.ForceUpdate);
var afterImport = bodies.Select(identitySnapshot).ToList();
AssetDatabase.SaveAssets();
var after = bodies.Select(identitySnapshot).ToList();
var failures = new List<string>();
var unknown = new List<string>();
var warnings = new List<string>();
for (int i = 0; i < bodies.Length; i++) {
    string name = System.IO.Path.GetFileName(bodies[i]);
    Newtonsoft.Json.Linq.JToken a;
    if (!baseline.TryGetValue(bodies[i], out a)) { unknown.Add(name + ": нет baseline identity-a.json"); continue; }
    if ((string)a["sha256"] != field(before[i], "sha256")) failures.Add(name + ": файл изменился между A и проверкой");
    if (field(before[i], "sha256") != field(afterFlush[i], "sha256")) failures.Add(name + ": первый SaveAssets (отложенные правки в памяти) изменил файл");
    if (field(afterFlush[i], "sha256") != field(after[i], "sha256")) failures.Add(name + ": SaveAssets после ForceUpdate изменил файл");
    if ((string)a["uxrUniqueIdLinesSha256"] != field(after[i], "uxrUniqueIdLinesSha256")) failures.Add(name + ": строки _uxrUniqueId изменились");
    if ((string)a["loadedIdsSha256"] != field(before[i], "loadedIdsSha256") || (string)a["loadedIdsSha256"] != field(afterImport[i], "loadedIdsSha256") ||
        (string)a["loadedIdsSha256"] != field(after[i], "loadedIdsSha256"))
        failures.Add(name + ": загруженные UniqueId изменились");
    if (field(before[i], "dirtyObjects") != "0") warnings.Add(name + ": до проверки были dirty объекты (" + field(before[i], "dirtyObjects") + ")");
    if (field(afterImport[i], "dirtyObjects") != "0") warnings.Add(name + ": после ForceUpdate+load есть dirty (" + field(afterImport[i], "dirtyObjects") + ")");
    if (field(after[i], "uxrFlagMismatches") != "0") failures.Add(name + ": UXR __isInPrefab/__prefabGuid расходятся");
    // Строже исходного критерия (может давать ложный сигнал для runtime-генерируемых ID) — только предупреждение.
    if (field(after[i], "unpersistedIds") != "0") warnings.Add(name + ": загруженные UniqueId отсутствуют на диске (" + field(after[i], "unpersistedIds") + ")");
    if (field(after[i], "assetIdCanonical") != "True") failures.Add(name + ": _assetId не каноничен");
}
bool? passed = failures.Count > 0 ? (bool?)false : unknown.Count > 0 ? (bool?)null : true;
System.IO.Directory.CreateDirectory(Root);
System.IO.File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
    executionCompleted = true, phase = Phase, passed, failures, unknown, warnings, dirtyScenesBefore = dirtyScenes,
    baseline = System.IO.File.Exists(baselinePath) ? baselinePath : null, before, afterFlush, afterImport, after,
    scope = "SaveAssets flush + ForceUpdate import + load + SaveAssets on 3 ClipBody prefabs; file/ID identity only."
}, Newtonsoft.Json.Formatting.Indented));
return new { executionCompleted = true, phase = Phase, passed, failures = failures.Take(10).ToArray(), failureCount = failures.Count,
    unknown = unknown.Take(5).ToArray(), warnings = warnings.Take(5).ToArray(), reportPath,
    sha256 = after.Select(r => System.IO.Path.GetFileName((string)r["path"]) + "=" + field(r, "sha256")).ToArray() };
