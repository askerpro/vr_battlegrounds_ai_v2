"""Одноразовая правка: стенд раскладки пишет и читает раскладку оружия (WeaponInfo), а не стиль арсенала."""
import io
import re

p = 'Assets/Editor/VR_Battlegrounds/Arsenal/ArsenalLayoutAuthoringStandEditor.cs'
s = io.open(p, encoding='utf-8').read()

def rep(a, b):
    global s
    assert s.count(a) == 1, a[:90]
    s = s.replace(a, b)

rep("""    /// <summary>Явный bake/restore staging-проекции; source для генератора остаётся только Style.</summary>""",
    """    /// <summary>
    ///     Стенд ручной раскладки оружия в слоте. Restore показывает раскладку каждого оружия пресета над панелью
    ///     слота того вида, в ряд которого оно стоит; Bake записывает сдвинутые ручки в раскладку оружия
    ///     (<see cref="WeaponInfo.SlotLayouts" />). Стиль арсенала даёт только вид опор.
    /// </summary>""")

# Ряды и виды слотов берутся из корпуса станции лобби.
rep("""        private static string Hash(string value)""",
    """        /// <summary>Вид слота записи: вид ряда корпуса лобби, в который запись ставит оружие.</summary>
        private static ArsenalSlotRow RowOf(ArsenalPreset.Entry entry)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPath);
            if (source == null) throw new InvalidOperationException("Нет корпуса станции лобби: " + DemoPath);
            var row = source.GetComponentsInChildren<ArsenalSlotRow>(true).FirstOrDefault(r => r.RowKey == entry.Row);
            if (row == null || row.SlotPrefab == null) throw new InvalidOperationException("В корпусе лобби нет ряда «" + entry.Row + "» с префабом слота.");
            return row;
        }
        private static ArsenalPresentationZone KindOf(ArsenalPreset.Entry entry) => RowOf(entry).Zone;
        private static ArsenalPresentationSnapshot Layout(WeaponInfo weapon, ArsenalPresentationZone kind, ArsenalPresentationStyle style) =>
            ArsenalPresentationResolver.Resolve(weapon, kind, style);
        /// <summary>Раскладка оружия для вида слота в сериализованном WeaponInfo; нет — добавляется.</summary>
        private static SerializedProperty LayoutProperty(SerializedObject weapon, ArsenalPresentationZone kind)
        {
            var list = weapon.FindProperty("_slotLayouts");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("SlotKind").enumValueIndex == (int)kind) return list.GetArrayElementAtIndex(i);
            int index = list.arraySize; list.arraySize++;
            var entry = list.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("SlotKind").enumValueIndex = (int)kind;
            entry.FindPropertyRelative("Supports").arraySize = 0;
            return entry;
        }
        private static string Hash(string value)""")

rep("""            string entries = string.Join("|", stand.Preset.Entries.Select(e => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(e.Weapon)) + ":" + e.Zone + ":" +
                AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(e.Weapon))));""",
    """            string entries = string.Join("|", stand.Preset.Entries.Select(e => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(e.Weapon)) + ":" + e.Row + ":" +
                AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(e.Weapon))));""")

rep("""            ArsenalPresentationResolver.Validate(stand.Style);
            if (stand.Preset.Entries.Count != 20 || stand.Slots.Count != 20 || stand.Slots.Select(s => s.Weapon).Distinct().Count() != 20)
                throw new InvalidOperationException("Нужны 20 уникальных canonical entries/handles.");
            foreach (var slot in stand.Slots)
            {
                if (!stand.Preset.Entries.Any(e => e.Weapon == slot.Weapon && e.Zone == slot.Zone) || slot.Frame == null || slot.Frame.parent != stand.transform)
                    throw new InvalidOperationException("Staged key/frame не соответствует источнику.");
                if ((slot.Frame.localScale - Vector3.one).sqrMagnitude > 1e-12f) throw new InvalidOperationException("Slot frame не масштабируется.");
                foreach (var handle in new[] { slot.Item, slot.Magazine, slot.Card }.Concat(slot.Supports.Select(s => s.Handle))) Pose(handle, slot.Frame);
                if (slot.Zone == ArsenalPresentationZone.Shelf && slot.Supports.Count != 0)
                    throw new InvalidOperationException("Shelf не использует крючки.");
                if (slot.Supports.Select(s => s.Role).Distinct().Count() != slot.Supports.Count) throw new InvalidOperationException("Дубли support roles.");
            }""",
    """            ArsenalPresentationResolver.ValidateStyle(stand.Style);
            int count = stand.Preset.Entries.Count;
            if (stand.Slots.Count != count || stand.Slots.Select(s => s.Weapon).Distinct().Count() != count)
                throw new InvalidOperationException("Ручки стенда не соответствуют записям пресета: сначала Restore.");
            foreach (var slot in stand.Slots)
            {
                if (!stand.Preset.Entries.Any(e => e.Weapon == slot.Weapon && KindOf(e) == slot.Zone) || slot.Frame == null || slot.Frame.parent != stand.transform)
                    throw new InvalidOperationException("Ручки слота не соответствуют записи пресета.");
                if ((slot.Frame.localScale - Vector3.one).sqrMagnitude > 1e-12f) throw new InvalidOperationException("Slot frame не масштабируется.");
                foreach (var handle in new[] { slot.Item, slot.Magazine, slot.Card }.Concat(slot.Supports.Select(s => s.Handle))) Pose(handle, slot.Frame);
                if (slot.Supports.Select(s => s.Role).Distinct().Count() != slot.Supports.Count) throw new InvalidOperationException("Дубли support roles.");
            }""")

