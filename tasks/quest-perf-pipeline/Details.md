# quest-perf-pipeline — подробности

## Поручение

Пользователь 2026-10-11: сетап, который соберёт игру, через adb запустит её на шлеме, прогонит перф-тесты, закроет
приложение и вытащит логи и перф-результаты; процесс автоматизирован. Изначально предлагалось поручить эксперту
performance; пользователь отдал конвейер launch-infra (сборки и запуск плееров — зона launch-infra).

## Состояние на 2026-10-11 (база `7c42ac5d`)

- **Сборка.** `GameBuilder.RunBatch` (`-buildProfile quest -config test`) и `Tools/release/Build-Game.ps1 -Targets quest
  -Config Test`; APK `Build/Quest/VrBattlegrounds_Quest.apk`. Batch требует закрытого редактора (lockfile). Test =
  Development, IL2CPP. XR-лоадер ставит `XrBuildSettingsScope` на время сборки. Отдельного perf-профиля нет.
- **Пакет** `com.UnityTechnologies.com.unity.template.urpblank` (шаблонный), activity
  `com.unity3d.player.UnityPlayerGameActivity`.
- **Перф-тест** (зона performance): запуск только руками — режим отладки → планшет → «Перф-тесты» → «Старт»;
  `StressTestLauncher.TryStart` требует подключения к серверу, аватара и разминки; шлем в билде — клиент. Результаты:
  `persistentDataPath/perf/<ts>_<роль>_<N>/perf.log` + `summary.json` (`completed`, `endReason`, `phases`) и
  `perf/history.log`; на шлеме `/sdcard/Android/data/<pkg>/files/perf/`. Приложение после теста не закрывается.
- **adb-скриптов нет**; adb не в PATH, есть в Unity Android SDK
  (`C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe`).
- **Параметры плеера** только через `Environment.GetCommandLineArgs`; intent extras в коде не разбираются.

### Проверено на шлеме (Quest 3, Android 14, serial 2G0YC1ZFB203VH), 2026-10-11

Установленная сборка от 2026-09-29:
`am start -S -W -n <pkg>/com.unity3d.player.UnityPlayerGameActivity -e unity '-e2eScenario qpp-arg-probe -e2eRole client
-e2eResult /sdcard/Android/data/<pkg>/files/qpp-probe.json -e2eTimeout 30'` — харнесс на шлеме разобрал аргументы
(«Старт харнесса: сценарий=qpp-arg-probe, роль=client…»), записал вердикт `error` «неизвестный сценарий» в файл,
`adb pull` забрал его, `am force-stop` закрыл приложение. Перед стартом `input keyevent KEYCODE_WAKEUP` и
`am broadcast -a com.oculus.vrpowermanager.prox_close` (шлем спал), после — `...automation_disable`.
Вывод: `-e unity "<args>"` доходит до `GetCommandLineArgs` на GameActivity; собственный разбор intent не нужен.
В Git Bash для путей `/sdcard/...` нужен `MSYS_NO_PATHCONV=1`.

## Проект

Перф-прогон на шлеме — это e2e-прогон на устройстве: дирижёр на ПК и сценарий внутри плеера с JSON-вердиктом.
Модель и харнесс уже есть (`Assets/Scripts/Debug/E2E/`, `Tools/e2e/Run-E2E.ps1`); перф-тест — один из сценариев.

```
Run-QuestE2E.ps1 ─ adb ─► [шлем] am start -e unity "-e2eScenario <s> -e2eResult <sdcard> -e2eQuitOnFinish ..."
     │                         └─ E2ERunner → сценарий (perf-stress: StressTestLauncher → Finished) → JSON → Quit
     ├─ ждёт статус JSON (adb shell cat), таймаут
     ├─ force-stop, logcat (pid), pull: результат + новые каталоги perf/
     └─ results/<ts>-quest-<s>/summary.json, код 0/1/2
```

Владельцы: дирижёр, харнесс (`-e2eQuitOnFinish`, путь результата на устройстве), сборка, adb — launch-infra.
Сценарий `perf-stress` (что запускать, какие фазы, критерии) и формат/место перф-отчёта — performance; связь —
контракт: сценарий записывает в E2E-результат путь каталога своего отчёта, дирижёр забирает его целиком.

### Этапы

1. `design` — этот документ.
2. `device-runner` — `Tools/e2e/Run-QuestE2E.ps1`: поиск adb, выбор устройства, опциональная установка APK,
   wake/prox, запуск сценария с аргументами, ожидание вердикта, force-stop, logcat, pull результата и новых `perf/`,
   восстановление prox в finally, `summary.json`, коды 0/1/2. Харнесс: `-e2eQuitOnFinish` для клиента на устройстве.
   Проверка — на шлеме существующим сценарием и неизвестным сценарием (негатив).
