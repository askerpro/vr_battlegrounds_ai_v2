# Переключаемые режимы управления ботами: ServerPuppet / ClientDevice (проект, 2026-10-12)

Решение пользователя: «бот и клиент отличаются только источником ввода: у игрока — шлем и контроллеры, у бота —
Blaze через наш адаптер»; «режим переключения: дешёвые быстрые тесты без клиентов (серверный кукловод) и отдельные
клиентские инстансы со скриптом — настоящие локальные аватары». Проект — архитектор (только чтение, HEAD 82ce74e1b).
Механизм ввода устройств — launch-infra (E2EHeadPose a311cdb1, xr-input в их backlog), ответ 5206/5226.

## Слои

| Слой | Что | Puppet (сервер) | Device (процесс клиента) |
|---|---|---|---|
| L1 мозг `IBotBrain` | Blaze (`BlazeBrain`) или скрипт (`BotScriptBrain`) | сервер | клиент |
| L2 намерения `BotIntent` | MoveTo/Stop, LookAt, AimPoint, Fire, Equip, Stow, Drop, Buy, Ready | общий | общий |
| L3 пилот `BotPilot` | W*, траектории, хват по прибытии, правило огня → `BotDeviceFrame` (голова, запястья, корень, **кнопки** grip/trigger/A/B, стики) | общий | общий |
| L4 исполнитель `IBotExecutor` | `Submit(BotDeviceFrame)`, `CanDrive` | `BotAvatarDriver` + `BotPuppetActions` (фронты кнопок → вызовы SDK от сервера, как UxrStandardAvatarController) | `BotXrInputSource` → xr-input launch-infra (SDK сам делает хват/выстрел по кнопкам Local-аватара) |

Флаги `BotInputFrame` → кнопки: GrabPrimary=GripR↓, GrabSupport=GripL↓, ReleaseSupport=GripL↑, Stow=GripR↑ у якоря,
Trigger=TriggerR↓. Вызовы `UxrGrabManager.*`, `TryToShootRound`, `RequestAutomationPreparation` — только в L4 Puppet
(тест-сканер + ревью). Сервисные действия раскрываются исполнителем: покупка (Puppet — BotShopper; Device — хват со
стены арсенала), готовность (ServerSetReady / жетон → CmdSetReady), перезарядка (automation / путь человека — R2).

## ClientDevice = xr-input (launch-infra, ревью avatar-ik)

Состав (предложение): голова 6DoF (обобщение E2EHeadPose, слот StageUpdating(Update)); запястья L/R — наследник
UxrControllerTracking (UpdateSensor, сглаживание 0, до Manipulation); кнопки/оси — наследник UxrControllerInput
(Dual: grip/trigger press+1D, Button1/2, стики; по умолчанию всё отпущено); корень — UxrManager.MoveAvatarTo/
TranslateAvatar (как E2EViews); API источника `IXrInputSource.TryRead(frame, out XrInputFrame)` один на процесс;
включение — плеер `-e2eInput scripted[:script=]`, редактор/MPPM — `PlayLaunchConfiguration.ParticipantInputs[]`
(PauseOnFocusLoss=false, без XR-лоадера); компоненты в PlayerBase.prefab (выключены; R1 — ссылка на ввод
синхронизируется, добавленный в рантайме не резолвится у других). Сервер видит обычного игрока (IsBot=false).
Мозг на клиенте v1 — только скрипт (`ScriptedClientPilot` на свой Local-аватар), навигация NavMesh локально;
Blaze на клиенте — опция.

## Скриптовый мозг

JSON `{name, seed, steps:[{op,args,timeoutS,expect}]}`, шаги ждут событий: wait-phase/round/alive/ghost/respawn,
goto, look, buy, draw, stow, drop, ready, aim, fire, expect (только чтение реплицированного состояния). Назначение:
MPPM — ParticipantInputs + адресная `run-script`; плееры — `-e2eArg script=`; Puppet — `BotDirector.AddBot(Brain=Script)`.
Вердикт: клиент — E2EResult по шагам; сервер — оракул `match-smoke` (содержание — gameplay); агрегация — launch-infra.

## Режимы

(а) быстрый — host/ServerOnly без клиентов, Blaze или Script / Puppet; (б) полный — S + N клиентов, Script / Device;
(в) смешанный. Один скрипт в (а) и (б) — проверка паритета.

## Инварианты

Один писатель на трансформ; нет второго хранилища; нет бот-веток в спавне; новых SyncVar/Cmd/Rpc нет; L1–L3 не
вызывают SDK-действий; бот-компоненты только у аватара, которым управляет этот процесс; в релизе xr-input не
активируется.

## Этапы