# Exception(...) больше не нужен.
s, n = re.subn(r'\n        private static SerializedProperty Exception\(SerializedObject input, ArsenalLayoutAuthoringStand\.SlotHandles slot\)\n        \{.*?\n        \}\n', '\n', s, flags=re.S)
assert n == 1

# BakePrepared: в раскладку оружия.
start = s.index('        public static string BakePrepared(ArsenalLayoutAuthoringStand stand, int selected = -1)')
end = s.index('        private static Transform Handle(Transform frame, string name, ArsenalPresentationPose pose)')
s = s[:start] + '''        public static string BakePrepared(ArsenalLayoutAuthoringStand stand, int selected = -1)
        {
            ValidateStand(stand);
            RequireCleanInputs(stand);
            if (SourceFingerprint(stand) != stand.SourceBaseline) throw new InvalidOperationException("Раскладки оружия или ассортимент изменились после Restore; staged изменения сначала сопоставьте с новым источником.");
            if (selected < -1 || selected >= stand.Slots.Count) throw new ArgumentOutOfRangeException(nameof(selected));
            var changed = new List<string>();
            foreach (var slot in stand.Slots.Where((s, index) => selected < 0 || index == selected))
            {
                var baseline = Layout(slot.Weapon, slot.Zone, stand.Style);
                var item = Pose(slot.Item, slot.Frame); var magazine = Pose(slot.Magazine, slot.Frame); var card = Pose(slot.Card, slot.Frame);
                bool supportsChanged = baseline.Supports.Count != slot.Supports.Count;
                for (int i = 0; !supportsChanged && i < slot.Supports.Count; i++) supportsChanged =
                    baseline.Supports[i].Role != slot.Supports[i].Role || baseline.Supports[i].AnchorKind != slot.Supports[i].AnchorKind || !Same(baseline.Supports[i].SlotPose, Pose(slot.Supports[i].Handle, slot.Frame));
                if (Same(item, baseline.ItemTarget) && Same(magazine, baseline.MagazineTarget) && Same(card, baseline.CardTarget) && !supportsChanged) continue;
                var weapon = new SerializedObject(slot.Weapon);
                var layout = LayoutProperty(weapon, slot.Zone);
                SetPose(layout.FindPropertyRelative("ItemTarget"), item);
                SetPose(layout.FindPropertyRelative("MagazineTarget"), magazine);
                SetPose(layout.FindPropertyRelative("CardTarget"), card);
                var supports = layout.FindPropertyRelative("Supports"); supports.arraySize = slot.Supports.Count;
                for (int i = 0; i < slot.Supports.Count; i++)
                {
                    var target = supports.GetArrayElementAtIndex(i); var handle = slot.Supports[i];
                    target.FindPropertyRelative("Role").stringValue = handle.Role;
                    target.FindPropertyRelative("AnchorKind").enumValueIndex = (int)handle.AnchorKind;
                    SetPose(target.FindPropertyRelative("SlotPose"), Pose(handle.Handle, slot.Frame));
                }
                Undo.RecordObject(slot.Weapon, "Bake Arsenal Weapon Slot Layout");
                weapon.ApplyModifiedProperties();
                EditorUtility.SetDirty(slot.Weapon); AssetDatabase.SaveAssetIfDirty(slot.Weapon);
                changed.Add(slot.Weapon.WeaponId);
            }
            if (changed.Count == 0) return "Нет изменений раскладки.";
            // Выбранный bake не уничтожает unsaved edits других слотов.
            WriteBaselines(stand, selected);
            EditorUtility.SetDirty(stand);
            return "Раскладка сохранена в оружие: " + string.Join(", ", changed) + ".";
        }
        /// <summary>Поза карточки выбранного слота — всем оружиям стенда в слотах того же вида.</summary>
        public static string BakeCardForKindPrepared(ArsenalLayoutAuthoringStand stand, int selected)
        {
            ValidateStand(stand);
            RequireCleanInputs(stand);
            if (SourceFingerprint(stand) != stand.SourceBaseline) throw new InvalidOperationException("Источник изменился после Restore.");
            var slot = stand.Slots[selected];
            var target = Pose(slot.Card, slot.Frame);
            var previous = Layout(slot.Weapon, slot.Zone, stand.Style);
            // Перед записью требуем, чтобы кроме выбранной карточки ничего не было staged.
            foreach (var other in stand.Slots)
            {
                string current = SlotFingerprint(other);
                if (other == slot)
                {
                    var stagedPosition = other.Card.localPosition; var stagedRotation = other.Card.localRotation;
                    try { other.Card.SetLocalPositionAndRotation(previous.CardTarget.Position, previous.CardTarget.Rotation); current = SlotFingerprint(other); }
                    finally { other.Card.SetLocalPositionAndRotation(stagedPosition, stagedRotation); }
                }
                if (current != other.CommittedBaseline) throw new InvalidOperationException("Сначала запеките прочие staged edits; затем карточка обновится у всех оружий этого вида слота.");
            }
            int written = 0;
            foreach (var weaponInfo in stand.Slots.Where(s => s.Zone == slot.Zone).Select(s => s.Weapon).Distinct())
            {
                var weapon = new SerializedObject(weaponInfo);
                Undo.RecordObject(weaponInfo, "Bake Arsenal Card For Slot Kind");
                SetPose(LayoutProperty(weapon, slot.Zone).FindPropertyRelative("CardTarget"), target);
                weapon.ApplyModifiedProperties(); EditorUtility.SetDirty(weaponInfo); AssetDatabase.SaveAssetIfDirty(weaponInfo); written++;
            }
            RestorePrepared(stand, true);
            return "Карточка записана в раскладку " + written + " оружий для слота " + slot.Zone + ".";
        }
''' + s[end:]

