# xr-input r1 — симулированный ввод устройства в обычный сетевой клиент (2026-10-19)

Цель вехи 1: обычный сетевой клиент в Editor/MPPM без шлема получает ввод «шлема» (голова, два контроллера,
кнопки/оси) от сценария; дальше весь путь штатный — UxrStandardAvatarController, хват, ноги, IK, сеть. Сервер видит
обычного игрока. Бот, клиповые тела и прямые вызовы SDK из сценария здесь не участвуют.

## Состав

| Часть | Где | Что делает |
|---|---|---|
| `DeviceFrame` | `Assets/Scripts/Debug/E2E/Input/` | кадр устройства: голова и два контроллера в пространстве корня аватара (пространство трекинга), кнопки grip/trigger/A/B/стик, оси grip/trigger 1D и стик 2D; по умолчанию всё отпущено |
| `IAvatarInputSource` | там же | `bool TryRead(ref DeviceFrame frame)` — источник ввода (сценарий; позже Blaze, бот) |
| `ScriptedControllerInput : UxrControllerInput` | там же, класс компилируется всегда | Dual; кнопки и оси из кадра через штатные `SetButtonFlags`/`GetInput1D/2D`; хаптика — пусто |
| `ScriptedControllerTracking : UxrControllerTracking` | там же, всегда | `UpdateSensors` → штатный `UpdateSensor(side, local)`, сглаживание 0; запястья пишет штатный `UpdateAvatar` |
| `E2EScriptedInput` | там же, `#if !VRBG_NO_E2E` | единственный владелец включения: привязка к своему Local-аватару, правило «один источник», `CameraTrackingEnabled=false`, local pose камеры в `StageUpdating(Update)` (как `E2EHeadPose`), включение/выключение компонентов, возврат состояния |
| Компоненты в `PlayerBase.prefab` | выключены | R1: ссылка на ввод синхронизируется (`UxrAvatar.OnControllerInputChanged`), добавленный в рантайме компонент у других не резолвится |
| Пилот `ScriptedPilot` | `Assets/Scripts/Debug/E2E/Input/` | намерения → кадры: ходьба головой (≤ 1,2 м/с), взгляд, подвод кисти к точке хвата, нажатие/отпускание |
| Сценарий `scripted-client` | `Assets/Scripts/Debug/E2E/Scenarios/` | JSON-шаги (интерпретатор поверх `ctx.Step`/`WaitServer`) на клиенте; серверный оракул на сервере |

## Инварианты

- Один источник ввода на процесс: при включении проверяется, что других включённых `UxrControllerInput`, кроме
  dummy, нет; иначе отказ (`GameLog.Error`). Реальная XR и скриптовый ввод одновременно не работают.
- Один писатель: local pose камеры — только `E2EScriptedInput`; запястья — штатный `UxrControllerTracking`;
  кости, пивот, Dummy Forward, масштаб, BodyIK, PlayerCalibration не пишутся.
- Перемещение — только позой головы и контроллеров в пространстве трекинга (физическая ходьба); корень не двигается,
  `MoveAvatarTo`/телепорт не используются. Скорость ≤ 1,2 м/с, без скачков.
- Сценарий не вызывает `UxrGrabManager`, `TryToShootRound` и прочие действия SDK: только кадр устройства.
- Релиз: классы компонентов есть во всех сборках (иначе missing script в префабе), но включает их только
  `E2EScriptedInput` (`#if !VRBG_NO_E2E`); в сборке с `VRBG_NO_E2E` компонент в `OnEnable` выключает себя с
  `GameLog.Error`.

## Веха 1 — приёмка (согласована пользователем 2026-10-19)

MPPM: выделенный сервер + 1 клиент, затем + 2. Клиент позой головы/рук подходит к своей стене арсенала, grip →
оружие в руке штатным хватом. Оракул сервера: слот пуст, касса списала деньги, владелец — клиент, у сервера обычный
игрок. Второй клиент видит оружие в руке первого. Голова ±1 см/0,1°, запястья ±1 мм, 0 `UxrComponentNotFound`.
Пользователь видит прогон в Editor. Плеер (`-e2eInput scripted`) — вторым шагом.

## Ревью

avatar-ik (камера, запястья, калибровка, ноги), manipulation (хват), network (репликация), launch-infra (стенд, E2E).

## Решения 2026-10-19 (после разведки gameplay)

- Elimination стартует при `minPlayersToStart = 2` и игроке в каждой команде; боты уже считаются в этот минимум
  (`EliminationMode.IsPlayersReady` берёт все сессии `PlayerRoster` с ролью Player, `PlayersManager.RegisterBot`),
  но стенд запрещает e2e с ботами (`BotCount != 0` → `e2e-launch-conflict`). Поэтому: S+1 — смоук ввода
  (`scripted-input-smoke`), покупка — S+2 (`scripted-wall-purchase`).
- Команду клиент выбирает тем же `CmdRequestTeamChange`, что планшет (нажатие пальцем по планшету — позже).
  Старт матча — серверный эквивалент кнопки админа: `AdminMapCommands.IsAvailable(GoLive)` + `MapReferee.GoLive()`
  (у выделенного сервера нет сессии, клиенты стенда не админы).
- При Begin все становятся призраками (свой `GhostAvatar.prefab`), возрождение — входом в свою зону: компоненты
  скриптового ввода нужны и в PlayerBase (живые тела через варианты), и в GhostAvatar.
- Хват SDK — по флагу Press Grip с фронтом (отпущен ≥1 кадр); выстрел — флаг Trigger; Touch не читается.
