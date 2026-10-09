"""Одноразовая правка (согласие map-runtime-bootstrap, сообщение 263): адаптер и MapBootstrap без авторской ветки."""
import io

def edit(p, reps):
    s = io.open(p, encoding='utf-8').read()
    for a, b in reps:
        assert s.count(a) == 1, (p, a[:90])
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8').write(s)

M = 'Assets/Scripts/Maps/Runtime/'
edit(M + 'MapArsenalCompositionAdapter.cs', [
("""    /// Порядок: <see cref="Describe"/> строит описание каждой станции в режиме Generated из тех же входов, что у всех""",
 """    /// Порядок: <see cref="Describe"/> строит описание каждой станции из тех же входов, что у всех"""),
("""    /// (handle принадлежит ему с момента сборки), своего teardown у адаптера нет. Авторские станции сюда не попадают.""",
 """    /// (handle принадлежит ему с момента сборки), своего teardown у адаптера нет."""),
("""        /// <summary>Значение <see cref="MapStationConfig.DecorationFallback"/> авторской станции (пишет <see cref="MapRoot"/>).</summary>
        internal const string AuthoredFallback = "Authored";

""", ""),
("""        /// Описания станций в режиме Generated. Отказ описания (нестилизованный пресет, нет каталога, переполнение
        /// места) — именованная ошибка в <paramref name="errors"/>: <c>Station.Generated.&lt;вид&gt;:&lt;StationKey&gt;</c>.""",
 """        /// Описания станций карты. Отказ описания (пресет без стиля или без ряда записи, нет каталога) — именованная
        /// ошибка в <paramref name="errors"/>: <c>Station.Generated.&lt;вид&gt;:&lt;StationKey&gt;</c>."""),
("""                if (station == null || station.Mode != ArsenalCompositionMode.Generated) continue;""",
 """                if (station == null) continue;"""),
("""        /// Клиент: опубликованный сервером config описывает те же станции. Режим (Authored/Generated) каждой станции
        /// сцены совпадает; у сгенерированной совпадают оформление, fallback, layout hash и версия схемы ID.""",
 """        /// Клиент: опубликованный сервером config описывает те же станции: совпадают оформление, fallback, layout hash
        /// и версия схемы ID."""),
("""            foreach (ArsenalStationCompositionBinding binding in sceneStations ?? Array.Empty<ArsenalStationCompositionBinding>())
            {
                if (binding == null || binding.Mode != ArsenalCompositionMode.Authored) continue;
                if (published.TryGetValue(binding.StationKey ?? "", out MapStationConfig server) && server.DecorationFallback != AuthoredFallback)
                    errors.Add("Station.Generated.ModeMismatch:" + binding.StationKey);
            }

""", ""),
("""                if (server.DecorationFallback == AuthoredFallback) errors.Add("Station.Generated.ModeMismatch:" + key);
                else if (server.IdentitySchemaVersion""", """                if (server.IdentitySchemaVersion"""),
])
edit(M + 'MapBootstrap.cs', [
("""            // config, клиент сверяет с ними своё описание. Отказ описания — отказ запуска, без отката на Authored.""",
 """            // config, клиент сверяет с ними своё описание. Отказ описания — отказ запуска."""),
("""            try
            {
                // Ассортимент авторских станций — из канонического паспорта карты, до любой выдачи. Повтор того же
                // preset безопасен. Сгенерированные станции готовит сборщик по описанию, не этот путь.
                foreach (ArsenalStationCompositionBinding station in _bindings.Stations)
                    if (station.Mode == ArsenalCompositionMode.Authored)
                        station.AuthoredBinding.Prepare(map.arsenalPreset);
            }
            catch (Exception error)
            {
                FailRun("Composition.Exception", error.Message);
                return;
            }

""", ""),
])
print('ok')
