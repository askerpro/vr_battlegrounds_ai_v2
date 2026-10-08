if (UnityEditor.EditorApplication.isPlaying || UnityEditor.EditorApplication.isCompiling)
    throw new System.InvalidOperationException("Inventory требует idle Editor.");
var registry = UnityEditor.AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Maps.MapRegistry>("Assets/Data/Maps/MapRegistry.asset");
if (registry == null) throw new System.InvalidOperationException("MapRegistry.Missing");
var maps = new System.Collections.Generic.List<object>();
var failures = new System.Collections.Generic.List<string>();
foreach (var data in registry.maps)
{
    if (data == null) { failures.Add("MapData.Null"); continue; }
    string path = null;
    foreach (string guid in UnityEditor.AssetDatabase.FindAssets(data.sceneName + " t:Scene"))
    {
        string candidate = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
        if (System.IO.Path.GetFileNameWithoutExtension(candidate) != data.sceneName) continue;
        if (path != null) throw new System.InvalidOperationException("Scene.Duplicate:" + data.sceneName);
        path = candidate;
    }
    if (path == null) { failures.Add("Scene.Missing:" + data.sceneName); continue; }
    var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene(path);
    try
    {
        var records = new System.Collections.Generic.List<object>();
        var netIds = new System.Collections.Generic.HashSet<ulong>();
        var uids = new System.Collections.Generic.HashSet<System.Guid>();
        int network = 0, uxr = 0, stations = 0, zones = 0;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var component in root.GetComponentsInChildren<UnityEngine.MonoBehaviour>(true))
        {
            if (component == null) continue;
            var ni = component as Mirror.NetworkIdentity;
            var uid = component as UltimateXR.Core.Unique.IUxrUniqueId;
            bool relevant = ni != null || uid != null || component is VrBattlegrounds.Maps.TeamSpawnZone ||
                component is VrBattlegrounds.Arsenal.ArsenalWallController || component is VrBattlegrounds.Arsenal.ArsenalBoundaryWall ||
                component is VrBattlegrounds.PhysicalSpaceUtils.PhysicalSpaceAnchor || component is VrBattlegrounds.Managers.MapReferee;
            if (!relevant) continue;
            if (ni != null) { network++; if (ni.sceneId == 0 || !netIds.Add(ni.sceneId)) failures.Add("SceneIdentity.Invalid:" + data.sceneName + "/" + component.name); }
            if (uid != null) { uxr++; if (uid.UniqueId == System.Guid.Empty || !uids.Add(uid.UniqueId)) failures.Add("UxrIdentity.Invalid:" + data.sceneName + "/" + component.name); }
            if (component is VrBattlegrounds.Arsenal.ArsenalWallController) stations++;
            if (component is VrBattlegrounds.Maps.TeamSpawnZone) zones++;
            var position = component.transform.position; var rotation = component.transform.rotation; var scale = component.transform.lossyScale;
            string hierarchy = component.name;
            for (var parent = component.transform.parent; parent != null; parent = parent.parent) hierarchy = parent.name + "/" + hierarchy;
            records.Add(new { type = component.GetType().FullName, hierarchy, sceneId = ni != null ? ni.sceneId.ToString() : null,
                uxrId = uid != null ? uid.UniqueId.ToString("D") : null, active = component.gameObject.activeInHierarchy,
                position = new[] {position.x, position.y, position.z}, rotation = new[] {rotation.x, rotation.y, rotation.z, rotation.w},
                scale = new[] {scale.x, scale.y, scale.z}, referenceJson = UnityEditor.EditorJsonUtility.ToJson(component) });
        }
        maps.Add(new { scene = data.sceneName, path, network, uxr, stations, zones, records });
    }
    finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
}
string report = "tasks/map-runtime-bootstrap/reports/details/native-inventory-20261005.json";
System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(report));
System.IO.File.WriteAllText(report, Newtonsoft.Json.JsonConvert.SerializeObject(new {maps, failures, limit = "Native preview world transforms/serialized IDs. Actual runtime UXR registration and Relay refs require later live probe."}, Newtonsoft.Json.Formatting.Indented));
string summary = "tasks/map-runtime-bootstrap/reports/native-inventory-summary.json";
System.IO.File.WriteAllText(summary, Newtonsoft.Json.JsonConvert.SerializeObject(new {mapCount = maps.Count, failureCount = failures.Count, reportPath = report, failures = failures.GetRange(0,System.Math.Min(10,failures.Count)), registeredRuntimeIdsVerified = false}, Newtonsoft.Json.Formatting.Indented));
return new {passed = failures.Count == 0, mapCount = maps.Count, failureCount = failures.Count, reportPath = report, summaryPath = summary};
