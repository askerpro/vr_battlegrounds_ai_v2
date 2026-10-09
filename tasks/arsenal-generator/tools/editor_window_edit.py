"""Одноразовая правка: окно и статусы редактора арсенала — записи «оружие → ряд», без авторских команд."""
import io
import re

def edit(p, reps):
    s = io.open(p, encoding='utf-8').read()
    for a, b in reps:
        assert s.count(a) == 1, (p, a[:90])
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8').write(s)
    return s

E = 'Assets/Editor/VR_Battlegrounds/Arsenal/'
edit(E + 'ArsenalEditorStatus.cs', [
("""        public static string Capacity(string path)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var wall = root != null ? root.GetComponent<ArsenalWallController>() : null;
            if (wall == null) return "Станция отсутствует";
            return $"{wall.Slots.Count} слотов: панель {wall.Slots.Count(s => s.PresentationZone == ArsenalPresentationZone.Pegboard)}, полка {wall.Slots.Count(s => s.PresentationZone == ArsenalPresentationZone.Shelf)}";
        }""",
 """        /// <summary>Ряды корпуса станции: ключ и вид слота.</summary>
        public static string Capacity(string path)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null || root.GetComponent<ArsenalWallController>() == null) return "Станция отсутствует";
            var rows = root.GetComponentsInChildren<ArsenalSlotRow>(true);
            if (rows.Length == 0) return "У корпуса нет рядов";
            return "Ряды: " + string.Join(", ", rows.Select(r => r.RowKey + " (" + r.Zone + ")"));
        }"""),
("""            if (entries.Any(e => !Enum.IsDefined(typeof(ArsenalPresentationZone), e.Zone))) return "В ассортименте есть неизвестная зона.";""",
 """            if (entries.Any(e => string.IsNullOrWhiteSpace(e.Row))) return "Каждой строке нужен ряд корпуса.";"""),
("""            string invalid = ValidateEntries(values);
            if (invalid != null || preset.PresentationStyle == null) return invalid;
            try
            {
                foreach (var entry in values) ArsenalPresentationResolver.Resolve(entry.Weapon, entry.Zone, preset.PresentationStyle);
                return null;
            }
            catch (InvalidOperationException exception) { return "Недопустимый presentation style: " + exception.Message; }
        }""",
 """            string invalid = ValidateEntries(values);
            if (invalid != null) return invalid;
            if (preset.PresentationStyle == null) return "Ассортименту нужен стиль арсенала (внешний вид станции).";
            return null;
        }"""),
("""        public static string CapacityProblem(IEnumerable<ArsenalPreset.Entry> entries, MapData map)
        {
            if (map == null) return "Выберите карту.";
            string path = map.sceneName == "Lobby" ? EditorTools.ArsenalPresetAssetBuilder.DemoPath : EditorTools.ArsenalPresetAssetBuilder.CommonPath;
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var wall = root != null ? root.GetComponent<ArsenalWallController>() : null;
            if (wall == null) return "Нет станции для проверки вместимости: " + path;
            if (wall.Slots.Any(s => s == null) || wall.Slots.Distinct().Count() != wall.Slots.Count)
                return "У станции пустые или повторные ссылки слотов: " + path;
            foreach (ArsenalPresentationZone zone in Enum.GetValues(typeof(ArsenalPresentationZone)))
                if (entries.Count(e => e.Zone == zone) > wall.Slots.Count(s => s.PresentationZone == zone))
                    return "Не хватает слотов зоны " + zone + " для карты " + map.displayName;
            return null;
        }""",
 """        /// <summary>
        /// Ассортимент ложится на корпус станции карты: ряд каждой записи есть в корпусе, у оружия есть раскладка
        /// для вида слота этого ряда. Вместимость ряда не проверяется — ряд вешает столько слотов, сколько дали.
        /// </summary>
        public static string CapacityProblem(IEnumerable<ArsenalPreset.Entry> entries, MapData map)
        {
            if (map == null) return "Выберите карту.";
            string path = map.sceneName == "Lobby" ? EditorTools.ArsenalPresetAssetBuilder.DemoPath : EditorTools.ArsenalPresetAssetBuilder.CommonPath;
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null || root.GetComponent<ArsenalWallController>() == null) return "Нет станции карты: " + path;
            var rows = root.GetComponentsInChildren<ArsenalSlotRow>(true);
            foreach (var entry in entries)
            {
                var row = rows.FirstOrDefault(r => r.RowKey == entry.Row);
                if (row == null) return "В корпусе станции карты " + map.displayName + " нет ряда «" + entry.Row + "».";
                if (entry.Weapon != null && !entry.Weapon.TryGetSlotLayout(row.Zone, out _))
                    return "У оружия " + entry.Weapon.WeaponId + " нет раскладки для слота " + row.Zone + " (ряд «" + entry.Row + "»).";
            }
            return null;
        }"""),
])

