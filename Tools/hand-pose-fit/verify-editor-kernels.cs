// Выполнять через Unity execute_code под lease, только вне Play/compile. Без NUnit runner/scene save.
if(UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode||UnityEditor.EditorApplication.isCompiling||UnityEngine.Resources.FindObjectsOfTypeAll<UltimateXR.Editor.Manipulation.HandPoses.UxrHandPoseEditorWindow>().Length>0)
    return new{passed=false,executed=false,reason="Закройте SDK pose editor и завершите Play/compile."};
var assembly=System.AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name=="VrBattlegrounds.HandPoseReview.Tests");
if(assembly==null)return new{passed=false,executed=false,reason="Test assembly not loaded"};
var rows=new System.Collections.Generic.List<object>();var failures=new System.Collections.Generic.List<string>();
foreach(string name in new[]{"HandPoseFitGeometryTests","HandPoseFitVolumeQualityTests","SdkHandPreviewParityTests","SdkPreviewBindingTests"}) {
    var type=assembly.GetType("VrBattlegrounds.Editor.HandPoseReview.Tests."+name);var fixture=System.Activator.CreateInstance(type);
    foreach(var method in type.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.DeclaredOnly)) {
        var attributes=method.GetCustomAttributes(false);var cases=attributes.Where(a=>a.GetType().Name=="TestCaseAttribute").Select(a=>(object[])a.GetType().GetProperty("Arguments").GetValue(a)).ToArray();
        if(cases.Length==0&&attributes.Any(a=>a.GetType().Name=="TestAttribute"))cases=new[]{new object[0]};
        foreach(var args in cases) {
            var timer=System.Diagnostics.Stopwatch.StartNew();string error=null;
            try{method.Invoke(fixture,args);}catch(System.Exception e){error=(e.InnerException??e).ToString();failures.Add(name+"."+method.Name+": "+error);}
            timer.Stop();rows.Add(new{fixture=name,method=method.Name,arguments=args,passed=error==null,milliseconds=timer.Elapsed.TotalMilliseconds,error=error});
        }
    }
}
var gate=VrBattlegrounds.EditorTools.AndroidCompileGate.Run();
var report=new{passed=failures.Count==0&&rows.Count==63&&gate.Passed,total=rows.Count,failed=failures.Count,android=gate,rows=rows,failures=failures,utc=System.DateTime.UtcNow.ToString("o"),scope="Direct native NUnit methods; not full runner/UI FPS/game IK/Quest acceptance"};
string folder="Docs/tasks/report/hand-pose-fit-2026-10-05";System.IO.Directory.CreateDirectory(folder);
string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(folder,"checkpoint-verification.json"));
System.IO.File.WriteAllText(path,Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
return new{report.passed,report.total,report.failed,androidPassed=gate.Passed,examples=failures.Take(5).ToArray(),reportPath=path};
