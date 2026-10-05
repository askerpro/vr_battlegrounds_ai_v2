// Один и тот же temporal probe до и после исправления; настоящий socketless Mirror server.
if (UnityEditor.EditorApplication.isPlaying || Mirror.NetworkServer.active || Mirror.NetworkClient.active)
    throw new System.InvalidOperationException("Probe требует свободного Editor без сети/Play Mode.");
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var created = new System.Collections.Generic.List<UnityEngine.GameObject>();
var previousTransport = Mirror.Transport.active;
bool previousListen = Mirror.NetworkServer.listen;
var rows = new System.Collections.Generic.List<object>();
int failed = 0;
try
{
    var transportObject = new UnityEngine.GameObject("MapRunTeardownTransport"); created.Add(transportObject);
    var transport = transportObject.AddComponent<kcp2k.KcpTransport>();
    typeof(kcp2k.KcpTransport).GetMethod("Awake", flags).Invoke(transport, null);
    transport.enabled = false; Mirror.Transport.active = transport; Mirror.NetworkServer.listen = false;
    Mirror.NetworkServer.Listen(4);
    foreach (bool stopServer in new[] { false, true })
    {
        var root = new UnityEngine.GameObject("MapRunTeardownAuthority"); created.Add(root);
        var authority = root.AddComponent<VrBattlegrounds.Maps.Runtime.MapRunAuthority>();
        typeof(Mirror.NetworkIdentity).GetMethod("Awake", flags).Invoke(root.GetComponent<Mirror.NetworkIdentity>(), null);
        Mirror.NetworkServer.Spawn(root);
        var nextKey = typeof(VrBattlegrounds.Maps.Runtime.MapRunAuthority).GetProperty("NextKey", flags);
        var begin = typeof(VrBattlegrounds.Maps.Runtime.MapRunAuthority).GetMethod("BeginRun", flags);
        var catalog = new VrBattlegrounds.Maps.Runtime.MapRunResolverCatalog("warmup", new[] { "mode-a" }, new[] {
            new VrBattlegrounds.Maps.Runtime.MapRunMapDescription("ProbeMap", VrBattlegrounds.Maps.Runtime.MapRunKind.Combat,
                "content-v1", 1, "arsenal", "arsenal-v1", new[] { "mode-a" }) });
        var bindings = new VrBattlegrounds.Maps.Runtime.MapRunBindings("ProbeMap", System.Array.Empty<VrBattlegrounds.Maps.Runtime.MapStationConfig>());
        System.Func<object[]> request = () => new object[] {
            VrBattlegrounds.Maps.Runtime.MapRunResolver.Resolve(new VrBattlegrounds.Maps.Runtime.MapRunRequest(
                (VrBattlegrounds.Maps.Runtime.MapRunKey)nextKey.GetValue(authority), "ProbeMap", "mode-a", "content-v1"), catalog, bindings), null };
        var args = request();
        if (!(bool)begin.Invoke(authority, args)) throw new System.InvalidOperationException("Initial begin refused");
        var scope = (VrBattlegrounds.Maps.Runtime.MapRunScope)args[1];
        int callbackCount = 0;
        bool resurrected = false;
        System.Action attempt = () => { callbackCount++; resurrected |= (bool)begin.Invoke(authority, request()); };
        scope.Cancellation.Register(attempt);
        scope.Own(attempt);
        if (stopServer) Mirror.NetworkServer.UnSpawn(root);
        else typeof(VrBattlegrounds.Maps.Runtime.MapRunAuthority).GetMethod("Retire", flags).Invoke(authority,
            new object[] { scope, authority.Current.Revision });
        bool passed = callbackCount == 2 && !resurrected && scope.IsDisposed &&
            authority.Current.Status == VrBattlegrounds.Maps.Runtime.MapBootstrapStatus.Retiring;
        if (!passed) failed++;
        rows.Add(new { name = stopServer ? "ServerStopCannotResurrectRun" : "RetireCannotStartRunDuringTeardown", passed, callbackCount, resurrected,
            currentSequence = authority.Current.Key.LoadSequence });
    }
}
finally
{
    if (Mirror.NetworkServer.active) Mirror.NetworkServer.Shutdown();
    foreach (var obj in created) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
    Mirror.Transport.active = previousTransport; Mirror.NetworkServer.listen = previousListen;
}
string path = "Docs/tasks/report/map-runtime-bootstrap/teardown-latest.json";
System.IO.File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(new {passed = failed == 0, failureCount = failed, checks = rows}, Newtonsoft.Json.Formatting.Indented));
return new {passed = failed == 0, failureCount = failed, reportPath = path};
