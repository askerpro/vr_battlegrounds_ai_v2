if (UnityEditor.EditorApplication.isPlaying || UnityEditor.EditorApplication.isCompiling || Mirror.NetworkServer.active)
    throw new System.InvalidOperationException("Adversarial probe requires idle Editor.");
var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Additive);
scene.name = "MapAdversarial_" + System.Guid.NewGuid().ToString("N");
string folder = "Assets/Editor/VR_Battlegrounds/Maps/__Probe_" + System.Guid.NewGuid().ToString("N");
if (UnityEditor.AssetDatabase.IsValidFolder(folder)) throw new System.InvalidOperationException("Own fixture path already exists.");
string ownFolderGuid = UnityEditor.AssetDatabase.CreateFolder("Assets/Editor/VR_Battlegrounds/Maps",System.IO.Path.GetFileName(folder));
if (string.IsNullOrEmpty(ownFolderGuid)) throw new System.InvalidOperationException("Own fixture folder creation failed.");
var created = new System.Collections.Generic.List<UnityEngine.Object>();
var checks = new System.Collections.Generic.List<object>(); int failures=0;
var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
System.Action<string,System.Action> check=(name,assertion)=>{try {assertion(); checks.Add(new{name,passed=true});} catch(System.Exception error){failures++;checks.Add(new{name,passed=false,error=error.Message});}};
System.Action<bool,string> require=(value,reason)=>{if(!value)throw new System.InvalidOperationException(reason);};
System.Func<string,UnityEngine.GameObject> make=name=>{var obj=new UnityEngine.GameObject(name);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj,scene);created.Add(obj);return obj;};
try
{
    var prefabAssets=new System.Collections.Generic.List<UnityEngine.GameObject>();
    foreach(string role in new[]{"Referee","Coordinator","Mode","Other"})
    {
        var obj=make(role);obj.AddComponent<Mirror.NetworkIdentity>();
        if(role=="Referee")obj.AddComponent<VrBattlegrounds.Managers.MapReferee>();
        if(role=="Coordinator")obj.AddComponent<VrBattlegrounds.Arsenal.ArsenalBoundaryWall>();
        if(role=="Mode")obj.AddComponent<VrBattlegrounds.GameModes.WarmupMode>();
        var asset=UnityEditor.PrefabUtility.SaveAsPrefabAsset(obj,folder+"/"+role+".prefab");
        require(asset!=null,"Fixture prefab save failed");prefabAssets.Add(asset);UnityEngine.Object.DestroyImmediate(obj);
    }
    var registered=prefabAssets.GetRange(0,3);
    var catalog=UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.Maps.Runtime.MapRuntimeCatalog>();created.Add(catalog);
    var registry=UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.Maps.MapRegistry>();created.Add(registry);
    var modeRegistry=UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.GameModes.GameModeRegistry>();created.Add(modeRegistry);
    var preset=UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.Arsenal.ArsenalPreset>();created.Add(preset);
    typeof(VrBattlegrounds.Arsenal.ArsenalPreset).GetField("_presetId",flags).SetValue(preset,"fixture-arsenal");
    var team=UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.TeamData>();created.Add(team);team.teamIndex=1;
    var warmup=UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.GameModes.GameModeData>();created.Add(warmup);warmup.modeId="warmup";warmup.modePrefab=prefabAssets[2];
    var match=UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.GameModes.GameModeData>();created.Add(match);match.modeId="mode-a";match.modePrefab=prefabAssets[2];match.teams=new[]{team};
    modeRegistry.warmup=warmup;modeRegistry.modes=new[]{match};
    var map=UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.Maps.MapData>();created.Add(map);map.sceneName=scene.name;map.kind=VrBattlegrounds.Maps.Runtime.MapRunKind.Combat;map.arsenalPreset=preset;map.supportedModes=new[]{match};registry.maps=new[]{map};
    var entry=new VrBattlegrounds.Maps.Runtime.MapRuntimeCatalog.ContentEntry{Map=map,ScenePath="Assets/MissingProbeScene.unity",ContentFingerprint="fixture-content",ArsenalFingerprint="fixture-arsenal-hash"};
    var fields=new System.Collections.Generic.Dictionary<string,object>{{"_maps",registry},{"_modes",modeRegistry},{"_refereePrefab",prefabAssets[0].GetComponent<VrBattlegrounds.Managers.MapReferee>()},{"_coordinatorPrefab",prefabAssets[1].GetComponent<VrBattlegrounds.Arsenal.ArsenalBoundaryWall>()},{"_content",new[]{entry}}};
    foreach(var pair in fields)typeof(VrBattlegrounds.Maps.Runtime.MapRuntimeCatalog).GetField(pair.Key,flags).SetValue(catalog,pair.Value);
    require(catalog.Validate(registered).Passed,string.Join(",",catalog.Validate(registered).Errors));
    var root=make("MapRoot").AddComponent<VrBattlegrounds.Maps.Runtime.MapRoot>();var environment=make("Environment");var gameplay=make("Gameplay");
    var alias=UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.Maps.MapData>();created.Add(alias);alias.sceneName=scene.name;alias.kind=VrBattlegrounds.Maps.Runtime.MapRunKind.Debug;
    alias.debugExemptions=VrBattlegrounds.Maps.Runtime.MapDebugExemptions.Calibration|VrBattlegrounds.Maps.Runtime.MapDebugExemptions.Stations|VrBattlegrounds.Maps.Runtime.MapDebugExemptions.TeamZones;
    typeof(VrBattlegrounds.Maps.Runtime.MapRoot).GetField("_map",flags).SetValue(root,alias);
    typeof(VrBattlegrounds.Maps.Runtime.MapRoot).GetField("_environment",flags).SetValue(root,environment);
    typeof(VrBattlegrounds.Maps.Runtime.MapRoot).GetField("_gameplay",flags).SetValue(root,gameplay);
    check("DifferentPassportCannotBypassCombatBindings",()=>{
        var bindings=root.ValidateBindings();require(bindings.Passed,"alias fixture invalid");
        var result=catalog.Resolve(new VrBattlegrounds.Maps.Runtime.MapRunRequest(new VrBattlegrounds.Maps.Runtime.MapRunKey(System.Guid.NewGuid(),1),scene.name,"mode-a","fixture-content"),bindings.Bindings,registered);
        require(!result.Passed,"Debug alias published Combat config without required bindings");
    });
    check("ForeignModeAssetWithSameIdIsRejected",()=>{
        var foreign=UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.GameModes.GameModeData>();created.Add(foreign);foreign.modeId="mode-a";
        map.supportedModes=new[]{foreign};
        try {require(!catalog.Validate(registered).Passed,"foreign mode asset accepted by ID");} finally {map.supportedModes=new[]{match};}
    });
    check("RequiredSceneIdentityCannotBePrefab",()=>{
        var ni=prefabAssets[0].GetComponent<Mirror.NetworkIdentity>();ulong original=ni.sceneId;ni.sceneId=12345;
        try {require(!catalog.Validate(registered).Passed,"nonzero sceneId accepted as prefab");} finally {ni.sceneId=original;}
    });
    check("UnrequiredRegistryPrefabCannotReplaceRequiredAssetId",()=>{
        var ni=prefabAssets[3].GetComponent<Mirror.NetworkIdentity>();uint original=ni.assetId;
        var assetField=typeof(Mirror.NetworkIdentity).GetField("_assetId",flags);
        assetField.SetValue(ni,prefabAssets[0].GetComponent<Mirror.NetworkIdentity>().assetId);registered.Add(prefabAssets[3]);
        try {require(!catalog.Validate(registered).Passed,"extra spawn entry shadows referee assetId");} finally {registered.Remove(prefabAssets[3]);assetField.SetValue(ni,original);}
    });
    check("WholeSceneIdentityCollisionsAreRejected",()=>{
        var a=make("IdentityA");a.transform.SetParent(gameplay.transform,false);var ia=a.AddComponent<Mirror.NetworkIdentity>();ia.sceneId=54321;
        var b=make("IdentityB");b.transform.SetParent(gameplay.transform,false);var ib=b.AddComponent<Mirror.NetworkIdentity>();ib.sceneId=54321;
        try {require(!root.ValidateBindings().Passed,"nonstation sceneId collision accepted");} finally {UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);}
    });
    check("MissingHashSourcesProduceReportInsteadOfThrow",()=>{
        object report=VrBattlegrounds.EditorTools.MapRunPreflight.Validate(catalog,registered);
        var data=Newtonsoft.Json.Linq.JObject.FromObject(report);
        require(data.Value<bool>("passed")==false && data.Value<int>("failureCount")>0,"missing source preflight not failed");
    });
}
finally
{
    for(int i=created.Count-1;i>=0;i--)if(created[i]!=null)UnityEngine.Object.DestroyImmediate(created[i]);
    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);
    if(previous.IsValid()&&previous.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
    // Только собственный уникальный временный каталог, никогда production assets.
    if (!folder.StartsWith("Assets/Editor/VR_Battlegrounds/Maps/__Probe_",System.StringComparison.Ordinal) ||
        UnityEditor.AssetDatabase.AssetPathToGUID(folder) != ownFolderGuid) throw new System.InvalidOperationException("Cleanup ownership mismatch.");
    UnityEditor.AssetDatabase.DeleteAsset(folder);
}
string reportPath="Docs/tasks/report/map-runtime-bootstrap/catalog-adversarial-latest.json";
System.IO.File.WriteAllText(reportPath,Newtonsoft.Json.JsonConvert.SerializeObject(new{passed=failures==0,failureCount=failures,checks,limits="Native temporary metadata prefab assets, no network spawn/gameplay. Own assets deleted."},Newtonsoft.Json.Formatting.Indented));
return new{passed=failures==0,failureCount=failures,checkCount=checks.Count,reportPath};
