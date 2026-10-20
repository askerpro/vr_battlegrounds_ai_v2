# Калибровка прицелов оружия — подробности (сбор контекста)

Собрано 2026-10-20 по Git (все refs), worktree, локальным отчётам и хабу координации (только чтение).
База новой ветки `weapon-sight-calibration` = `origin/dev` `43332888b`. Ссылки: `<ref>@<SHA>:<путь>` или путь
на диске. «dev» ниже = `origin/dev`.

**Главный вывод.** Весь закоммиченный код, сцены и ассеты по прицелам уже в dev: три этапа weapon-system
(`sight-tr15-optic`, `sight-iron-kits`, `sight-arsenal-dedupe`) влиты 2026-10-12, WIP 2026-10-05/06 тоже в dev.
Непринятого кода по прицелам ни в одной ветке, stash и worktree нет. Вне Git лежат Blender-разборы и
генератор таблицы разделителя (worktree `F:/CodexWorktrees/weapon-system`, `tasks/weapon-system/reports/`),
а также локальные пакеты доказательств 2026-10-04/05 в основном checkout.
Финальная калибровка стволов ещё не начиналась: запись таблицы и Apply выключены в коде.

## 0. Действующий подход — стенд Sight Auto-Zero (уточнение пользователя 2026-10-20)

Калибровку вели два агента в разное время. Подход первого (Codex, 2026-10-04/06: ручной стенд
`WeaponSightManualCalibration`, маркеры R/F, копии `*_SightReview`, доноры, проставки) **устарел**. Действующий —
стенд weapon-system (Claude, 2026-10-11/12, `a3b06c637`): окно `Tools/VR Battlegrounds/Weapons/Sight Auto-Zero`,
выбор прицела из трёх вариантов: **SightKit_MX68**, **SightKit_TR15**, **Родной прицел**. Из первого этапа
остаются в силе модель zero 15 м, инварианты ShotSource и коллиматор TR15 с конечным фокусом (принят в шлеме).

**Идея.** Регулировочные детали прицела (столбик мушки или блок целика) — отдельные статические объекты, pivot
ровно на точке прицеливания (F — вершина мушки, R — центр прорези/отверстия). «Свести» аналитически двигает одну
подвижную деталь по локальной Y так, чтобы линия R→F прошла через ShotSource + 15 м. Боковую ошибку показывает,
не исправляет.

**Родной прицел — у ствола есть свой целик и мушка, вшитые в меш корпуса.**
1. Blender (через Blender MCP, кода нет): меши корпусов загружены в `blender/iron-split.blend` (AK105, Mk14, Viper,
   R08) и `iron-split-2.blend` (остальные 12), меш разбит на острова связности, вручную определено, какие острова —
   целик, мушка, подвижная деталь, где точки R/F и ход. Результат — `tools/sight-split/islands*.json`
   (селекторы островов по числу вершин/треугольников и окну координат, точки, ход, подъём сложенных рамок).
2. `gen_table.py` превращает json в `SightSplitTable.cs` — спецификации 11 стволов (GUID/localFileId меша,
   узел, острова, точки, ход).
3. В Unity `SightPartSplitter` по спецификации режет меш узла: остров детали → отдельный объект
   `SightRearFrame/SightRear` или `SightFrontFrame/SightFront` с pivot на точке, остаток → меш корпуса. Это делает
   `NativeSightStand` на **временной копии** префаба; **меши только в памяти, ассеты не сохраняются**.
   Сухой прогон (`changelog/2026-10-12-sight-split-dry-run-485.txt`) уже называет будущие ассеты
   (`…/Meshes/AK105_SightFront.asset`, `…_Body.asset` и т.п.), но их нет.
   → Главная работа финальной задачи: сделать разделение постоянным (решение 2026-10-12 — в сборщиках
   `KinemationWeaponBuilder`/`HandsPackWeaponBuilder`, повторяемо), затем сводить и применять.

**Комплект — у ствола своего прицела нет или он непригоден.** `blender/iron-kits.blend`: из FBX пака вырезаны
механические прицелы TR15 (раздельные целик и мушка) и MX68, подвижные детали (столбик мушки, барабан/пластина
целика) отделены, pivot на F/R. Экспорт — `Assets/Art/Weapons/Sights/IronKits/*.fbx`, сборщик `SightKitBuilder`
→ `SightKit_TR15.prefab` (R→F 300 мм, ход −8/+3 мм) и `SightKit_MX68.prefab` (R→F 365,9 мм, ±3 мм). На стенде
комплект — один объект: пользователь ставит его на планку, раздвигает половины вдоль планки, жмёт «Свести».
Запись в префаб — `WeaponSightAuthoring` (nested `SightKit_*` под `MeshContainer`, компенсация масштаба корня).

