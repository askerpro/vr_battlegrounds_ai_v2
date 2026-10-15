# Адресный timeout MCP и отдельная CLI-проверка

Источник инфраструктуры: `F:/UnityProjects/agent-infra`, окончательный source
`6faf77acb9fdea4fecb003461575366bb2511b61`. Он включает адресный timeout `f344798`,
раннюю проверку выходных путей `29bdf388` и серверный WebSocket deadline.
Runtime и immutable MCP wheel выпущены собственной FIFO-заявкой729.

## Причина и согласованный контракт

Событие 7911, ticket713: синхронный `execute_code` вернул `TimeoutError` через 30 секунд,
хотя native Windows BuildReport и сохранённый результат подтвердили успешную сборку
за 87,6105608 секунды. Исходная сборка не повторяется. R сохранён как
`3f37b42cf138cae45c2c2b5525d33123850cb651`; производный `PC.asset` не принимается
в source по заключению владельца performance7926.

Для `execute_code/action=execute` добавлен явный `timeout_seconds`: только integer
30–300, default30. Пропуск и явное значение30 сохраняют прежний backend deadline30.
Параметр не распространяется на history/clear_history/replay. После получения HTTP
сессии, непосредственно перед каждой отправкой, повторяется native guard и проверяется
свежая RUNNING-аренда: остаток не меньше запрошенного timeout плюс60 секунд.
Используется существующий timeout envelope Hub; C# bridge не меняется.

У execute/replay отключён скрытый повтор после reload. Неизвестный transport outcome
возвращается с `outcome_unknown` и `inspect_previous_outcome`; любой такой признак
в text/structured представлениях запрещает повтор. Доказанная недоставка сохраняет
прежний безопасный повтор. Истечение ожидания не останавливает уже выполняющийся C#.

## Проверки source

- Исходное воспроизведение RED: 11 тестов, 18 failures, без errors.
- Первый адресный GREEN: 72 теста, без skips; actual MCP SDK/FastMCP.
- Независимое ревью нашло два случая небезопасного retry; оба исправлены.
- Исправления ревью: GREEN62; независимая проверка actual SDK18/18 PASS, ACCEPT.
- Полный source suite на `f344798`: 1058 тестов, один skip, два непрошедших случая. Сбой timing
  hook воспроизводится на baseline; случай service/lease также воспроизведён на baseline.
  Полный набор пока не объявляется GREEN.
- Первый wheel f344798 /724 собран из официального `mcpforunityserver==10.2.0`
  с проверенным SHA256; dependencies не обновлялись. Его SHA256:
  `dc2dbb11604ddfec5a4f5ea6aa256bc1826061f6bca696266860377f0346e5ab`.
- Последующий server fix: 47/47 PASS без skips; независимое ревью ACCEPT и45/45 PASS.
  Actual SansIO/raw WS стенд: baseline20/20 теряет ответ при живом JSON heartbeat;
  timeout315 доставляет единственный ответ после46 секунд. Полный suite на окончательном
  source повторно не запускался; прежние два baseline-дефекта не объявлены закрытыми.

## CLI pilot

Отдельный checkout `F:/CodexWorktrees/unity-cli-validation/Vr_Battlegrounds_ai`, ветка
`codex/unity-cli-validation-20261015`, база `04fc9de8875d390ab2fb1ddb4116536aa288a01d`.
Все2207 LFS-файлов восстановлены. В рамках общей FIFO, ticket716/epoch608, один раз
запущен native Unity CLI для16 существующих EditMode тестов UI/сети. У запуска есть
неизменяемый intent, PID с временем создания, отдельные XML/log/receipt. GUI worker
не завершается, общую очередь и аренду обслуживает сопровождающий.

Это ограниченная ручная проверка, production CLI executor брокера ещё не выпущен.
Offline-прототип контракта22/22 PASS; guards pilot8/8 PASS. Native CLI завершился
с фактическим exit0: полный XML содержит16/16 PASS, без failed/skipped/inconclusive.
Это семь UI и девять сетевых EditMode тестов; PlayMode, build и Player E2E не запускались.

Холодная загрузка заняла1717,79 секунды. После UTF scheduling около09:45:52 UTC
до начала тестов09:50:06 UTC прошло ещё примерно4 минуты14 секунд; точная задача
внутри этого интервала не установлена. Сами тесты заняли1,57 секунды. Первичный
наблюдатель получил PermissionError при публикации receipt; исходные intent/receipt/tmp
сохранены. Отдельный read-only наблюдатель удержал native handle того же PID210948
с проверенным временем создания, получил настоящий exit0 и подтвердил закрытие проекта.

