// Подготовленный execute_code: только статические preview-копии, без изменения исходных ассетов.
// Сохранённые requests сохраняют 64-bit fileID; данные читаются напрямую Newtonsoft в C#.
var beforePreview = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
var beforeScenes = UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
var beforeDirty = beforeScenes.Select(s=>UnityEngine.SceneManagement.SceneManager.GetSceneByPath(s.path).isDirty).ToArray();
var requests = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<VrBattlegrounds.Editor.HandPoseReview.HandPoseFitRequest>>(
    System.IO.File.ReadAllText("Temp/HandPoseFit/sdk-reference-library/requests.json"),
    VrBattlegrounds.Editor.HandPoseReview.FitUnityJsonConverter.Settings);
var selected = requests.Where(r => r.OutputDirectory.EndsWith("bighands-Battery-p0-Right") || r.OutputDirectory.EndsWith("cyborg-LaserGrip-p0-Right")).ToArray();
var results = new System.Collections.Generic.List<object>();
bool passed = selected.Length == 2;
foreach (var request in selected)
{
    string sourceCase = System.IO.Path.GetFileName(request.OutputDirectory);
    request.OutputDirectory = "Temp/HandPoseFit/quality-2026-10-05/fresh-sdk/" + sourceCase;
    var report = VrBattlegrounds.Editor.HandPoseReview.HandPoseFitAnalyzer.Analyze(request);
    bool valid = report.Error == null && report.HandTriangles > 0 && report.ObjectTriangles > 0 && report.Cameras.Count == 24 && report.Cameras.All(c=>System.IO.File.Exists(System.IO.Path.Combine(report.OutputDirectory,c.Image))) && !report.LocalAnalysisVolumeApplied;
    passed = passed && valid;
    results.Add(new { caseId = sourceCase, passed = valid, reportPath = report.JsonPath, version = report.AlgorithmVersion,
        handTriangles = report.HandTriangles, objectTriangles = report.ObjectTriangles, pngCount = report.Cameras.Count,
        localApplied = report.LocalAnalysisVolumeApplied, audit = report.LocalVolumeReviewAudit,
        thumbNear = report.Zones.Where(z=>z.Zone=="thumb").Sum(z=>z.SampledAreaMm2*z.NearSurfaceFraction), error = report.Error });
}
var afterScenes = UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
bool scenesSame = beforeScenes.Length == afterScenes.Length && beforeScenes.Zip(afterScenes,(a,b)=>a.path==b.path&&a.isLoaded==b.isLoaded&&a.isActive==b.isActive).All(x=>x);
bool dirtySame = beforeDirty.SequenceEqual(afterScenes.Select(s=>UnityEngine.SceneManagement.SceneManager.GetSceneByPath(s.path).isDirty));
bool cleanupPassed = beforePreview == UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount && scenesSame && dirtySame && !VrBattlegrounds.Editor.HandPoseReview.HandPoseFitRuntimeCapture.Busy;
passed = passed && cleanupPassed;
var output = new { passed, results, cleanup = new { passed = cleanupPassed, previewBefore = beforePreview, previewAfter = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount, scenesSame, dirtySame, queueBusy = VrBattlegrounds.Editor.HandPoseReview.HandPoseFitRuntimeCapture.Busy } };
string path = "tmp/HandPoseFitQuality20261005/fresh-sdk-probe.json";
System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
System.IO.File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(output, VrBattlegrounds.Editor.HandPoseReview.FitUnityJsonConverter.Settings));
return new { passed, cases = results.Count, reportPath = path, cleanupPassed };
