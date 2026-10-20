# Калибровка прицелов оружия

Документ базы знаний, 2026-10-09. Сводка по снимку `a21fdf7af` (2026-10-05, «сохранить WIP калибровки прицелов»),
сверенная с кодом рабочего дерева на 2026-10-09. Подробности — в исходных документах, здесь только опора для задачи.

Источники (читать `git --no-pager show <SHA>:<путь>`):

| Метка | Документ на `a21fdf7af` |
|---|---|
| [H] | `Docs/tasks/weapon-sight-calibration-handoff.md` — главный: состояние, границы checkpoint, порядок продолжения |
| [D] | `Docs/tasks/weapon-sight-calibration-design.md` — модель, формулы, конвейер, будущие тесты |
| [Rv] | `Docs/tasks/weapon-sight-calibration-review.md` — визуальный пакет, доноры, черновые R/F, прототип TR15 |
| [M] | `Docs/tasks/weapon-sight-manual-calibration.md` — ручной стенд в Edit Mode |
| [P] / [C] / [PM] | `…-calibration-plan.md` / `…-calibration-commit-plan.md` / `weapon-sight-playmode-review.md` |

`a21fdf7af` не входит ни в одну ветку; то же содержимое в истории `dev` — коммит `3a46cd931`. Более поздний
`d6c0fded0` (2026-10-06) дополнил [H]/[M]: разделение мушки/целика MKR9, назначение маркеров, витрина аксессуаров.
Текущие версии лежат в `Docs/tasks/weapon-sight-*.md`.