- XML SHA256: `cddc5a29d9914a80ff4c964a87c10bf094036fb3459076a81b0be06467993c28`.
- Editor.log SHA256: `1fc656737f90d294d3b81af0a73b9c1a41e71071e005808c94fcca6ee209ed6a`.
- Внешний R: `22fb3c056fdd1f9a6b863da29d6ca9d85263c2db`, ref
  `refs/heads/codex/tmp/unity-cli-pilot716-result-20261015-a397324e78354d7db1dcc25a4d63796b`.

Единственный tracked diff CLI checkout — производный Packages/packages-lock.json:
UPM заменил embedded/file-запись MCP на git pin из manifest. После сохранения evidence
этот diff восстановлен; закрытый CLI checkout чистый. Два ранних CS0246 ShaderGraph
сохранены в полном логе; последующий результат16 PASS не скрывает эти сообщения.

При finish сопровождающий ошибочно указал output root с `/**`. Unity save_outputs
завершился успешно, затем Python lstat буквального glob дал WinError123. Ticket716
восстановлен штатным recover до DONE, blocked=false/recovery=false: чистая база
позволила пройти без повторного capture с ошибочными roots. Broker result_sha не создан;
внешние R/ref и отчёты сохранены отдельно. Это не потеря native XML и не повтор Unity.

Подробные результаты находятся в ignored
`tasks/agent-coordination-protocol/reports/mcp-timeout-cli-20261015/` и
`reports/unity-cli-pilot-20261015/`, вне Git. В отдельном CLI checkout итог —
`reports/unity-cli-pilot-20261015/runs/6dc01e4d3bf2470a850d338ea42f3f7d/pilot-outcome.json`.

## Выпуск и проверка живого MCP

В собственной FIFO-заявке724 развёрнут runtime f344798 и immutable wheel той же
версии; backup runtime-versions/before-8a46ca307ce441ccb2b08c199038a114.
Новый backend216036, точная клиентская команда прошла initialize/tools-list48;
схема execute_code показывает integer30..300/default30. Upgrade reservation
штатно снят. 724 завершена DONE с Rdc4f4894ee2eacb7db5b258722f959a73d911156
и восемью архивированными артефактами; FIFO передана UI725.

Единственная46-секундная проба выполнилась один раз: native receipt и history
подтвердили46.0151105s, но command_result потерян при disconnect. Защита
outcome_unknown/inspect_previous_outcome сработала, replay не было. Это воспроизведение
обосновало дополнительный Python-only server fix: HTTP `ws_ping_timeout=315`
через существующий FastMCP API. Interval20, JSON heartbeat, command deadlines,
default30, lease budget и no-replay сохранены. Patched main проверяется по hash до импорта.

После тестов и отдельного ревью source6faf77 выпущен новой заявкой729 в конец FIFO;
собственная ожидающая728 отменена, чужие заявки и порядок не менялись. Backup:
`runtime-versions/before-23d959d21d53420b87c6babac5ed782b`. Backend228516,
initialize/tools-list48 PASS, worker выбран явно: `Vr_Battlegrounds_ai@72e96145498eb8a0`.
Новая проба с другим nonce вернула прямой SUCCESS после46.0118654s, без disconnect
и replay. Стандартный run_tests/get_test_job вернул полный succeeded-результат
job37b70caee45d48c68cbd688820798ae5:16/16 PASS,0 failed/skipped; точные test ID
совпали с CLI. Входы различаются: CLI04fc9de, MCP1e945a996; сравнение производительности
и полная игровая приёмка из этого не следуют.

Runtime включает проверку29bdf388: живой CLI отверг output/** до enqueue,
по уникальному ключу создано0 билетов. Контракты старых заявок не переписывались.
729 завершена DONE: R `f161480da9a474cbd8abfbd3fbb4f6abe29dd1dc`, ref
`refs/heads/codex/tmp/result-729-c472d2a9d73e4b38b3964e3ab03d2484`, accepted=true,
unexpected=[],19 broker artifacts, включая CLI XML/log. Это технический результат
сопровождающего. Blocked/recovery=false, очередь перешла к UI730.

Окончательный wheel SHA256:
`03e3edd8aa9360044c4effc17e99fde3d675c0b7a40cf7c931ba5dfdf1991857`;
main SHA256 `f76766ee6889fc7ae9ea21b183284a5f7cdc545491cf579359e59a9368555396`.
Периодический delayCall и потеря состояния при настоящем reload/restart остаются
отдельными границами; этот выпуск не обещает durable job store для любых перезапусков.

Игровая приёмка других владельцев и production CLI executor этим этапом не заявлены.