3. `quest-build` — сборка APK из конвейера: при закрытом редакторе `Build-Game.ps1 -Targets quest -Config Test`, при
   открытом (worker) — `GameBuilder.Build` через `delayCall` и статус-файл (без MCP-таймаута, см. vr-test-stand §15);
   штамп свежести по содержимому.
4. `perf-scenario` — с performance: сценарий `perf-stress` без рук (подключение/роль, разминка, `TryStart`, ожидание
   Finished, путь отчёта в результат). Топология — решение пользователя/performance (см. ниже).

### Контракт `quest-device-run` и ревью performance (2026-10-11)

Спецификация: [contracts/quest-device-run.json](contracts/quest-device-run.json). rev1 → ревью performance (inbox 3897):
каркас подходит, ACK на rev2 с манифестом условий прогона `run.json` (инвариант performance: число без условий — не
факт). rev1 отклонена как заменённая, rev2 опубликована (3901).

- **Топология — решение пользователя 2026-10-11 (rev3):** шлем — настоящий клиент выделенного сервера на ПК по LAN,
  чтобы проверялся prod flow. Сервер — Windows-плеер той же ревизии (`-batchmode -nographics`), одна изолированная пара
  портов (механизм `e2e-run-isolation`, отличная от worker и основного checkout); клиент находит сервер через Discovery
  по LAN на этой паре, `-e2eServerAddress <IP ПК>` — запасной путь. Host на шлеме — только явным аргументом.
  Performance предлагал host (сравнимость с замерами 2026-09-28/29) — rev2 заменена rev3; во время прогона редактор и
  MCP не трогать. Штамп ревизии сборки вшивает `quest-build`, харнесс отдаёт его в результате, дирижёр сверяет APK и сервер.
- **Внешние метрики** не нужны: logcat процесса + `perf.log`/`summary.json`/`history.log` достаточно; OVR Metrics
  меняет состояние шлема, `dumpsys gfxinfo` не видит VR-композитор. Снаружи — только `dumpsys battery` до/после.
- **perf-stress** (зона performance, этап `perf-scenario`): `-e2eArg mode=normal|short|map-only|per-skin`, `puppets`,
  `clutter`, `phase_s`, `warmup_s`, `skin`; умолчания = `StressTestConfig`; таймаут сценарий считает сам из плана;
  `passed` = дошёл до Finished со всеми фазами и файлы записаны, бюджетных порогов нет. Артефакты: `perf/<ts>/`, `perf/history.log`.

### Порядок реализации `device-runner` (2026-10-11)

Контракт rev3 согласован (ACK performance 3942, своя задача). Пользователь: host допустим, если клиентская инфра
требует доработок. Находка: собранный плеер на шлеме стартует клиентом (роль выбирает `GameNetworkDiscovery` по
`IsHeadless`, зона network); стать host без рук можно только через шов выбора роли в плеере — отдельное согласование с
network. Клиентская топология шва не требует, поэтому первой реализуется client (prod flow); `-Topology host` в
дирижёре отвечает кодом 2 до шва. Самопроверка каркаса — сценарий `device-smoke` (server ждёт клиента, client ждёт
подключения, по `-e2eArg artifact=1` объявляет файл-артефакт).

Живая проверка требует новой сборки APK и Windows-плеера одной ревизии (новые флаги харнесса): сборка на worker под
арендой через `delayCall` + статус-файл, установка APK дирижёром (`-Apk`), прогон client по LAN. Пожелание
performance (3942): Wi-Fi шлема и CPU ПК в run.json — включено сразу.

### Реализация `device-runner` (2026-10-11, до компиляции и новой сборки)

Дирижёр `Tools/e2e/Run-QuestE2E.ps1`; харнесс: `E2EContext` (`-e2eArg` → `Arg()`/`ArgError`, `-e2eQuitOnFinish`,
относительный `-e2eResult` от `persistentDataPath`), `E2EResult` (`Artifacts`/`AddArtifact`, условия сборки),
`E2ERunner` (отказ по `ArgError`, Quit любой роли по флагу), сценарий `device-smoke`. Описание — `Docs/testing.md`,
«Прогон на шлеме».