# Restore: вид слота из ряда, панель и карточка — из префаба слота ряда.
rep("""            if (stand.Preset == null || stand.Style == null) throw new InvalidOperationException("Нет canonical sources.");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPath);
            RequireCleanInputs(stand);
            if (source == null) throw new InvalidOperationException("Нет source Demo для render-only panels.");
            var templates = source.GetComponentsInChildren<FirearmSlotController>(true);
            foreach (var entry in stand.Preset.Entries)
            {
                var resolved = ArsenalPresentationResolver.Resolve(entry.Weapon, entry.Zone, stand.Style);
                if (entry.Zone == ArsenalPresentationZone.Shelf && resolved.Supports.Count != 0) throw new InvalidOperationException("Shelf Supports должны быть пусты.");
                var template = templates.Single(s => s.WeaponData == entry.Weapon);
                if (template.transform.Find("PegboardSection") == null || template.GetComponentInChildren<ArsenalPriceTag>(true) == null)
                    throw new InvalidOperationException("Source panel/card неполон.");
            }""",
    """            if (stand.Preset == null || stand.Style == null) throw new InvalidOperationException("Нет пресета или стиля арсенала стенда.");
            RequireCleanInputs(stand);
            foreach (var entry in stand.Preset.Entries)
            {
                Layout(entry.Weapon, KindOf(entry), stand.Style);
                var template = RowOf(entry).SlotPrefab;
                if (template.transform.Find("PegboardSection") == null || template.GetComponentInChildren<ArsenalPriceTag>(true) == null)
                    throw new InvalidOperationException("Префаб слота ряда «" + entry.Row + "» без панели или карточки.");
            }""")
