"""Одноразовая правка: записи пресета «оружие → ряд», представление разрешается при сборке слота."""
import io

def edit(p, reps):
    s = io.open(p, encoding='utf-8').read()
    for a, b in reps:
        assert s.count(a) == 1, (p, a[:80])
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8').write(s)

P = 'Assets/Scripts/Arsenal/'
edit(P + 'ArsenalStationResolver.cs', [
("""            // Место в ряду — порядок записей пресета внутри одного ряда (ключ ряда, а без ключа — зона).""",
 """            // Место в ряду — порядок записей пресета внутри одного ряда."""),
("""                string row=RowOf(entry);""", """                string row=entry.RowKey;"""),
("""        /// <summary>Группа ряда записи: явный ключ ряда или зона.</summary>
        public static string RowOf(ArsenalFrozenEntry entry) =>
            string.IsNullOrEmpty(entry.RowKey) ? "zone:"+entry.Zone : "row:"+entry.RowKey;

""", ""),
("""        /// <summary>
        /// Корпус: заказанный явно; иначе наименьший по числу мест, в который помещаются ряды по количеству слотов;
        /// иначе универсальный; иначе без корпуса. Выбор по количеству, не по размерам оружия.
        /// </summary>""", """        /// <summary>
        /// Корпус: заказанный явно; иначе универсальный; иначе без корпуса. Корпус станции с рядами — префаб на сцене,
        /// каталожный корпус только оформляет его.
        /// </summary>"""),
("""            int peg=slots.Count(s=>s.Entry.Zone==ArsenalPresentationZone.Pegboard),shelf=slots.Count-peg;
""", ""),
("""            var winner=set.Where(d=>!d.DefaultUniversal&&d.PegCapacity>=peg&&d.ShelfCapacity>=shelf)
                .OrderBy(d=>d.PegCapacity+d.ShelfCapacity).ThenBy(d=>d.PegCapacity).ThenBy(d=>d.ShelfCapacity)
                .ThenBy(d=>d.DecorationId,StringComparer.Ordinal).FirstOrDefault();
            if(winner!=null)return new ArsenalDecorationSelection(ArsenalDecorationFallback.Sized,winner,diagnostics);
            winner=available""", """            var winner=available"""),
("""        /// Отпечаток применённых настроек: что, в какой ряд и каким по счёту, с какими позами стиля. Хешируются только
        /// входы, а не вычисленные позы: вычисления с плавающей точкой на ПК и Quest (ARM64, FMA) могут дать разные
        /// биты. Геометрия рядов в отпечаток не входит — она часть сцены, одинаковой на всех машинах.""",
 """        /// Отпечаток применённых настроек: что, в какой ряд и каким по счёту, раскладки оружия и стиль. Хешируются
        /// только входы, а не вычисленные позы: вычисления с плавающей точкой на ПК и Quest (ARM64, FMA) могут дать
        /// разные биты. Геометрия рядов в отпечаток не входит — она часть сцены, одинаковой на всех машинах."""),
("""                hash.Add((int)selection.Kind);hash.Add(selection.DecorationId);""",
 """                hash.Add((int)selection.Kind);hash.Add(selection.DecorationId);hash.Add(input.Style!=null?input.Style.name:"");"""),
("""                    var e=slot.Entry;var p=e.Presentation;
                    hash.Add(e.NetworkIndex);hash.Add(e.LogicalSlotKey);hash.Add((int)e.Zone);hash.Add(e.RowKey);
                    hash.Add(slot.RowIndex);hash.Add(rowCounts[RowOf(e)]);
                    hash.Add(p.ItemTarget);hash.Add(p.MagazineTarget);hash.Add(p.CardTarget);hash.Add(p.CardSize.x);hash.Add(p.CardSize.y);hash.Add(p.CardFontSize);
                    foreach(var s in p.Supports){hash.Add(s.Role);hash.Add((int)s.AnchorKind);hash.Add(s.SlotPose);}""",
 """                    var e=slot.Entry;
                    hash.Add(e.NetworkIndex);hash.Add(e.LogicalSlotKey);hash.Add(e.RowKey);
                    hash.Add(slot.RowIndex);hash.Add(rowCounts[e.RowKey]);
                    foreach(var p in e.WeaponResource.SlotLayouts.OrderBy(l=>(int)l.SlotKind))
                    {
                        hash.Add((int)p.SlotKind);hash.Add(p.ItemTarget);hash.Add(p.MagazineTarget);hash.Add(p.CardTarget);
                        hash.Add(p.CardSize.x);hash.Add(p.CardSize.y);hash.Add(p.CardFontSize);
                        foreach(var s in p.Supports){hash.Add(s.Role);hash.Add((int)s.AnchorKind);hash.Add(s.SlotPose);}
                    }"""),
])
edit(P + 'ArsenalSlotRow.cs', [
("""        [Tooltip("Ключ ряда для записи пресета. Пусто — ряд берёт записи своей зоны.")]""",
 """        [Tooltip("Ключ ряда: записи пресета указывают его, чтобы попасть в этот ряд.")]"""),
("""        [Tooltip("Зона представления: какие позы стиля применяются к слотам этого ряда.")]""",
 """        [Tooltip("Вид слота ряда: какую раскладку оружия (панель или полка) применять к его слотам.")]"""),
("""        /// <summary>Подходит ли ряд записи пресета: по ключу ряда, а без ключа — по зоне.</summary>
        public bool Accepts(string rowKey, ArsenalPresentationZone zone) =>
            string.IsNullOrEmpty(rowKey) ? _zone == zone : string.Equals(RowKey, rowKey, StringComparison.Ordinal);

""", ""),
])
edit(P + 'ArsenalSlotBuilder.cs', [
("""    ///     Сборщик одного слота сгенерированной станции: префаб слота + содержимое (оружие, карточка, позы оружия,
    ///     магазина и опор из стиля) → один настроенный слот. Где слот будет стоять и кто его соседи, не знает:
    ///     это работа ряда (<see cref="ArsenalSlotRow" />).""",
 """    ///     Сборщик одного слота станции: префаб слота + оружие (карточка, раскладка оружия для вида слота) и внешний
    ///     вид арсенала → один настроенный слот. Где слот будет стоять и кто его соседи, не знает: это работа ряда
    ///     (<see cref="ArsenalSlotRow" />)."""),
("""        public static ArsenalSlotController Build(ArsenalSlotController slotPrefab, Transform inactiveStaging,
            ArsenalFrozenEntry entry, MapRunKey run, string stationKey, List<NetworkUxrIdentityAssignment> assignments)
        {
            if (slotPrefab == null || inactiveStaging == null || entry == null || assignments == null)""",
 """        public static ArsenalSlotController Build(ArsenalSlotController slotPrefab, ArsenalPresentationZone slotKind,
            ArsenalPresentationSnapshot presentation, Transform inactiveStaging, ArsenalFrozenEntry entry, MapRunKey run,
            string stationKey, List<NetworkUxrIdentityAssignment> assignments)
        {
            if (slotPrefab == null || presentation == null || inactiveStaging == null || entry == null || assignments == null)"""),
("""            ArsenalSupportProjection.MaterializePresentation(firearm, entry.Presentation.Snapshot);""",
 """            firearm.ConfigurePresentationZone(slotKind);
            ArsenalSupportProjection.MaterializePresentation(firearm, presentation);"""),
])
edit(P + 'ArsenalStationComposer.cs', [
("""                var assignments = new List<NetworkUxrIdentityAssignment>();
                var rowSlots""", """                var assignments = new List<NetworkUxrIdentityAssignment>();
                var presentations = new List<ArsenalPresentationSnapshot>(description.Slots.Count);
                var rowSlots"""),
("""                    ArsenalSlotRow row = FindRow(rows, manifest.Entry);
                    if (row == null)
                        throw new InvalidOperationException("ArsenalComposer.MissingRow:" + station.StationKey + ":" + ArsenalStationResolver.RowOf(manifest.Entry));
                    if (row.SlotPrefab == null) throw new InvalidOperationException("ArsenalComposer.RowWithoutSlotPrefab:" + row.name);
                    ArsenalSlotController slot = ArsenalSlotBuilder.Build(row.SlotPrefab, staging.transform, manifest.Entry, scope.Key,
                        description.StationKey, assignments);
                    slots.Add(slot);""",
 """                    ArsenalSlotRow row = FindRow(rows, manifest.Entry.RowKey);
                    if (row == null)
                        throw new InvalidOperationException("ArsenalComposer.MissingRow:" + station.StationKey + ":" + manifest.Entry.RowKey);
                    if (row.SlotPrefab == null) throw new InvalidOperationException("ArsenalComposer.RowWithoutSlotPrefab:" + row.name);
                    // Раскладка оружия — для вида слота этого ряда; внешний вид — стиль арсенала.
                    ArsenalPresentationSnapshot presentation;
                    try { presentation = ArsenalPresentationResolver.Resolve(manifest.Entry.WeaponResource, row.Zone, description.Style); }
                    catch (InvalidOperationException ex) { throw new InvalidOperationException("ArsenalComposer.Presentation:" + manifest.Entry.LogicalSlotKey + ":" + ex.Message); }
                    ArsenalSlotController slot = ArsenalSlotBuilder.Build(row.SlotPrefab, row.Zone, presentation, staging.transform,
                        manifest.Entry, scope.Key, description.StationKey, assignments);
                    slots.Add(slot);
                    presentations.Add(presentation);"""),
("""                presets.Prepare(description, slots);""", """                presets.Prepare(description, slots, presentations);"""),
("""        /// <summary>Ряд записи: по ключу ряда, без ключа — первый ряд её зоны в порядке иерархии корпуса.</summary>
        private static ArsenalSlotRow FindRow(ArsenalSlotRow[] rows, ArsenalFrozenEntry entry)
        {
            foreach (ArsenalSlotRow row in rows)
                if (row.Accepts(entry.RowKey, entry.Zone)) return row;
            return null;
        }""", """        /// <summary>Ряд записи по ключу; два ряда с одним ключом на станции — ошибка корпуса.</summary>
        private static ArsenalSlotRow FindRow(ArsenalSlotRow[] rows, string rowKey)
        {
            ArsenalSlotRow found = null;
            foreach (ArsenalSlotRow row in rows)
            {
                if (!string.Equals(row.RowKey, rowKey, StringComparison.Ordinal)) continue;
                if (found != null) throw new InvalidOperationException("ArsenalComposer.DuplicateRowKey:" + rowKey);
                found = row;
            }
            return found;
        }"""),
])
edit(P + 'ArsenalStationPresetBinding.cs', [
("""        public void Prepare(ArsenalStationDescription description, IReadOnlyList<ArsenalSlotController> slots)""",
 """        public void Prepare(ArsenalStationDescription description, IReadOnlyList<ArsenalSlotController> slots,
            IReadOnlyList<ArsenalPresentationSnapshot> presentations)"""),
("""            if (slots == null || slots.Count != description.Slots.Count)""",
 """            if (slots == null || presentations == null || slots.Count != description.Slots.Count || presentations.Count != slots.Count)"""),
("""                prepared.Add(slot, entry.Presentation.Snapshot);""",
 """                if (presentations[i] == null) throw new InvalidOperationException("ArsenalPreset.Prepare: нет представления слота " + i + ".");
                prepared.Add(slot, presentations[i]);"""),
("""            for (int i = 0; i < slots.Count; i++)
            {
                slots[i].ConfigurePresentationZone(description.Slots[i].Entry.Zone);
                slots[i].ConfigureWeapon(description.Slots[i].Entry.WeaponResource);
            }""", """            for (int i = 0; i < slots.Count; i++) slots[i].ConfigureWeapon(description.Slots[i].Entry.WeaponResource);"""),
])
print('ok')