**Стволы по вариантам** (сдвиг решателя на 15 м, аренда 494; ничего ещё не применено):

| Ствол (префаб) | Вариант | Подвижная деталь | Сдвиг |
|---|---|---|---|
| AK105 | Родной | столбик мушки | +0,91 мм |
| Mk14 | Родной | диоптр целика (ход до +8) | +6,67 мм |
| Viper | Родной | целик на затворе (ход до −4) | −3,07 мм |
| R08 | Родной | целик | +0,57 мм |
| MP5K | Родной | столбик мушки | −0,90 мм |
| Uzi | Родной | мушка целым островом с ушками | −0,90 мм |
| Herrington (Remington 11-87) | Родной | передняя лопатка | +1,65 мм |
| Revolver | Родной | целик | +0,39 мм |
| Gun (BrowningHiPower) | Родной | мушка, обе половины на затворе | −0,19 мм |
| Scar | Родной | столбик мушки после подъёма рамок | −2,03 мм |
| PPK | Родной, без подвижной детали | — (только показ ошибки) | +14,7 мм ошибка |
| M16 (`AR15.prefab`, пак Hands) | Комплект TR15 (мушки нет, целик сложен) | столбик мушки комплекта | −5,6 мм |
| MKR9 | Комплект (его прицел — MX68; копия SightReview со scale 100) | столбик мушки | −3,71 мм (разбор) |
| SRM12 | Комплект/донорные детали TR15 | столбик мушки | −5,16 мм (разбор) |
| SniperRifle | Комплект (сейчас доноры SRM12 только в SightReview) | столбик мушки | −7,14 мм (разбор) |
| TR15 | Коллиматор XPS2, принят | — | — |
| ShotgunReal (FABARM) | Не калибруется (решение) | — | — |
| SDKGun, SDK ShotGun, Machinegun | Решения нет | — | — |

Сдвиги «(разбор)» посчитаны в Blender-разборе (`islands-2.json`, поле `zeroing`), не на стенде.
Какой комплект (TR15 или MX68) ставить на SRM12/SniperRifle — решения нет.

**Что из первого этапа устарело и подлежит уборке (решение по объёму — за пользователем):**
сцена `Assets/Scenes/Tools/WeaponSightManualCalibration.unity`, `ManualSightCalibration*`, `SightAlignmentMarker`,
`ManualSightCalibrationSession`, `SightGameplayReviewBuilder` и прочий `Assets/Editor/VR_Battlegrounds/Weapons/Calibration/*`,
черновик `MKR9_NativeProbe`, копии `*_SightReview` после переноса прицелов в оригиналы. С уборкой исчезают два
лишних писателя префабов; остаётся вопрос `WeaponSightAuthoring` против единственного `WeaponSystemAuthoring`.
`WeaponSightReview.unity` (стенд Play с коллиматором) и стрельбище Lobby остаются для проверки в игре.

## 1. Хронология

