"""Одноразовая правка тестов адаптера (согласие map-runtime-bootstrap, сообщение 263): без авторского режима."""
import io

p = 'Assets/Tests/EditMode/Maps/MapArsenalCompositionAdapterTests.cs'
s = io.open(p, encoding='utf-8').read()
reps = [
("""    /// Play Mode-проба задачи 7 (см. прогресс-документ). Стильного пресета в проекте нет: фикстура — копия
    /// FullDemoArsenal в памяти со стилем IndustrialPegboardPresentation, ассеты не меняются.""",
 """    /// Play Mode-проба генератора. Фикстура пресета — копия FullDemoArsenal в памяти: стиль
    /// IndustrialPegboardPresentation и записи в ряд «pegboard»; ассеты не меняются."""),
("""        /// <summary>Копия FullDemoArsenal в памяти со стилем: записи, которые стиль и каталог умеют разрешить.</summary>
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
        {""",
 """        private const string RowKey = "pegboard";

        /// <summary>Копия FullDemoArsenal в памяти со стилем: первые записи, все в ряд <see cref="RowKey"/>.</summary>
        private ArsenalPreset StyledPreset(int maxEntries = 4)
        {
            var style = AssetDatabase.LoadAssetAtPath<ArsenalPresentationStyle>("Assets/Data/Weapons/IndustrialPegboardPresentation.asset");
            Assert.IsNotNull(style, "Нет IndustrialPegboardPresentation.");
            return Preset(style, maxEntries);
        }

        /// <summary>Та же копия без стиля арсенала.</summary>
        private ArsenalPreset UnstyledPreset() => Preset(null, 4);

        private ArsenalPreset Preset(ArsenalPresentationStyle style, int maxEntries)
        {
            var source = AssetDatabase.LoadAssetAtPath<ArsenalPreset>("Assets/Data/Weapons/FullDemoArsenal.asset");
            Assert.IsNotNull(source, "Нет FullDemoArsenal.");
            ArsenalPreset preset = UnityEngine.Object.Instantiate(source);
            _owned.Add(preset);
            typeof(ArsenalPreset).GetField("_presentationStyle", Fields).SetValue(preset, style);
            var kept = source.Entries.Where(e => e.Weapon != null).Take(maxEntries)
                .Select(e => new ArsenalPreset.Entry { Weapon = e.Weapon, Row = RowKey }).ToList();
            Assume.That(kept.Count, Is.GreaterThanOrEqualTo(2), "В FullDemoArsenal меньше двух записей.");
            typeof(ArsenalPreset).GetField("_entries", Fields).SetValue(preset, kept);
            return preset;
        }

        /// <summary>Оболочка станции с рядом <see cref="RowKey"/>, как её проверяет MapRoot и ждёт сборщик.</summary>
        private ArsenalStationCompositionBinding Station(string key)
        {"""),
("""            Set(binding, "_stationKey", key);
            Set(binding, "_mode", mode);
            Set(binding, "_controller", wall);
            Set(binding, "_authoredBinding", presets);
            Set(binding, "_stationAnchor", anchor);""",
 """            Set(binding, "_stationKey", key);
            Set(binding, "_controller", wall);
            Set(binding, "_stationAnchor", anchor);"""),
("""        private static MapRunBindings Authored(string scene, params string[] keys) =>
            new MapRunBindings(scene, keys.Select(k => new MapStationConfig(k, "", MapArsenalCompositionAdapter.AuthoredFallback, "shell-" + k, 1)));""",
 """        /// <summary>Записи оболочек станций, как их пишет <see cref="MapRoot"/> до описания генератора.</summary>
        private static MapRunBindings Shell(string scene, params string[] keys) =>
            new MapRunBindings(scene, keys.Select(k => new MapStationConfig(k, "", "Generated", "shell-" + k, 1)));"""),
("""            ArsenalStationCompositionBinding generated = Station(StationKey);
            ArsenalStationCompositionBinding authored = Station("AuthoredStation", ArsenalCompositionMode.Authored);
            var errors = new List<string>();

            MapArsenalCompositionAdapter adapter = MapArsenalCompositionAdapter.Describe(new[] { generated, authored },
                StyledPreset(), Catalog(), new FakeComposer(), errors);

            CollectionAssert.IsEmpty(errors, "Описание тестовой станции отказало.");
            Assert.AreEqual(1, adapter.StationCount, "Авторская станция попала в адаптер генератора.");

            MapRunBindings applied = adapter.Apply(Authored("Lobby", StationKey, "AuthoredStation"));
            MapStationConfig station = applied.Stations.Single(s => s.StationKey == StationKey);
            Assert.AreNotEqual(MapArsenalCompositionAdapter.AuthoredFallback, station.DecorationFallback,
                "Запись сгенерированной станции осталась авторской оболочкой.");""",
 """            ArsenalStationCompositionBinding generated = Station(StationKey);
            var errors = new List<string>();

            MapArsenalCompositionAdapter adapter = MapArsenalCompositionAdapter.Describe(new[] { generated },
                StyledPreset(), Catalog(), new FakeComposer(), errors);

            CollectionAssert.IsEmpty(errors, "Описание тестовой станции отказало.");
            Assert.AreEqual(1, adapter.StationCount);

            MapRunBindings applied = adapter.Apply(Shell("Lobby", StationKey));
            MapStationConfig station = applied.Stations.Single(s => s.StationKey == StationKey);
            Assert.AreNotEqual("shell-" + StationKey, station.LayoutFingerprint, "Запись станции осталась записью оболочки.");"""),
("""            Assert.AreEqual((uint)ArsenalCompositionCatalog.CurrentIdentitySchemaVersion, station.IdentitySchemaVersion);
            Assert.AreEqual("shell-AuthoredStation", applied.Stations.Single(s => s.StationKey == "AuthoredStation").LayoutFingerprint,
                "Запись авторской станции изменена адаптером.");""",
 """            Assert.AreEqual((uint)ArsenalCompositionCatalog.CurrentIdentitySchemaVersion, station.IdentitySchemaVersion);"""),
("""        public void Нестилизованный_пресет_даёт_именованный_отказ_без_отката_на_Authored()""",
 """        public void Пресет_без_стиля_даёт_именованный_отказ()"""),
("""            CollectionAssert.AreEqual(new[] { "Station.Generated.CatalogMissing:" + StationKey }, errors);

            // Карта без сгенерированных станций каталог ресурсов не требует.
            errors.Clear();
            MapArsenalCompositionAdapter authoredOnly = MapArsenalCompositionAdapter.Describe(
                new[] { Station("AuthoredStation", ArsenalCompositionMode.Authored) }, null, null, new FakeComposer(), errors);
            CollectionAssert.IsEmpty(errors);
            Assert.AreEqual(ArsenalCompositionReadiness.Passed, authoredOnly.Readiness, "Карта только с авторскими станциями не готова.");""",
 """            CollectionAssert.AreEqual(new[] { "Station.Generated.CatalogMissing:" + StationKey }, errors);

            // Карта без станций каталог ресурсов не требует.
            errors.Clear();
            MapArsenalCompositionAdapter none = MapArsenalCompositionAdapter.Describe(
                Array.Empty<ArsenalStationCompositionBinding>(), null, null, new FakeComposer(), errors);
            CollectionAssert.IsEmpty(errors);
            Assert.AreEqual(ArsenalCompositionReadiness.Passed, none.Readiness, "Карта без станций не готова.");"""),
("""            ArsenalStationCompositionBinding generated = Station(StationKey);
            ArsenalStationCompositionBinding authored = Station("AuthoredStation", ArsenalCompositionMode.Authored);
            var stations = new[] { generated, authored };
            var errors = new List<string>();
            MapArsenalCompositionAdapter adapter = MapArsenalCompositionAdapter.Describe(stations, StyledPreset(), Catalog(), new FakeComposer(), errors);
            CollectionAssert.IsEmpty(errors);
            MapRunBindings published = adapter.Apply(Authored("Lobby", StationKey, "AuthoredStation"));""",
 """            ArsenalStationCompositionBinding generated = Station(StationKey);
            var stations = new[] { generated };
            var errors = new List<string>();
            MapArsenalCompositionAdapter adapter = MapArsenalCompositionAdapter.Describe(stations, StyledPreset(), Catalog(), new FakeComposer(), errors);
            CollectionAssert.IsEmpty(errors);
            MapRunBindings published = adapter.Apply(Shell("Lobby", StationKey, "OtherStation"));"""),
("""                new string('0', 64), server.IdentitySchemaVersion), published.Stations.Single(s => s.StationKey == "AuthoredStation") });""",
 """                new string('0', 64), server.IdentitySchemaVersion), published.Stations.Single(s => s.StationKey == "OtherStation") });"""),
("""            errors.Clear();
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
            Assert.IsFalse(adapter.Verify(Config(key, "Lobby", Authored("Lobby", "AuthoredStation")), stations, errors));""",
 """            errors.Clear();
            Assert.IsFalse(adapter.Verify(Config(key, "Lobby", Shell("Lobby", "OtherStation")), stations, errors));"""),
("""        public void Без_сгенерированных_станций_клиент_готов_сразу()""", """        public void Без_станций_клиент_готов_сразу()"""),
("""            Set(root, "_stations", new[] { Station("AuthoredStation", ArsenalCompositionMode.Authored) });
            var bootstrap = go.AddComponent<MapBootstrap>();""",
 """            Set(root, "_stations", Array.Empty<ArsenalStationCompositionBinding>());
            var bootstrap = go.AddComponent<MapBootstrap>();"""),
("""            MapRunConfig config = Config(new MapRunKey(Guid.NewGuid(), 1), TestScene, Authored(TestScene, "AuthoredStation"));""",
 """            MapRunConfig config = Config(new MapRunKey(Guid.NewGuid(), 1), TestScene, Shell(TestScene));"""),
("""            Assert.IsTrue(bootstrap.IsLocallyReady(config.Key), "Карта только с авторскими станциями ждёт генератора.");""",
 """            Assert.IsTrue(bootstrap.IsLocallyReady(config.Key), "Карта без станций ждёт генератора.");"""),
("""        public void MapRoot_допускает_станцию_в_режиме_Generated()""", """        public void MapRoot_допускает_оболочку_станции_с_рядом()"""),
("""            Assert.IsFalse(errors.Any(e => e.StartsWith("Station.Generated") || e.StartsWith("Station.Mode")),
                "Сгенерированная станция отвергнута проверкой привязок: " + string.Join(", ", errors));""",
 """            Assert.IsFalse(errors.Any(e => e.StartsWith("Station.Generated") || e.StartsWith("Station.Rows") ||
                                           e.StartsWith("Station.AuthoredSlots") || e.StartsWith("Station.OwnerRefs")),
                "Оболочка станции отвергнута проверкой привязок: " + string.Join(", ", errors));"""),
("""                var root = new GameObject("FakeGeneratedSlots");
                Roots.Add(root);
                var handle = new ArsenalCompositionHandle(description, scope, station, root, Array.Empty<ArsenalSlotController>(),
                    Array.Empty<NetworkUxrIdentityAssignment>(), new List<Transform>());""",
 """                // Слот-заглушка: handle владеет слотами и уничтожает их при разборке.
                var root = new GameObject("FakeGeneratedSlot");
                root.SetActive(false);
                Roots.Add(root);
                var handle = new ArsenalCompositionHandle(description, scope, station, new[] { root.AddComponent<ArsenalSlotController>() },
                    Array.Empty<NetworkUxrIdentityAssignment>());"""),
]
for a, b in reps:
    assert s.count(a) == 1, a[:90]
    s = s.replace(a, b)
io.open(p, 'w', encoding='utf-8').write(s)
print('ok')