Проверено без Unity: парсер PowerShell, отказы с кодом 2 (`-Topology host`, неверный `-Serial`, битый `-Arg`, нет
плеера сервера), сбор полей `run.json` с шлема (батарея, getprop, versionCode/Name, Wi-Fi), IP шлема 192.168.1.234/24
и ПК 192.168.1.245, брандмауэр Windows на этой машине выключен для всех профилей. Проба на старой сборке (2026-09-29)
с сервером из e2e-плеера основного checkout: цепочка сервер → `am start` → ожидание → force-stop → automation_disable
→ logcat → `run.json` → код 2 прошла; сервер ответил «неизвестный сценарий». **Находка:** при выключенных контроллерах
`am start -W` показывает `com.oculus.vrshell/.systemdialog.launchcheck.LaunchCheckControllerRequiredDialogActivity`,
приложение не стартует — дирижёр распознаёт это и отказывает с подсказкой. Для безрукого прогона контроллеры должны
быть включены (или сборка — объявлять поддержку рук; решение вне зоны launch-infra).

Ждёт: компиляция, новая сборка APK и Windows-плеера одной ревизии, живой прогон `device-smoke -Arg artifact=1`.

### Открытые решения
- **Пакет** шаблонный; смена = новое приложение на устройствах — отдельное решение, не в этой задаче.
- **Датчик приближения** удерживается broadcast'ом `prox_close`; восстановление обязательно в finally.

### Живая проверка `device-runner` (2026-10-12)

Сборки на worker: аренда 442 (вход `2534bd09`; простой ~1 ч из-за модального «Unsupported Input Handling on Android»,
закрыт человеком) и аренда 449 (вход `3d3dd1c9`, исправленный `device-smoke`): компиляция без ошибок, AndroidCompileGate
PASS, `E2EPlayerBuilder` PASS, `GameBuilder` Quest/Test PASS (APK 679 МБ). Побочные `MapRuntimeCatalog.asset`,
`PC.asset` — finish `unexpected`, не доставлены.

Прогоны `Run-QuestE2E.ps1 -Scenario device-smoke -Arg artifact=1` (Quest 3, LAN 192.168.1.0/24, сервер на ПК):
1. `20261012-001556` — INCONCLUSIVE: шлем в Quest Link, `am start` завис. → дирижёр распознаёт Link (отказ, `-StopLink`).
2. `20261012-002144` — RED, настоящий: клиент нашёл именно наш сервер (`kcp://192.168.1.245:55564`, Discovery 55565 —
   изоляция работает), но засчитал подключение по транспорту за ~80 мс и вышел по `-e2eQuitOnFinish` до сессии на сервере.
   → клиент ждёт свою `PlayerSession.LocalSession` и держит 2 с; прямой фоллбэк — через `RequestClientConnect`, не `StartClient`.
   Link закрыт и поднят обратно, но связь с ПК потом оборвалась (диалог «Link отключён»).
3. `20261012-004437` — INCONCLUSIVE: на шлеме диалог «Link отключён». → дирижёр до прогона отказывает при любом
   системном диалоге в фокусе; при сбое `am start` называет диалог. Возврат Link убран по решению пользователя.
4. **`20261012-011211` — GREEN** (вход `3d3dd1c9` + дирижёр в worktree): сервер 3/3 (подключение с сессией за 20 с),
   клиент 2/2 (Discovery, своя сессия за 2,65 с), артефакты обеих сторон забраны, `run.json` полный (Quest 3, батарея
   17 % / 46 °C, USB, Wi-Fi 5180 МГц −31 дБм, Ryzen 9 5900X, порты 61119/61120, сборки Development 0.1.0 / 6000.4.1f1).

Не проверено: фоллбэк прямого подключения вживую (Discovery сработал), брандмауэр включён (на машине выключен),
повтор после rebase, `-Topology host` (шов роли — зона network).

### Реализация `quest-build` (2026-10-12, до компиляции на worker)

- **Диалог «Unsupported Input Handling on Android».** По IL Unity 6000.4.1f1: `CheckPrerequisites.ValidateInputHandling`
  (UnityEditor.Android.Extensions) вызывает `EditorUtility.DisplayDialog(…, ok "Ignore", cancel "Cancel Build",
  ForThisSession, "AndroidInputHandlingWarning")` и при `false` отменяет сборку; в batch диалога нет. `DisplayDialog`
  → `EditorDialog.DisplayDecisionDialogWithOptOut`: opt-out читается из `EditorPrefs`/`SessionState.GetInt("DialogOptOut."
  + ключ)` как `DialogResult`, `true` только при `DefaultAction` (= ok, «Ignore»). `EditorUtility.SetDialogOptOutDecision`
  пишет `SessionState.SetBool(ключ)` без префикса — этот диалог его не видит. Поэтому `GameBuilder` пишет
  `SessionState.SetInt("DialogOptOut.AndroidInputHandlingWarning", (int)DialogResult.DefaultAction)` перед Android-сборкой.