| Дата | Кто | Что | SHA / ref | В dev |
|---|---|---|---|---|
| 2026-10-04/05 | Codex-сессия (коммит от имени пользователя; по стилю handoff, JS store инструментов, Plastic) | Исследование, контракт zero 15 м, Audit/solver, Sight Review-копии TR15/MKR9/SRM12/SniperRifle/Viper, finite-focus TR15, ручной стенд Edit Mode, стрельбище Lobby с фото попаданий | снимок `a21fdf7af` (вне веток); в dev — `3a46cd931` «сохранить WIP калибровки прицелов» | да (WIP-checkpoint, не приёмка) |
| 2026-10-05 | Claude | `Ar15ScaleCalibration.cs`, материал `M_TR15_Ironsights` в оружейном срезе | `6c964ec14` | да |
| 2026-10-06 | пользователь/агент | «calibration stand wip»: разделение `SM_MKR9_IronSights_Rear/_Front`, `SightAccessoryShowcase`, правки ручного стенда и сцены | `d6c0fded0` | да (WIP) |
| 2026-10-09 | weapon-system (Claude) | База знаний `Docs/weapons/` вкл. `sight-calibration.md` (сводка a21fdf7af, сверка с кодом); черновик в `F:/CodexWorktrees/weapon-system/tasks/weapon-system/reports/expert-kb/sight-calibration.md` (хаб 1061/1091) | `49e0f0a76` | да |
| 2026-10-11 | пользователь + weapon-system | Решения: калибровка — главный приоритет, эталон TR15 Sight Review, родная механика, дубли со стены; план r43 | `60a11fee2` | да |
| 2026-10-11 | Claude | Экран дорожки стрельбища показывает попадания только своей мишени (Play/шлем «ожидает проверки») | `02f8d4fda` | да |
| 2026-10-11/12 | weapon-system, этап `sight-tr15-optic` | `Tr15OpticBuilder`: коллиматор XPS2 из эталона в основной TR15, `TR15_XPS2_Reticle.mat`, `WeaponOpticView` 15 м; принят в шлеме (аренда 447), assetId канонический (аренда 450) | `5fc162060` (до rebase `9d5276eb8`); checkpoints `26ec381d8`, `900df17d9`; хаб 4156→4242 | да |
| 2026-10-12 | weapon-system | Docs: статус TR15, план r46 | `12aa19ac3` | да |
| 2026-10-12 | weapon-system + субагенты, Blender | Этап `sight-iron-kits`: комплекты TR15/MX68 (FBX нарезаны в Blender), SightAdjustablePart, решатель «Свести», таблица, writer `WeaponSightAuthoring`, окно Sight Auto-Zero, разделитель SightSplit (11 стволов). Пользователь опробовал на стенде. Тесты Sights+SightSplit 104/104, AndroidCompileGate PASS (аренды 458/463/468/471/477/485/494/496) | `a3b06c637`; checkpoints `7e9f1de09`, `cc6d81d91`, `0b9e845cc`, `bd7b81e57`, `5a6a340d6`, `d371e946b`, `86bcb811c`, `164c663f6`, `1c1876b1f`, `72d363a7d`, `634c74e6c`; хаб 4275→4909 | да |
| 2026-10-12 | weapon-system | Docs: инструменты влиты, решения по ходу/стволам, backlog, журнал | `b1d73e916` | да |
| 2026-10-12 | weapon-system + gameplay (4912) | Этап `sight-arsenal-dedupe`: стена лобби 25→20, без 5 копий Sight Review; одна строка `ArsenalFingerprint` лобби в каталоге | `b624fdb3c`; checkpoint `7fec21022`; хаб 4053…4978 | да |
| 2026-10-13 | avatar-grip (hand-rig-quality) | Записи хвата MEF/Cyborg во всех префабах оружия, включая `SightReview/*` и `SightCalibrationDrafts/*` | `69b0420e4` | да |
| 2026-10-17 | пользователь | Weapon-system передан Codex; «финальная калибровка — отдельной задачей по его команде» | `F:/CodexWorktrees/weapon-system` `codex/r08-magazine-only`@`7e3605570`:`tasks/weapon-system/Readme.md` «Ждёт пользователя» | нет (ветка не влита, но про прицелы там только эта строка) |

Технические checkpoint-ветки `codex/tmp/weapon-system-sight-*`, `…-dedupe-*`, `…-native-sight-mode-trial-*`:
их содержимое по прицелам совпадает с dev; разница только в `TR15.prefab` (поздние правки хвата в dev).
Ветки `codex/tmp/weapon-system-aimlock-*` относятся к `aimlock-held-lag` (удержание оружия, влит `2bc2e9f6`),
а не к прицелам. `codex/tmp/bots-fix-aim-*` — прицеливание ботов. `claude/weapon-system`,
`archive/expert-rebase-*/weapon-system*`, `wip-backup/20261017/weapon-system`, `agents/archive/weapon-*`,
`stash@{35}` прицелов не содержат.

## 2. Что уже есть (всё в dev, если не сказано иное)

### Runtime (`Assets/Scripts/`)
- `Weapons/Sights/WeaponSightCalibrationSettings.cs` — default zero 15 м; ассет
  `Assets/Data/Weapons/SightCalibration/WeaponSightCalibrationSettings.asset`. Допустимо (0, 20] м, без clamp.
- `Weapons/Sights/WeaponSightCalibrationProfile.cs` — адресный override (WeaponInfo+SightId+shotType).
  Ни одного ассета-профиля нет.
