# Прогон сценария на шлеме Quest принят и влит (origin/dev 1fc34c84)

Задача `quest-perf-pipeline`, этап `device-runner`: принят пользователем 2026-10-12, merge-request 78dda86f,
публикация базы worker — ticket 455. Контракт `quest-device-run` r3 (ACK performance 3942): универсальный дирижёр
`Tools/e2e/Run-QuestE2E.ps1`, шлем — настоящий клиент выделенного сервера на ПК по LAN (решение пользователя: prod flow).

Проверено: worker 449 (вход 3d3dd1c9) — компиляция, AndroidCompileGate, сборки PASS; Quest 3 device-smoke GREEN
(`Tools/e2e/results/20261012-011211-quest-device-smoke`). Не проверено: прямой фоллбэк вживую, включённый брандмауэр,
host-топология (шов роли — network).

Уроки:
- Quest Link держит фокус — нативная игра не стартует; любой системный диалог (контроллеры, «Link отключён») тоже.
  Проверять до прогона и отказывать с именем; закрывает человек. Возврат Link со шлема не держит связь с ПК —
  пользователь отказался от автовозврата.
- Клиент с `-e2eQuitOnFinish` должен дождаться своей сессии на сервере, иначе сервер его не засчитает.
- Ожидатель сборки продлевал аренду час при модальном окне Unity — продлевать только при росте Editor.log.
- ACK контракта привязан к ревизии плана: после register — повторный contract-ack, иначе renew/begin отказывают.
- Сценарии с прямым `StartClient()` обходят шов портов — только `GameNetworkDiscovery.RequestClientConnect`.

Дальше: `quest-build` (opt-out диалога Input Handling, штамп ревизии APK/сервера, сборка из конвейера);
performance — `perf-scenario`.
