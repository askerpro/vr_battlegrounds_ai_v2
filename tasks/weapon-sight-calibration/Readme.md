# weapon-sight-calibration — финальная калибровка прицелов оружия

Обновлено: 2026-10-20. Ветка `weapon-sight-calibration` от `origin/dev` `43332888b`, основной checkout.
Подробности: [Details.md](Details.md), разд. 0 — действующий подход и стволы по вариантам.

## Цель

Свести прицелы всех стволов на zero 15 м стендом **Sight Auto-Zero** (weapon-system, 2026-10-12, `a3b06c637`)
и записать результат в игровые префабы. Решение пользователя 2026-10-12: «финальная калибровка — отдельной задачей».

## Подход (уточнение пользователя 2026-10-20)

- Действующий стенд — окно `Tools/VR Battlegrounds/Weapons/Sight Auto-Zero`, три варианта прицела:
  **SightKit_MX68**, **SightKit_TR15**, **Родной прицел**.
- Родной прицел: целик и мушка ствола отделены от меша корпуса в отдельные подвижные объекты с pivot на точке
  прицеливания. Острова определены в Blender (`blender/iron-split*.blend` → `tools/sight-split/islands*.json` →
  `SightSplitTable.cs`, 11 стволов), режет `SightPartSplitter` — **пока только в памяти на копии стенда**.
- Ствол без своего прицела получает готовый комплект TR15 или MX68, вырезанный в Blender (`blender/iron-kits.blend`).
- Ручной стенд `WeaponSightManualCalibration` и подход первого агента (маркеры, копии Sight Review, доноры)
  **устарели**. В силе остаются модель zero, инварианты ShotSource и коллиматор TR15.

## Состояние

- Весь код стенда, комплекты и разборы — в dev. Непринятой работы по прицелам нет ни в ветках, ни в worktree.
- Калибровка не начата: `SaveUnlocked`/`ApplyUnlocked` = false, `WeaponSightTable.asset` пуст, ни один префаб
  не сведён, разделённые меши не сохранены. Принят только коллиматор TR15.

## Материалы задачи

Опись — [tools/Readme.md](tools/Readme.md); для других агентов — [Docs/weapons/sight-calibration.md](../../Docs/weapons/sight-calibration.md),
разделы 9–10.
- `blender/` — `iron-kits.blend`, `iron-split.blend`, `iron-split-2.blend`, скриншоты pivot комплектов.
- `tools/sight-split/` — генератор `SightSplitTable.cs` и входы `islands*.json`.
- `tools/research-2026-10-04/` — скрипты первого этапа (история).
- `changelog/2026-10-12-*` — итоги аренд Unity стенда и сухой прогон разделителя.
- Вне Git (`reports/`, эта машина): скриншоты разборов по стволам, `plan-477/485.json`, лог компиляции.

## Нужны решения до реализации

1. Где делать постоянное разделение родных прицелов: в сборщиках `KinemationWeaponBuilder`/`HandsPackWeaponBuilder`
   (решение 2026-10-12) — подтвердить, куда сохранять меши детали и корпуса.
2. Writer прицелов: `WeaponSightAuthoring` или шаг внутри `WeaponSystemAuthoring` (инвариант «один writer»).
3. Комплект для SRM12 и SniperRifle (TR15 или MX68); стволы без решения: SDKGun, SDK ShotGun, Machinegun.
4. Объём уборки первого этапа: ручной стенд, `Weapons/Calibration/*`, копии Sight Review (+ записи в
   `WeaponRegistry`/`ArsenalCompositionCatalog`, зона gameplay).

## Зависимости

- `codex/r08-magazine-only` (`7e3605570`, weapon-system, не влита) меняет `R08.prefab`, `Revolver.prefab`,
  `WeaponSystemAuthoring.cs` — до правок этих файлов дождаться вливания или согласовать.
- avatar-grip пишет хваты в те же префабы оружия; level-design — `Lobby.unity` (стрельбище).

## Следующий шаг

Получить решения 1–4, затем: постоянное разделение в сборщиках → включить Save/Apply → по стволам свести,
записать в таблицу, применить (порядок — быстрый результат) → проверка в шлеме на стрельбище Lobby.
Тесты новой логики — после приёмки пользователем.