- `Weapons/Sights/WeaponOpticView.cs` — finite-focus сетка: T = ShotSource + forward·zero. Стоит в `TR15.prefab`,
  `SightReview/TR15_SightReview.prefab`, сцене `WeaponSightReview.unity`.
  Шейдер: `Assets/Art/Weapons/Sights/FiniteFocusReticle.shader` (`unity_StereoEyeIndex`).
- `Weapons/Sights/SightAdjustablePart.cs` — подвижная деталь, ход только по локальной Y (travelDown/Up).
  Используется только в `Assets/Prefabs/Weapons/Sights/SightKit_TR15.prefab` и `SightKit_MX68.prefab`.
- `Weapons/Sights/SightAlignmentMarker.cs`, `ManualSightCalibrationSession.cs` — маркеры Rear/Front и сессия
  ручного стенда (только сцена `WeaponSightManualCalibration.unity`).
- `Debug/SightCalibrationBench.cs` — стенд Play Mode (сцена `WeaponSightReview.unity`).
- `Maps/ShootingRangeDisplay.cs`, `Maps/ShootingRangePhotoCapture.cs`, `Maps/ShootingTarget.cs` — стрельбище
  Lobby: щиты 10/15/20/25/30 м, мониторы последнего попадания со смещением в мм.

### Editor (`Assets/Editor/VR_Battlegrounds/`)
Новый конвейер, 2026-10-12, рабочий:
- `Weapons/Sights/SightAutoZeroWindow.cs` — меню `Tools/VR Battlegrounds/Weapons/Sight Auto-Zero`. Режимы
  «комплект на планке» и «Родной прицел». Флаги `SaveUnlocked = false`, `ApplyUnlocked = false`, строки 33–34:
  их включает задача финальной калибровки. В режиме «Родной прицел» Apply нет совсем.
- `Weapons/Sights/WeaponSightAuthoring.cs` — writer узлов прицела (только вставка nested `SightKit_*` под
  `MeshContainer`): сверка TRS, компенсация масштаба корня, канонический `_assetId`.
- `Weapons/Sights/Core/`: `SightAutoZeroSolver` (аналитическое «Свести»: линия R→F через ShotSource+Z),
  `SightKitBuilder` (меню `…/Weapons/Sights/Build Iron Sight Kits`), `SightKitScale`, `SightParts`,
  `IronKitMaterials`, `NativeSightStand`, `WeaponSightTable` (ассет `Assets/Data/WeaponSystem/Sights/WeaponSightTable.asset`,
  `_entries: []`). Отдельная asmdef `VrBattlegrounds.SightTools.Editor`.
- `Gameplay/SightSplit/` (`SightPartSplitter`, `SightMeshSplit`, `MeshIslands`, `SightSplitSpec`, `SightSplitTable`) —
  выносит подвижный остров вшитого прицела на копии ствола, меши только в памяти. Таблица сгенерирована
  из Blender-разборов; asmdef `VrBattlegrounds.SightSplit.Editor`.
- `Gameplay/Tr15OpticBuilder.cs` — единственный writer оптики TR15.
- `Gameplay/Ar15ScaleCalibration.cs` — масштаб AR-15 (корень 0,70).

Старый конвейер 2026-10-04/06, WIP и диагностика (`Weapons/Calibration/`):
- `ManualSightCalibrationWindow`/`Authoring`/`Geometry` — меню `…/Weapons/Manual Sight Calibration`.
- `WeaponSightAudit`(+Report) — `…/Sight Calibration/Audit`; `SightVisualReview` — `…/Capture Visual Inventory`.
- `SightCalibrationBenchBuilder` — `…/Sight Calibration/Play Mode Review`.
- Также `SightGeometrySolver`, `SightPrototypeReview`, `SightGameplayReviewBuilder`, `LobbyRangeLayoutBuilder`,
  `SightAccessoryShowcase`.

### Сцены
- `Assets/Scenes/Tools/WeaponSightManualCalibration.unity` — ручной стенд Edit Mode, вне Build Settings.
  Возможны синтетические маркеры MKR9 из проб.
- `Assets/Scenes/Tools/WeaponSightReview.unity` — стенд Play Mode (28k строк).
- Стенд Sight Auto-Zero создаётся окном как несохраняемая сцена.
- Lobby (`Assets/Scenes/Lobby.unity`, зона level-design) — стрельбище; дельта `Tools/Checkpoints/WeaponSightCalibration/lobby-range.patch`.

