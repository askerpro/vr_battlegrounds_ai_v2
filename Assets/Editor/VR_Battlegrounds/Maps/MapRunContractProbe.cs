using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Временный probe задачи 2. Не permanent тест; не сохраняет сцены или assets.</summary>
    public static class MapRunContractProbe
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static object Run()
        {
            if (EditorApplication.isPlaying || NetworkServer.active || NetworkClient.active)
                throw new InvalidOperationException("Probe требует свободного Editor без Play/сети.");
            var checks = new List<object>();
            var trace = new List<string>();
            int failures = 0;
            Action<string, Action> check = (name, assertion) =>
            {
                try { assertion(); checks.Add(new { name, passed = true }); }
                catch (Exception error)
                {
                    failures++;
                    checks.Add(new { name, passed = false, error = error.ToString() });
                }
            };
            var key = new MapRunKey(Guid.NewGuid(), 1);
            var source = new[] { Station("station-a") };
            var bindings = new MapRunBindings("ProbeMap", source);
            var catalog = Catalog();
            MapRunResolution resolution = Resolve(key, catalog, bindings);
            check("KnownContentResolves", () => Require(resolution.Passed, string.Join(",", resolution.Errors)));
            check("ConfigCollectionsDoNotAlias", () =>
            {
                source[0] = Station("mutated");
                Require(resolution.Config.Stations[0].StationKey == "station-a", "caller changed config");
                var mutable = (IList<MapStationConfig>)resolution.Config.Stations;
                bool rejected = false;
                try { mutable[0] = Station("mutated"); } catch (NotSupportedException) { rejected = true; }
                Require(rejected, "read-only collection accepted mutation");
            });
            check("UnknownContentDoesNotResolve", () =>
            {
                var unknownMap = MapRunResolver.Resolve(new MapRunRequest(key, "Absent", "mode-a", "content-v1"), catalog, bindings);
                Require(!unknownMap.Passed && unknownMap.Errors.Contains("Map.Unknown:Absent"), "unknown map accepted");
                var unknownMode = MapRunResolver.Resolve(new MapRunRequest(key, "ProbeMap", "absent", "content-v1"), catalog, bindings);
                Require(!unknownMode.Passed && unknownMode.Errors.Contains("Mode.Unknown:absent"), "unknown mode accepted");
                var staleHash = MapRunResolver.Resolve(new MapRunRequest(key, "ProbeMap", "mode-a", "old"), catalog, bindings);
                Require(!staleHash.Passed && staleHash.Errors.Contains("Map.ContentFingerprint.Mismatch"), "stale content accepted");
            });
            check("CatalogAndStationDuplicatesFail", () =>
            {
                var duplicates = new MapRunResolverCatalog("warmup", new[] { "mode-a", "mode-a" }, catalog.Maps);
                Require(!Resolve(key, duplicates, bindings).Passed, "duplicate modes accepted");
                Require(!Resolve(key, catalog, new MapRunBindings("ProbeMap", new[] { Station("a"), Station("a") })).Passed, "duplicate station accepted");
                Require(!Resolve(key, catalog, new MapRunBindings("ProbeMap", Enumerable.Repeat(Station("a"), 129))).Passed, "oversized stations accepted");
            });
            check("CapturedModeAndFallbackAreFrozen", () =>
            {
                var modes = new[] { "mode-a" };
                var map = new MapRunMapDescription("ProbeMap", MapRunKind.Combat, "content-v1", 1, "arsenal", "arsenal-v1", modes);
                modes[0] = "mode-b";
                var frozenCatalog = new MapRunResolverCatalog("warmup", new[] { "mode-a", "mode-b" }, new[] { map });
                var fallback = MapRunResolver.Resolve(new MapRunRequest(key, "ProbeMap", "mode-b", "content-v1"), frozenCatalog, bindings);
                Require(fallback.Passed && fallback.Config.MatchIntent.ModeId == "mode-a" &&
                    fallback.Config.MatchIntent.ResolutionReason == "IncompatibleCapturedMode:mode-b", "fallback differs");
                Require(resolution.Config.MatchIntent.ModeId == "mode-a", "resolved config changed");
            });
            check("LobbyExplicitNoMatch", () =>
            {
                var map = new MapRunMapDescription("ProbeMap", MapRunKind.Lobby, "content-v1", 1, "arsenal", "arsenal-v1", Array.Empty<string>());
                var lobby = Resolve(key, new MapRunResolverCatalog("warmup", new[] { "mode-a" }, new[] { map }), bindings);
                Require(lobby.Passed && !lobby.Config.MatchIntent.HasMatch, "lobby has match");
            });
            check("ScopeDisposesInReverseAndIsIdempotent", () =>
            {
                var scope = new MapRunScope(key);
                var order = new List<int>();
                scope.Own(() => order.Add(1)); scope.Own(() => order.Add(2));
                scope.Dispose(); scope.Dispose(); scope.Own(() => order.Add(3));
                Require(scope.Cancellation.IsCancellationRequested && order.SequenceEqual(new[] { 2, 1, 3 }), "teardown order/count differs");
            });
            check("ScopeContinuesAfterTeardownFailure", () =>
            {
                var scope = new MapRunScope(key);
                bool released = false;
                scope.Own(() => released = true); scope.Own(() => { throw new InvalidOperationException("expected probe failure"); });
                bool reported = false;
                try { scope.Dispose(); } catch (AggregateException) { reported = true; }
                Require(reported && released && scope.Cancellation.IsCancellationRequested, "failed teardown leaked resources");
            });

            var created = new List<GameObject>();
            Transport previousTransport = Transport.active;
            bool previousListen = NetworkServer.listen;
            try
            {
                var transportObject = new GameObject("MapRunProbeTransport"); created.Add(transportObject);
                var transport = transportObject.AddComponent<kcp2k.KcpTransport>();
                typeof(kcp2k.KcpTransport).GetMethod("Awake", Members).Invoke(transport, null);
                transport.enabled = false;
                Transport.active = transport;
                NetworkServer.listen = false;
                NetworkServer.Listen(4);
                Require(NetworkServer.active, "socketless server did not start");
                var server = Authority("MapRunProbeServer", created);
                NetworkServer.Spawn(server.gameObject);
                var client = Authority("MapRunProbeLateClient", created);
                var notifications = new List<MapRunSnapshot>();
                Action<MapRunSnapshot> subscriber = snapshot => notifications.Add(snapshot);
                server.Subscribe(subscriber);
                server.Subscribe(subscriber);
                notifications.Clear();
                var nextKey = (MapRunKey)typeof(MapRunAuthority).GetProperty("NextKey", Members).GetValue(server);
                var beginArgs = new object[] { Resolve(nextKey, catalog, bindings), null };
                bool begun = Call(server, "BeginRun", beginArgs);
                var activeScope = (MapRunScope)beginArgs[1];
                check("DedicatedAppliesCommitWithoutClientHook", () =>
                    Require(begun && notifications.Count == 1 && server.Current.Status == MapBootstrapStatus.Preparing, "dedicated notification missing/duplicate"));
                trace.Add("Begin " + server.Current.Key + " revision=" + server.Current.Revision);
                check("CompositionReadyDoesNotBecomeReady", () =>
                {
                    Require(Call(server, "CommitPrepared", activeScope, 1UL, 201U, 202U), "prepared rejected");
                    Require(server.Current.Status == MapBootstrapStatus.CompositionReady && !server.Current.IsReady &&
                        server.Current.ActiveModeNetId == 0 && notifications.Count == 2, "composition admitted gameplay/duplicate notification");
                });
                check("RevisionRejectsOldScope", () =>
                {
                    ulong revision = server.Current.Revision;
                    Require(!Call(server, "Fail", activeScope, 1UL, "stale"), "stale revision committed");
                    using (var forged = new MapRunScope(activeScope.Key))
                        Require(!Call(server, "Fail", forged, revision, "forged"), "forged scope committed");
                    Require(server.Current.Revision == revision, "revision changed");
                });
                check("FullSnapshotSerializes", () =>
                {
                    var writer = new NetworkWriter();
                    writer.Write(server.Current);
                    var reader = new NetworkReader(writer.ToArray());
                    MapRunSnapshot copy = reader.Read<MapRunSnapshot>();
                    Require(reader.Remaining == 0 && Same(server.Current, copy), "full wire roundtrip differs");
                });
                check("LateClientActualSyncVarDeserialize", () =>
                {
                    var owner = new NetworkWriter(); var observers = new NetworkWriter();
                    typeof(NetworkIdentity).GetMethod("SerializeServer", Members).Invoke(server.netIdentity, new object[] { true, owner, observers });
                    byte[] payload = observers.ToArray();
                    Require(payload.Length != 0, "no initial state");
                    typeof(NetworkIdentity).GetMethod("DeserializeClient", Members).Invoke(client.netIdentity, new object[] { new NetworkReader(payload), true });
                    Require(Same(server.Current, client.Current), "late client descriptor differs");
                    int called = 0;
                    client.Subscribe(snapshot => { Require(Same(server.Current, snapshot), "late subscriber differs"); called++; });
                    Require(called == 1, "late subscriber count differs");
                });
                check("ClientCannotCommit", () =>
                {
                    var args = new object[] { Resolve(nextKey, catalog, bindings), null };
                    Require(!Call(client, "BeginRun", args) && args[1] == null, "remote became canonical writer");
                });
                check("UnsubscribeAndRetireCancelBeforeNextRun", () =>
                {
                    server.Unsubscribe(subscriber);
                    int count = notifications.Count;
                    Require(Call(server, "Retire", activeScope, server.Current.Revision), "retire rejected");
                    Require(activeScope.IsDisposed && activeScope.Cancellation.IsCancellationRequested && notifications.Count == count, "retire leaked subscription/scope");
                    nextKey = (MapRunKey)typeof(MapRunAuthority).GetProperty("NextKey", Members).GetValue(server);
                    var args = new object[] { Resolve(nextKey, catalog, bindings), null };
                    Require(Call(server, "BeginRun", args), "next run rejected");
                    Require(!Call(server, "Fail", activeScope, server.Current.Revision, "old-load"), "old run committed");
                    Require(nextKey.LoadSequence == 2 && server.Current.Revision == 1, "same-scene generation differs");
                    activeScope = (MapRunScope)args[1];
                });
                check("ForeignSessionEpochRejected", () =>
                {
                    var args = new object[] { Resolve(new MapRunKey(Guid.NewGuid(), 3), catalog, bindings), null };
                    Require(!Call(server, "BeginRun", args), "foreign session accepted");
                });
                check("MalformedWireRejected", () =>
                {
                    var writer = new NetworkWriter(); writer.WriteByte(255);
                    bool rejected = false;
                    try { new NetworkReader(writer.ToArray()).Read<MapRunSnapshot>(); } catch (InvalidDataException) { rejected = true; }
                    Require(rejected, "unknown schema accepted");
                });
                check("HostPublishesExactlyOnce", () =>
                {
                    Require(Call(server, "Retire", activeScope, server.Current.Revision), "host pre-retire rejected");
                    NetworkClient.ConnectHost();
                    Require(NetworkServer.activeHost, "local host pair missing");
                    int called = 0;
                    Action<MapRunSnapshot> hostSubscriber = snapshot => called++;
                    server.Subscribe(hostSubscriber); called = 0;
                    nextKey = (MapRunKey)typeof(MapRunAuthority).GetProperty("NextKey", Members).GetValue(server);
                    var args = new object[] { Resolve(nextKey, catalog, bindings), null };
                    Require(Call(server, "BeginRun", args) && called == 1, "host notified twice/never");
                    server.Unsubscribe(hostSubscriber);
                });
            }
            finally
            {
                if (NetworkClient.active) NetworkClient.Shutdown();
                if (NetworkServer.active) NetworkServer.Shutdown();
                foreach (GameObject obj in created) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
                Transport.active = previousTransport;
                NetworkServer.listen = previousListen;
            }
            const string path = "Docs/tasks/report/map-runtime-bootstrap/contracts.json";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                utc = DateTime.UtcNow, passed = failures == 0, failureCount = failures, checks, trace,
                limits = "Socketless Mirror server + local host pair + separate actual SyncVar deserialize. No remote transport, Unity gameplay, native catalog preflight or SDK Relay proof."
            }, Newtonsoft.Json.Formatting.Indented));
            return new { passed = failures == 0, failureCount = failures, checkCount = checks.Count, reportPath = path };
        }

        private static MapStationConfig Station(string key) => new MapStationConfig(key, "", "Bare", "layout-v1", 1);
        private static MapRunResolverCatalog Catalog() => new MapRunResolverCatalog("warmup", new[] { "mode-a", "mode-b" },
            new[] { new MapRunMapDescription("ProbeMap", MapRunKind.Combat, "content-v1", 1, "arsenal", "arsenal-v1", new[] { "mode-a" }) });
        private static MapRunResolution Resolve(MapRunKey key, MapRunResolverCatalog catalog, MapRunBindings bindings) =>
            MapRunResolver.Resolve(new MapRunRequest(key, "ProbeMap", "mode-a", "content-v1"), catalog, bindings);
        private static bool Call(MapRunAuthority authority, string method, params object[] args) =>
            (bool)typeof(MapRunAuthority).GetMethod(method, Members).Invoke(authority, args);
        private static MapRunAuthority Authority(string name, List<GameObject> created)
        {
            var root = new GameObject(name); created.Add(root);
            var authority = root.AddComponent<MapRunAuthority>();
            typeof(NetworkIdentity).GetMethod("Awake", Members).Invoke(root.GetComponent<NetworkIdentity>(), null);
            return authority;
        }
        private static bool Same(MapRunSnapshot left, MapRunSnapshot right)
        {
            var a = new NetworkWriter(); a.WriteMapRunSnapshot(left);
            var b = new NetworkWriter(); b.WriteMapRunSnapshot(right);
            return a.ToArray().SequenceEqual(b.ToArray());
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
