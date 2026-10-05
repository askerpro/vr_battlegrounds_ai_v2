if (UnityEditor.EditorApplication.isPlaying || UnityEditor.EditorApplication.isCompiling || Mirror.NetworkServer.active)
    throw new System.InvalidOperationException("Root probe требует idle Editor без server/Play.");
var previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
scene.name = "MapRootProbe_" + System.Guid.NewGuid().ToString("N");
var created = new System.Collections.Generic.List<UnityEngine.Object>();
var checks = new System.Collections.Generic.List<object>();
int failures = 0;
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
System.Func<string, UnityEngine.GameObject> make = name => {
    var obj = new UnityEngine.GameObject(name); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, scene); created.Add(obj); return obj;
};
System.Action<string, System.Action> check = (name, assertion) => {
    try { assertion(); checks.Add(new {name, passed = true}); }
    catch (System.Exception error) { failures++; checks.Add(new {name, passed = false, error = error.Message}); }
};
System.Action<bool, string> require = (value, reason) => { if (!value) throw new System.InvalidOperationException(reason); };
try
{
    var root = make("MapRoot").AddComponent<VrBattlegrounds.Maps.Runtime.MapRoot>();
    var map = UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.Maps.MapData>(); created.Add(map);
    map.sceneName = scene.name; map.kind = VrBattlegrounds.Maps.Runtime.MapRunKind.Debug;
    map.debugExemptions = VrBattlegrounds.Maps.Runtime.MapDebugExemptions.Calibration |
        VrBattlegrounds.Maps.Runtime.MapDebugExemptions.Stations | VrBattlegrounds.Maps.Runtime.MapDebugExemptions.TeamZones;
    var environment = make("Environment"); var gameplay = make("Gameplay");
    typeof(VrBattlegrounds.Maps.Runtime.MapRoot).GetField("_map", flags).SetValue(root, map);
    typeof(VrBattlegrounds.Maps.Runtime.MapRoot).GetField("_environment", flags).SetValue(root, environment);
    typeof(VrBattlegrounds.Maps.Runtime.MapRoot).GetField("_gameplay", flags).SetValue(root, gameplay);
    check("ExplicitDebugExemptionsResolve", () => require(root.ValidateBindings().Passed, string.Join(",", root.ValidateBindings().Errors)));
    check("MissingMapFailsBeforeWriter", () => {
        typeof(VrBattlegrounds.Maps.Runtime.MapRoot).GetField("_map", flags).SetValue(root, null);
        var result = root.ValidateBindings(); require(!result.Passed && result.Bindings == null && result.Errors.Contains("MapData.Missing"), "missing map accepted");
        typeof(VrBattlegrounds.Maps.Runtime.MapRoot).GetField("_map", flags).SetValue(root, map);
    });
    check("CombatCannotUseDebugExemptions", () => {
        map.kind = VrBattlegrounds.Maps.Runtime.MapRunKind.Combat;
        require(!root.ValidateBindings().Passed, "combat exemption accepted"); map.kind = VrBattlegrounds.Maps.Runtime.MapRunKind.Debug;
    });
    check("GameplayEnvironmentCannotOverlap", () => {
        typeof(VrBattlegrounds.Maps.Runtime.MapRoot).GetField("_environment", flags).SetValue(root, gameplay);
        require(root.ValidateBindings().Errors.Contains("Map.Groups.Overlap"), "overlap accepted");
        typeof(VrBattlegrounds.Maps.Runtime.MapRoot).GetField("_environment", flags).SetValue(root, environment);
    });
    check("DuplicateRootsFail", () => {
        var duplicate = make("Duplicate").AddComponent<VrBattlegrounds.Maps.Runtime.MapRoot>();
        require(root.ValidateBindings().Errors.Contains("MapRoot.Count"), "duplicate accepted"); UnityEngine.Object.DestroyImmediate(duplicate.gameObject);
    });
    check("GroupScaleAndInactiveRootsFail", () => {
        gameplay.transform.localScale = new UnityEngine.Vector3(2,1,1);
        require(!root.ValidateBindings().Passed, "scaled gameplay accepted"); gameplay.transform.localScale = UnityEngine.Vector3.one;
        root.gameObject.SetActive(false); require(!root.ValidateBindings().Passed, "inactive root accepted"); root.gameObject.SetActive(true);
    });
    var zone = make("Zone").AddComponent<VrBattlegrounds.Maps.TeamSpawnZone>(); zone.transform.SetParent(gameplay.transform, false);
    typeof(VrBattlegrounds.Maps.Runtime.MapRoot).GetField("_zones", flags).SetValue(root, new[] {zone});
    var stationRoot = make("Station"); stationRoot.transform.SetParent(gameplay.transform, false);
    var ni = stationRoot.AddComponent<Mirror.NetworkIdentity>(); ni.sceneId = 34567;
    var wall = stationRoot.AddComponent<VrBattlegrounds.Arsenal.ArsenalWallController>();
    var binding = stationRoot.AddComponent<VrBattlegrounds.Arsenal.ArsenalStationPresetBinding>();
    var anchor = stationRoot.AddComponent<VrBattlegrounds.Arsenal.ArsenalStationAnchor>();
    var poses = stationRoot.AddComponent<VrBattlegrounds.Arsenal.ArsenalEquipmentPoses>();
    var composition = stationRoot.AddComponent<VrBattlegrounds.Arsenal.ArsenalStationCompositionBinding>();
    var standing = make("StandingPoint"); standing.transform.SetParent(stationRoot.transform, false);
    var facing = make("ArenaFacing"); facing.transform.SetParent(stationRoot.transform, false);
    anchor.Configure(wall, zone, standing.transform, facing.transform);
    var fields = new System.Collections.Generic.Dictionary<string, object> {
        {"_stationKey", "probe-station"}, {"_controller", wall}, {"_authoredBinding", binding},
        {"_stationAnchor", anchor}, {"_equipmentPoses", poses}, {"_stationIdentity", ni} };
    foreach (var pair in fields) typeof(VrBattlegrounds.Arsenal.ArsenalStationCompositionBinding).GetField(pair.Key, flags).SetValue(composition, pair.Value);
    var stationRefs = new[] {composition};
    typeof(VrBattlegrounds.Maps.Runtime.MapRoot).GetField("_stations", flags).SetValue(root, stationRefs);
    check("AuthoredStationReferencesResolve", () => require(root.ValidateBindings().Passed, string.Join(",",root.ValidateBindings().Errors)));
    check("AuthoredStationWorldPlacementIsPreserved", () => {
        stationRoot.transform.position = new UnityEngine.Vector3(4,0,2);
        try { var result = root.ValidateBindings(); require(result.Passed && stationRoot.transform.position == new UnityEngine.Vector3(4,0,2), string.Join(",",result.Errors)); }
        finally { stationRoot.transform.position = UnityEngine.Vector3.zero; }
    });
    check("BindingCollectionsDoNotAliasAndKeyIsNotWritten", () => {
        var result = root.ValidateBindings(); require(result.Passed, "fixture invalid");
        stationRefs[0] = null; require(result.Bindings.Stations[0] == composition && composition.StationKey == "probe-station", "binding alias/key changed"); stationRefs[0] = composition;
    });
    check("StationCannotBeChildOfScaledZone", () => {
        stationRoot.transform.SetParent(zone.transform, false);
        require(root.ValidateBindings().Errors.Contains("Station.Zone:probe-station"), "station under zone accepted"); stationRoot.transform.SetParent(gameplay.transform, false);
    });
    check("GeneratedPathIsClosed", () => {
        typeof(VrBattlegrounds.Arsenal.ArsenalStationCompositionBinding).GetField("_mode",flags).SetValue(composition,VrBattlegrounds.Arsenal.ArsenalCompositionMode.Generated);
        require(root.ValidateBindings().Errors.Contains("Station.Generated.NotAdmitted:probe-station"),"generated accepted");
        typeof(VrBattlegrounds.Arsenal.ArsenalStationCompositionBinding).GetField("_mode",flags).SetValue(composition,VrBattlegrounds.Arsenal.ArsenalCompositionMode.Authored);
    });
    check("MissingControllerFailsNamed", () => {
        typeof(VrBattlegrounds.Arsenal.ArsenalStationCompositionBinding).GetField("_controller",flags).SetValue(composition,null);
        require(!root.ValidateBindings().Passed,"missing controller accepted");
        typeof(VrBattlegrounds.Arsenal.ArsenalStationCompositionBinding).GetField("_controller",flags).SetValue(composition,wall);
    });
    check("UnknownStationSceneIdentityFails", () => { ni.sceneId = 0; require(!root.ValidateBindings().Passed,"zero sceneId accepted"); ni.sceneId=34567; });
    check("MissingCatalogDependenciesFailNamed", () => {
        var catalog = UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.Maps.Runtime.MapRuntimeCatalog>(); created.Add(catalog);
        var result = catalog.Validate(null); require(!result.Passed && result.Description == null && result.Errors.Contains("Catalog.SpawnRegistry.Missing"),"missing catalog accepted");
    });
}
finally
{
    for (int i = created.Count-1; i>=0; i--) if (created[i] != null) UnityEngine.Object.DestroyImmediate(created[i]);
    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
    if (previousScene.IsValid() && previousScene.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousScene);
}
string path = "Docs/tasks/report/map-runtime-bootstrap/root-preflight-latest.json";
System.IO.File.WriteAllText(path,Newtonsoft.Json.JsonConvert.SerializeObject(new {passed = failures==0, failureCount=failures, checks,
    limits="Native read-only authoring fixture. No mode/stock/avatar, runtime UXR registration or gameplay readiness. Fixture sceneId tests metadata only."},Newtonsoft.Json.Formatting.Indented));
return new {passed = failures==0, failureCount=failures, checkCount=checks.Count, reportPath=path};
