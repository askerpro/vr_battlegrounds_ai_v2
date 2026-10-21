# bot-input r1 — ввод устройства в аватар бота (дизайн, согласован пользователем 2026-10-20)

**Решения пользователя 2026-10-20** (все — рекомендованные варианты):
1. SDK-флаг `InputDriven` у аватара `UpdateExternally` — патч UltimateXR **89** (зарезервирован, ключ `input-driven`);
   своего исполнителя «Grip → TryGrab» без SDK не делаем.
2. Общий слой (кадр устройства, компоненты ввода, пилот) переносится в продукт `Assets/Scripts/Player/Input/`;
   в E2E остаются фасад клиента, интерпретатор JSON и сценарии. Пилот — после вливания этапа `hit` e2e-combat.
3. Порядок: сначала бот под сценарием (итерация 1), затем Blaze через ввод (итерация 2).
4. Прежний путь ботов удаляется в этой задаче после приёмки Blaze через ввод; до того на одном аватаре — никогда оба.

**Состояние 2026-10-21:** итерации 1 (демо Input Smoke) и 2 (Blaze через ввод) приняты; прежний путь и кейсы стенда
прежнего пути удалены (решение пользователя); добавлен патч 90 (досылание при Initialize, дефект выдачи стартового
пистолета). Как бот держит оружие — пилот-стрелок; сведение с клиентом и `ScenarioInputSource` — после series.

