"""Одноразовая правка (согласие map-runtime-bootstrap, сообщение 263): удаление авторской ветки станций."""
import io

def edit(p, reps):
    s = io.open(p, encoding='utf-8').read()
    for a, b in reps:
        assert s.count(a) == 1, (p, a[:90])
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8').write(s)

M = 'Assets/Scripts/Maps/Runtime/'
edit(M + 'MapRoot.cs', [
("""                if (!Enum.IsDefined(typeof(ArsenalCompositionMode), binding.Mode)) errors.Add("Station.Mode.Invalid:" + key);
""", ""),
("""                if (binding.Controller == null || binding.Controller.gameObject != binding.gameObject ||
                    binding.AuthoredBinding == null || binding.AuthoredBinding.gameObject != binding.gameObject ||""",
 """                if (binding.Controller == null || binding.Controller.gameObject != binding.gameObject ||
                    binding.GetComponent<ArsenalStationPresetBinding>() == null ||"""),
("""                    errors.Add("Station.OwnerRefs:" + key);
                var ni = binding.StationIdentity;""",
 """                    errors.Add("Station.OwnerRefs:" + key);
                // Слоты станции вешает сборщик в ряды корпуса: корпус без рядов собрать нельзя.
                if (binding.GetComponentsInChildren<ArsenalSlotRow>(true).Length == 0) errors.Add("Station.Rows.Missing:" + key);
                // Авторских слотов в корпусе нет: их ID пересекались бы с ID сборщика.
                if (binding.GetComponentsInChildren<ArsenalSlotController>(true).Length != 0) errors.Add("Station.AuthoredSlots:" + key);
                var ni = binding.StationIdentity;"""),
("""                // Hash только читает authored IDs/frames; не назначает UID или семантический seed. Сгенерированная станция
                // здесь описана только оболочкой: оформление, layout hash и версию схемы ID её записи до публикации
                // config подставляет MapBootstrap из описания генератора (MapArsenalCompositionAdapter.Apply).
                bool authored = binding.Mode == ArsenalCompositionMode.Authored;
                stationConfigs.Add(new MapStationConfig(key, string.Empty,
                    authored ? MapArsenalCompositionAdapter.AuthoredFallback : "Generated", AuthoredFingerprint(binding, uids), 1));""",
 """                // Здесь станция описана только оболочкой: оформление, layout hash и версию схемы ID её записи до
                // публикации config подставляет MapBootstrap из описания генератора (MapArsenalCompositionAdapter.Apply).
                stationConfigs.Add(new MapStationConfig(key, string.Empty, "Generated", ShellFingerprint(binding, uids), 1));"""),
("""        private static string AuthoredFingerprint(ArsenalStationCompositionBinding binding, Guid[] uids)
        {
            var values = new List<string> { binding.StationKey ?? "", binding.StationIdentity != null ? binding.StationIdentity.sceneId.ToString() : "" };
            values.AddRange(uids.OrderBy(id => id).Select(id => id.ToString("D")));
            foreach (var slot in binding.Controller != null ? binding.Controller.Slots : Array.Empty<ArsenalSlotController>())
                values.Add(slot != null ? ((int)slot.PresentationZone).ToString() + ":" +
                    (slot.ItemAnchor != null ? slot.ItemAnchor.UniqueId.ToString("D") : "missing") : "missing");
            // Stable placement frames плюс role IDs. Animated target/item transforms не являются authoring source.""",
 """        /// <summary>Отпечаток оболочки станции: ключ, sceneId, UniqueId корпуса и кадры размещения.</summary>
        private static string ShellFingerprint(ArsenalStationCompositionBinding binding, Guid[] uids)
        {
            var values = new List<string> { binding.StationKey ?? "", binding.StationIdentity != null ? binding.StationIdentity.sceneId.ToString() : "" };
            values.AddRange(uids.OrderBy(id => id).Select(id => id.ToString("D")));
            // Стабильные кадры размещения; анимируемые цели и предметы источником не являются."""),
])
print('ok')
