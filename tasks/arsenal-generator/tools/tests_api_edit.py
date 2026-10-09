"""Одноразовая правка тестов генератора под новый API (без авторского режима, запись = оружие + ряд)."""
import io
import re

def edit(p, reps):
    s = io.open(p, encoding='utf-8').read()
    for a, b in reps:
        assert s.count(a) == 1, (p, a[:90])
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8').write(s)
    return s

T = 'Assets/Tests/EditMode/Arsenal/'
edit(T + 'ArsenalStationResolverTests.cs', [
("""            _preset = Object.Instantiate(source);
            SetStyle(_preset, AssetDatabase.LoadAssetAtPath<ArsenalPresentationStyle>(StylePath));""",
 """            _preset = Object.Instantiate(source);
            SetStyle(_preset, AssetDatabase.LoadAssetAtPath<ArsenalPresentationStyle>(StylePath));
            // Записи нового формата: оружие и ряд корпуса.
            SetEntries(_preset, source.Entries.Where(e => e.Weapon != null)
                .Select((e, i) => new ArsenalPreset.Entry { Weapon = e.Weapon, Row = i % 2 == 0 ? "pegboard" : "shelf" }).ToList());"""),
("""                Assert.AreEqual(_preset.Entries[i].Zone, description.Slots[i].Entry.Zone);""",
 """                Assert.AreEqual(_preset.Entries[i].Row, description.Slots[i].Entry.RowKey);"""),
])

p = T + 'ArsenalWallSlotInstallTests.cs'
s = io.open(p, encoding='utf-8').read()
s = s.replace("""    /// не теряется (класс NET-17: сгенерированная станция собирается позже спавна). Сгенерированная станция
    /// не читает слоты из иерархии — порядок детей не порядок манифеста — и получает их один раз через
    /// <see cref="ArsenalWallController.InstallGeneratedSlots" />. После установки неверный индекс —
    /// именованная ошибка, а не молчаливая потеря.""",
"""    /// не теряется (класс NET-17: станция собирается позже спавна). Стена не читает слоты из иерархии —
    /// порядок детей не порядок манифеста — и получает их один раз через
    /// <see cref="ArsenalWallController.InstallSlots" />. После установки неверный индекс —
    /// именованная ошибка, а не молчаливая потеря.""", 1)
s = s.replace("""        private static readonly FieldInfo Mode = typeof(ArsenalStationCompositionBinding).GetField("_mode", Private);
""", "", 1)
s = s.replace("""        private ArsenalWallController Wall(string name, bool generated)""", """        private ArsenalWallController Wall(string name)""", 1)
s = s.replace("""            if (generated) Mode.SetValue(root.AddComponent<ArsenalStationCompositionBinding>(), ArsenalCompositionMode.Generated);
""", "", 1)
s, n = re.subn(r'\n        \[Test\]\n        public void Авторская_станция_берёт_слоты_из_иерархии\(\)\n        \{.*?\n        \}\n', '\n', s, flags=re.S)
assert n == 1
s = s.replace('Wall("EarlyAuthored", false)', 'Wall("Early")').replace('Wall("Generated", true)', 'Wall("Generated")').replace('Wall("GeneratedBad", true)', 'Wall("GeneratedBad")')
s = s.replace('InstallGeneratedSlots', 'InstallSlots')
s = s.replace('"Сгенерированная станция не читает иерархию."', '"Стена не читает иерархию."')
io.open(p, 'w', encoding='utf-8').write(s)
print('ok')