- **Штамп ревизии** — `Release/BuildRevisionStamp.cs`: `git rev-parse --short=12 HEAD` (+`-dirty` по `git status
  --porcelain -uno -- Assets Packages ProjectSettings`, `unknown` без git), `bundleVersion = <база>+<ревизия>` на время
  сборки, `Dispose` — возврат и `AssetDatabase.SaveAssets()`. `GameBuilder.Build(…, Test[, revision])` и e2e-плеер; прод — нет.
- **Пара** — `E2EPlayerBuilder.StartPairBuild(statusPath)` (delayCall, статус-JSON) и `RunPairBatch` (`-pairStatus`).
- **Дирижёр** — сверка ревизий (versionName пакета / `appVersion` сервера, код 2), `-Build` (batch Unity, отказ при
  занятом `Temp/UnityLockfile`), `-DryRunBuild`, блоки `build` и `builds.revision*` в `run.json`.
- Проверено без Unity: парсер PowerShell, `-Build -DryRunBuild` (редактор закрыт → команда Unity; занятый lockfile в
  копии скрипта → отказ 2; конфликты `-Apk`/`-ServerPlayer`/`-Unity`), сверка ревизий на синтетических JSON (8 случаев),
  компиляция изменённых editor-файлов против DLL Unity 6000.4.1f1 со стабами зависимостей.

### Живая проверка `quest-build` (2026-10-12)

Worker, аренда 461, вход `d1e3da3b`: компиляция и AndroidCompileGate PASS. Перед сборкой стёрт сохранённый в сессии
ответ человека `DialogOptOut.AndroidInputHandlingWarning` (было 0 — подтверждает формат ключа из IL), затем
`StartPairBuild`: пара собрана за 452 с **без модального окна**, ревизия `d1e3da3ba8fc` на обеих сборках, в отчёте
Quest-сборки — предупреждение «Active Input Handling = Both». После сборки `bundleVersion` = `0.1.0`,
`ProjectSettings.asset` не изменён (worker: только прежние `MapRuntimeCatalog.asset`, `PC.asset` — unexpected, не доставлены).

Шлем: `Run-QuestE2E.ps1 -Scenario device-smoke -Apk ... -Arg artifact=1 -StopLink` — GREEN, `20261012-021845-quest-device-smoke`;
versionName APK `0.1.0+d1e3da3ba8fc`, run.json `builds.check` «ревизии совпадают: d1e3da3ba8fc».

`-Build` (batch) вживую не запускался: в агентском worktree LFS-роль `agent` → дирижёр отказывает (добавлено после
проверки: ассеты-указатели дали бы битую сборку); `-DryRunBuild` и отказ проверены. Не проверено: `-Build` в
основном checkout, `RunPairBatch` в batch-режиме.

### Запрос lobby-decoration 4449 — замер GPU лобби (2026-10-12)

Level-design по поручению пользователя: Lobby + 9 удалённых аватаров, ovr_gpu/ovr_cpu по точкам обзора, без рук.
Разбор (ответ 4451): perf-stress, расстановка «внутри арены», метрики по точкам — performance (переслано 4453);
механизм обхода точек — launch-infra, этап `e2e-views`: хелпер `E2EViews` ставит взгляд официальным
`UxrManager.TeleportLocalAvatar` (пол+yaw, не второй писатель), пишет фактическую позу камеры; высота глаз = трекинг
лежащего шлема (точная 1.6 м — только тестовый XR-адаптер, backlog xr-input). Ревью avatar-ik запрошено (4455).
Сборка ветки lobby-decoration — штатно через worker (`StartPairBuild`); для человека в основном checkout `-Build`
получит проверку LFS-указателей (`git lfs ls-files` + сигнатура pointer) в этапе `e2e-views`.

Ревью avatar-ik 4459: согласен, второго писателя нет. API — `UxrManager.Instance.MoveAvatarTo(UxrAvatar.LocalAvatar,
floorPos, forward)` (синхронный, путь игрового `PlayerController.RpcDevTeleport`; TeleportLocalAvatar допустим, но
прерывает телепорт-корутины); вызывать из своей корутины, не из AvatarUpdated/StageUpdated; ждать 3 кадра (ноги).
Задаётся только проекция камеры на пол и yaw; тангаж/крен/высота — от лежащего шлема; в результат — фактическая поза
камеры и тангаж. Пол/калибровку/пивот не трогать. Для повторяемости — шлем на подставке на высоте глаз ровно.
Будущий xr-input: один источник в слоте ввода до стадии Manipulation, без записи в кости/пивот/калибровку.

### Живая проверка `e2e-views` (2026-10-12)