Обновлено 2026-10-20. **Действующий подход — стенд Sight Auto-Zero**, [раздел 9](#9-конвейер-sight-auto-zero-2026-10-12)
(решение пользователя 2026-10-20). Разделы 1–3 (назначение, модель zero, инварианты ShotSource) и коллиматор TR15
остаются в силе. Ручной стенд `WeaponSightManualCalibration`, маркеры R/F, копии Sight Review и доноры первого
этапа (разделы 4, 5 кроме «В игре», 6, 8) **устарели и удалены из проекта 2026-10-20** (задача
`weapon-sight-calibration`: сцены `Scenes/Tools/WeaponSight*`, `Editor/…/Weapons/Calibration/*`, маркеры и сессия стенда,
`SightCalibrationBench`, копии `*_SightReview` с записями в реестре, каталоге, MANAGERS и BotCombatStand); описание оставлено
как история. Точка входа — окно `Tools/VR Battlegrounds/Weapons/Настройка оружия` (вкладки «Калибровка прицела», «Аксессуары» с витриной, «Раскладка в слоте», «Урон и разброс»);
где лежат Blender-исходники, генераторы, отчёты и доказательства — [раздел 10](#10-материалы-и-где-их-искать).
Финальная калибровка стволов ведётся задачей [`tasks/weapon-sight-calibration/`](../../tasks/weapon-sight-calibration/Readme.md).

## 1. Назначение

Сделать так, чтобы на выбранной дистанции сведения точка, на которую указывает прицел, математически совпадала с
точкой попадания первого обычного выстрела. Это равенство на одной дистанции, а не «круг допуска» [D].
Ближняя стрельба интуитивная, точное прицеливание нужно дальше; арена до 20 м [H][D]. Калибровка двигает только
детали прицела. Ствол, пулю и хват она не трогает.

## 2. Модель

**Система отсчёта [D].** M — начало `ShotSource` (`UxrProjectileSource.ShotTypes[i].ShotSource`), u — его forward
перед выстрелом. Плоскость цели Π(d) проходит через M + d·u перпендикулярно u. Все дистанции считаются от ShotSource
вдоль u, не от глаза или стекла.

**Механический прицел [D].** Линия прицела идёт из R (центр прорези/апертуры целика) через F (вершина мушки) до Π(d).
Использовать центры bounds вместо R/F нельзя. Если линия сведена с осью выстрела на дистанции R, поперечная ошибка:

    ‖e(d)‖ = h · |1 − d / R|

- **h** — поперечное расхождение линии прицела и оси выстрела в плоскости дула (d = 0). Это двумерный вектор
  (по горизонтали и вертикали), а не только высота прицела над стволом. У дула ошибка стремится к h при любом zero.
- **d** — дистанция оценки от ShotSource вдоль u, `0 < d ≤ 20` м.
- **R** — дистанция сведения (zero). В [D] она обозначена **Z**, а буквой R там названа точка целика; это одна и та же
  дистанция, не путать с точкой.

Ошибка равна 0 на d = R, после R растёт с обратным знаком. Supremum на [a, b] = h·max(|1−a/R|, |1−b/R|): проверять оба
края, R может лежать внутри интервала. Дискретные мишени весь интервал не доказывают. h измеряется заново для каждого
кандидата [D]. Пример из [Rv] (R = 15 м): Viper h = 17,43 мм → 5,81 мм на 20 м; SRM12 64,46 → 21,49 мм.
Прежний общий порог «≤ 2 см от дула» **отменён**; 2 см — только диагностическая граница [D][P].

В коде `SightGeometrySolver.Evaluate` (Editor, double) считает общий вид e(d) = intercept + slope·d. После сведения
это та же формула с h = intercept.

**Пристрелка (zero) [D][M][H].** По умолчанию **15 м**: `WeaponSightCalibrationSettings._defaultZeroDistance`, ассет
`Assets/Data/Weapons/SightCalibration/WeaponSightCalibrationSettings.asset`. Для ствола или прицела —
`WeaponSightCalibrationProfile` (`_overrideZeroDistance` + `_customZeroDistance`). Допустимо только **(0, 20]** м:
`IsValidDistance` отвергает NaN, ∞, ≤ 0 и > 20 без скрытого clamp. Смена default или override — это запрос
**повторной** калибровки, а не автоматическая правка префаба или наведение в runtime.

**Оптика [D][Rv].** Выбрана модель «виртуальное изображение на конечной дистанции». Точка сетки T = M + R·u закреплена
в системе оружия, каждый глаз смотрит лучом (T − E). **Фокус оптики с конечной дистанцией равен выбранному zero**:
`WeaponOpticView` перед рендером ставит T = ShotSource.position + forward·distance. Сетку включает, только если
`_calibratedZeroDistance` совпадает с разрешённой дистанцией. Сетка на поверхности стекла (старый TR15) и коллимация на
бесконечность отвергнуты: они не дают одной точки сведения для обоих глаз. Глаз выбирает шейдер
`FiniteFocusReticle.shader` (`unity_StereoEyeIndex`), Camera.main не используется.
**Кратность (zoom) — отдельный параметр**, не связанный с zero и длиной арены. Увеличивающая оптика — отдельный
SightType с eye relief, exit pupil, zoom и фокусом. Вид ассета кратность не подтверждает [D].

**Дробовик [D].** Zero относится к **оси залпа**, а не к первой дробине и не к среднему случайного набора дробин.

**Где хранятся данные:**

| Что | Где |
|---|---|
| Default zero | `WeaponSightCalibrationSettings` (`Assets/Scripts/Weapons/Sights/`), ассет в `Assets/Data/Weapons/SightCalibration/` |
| Адресный zero | `WeaponSightCalibrationProfile`: WeaponInfo + SightId + trigger/shotType index. Ручной стенд пишет в `…/SightCalibration/Manual/` |
| Опоры R/F | `SightAlignmentMarker` (роль Rear/Front) на Empty внутри детали целика/мушки, в префабе проверочной копии |
| Сессия стенда | `ManualSightCalibrationSession` (сцена `Assets/Scenes/Tools/WeaponSightManualCalibration.unity`, вне Build Settings) |
| Оптика | `WeaponOpticView` на линзе (`TR15_SightReview` и с 2026-10-12 основной `TR15` через `Tr15OpticBuilder`, `_sightId: XPS2`, 15 м) + `Assets/Art/Weapons/Sights/FiniteFocusReticle.shader` |
| Проверочные копии | `Assets/Prefabs/Weapons/SightReview/*_SightReview.prefab` (5 шт.), черновики — `…/SightCalibrationDrafts/` |

## 3. Инварианты

1. **TRS всех ShotSource защищены.** Калибровка не двигает, не поворачивает и не масштабирует ShotSource. Это касается
   и их родителей, и ссылок `ShotTypes` [M][H]. Стенд при загрузке запоминает по всем `UxrProjectileSource` и всем
   ShotTypes: ссылку, число записей, локальные позицию, поворот и масштаб, родителя каждого узла цепочки
   ShotSource → корень. Запоминает и TRS корня. Любое отличие останавливает Save. Ось ствола —
   ещё и система координат окон выброса `WeaponSystemAuthoring.TryGetBarrelFrame`.
2. Направление снаряда не подправляется ради сведения. Нельзя использовать `ShotOrientationModifier` как скрытую
   коррекцию, выбирать цель Raycast, переносить начало снаряда к глазу или поднимать виртуальный ствол [D][H].
3. Двигаются только детали прицела и только в физически обоснованных пределах; по умолчанию все DOF заблокированы.
   Нельзя: деформировать или масштабировать корпус ради результата, переносить хват, утапливать прицел или подвешивать
   его над креплением, удалять органы управления. Целик внутри цельного Base нельзя двигать вместе с Base [D][Rv].
4. Масштаб корня оружия не меняется: служебное размещение — отдельный `WeaponPlacement_EditorOnly` со scale 1 [M].
5. Исходные игровые префабы и основные записи WeaponRegistry не заменяются до приёмки пользователем. Результат
   сохраняется только в проверочную копию [M][PM].
6. **Один writer префабов оружия.** Сейчас это `WeaponSystemAuthoring` ([invariants.md](invariants.md) п. 10). Он
   ведёт и пять копий `*_SightReview` (волны F1–F3). Старые `ManualSightCalibrationAuthoring.Save` и
   `SightGameplayReviewBuilder` — WIP, но тоже пишут `SightReview/*`. **Перед любым Apply/Save через ручной стенд
   согласовать writer**, чтобы у префаба не было двух писателей (раздел 7).
7. Постоянные тесты новой механики не пишутся до пользовательской приёмки [H][P].

## 4. Инструменты и порядок калибровки

| Инструмент | Назначение |
|---|---|
| `Tools/VR Battlegrounds/Weapons/Manual Sight Calibration` (`ManualSightCalibrationWindow`/`Authoring`/`Geometry`) | Ручное сведение без Play |
| `…/Sight Calibration/Audit` (`WeaponSightAudit`) | Только чтение: inventory реестра, проверка настроек; `calibrationAccepted = false` |
| `…/Sight Calibration/Capture Visual Inventory` (`SightVisualReview`) | Изолированные снимки оружия и доноров |
| `SightGeometrySolver` | Чистая double-геометрия, ассеты не пишет |
| `SightPrototypeReview`, `SightCalibrationBenchBuilder`, `SightGameplayReviewBuilder`, `LobbyRangeLayoutBuilder` | Диагностика и стенды пакета 2026-10-04/05 |
| `SightAccessoryShowcase` (`d6c0fded0`) | Витрина прицелов и аксессуаров в сцене стенда, только для выбора деталей |

Порядок на ручном стенде [M] + `d6c0fded0`:

1. Сохранить открытые сцены и открыть стенд. Выбрать оружие (если есть `_SightReview`, грузится копия) и дистанцию.
2. Нажать «Выровнять ShotSource на центр»: поворачивается Placement, а не ShotSource.
3. Выделить деталь целика → «Выделенное → целик», деталь мушки → «Выделенное → мушка». Маркер Empty
   `SightReference_Rear/Front` поставить в **фактическую** точку наведения: Rear — откуда смотрит глаз, Front — вершина
   мушки. Направление задают позиции маркеров, а не их поворот.
4. Двигать родительские детали прицела штатными инструментами, следя за отклонением X/Y в мм (раздел 5).
5. «Вид через прицел» — осмотр. «Сохранить настройку прицела» — только после согласования writer (инвариант 6).

Полный конвейер [D] (не реализован): Audit → References → Feasibility → Candidate → VisualReview → Approved → Apply →
Verify. Apply принимает только адресно утверждённого кандидата с совпадающими отпечатками входов. Изменение zero
инвалидирует принятие.

## 5. Проверки

**Две проверочные линии (ray) [M].** Обе рисует окно стенда. Зелёная — из настоящего ShotSource по forward до
плоскости мишени (ось пули). Голубая — Rear → Front, продлённая до той же плоскости. Отклонение пересечения от центра
показывается в мм по осям ShotSource right/up. Save отказывает, если линию построить нельзя: мушка не впереди целика,
ShotSource не смотрит в центр мишени, scale Placement ≠ 1 или маркер не «голый» Empty.

**Камера проверки стоит позади REAR [M].** «Вид через прицел» ставит перспективную Scene View на 18 см позади
маркера Rear. Камера смотрит вдоль Rear → Front, up = ShotSource.up. Play Mode и игровая камера не затрагиваются.
Это вид глаза за целиком, а не центральная камера оптики: стерео проверяется отдельно.

**Donor material [Rv].** Материал донора — отдельный game-owned вариант: `SniperRifle_SRS_Iron.mat` на основе
`M_TR15_Ironsights`. Исходные материал и меши донора не меняются, атлас корпуса на чужие UV не назначается. Проверка
сохранённого ассета: shader, параметры и карты совпадают с ожидаемыми, hashes источников не изменились. Внешний вид
принимается в Lobby отдельно от сведения [PM].

**Donor scale [Rv][D][H].** Размеры донорных деталей не меняются ради сведения. SRS-модули — исходные размеры и
взаимная поза. У MKR9 масштаб ≈100 — это масштаб импорта FBX; по контракту при применении его выпекают в
game-owned mesh, корень оружия остаётся прежним. Масштаб донорной конструкции и крепления SniperRifle ещё нужно
осмотреть. Масштаб корня берётся текущий (AR-15: 0,70, не старые 0,83).

**Защита данных при Save [M][H].** Сравниваются хэши входа, выхода и профиля с моментом загрузки, иерархия копии
сверяется с исходником. Писать можно только в `SightReview/` или `SightCalibrationDrafts/`. Перед записью делается
резервная копия в `tmp/weapon-sight-calibration/manual/backups/`. При отказе — транзакционный откат.

**В игре [PM].** Lobby: щиты на 10/15/20/25/30 м от края, мониторы показывают последнее попадание (SDK contact).
Оригинал и Sight Review сравниваются в руках по шагам: доступность, осмотр, посадка, ощущение, первый выстрел на 15 м,
дистанции, оптика AR-15 каждым глазом с движением головы, серия, повтор, мониторы. Попадание само по себе калибровку
не принимает.

## 6. История приёмки на 2026-10-05 (снимок `a21fdf7af`)

**Это история, не текущая приёмка.** Ни одно оружие не объявлено откалиброванным. Результаты получены
2026-10-04/05 на тогдашних файлах. Код стенда менялся позже (`d6c0fded0`), поэтому прежние PASS его не покрывают.

| Тогдашний статус | Что |
|---|---|
| PASS (история) | Audit: 20/20 записей, 22 shotTypes, 253 записи геометрии, 18 native-проб настроек. 12 проб solver. Editor и AndroidCompileGate второго среза [D][P] |
| PASS (история) | TR15 finite focus 15 м: 5/5 смещений монокамеры против 0/5 у старой сетки. Растровое окно 5×5 px, не численный zero [Rv] |
| PASS (история) | Материал SRS, Lobby layout, SDK contact → фото, смещение +12/−8 мм, Android Lobby [Rv][PM] |
| PASS (история) | Ручной стенд native: 25 записей, совпадение двух лучей, движение родителя меняет прицельный луч, ShotSource неизменен, запрет его roll/scale, камера за целиком, щит без Collider [M][H] |
| PENDING (история) | Save roundtrip; rollback новой и существующей копии/профиля; защита чужих output/profile; уникальность и provenance SDK identity; Undo/Redo переключения и дистанции; финальный Android после последних правок Authoring [H][C] |
| PENDING (история) | `MapGameplayHierarchy.ValidateAll`, `PhysicalArenaLayoutMigration.ValidateAll`, Bake Occlusion для Lobby; Quest stereo; actual SDK shot/replay; два клиента; визуальная приёмка в шлеме [H][PM] |
| Расчёт, не применён | Viper: мушку +3,0399 мм; SRM12: −5,3231 мм (`DraftNeedsVisualAcceptance`) [Rv][H] |

Локальные доказательства лежат вне Git, в основном checkout: `F:/UnityProjects/Vr_Battlegrounds_ai/Docs/tasks/report/
weapon-sight-calibration/` и `…/tmp/weapon-sight-calibration/`. В этом worktree их нет.

## 7. Открытые вопросы

1. **Writer прицелов.** Как вписать сведение в единственный writer `WeaponSystemAuthoring`: таблица прицелов в нём или
   стенд как его шаг? Пока не решено, Save/Apply ручного стенда в `SightReview/*` не выполнять.
2. Замена основных записей WeaponRegistry принятыми копиями, удаление дублей `*_SightReview` и
   `SightCalibrationDrafts/MKR9_NativeProbe_b1bbb91d.prefab` (refactor-plan, этап H, В10).
3. Viper: мушка — часть Base. Нужна правка меша с сохранением корпуса или механизм целика. SRM12: доказать ход
   регулировки и контакт крепления.
4. SniperRifle: переходные площадки 34×3×35 мм — эскиз (фаски, зажим, пересечения); масштаб донорной сборки.
5. MKR9: выпечь масштаб ≈100 в game-owned mesh или оставить на контейнере (раздел 8).
6. Полный fingerprint/Approved/Apply не реализован: строить конвейер [D] или ограничиться ручным стендом + writer?
7. Zoom: поля нет. Нужна ли увеличивающая оптика хоть одному стволу (для SniperRifle — нет).
8. Stereo Quest и eyebox для `FiniteFocusReticle`; звук щитов не выбран; ожидания `LobbyLayoutTests` устарели.
9. Единое обозначение дистанции сведения в будущем `Docs/weapons/` (R или Z).

## 8. Расхождения источника с кодом (2026-10-09)

- В [D] zero обозначен Z, а R — точка целика. Формула выше пишет R для zero (п. 2).
- У zoom нет ни одного поля в коде: `WeaponOpticView` знает только дистанцию. Кратность пока только контракт [D].
- `MKR9_SightReview.prefab`: у контейнера `SM_MKR9_IronSights` остался localScale ≈ (100, 100, 100). Под ним дети
  `_Rear/_Front` со scale 1. Выпечки масштаба, описанной в [Rv], нет.
- [H]/[M] писались до `WeaponSystemAuthoring`. Сейчас `SightReview/*` переводит writer, а `ManualSightCalibrationAuthoring.Save`
  (копирует локальные TRS всех узлов и маркеры) и `SightGameplayReviewBuilder` (также пишет MANAGERS-префаб) — ещё два
  писателя тех же файлов. Хэш-проверка Save ловит только изменения между загрузкой и записью.
- Ни одного ассета `WeaponSightCalibrationProfile` нет; папки `…/SightCalibration/Manual/` нет. `Review/*.asset` — это
  `WeaponInfo` копий, а не профили. Save жёстко пишет `_sightId = "ManualIron"` и всегда включает override.
- `MKR9_NativeProbe_b1bbb91d.prefab` (неуспешная проба Save, не игровое оружие) всё ещё в репозитории.

## 9. Конвейер Sight Auto-Zero (2026-10-12)

Этапы weapon-system `sight-tr15-optic` (`5fc162060`), `sight-iron-kits` (`a3b06c637`), `sight-arsenal-dedupe`
(`b624fdb3c`); решения — [decisions.md](decisions.md) (2026-10-11/12), журнал —
`experts/weapon-system/journal/2026-10-12-sight-tools.md`. Пользователь 2026-10-12: «пока только инструменты,
финальная калибровка — отдельной задачей».

**Механика (решения 2026-10-12).** Регулировочные детали (столбик мушки, блок целика) — отдельные статические
объекты, не skinned; pivot ровно на точке прицеливания (F — вершина мушки, R — центр отверстия/прорези). Сведение —
одной деталью и только по высоте: решатель двигает деталь, чтобы линия R→F прошла через ShotSource + Z (Z = 15 м);
боковая ошибка показывается, но не исправляется. Рамки только в поднятом положении. Ход: TR15 −8/+3 мм, Mk14 +8 мм,
Viper −4 мм, остальные ±3 мм.

| Часть | Где | Что |
|---|---|---|
| Комплекты | `Assets/Art/Weapons/Sights/IronKits/*.fbx`, `Assets/Prefabs/Weapons/Sights/SightKit_{TR15,MX68}.prefab`; сборщик — меню `…/Weapons/Sights/Build Iron Sight Kits` (`SightKitBuilder`) | Нарезаны в Blender из пака. TR15: R→F 300 мм, F выше R на 4,44 мм; MX68: R→F 365,9 мм. Комплект — один объект на планке, масштаб через `SightKitScale` |
| Подвижная деталь | `Assets/Scripts/Weapons/Sights/SightAdjustablePart.cs` | Ход только по локальной Y (travelDown/Up) |
| Решатель | `Assets/Editor/VR_Battlegrounds/Weapons/Sights/Core/SightAutoZeroSolver.cs` | Аналитическое «Свести» |
| Окно | вкладка «Калибровка прицела» окна `Tools/VR Battlegrounds/Weapons/Настройка оружия` (`WeaponSetupWindow` + `SightAutoZeroPanel`; до 2026-10-20 — отдельное окно Sight Auto-Zero) | Режимы «комплект на планке» и «Родной прицел»; стенд — несохраняемая сцена. `SaveUnlocked`/`ApplyUnlocked` = false до задачи калибровки; у «Родного прицела» Apply не написан |
| Таблица | `Assets/Data/WeaponSystem/Sights/WeaponSightTable.asset` | Пуста (`_entries: []`) |
| Writer комплекта | `Assets/Editor/VR_Battlegrounds/Weapons/Sights/WeaponSightAuthoring.cs` | Вставка nested `SightKit_*` под `MeshContainer`, компенсация масштаба корня, канонический `_assetId`. Третий писатель префабов — см. инвариант 6 |
| Разделитель | `Assets/Editor/VR_Battlegrounds/Gameplay/SightSplit/` (`SightPartSplitter`, `SightSplitTable` …) | Выносит подвижный остров вшитого прицела на копии ствола (меши в памяти), 11 стволов. Таблица **генерируется** (раздел 10), привязана к GUID/localFileId и числу вершин меша. Встроить в `KinemationWeaponBuilder`/`HandsPackWeaponBuilder` — не сделано |
| Оптика TR15 | `Assets/Editor/VR_Battlegrounds/Gameplay/Tr15OpticBuilder.cs` | Коллиматор XPS2 в основном TR15, принят в шлеме |
| Тесты | `Assets/Tests/EditMode/Sights/*`, `Assets/Tests/EditMode/SightSplit/*` | 104/104 на 2026-10-12 |

**Три варианта прицела в окне:** `SightKit_MX68`, `SightKit_TR15`, «Родной прицел».

- **Родной прицел** — целик и мушка вшиты в меш корпуса. В Blender (`iron-split*.blend`, через Blender MCP) меш
  разбит на острова и размечено, какие острова — целик, мушка, подвижная деталь, где R/F и ход; результат —
  `islands*.json` → генератор → `SightSplitTable.cs` (11 стволов). `SightPartSplitter` выносит остров детали в
  отдельный объект `SightRearFrame/SightRear` или `SightFrontFrame/SightFront` с pivot на точке, остаток — меш
  корпуса. Сейчас это делается **только на временной копии стенда, меши в памяти**; постоянное разделение в
  сборщиках и сохранение мешей — работа задачи финальной калибровки.
- **Комплект** — для ствола без своего прицела или с непригодным: механические прицелы TR15 и MX68, вырезанные из
  FBX пака в Blender (`iron-kits.blend`) с отдельными подвижными деталями. Ставится одним объектом на планку.

| Ствол | Вариант | Подвижная деталь | Сдвиг на 15 м (не применён) |
|---|---|---|---|
| AK105 / MP5K / Scar | Родной | столбик мушки (у Scar — после подъёма рамок) | +0,91 / −0,90 / −2,03 мм |
| R08 / Revolver | Родной | целик | +0,57 / +0,39 мм |
| Mk14 | Родной | диоптр целика, ход до +8 | +6,67 мм |
| Viper | Родной | целик на затворе, ход до −4 | −3,07 мм |
| Uzi | Родной | мушка целым островом с ушками | −0,90 мм |
| Herrington (Remington 11-87) | Родной | передняя лопатка | +1,65 мм |
| Gun (BrowningHiPower) | Родной | мушка; обе половины на затворе | −0,19 мм |
| PPK | Родной, без подвижной детали | — | только показ ошибки, +14,7 мм |
| M16 (`AR15.prefab`) | Комплект TR15 (мушки нет) | столбик мушки комплекта | −5,6 мм |
| MKR9 / SRM12 / SniperRifle | Комплект (сейчас донорные детали в копиях Sight Review) | столбик мушки | −3,71 / −5,16 / −7,14 мм (по разбору Blender) |
| TR15 | Коллиматор XPS2, принят | — | — |
| ShotgunReal (FABARM) | Не калибруется | — | — |
| SDKGun, SDK ShotGun, Machinegun | Решения нет | — | — |

**Ловушки:** `SaveAsPrefabAsset` обнуляет `NetworkIdentity._assetId` (тест `NetworkAssetIdOnDiskTests`); пересборка
сборщиком пака ломает `SightSplitTable` и баланс (`WeaponBalanceApplier.Apply`); целик Viper и обе половины Browning
сидят на затворе и едут с ним; копии `*_SightReview` сняты со стены лобби, но ещё в `WeaponRegistry.asset` и
`ArsenalCompositionCatalog.asset`.

## 10. Материалы и где их искать

| Что | Где |
|---|---|
| Рабочая задача финальной калибровки: состояние, решения, хронология, зависимости | `tasks/weapon-sight-calibration/` — `Readme.md`, `Details.md` |
| Blender-исходники: нарезка комплектов TR15/MX68, разборы островов 16 стволов | `tasks/weapon-sight-calibration/blender/` (`iron-kits.blend`, `iron-split.blend`, `iron-split-2.blend`, скриншоты pivot) |
| Генератор `SightSplitTable.cs` и входы | `tasks/weapon-sight-calibration/tools/sight-split/` (`gen_table.py`, `islands*.json`, шаблон); описание — `tools/Readme.md` |
| Сухой прогон разделителя по стволам (острова, точки, ход) | `tasks/weapon-sight-calibration/changelog/2026-10-12-sight-split-dry-run-485.txt` |
| Итоги аренд Unity: размеры комплектов, проверки Apply, решатель, тесты разделителя | `tasks/weapon-sight-calibration/changelog/2026-10-12-sight-*-leases-*.md` |
| Скрипты и MCP-сниппеты исследования 2026-10-04/05 (аудит, геометрия, доноры SniperRifle) | `tasks/weapon-sight-calibration/tools/research-2026-10-04/` |
| План этапов weapon-system по прицелам | `tasks/weapon-system/plan.json` (`sight-tr15-optic`, `sight-iron-kits`, `sight-arsenal-dedupe`) |
| Документы первого этапа | `Docs/tasks/weapon-sight-*.md` |
| Дельта стрельбища Lobby | `Tools/Checkpoints/WeaponSightCalibration/lobby-range.patch` |
| Вне Git, только на этой машине: скриншоты разборов, пакет доказательств 2026-10-05, визуальные обзоры, кандидаты звука щитов | `F:/UnityProjects/Vr_Battlegrounds_ai/tasks/weapon-sight-calibration/reports/`, `…/Docs/tasks/report/weapon-sight-calibration/`, `…/tmp/weapon-sight-calibration/`; оригиналы отчётов этапов — `F:/CodexWorktrees/weapon-system/tasks/weapon-system/reports/` |
