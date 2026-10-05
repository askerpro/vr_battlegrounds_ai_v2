// Настоящие source entry points в socketless server; не remote delivery и не пользовательская сцена.
if (UnityEditor.EditorApplication.isPlaying || Mirror.NetworkServer.active || Mirror.NetworkClient.active)
    throw new System.InvalidOperationException("Lifecycle baseline требует свободного Editor без сети.");
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var statics = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var created = new System.Collections.Generic.List<UnityEngine.Object>();
var originals = new System.Collections.Generic.Dictionary<System.Reflection.FieldInfo, object>();
foreach (var type in new[] { typeof(VrBattlegrounds.Managers.SessionManager), typeof(VrBattlegrounds.Managers.Series), typeof(VrBattlegrounds.Managers.MapReferee) })
{
    var field = type.GetField("<Instance>k__BackingField", statics);
    originals.Add(field, field.GetValue(null));
}
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
var previousTransport = Mirror.Transport.active;
bool previousListen = Mirror.NetworkServer.listen;
var rows = new System.Collections.Generic.List<object>();
int sceneCallbacks = 0;
System.Action<string> sceneChanged = name => sceneCallbacks++;
VrBattlegrounds.Network.GameNetworkManager.ServerSceneChanged += sceneChanged;
System.Func<string, UnityEngine.GameObject> make = name => {
    var obj = new UnityEngine.GameObject("MapLifecycleProbe_" + name);
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, scene); created.Add(obj); return obj;
};
System.Action<UnityEngine.GameObject> identityAwake = obj => typeof(Mirror.NetworkIdentity).GetMethod("Awake", flags).Invoke(obj.GetComponent<Mirror.NetworkIdentity>(), null);
try
{
    var initialLoose = new System.Collections.Generic.List<UltimateXR.Manipulation.UxrGrabbableObject>();
    VrBattlegrounds.Interaction.LooseItems.CollectCandidates(initialLoose);
    if (initialLoose.Count != 0) throw new System.InvalidOperationException("Global loose items присутствуют: cleanup probe не допускается.");
    var transport = make("Transport").AddComponent<kcp2k.KcpTransport>();
    typeof(kcp2k.KcpTransport).GetMethod("Awake", flags).Invoke(transport, null);
    transport.enabled = false; Mirror.Transport.active = transport; Mirror.NetworkServer.listen = false; Mirror.NetworkServer.Listen(4);
    if (!Mirror.NetworkServer.active) throw new System.InvalidOperationException("Server не запущен.");

    var wallRoot = make("Wall"); wallRoot.AddComponent<Mirror.NetworkIdentity>();
    var wall = wallRoot.AddComponent<VrBattlegrounds.Arsenal.ArsenalWallController>();
    identityAwake(wallRoot); Mirror.NetworkServer.Spawn(wallRoot);
    bool attempted = (bool)typeof(VrBattlegrounds.Arsenal.ArsenalWallController).GetField("_presetInitialRefillDone", flags).GetValue(wall);
    rows.Add(new { name = "SceneRefillBeforeCallback", passed = !attempted, initialRefillAttempted = attempted, sceneCallbacks,
        limit = "Настоящий OnStartServer, пустая wall: ранняя попытка refill; stock creation отдельно в composition probe." });

    var itemRoot = make("LooseMagazine"); itemRoot.AddComponent<Mirror.NetworkIdentity>();
    var grab = itemRoot.AddComponent<UltimateXR.Manipulation.UxrGrabbableObject>();
    var mag = itemRoot.AddComponent<UltimateXR.Mechanics.Weapons.UxrFirearmMag>();
    // UXR registry нужен CollectCandidates; lifecycle вызывается только на собственном объекте.
    typeof(UltimateXR.Manipulation.UxrGrabbableObject).GetMethod("Awake", flags).Invoke(grab, null);
    typeof(UltimateXR.Mechanics.Weapons.UxrFirearmMag).GetMethod("Awake", flags).Invoke(mag, null);
    identityAwake(itemRoot); Mirror.NetworkServer.Spawn(itemRoot);
    var candidates = new System.Collections.Generic.List<UltimateXR.Manipulation.UxrGrabbableObject>();
    VrBattlegrounds.Interaction.LooseItems.CollectCandidates(candidates);
    if (candidates.Count != 1 || candidates[0] != grab || !VrBattlegrounds.Interaction.LooseItems.IsLoose(grab))
        throw new System.InvalidOperationException("Harness magazine не единственный loose candidate.");
    uint itemNetId = itemRoot.GetComponent<Mirror.NetworkIdentity>().netId;
    var cleanup = make("CandidateCleanup").AddComponent<VrBattlegrounds.GameModes.ModeStartCleanup>();
    typeof(VrBattlegrounds.GameModes.ModeStartCleanup).GetMethod("OnEnable", flags).Invoke(cleanup, null);
    // Mirror scene object при Destroy деактивируется и unspawn-ится, сохраняя оболочку для следующего run.
    bool removed = itemRoot == null || !itemRoot.activeSelf || !Mirror.NetworkServer.spawned.ContainsKey(itemNetId);
    rows.Add(new { name = "CandidateModeEnableCleanup", passed = !removed, removedOwnItem = removed, committed = false,
        limit = "Настоящий OnEnable/RemoveAll; global candidates проверены перед запуском и состояли только из fixture." });

    var sessionRoot = make("Session"); sessionRoot.AddComponent<Mirror.NetworkIdentity>();
    var session = sessionRoot.AddComponent<VrBattlegrounds.Managers.SessionManager>();
    var series = sessionRoot.AddComponent<VrBattlegrounds.Managers.Series>();
    var authority = sessionRoot.AddComponent<VrBattlegrounds.Maps.Runtime.MapRunAuthority>();
    var registry = UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.GameModes.GameModeRegistry>(); created.Add(registry);
    var mapRegistry = UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.Maps.MapRegistry>(); created.Add(mapRegistry);
    var map = UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.Maps.MapData>(); created.Add(map); map.sceneName = "ProbeMap";
    var team = UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.TeamData>(); created.Add(team); team.teamIndex = 1;
    var modes = new System.Collections.Generic.List<VrBattlegrounds.GameModes.GameModeData>();
    foreach (string id in new[] { "warmup", "mode-a", "mode-b" })
    {
        var mode = UnityEngine.ScriptableObject.CreateInstance<VrBattlegrounds.GameModes.GameModeData>(); created.Add(mode);
        mode.modeId = id; mode.teams = id == "warmup" ? System.Array.Empty<VrBattlegrounds.TeamData>() : new[] { team }; modes.Add(mode);
    }
    registry.warmup = modes[0]; registry.modes = new[] { modes[1], modes[2] }; map.supportedModes = registry.modes; mapRegistry.maps = new[] { map };
    typeof(VrBattlegrounds.Managers.SessionManager).GetField("_mapRegistry", flags).SetValue(session, mapRegistry);
    typeof(VrBattlegrounds.Managers.SessionManager).GetField("_gameModeRegistry", flags).SetValue(session, registry);
    typeof(VrBattlegrounds.Managers.SessionManager).GetField("<Instance>k__BackingField", statics).SetValue(null, session);
    typeof(VrBattlegrounds.Managers.Series).GetField("<Instance>k__BackingField", statics).SetValue(null, series);
    series.LoadMapOverride = name => { };
    identityAwake(sessionRoot); Mirror.NetworkServer.Spawn(sessionRoot);
    var refereeRoot = make("Referee"); refereeRoot.AddComponent<Mirror.NetworkIdentity>();
    var referee = refereeRoot.AddComponent<VrBattlegrounds.Managers.MapReferee>(); referee.SceneNameOverride = "ProbeMap";
    referee.ModeFactory = data => {
        var obj = make(data.modeId); obj.AddComponent<Mirror.NetworkIdentity>(); obj.AddComponent<VrBattlegrounds.GameModes.WarmupMode>(); identityAwake(obj); return obj;
    };
    identityAwake(refereeRoot); Mirror.NetworkServer.Spawn(refereeRoot);
    rows.Add(new { name = "InitialStartWithoutSceneChange", passed = authority.Current.Status != VrBattlegrounds.Maps.Runtime.MapBootstrapStatus.None,
        sceneCallbacks, descriptorStatus = authority.Current.Status.ToString(), legacyMode = referee.ActiveGameMode != null,
        limit = "Настоящий session/referee server lifecycle без SceneChanged; transport manager и configured SDK startup не проверены." });
    session.SetSession("ProbeMap", "mode-a");
    if (!series.ServerBegin(new[] { "ProbeMap" })) throw new System.InvalidOperationException("Series.ServerBegin refused fixture.");
    session.SetSession("ProbeMap", "mode-b");
    if (!referee.GoLive()) throw new System.InvalidOperationException("GoLive refused fixture.");
    string actualMode = referee.ActiveGameMode.ModeData.modeId;
    rows.Add(new { name = "FutureSelectionAffectsGoLive", passed = actualMode == "mode-a", capturedAtBegin = "mode-a", futureSelection = "mode-b", actualMode,
        limit = "Настоящие SetSession/ServerBegin/GoLive; factory создаёт минимальный WarmupMode для измерения выбора, не mode policies." });
}
finally
{
    VrBattlegrounds.Network.GameNetworkManager.ServerSceneChanged -= sceneChanged;
    if (Mirror.NetworkServer.active) Mirror.NetworkServer.Shutdown();
    for (int i = created.Count - 1; i >= 0; i--) if (created[i] != null) UnityEngine.Object.DestroyImmediate(created[i]);
    UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
    foreach (var pair in originals) pair.Key.SetValue(null, pair.Value);
    Mirror.Transport.active = previousTransport; Mirror.NetworkServer.listen = previousListen;
}
string path = "Docs/tasks/report/map-runtime-bootstrap/lifecycle-baseline.json";
System.IO.File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(new { checks = rows, actualServer = true, limit = "Isolated preview scene, no remote channel/Quest or saving user scene." }, Newtonsoft.Json.Formatting.Indented));
return new { passed = false, checkCount = rows.Count, reportPath = path };