w = E + 'ArsenalEditorWindow.cs'
s = io.open(w, encoding='utf-8').read()
s = s.replace("""entry.Zone = (ArsenalPresentationZone)EditorGUILayout.EnumPopup(entry.Zone, GUILayout.Width(100));""",
              """entry.Row = EditorGUILayout.TextField(entry.Row, GUILayout.Width(100));""", 1)
s = s.replace("""draft.Add(new ArsenalPreset.Entry { Weapon = weapon, Zone = ArsenalPresentationZone.Pegboard });""",
              """draft.Add(new ArsenalPreset.Entry { Weapon = weapon, Row = "pegboard" });""", 1)
s = s.replace("""item.FindPropertyRelative("Zone").enumValueIndex = (int)entries[i].Zone; }""",
              """item.FindPropertyRelative("Row").stringValue = entries[i].Row; }""", 1)
s = s.replace("""". Порядок и зоны сохраняются как показано.\"""", """". Порядок и ряды сохраняются как показано.\"""", 1)
s = s.replace("""return "Ассортимент сохранён. Геометрия станции не расширялась.";""",
              """return "Ассортимент сохранён. Станцию соберёт генератор при запуске карты.";""", 1)
for pattern in [
    r'\n            EditorGUILayout\.HelpBox\("Начальная конфигурация рассчитана на каталог 20 оружий[^\n]*\n',
    r'\n            Command\("Начальная конфигурация 20/10 \(восстановление\)"[^\n]*\n',
    r'\n            EditorGUILayout\.HelpBox\("Источник — сохранённый «Полный ассортимент лобби»[^\n]*\n',
    r'\n            Command\("Пересобрать префаб станции лобби"[^\n]*\n',
    r'\n            Command\("Обновить четыре станции лобби"[^\n]*\n',
    r'\n            if \(GUILayout\.Button\("Проверить запасные магазины станций"\)\)[^\n]*\n',
    r'\n            Command\("Настроить магазины двух станций и историю магазинов каталога"[^\n]*\n',
    r'\n            Command\("Удалить старые превью из четырёх префабов и активной сцены"[^\n]*\n',
]:
    s, n = re.subn(pattern, '\n', s)
    assert n == 1, pattern
s = s.replace("""            EditorGUILayout.HelpBox("Поддержанного сборщика общей геометрии Common нет. Ассортимент её не расширяет.", MessageType.Info);""",
              """            EditorGUILayout.HelpBox("Слоты станций собирает генератор при запуске карты: корпус задаёт ряды (ArsenalSlotRow), пресет — какое оружие в какой ряд, раскладку в слоте — оружие.", MessageType.Info);""", 1)
s = s.replace("""            if (wall != null) EditorGUILayout.LabelField("Якоря: " + wall.Slots.Count(s => s.ItemAnchor != null) + "; карточки: " + root.GetComponentsInChildren<ArsenalPriceTag>(true).Length);
""", "", 1)
io.open(w, 'w', encoding='utf-8').write(s)
print('ok')