### Ассеты
- Комплекты: `Assets/Art/Weapons/Sights/IronKits/{TR15_FrontSight_Kit,TR15_RearSight_Kit,MX68_IronSights_Kit}.fbx`
  + материалы и текстуры. Префабы: `Assets/Prefabs/Weapons/Sights/SightKit_{TR15,MX68}.prefab`.
  TR15: R→F 300 мм, F выше R на 4,44 мм, ход −8/+3 мм. MX68: R→F 365,9 мм.
- Коллиматор TR15: `Assets/Art/Weapons/Kinemation/TR15/Meshes/TR15_XPS2_Optic.asset`, `…/Materials/TR15_XPS2_Reticle.mat`,
  `TR15_XPS2_Lens.*`.
- MKR9: `Assets/Art/Weapons/Kinemation/MKR9/Meshes/SM_MKR9_IronSights_{Rear,Front}.asset`. SRM12:
  `Assets/Art/Weapons/Kinemation/SRM12/Meshes/SRM12_*Sight*.asset`. Донорный материал
  `Assets/Art/Weapons/Sights/Materials/SniperRifle_SRS_Iron.mat`. Материалы стенда — `Assets/Art/Weapons/Sights/Review/*`.
- Копии Sight Review: `Assets/Prefabs/Weapons/SightReview/{TR15,MKR9,SRM12,SniperRifle,Viper}_SightReview.prefab`
  + `Assets/Data/Weapons/SightCalibration/Review/*_SightReview.asset` (это WeaponInfo, не профили). Со стены лобби
  сняты, но **всё ещё в `WeaponRegistry.asset` и `Assets/Data/Arsenal/ArsenalCompositionCatalog.asset`** (зона gameplay).
- Черновик `Assets/Prefabs/Weapons/SightCalibrationDrafts/MKR9_NativeProbe_b1bbb91d.prefab` — неуспешная проба Save,
  не оружие; удаляется на `cleanup-h`.
- Тесты: `Assets/Tests/EditMode/Sights/*` (NativeSightStand, SightAutoZeroSolver, SightKitPrefab, SightKitScale),
  `Assets/Tests/EditMode/SightSplit/SightPartSplitterTests.cs`; `KinemationWeaponTests` проверяет окно коллиматора.
- Пробы: `Tools/Probes/ar15-scale-optic-invariants.cs.txt`, `Tools/Probes/tr15-optic-isolated-render.cs.txt`.
- SDK-патч 42 `UxrComponent.SetEditorUniqueId` (`Docs/UltimateXR/sdk-patches.md`) — зависимость ручного стенда.

### Blender (вне Git, worktree `F:/CodexWorktrees/weapon-system/tasks/weapon-system/reports/`, путь игнорируется `.gitignore:144 /tasks/*/reports/`)
- `iron-kits/iron-kits.blend` (2026-10-12) — нарезка комплектов TR15/MX68 из FBX пака: подвижные детали отделены,
  pivot на F/R. Скриншоты до/после и pivot: `iron-kits/01…14_*.png`.
- `iron-split/iron-split.blend`, `iron-split-2.blend` + `islands.json` (AK105/Mk14/Viper/R08, меши Kinemation),
  `islands-2.json` (Hands-пак, Herrington). Это разбор островов вшитых прицелов 16 стволов, по папке скриншотов
  на ствол (`iron-split/<Weapon>/*.png`).
- `sight-split/gen_table.py` — генератор `SightSplitTable.cs` из islands*.json; `table_template.cs.txt`,
  `plan-477/485.{json,txt}` (сухой прогон: 11 стволов без отказов, Δ ≤ 0,001 мм), `lease-471.md`.
- `sight-autozero/lease-458.md` — итоги аренд 458/463/468/494: размеры комплектов, проверка Apply на копиях
  AR15/MP5K, решатель по родным прицелам.
- Python-скриптов для Blender в Git нет. Нарезка, видимо, шла через Blender MCP (`execute_blender_code`),
  код не сохранён. Воспроизводимость — через .blend и islands.json.

### Локальные доказательства 2026-10-04/05 (основной checkout, вне Git)
- `Docs/tasks/report/weapon-sight-calibration/` (297 МБ: README, `checkpoint/` с py-скриптами checkpoint,
  `runs/`, `handoff-package.zip`).
