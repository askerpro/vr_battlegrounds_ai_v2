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
