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

### Открытые решения

- **Топология перф-прогона.** (а) Шлем host — без ПК-сервера, проще и стабильнее, но шлем несёт серверную нагрузку,
  которой в игре нет. (б) Выделенный сервер на ПК (`Run-E2E`-плеер, `-batchmode -nographics`) + шлем-клиент по
  Wi-Fi — как в игре; нужен LAN (KCP по UDP, `adb reverse` UDP не умеет), адрес ПК и изоляция портов (есть).
- **Пакет** шаблонный; смена = новое приложение на устройствах — отдельное решение, не в этой задаче.
- **Датчик приближения** удерживается broadcast'ом `prox_close`; восстановление обязательно в finally.
