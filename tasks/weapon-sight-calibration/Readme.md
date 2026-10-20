# weapon-sight-calibration — финальная калибровка прицелов оружия

Обновлено: 2026-10-20. Ветка `weapon-sight-calibration` от `origin/dev` `43332888b`, основной checkout.
Подробности: [Details.md](Details.md), разд. 0 — действующий подход и стволы по вариантам.

## Цель

Свести прицелы всех стволов на zero 15 м стендом **Sight Auto-Zero** (weapon-system, 2026-10-12, `a3b06c637`)
и записать результат в игровые префабы. Решение пользователя 2026-10-12: «финальная калибровка — отдельной задачей».

## Подход (уточнение пользователя 2026-10-20)

- Действующий стенд — Sight Auto-Zero, теперь вкладка «Калибровка прицела» окна `Tools/VR Battlegrounds/Weapons/Настройка оружия`; три варианта прицела:
  **SightKit_MX68**, **SightKit_TR15**, **Родной прицел**.
- Родной прицел: целик и мушка ствола отделены от меша корпуса в отдельные подвижные объекты с pivot на точке
  прицеливания. Острова определены в Blender (`blender/iron-split*.blend` → `tools/sight-split/islands*.json` →
  `SightSplitTable.cs`, 11 стволов), режет `SightPartSplitter` — **пока только в памяти на копии стенда**.
- Ствол без своего прицела получает готовый комплект TR15 или MX68, вырезанный в Blender (`blender/iron-kits.blend`).
- Ручной стенд `WeaponSightManualCalibration` и подход первого агента (маркеры, копии Sight Review, доноры)
  **устарели**. В силе остаются модель zero, инварианты ShotSource и коллиматор TR15.

## Решения 2026-10-20

- Единый стенд «Настройка оружия» из модулей (прицел, аксессуары, баланс/паспорт `WeaponInfo`, ссылка на стенд
  раскладки слота); каждый модуль пишет в свой источник данных, префаб собирает один писатель. Первый модуль —
  калибровка (эта задача), остальные — следующими задачами.
- Без копий: правка оружия либо применяется к игровому префабу, либо отклоняется; копия — только до коммита для
  сравнения side by side.
- Старый ручной стенд и копии `*_SightReview` удаляются в этой задаче; витрина аксессуаров сохраняется.

## Этап `cleanup-old-stand` (2026-10-20, ждёт проверки в Unity)

Удалено: сцены `Scenes/Tools/WeaponSightManualCalibration.unity` и `WeaponSightReview.unity`,
`Editor/VR_Battlegrounds/Weapons/Calibration/*`, `SightAlignmentMarker`, `ManualSightCalibrationSession`,
`Debug/SightCalibrationBench`, префабы `Prefabs/Weapons/SightReview/*` и `SightCalibrationDrafts/*`, WeaponInfo
`Data/Weapons/SightCalibration/Review/*`, материалы стенда `Art/Weapons/Sights/Review/*` (кроме `MonitorPicture`/
`MonitorShell` — их использует стрельбище Lobby), `SniperRifle_SRS_Iron.mat`, меши `SM_MKR9_IronSights_{Front,Rear}`,
`Tools/Checkpoints/WeaponSightCalibration/`. Строки копий убраны из `WeaponSystemAuthoring.Weapons`, `ClipOverrides`
и ключей окон выброса; ссылки — из `WeaponRegistry`, `ArsenalCompositionCatalog`, списка spawnable `--- MANAGERS ---`
и списка оружия `BotCombatStand.unity`. Витрина: `Editor/VR_Battlegrounds/Weapons/Accessories/SightAccessoryShowcase.cs`,
вызывается вкладкой «Аксессуары» окна «Настройка оружия» (строит в активной сцене). Механические прицелы паков
(IronSights TR15, MX68, MKR9) из витрины убраны — в ряду механики только готовые `SightKit_*`.

Устаревшие ожидания тестов (обновить после приёмки): `WeaponEjectionTests` (TestCase `MKR9/SniperRifle/SRM12_SightReview`),
`WeaponSystemWavePrefabTests` (`SRM12_SightReview`), `WeaponShotSoundTests` (семейства `*_SightReview`),
`WeaponScaleTests` (ветка `PublishedSightFamilies`/`DraftRoot`, теперь мёртвая), `ManipulationFeedbackCoverageTests`
(фильтр `NotInGame`).

Не правились (чужие индексы и задачи, упоминания устарели): `Docs/README.md`, `Docs/CHANGELOG.md`,
`tasks/arsenal-generator/plan.json`, `tasks/weapon-system/*`, `experts/weapon-system/backlog.md` п.5 и п.15,
`Docs/gameplay.md` (стена лобби).

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
2. Writer прицелов: `WeaponSightAuthoring` или шаг внутри `WeaponSystemAuthoring` (инвариант «один writer»);
   с решением «единый стенд, один писатель префаба» — шаг writer'а, собирающего префаб из записей модулей.
3. Комплект для SRM12 и SniperRifle (TR15 или MX68); стволы без решения: SDKGun, SDK ShotGun, Machinegun.

## Зависимости

