using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Адаптер генерируемых станций арсенала в запуске карты (задача 7 плана map-runtime-bootstrap).
    ///
    /// <para>
    /// Что доказывает. Описание сгенерированной станции строится из входов, одинаковых на всех машинах, и его выбор
    /// оформления, fallback, layout hash и версия схемы ID попадают в config запуска до публикации; клиент сверяет своё
    /// описание с config и при расхождении не собирает станцию. Отказ описания (нестилизованный пресет, нет каталога
    /// ресурсов) — именованный, без отката на Authored. Готовность клиента для барьера Relay
    /// (<see cref="MapBootstrap.IsLocallyReady"/>) ждёт, пока сборщик не подтвердит регистрацию ID всех станций
    /// (<c>GeneratedAnchorMissingAtInitialRequest</c>): до этого снимок не просится; отказ сборщика именован.
    /// </para>
    ///
    /// <para>
    /// Пределы. Сборщик (<see cref="ArsenalStationComposer"/>) требует Play Mode, поэтому здесь его заменяет
    /// <see cref="FakeComposer"/> со сценарием ответов. Настоящая сборка, регистрация ID и разборка через scope —
    /// Play Mode-проба задачи 7 (см. прогресс-документ). Стильного пресета в проекте нет: фикстура — копия
    /// FullDemoArsenal в памяти со стилем IndustrialPegboardPresentation, ассеты не меняются.
    /// </para>
    /// </summary>
    public class MapArsenalCompositionAdapterTests
    {
        private const string StationKey = "GeneratedTestStation";

        /// <summary>Имя сцены карты в config: у сцены EditMode-прогона имени нет, а resolver требует идентификатор.
        /// Клиентская половина MapBootstrap сцену config с собственной не сверяет — это делает UpdateClientRun.</summary>
        private const string TestScene = "GeneratedTestMap";
        private const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private MapBootstrap _bootstrap;

        [TearDown]
        public void Cleanup()
        {
            if (_bootstrap != null) Invoke(_bootstrap, "OnDestroy");
            _bootstrap = null;
            foreach (UnityEngine.Object item in _owned)
                if (item != null) UnityEngine.Object.DestroyImmediate(item);
            _owned.Clear();
        }

        // ── Фикстура ─────────────────────────────────────────────────────────

        private static ArsenalCompositionCatalog Catalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ArsenalCompositionCatalog>("Assets/Data/Arsenal/ArsenalCompositionCatalog.asset");
            Assert.IsNotNull(catalog, "Нет каталога ресурсов генератора.");
            return catalog;
        }

        /// <summary>Копия FullDemoArsenal в памяти со стилем: записи, которые стиль и каталог умеют разрешить.</summary>
        private ArsenalPreset StyledPreset(int maxEntries = 4)
        {
            var source = AssetDatabase.LoadAssetAtPath<ArsenalPreset>("Assets/Data/Weapons/FullDemoArsenal.asset");
            var style = AssetDatabase.LoadAssetAtPath<ArsenalPresentationStyle>("Assets/Data/Weapons/IndustrialPegboardPresentation.asset");
            Assert.IsNotNull(source, "Нет FullDemoArsenal.");
            Assert.IsNotNull(style, "Нет IndustrialPegboardPresentation.");
            ArsenalCompositionCatalog catalog = Catalog();

            ArsenalPreset preset = UnityEngine.Object.Instantiate(source);
            _owned.Add(preset);
            typeof(ArsenalPreset).GetField("_presentationStyle", Fields).SetValue(preset, style);
            var kept = new List<ArsenalPreset.Entry>();
            foreach (ArsenalPreset.Entry entry in source.Entries)
            {
                if (entry.Weapon == null || catalog.Weapons.Count(m => m.Weapon == entry.Weapon) != 1) continue;
                try { ArsenalPresentationResolver.Resolve(entry.Weapon, entry.Zone, style); }
                catch (Exception) { continue; }
                if (kept.Count < maxEntries) kept.Add(entry);
            }
            Assume.That(kept.Count, Is.GreaterThanOrEqualTo(2), "Каталог и стиль не разрешают и двух записей FullDemoArsenal.");
            typeof(ArsenalPreset).GetField("_entries", Fields).SetValue(preset, kept);
            return preset;
        }

        private ArsenalPreset UnstyledPreset()
        {
            var source = AssetDatabase.LoadAssetAtPath<ArsenalPreset>("Assets/Data/Weapons/FullDemoArsenal.asset");
            Assert.IsNotNull(source);
            Assume.That(source.PresentationStyle, Is.Null, "FullDemoArsenal получил стиль — фикстура «нестилизованного пресета» устарела.");
            return source;
        }

        /// <summary>Оболочка станции в режиме <paramref name="mode"/>, как её проверяет MapRoot и ждёт сборщик.</summary>
        private ArsenalStationCompositionBinding Station(string key, ArsenalCompositionMode mode = ArsenalCompositionMode.Generated)
        {
            var go = new GameObject(key);
            _owned.Add(go);
            go.AddComponent<NetworkIdentity>();
            var wall = go.AddComponent<ArsenalWallController>();
            var presets = go.AddComponent<ArsenalStationPresetBinding>();
            var poses = go.AddComponent<ArsenalEquipmentPoses>();
            var anchor = go.AddComponent<ArsenalStationAnchor>();
            anchor.ConfigureRaisedBounds(new Bounds(Vector3.zero, Vector3.one * 50f));
            var binding = go.AddComponent<ArsenalStationCompositionBinding>();
            Set(binding, "_stationKey", key);
            Set(binding, "_mode", mode);
            Set(binding, "_controller", wall);
            Set(binding, "_authoredBinding", presets);
            Set(binding, "_stationAnchor", anchor);
            Set(binding, "_equipmentPoses", poses);
            return binding;
        }

        private static MapRunConfig Config(MapRunKey key, string scene, MapRunBindings stations)
        {
            var catalog = new MapRunResolverCatalog("warmup", Array.Empty<string>(),
                new[] { new MapRunMapDescription(scene, MapRunKind.Lobby, "content", 1, "arsenal", "arsenal-v1", Array.Empty<string>()) });
            MapRunResolution resolution = MapRunResolver.Resolve(new MapRunRequest(key, scene, "", "content"), catalog, stations);
            Assert.IsTrue(resolution.Passed, string.Join(", ", resolution.Errors));
            return resolution.Config;
        }

        private static MapRunBindings Authored(string scene, params string[] keys) =>
            new MapRunBindings(scene, keys.Select(k => new MapStationConfig(k, "", MapArsenalCompositionAdapter.AuthoredFallback, "shell-" + k, 1)));

        private static void Set(object target, string field, object value)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo info = type.GetField(field, Fields | BindingFlags.DeclaredOnly);
                if (info == null) continue;
                info.SetValue(target, value);
                return;
            }
            throw new MissingFieldException(target.GetType().FullName, field);
        }

        private static void Invoke(object target, string method) =>
            target.GetType().GetMethod(method, Fields).Invoke(target, null);

        /// <summary>Сборщик со сценарием ответов ValidateReady; handle принадлежит scope, как у настоящего.</summary>
        private sealed class FakeComposer : IArsenalStationComposer
        {
            public ArsenalCompositionReadiness Next = ArsenalCompositionReadiness.Pending;
            public string NextReason = "NotRegistered:Fake";
            public string ThrowOnPrepare;
            public int Prepared, Activated;
            public MapRunScope LastScope;
            public readonly List<GameObject> Roots = new List<GameObject>();

            public ArsenalCompositionHandle Prepare(ArsenalStationDescription description, MapRunScope scope, ArsenalStationCompositionBinding station)
            {
                if (ThrowOnPrepare != null) throw new InvalidOperationException(ThrowOnPrepare);
                Prepared++;
                LastScope = scope;
                var root = new GameObject("FakeGeneratedSlots");
                Roots.Add(root);
                var handle = new ArsenalCompositionHandle(description, scope, station, root, Array.Empty<ArsenalSlotController>(),
                    Array.Empty<NetworkUxrIdentityAssignment>(), new List<Transform>());
                scope.Own(handle);
                return handle;
            }

            public void Activate(ArsenalCompositionHandle handle)
            {
                Activated++;
                handle.IsActive = true;
            }

            public ArsenalCompositionReadiness ValidateReady(ArsenalCompositionHandle handle, out string reason)
            {
                reason = Next == ArsenalCompositionReadiness.Passed ? null : NextReason;
                return handle.IsDisposed ? ArsenalCompositionReadiness.Failed : Next;
            }
        }

        // ── Описание и config ────────────────────────────────────────────────

        [Test]
        public void Описание_сгенерированной_станции_попадает_в_config_до_публикации()
        {
            ArsenalStationCompositionBinding generated = Station(StationKey);
            ArsenalStationCompositionBinding authored = Station("AuthoredStation", ArsenalCompositionMode.Authored);
            var errors = new List<string>();

            MapArsenalCompositionAdapter adapter = MapArsenalCompositionAdapter.Describe(new[] { generated, authored },
                StyledPreset(), Catalog(), new FakeComposer(), errors);

            CollectionAssert.IsEmpty(errors, "Описание тестовой станции отказало.");
            Assert.AreEqual(1, adapter.StationCount, "Авторская станция попала в адаптер генератора.");

            MapRunBindings applied = adapter.Apply(Authored("Lobby", StationKey, "AuthoredStation"));
            MapStationConfig station = applied.Stations.Single(s => s.StationKey == StationKey);
            Assert.AreNotEqual(MapArsenalCompositionAdapter.AuthoredFallback, station.DecorationFallback,
                "Запись сгенерированной станции осталась авторской оболочкой.");
            Assert.IsTrue(Enum.TryParse(station.DecorationFallback, out ArsenalDecorationFallback _),
                "Fallback оформления не из описания генератора: " + station.DecorationFallback);
            Assert.AreEqual(64, station.LayoutFingerprint.Length, "Layout hash описания не записан.");
            Assert.AreEqual((uint)ArsenalCompositionCatalog.CurrentIdentitySchemaVersion, station.IdentitySchemaVersion);
            Assert.AreEqual("shell-AuthoredStation", applied.Stations.Single(s => s.StationKey == "AuthoredStation").LayoutFingerprint,
                "Запись авторской станции изменена адаптером.");

            // Запись проходит resolver и сетевой descriptor без потерь — клиент сверяет именно её.
            MapRunConfig config = Config(new MapRunKey(Guid.NewGuid(), 1), "Lobby", applied);
            var writer = new NetworkWriter();
            writer.WriteMapRunSnapshot(new MapRunSnapshot(config, 1, MapBootstrapStatus.CompositionReady, MapState.Warmup, 0, "", 0, 1, 2, ""));
            MapStationConfig wire = new NetworkReader(writer.ToArray()).ReadMapRunSnapshot().Config.Stations.Single(s => s.StationKey == StationKey);
            Assert.AreEqual(station.LayoutFingerprint, wire.LayoutFingerprint);
            Assert.AreEqual(station.DecorationFallback, wire.DecorationFallback);
            Assert.AreEqual(station.DecorationId, wire.DecorationId);
        }

        [Test]
        public void Нестилизованный_пресет_даёт_именованный_отказ_без_отката_на_Authored()
        {
            var errors = new List<string>();
            MapArsenalCompositionAdapter adapter = MapArsenalCompositionAdapter.Describe(new[] { Station(StationKey) },
                UnstyledPreset(), Catalog(), new FakeComposer(), errors);

            Assert.IsTrue(errors.Any(e => e.StartsWith("Station.Generated.UnstyledPreset:" + StationKey)),
                "Нет именованного отказа нестилизованного пресета: " + string.Join(", ", errors));
            Assert.IsFalse(adapter.HasStations, "Станция с отказавшим описанием осталась к сборке.");
        }

        [Test]
        public void Без_каталога_ресурсов_сгенерированная_станция_не_описывается()
        {
            var errors = new List<string>();
            MapArsenalCompositionAdapter.Describe(new[] { Station(StationKey) }, StyledPreset(), null, new FakeComposer(), errors);
            CollectionAssert.AreEqual(new[] { "Station.Generated.CatalogMissing:" + StationKey }, errors);

            // Карта без сгенерированных станций каталог ресурсов не требует.
            errors.Clear();
            MapArsenalCompositionAdapter authoredOnly = MapArsenalCompositionAdapter.Describe(
                new[] { Station("AuthoredStation", ArsenalCompositionMode.Authored) }, null, null, new FakeComposer(), errors);
            CollectionAssert.IsEmpty(errors);
            Assert.AreEqual(ArsenalCompositionReadiness.Passed, authoredOnly.Readiness, "Карта только с авторскими станциями не готова.");
        }

        [Test]
        public void Клиент_сверяет_config_и_не_принимает_расхождения()
        {
            ArsenalStationCompositionBinding generated = Station(StationKey);
            ArsenalStationCompositionBinding authored = Station("AuthoredStation", ArsenalCompositionMode.Authored);
            var stations = new[] { generated, authored };
            var errors = new List<string>();
            MapArsenalCompositionAdapter adapter = MapArsenalCompositionAdapter.Describe(stations, StyledPreset(), Catalog(), new FakeComposer(), errors);
            CollectionAssert.IsEmpty(errors);
            MapRunBindings published = adapter.Apply(Authored("Lobby", StationKey, "AuthoredStation"));
            var key = new MapRunKey(Guid.NewGuid(), 1);

            Assert.IsTrue(adapter.Verify(Config(key, "Lobby", published), stations, errors), string.Join(", ", errors));

            MapStationConfig server = published.Stations.Single(s => s.StationKey == StationKey);
            var layout = new MapRunBindings("Lobby", new[] { new MapStationConfig(StationKey, server.DecorationId, server.DecorationFallback,
                new string('0', 64), server.IdentitySchemaVersion), published.Stations.Single(s => s.StationKey == "AuthoredStation") });
            Assert.IsFalse(adapter.Verify(Config(key, "Lobby", layout), stations, errors));
            CollectionAssert.Contains(errors, "Station.Generated.LayoutMismatch:" + StationKey);

            errors.Clear();
            Assert.IsFalse(adapter.Verify(Config(key, "Lobby", Authored("Lobby", StationKey, "AuthoredStation")), stations, errors),
                "Сервер считает станцию авторской, клиент собрал бы свою — расхождение не замечено.");
            CollectionAssert.Contains(errors, "Station.Generated.ModeMismatch:" + StationKey);

            errors.Clear();
            var reversed = new MapRunBindings("Lobby", published.Stations.Select(s => s.StationKey == "AuthoredStation"
                ? new MapStationConfig(s.StationKey, "", "Bare", new string('1', 64), 1) : s));
            Assert.IsFalse(adapter.Verify(Config(key, "Lobby", reversed), stations, errors),
                "Сервер собрал станцию генератором, у клиента она авторская — расхождение не замечено.");
            CollectionAssert.Contains(errors, "Station.Generated.ModeMismatch:AuthoredStation");

            errors.Clear();
            Assert.IsFalse(adapter.Verify(Config(key, "Lobby", Authored("Lobby", "AuthoredStation")), stations, errors));
            CollectionAssert.Contains(errors, "Station.Generated.NotPublished:" + StationKey);
        }

        // ── Свод готовности ──────────────────────────────────────────────────

        [Test]
        public void Свод_готовности_ждёт_Pending_и_отказывает_на_Failed()
        {
            var composer = new FakeComposer();
            var errors = new List<string>();
            MapArsenalCompositionAdapter adapter = MapArsenalCompositionAdapter.Describe(new[] { Station(StationKey) },
                StyledPreset(), Catalog(), composer, errors);
            CollectionAssert.IsEmpty(errors);
            Assert.AreEqual(ArsenalCompositionReadiness.Pending, adapter.Poll(), "Несобранная станция не держит готовность.");
            StringAssert.Contains("NotComposed", adapter.Reason);

            var scope = new MapRunScope(new MapRunKey(Guid.NewGuid(), 1));
            try
            {
                adapter.Compose(scope);
                Assert.AreEqual(1, composer.Prepared);
                Assert.AreEqual(1, composer.Activated, "Собранная станция не включена.");

                Assert.AreEqual(ArsenalCompositionReadiness.Pending, adapter.Poll());
                StringAssert.StartsWith(StationKey + ": NotRegistered", adapter.Reason, "Причина ожидания без StationKey.");

                composer.Next = ArsenalCompositionReadiness.Passed;
                Assert.AreEqual(ArsenalCompositionReadiness.Passed, adapter.Poll());
                Assert.IsEmpty(adapter.Reason);

                composer.Next = ArsenalCompositionReadiness.Failed;
                composer.NextReason = "ForeignOwner:x";
                Assert.AreEqual(ArsenalCompositionReadiness.Failed, adapter.Poll());
                StringAssert.Contains("ForeignOwner", adapter.Reason);
            }
            finally { scope.Dispose(); }

            Assert.IsTrue(composer.Roots.All(r => r == null), "Scope не разобрал собранную станцию.");
        }

        // ── Клиент MapBootstrap: барьер ждёт регистрации ID ──────────────────

        /// <summary>Клиентская сцена карты: MapRoot с одной сгенерированной станцией, MapBootstrap со сборщиком-фейком.</summary>
        private (MapBootstrap bootstrap, FakeComposer composer, MapRunConfig config) ClientMap(Func<MapRunBindings, MapRunBindings> tamper = null)
        {
            Assume.That(NetworkServer.active, Is.False, "Клиентская половина проверяется без активного сервера.");
            ArsenalStationCompositionBinding station = Station(StationKey);
            ArsenalPreset preset = StyledPreset();
            var map = ScriptableObject.CreateInstance<MapData>();
            _owned.Add(map);
            var go = new GameObject("MapRoot");
            _owned.Add(go);
            map.sceneName = TestScene;
            map.arsenalPreset = preset;
            var root = go.AddComponent<MapRoot>();
            Set(root, "_map", map);
            Set(root, "_stations", new[] { station });
            var bootstrap = go.AddComponent<MapBootstrap>();
            Invoke(bootstrap, "Awake");
            _bootstrap = bootstrap;
            var composer = new FakeComposer();
            bootstrap.Composer = composer;

            // Config, который опубликовал бы сервер: описание из тех же входов.
            var errors = new List<string>();
            MapRunBindings published = MapArsenalCompositionAdapter.Describe(new[] { station }, preset, Catalog(), new FakeComposer(), errors)
                .Apply(Authored(TestScene, StationKey));
            CollectionAssert.IsEmpty(errors);
            if (tamper != null) published = tamper(published);
            return (bootstrap, composer, Config(new MapRunKey(Guid.NewGuid(), 1), TestScene, published));
        }

        [Test]
        public void Клиент_не_просит_снимок_пока_ID_сгенерированных_станций_не_зарегистрированы()
        {
            var (bootstrap, composer, config) = ClientMap();
            int changes = 0;
            Action counter = () => changes++;
            MapBootstrap.LocalReadinessChanged += counter;
            try
            {
                var barrier = new InitialStateBarrier();
                bootstrap.AcceptLocalRun(config, Catalog());

                Assert.AreEqual(config.Key, bootstrap.LocalRunKey);
                Assert.AreEqual(1, composer.Prepared, "Клиент не собрал сгенерированную станцию.");
                Assert.AreEqual(1, composer.Activated);
                Assert.AreEqual(config.Key, composer.LastScope.Key, "Клиент собрал станцию не с ключом принятого запуска.");
                Assert.IsFalse(bootstrap.IsLocallyReady(config.Key), "Запуск готов, хотя ID станции не зарегистрированы.");
                Assert.AreEqual(0u, barrier.Evaluate(config.Key, bootstrap.IsLocallyReady(config.Key)),
                    "Барьер Relay попросил снимок до регистрации адресатов сгенерированной станции.");

                bootstrap.PollLocalRun();
                Assert.IsFalse(bootstrap.IsLocallyReady(config.Key), "Pending в повторном опросе открыл готовность.");

                int before = changes;
                composer.Next = ArsenalCompositionReadiness.Passed;
                bootstrap.PollLocalRun();
                Assert.IsTrue(bootstrap.IsLocallyReady(config.Key), "Все станции Passed, а запуск не готов.");
                Assert.AreEqual(before + 1, changes, "Барьер Relay не узнал о готовности: событие не поднято.");
                Assert.AreNotEqual(0u, barrier.Evaluate(config.Key, bootstrap.IsLocallyReady(config.Key)),
                    "Готовый запуск не просит снимок.");

                bootstrap.CloseLocalRun();
                Assert.IsFalse(bootstrap.IsLocallyReady(config.Key), "Закрытый запуск готов.");
                Assert.IsTrue(composer.LastScope.IsClosed, "Closing не закрыл scope клиента: писатели станции не остановлены.");
                Assert.IsFalse(composer.LastScope.IsDisposed, "Closing — не teardown: станция живёт до выгрузки сцены.");
            }
            finally { MapBootstrap.LocalReadinessChanged -= counter; }

            Invoke(bootstrap, "OnDestroy");
            _bootstrap = null;
            Assert.IsTrue(composer.LastScope.IsDisposed, "Выгрузка сцены не разобрала станции клиента.");
            Assert.IsTrue(composer.Roots.All(r => r == null), "Поддерево станции пережило выгрузку.");
        }

        [Test]
        public void Отказ_сборщика_на_клиенте_именован_и_закрывает_готовность()
        {
            var (bootstrap, composer, config) = ClientMap();
            bootstrap.AcceptLocalRun(config, Catalog());

            composer.Next = ArsenalCompositionReadiness.Failed;
            composer.NextReason = "ForeignOwner:test";
            LogAssert.Expect(LogType.Error, new Regex("Arsenal\\.Generated\\.NotReady.*ForeignOwner"));
            bootstrap.PollLocalRun();

            Assert.AreEqual("Arsenal.Generated.NotReady", bootstrap.FailureCode);
            Assert.IsFalse(bootstrap.IsLocallyReady(config.Key), "Отказавший запуск готов к снимку.");
            Assert.IsTrue(composer.LastScope.IsDisposed, "Отказавшие станции не разобраны.");
        }

        [Test]
        public void Исключение_сборщика_на_клиенте_именовано()
        {
            var (bootstrap, composer, config) = ClientMap();
            composer.ThrowOnPrepare = "ArsenalComposer.ShellIncomplete:" + StationKey;
            LogAssert.Expect(LogType.Error, new Regex("Arsenal\\.Generated\\.Compose.*ArsenalComposer\\.ShellIncomplete"));

            bootstrap.AcceptLocalRun(config, Catalog());

            Assert.AreEqual("Arsenal.Generated.Compose", bootstrap.FailureCode);
            Assert.IsFalse(bootstrap.IsLocallyReady(config.Key));
        }

        [Test]
        public void Расхождение_с_config_сервера_не_собирает_станцию_на_клиенте()
        {
            var (bootstrap, composer, config) = ClientMap(published => new MapRunBindings(published.MapScene,
                published.Stations.Select(s => new MapStationConfig(s.StationKey, s.DecorationId, s.DecorationFallback, new string('0', 64), s.IdentitySchemaVersion))));
            LogAssert.Expect(LogType.Error, new Regex("Arsenal\\.Generated\\.ConfigMismatch.*LayoutMismatch"));

            bootstrap.AcceptLocalRun(config, Catalog());

            Assert.AreEqual("Arsenal.Generated.ConfigMismatch", bootstrap.FailureCode);
            Assert.AreEqual(0, composer.Prepared, "Клиент собрал станцию по своему описанию вопреки config сервера.");
            Assert.IsFalse(bootstrap.IsLocallyReady(config.Key));
        }

        [Test]
        public void Без_сгенерированных_станций_клиент_готов_сразу()
        {
            Assume.That(NetworkServer.active, Is.False);
            var go = new GameObject("MapRoot");
            _owned.Add(go);
            var root = go.AddComponent<MapRoot>();
            Set(root, "_stations", new[] { Station("AuthoredStation", ArsenalCompositionMode.Authored) });
            var bootstrap = go.AddComponent<MapBootstrap>();
            Invoke(bootstrap, "Awake");
            _bootstrap = bootstrap;
            var composer = new FakeComposer();
            bootstrap.Composer = composer;
            MapRunConfig config = Config(new MapRunKey(Guid.NewGuid(), 1), TestScene, Authored(TestScene, "AuthoredStation"));

            bootstrap.AcceptLocalRun(config, null);

            Assert.IsTrue(bootstrap.IsLocallyReady(config.Key), "Карта только с авторскими станциями ждёт генератора.");
            Assert.AreEqual(0, composer.Prepared);
        }

        [Test]
        public void MapRoot_допускает_станцию_в_режиме_Generated()
        {
            var go = new GameObject("MapRoot");
            _owned.Add(go);
            var root = go.AddComponent<MapRoot>();
            Set(root, "_stations", new[] { Station(StationKey) });

            IReadOnlyList<string> errors = root.ValidateBindings(includeSceneScans: false).Errors;

            Assert.IsFalse(errors.Any(e => e.StartsWith("Station.Generated") || e.StartsWith("Station.Mode")),
                "Сгенерированная станция отвергнута проверкой привязок: " + string.Join(", ", errors));
        }
    }
}