| # | Этап (зона) | После | Критерии |
|---|---|---|---|
| 0 | control-modes-design (bots) | сейчас | согласования: launch-infra, avatar-ik, network, weapon-system (R2), gameplay (оракул) |
| LI-1 | xr-input (launch-infra) | согласование состава | xr-input-smoke в MPPM-клоне и плеере: голова ±1 см/0,1°, запястья ±1 мм, grip держит, trigger стреляет путём человека, 0 UxrComponentNotFound |
| B-1 | pilot-split (bots) | clip-cutover | рефакторинг L1–L4 только Puppet: эталоны 100%, 0 ошибок, тесты фронтов кнопок и «нет SDK в L1–L3» |
| B-2 | script-brain (bots) | B-1 | скрипт buy→draw→fire→stow→ready в режиме (а), детерминизм 3/3 |
| B-3 | device-executor (bots) | LI-1 + B-1 | S1C/S2C MPPM: сервер видит обычного игрока, автор хвата/выстрела — клиент, паритет с B-2, 0 ошибок |
| B-4 | match-smoke (bots+gameplay+launch-infra) | B-3 + оракул | полный матч elimination S+3, поздний вход, reconnect — зелёно |
| B-5 | client-blaze (опция) | B-4 | — |

## Риски

R1 ввод в рантайме ломает ControllerInputChanged → компоненты в префабе. R2 automation требует IsWorldAuthority — у
клиента нет: v1 полные магазины / путь человека / вход машины weapon-system. R3 MPPM: фокус-пауза, XR-лоадер, ≤4
игроков, E2EHeadPose пока только плеер. R4 TryGrab берёт ближайший предмет. R5 телепорт/калибровка читают стик/B1.
R6 сетевые тайминги. R7 объём B-1 до pf-quest.

## Решения пользователя (2026-10-12)

- Слои и режимы утверждены (быстрый — серверный кукловод; полный — клиентские процессы с настоящим Local-аватаром).
- Перезарядка у скриптовых клиентов — **v1 без перезарядки** (полные магазины); путь человека — позже.
- **Перемещение скриптового клиента — подменой tracked device**: позы шлема и контроллеров задаются источником (как
  ходит живой игрок в комнате), а не MoveAvatarTo/телепорт. Механизм — у launch-infra/performance (E2EHeadPose
  `a311cdb1`; xr-input в backlog launch-infra) — обобщить на голову+контроллеры+кнопки.
- Порядок: pilot-split и script-brain — **до pf-quest** (перф на итоговом коде).
- Blaze в клиентском процессе — **пока нет, только скрипт**.

## Условия avatar-ik к xr-input (5292)

1. Один источник на процесс (реальная XR или скриптовая, никогда обе); выбор при старте участника; смена — выключить
   одну, включить другую. 2. Голова — как E2EHeadPose: `CameraTrackingEnabled=false`, local pose камеры один раз за
   кадр в `StageUpdating(Update)`, на выходе `=true`. 3. Контроллеры — через слот устройства UXR (своя реализация
   UxrTrackingDevice/UxrControllerInput вместо UxrUnityXR*), запястья пишет штатный UxrControllerTracking (писатель
   кисти №1), сглаживание 0, позы в пространстве трекинга, HeightOffset пола — тем же путём, что у человека.
   4. Кнопки/оси — только через API этой реализации UxrControllerInput; по умолчанию отпущено/ноль. 5. Не писать кости,
   пивот, Dummy Forward, масштаб, BodyIK, PlayerCalibration; высота головы скрипта = рост игрока (калибровка меряет
   camera.y − root.y) — задавать осознанно или через сессию. 6. Ноги: шаг по скорости камеры — реалистичная скорость
   (≤ ~2 м/с), без скачков > teleportDistance × scale за кадр; после pose-set высота головы управляет приседом.
   7. TryRead без аллокаций. 8. В релизе не активируется — define и рантайм-проверка + тест. 9. Diff — avatar-ik до
   merge-request; уровень реализации решает launch-infra, для Editor/MPPM ожидается UXR-уровень (UnityEngine.XR
   InputDevices без XR-лоадера не эмулируются).

## Процедуры пилота (вопрос пользователя 2026-10-13)

Слой L3 даёт вызываемые процедуры — макродействия, одинаковые для Blaze, скрипта и обоих исполнителей: reload, draw,
stow, buy-from-wall, take-magazine, fire-burst… Процедура = машина состояний, которая выдаёт кадры устройства (цели
рук-траектории + нажатия кнопок) и ждёт подтверждения SDK между шагами; цели (снап магазина, приёмник, рукоятка
затвора, якоря подсумков) читаются у SDK каждый кадр. Reload путём человека: Button1 сброс → левая к подсумку, grip →
магазин в приёмник, отпустить → рукоятка затвора, оттянуть, отпустить. В Device-режиме нажатия идут через xr-input,
в Puppet — в те же вызовы SDK, что делает UxrStandardAvatarController. Появляются в pilot-split (draw/stow/fire —
оформить процедурами), reload — следующей процедурой с ревью weapon-system (механика магазинов/затвора).
Открытый вопрос пользователю (на этапе процедуры reload): переводить ли серверных ботов с automation на перезарядку
путём человека.
