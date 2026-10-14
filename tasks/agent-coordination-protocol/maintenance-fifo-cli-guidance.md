# MCP результаты, FIFO и подготовка Unity CLI — 2026-10-15

Пользователь поручил продолжать очередь в аварийном mode off, сохранять FIFO,
подготавливать исправления вне worker и доставлять их собственной заявкой в конец.
Допуски живых операций, fencing, чужие checkout, результаты и ACK сохраняются.
Владельцы сразу сообщают инфраструктурный отказ через inbox и подтверждённый
Codex channel; самостоятельный infra repair/bypass не разрешён.

## Подтверждённые отдельные отказы

1. Стандартный Python `run_tests` upstream 10.2.0 выполняет неявный refresh/compile;
   preflight принимает старый idle snapshot и отправляет старт перед domain reload.
   Проба исходного модуля воспроизводит потерю command_result без job_id. Сохранённые
   server/Editor.log по 669/670/676 согласуются с этим; UUID неявного refresh в старых
   логах отсутствует, поэтому историческая причинность ограничена корреляцией.
   Fix отделяет явную подготовку от старта, отказывает на dirty/unknown/busy и
   проверяет сохранение штатного job_id через реальный upstream module fixture.
2. UI672 получил ID, native RunStarted/RunFinished и запись TestResults.xml были;
   MCP job остался completed=0/result=null. После domain reload TestJobManager
   восстанавливает SessionState, однако lazy TestRunnerService не регистрируется
   для GetTestJob. Нужен callback receiver до первого framework update, без Execute
   или replay. Результат старой попытки не объявлен восстановленным этим диагнозом.
   Для reload между leaf callbacks результат теперь собирается из полного native
   terminal дерева вместо утраченного volatile списка. Managed RED: summary=2,
   details=1; GREEN восстановил обе детали, включая прежний Failed/stack/output.
3. Selected checkpoint scene-detail падал на чужом nested repo с `dir/` вне scope.
   Source fix фильтрует чужие untracked до проверки путей; собственный scope и full
   checkpoint сохраняют строгие проверки. Чужие файлы/index/GUID не изменяются.

## Подготовка миграции

План и offline schema 1 находятся в отдельном source worktree agent-infra:
[unity-cli-migration.md](F:/CodexWorktrees/mcp-results-cli-migration-20261015/agent-infra/docs/unity-cli-migration.md),
[пример spec](F:/CodexWorktrees/mcp-results-cli-migration-20261015/agent-infra/tools/unity_cli_migration/example-spec.json).
Прототип строит argv и проверяет полный NUnit XML, но не запускает Unity и не
подключён к admission. Production CLI executor ещё не выпущен. Целевой вариант —
отдельный закрытый CLI checkout под **той же глобальной FIFO**, с immutable inputs,
durable process intent, lease/renew, архивом XML/log и штатными R/B/finish/receive.
Второй Editor в открытом GUI worker не является миграцией.

## Граница приёмки

Source/fixture и independent review фиксируются отдельно от живой проверки.
Окончательная MCP проверка: собственный RUNNING ticket, явная подготовка и native
idle, стандартный run_tests возвращает job_id, get_test_job доставляет полный
terminal Result с leaf details, сохранение результата до cleanup/finish, затем
worker readiness и следующий прежний участник FIFO. Прямая C# проба предыдущего
этапа и initialize/resources сами по себе этот маршрут не принимают.
Игровые задачи, hardware/Quest и старые UNKNOWN исходы этим этапом не принимаются.
Свежие receipts и подробные результаты — в ignored reports своей задачи.

## Подготовленные проверки выпуска

Source commit agent-infra: `de5035a57e7f08a157ef1878777c73d7c5055c23`.
Независимые review исправлений source и FIFO deployment выполнены; финальные
адресные source проверки: queued 22, MCP server 37, reservation 8 — OK; CLI
offline prototype 22 — OK. Широкий прогон во время подготовки: 1003 OK, 12 skipped;
новые проверки reservation, добавленные после его старта, выполнены отдельно.
Immutable wheel реальным uvx entrypoint подтвердил 71 dependency pin, helper/tool
hashes и импорт main без вызова main/запуска сервера. Live deployment ещё впереди.

Package pins, двухфайловая установка и managed reload доказательства:
[Tools/UnityMcp/README.md](../../Tools/UnityMcp/README.md).
Подготовленный live probe ожидает 3 leaf results: 2 PASS и 1 намеренный FAIL,
долгий prebuild и реальный reload. Это инфраструктурная проверка доставки;
полный Result получить до finish. Долговременный cache после последующего terminal
reload остаётся ограничением upstream и не объявляется исправленным этим этапом.
