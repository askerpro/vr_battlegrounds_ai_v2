from pathlib import Path

base = Path('Tools/Probes')
setup = (base / 'arsenal-authoring-roundtrip-setup.cs.txt').read_text(encoding='utf-8-sig')
test = (base / 'arsenal-authoring-roundtrip.cs.txt').read_text(encoding='utf-8-sig')
# Подготовка text-only пакета. Один внешний coordinator lock удерживается от setup до exact cleanup.
setup = setup[:setup.index('var own=default')]
setup = setup.replace('var active=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;', 'var originalActive=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;')
setup = setup.replace('var selection=UnityEditor.Selection.instanceIDs;', 'var originalSelection=UnityEditor.Selection.instanceIDs;')
body = test[test.index(' var originalStyle='):test.index(' // Неверные marker/path')]
body = body.replace(' require();', ' ')
body = body.replace('string saved=disk(clonePath);', 'string saved=disk(clonePath);')
body += '''
 var defaultPose=first.Card.localPosition;
 VrBattlegrounds.Editor.Arsenal.ArsenalLayoutAuthoring.BakeCardDefaultPrepared(stand,firstIndex);
 check(clone.Exceptions.Count(e=>e.OverrideCard)==0,"Explicit card zone default avoids20 exact copies");
 check(stand.Slots.Where(s=>s.Zone==first.Zone).All(s=>(s.Card.localPosition-defaultPose).sqrMagnitude<1e-12f),"All cards of actual zone use new canonical default");
 check(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(own,scenePath),"Own projection saved for reload proof");
 UnityEditor.SceneManagement.EditorSceneManager.CloseScene(own,true);
 own=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath,UnityEditor.SceneManagement.OpenSceneMode.Additive);
 stand=own.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<VrBattlegrounds.Editor.Arsenal.ArsenalLayoutAuthoringStand>(true)).Single();
 VrBattlegrounds.Editor.Arsenal.ArsenalLayoutAuthoring.ValidateStand(stand);
 check(stand.Style==clone&&stand.Slots.Count==20,"Reload preserves source Style/20 keys and handles");
 var loaded=stand.Slots[firstIndex];
 check((loaded.Item.localPosition-expectedItem).sqrMagnitude<1e-12f&&(loaded.Magazine.localPosition-expectedMag).sqrMagnitude<1e-12f&&(loaded.Card.localPosition-expectedCard).sqrMagnitude<1e-12f&&(loaded.Supports[0].Handle.localPosition-expectedSupport).sqrMagnitude<1e-12f,"Reload preserves all four saved target types");
 saved=disk(clonePath);VrBattlegrounds.Editor.Arsenal.ArsenalLayoutAuthoring.BakePrepared(stand);check(saved==disk(clonePath),"Bake after scene load has byte-identical Style");
'''
header = '''
var own=default(UnityEngine.SceneManagement.Scene);VrBattlegrounds.Editor.Arsenal.ArsenalLayoutAuthoringStand stand=null;
VrBattlegrounds.Arsenal.ArsenalPresentationStyle clone=null;string createdGuid=null,error=null;
var checks=new System.Collections.Generic.List<object>();var failures=new System.Collections.Generic.List<string>();var readback=new System.Collections.Generic.List<object>();
System.Action<bool,string> check=(pass,name)=>{checks.Add(new{name,pass});if(!pass)failures.Add(name);};
System.Action<Action,string> refusal=(action,name)=>{try{action();check(false,name);}catch(Exception ex){check(ex.GetBaseException().Message.Contains("несохранённые ассеты"),name);}};
System.Func<string,string> disk=p=>Convert.ToBase64String(System.IO.File.ReadAllBytes(p));
System.Func<UnityEngine.Vector3,float[]> vector=v=>new[]{v.x,v.y,v.z};
try {
 own=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath,UnityEditor.SceneManagement.OpenSceneMode.Additive);
 stand=own.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<VrBattlegrounds.Editor.Arsenal.ArsenalLayoutAuthoringStand>(true)).Single();
 VrBattlegrounds.Editor.Arsenal.ArsenalLayoutAuthoring.ValidateStand(stand);
 clone=UnityEngine.Object.Instantiate(style);clone.name="AuthoringRoundtripStyle";UnityEditor.AssetDatabase.CreateAsset(clone,clonePath);createdGuid=UnityEditor.AssetDatabase.AssetPathToGUID(clonePath);UnityEditor.AssetDatabase.SaveAssetIfDirty(clone);
 var input=new UnityEditor.SerializedObject(stand);input.FindProperty("_style").objectReferenceValue=clone;input.ApplyModifiedPropertiesWithoutUndo();
 VrBattlegrounds.Editor.Arsenal.ArsenalLayoutAuthoring.RestorePrepared(stand,true);UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(own);
'''
cleanup = '''
} catch(Exception ex) {error=ex.ToString();failures.Add("Unhandled backend fixture error");}
finally {
 try {
  if(own.IsValid()&&own.isLoaded&&stand!=null) {
   var input=new UnityEditor.SerializedObject(stand);input.FindProperty("_style").objectReferenceValue=style;input.ApplyModifiedPropertiesWithoutUndo();
   VrBattlegrounds.Editor.Arsenal.ArsenalLayoutAuthoring.RestorePrepared(stand,true);UnityEditor.SceneManagement.EditorSceneManager.SaveScene(own,scenePath);UnityEditor.SceneManagement.EditorSceneManager.CloseScene(own,true);
  }
  if(createdGuid!=null&&UnityEditor.AssetDatabase.AssetPathToGUID(clonePath)==createdGuid)UnityEditor.AssetDatabase.DeleteAsset(clonePath);
  if(clone!=null&&!UnityEditor.EditorUtility.IsPersistent(clone))UnityEngine.Object.DestroyImmediate(clone);
  var original=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(originalActive);if(original.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);UnityEditor.Selection.instanceIDs=originalSelection;
 }catch(Exception cleanupError){failures.Add("Cleanup: "+cleanupError.Message);}
}
foreach(var row in assets) {
 var p=row.path;var a=UnityEditor.AssetDatabase.LoadMainAssetAtPath(p);
 check(UnityEditor.EditorJsonUtility.ToJson(a)==row.json&&UnityEditor.EditorUtility.IsDirty(a)==row.dirty&&disk(p)==row.disk&&System.IO.File.ReadAllText(p+".meta")==row.meta,"Foreign native/disk preserved "+p);
}
var originalSnapshots=Newtonsoft.Json.Linq.JArray.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(scenes));
foreach(var before in originalSnapshots) {
 var s=UnityEngine.SceneManagement.SceneManager.GetSceneByPath((string)before["path"]);var current=s.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<UnityEngine.Component>(true)).Where(c=>c!=null).ToDictionary(c=>c.GetInstanceID(),c=>UnityEditor.EditorJsonUtility.ToJson(c));
 check(s.isLoaded&&s.isDirty==(bool)before["dirty"]&&current.Count==before["rows"].Count()&&before["rows"].All(r=>current.ContainsKey((int)r["id"])&&current[(int)r["id"]]==(string)r["json"]),"Foreign scene preserved "+s.path);
}
check(!System.IO.File.Exists(clonePath)&&!System.IO.File.Exists(clonePath+".meta")&&string.IsNullOrEmpty(UnityEditor.AssetDatabase.AssetPathToGUID(clonePath)),"Exact-owned clone asset/meta cleanup");
check(!UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath).isLoaded,"Own scene closed and original setup restored");
var report=new{checks,failures,error,readback};System.IO.File.WriteAllText("tmp/arsenal-visual-stage/authoring-backend-roundtrip.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
return new{checks=checks.Count,failures=failures.Count,error,failed=failures.Take(5).ToArray()};
'''
(base / 'arsenal-authoring-backend-roundtrip.cs.txt').write_text(setup + header + body + cleanup, encoding='utf-8')
print('Prepared single-external-lease backend roundtrip with cleanup before release.')
