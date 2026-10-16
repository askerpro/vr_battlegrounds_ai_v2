// B8: только наблюдение precache/container/memory в собственном worker; SDK/состояние игры не меняет.
const string Root = "tasks/bots-fix/reports/cc1-round1";
const string Label = "p1-before-queue";
string projectRoot = System.IO.Path.GetFullPath(Application.dataPath + "/..").Replace("\\", "/");
if (projectRoot != "F:/CodexWorktrees/unity-editor-worker/Vr_Battlegrounds_ai")
    return new { executionCompleted = true, refused = true, projectRoot };
var manager = UltimateXR.Core.UxrManager.HasInstance ? UltimateXR.Core.UxrManager.Instance : null;
var field = typeof(UltimateXR.Core.UxrManager).GetField("_dynamicInstances", BindingFlags.NonPublic | BindingFlags.Instance);
var instances = manager != null && field != null ? field.GetValue(manager) as System.Collections.IDictionary : null;
int live = 0;
var names = new List<string>();
if (instances != null) foreach (System.Collections.DictionaryEntry entry in instances) {
    var obj = entry.Value as GameObject;
    if (obj == null) continue;
    live++;
    if (names.Count < 5) names.Add(obj.name);
}
var payload = new {
    executionCompleted = true, readOnly = true, projectRoot, label = Label,
    utc = DateTime.UtcNow.ToString("o"), frame = Time.frameCount, time = Time.time,
    playing = EditorApplication.isPlaying, server = Mirror.NetworkServer.active, client = Mirror.NetworkClient.active,
    batch = Application.isBatchMode, graphics = SystemInfo.graphicsDeviceType.ToString(),
    managerPresent = manager != null, scene = manager != null ? manager.gameObject.scene.path : null,
    usePrecaching = manager != null ? (bool?)manager.UsePrecaching : null,
    isPrecaching = manager != null ? (bool?)manager.IsPrecaching : null,
    containerChildCount = manager != null ? (int?)manager.transform.childCount : null,
    dynamicFieldPresent = field != null, dynamicDictionaryPresent = instances != null,
    dynamicCount = instances != null ? (int?)instances.Count : null,
    dynamicLive = instances != null ? (int?)live : null, sampleNames = names,
    monoBytes = GC.GetTotalMemory(false), unityAllocatedBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()
};
System.IO.Directory.CreateDirectory(Root);
string path = Root + "/precache-" + Label + "-" + DateTime.UtcNow.Ticks + ".json";
System.IO.File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(payload, Newtonsoft.Json.Formatting.Indented));
return new { executionCompleted = true, readOnly = true, reportPath = path, snapshot = payload };