- `tmp/weapon-sight-calibration/` (236 МБ: audit/geometry json, donor/srs кандидаты, lobby-range, manual/backups,
  prototype, visual-review, audio-candidates, анализ геометрии py). `tmp/sight-calibration-research/audit.py`.
- Звук щитов не выбран; кандидаты в `tmp/weapon-sight-calibration/audio-candidates/`, источник
  `F:/UnityProjects/_SoundLibrary/Universal Sound FX`.

### Документы (dev)
- Сводка и опора задачи: `Docs/weapons/sight-calibration.md` (модель e(d) = h·|1−d/R|, инварианты, инструменты,
  история приёмки, открытые вопросы).
- Решения: `Docs/weapons/decisions.md`, строки 54–71.
- `experts/weapon-system/journal/2026-10-12-sight-tools.md`; backlog `sight-writer` (п.5) и
  `sight-open-questions` (п.15) — `experts/weapon-system/backlog.md`.
- Исходные документы 2026-10-05: `Docs/tasks/weapon-sight-calibration-{handoff,design,review,plan,commit-plan}.md`,
  `weapon-sight-manual-calibration.md`, `weapon-sight-playmode-review.md`.
- План этапов: `tasks/weapon-system/plan.json` (stages `sight-tr15-optic`, `sight-iron-kits`, `sight-arsenal-dedupe`),
  `tasks/weapon-system/Readme.md` строки 27–37.

## 3. Решения, ловушки, открытые вопросы, точка остановки

### Решения пользователя
- Дистанция сведения по умолчанию 15 м, override в (0, 20] м; смена zero = повторная калибровка.
  ShotSource и направление снаряда не трогаются; двигаются только детали прицела.
- 2026-10-11: калибровка всего оружия — главная задача. Эталон — TR15 Sight Review (XPS2, конечный фокус 15 м),
  его прицел уже в основном TR15. Остальные стволы — родная механика (целик/мушка), не замена на коллиматор.
  Порядок — чем быстрее результат, тем раньше.
- 2026-10-12, принципы механики:
  - Подвижные детали отделяются, включая вшитые в корпус: разделение в сборщике, повторяемое.
  - Рамки только в поднятом положении.
  - Pivot подвижной детали — точно на точке прицеливания.
  - Сведение только по высоте, одной деталью; боковую ошибку инструмент показывает, но не исправляет.
  - Статические меши, не skinned.
  - Комплект — один объект на планке.
- 2026-10-12, по стволам:
  - FABARM (ShotgunReal) — не калибруем.
  - Remington 11-87 (Herrington) — передняя лопатка, если получится.
  - PPK — только показ ошибки (+14,7 мм на 15 м).
  - Uzi — мушка целым островом с ушками; 0,4 мм вбок (3,25 см на 15 м) оставлены.
  - Ход: TR15 −8/+3 мм, Mk14 +8 мм, Viper −4 мм, остальные ±3 мм.
- 2026-10-12: «пока делаем только инструменты — финальная калибровка будет отдельной задачей» — это и есть текущая задача.
- 2026-10-05: для SniperRifle — только механический комплект SRS/SRM12, без кратности.
  Копии Sight Review остаются рабочим материалом до переноса в оригиналы.

### Результаты решателя на стенде «Родной прицел» (Z = 15 м, аренда 494) — ещё не применены
Укладываются в ход ±3 мм:

| Ствол | Нужный сдвиг |
|---|---|
| AK105 | +0,91 мм |
| R08 | +0,57 мм |
| MP5K | −0,90 мм |
| Uzi | −0,90 мм |
| Herrington | +1,65 мм |
| Revolver | +0,39 мм |
| Gun / Browning Hi-Power | −0,19 мм |
| Scar | −2,03 мм |

Требуют расширенного хода (уже выставлен решениями):

| Ствол | Нужный сдвиг |
|---|---|
| Mk14 | +6,67 мм |
| Viper | −3,07 мм |

PPK — только измерение: |e| = 16,1 / 15,4 / 14,1 мм на 5 / 10 / 20 м.

Комплект TR15 на AR-15 требует −5,6 мм. Старые расчёты 2026-10-05: Viper мушка +3,04 мм, SRM12 −5,32 мм.