Worker, аренда 472, вход `d2e9c075`: компиляция и AndroidCompileGate PASS, `E2EViews` загружен; `StartPairBuild` PASS
(728 с, ревизия `d2e9c075b741`, без модального окна — opt-out в сессии уже 0). Побочные `MapRuntimeCatalog.asset`,
`PC.asset` — unexpected, не доставлены.

Шлем (Quest 3, без Link): `Run-QuestE2E.ps1 -Scenario device-smoke -Apk … -Arg artifact=1 -ViewsFile
tasks/quest-perf-pipeline/tools/lobby-views.json` — **GREEN** `20261012-033522-quest-device-smoke`: сервер 4/4
(файл точек прочитан, 8 имён по порядку), клиент 3/3, «обход точек обзора» 8/8 — отклонение XZ ≤ 0,0001 м, курс ≤ 0,01°;
ревизии совпадают. Фактическая камера: высота глаз 0,80 м, тангаж +32,2°, крен −2,4° на всех точках — шлем лежит с
наклоном вниз; для перф-замера лобби нерепрезентативно → шлем на подставку ровно на высоте глаз (совет avatar-ik).
`tools/lobby-views.json` — точки из запроса level-design 4449 (пол x/z + yaw, высоту не задаём).

Не проверено: `-Build` с LFS-проверкой в основном checkout (там 0 указателей из 2206, проверено только чтением).

### Контракт r4 — вердикт по логам (2026-10-12)

`quest-device-run` r4 (providing_stage `e2e-log-verdict`): ACK performance 4863, своя задача; agreed. Условие перф-зоны:
`E2ELogCapture` без аллокаций для Log/Warning во время замера — обработчик отбрасывает их по LogType первой проверкой.
Дизайн: захват в процессе (`logMessageReceivedThreaded`, ставится при создании E2EResult — E2ERunner.cs не трогаем:
там идёт perf-stress-scenario/scenario), шум каркаса — `Tools/e2e/known-noise.json`, шум сценария —
`result.DeclareKnownNoise(id, regex, reason)`. Этап на паузе по решению пользователя: сначала симуляция позы головы.

### Симуляция позы головы — исследование avatar-ik (2026-10-12)

Пользователь: перф-тесты не должны требовать, чтобы человек держал шлем на нужной высоте. Исследование (консультант
avatar-ik, read-only): HMD → камера через legacy `TrackedPoseDriver`, который `UxrAvatar.Start` добавляет в рантайме
(`UxrAvatar.cs:1394-1423`; пишет local pose в Update -30000 и в onBeforeRender); голову UXR-трекинг не ведёт; симулятора
HMD в UXR нет. Вариант A (принят): `avatar.CameraTrackingEnabled = false` (штатный API, `UxrAvatar.cs:482-515`) только у
`UxrAvatar.LocalAvatar` в e2e, один писатель local pose камеры в `UxrManager.StageUpdating(UxrUpdateStage.Update)`
(прецедент `BotAvatarDriver.cs:95-122`); не писать пивот/Dummy Forward/BodyIK/кости/калибровку; высота критерием
`camera.y − root.y` = цель ±1 см (цель — `Applied.EyeHeight`, иначе 1.6; local Y = цель − FloorOffset); тангаж/крен 0,
курс — E2EViews; повтор на LocalAvatarChanged/после AvatarMode (Mirror-спавн включает TPD снова); восстановление при
выключении. B (писать поверх TPD) и C (trackingType) отклонены — два писателя/обход. Рендер: LateLatching 0, SpaceWarp 0
(контракт: должны остаться 0); GPU рендерит симулированную позу, ATW лежащего шлема ≈ 0. Сеть: NetworkTransform камеры
Local, ClientToServer — симуляция реплицируется. Руки — v1 нет (статус контроллеров в отчёт), v2 отдельно.
Побочное: `OculusSettings TargetQuest3: 0` — передать performance.

Подтверждение avatar-ik 4877 (вариант A, B/C отклонены), условия:
1. Только `UxrAvatar.LocalAvatar` и только при `-e2eScenario`; один писатель local pose камеры в `StageUpdating(Update)` —
   до UpdateAvatar локального контроллера и AvatarUsingTracking; позже в кадре камеру не трогать.
2. Local Y — через `pivot.InverseTransformPoint(root.position + up × цель)` (база префаба у пивота может быть ненулевой);
   приёмка мировая: `camera.y − root.y` = цель ±1 см.
3. Замер роста калибровки читает `camera.y − root.y` (`PhysicalSpaceSyncManager.cs:281`) и измерит цель; цель =
   `Applied.EyeHeight` или 1.6; не запускать замер калибровки в сценарии с другой целью; не писать
   PlayerCalibration/пивот/Dummy Forward/BodyIK/кости.
