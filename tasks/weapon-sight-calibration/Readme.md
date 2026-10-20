# weapon-sight-calibration — финальная калибровка прицелов оружия

Обновлено: 2026-10-20. Ветка `weapon-sight-calibration` от `origin/dev` `43332888b`, основной checkout.
Подробный контекст, хронология, пути и ловушки: [Details.md](Details.md).

## Цель

Свести прицелы всех стволов на дистанцию zero 15 м штатными инструментами Sight Auto-Zero
(решение пользователя 2026-10-12: «финальная калибровка — отдельной задачей»).
Эталон — TR15 с коллиматором XPS2, уже принят в шлеме.

## Состояние

- Весь принятый код, сцены и ассеты по прицелам уже в dev: этапы `sight-tr15-optic` (`5fc162060`),
  `sight-iron-kits` (`a3b06c637`), `sight-arsenal-dedupe` (`b624fdb3c`), WIP 2026-10-05/06 (`3a46cd931`, `d6c0fded0`).
  Непринятой работы по прицелам нет ни в ветках, ни в stash, ни в worktree.
- Калибровка не начата: `SaveUnlocked`/`ApplyUnlocked` = false в `SightAutoZeroWindow.cs`,
  `WeaponSightTable.asset` пуст, ни один префаб не сведён. Сдвиги решателя по стволам — в Details.md, разд. 3.
- Опора: `Docs/weapons/sight-calibration.md`, `Docs/weapons/decisions.md` (стр. 54–71),
  `experts/weapon-system/journal/2026-10-12-sight-tools.md`, backlog weapon-system п.5 и п.15.

## Материалы задачи (собраны 2026-10-20)

Описание каждого файла — [tools/Readme.md](tools/Readme.md); для других агентов то же сведено в
[Docs/weapons/sight-calibration.md](../../Docs/weapons/sight-calibration.md), разделы 9–10.
- `blender/` — `iron-kits.blend` (нарезка комплектов TR15/MX68), `iron-split.blend`, `iron-split-2.blend`
  (разборы островов 16 стволов), скриншоты pivot комплектов.
- `tools/sight-split/` — генератор `SightSplitTable.cs` и его входы `islands*.json`.
- `tools/research-2026-10-04/` — скрипты и MCP-сниппеты исследования первого этапа.
- `changelog/2026-10-12-*` — итоги аренд Unity этапа `sight-iron-kits` и сухой прогон разделителя.
- Вне Git (`reports/`, только эта машина): скриншоты разборов по стволам, `plan-477/485.json`, лог компиляции.
  Пакеты 2026-10-05 остаются в `Docs/tasks/report/weapon-sight-calibration/` и `tmp/weapon-sight-calibration/`.

## Нужны решения до реализации

1. Writer прицелов: Apply внутри `WeaponSystemAuthoring` или отдельный writer только узлов прицела
   (инвариант «один writer», backlog `sight-writer`).
2. Встроить разделитель SightSplit в `KinemationWeaponBuilder`/`HandsPackWeaponBuilder` для режима «Родной прицел».
3. Стволы без решения: SDKGun, SDK ShotGun, Machinegun.
4. Копии Sight Review: перенос в оригиналы и удаление из `WeaponRegistry`/`ArsenalCompositionCatalog` (gameplay).

## Зависимости

- `codex/r08-magazine-only` (`7e3605570`, weapon-system, не влита) меняет `R08.prefab`, `Revolver.prefab`,
  `WeaponSystemAuthoring.cs` — до правок этих файлов дождаться вливания или согласовать.
- avatar-grip пишет хваты в те же префабы оружия; level-design — `Lobby.unity` (стрельбище).

## Следующий шаг

Получить решения 1–4, затем: разделитель в сборщиках → включить Save/Apply → по стволам свести,
записать в таблицу, применить (порядок — быстрый результат) → проверка в шлеме на стрельбище Lobby.
Тесты новой логики — после приёмки пользователем.