### Не покрыто ни комплектом, ни разделителем
- M16 — мушки нет, нужен комплект (TR15).
- SRM12, SniperRifle, MKR9 — донорные половины пересаживаются комплектом. У MKR9 контейнер со scale ≈100.
- ShotgunReal — по решению не калибруется.
- SDKGun, ShotGun (SDK), Machinegun — решения нет: **открытый вопрос**.

### Ловушки
- `SaveAsPrefabAsset` обнуляет `NetworkIdentity._assetId`. После записи нужен normalizer и сверка с диском:
  тест `Assets/Tests/EditMode/Prefabs/NetworkAssetIdOnDiskTests.cs`; та же ошибка поймана у avatar-grip (хаб 6378).
- Один writer префабов оружия — `WeaponSystemAuthoring` (`Docs/weapons/invariants.md` п.10).
  - `WeaponSightAuthoring` (Apply комплекта) и старые `ManualSightCalibrationAuthoring.Save`,
    `SightGameplayReviewBuilder` — ещё три писателя.
  - До решения `sight-writer` Save/Apply ручного стенда не выполнять. Включая Apply, согласовать вписывание
    в единственного writer.
- `SightSplitTable` привязан к GUID/localFileId меша и к числу вершин и треугольников.
  - Пересборка сборщиком пака или правка меша ломает спецификацию. Таблицу правят через разбор и `gen_table.py`,
    не руками.
  - Целик Viper и обе половины Browning сидят на затворе (Slide/Gate) и едут с ним.
- Сборщик пака затирает баланс в префабе: после пересборки нужен `WeaponBalanceApplier.Apply`
  (`Docs/gameplay.md`). Иначе краснеет `WeaponBalanceTests`.
- MKR9: масштаб импорта ≈100 не выпечен (`Docs/weapons/sight-calibration.md` п.8).
- Масштаб корня берётся текущий (AR-15 0,70), комплект в реальном размере через `SightKitScale`.
- Встроенная VerifyOverlay сначала считала в системе корня FBX (ошибка 23–262 мм); исправлено (lease-458).
- `LobbyLayoutTests.За_краем_мишени_видимые_с_арены` красный (аренда 463), ожидания устарели.
- Документы отстают: `Docs/gameplay.md` («Лобби», строка ~522) ещё пишет про 5 копий Sight Review на стене.
  `tasks/weapon-system/Readme.md` даёт SHA оптики `9d5276eb` — это SHA до rebase, в dev `5fc162060`.
- Зоны в `experts/*/expert.json`:
  - weapon-system владеет `Assets/Editor/VR_Battlegrounds/Weapons/**`, `Assets/Scripts/Weapons/**`,
    `Assets/Prefabs/Weapons/**`, `Assets/Art/Weapons/**`, `Assets/Data/WeaponSystem/**`,
    `Assets/Data/Weapons/SightCalibration/**`.
  - Без владельца: `Gameplay/SightSplit/**`, `Assets/Tests/EditMode/{Sights,SightSplit}/**`, `Assets/Scenes/Tools/**`,
    `Scripts/Debug/SightCalibrationBench.cs`, `Scripts/Maps/ShootingRange*.cs`, `WeaponRegistry.asset`
    (у weapon-system — только watch).

### Открытые вопросы (`Docs/weapons/sight-calibration.md` п.7 и backlog)
1. Writer прицелов: таблица и Apply внутри `WeaponSystemAuthoring` или отдельный writer только узлов прицела?
2. Режим «Родной прицел»: встроить разделитель в `KinemationWeaponBuilder`/`HandsPackWeaponBuilder`
   (решение 2026-10-12 «разделение в сборщике, повторяемое») — Apply для него не написан.
3. Перенос копий Sight Review в оригиналы; удаление из `WeaponRegistry`, `ArsenalCompositionCatalog` (gameplay)
   и черновика MKR9 (refactor-plan этап H, В10).
4. Viper (мушка часть Base), SRM12 (ход и крепление), SniperRifle (переходные площадки, масштаб донора),
   MKR9 (выпечка масштаба ≈100).
5. Полный конвейер [D] Audit→…→Apply→Verify с отпечатками или ручной стенд + writer?
6. Zoom (поля нет); stereo/eyebox `FiniteFocusReticle` на Quest; звук щитов; стволы без решения
   (SDKGun, SDK ShotGun, Machinegun); единое обозначение zero (R или Z).
7. Судьба старого конвейера `Weapons/Calibration/*` и сцен `Scenes/Tools/WeaponSight*` после перехода на Sight Auto-Zero.

