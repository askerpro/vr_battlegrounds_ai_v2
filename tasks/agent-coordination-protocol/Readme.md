# Координация агентов

Обновлено: 2026-10-15. Владелец: coordination-maintenance, Codex.
Checkout: F:/CodexWorktrees/agent-coordination-protocol/Vr_Battlegrounds_ai.

## Цель и границы

Согласовать архитектуру и пересечения задач до реализации, разрешать независимую
работу, последовательно вливать принятые изменения. Человек видит актуальный статус
в Git. Источники брокера, прокси и протокола находятся в F:/UnityProjects/agent-infra.

## Принятое решение

Единственный авторский план — [plan.json](plan.json); хаб хранит подтверждённые
ревизии, контракты, ACK, допуски и очередь. Изменение плана требует регистрации.
Допуск этапа не является Unity-арендой. Inbox — основной канал для всех вендоров;
штатный канал только уведомляет прочитать task-id/event_id. При недоступной доставке
уведомление переносит человек. ACK не заменяет согласование контракта.

Материалы изолированы в tasks/<task-id>. Reports вне Git; долговременные утилиты
в Tools. Общие журналы задачи не редактируют. В Docs попадает принятая архитектура
отдельным этапом с владельцем области.

## Статус и проверки

Реестр UI исправлен этапом ui-zone-task-registration: коммит d449f1983 опубликован
в origin/dev, задача debug-menu-refactor определяется как задача владельца UI.
Зоны owns, ACK и игровые исходники этим выпуском не менялись.

Текущий этап mcp-execute-timeout-cli-pilot: пользователь согласовал адресный
timeout_seconds 30–300 для execute_code/action=execute при прежнем default 30,
свежую проверку RUNNING-аренды с запасом 60 секунд перед отправкой и отсутствие
автоматического повтора при неизвестном исходе. Сборка ticket713 после ответа
TimeoutError завершилась успешно за 87,61 секунды; повтор не выполняется.
Побочный PC.asset из её R не принимается по заключению владельца performance7926.

Для отдельной CLI-проверки создан checkout
F:/CodexWorktrees/unity-cli-validation/Vr_Battlegrounds_ai на принятой базе
04fc9de8875d390ab2fb1ddb4116536aa288a01d с полными LFS-ассетами. Ручной pilot716
дал actual exit0 и полный XML16/16 PASS: семь UI и девять сетевых EditMode тестов.
Холодная загрузка1717,79 секунды, затем около4 минут14 секунд до начала тестов;
причина второго интервала не установлена. XML SHA256 начинается с `cddc5a29`,
лог — с `1fc65673`; полные hashes и внешний R `22fb3c056fdd` закреплены в
[итоге pilot](changelog/2026-10-15-mcp-timeout-cli-pilot.md).
Производный package-lock diff сохранён и восстановлен, закрытый CLI checkout чистый.
Ошибочный `/**` в output roots finish вызвал WinError123; штатный recover завершил716
в DONE без broker result_sha, внешний R/ref и evidence сохранены. Это ещё не
production CLI executor и не проверка PlayMode/build/Player E2E.