4. Повтор на `LocalAvatarChanged` и после смены `AvatarMode`; при выключении — `CameraTrackingEnabled = true`, без
   оставленных подписок.
5. На шлеме подтвердить фактом, что рендер следует позе камеры при выключенном TPD (лог позы и снимок кадра).
6. Руки: лежащие контроллеры тянут IK рук — могут попасть в кадр и влиять на GPU; фиксировать в отчёте.
Дифф — avatar-ik до вливания.

Решение пользователя (через performance, 4897, 2026-10-12): хоста на Quest не будет — host-топология для прогонов на
шлеме не нужна. Шов выбора роли в плеере (зона network) снят с плана; `-Topology host` в дирижёре — код 2,
параметр убрать при следующей правке дирижёра.

Аренда 499 (вход `21e7bf1f`): компиляция и AndroidCompileGate PASS, `E2EHeadPose` загружен. `StartPairBuild` не стартовал —
в worker-редакторе не срабатывает `EditorApplication.delayCall` (проба: delayCall не выполнен, одноразовый
`update` — да); пара собрана вручную через `update` (617 с, ревизия `21e7bf1f3017`), сборки скопированы в `Build\`.
`StartPairBuild` переведён на одноразовый `EditorApplication.update` (+ ошибка сборки пишется в статус). Сопровождающему —
infra_issue. Прогон на шлеме отложен: села батарея шлема (пользователь, 2026-10-12).

### БЛОКЕР: Quest-сборки плоские (2026-10-12)

Прогон `20261012-180954` (вход `21e7bf1f`, `-HeadPose 1.6`): пользователь видит на шлеме чёрное 2D-окно, звук лобби.
`dumpsys package`: только `android.intent.category.LAUNCHER`, нет `com.oculus.intent.category.VR`; aapt по APK — 0
Oculus-ключей. Сборки GameBuilder Quest/Test (пары StartPairBuild) — плоские; прогоны device-smoke шли в 2D (сессия до
VISIBLE, не FOCUSED). Симуляция головы при этом: высота 1.6000 м, тангаж/крен 0.00°, 128/300 кадров — прервано
«локальный аватар сменился» (вероятно, следствие 2D). Перф-цифры с этих сборок недействительны (сообщено performance и
lobby-decoration). Разбор причины + пост-проверка VR-категории в GameBuilder.

### Реализация `quest-vr-manifest` (2026-10-12, до компиляции на worker)

Причина: `XrBuildSettingsScope.Apply` менял лоадеры Android только в памяти, объект оставался чистым; посреди
`BuildPlayer` Unity его выгружала и перечитывала с диска (`m_Loaders: []`), и `OculusManifest` (callbackOrder 10000)
выходил без правки манифеста. Исправление: на время сборки `DontUnloadUnusedAsset` + `SetDirty` для
`XRGeneralSettingsPerBuildTarget`, `XRGeneralSettings` группы и его менеджера; возврат по группе, флаги назад,
`ClearDirty` (файл не пересериализуется). Переписан ли файл во время сборки — сверка SHA1, тогда предупреждение и
грязность остаётся. Новый `QuestManifestValidator` (10001, взводит GameBuilder): Quest без VR-категории или
`supportedDevices` — `BuildFailedException` с состоянием XR сейчас/при выставлении; планшет с VR-категорией — ошибка;
без Quest 3 (`eureka`/`quest3`) — предупреждение. Плюс aapt по готовому APK. E2E-плеер — в скоупе профиля Server.
Локально: csc против ScriptAssemblies основного checkout — 0 ошибок; правила на манифестах: Gradle worker 10-12 FAIL,
Gradle main 09-29 PASS (+ предупреждение quest2), aapt `Build/Quest` APK FAIL. Обвязка и логи: reports/ не создавались,
проба — во временной папке агента.

#### Настоящая причина плоских сборок (аренда 516, 2026-10-12)

Валидатор остановил плоскую сборку до Gradle (страховка работает). Диагностика: в момент проверки XRGeneralSettings(Android)
тот же объект (id 68838), OculusLoader на месте → гипотеза «перечитывание с диска» ОПРОВЕРГНУТА. В логе среди
IPostGenerateGradleAndroidProject вызваны только `Unity.XR.Management…AndroidManifestBuildEventReceiver` и наш валидатор —
`OculusManifest` (OculusBuildProcessor.cs:413 `#if UNITY_ANDROID`) в editor-сборке отсутствует: редактор worker
скомпилирован под Standalone, GameBuilder переключает платформу и сразу зовёт BuildPlayer без перекомпиляции editor-скриптов.
29.09 основной checkout стоял на Android — работало. `-Targets all` в Build-Game.ps1 (первый профиль server → -buildTarget
Win64) даёт тот же дефект. SetDirty/DontUnload в XrBuildSettingsScope лечили не то и привели к записи XR-ассета на диск
посреди сборки (к finish вернулся) — откатываются. Исправление: проверка наличия типа OculusManifest перед сборкой Quest,
StartPairBuild переключает на Android и продолжает после перекомпиляции (Quest первым), Build-Game.ps1 — отдельный запуск
Unity на группу платформ. Evidence: tasks/quest-perf-pipeline/reports/vr-manifest-516-validator.log (вне Git).