- `codex/r08-magazine-only` (`7e3605570`, weapon-system, не влита) меняет `R08.prefab`, `Revolver.prefab`,
  `WeaponSystemAuthoring.cs` — до правок этих файлов дождаться вливания или согласовать.
- avatar-grip пишет хваты в те же префабы оружия; level-design — `Lobby.unity` (стрельбище).

## Следующий шаг

Принять уборку старого стенда; получить решения 1–3, затем: оболочка окна «Настройка оружия» с модулем прицела → постоянное разделение в сборщиках → включить Save/Apply → по стволам свести,
записать в таблицу, применить (порядок — быстрый результат) → проверка в шлеме на стрельбище Lobby.
Тесты новой логики — после приёмки пользователем.

## Этап `weapon-setup-window` (2026-10-20, ждёт проверки в Unity)

Окно `Tools/VR Battlegrounds/Weapons/Настройка оружия` (`Assets/Editor/VR_Battlegrounds/Weapons/WeaponSetupWindow.cs`):
общий выбор оружия из `WeaponRegistry`, кнопка «В Редакторе арсенала»; вкладки:
- «Калибровка прицела» — прежнее окно Sight Auto-Zero, вынесенное в панель `Weapons/Sights/SightAutoZeroPanel.cs`
  (меню Sight Auto-Zero убрано, логика не менялась);
- «Аксессуары» — постройка витрины в активной сцене; запись аксессуаров в префаб — следующий модуль;
- «Раскладка в слоте» — свои раскладки `WeaponInfo` и их использование, открытие стенда `ArsenalLayoutAuthoring`
  с выбором ручки этого оружия (правка и Bake — в инспекторе стенда);
- «Урон и разброс» — поля паспорта и баланса `WeaponInfo` + «Применить баланс к префабу» (`WeaponBalanceApplier`).
Обратная ссылка: кнопка «Настройка оружия…» на вкладке «Каталог» Редактора арсенала (`ArsenalEditorWindow`, зона gameplay,
+2 строки). Новых писателей префабов нет.

## Замечания пользователя 2026-10-20 (раунд 1, в работе у субагентов)

Прицелы (субагент A):
1. Если у ствола есть родной прицел (`SightSplitTable`) — выбор комплектов на стенде недоступен.
2. SRM12 (SRS): добавить режим «Родной прицел» (разбор есть в `islands-2.json`, в таблицу не внесён).
3. AK105: свой прицел сделать калибруемым — корректно отделить в Blender.
4. Herrington (Remington 11-87): подвижная деталь определена неверно и слиплась с внутренностями ствола.
5. Mk14: подвижная часть определена неверно — при сведении диоптр взлетает и тянет кусок металла; либо двигать
   мушку, либо правильно выделить целик.
6. Scar: родные прицелы сложены — по решению 2026-10-12 поднять рамки и сделать рабочими; если невозможно —
   вернуть вопрос (удалить и заменить комплектами решает пользователь).
7. Коллиматор XPS2 из TR15 → комплект `SightKit_XPS2` для стволов без своего прицела; из TR15 убрать, ставить
   через стенд. Решение: включить «Сохранить в таблицу» и «Применить к префабу» для комплектов
   (`WeaponSightAuthoring`); родной прицел с записью — следующим шагом.

Раскладка (субагент B):
8. «Раскладка в слоте» открывает не весь арсенал, а выбранное оружие и его слот.

Итог раунда 1 (2026-10-20, не закоммичено, ждёт проверки пользователем): п.1–7 — `changelog/2026-10-20-round1-sights.md`
(AK105 сводится мушкой по решению пользователя; Mk14 — мушкой вниз −6,41 мм вместо диоптра; Herrington — только лопатка;
SRM12 в таблице; Scar поднимает рамки на стенде; `SightKit_XPS2`, запись комплектов включена, TR15 переустановлен через
стенд). П.8 — `changelog/2026-10-20-round1-slot-layout.md` (стенд раскладки только для одного ствола, сцена пустая).
Устаревшие ожидания тестов: `SightPartSplitterTests.Table_CoversAgreedWeapons` (нет SRM12),
`KinemationWeaponTests` TR15 (узел `SM_Attach_AR15_XPS2`). Посторонний красный: треугольники R08 (24605 > 22000).

## Решения 2026-10-20 (миграция арсенала)

- АК105 сводится мушкой, как у реального; планка целика лежит на «П» и не двигается.
- Весь арсенал переводится на новую схему: сборка ствола = собрать из пака → подготовить прицел (родной — постоянное
  разделение в mesh-ассеты по `SightSplitTable`; комплект — nested `SightKit_*`) → применить запись `WeaponSightTable`
  → баланс. Единственный писатель узлов прицела — `WeaponSightAuthoring`; вызывают его сборщики и кнопка стенда.
- Стволы без своего прицела: AR15 (M16) и SniperRifle — `SightKit_TR15`, MKR9 — `SightKit_MX68`.
- SDKGun, SDK ShotGun, Machinegun — убрать из арсенала (реестр, ассортименты, каталог; зона gameplay).
- Раунд 1 закоммичен предварительно; приёмка — вместе с миграцией.