MCP runtime6faf77 и pinned wheel выпущены собственной729: initialize/tools-list48,
прямой ответ execute46.0118654s и полный run_tests/get_test_job16/16 PASS подтверждены.
Для HTTP control-pong задан конечный timeout315 при сохранении JSON heartbeat,
default30, lease guards и no-replay. Output/** живым CLI отклонён до enqueue.
729 DONE, Rf161480da9a4,19 broker artifacts; blocked/recovery=false, FIFO перешла к730.
Полный source suite на предыдущем f344798:1058 тестов, один skip, два baseline-дефекта
(hook timing и короткая service lease). Последний delta:47/47 PASS, review45/45 PASS.
Полный набор на окончательном source повторно не запускался и не объявляется GREEN.

Текущее сопровождение: coordination mode off; очередь работает без плановой паузы.
Проверенные исправления доставляются собственной заявкой сопровождающего в конец FIFO.
Этап mcp-test-job-lifecycle влит в origin/dev c36f55e9; его прямая C# проба доказала
сохранение активного prebuild и полного Failed Result, но не закрыла стандартный MCP путь.
Свежие потери job_id расследуются отдельно как гонка неявного refresh/compile; для UI672
native RunFinished/XML были, а MCP callbacks после reload не доставили результат.
Параллельно подготовлены план миграции и offline контракт Unity CLI через тот же брокер
в source worktree agent-infra. Production CLI executor ещё не реализован.
Исторические проверки ниже относятся к прежним выпускам; актуальный live итог хранится
в ignored reports, не считается игровой приёмкой других владельцев.

### Историческое состояние до 2026-10-15

- Миграция завершена, enforced включён; восемь активных владельцев зарегистрированы.
- Inbox имеет свежую адресную выдачу и стабильную пагинацию; watch-inbox ограничен
  по времени. Request-base исполняет системный publisher, без LLM-сопровождающего.
- Native routes имеют отдельную привязку и CAS epoch; адрес проверяется свежим
  discovery, неоднозначность требует ручной доставки.
- Служебные Windows subprocess запускаются без консольных окон. Предыдущий полный
  Python-набор: 332 passed, 1 skipped; визуальная проба ограничена восемью секундами.
- Runtime32705c4 установлен после штатного завершения аренды271. Nested cwd
  определяет задачу по Git-root, относительные пути — по фактическому cwd.
  Проверки координации 102/102; независимое ревью без блокеров. Read-only проверка
  реального weapon cwd разрешила отдельный cd; чужие отчёты сохранены.
- Исправлен отказ подготовки run_tests; модальный Save-dialog остаётся гипотезой.
  Реальный эксперимент восстановления Unity не выполнен, пользовательская приёмка
  игровых изменений этим не подтверждается.

## Текущий план

Регистрация перенесена административным CAS handoff revision 19 → 20 в внешний checkout; история этапов сохранена.
Адрес infra_issue остаётся agent-coordination-protocol независимо от пути checkout.
В текущем Codex штатный Monitor с доставкой вывода в контекст не найден;
применяются обязательные проверки inbox/status и штатные уведомления.

Обязательный watch-inbox через штатный Monitor записан в AGENTS.md и правилах
для клиентов, доставляющих вывод в контекст. Запуск при старте/возобновлении,
повтор после timeout; один watcher на задачу/сессию, без автоматических ACK/действий.

Общий deletes реализован в checkpointc94bf7b: точные пути, отдельное разрешение
операции удаления, проверки пересечений, нативного hook и Git diff перед
worker/вливанием. Для любого клиента есть coordination.py delete --token --path.
Новые проверки 15/15, связанные 59/59; независимое ревью без блокеров.
Runtimec94bf7b установлен после штатного finish аренды282; pause=false,
заявки сохранены. Read-only проба установленной версии подтвердила общий
deletes, отказ Add/Edit и наличие CLI; чужие файлы не менялись.
Два файла arsenal-generator — примеры, не исключения в коде. Само удаление
выполняет владелец после проверки содержимого и ссылок.

Состояние очереди, аренды и базы проверяется живым status, а не этим снимком.
Управляемые сессии и межвендорные адаптеры отложены пользователем.

Подробности: [Details.md](Details.md), [правило общения](communication-policy.md),
[автоматизация](infrastructure-automation.md),
[исследование recovery](worker-recovery-analysis.md), [отложенный монитор](event-monitor.md).

## Проверка сопровождающего 2026-10-09

Ticket 292 восстановлен: DONE, blocked=false, recovery_required=false; worker вернулся на B edcb79e8, Lobby восстановлена, ready=true. Аварийный R e56ab413 сохранён без игровой приёмки. MCP HTTP и закреплённый stdio proxy прошли initialize, tools/list и instances/read; stdio initialize 0,98 с, 48 инструментов.

Уточнение проверки MCP: выше проверялся прямой runtime. Точная проектная команда
`uv run --quiet Tools/agents/unity_mcp_proxy.py` воспроизвела initialize timeout
в bots-fix, vr-test-stand и 2cf1. Причина — неявные standard handles при Windows
CREATE_NO_WINDOW в launcher; явные stdin/stdout/stderr устраняют отказ.
Исходный тест real pipes RED, после правки GREEN; связанные 24 проверки прошли.
Исходники исправления изолированы в F:/CodexWorktrees/c14d/agent-infra.

Native Monitor с доставкой в контекст не обнаружен; inbox проверяется явно. Codex list_threads/read_thread/send_message_to_thread доступны и проверены. Собственный native route опубликован после fresh discovery: epoch6, session01a11ed8-9871-72d3-8f28-e61675d7bff0; route-resolve с bots-fix работает в обе стороны. ACK только конкретных прочитанных событий. Артефакты: reports/recovery-292-20261009/.


## Аудируемый аварийный режим — 2026-10-10

Пользователь разрешил временное снятие локальных hook-блокеров с TTL, обязательным
аудитом и статистикой. Source9e5fa445 закреплён/проверен; штатный выпуск через origin/dev
и runtime deployment сопровождающего, без прямых патчей в checkout агентов.
[Проверки и границы](changelog/2026-10-10-audited-emergency-hooks.md).


## Global off и наблюдение — 2026-10-10

По поручению пользователя общий mode off уже включён. Source53f6760 добавляет
best-effort аудит без блокировки команд; штатный выпуск через origin/dev и managed
runtime deployment. [Проверки и границы](changelog/2026-10-10-global-off-observability.md).


## Неизменяемые Unity inputs — 2026-10-10

Source c72db11a06bb4cfc1d76c4142f692949cf476ab3 интегрирует reviewed b253fe4 поверх
offaudit53f6760. [Проверки](changelog/2026-10-10-immutable-worker-inputs.md).
Modeoff по поручению пользователя сохраняется; новый API опционален.

## EOL PhaseB и inbox --unread — 2026-10-11

Сопровождающий — Claude. Runtime ce950de88a7d27b51b27a71515e7309af1afc864: inbox --unread,
readonly gate, register UX, EOL-защиты broker. Продукт: Unity YAML/ProjectSettings/tasks в LF,
бинарные .asset — binary, pre-push EOL-проверка. [Проверки](changelog/2026-10-11-eol-phaseb-inbox-unread.md).

## LFS-хуки и правила Play — 2026-10-12

post-rewrite/post-merge гидратируют LFS main/worker после rebase/merge (инцидент 4567); Play только
через PlayLaunch; launcher serialized_repair.py. [Проверки](changelog/2026-10-12-hooks-lfs-rules.md).

## Вопросы человеку и wait-inbox — 2026-10-14

Вопросы пользователю — только AskUserQuestion; Stop-хук возвращает ход на вопрос текстом, открытые
вопросы видны на доске («Ждут вашего ответа»). Ожидание inbox — фоновый wait-inbox вместо Monitor-циклов;
inbox --unread --as (режим warn). Runtime eb4ab1d. [Проверки](changelog/2026-10-14-human-questions-wait-inbox.md).