Доработка (субагент, 2026-10-12, без Unity): XrBuildSettingsScope = origin/dev + read-only `Describe` (InitOnStart,
лоадеры) для текста валидатора. `QuestManifestValidator.OculusManifestCompiled()` — тип `UnityEditor.XR.Oculus.OculusManifest`
(не `Unity.XR.Oculus.Editor.*`; сборка Unity.XR.Oculus.Editor) в домене; в Library основного checkout Android-dag
(1300b0a) содержит его, Standalone-dag (1900b0a) — нет. GameBuilder.Build проверяет до всех сборок (тип в вызове не
меняется): Quest — Fail, только Tablet — предупреждение (Mirror AndroidManifestHelper под тем же `#if`, multicast для
LAN-discovery). StartPairBuild: задание в SessionState, `SwitchActiveBuildTargetAsync`, `[InitializeOnLoadMethod]`
продолжает (до 3 перезагрузок), ошибки компиляции ловит `assemblyCompilationFinished`; файл статуса пустой всё время.
BuildPair: Quest → e2e-плеер; e2e-плеер явно переключает на StandaloneWindows64 до XR-скоупа (реимпорт XR при
переключении), после пары редактор на Standalone. RunPairBatch без Android-домена — код 1: Run-QuestE2E.ps1
(этап e2e-head-pose) должен добавить `-buildTarget Android`. Build-Game.ps1: Android-группа, затем server, лог на запуск.
Проверки: csc Assembly-CSharp-Editor (rsp 1900b0aEDbg, исходники worktree) — 0 ошибок/0 предупреждений в изменённых;
ParseFile Build-Game.ps1 — 0; Get-BuildRuns на 9 наборах + 3 ошибочных; коды 2 без Unity.

### Каркас для смок-теста серии `e2e-smoke-kit` — проект (2026-10-13)

Решение пользователя: сценарий серии матчей и его критерии пишет gameplay (5863 → 5869), launch-infra готовит каркас.
Аудит (только чтение): раннер переживает смену карты (DontDestroyOnLoad); барьера нет — сценарии синхронизируются
через игровые `PlayerSession.Score`/`HasGrabbedDogTag`, которые игра сбрасывает; нет индекса клиента, признака
позднего клиента, журнала шагов и `-Arg` в `Run-E2E.ps1`; `E2EWait` не ловит исключение условия и не проверяет общий
дедлайн внутри ожидания; в редакторе разрешён один сценарий.

Проект (5875), принят gameplay с уточнениями (5880); сетевая часть — на ревью network (5877):
- Барьер на своих сообщениях Mirror только для e2e (`#if !VRBG_NO_E2E`, только при `-e2eScenario`): клиент → сервер
  «шаг достигнут» (роль, имя, data), сервер → клиенты «шаг сервера» (имя, data) и `abort` (причина). Регистрация —
  каркасом после старта сети, повторно после каждого рестарта; игровой код и GameNetworkManager не меняются.
- `ctx.Step(name, data = null)` — журнал + отправка серверу; имена динамические; повтор идемпотентен: сервер хранит
  первое достижение и последнее data по роли; клиент после переподключения пересылает свои достигнутые шаги.
- `E2EBarrier.WaitClients(ctx, step, timeout, includeLate = false)` → словарь роль → data; таймаут — именованный
  провал «barrier <шаг>: дошли …; нет …». Поздний клиент — отдельным барьером (например `late-joined`).
- `ctx.ServerStep(name, data)` + `E2EBarrier.WaitServer(ctx, step, timeout)`; провал сервера рассылает `abort`,
  клиенты завершаются именованным провалом сразу; провал клиента — шаг `failed`, виден серверу.
- `E2EContext.ClientIndex` (сервер 0), `IsLateClient`, `ClientCount`; `Run-E2E.ps1` передаёт `-e2eClients`, `-e2eLate`.
- Журнал `steps [{name, role, data, utc, outcome}]` (UTC) в результате процесса, включая шаги сервера; `Run-E2E.ps1`
  сводит общую ленту по времени в `summary.json`.
