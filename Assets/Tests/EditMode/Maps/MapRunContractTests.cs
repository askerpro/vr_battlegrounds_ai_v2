using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using NUnit.Framework;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Контракт запуска карты: неизменяемый config, чистый resolver, scope и единственный писатель descriptor
    /// (<see cref="MapRunAuthority"/>). Перенесено из временных probes задачи 2 (<c>MapRunContractProbe</c>,
    /// <c>Tools/Probes/MapRuntimeBootstrap/teardown.cs</c>) после их GREEN.
    ///
    /// <para>
    /// Что доказывает. Config не делит коллекции с вызывающим; неизвестная карта, режим или устаревший отпечаток
    /// содержимого не разрешаются; режим серии фиксируется при загрузке, несовместимый заменяется с явной причиной;
    /// лобби — явный NoMatch. Scope снимает ресурсы в обратном порядке и не теряет их при сбое. Descriptor пишет только
    /// сервер, по ревизии и своему scope; CompositionReady не открывает gameplay; полный descriptor переживает провод
    /// и initial-сериализацию SyncVar; повреждённый провод отвергается; teardown и остановка сервера не могут начать
    /// новый запуск из своих колбэков.
    /// </para>
    /// </summary>
    public class MapRunContractTests : MirrorTestHarness
    {
        private static MapStationConfig Station(string key) => new MapStationConfig(key, "", "Bare", "layout-v1", 1);

        private static MapRunResolverCatalog Catalog() => new MapRunResolverCatalog("warmup", new[] { "mode-a", "mode-b" },
            new[] { new MapRunMapDescription("ProbeMap", MapRunKind.Combat, "content-v1", 1, "arsenal", "arsenal-v1", new[] { "mode-a" }) });

        private static MapRunResolution Resolve(MapRunKey key, MapRunResolverCatalog catalog, MapRunBindings bindings,
                                                string scene = "ProbeMap", string mode = "mode-a", string content = "content-v1") =>
            MapRunResolver.Resolve(new MapRunRequest(key, scene, mode, content), catalog, bindings);

        private static MapRunBindings Bindings(params MapStationConfig[] stations) => new MapRunBindings("ProbeMap", stations);

        private static byte[] Wire(MapRunSnapshot snapshot)
        {
            var writer = new NetworkWriter();
            writer.WriteMapRunSnapshot(snapshot);
            return writer.ToArray();
        }

        // ── Чистый контракт ──────────────────────────────────────────────────

        [Test]
        public void Config_не_делит_коллекции_с_вызывающим()
        {
            var source = new[] { Station("station-a") };
            MapRunResolution resolution = Resolve(new MapRunKey(Guid.NewGuid(), 1), Catalog(), new MapRunBindings("ProbeMap", source));
            Assert.IsTrue(resolution.Passed, string.Join(",", resolution.Errors));

            source[0] = Station("mutated");
            Assert.AreEqual("station-a", resolution.Config.Stations[0].StationKey, "Вызывающий изменил разрешённый config.");
            Assert.Throws<NotSupportedException>(() => ((IList<MapStationConfig>)resolution.Config.Stations)[0] = Station("mutated"),
                "Коллекция станций config изменяема.");
        }

        [Test]
        public void Неизвестное_содержимое_не_разрешается()
        {
            var key = new MapRunKey(Guid.NewGuid(), 1);
            CollectionAssert.Contains(Resolve(key, Catalog(), Bindings(), scene: "Absent").Errors, "Map.Unknown:Absent");
            CollectionAssert.Contains(Resolve(key, Catalog(), Bindings(), mode: "absent").Errors, "Mode.Unknown:absent");
            CollectionAssert.Contains(Resolve(key, Catalog(), Bindings(), content: "old").Errors, "Map.ContentFingerprint.Mismatch");
        }

        [Test]
        public void Дубликаты_каталога_и_станций_отвергаются()
        {
            var key = new MapRunKey(Guid.NewGuid(), 1);
            var duplicates = new MapRunResolverCatalog("warmup", new[] { "mode-a", "mode-a" }, Catalog().Maps);
            Assert.IsFalse(Resolve(key, duplicates, Bindings()).Passed, "Дубликат режима принят.");
            Assert.IsFalse(Resolve(key, Catalog(), Bindings(Station("a"), Station("a"))).Passed, "Дубликат станции принят.");
            Assert.IsFalse(Resolve(key, Catalog(), new MapRunBindings("ProbeMap", Enumerable.Repeat(Station("a"), MapRunConfig.MaxStations + 1))).Passed,
                "Превышение числа станций принято.");
        }

        [Test]
        public void Несовместимый_режим_серии_заменяется_с_причиной_и_замораживается()
        {
            var modes = new[] { "mode-a" };
            var map = new MapRunMapDescription("ProbeMap", MapRunKind.Combat, "content-v1", 1, "arsenal", "arsenal-v1", modes);
            modes[0] = "mode-b";
            var catalog = new MapRunResolverCatalog("warmup", new[] { "mode-a", "mode-b" }, new[] { map });

            MapRunResolution fallback = Resolve(new MapRunKey(Guid.NewGuid(), 1), catalog, Bindings(), mode: "mode-b");

            Assert.IsTrue(fallback.Passed, string.Join(",", fallback.Errors));
            Assert.AreEqual("mode-a", fallback.Config.MatchIntent.ModeId, "Описание карты изменилось задним числом.");
            Assert.AreEqual("IncompatibleCapturedMode:mode-b", fallback.Config.MatchIntent.ResolutionReason);
        }

        [Test]
        public void Лобби_явно_без_матча()
        {
            var map = new MapRunMapDescription("ProbeMap", MapRunKind.Lobby, "content-v1", 1, "arsenal", "arsenal-v1", Array.Empty<string>());
            MapRunResolution lobby = Resolve(new MapRunKey(Guid.NewGuid(), 1), new MapRunResolverCatalog("warmup", new[] { "mode-a" }, new[] { map }), Bindings());
            Assert.IsTrue(lobby.Passed && !lobby.Config.MatchIntent.HasMatch, "У лобби появился режим матча.");
        }

        [Test]
        public void Scope_снимает_ресурсы_в_обратном_порядке_и_один_раз()
        {
            var scope = new MapRunScope(new MapRunKey(Guid.NewGuid(), 1));
            var order = new List<int>();
            scope.Own(() => order.Add(1));
            scope.Own(() => order.Add(2));
            scope.Dispose();
            scope.Dispose();
            scope.Own(() => order.Add(3)); // поздний ресурс снятого scope освобождается сразу

            Assert.IsTrue(scope.Cancellation.IsCancellationRequested);
            CollectionAssert.AreEqual(new[] { 2, 1, 3 }, order);
        }

        [Test]
        public void Сбой_одного_ресурса_не_мешает_снять_остальные()
        {
            var scope = new MapRunScope(new MapRunKey(Guid.NewGuid(), 1));
            bool released = false;
            scope.Own(() => released = true);
            scope.Own(() => throw new InvalidOperationException("ожидаемый сбой теста"));

            Assert.Throws<AggregateException>(() => scope.Dispose());
            Assert.IsTrue(released && scope.Cancellation.IsCancellationRequested, "Сбой teardown оставил ресурс.");
        }

        [Test]
        public void Close_отменяет_scope_без_teardown()
        {
            var scope = new MapRunScope(new MapRunKey(Guid.NewGuid(), 1));
            bool released = false;
            scope.Own(() => released = true);

            scope.Close();

            Assert.IsTrue(scope.IsClosed && scope.Cancellation.IsCancellationRequested);
            Assert.IsFalse(scope.IsDisposed || released, "Closing выполнил teardown раньше выгрузки сцены.");
        }

        [Test]
        public void Повреждённый_провод_отвергается()
        {
            var writer = new NetworkWriter();
            writer.WriteByte(255);
            Assert.Throws<InvalidDataException>(() => new NetworkReader(writer.ToArray()).ReadMapRunSnapshot());
        }

        // ── Единственный писатель ────────────────────────────────────────────

        private MapRunScope BeginRun(MapRunAuthority authority)
        {
            Assert.IsTrue(authority.BeginRun(Resolve(authority.NextKey, Catalog(), Bindings()), out MapRunScope scope), "Запуск не начат.");
            return scope;
        }

        [Test]
        public void Сервер_публикует_descriptor_подписчику_ровно_один_раз()
        {
            SilenceMirrorNoise();
            MapRunAuthority authority = CreateRunAuthority();
            var notifications = new List<MapRunSnapshot>();
            Action<MapRunSnapshot> subscriber = notifications.Add;
            authority.Subscribe(subscriber);
            authority.Subscribe(subscriber);
            notifications.Clear();

            BeginRun(authority);

            Assert.AreEqual(1, notifications.Count, "Подписчик получил descriptor не один раз (или выделенный сервер ждал hook).");
            Assert.AreEqual(MapBootstrapStatus.Preparing, authority.Current.Status);
            authority.Unsubscribe(subscriber);
        }

        [Test]
        public void CompositionReady_не_открывает_gameplay()
        {
            SilenceMirrorNoise();
            MapRunAuthority authority = CreateRunAuthority();
            MapRunScope scope = BeginRun(authority);

            Assert.IsTrue(authority.CommitPrepared(scope, authority.Current.Revision, 201, 202));

            Assert.AreEqual(MapBootstrapStatus.CompositionReady, authority.Current.Status);
            Assert.IsFalse(authority.Current.IsReady, "Состав без режима открыл server Ready.");
            Assert.AreEqual(0u, authority.Current.ActiveModeNetId);
        }

        [Test]
        public void Старый_scope_и_устаревшая_ревизия_не_пишут()
        {
            SilenceMirrorNoise();
            MapRunAuthority authority = CreateRunAuthority();
            MapRunScope scope = BeginRun(authority);
            Assert.IsTrue(authority.CommitPrepared(scope, authority.Current.Revision, 201, 202));
            ulong revision = authority.Current.Revision;

            Assert.IsFalse(authority.Fail(scope, revision - 1, "stale"), "Устаревшая ревизия закоммичена.");
            using (var forged = new MapRunScope(scope.Key))
                Assert.IsFalse(authority.Fail(forged, revision, "forged"), "Чужой scope с тем же ключом закоммичен.");
            Assert.AreEqual(revision, authority.Current.Revision);

            Assert.IsTrue(authority.Retire(scope, revision));
            Assert.IsTrue(scope.IsDisposed);
            MapRunScope next = BeginRun(authority);
            Assert.AreEqual(2UL, next.Key.LoadSequence, "Повтор той же карты не получил новый ключ.");
            Assert.IsFalse(authority.Fail(scope, authority.Current.Revision, "old-load"), "Снятый запуск закоммитил в новый.");
            Assert.IsFalse(authority.BeginRun(Resolve(new MapRunKey(Guid.NewGuid(), 3), Catalog(), Bindings()), out _),
                "Запуск чужой сессии принят.");
        }

        [Test]
        public void Descriptor_целиком_переживает_провод_и_позднего_клиента()
        {
            SilenceMirrorNoise();
            MapRunAuthority server = CreateRunAuthority();
            MapRunScope scope = BeginRun(server);
            Assert.IsTrue(server.CommitPrepared(scope, server.Current.Revision, 201, 202));

            var reader = new NetworkReader(Wire(server.Current));
            MapRunSnapshot copy = reader.ReadMapRunSnapshot();
            Assert.AreEqual(0, reader.Remaining);
            CollectionAssert.AreEqual(Wire(server.Current), Wire(copy), "Провод потерял часть descriptor.");

            // Поздний клиент: настоящая initial-сериализация SyncVar отдельного объекта.
            var client = CreateNetworkComponent<MapRunAuthority>("LateClientSessionContext");
            ReplicateToClient(server, client);
            CollectionAssert.AreEqual(Wire(server.Current), Wire(client.Current), "Поздний клиент получил другой descriptor.");

            Assert.IsFalse(client.BeginRun(Resolve(server.NextKey, Catalog(), Bindings()), out MapRunScope clientScope) || clientScope != null,
                "Клиент стал писателем descriptor.");
        }

        [Test]
        public void Teardown_и_остановка_сервера_не_начинают_новый_запуск_из_колбэков()
        {
            SilenceMirrorNoise();
            foreach (bool stopServer in new[] { false, true })
            {
                MapRunAuthority authority = CreateNetworkComponent<MapRunAuthority>("SessionContext" + stopServer);
                InvokeLifecycleMethod(authority, "Awake");
                SpawnOnServer(authority);
                MapRunScope scope = BeginRun(authority);
                int callbacks = 0;
                bool resurrected = false;
                Action attempt = () =>
                {
                    callbacks++;
                    resurrected |= authority.BeginRun(Resolve(authority.NextKey, Catalog(), Bindings()), out _);
                };
                scope.Cancellation.Register(attempt);
                scope.Own(attempt);

                if (stopServer) NetworkServer.UnSpawn(authority.gameObject);
                else Assert.IsTrue(authority.Retire(scope, authority.Current.Revision));

                Assert.AreEqual(2, callbacks, $"Колбэки снятия не вызваны (остановка сервера: {stopServer}).");
                Assert.IsFalse(resurrected, $"Колбэк снятия начал новый запуск (остановка сервера: {stopServer}).");
                Assert.IsTrue(scope.IsDisposed);
                Assert.AreEqual(MapBootstrapStatus.Retiring, authority.Current.Status);
            }
        }
    }
}
