# Инструменты и исходники калибровки прицелов

Собрано 2026-10-20 из `F:/CodexWorktrees/weapon-system/tasks/weapon-system/reports/` (этапы weapon-system
`sight-iron-kits`, 2026-10-12) и `tmp/weapon-sight-calibration/` основного checkout (исследование 2026-10-04/05).
Сводка механики и решений: [Docs/weapons/sight-calibration.md](../../../Docs/weapons/sight-calibration.md).

## sight-split/ — генератор таблицы разделителя

`SightSplitTable.cs` (`Assets/Editor/VR_Battlegrounds/Gameplay/SightSplit/`) сгенерирован, руками не правится:
он привязан к GUID/localFileId меша и числу вершин и треугольников.

| Файл | Что |
|---|---|
| `islands.json` | Разбор островов вшитых прицелов и роли Rear/Front: AK105, Mk14, Viper, R08 |
| `islands-2.json` | Второй проход: MP5K, Uzi, Herrington, ShotgunReal, Revolver, Gun, PPK, Scar, SRM12, SniperRifle, MKR9, M16 (часть — только разбор, без выноса) |
| `table_template.cs.txt` | Шаблон C#-файла таблицы |
| `gen_table.py` | Генератор: `python gen_table.py tasks/weapon-sight-calibration/tools/sight-split <out.cs> tasks/weapon-sight-calibration/tools/sight-split/table_template.cs.txt` |

Если сборщик пака пересобрал меш или меш правили — обновить разбор в Blender (`../blender/iron-split*.blend`),
пересохранить islands*.json, перегенерировать таблицу и прогнать `Assets/Tests/EditMode/SightSplit/`.
Сухой прогон разделителя на 11 стволов: [../changelog/2026-10-12-sight-split-dry-run-485.txt](../changelog/2026-10-12-sight-split-dry-run-485.txt).

## ../blender/ — исходники Blender

| Файл | Что |
|---|---|
| `iron-kits.blend` | Нарезка комплектов TR15 и MX68 из FBX пака: подвижные детали (мушка-столбик, барабан/пластина целика) отделены, pivot точно на точках F/R. Результат — `Assets/Art/Weapons/Sights/IronKits/*.fbx` |
| `iron-kits-screens/*.png` | До/после нарезки и pivot F/R для TR15 и MX68 |
| `iron-split.blend` | Разбор островов прицелов AK105, Mk14, Viper, R08 → `sight-split/islands.json` |
| `iron-split-2.blend` | Разбор остальных 12 стволов → `sight-split/islands-2.json` |

Python-кода нарезки нет: работа шла через Blender MCP (`execute_blender_code`). Воспроизводимость — через .blend
и islands*.json. Скриншоты разборов по стволам (16 папок, ~48 МБ) в Git не вошли: оригиналы в
`F:/CodexWorktrees/weapon-system/tasks/weapon-system/reports/iron-split/<Weapon>/`, копия — в `../reports/` (вне Git).

## research-2026-10-04/ — исследование первого этапа

Одноразовые скрипты Codex-сессии 2026-10-04/05. Читают выгрузки из `tmp/weapon-sight-calibration/` основного
checkout (вне Git), поэтому как есть не запускаются; ценны как методика.

| Файл | Что |
|---|---|
| `audit.py` | Офлайн-аудит YAML префабов/мешей: где у стволов прицелы (без Unity) |
| `analyze_geometry.py`, `iron_geometry_notes.py`, `aperture_vertices.py`, `mount_vertices.py`, `compact_mount_vertices.py`, `mk14_sight_components.py` | Разбор геометрии прицелов по выгрузкам: апертура SRM12, площадки крепления, компоненты Mk14 |
| `contact_sheets.py` | Сборка контактных листов скриншотов (PIL) |
| `*-preview.cs.txt`, `reference-draft.cs.txt` | Сниппеты MCP `execute_code`: превью доноров и вариантов механики SniperRifle (SRS, open iron, compact), черновые R/F |
| `create-srs-matched-material.cs.txt` | Создание `SniperRifle_SRS_Iron.mat` из `M_TR15_Ironsights` |