rep("""                var resolved = ArsenalPresentationResolver.Resolve(entry.Weapon, entry.Zone, stand.Style);
                var template = templates.Single(s => s.WeaponData == entry.Weapon);
                var frame = new GameObject(entry.Weapon.WeaponId + " / " + entry.Zone).transform; frame.SetParent(stand.transform, false);
                bool isShelf = entry.Zone == ArsenalPresentationZone.Shelf;""",
    """                var kind = KindOf(entry);
                var resolved = Layout(entry.Weapon, kind, stand.Style);
                var template = RowOf(entry).SlotPrefab;
                var frame = new GameObject(entry.Weapon.WeaponId + " / " + kind).transform; frame.SetParent(stand.transform, false);
                bool isShelf = kind == ArsenalPresentationZone.Shelf;""")
rep("""                var staged = new ArsenalLayoutAuthoringStand.SlotHandles { Weapon = entry.Weapon, Zone = entry.Zone, Frame = frame };""",
    """                var staged = new ArsenalLayoutAuthoringStand.SlotHandles { Weapon = entry.Weapon, Zone = kind, Frame = frame };""")
rep("""                foreach (var support in resolved.Supports)
                {
                    if (isShelf) throw new InvalidOperationException("Shelf Style должен иметь Supports=[].");
                    var handle""", """                foreach (var support in resolved.Supports)
                {
                    var handle""")

# Инспектор.
rep("""            EditorGUILayout.ObjectField("Canonical Style", stand.Style, typeof(ArsenalPresentationStyle), false);""",
    """            EditorGUILayout.ObjectField("Стиль арсенала (вид опор)", stand.Style, typeof(ArsenalPresentationStyle), false);""")
rep("""            EditorGUILayout.HelpBox("Выберите ItemTarget/MagazineTarget/CardTarget или Peg support в Hierarchy и двигайте Move/Rotate. Масштаб не меняйте. Явный Bake сохраняет Style; Restore читает его. Shelf без крючков.", MessageType.Info);""",
    """            EditorGUILayout.HelpBox("Выберите ItemTarget/MagazineTarget/CardTarget или опору в Hierarchy и двигайте Move/Rotate. Масштаб не меняйте. Bake сохраняет раскладку в оружие (для вида слота его ряда); Restore читает её.", MessageType.Info);""")
rep("""            if (GUILayout.Button("Запечь выбранный → Style")) Run(stand, () => ArsenalLayoutAuthoring.BakePrepared(stand, selected));
            if (GUILayout.Button("Запечь все 20 → Style")) Run(stand, () => ArsenalLayoutAuthoring.BakePrepared(stand));
            if (GUILayout.Button("Карточка выбранного → общий default зоны")) Run(stand, () => ArsenalLayoutAuthoring.BakeCardDefaultPrepared(stand, selected));
            if (GUILayout.Button("Восстановить из Style"))
            {
                bool discard = !ArsenalLayoutAuthoring.HasStagedChanges(stand) || EditorUtility.DisplayDialog("Незапечённые позы", "Restore заменит рабочие poses сохранённым Style. Запеките их перед Restore, если хотите сохранить.", "Заменить рабочие позы", "Отмена");
                if (discard) Run(stand, () => { ArsenalLayoutAuthoring.RestorePrepared(stand, true); return "Проекция восстановлена из Style."; });
            }""",
    """            if (GUILayout.Button("Запечь выбранный → раскладка оружия")) Run(stand, () => ArsenalLayoutAuthoring.BakePrepared(stand, selected));
            if (GUILayout.Button("Запечь все → раскладки оружия")) Run(stand, () => ArsenalLayoutAuthoring.BakePrepared(stand));
            if (GUILayout.Button("Карточка выбранного → всем оружиям этого вида слота")) Run(stand, () => ArsenalLayoutAuthoring.BakeCardForKindPrepared(stand, selected));
            if (GUILayout.Button("Восстановить из раскладок оружия"))
            {
                bool discard = !ArsenalLayoutAuthoring.HasStagedChanges(stand) || EditorUtility.DisplayDialog("Незапечённые позы", "Restore заменит рабочие позы сохранёнными раскладками оружия. Запеките их перед Restore, если хотите сохранить.", "Заменить рабочие позы", "Отмена");
                if (discard) Run(stand, () => { ArsenalLayoutAuthoring.RestorePrepared(stand, true); return "Проекция восстановлена из раскладок оружия."; });
            }""")
rep("""                using (var lease = ArsenalEditorActions.AcquireAuthoringLease("Явный canonical layout bake/restore", stand))""",
    """                using (var lease = ArsenalEditorActions.AcquireAuthoringLease("Раскладка оружия: bake/restore", stand))""")
io.open(p, 'w', encoding='utf-8').write(s)
print('ok')