- `Run-E2E.ps1 -Arg ключ=значение` → `-e2eArg` всем процессам; чтение — `ctx.Arg(key, default)`.
- `E2EWait`: исключение условия — именованный провал, общий дедлайн проверяется внутри ожидания.
- `PlayModeStandParticipant`: разрешить все зарегистрированные сценарии.

Условия network (5886, приняты 5890; 1–4 обязательные): (1) рукопожатие `e2e-ready` от текущего запуска сервера —
до него шаги клиента копятся локально (иначе неизвестный id сообщения разрывает соединение); (2) типы и обработчики под
одним `#if !VRBG_NO_E2E` на всех ролях + рантайм-страж; сейчас Prod-сборка `VRBG_NO_E2E` не задаёт — GameBuilder при
Prod добавляет его всем профилям; (3) Reliable, имя/роль ≤64, data ≤256, ≤512 шагов на соединение, лишнее —
именованная ошибка; (4) роль из ожидаемого набора, одна роль — одно живое соединение, после штатной аутентификации;
(5) обработчики только пишут журнал; (6) таймауты вокруг смены карты с запасом; (7) host — отдельная роль.
Diff — network до checkpoint, gameplay — ревью API.

### Выделенные сборочные worktree `build-checkouts` (2026-10-14)

Решение пользователя (2026-10-14, AskUserQuestion): выделить **4 отдельных сборочных worktree** (по одному на пару
`<Config, Platform>`) с полным LFS (`role = build`, `hydration = full`), чтобы исключить переключение платформы Unity,
сброс кешей `Library`/Bee/IL2CPP и простои очереди интерактивного `unity-editor-worker`:
- `F:/CodexWorktrees/build-test-win64/Vr_Battlegrounds_ai` (`Server` Test + `e2e` Windows сервер)
- `F:/CodexWorktrees/build-test-android/Vr_Battlegrounds_ai` (`Quest` Test + `Tablet` Test)
- `F:/CodexWorktrees/build-prod-win64/Vr_Battlegrounds_ai` (`Server` Prod)
- `F:/CodexWorktrees/build-prod-android/Vr_Battlegrounds_ai` (`Quest` Prod + `Tablet` Prod)

Реализовано:
1. В `Tools/agents/lfs-worktree-policy.json` и `Tools/agents/configure_lfs_worktree.py` добавлена роль `build`
   (`buildHydration = full`, `buildWorktrees` на 4 пути) с автоматическим `git lfs pull` и защитой от pointer-файлов;
   покрыто тестом в `Tools/agents/tests/test_lfs_worktree_policy.py` (3/3 OK).
2. В `Assets/Editor/VR_Battlegrounds/Debug/E2EPlayerBuilder.cs`:
   - `RunQuestBatch()` собирает только `BuildProfile.Quest` (`BuildConfig.Test`) на Android без переключения на Win64;
   - `RunBatch()` и `RunQuestBatch()` принимают `-buildRevision <ревизия>` и `-serverStatus` / `-questStatus`.
3. В `Assets/Editor/VR_Battlegrounds/Release/GameBuilder.cs`:
   - `Result.CheckoutPath` включён в `Summary` сборки; отдельный `last-build.json` этот код не пишет;
   - `RunBatch()` поддерживает `-buildRevision <ревизия>` и `-compareBuildRoot <путь>` для кросс-worktree сверки
     `uxr-ids.txt`;
   - добавлено предупреждение при запуске `Prod`-сборки в `unity-editor-worker`.
4. В `Tools/release/Build-Game.ps1` и `Tools/e2e/Run-QuestE2E.ps1`:
   - автоматическая маршрутизация групп `StandaloneWindows64` и `Android` в соответствующую пару
     `build-<config>-win64` и `build-<config>-android`;
   - проверки перед сборкой: отсутствие `Temp\UnityLockfile`, запрет `vrb.lfs.role=agent`, 0 LFS pointer-файлов,
     совпадение `HEAD` SHA и чистоты между парными worktree, единый `-buildRevision`;
   - `Build-Game.ps1` поддерживает `-Ref <git-ref>` (перевод обоих сборочных worktree на один коммит + LFS hydrate)
     и `-DryRun`; `Run-QuestE2E.ps1` поддерживает `-DryRunBuild`, требует уже совпадающих SHA пары,
     параметра `-Ref` у него нет.
5. Документация обновлена в `Docs/release.md` и `Docs/agents/lfs-worktrees.md`.