Этап 2 эпика [bots-epic](../../bots-epic/Readme.md). Основа — исследование ввода и итог в
[bots-control-modes/Details.md](../../bots-control-modes/Details.md#итог-input-research-2026-10-19) и принятый скриптовый
ввод клиента ([xr-input r1](../../bots-control-modes/contracts/xr-input.md), `43332888b`, выстрел курком `fce97ecd4`).

## Цель и граница

Бот на сервере получает **кадр устройства** (голова и два контроллера в пространстве трекинга, кнопки и оси) и дальше
идёт кодом человека: `UxrStandardAvatarController` (хват по Grip, курок), `UxrControllerTracking` (запястья),
BodyIK/ArmIK/ноги от головы, `UxrFirearmWeapon` (выстрел), правила хвата проекта, сеть. Источник ввода — Blaze или
сценарий. Сценарий и Blaze **не вызывают действий SDK** — только формируют кадр.

Вне этапа (этап 3 rules-parity): закупка хватом со стены, готовность жетоном, выбор команды, тело после смены карты.
До него оружие бота по-прежнему выдаёт `BotGunner.SpawnPurchase` (в кобуру/к кисти) — бот **берёт его рукой по Grip**.

## Слои кадра сервера

```
мозг (Blaze | сценарий) → намерения → пилот (общий с клиентом) → DeviceFrame → устройство аватара → SDK (InputDriven) → код человека
```

| Слой | Что | Где |
|---|---|---|
| Мозг | Blaze: прокси NavMeshAgent, цель, shootEvent; сценарий: JSON-шаги | `BlazeInputSource` (Bots), `ScenarioInputSource` (E2E, интерпретатор шагов клиента) |
| Намерения | «иди к точке пола», «смотри на», «кисть к граберу-цели», «наведи ствол на», «нажми/отпусти» | API пилота |
| Пилот | намерения → кадр устройства, как его дал бы человек: ходьба головой ≤ 1,2 м/с, руки за головой, кисть к цели через смещения «грабер → запястье → сенсор», ритм курка, замкнутое наведение по фактическому дулу | `AvatarPilot` (+ стрелок) — бывшие `ScriptedPilot`/`ScriptedGunner`, без статики |
| Устройство | один на аватар: включает выключенные в префабе `ScriptedControllerInput/Tracking`, пишет local pose камеры, позы сенсоров и кнопки раз за кадр в `StageUpdating(Update)` | `AvatarInputDevice` — обобщение `E2EScriptedInput` с процесса на экземпляр |
| SDK | флаг `InputDriven` у аватара `UpdateExternally`: стадия Update контроллера, ввод из своего устройства, Local-гейты хвата/курка | патч UltimateXR (номер через `patch-reserve`) |

## Один писатель на степень свободы

| Степень свободы | Писатель (после этапа) | Было у бота |
|---|---|---|
| Корень аватара | только спавн (`AvatarManager`), дальше **никто** — корень стоит | `BotAvatarDriver` каждый кадр |
| Пивот камеры (пол, калибровка) | `AvatarCalibrationApplier` | то же |
| Local pose камеры (голова) | `AvatarInputDevice`, `StageUpdating(Update)` | `BotAvatarDriver` в `AvatarUsingTracking` |
| Позы сенсоров контроллеров | `ScriptedControllerTracking.UpdateSensors` → штатный `UpdateSensor` | — |
| Кости запястий | штатный `UxrControllerTracking.UpdateAvatar` (писатель кисти № 1 по контракту avatar-ik) | `BotAvatarDriver` мирово |
| Кнопки/оси | `ScriptedControllerInput.UpdateInput` (штатный `SetButtonFlags`) | семантические флаги `BotInputFrame` |
| Хват/отпускание | `UxrStandardAvatarController` по фронту Grip → `TryGrab`/`TryRelease`, правила `PlayerGrabManager`/`GrabRules` | `BotHands` → `UxrGrabManager.GrabObject` со снапом |
| Выстрел | `UxrFirearmWeapon` по курку руки на спуске | `BotGunner.ExecuteRequestedShot` → `TryToShootRound(0)` |
| Поза пальцев | штатный путь контроллера по состоянию хвата | патч 84 `UpdateExternalHandPoseFromGrabState` из драйвера |
| Корпус / руки / ноги | BodyIK / ArmIK / ноги от скорости головы | то же, но от движущегося корня |
| Поза у клиентов | `NetworkTransform` сервер → клиенты (`ServerAuthoredAvatar`), тот же набор трансформов | то же |

Правило: у аватара ровно **один** источник ввода. `AvatarInputDevice` отказывает (`GameLog.Error`), если у аватара
включён другой `UxrControllerInput` (кроме gamepad), если тело ведёт `BotAvatarDriver`, или если камеру пишет
`E2EHeadPose`; `BotAvatarDriver` симметрично не включается на аватаре с подключённым устройством.

## SDK-флаг `InputDriven`

`AvatarMode.Local` боту нельзя (`LocalAvatar` = первый Local, камера/AudioListener, 22 файла игры опираются на
`LocalAvatar`). Флаг на `UxrAvatar` при `UpdateExternally`: «ввод этого аватара — на этой машине». Ставит только
`AvatarInputDevice` при подключении, снимает при отключении. Точки патча — [таблица ниже](#точки-патча-sdk).

## Устройство на аватар (`AvatarInputDevice`)

- Экземпляр на аватар вместо статики процесса. `E2EScriptedInput` становится фасадом «устройство моего Local-аватара»
  для скриптового клиента — поведение клиента не меняется.
- Разрешение включения — **по экземпляру** (заявка устройства), а не глобальный `ScriptedInputGate`: компонент без
  заявки выключает себя. Заявки: клиент — только E2E (`#if !VRBG_NO_E2E`, как сейчас); бот — только сервер под
  `IsWorldAuthority`, аватар без владельца (`connectionToClient == null`).
- Пространство трекинга — пивот камеры, как у шлема; позы контроллеров переводятся в пространство корня минус
  `HeightOffset` (как в `E2EScriptedInput.WriteControllers`).
- Смена тела (смерть → призрак → возрождение): устройство переходит на новое тело; пилот сохраняет **позу головы в
  пространстве трекинга** (физическая голова не телепортируется), а не ставит голову над новым корнем; камеру
  устройство пишет сразу при привязке.

## Общий пилот

`AvatarPilot` — `ScriptedPilot`, привязанный к экземпляру устройства (не к статике `E2EScriptedInput`). Стрелок —
`ScriptedGunner`: подход по свободному пути, замкнутое наведение по `ShotSource`, курок «отпущен 2 → нажат 3 → отпущен».
Для двуручного оружия добавляется поддержка: левая кисть к снапу поддержки и Grip; цель рук для наведения считает
решатель `BotWeaponAim` (только чтение снапов), исполняет пилот. Плавность подвода кисти (`BotHandTrajectory`) — в
пилоте, для клиента и бота одинаково.

## Источники ввода

**ScenarioInputSource** — тот же JSON и интерпретатор шагов, что у скриптового клиента, привязанный к устройству бота
на сервере. На этапе 2 — шаги без клиентских команд: `walk`, `turn`, `step`, `carry`, `shoot-enemy`, `release`,
`hold`, `expect-held`, захват предмета рукой. `choose-team`, стена, жетон — после rules-parity (`Request*`).

**BlazeInputSource** — Blaze остаётся мозгом. Прокси NavMeshAgent — навигация мозга, не тело: голова идёт к прокси
пилотом (≤ 1,2 м/с; скорость агента ограничивается тем же пределом; при расхождении > 0,5 м прокси подтягивается к
голове — правда у тела). Цель Blaze → взгляд и наведение; shootEvent → нажатие курка стрелком, когда ствол наведён и
линия огня свободна (решение мозга «жать или нет», не гейт выстрела). Вне боя — путь `BotNavigator` → шаги ходьбы.
Экипировка: кисть к снапу оружия в кобуре → Grip; уборка — кисть к якорю → отпустить Grip (размещение решает SDK).

## Сеть

Новых сообщений нет. Направление `NetworkTransform` бота прежнее (сервер → клиенты), набор трансформов тот же; корень
больше не движется. События хвата и выстрела бота авторит сервер (`IsAuthor = isServer && !hasOwnerConnection`), как
сейчас. Фиктивных соединений нет.

## Что уходит из кода ботов (после приёмки Blaze-режима)

`BotAvatarDriver` (запись корня/камеры/кистей, вызов патча 84), `BotHands` (`GrabObject`/`ReleaseObject`/снап),
`BotGunner.ExecuteRequestedShot`/`TryToShootRound`/`RequestAutomationPreparation`-гейт, семантический `BotInputFrame`,
перенос целей вместе с корнем (`FollowRoot`), `BotBody` как источник ног (курс/взгляд переходят в намерения пилота).

## Итерации (каждую проверяет пользователь на стенде)

1. **Ввод в бота сценарием.** SDK-патч `InputDriven`, `AvatarInputDevice`, пилот без статики; на сервере (BotCombatStand
   или S + клиент-наблюдатель) один бот под `ScenarioInputSource`: идёт головой 3 м с поворотом, берёт оружие из кобуры
   по Grip, наводит и стреляет курком по цели стенда. Остальные боты — прежним путём (на одном аватаре — никогда оба).
2. **Blaze через ввод.** `BlazeInputSource`; все боты на вводе; прежний путь удаляется.
3. После подтверждения пользователя — тесты, heavy-регрессия, ревью зон (bots, avatar-ik, manipulation, network,
   weapon-system, launch-infra), вливание FF.

## Координация

- e2e-combat сейчас правит `ScriptedPilot`/`ScriptedGunner`/`ScriptedClientRunner`/`InputScript`: перенос и рефакторинг
  пилота — после вливания их этапа `hit`, порядок — через хаб.
- e2e-heavy-editor: компоненты `ScriptedInput` уже лежат в `PlayerBase`/`GhostAvatar` (меню `ScriptedInputPrefabSetup`);
  тела ботов — те же префабы игрока. Новых правок префабов не планируется.

## Риски

- Производительность: стадия Update `UxrStandardAvatarController` на каждого бота на Quest-хосте — замер в этапе 5.
- Хват берёт ближайшую разрешённую точку: перед нажатием пилот читает кандидата SDK (`TryGetGrabCandidate`, патч 67);
  расхождение с целью — лог как баг досягаемости/правил, не обход.
- Корень стоит, голова в десятках метров от него: всё, что считает «где игрок» по корню, ошибётся — [см. сверку](#корень-стоит-кто-читает-корень).
- Двуручное наведение реальными ограничениями SDK — новое и для клиента.
- Скорость Blaze-прокси против предела ходьбы — тактика Blaze станет медленнее.

## Точки патча SDK

Сверка HEAD 246d91e11 (пути от `Assets/ThirdParty/UltimateXR/Runtime/Scripts/`). Исследование 2026-10-19 насчитало
5 точек; с огнём и подключением устройств их **около 12**. Свободный номер патча — 89 (резерв через `patch-reserve`).
Маркировка `// VR Battlegrounds patch N:`; запись — `tasks/bots-input/changelog/<дата>-sdk-<N>.md`.

| # | Где | Сейчас | С флагом | Зачем |
|---|---|---|---|---|
| 1 | `Core/UxrManager.cs:2458` (`LocalAvatarControllers` → стадия Update :1366) | только `Local` | `Local \|\| InputDriven` | `UpdateInputDevice`, `UpdateTrackingDevices`, `IsGrabbing` |
| 2 | `Core/UxrManager.cs:2378` (`TryGetUpdatableController`, localOnly на :1999) | только `Local` | `\|\| InputDriven` | стадия AvatarUsingTracking |
| 3 | `Core/UxrManager.cs:2070` (Animation) | полная только `Local` | `\|\| InputDriven` | позы рук по событиям контроллера |
| 4 | `Avatar/UxrAvatar.cs:247` (`ControllerInput`) | `UpdateExternally` → external/Dummy | при `InputDriven` — включённый ввод аватара, **без** синхронизируемого `OnControllerInputChanged` | Grip/курок читаются из нашего ввода |
| 5 | `Mechanics/Weapons/UxrFirearmWeapon.cs:474, :499` | `Local` | `\|\| InputDriven` | курок держащей руки |
| 6 | `UxrFirearmWeapon.cs:478, :485-487` | `UxrAvatar.LocalAvatarInput` | `grabber.Avatar.ControllerInput` | на dedicated `LocalAvatarInput == null` |
| 7 | `Mechanics/Weapons/UxrWeapon.Custom.cs:239` (`HasLocalTriggerContext`) | `Local` | `\|\| InputDriven` | readiness-ledger |
| 8 | `UxrFirearmWeapon.cs:437, :450, :593, :606-607` | `Local` | `\|\| InputDriven` | учёт патронов, эпизод курка |
| 9 | `Manipulation/UxrGrabManager.Manipulation.cs:2093` (AlignToController) | `Local` | `\|\| InputDriven` | как у человека |
| 10 | `UxrGrabManager.Manipulation.cs:2563` (дальнее отпускание) | `== LocalAvatar` | `\|\| InputDriven` | как у человека |
| 11 | `Devices/UxrControllerTracking.cs:240-250` (статическое `GlobalControllerConnected`) | включает трекинг **по типу** отправителя | только при совпадении аватара отправителя | иначе отключение одного бота выключит трекинг у всех |
| 12 | `UxrControllerTracking.cs:192-224` (`RepeatSetupCameraCoroutine`) | крутится каждый кадр без XR | не запускать при `InputDriven` | headless-сервер |

Связанное, не точки флага: патч 84 (`UxrStandardAvatarController.cs:325`) при `InputDriven` возвращает false — позы
пальцев ведёт `ProcessControllerEvents`; хук патча 24 (`UxrStandardAvatarController.Custom.cs:199`, игровой
`RemoteAvatarIKThrottle`) не пропускает BodyIK `InputDriven`-аватара; на аватаре бота нет включённых `UxrLocomotion`
(иначе `UxrSmoothLocomotion` двигает корень). Оставить как есть: сопротивление хвата (патч 73 — для бота предмет
жёсткий), подсветка кандидатов (:844), `AvatarMoved` (:412 — корень не движется), хаптика курка (только Local).
Игровой `UxrReadinessLedgerPort.CanAuthor` уже требует `IsAuthorOfItem` — у бота автор сервер.

## Корень стоит: кто читает корень

Сверка HEAD 246d91e11 (пути от `Assets/Scripts/`). Игровая логика «где игрок» уже смотрит на голову: `BotSenses.FeetOf`
(`CameraFloorPosition`), цель Blaze (на слое Player только коллайдер `Camera`), зона спавна (`IsHeadCenterInZone`),
прицел `BotGunner` (коллайдеры цели). Корень при смене тела — начало трекинга, как у людей (`AvatarManager.cs:477-504`,
`SpawnPlaceRegistry`). `ServerAuthoredAvatar` переводит все NT аватара в ServerToClient — неподвижный корень просто
перестаёт слаться. Миникарты/радара нет.

Что меняется:

- **`WallPassMonitor` начнёт проверять ботов** (`Player/WallPass/WallPassMonitor.cs:117-157`): сейчас движущийся корень
  сбрасывает историю каждый кадр, то есть бот обходит правило прохода сквозь стены. С вводом это правило заработает
  для ботов как для людей — пилот обязан вести голову по навигационному пути, не срезая углы. Это паритет, а не баг.
- **Первый кадр нового тела**: голова в позе префаба у корня до первой записи камеры — `TeamSpawnZone` может
  засчитать призрака «в зоне». Устройство пишет камеру **сразу при привязке** к телу (из позы головы пилота в
  пространстве трекинга), не дожидаясь стадии Update.
- `BotBody.Feet`/`Place`/`BotMind.Remember` (ноги через смену тела) больше не нужны — непрерывность даёт поза головы
  в пространстве трекинга; `FollowRoot` удаляется.
- У людей уже так, у ботов станет видно с первой минуты: центр разлёта снаряжения и игнор столкновений трупа от корня
  (`Player/Corpse/DeathDropEjection.cs:56`, `Corpse.cs:177`) — в известные долги, владельцу gameplay.
- Проверить на стенде: объект `Camera` бота активен (выключен только компонент Camera) — на его коллайдере держатся
  Blaze и зона спавна.
