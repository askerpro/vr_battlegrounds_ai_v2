// Временная программа execute_code. Это не permanent gameplay test и не доказательство доставки сети.
if (UnityEditor.EditorApplication.isPlaying || Mirror.NetworkServer.active || Mirror.NetworkClient.active)
    throw new System.InvalidOperationException("Baseline требует свободного Editor без сети/Play Mode.");
var rows = new System.Collections.Generic.List<object>();
var trace = new System.Collections.Generic.List<string>();
var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var instanceFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var readyField = typeof(VrBattlegrounds.Managers.ManagerBootstrap).GetField("<IsReady>k__BackingField", flags);
var handlersField = typeof(VrBattlegrounds.Managers.ManagerBootstrap).GetField("ReadyInternal", flags);
var playersField = typeof(VrBattlegrounds.Managers.PlayersManager).GetField("<Instance>k__BackingField", flags);
object oldReady = readyField.GetValue(null), oldHandlers = handlersField.GetValue(null), oldPlayers = playersField.GetValue(null);
try
{
    playersField.SetValue(null, null);
    readyField.SetValue(null, false);
    handlersField.SetValue(null, null);
    typeof(VrBattlegrounds.Managers.ManagerBootstrap).GetMethod("Verify", flags).Invoke(null, null);
    bool ready = VrBattlegrounds.Managers.ManagerBootstrap.IsReady;
    trace.Add("required PlayersManager отсутствует → Verify → IsReady=" + ready);
    rows.Add(new { name = "MissingRequiredDoesNotReady", passed = !ready, observed = "IsReady=" + ready, scope = "Настоящий Verify; statics восстановлены, subscribers не вызваны" });
}
finally
{
    readyField.SetValue(null, oldReady);
    handlersField.SetValue(null, oldHandlers);
    playersField.SetValue(null, oldPlayers);
}
var objects = new System.Collections.Generic.List<UnityEngine.GameObject>();
try
{
    var root = new UnityEngine.GameObject("MapBootstrapBaseline_Referee"); objects.Add(root);
    root.AddComponent<Mirror.NetworkIdentity>();
    var referee = root.AddComponent<VrBattlegrounds.Managers.MapReferee>();
    var currentRoot = new UnityEngine.GameObject("MapBootstrapBaseline_Current"); objects.Add(currentRoot);
    currentRoot.AddComponent<Mirror.NetworkIdentity>();
    var current = currentRoot.AddComponent<VrBattlegrounds.GameModes.WarmupMode>();
    var oldRoot = new UnityEngine.GameObject("MapBootstrapBaseline_Old"); objects.Add(oldRoot);
    oldRoot.AddComponent<Mirror.NetworkIdentity>();
    var old = oldRoot.AddComponent<VrBattlegrounds.GameModes.WarmupMode>();
    var register = typeof(VrBattlegrounds.Managers.MapReferee).GetMethod("RegisterActiveGameMode", instanceFlags);
    register.Invoke(referee, new object[] { current });
    register.Invoke(referee, new object[] { old });
    bool overwritten = referee.ActiveGameMode == old;
    trace.Add("Register(current) → Register(old) → active=" + referee.ActiveGameMode.name);
    rows.Add(new { name = "OldEpochAfterReload", passed = !overwritten, observed = "old object overwrote current=" + overwritten, scope = "Настоящий registration entry; transport/reload не прогонялись" });
}
finally { foreach (var obj in objects) UnityEngine.Object.DestroyImmediate(obj); }
foreach (string name in new[] { "MapRunConfig", "MapRunSnapshot", "MapRunResolver", "MapRunAuthority", "MapRunScope" })
{
    bool exists = typeof(VrBattlegrounds.Managers.MapReferee).Assembly.GetType("VrBattlegrounds.Maps.Runtime." + name) != null;
    rows.Add(new { name = "MissingContract:" + name, passed = exists, observed = exists ? "present" : "required contract absent", scope = "Именованный RED задачи 2; compiler noise отсутствует" });
}
string path = "Docs/tasks/report/map-runtime-bootstrap/baseline.json";
System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
System.IO.File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(new { date = System.DateTime.UtcNow, probes = rows, trace, deferred = new[] { "SceneRefillBeforeCallback", "CandidateModeEnableCleanup", "InitialStartWithoutSceneChange", "FutureSelectionAffectsGoLive" }, limitation = "Не вызывается global cleanup, не подменяется active сервер, не сохраняются сцены. Непрогнанные probes остаются открытыми." }, Newtonsoft.Json.Formatting.Indented));
return new { passed = false, reportPath = path, probeCount = rows.Count, deferredCount = 4 };