### На чём остановились и следующий шаг
Инструменты влиты и опробованы пользователем на стенде. `WeaponSightTable` пуст, ни один префаб не сведён.
Только TR15 (коллиматор) принят в шлеме.

Записанный следующий шаг (`experts/weapon-system/journal/2026-10-12-sight-tools.md`, `tasks/weapon-system/Readme.md`):
1. Встроить разделитель в сборщики.
2. Включить `SaveUnlocked`/`ApplyUnlocked`.
3. По стволам: свести, записать в таблицу, применить к префабам — в порядке быстрого результата.
4. Проверить в шлеме на стрельбище Lobby.

Тесты новой логики — только после приёмки пользователем.

## 4. Связанные зоны и зависимости
- **weapon-system** — владелец префабов, `WeaponSystemAuthoring`, сборщиков пака, Sights-инструментов.
  Его задача передана Codex: worktree `F:/CodexWorktrees/weapon-system`, ветка `codex/r08-magazine-only`@`7e3605570`,
  не влита.
  - Эта ветка меняет `R08.prefab` (+288 строк), `Revolver.prefab`, `WeaponSystemAuthoring.cs` и SDK-патч 82.
  - Риск конфликта со `SightSplitTable` (R08, Revolver) и с writer'ом. Работать после её вливания
    или согласовать.
  - Этап `cleanup-h` (planned) пишет `Assets/Editor/VR_Battlegrounds/Weapons/**` и `Gameplay/**`
    и удаляет `MKR9_NativeProbe`.
- **avatar-grip / hand-rig-quality** (worktree `F:/CodexWorktrees/cyborg-hands`) пишет записи хвата во все префабы
  оружия, включая `SightReview/*` (`69b0420e4`). Это второй писатель тех же файлов, нужен zone-ack. Деталь прицела
  не должна попадать в зону хвата.
- **gameplay** — `ArsenalCompositionCatalog.asset` (копии Sight Review), `MapRuntimeCatalog.asset`
  (`ArsenalFingerprint`, этап `catalog-fingerprint`). `FullDemoArsenal.asset` — weapon-system (хаб 4063).
- **level-design** — `Assets/Scenes/Lobby.unity` (стрельбище, щиты, мониторы); `lobby-decoration` тоже пишет каталог.
- **network** — SDK-патч 42 `UxrComponent.SetEditorUniqueId`; `_assetId` префабов.
- **performance** — static, не skinned меши прицелов; стоимость шейдера сетки на Quest.
- **launch-infra** — PlayLaunch для Play Mode-проверок; `xr-input` (скриптовый ввод шлема) может пригодиться для
  автоматической проверки прицеливания.

## 5. Перенос в ветку `weapon-sight-calibration`
Ветка уже содержит всё принятое (`3a46cd931`, `d6c0fded0`, `6c964ec14`, `5fc162060`, `a3b06c637`, `b624fdb3c`,
`02f8d4fda` и docs `49e0f0a76`, `60a11fee2`, `12aa19ac3`, `b1d73e916`). **Cherry-pick не требуется.**
1. Выполнено 2026-10-20 (по указанию пользователя «собрать все полезные материалы и закоммитить»):
   из `F:/CodexWorktrees/weapon-system/tasks/weapon-system/reports/` и `tmp/weapon-sight-calibration/` в Git
   перенесены `blender/*.blend` + скриншоты pivot комплектов, `tools/sight-split/*`, `tools/research-2026-10-04/*`,
   `changelog/2026-10-12-*` (lease-458, lease-471, plan-485.txt). Опись — `tools/Readme.md`.
   Скриншоты разборов по стволам (~48 МБ), `plan-477/485.json`, лог компиляции — в `reports/` вне Git.
2. Локальные пакеты 2026-10-05 не переносились (сотни МБ, большая часть — снимки и git-обвязка checkpoint):
   `Docs/tasks/report/weapon-sight-calibration/`, `tmp/weapon-sight-calibration/`.
3. Дождаться вливания `codex/r08-magazine-only` (R08/Revolver/`WeaponSystemAuthoring`) или договориться
   с weapon-system до правок этих префабов. Затем rebase на свежий origin/dev.
4. Технические checkpoint-ветки `codex/tmp/weapon-system-sight-*`, `…-dedupe-*`, `…-native-sight-mode-trial-*` —
   не переносить: они полностью в dev, только история.
