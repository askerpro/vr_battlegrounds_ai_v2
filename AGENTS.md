# VR Battlegrounds AI

Quest 2/3 VR shooter, Unity 6 URP. Game code: `Assets/Scripts/`; vendored SDKs: `Assets/ThirdParty/`; package versions: `Packages/manifest.json`.
Communicate with the user in Russian. Code comments and product documentation are Russian; agent instructions may be English.
This is the canonical entry point. `CLAUDE.md` imports it; `.codex/AGENTS.md` links to it. These rules override older recipes and hooks.

## Always apply

- Preserve other people's changes. Write only in your assigned checkout; check its mode before edits or Unity operations.
- Use `GameLog.<Channel>.<Level>`, never Debug.Log. If no channel fits, use GameLog.Error.
- Live-server map changes go through `MapLoader.Instance.LoadMap(sceneName)`. Mirror owns GameNetworkManager.offlineScene/onlineScene.
- Synchronized actions from updates, timers, physics, RPCs or hooks require `StateEventAuthority.IsAuthorOfItem` / `IsWorldAuthority`.
- Editor scripts belong in `Assets/Editor/VR_Battlegrounds/<category>/`, never under Assets/Scripts. Menu names use `VR Battlegrounds` with a space.
- Assign tags through `GameTags` / `GameTagRules`; never remove TagManager entries in an open Editor.
- Keep one owner of state and one responsibility per class. Stop an approach that adds competing writers or bypasses invariants; propose the architectural solution. Continue independent/approved work.
- User validation of behavior quality must precede new or rewritten tests for any new/changed functionality, project-wide, including Editor tools, avatars and IK. This includes temporary smoke checks, managed probes and all scripts with test expectations, regardless of name or location. Before confirmation, use compilation, unchanged applicable existing checks and diagnostics needed for review; report obsolete expectations separately, never fit them to unverified code. Provide manual verification steps and actual diagnostic results; technical PASS is not user acceptance. Check gameplay/visuals in Unity/headset as appropriate. After user confirmation, add/update tests in the same task.
- Obtain and report actual validation results and their scope. Tool success is not a passed check; pending user acceptance is not task completion.
- Code/assets require user verification before an accepted commit. Documentation-only commits and broker technical checkpoints are exempt; a checkpoint does not accept work.
- `origin/dev` is the accepted-work source. Integrate accepted work fast-forward only; no force-push. Local main-checkout dev mirrors origin/dev.
- Keep task material in `tasks/<task-id>/`: compact dated Readme.md, Details.md, plan.json, tools/, contracts/, changelog/. Generated reports belong in reports/, outside Git; never force-add them.
- Ordinary tasks do not edit shared indexes/changelogs: README.md, Docs/README.md, CHANGELOG.md, Docs/CHANGELOG.md, SDK journals or tasks/README.md. Product Docs changes need an explicit stage and area owner.
- SDK patches: reserve the number through `coordination.py patch-reserve`, record it in `tasks/<task-id>/changelog/<date>-sdk-<number>.md`; Mirror code also gets `VR Battlegrounds patch`. Shared SDK journals are historical.
- New/changed managed source/docs/tools use LF; check only owned physical paths plus staged blobs before checkpoint. Untouched legacy files are not batch-normalized. PhaseB: Unity YAML, ProjectSettings and tasks/**/*.md|json are LF too; never re-materialize YAML by hand (Unity saves converge it); binary .asset files are `binary`; pre-push rejects CR in pushed eol=lf blobs. [EOL contract](Docs/agents/text-eol.md).
- Use native file editing first (Codex apply_patch, Claude Edit/Write). Follow the injected terminal/encoding guidance; use git --no-pager.

## Задачи, эксперты и ревью (решение пользователя 2026-10-17)

- Одна задача — один агент — один worktree — одна ветка `task/<task-id>` от свежей `origin/dev`.
  Создание: `python Tools/agents/coordination.py task-new --task <task-id> ...` (worktree, ветка, регистрация, бриф).
  Агент задачи сам работает во всех нужных зонах. Сессии экспертов новых задач не ведут; существующие
  expert worktree доводят текущие задачи до merge.
- Новая задача — своя цель, отдельная приёмка пользователем, независимое вливание или указание пользователя
  «отдельной задачей». Этап — шаг к уже согласованному критерию приёмки. Несвязанные цели этапами не добавлять.
- Бриф обязателен: `task-brief` (цель, мотивация, план `[ ]`/`[x]`, статус), чтение `task-brief-show`.
  Статус обновлять при каждом переходе этапа и в конце сессии.
- Перед правкой путей из `owns` эксперта (`experts/<id>/expert.json`) прочитать его `AGENT.md`. `expert.json` —
  карта маршрутизации ревью, не запрет на запись. В конце задачи — короткая запись в `experts/<id>/journal/`
  каждой существенно затронутой зоны.
- Перед merge — ревью каждой затронутой зоны: `review-request` → `review-prompt` → read-only субагент с этим
  промптом сам выполняет `review-ack`; ACK действует только для зафиксированного SHA, `changes` — исправить и повторить.
- Сериализованные ассеты Unity (`.unity`, `.prefab`, `.asset`, `ProjectSettings/**`, `.asmdef`) одновременно правит
  один выполняющийся этап; `.cs` сливается обычным merge/rebase. Zone-ACK и переговоры о порядке ради writes отменены.
- Маршрут и команды: [задачи, эксперты и ревью](.agents/rules/agent_coordination.md#задачи-эксперты-и-ревью-решение-2026-10-17);
  роль экспертов: [experts/COMMON.md](experts/COMMON.md).

## Delegation and context ownership

Applies to Codex and Claude through this entry point. The principal agent owns the task's
working context: goal, constraints, accepted decisions, contracts, dependencies, progress
and user communication. Keep detailed execution traces in task artifacts, outside that context.

- Proactively delegate bounded research, diagnosis, implementation or review when it would
  introduce substantial transient detail or when independent work can proceed in parallel.
  Use the current client's native subagents within the already authorized task; routine
  delegation needs no separate user approval. Respect explicit user preferences and tool limits.
- Do small direct edits and tightly coupled decisions yourself when delegation adds overhead.
  Keep architectural decisions with the principal agent; escalate unresolved choices before
  dependent implementation. Delegation must not expand task scope or existing permissions.
- Give each subagent a concrete outcome, minimal relevant context and source links, file/system
  scope, dependencies, allowed actions, acceptance checks and required return format.
  Prefer a fresh context for independent investigations; do not copy the full chat by default.
- Parallel writers use isolated worktrees or explicitly reserved, non-overlapping file scopes.
  Never allow concurrent edits to a shared file. Subagents follow the same coordination,
  Unity lease, acceptance and integration rules; delegation does not bypass a gate.
- The principal agent updates the compact task Readme.md and plan.json. Subagents keep detailed
  findings and artifacts in their assigned task subfolder; generated traces go under reports/.
  Return conclusions, evidence paths/SHAs, changed files, actual checks and their scope,
  remaining risks and questions. Reference raw logs instead of pasting them into the main chat.
- Inspect subagent evidence and relevant diffs before integration. The principal agent owns
  verification, contract reconciliation, final status and user acceptance; a subagent's success
  message is not proof. Continue independent work while a delegated task runs.
- Cross-task requests and answers go to the hub inbox first, then notify through the native
  channel when available using task-id/event_id. See [coordination communication](.agents/rules/agent_coordination.md#общение-агентов-принятое-правило-от-2026-10-09)
  and [model/effort guidance](.agents/rules/project-workflows.md#delegation).
- `client` в плане — справочная информация: запускай Codex или Claude по доступности
  квоты, без требования совпадения с прежним client или отдельной передачи клиента.
  Owner/worktree/session и допускающие leases сохраняют свои проверки. Общение между
  задачами всегда проходит через canonical inbox; native-уведомление возможно только
  внутри одного семейства после свежей проверки обоих адресов, и не означает ACK.
- Inbox is notifications, not ACK (ACK is `contract-ack` for contracts/plans). On every check run
  `python Tools/agents/coordination.py inbox --task <task> --owner <owner> --unread --as "<address>"`: it returns
  unread messages and relevant notices in order and marks them read; repeat while
  `pagination.has_more=true` (`--limit 50`, `--max-bytes 20000`). Other service history is
  summarized in `pagination.system_read` (open recent_ids with `inbox --event <id>`). Plain
  `inbox` marks nothing; manual `ack-events` is not needed. Register with
  `register --document tasks/<task-id>/Readme.md` (not plan.json). In the Codex sandbox,
  register/begin/message/`inbox --unread` and other writing coordination commands need
  escalation (the hub is outside the writable root); `attempt to write a readonly database`
  means retry with escalation.
- The task inbox is handled by the leading session, the one that published its address with
  route-publish. Always run `python Tools/agents/coordination.py inbox --owner <owner> [--task <task>]
  --unread --as "<your address from the first line of ListAgents, name [ref]>"`. The hub is now in warn
  mode: a call without `--as` still marks events but returns `binding_warning`. After the switch to
  enforce (by the maintainer, on the user's decision) sessions without `--as` or with someone else's
  address get the leading session's task events view-only: nothing is marked, the task is listed in
  `pagination.view_only_tasks`, repeating is useless. Do not copy someone else's address into `--as`.
  If your address changed, run route-publish for each of your active tasks. Do not use `ack-events` for
  the leading session; if it returned `route_warnings`, tell that session the task-id/event_id.
  Wait for new events with a background command (Bash, run_in_background):
  `python Tools/agents/coordination.py wait-inbox --owner <owner> --as "<address>"`. It marks nothing
  and ends with one JSON line: `inbox_ready` — run the command from `command_argv` (`instruction` is the
  same command as a Bash string), handle the events and start wait-inbox again; `timeout` (after 12 h) —
  just restart it; `error` — do not restart wait-inbox until `coordination.py status` succeeds.
  `watch-inbox` is deprecated. Do not use periodic alarms, Monitor loops or ScheduleWakeup to check the
  hub. See [inbox and waiting](.agents/rules/agent_coordination.md#inbox-и-ожидание-событий).
- Questions and decisions for the user go only through the AskUserQuestion tool, never as plain
  text at the end of a turn. Before asking, finish everything that does not depend on the answer.
  Give options with the recommended one first; the user can always answer freely via Other.
  A Stop hook returns the turn if the final message looks like a plain-text question.
- Before AskUserQuestion, process your inbox (contract-ack, replies to other agents):
  an open dialog freezes the task's inter-agent coordination until the human answers.
  Reminders about an earlier unanswered question do not create new dialogs.
- On a wake-up (background command finished, subagent notification) with nothing to report,
  answer with one short line or nothing.
- При проблеме agent-infra, worker, broker/proxy, coordination hooks или MCP сразу
  сообщи сопровождающему: canonical inbox, адрес agent-coordination-protocol,
  kind infra_issue, с пометкой «Для сопровождающего worker и agent-infra», затем
  подтверждённый native канал с task-id/event_id. Пользователь прямо разрешил эти
  уведомления; отдельно спрашивать его не нужно. Сохрани worktree, task/stage,
  ticket, request/job IDs, runtime/VERSION, симптом и redacted error/evidence.
  Если канал недоступен, сообщи об этом человеку; не оставляй отчёт только в чате.
- Агенты не чинят и не обходят инфраструктуру самостоятельно, включая локальные
  инфраструктурные launchers/config, маршрутизацию и состояние занятого worker.
  Ремонт, чужой/аварийный recovery и deployment выполняет только явно назначенный сопровождающий.
  Собственная аренда worker не является назначением на обслуживание. Разрешены
  read-only status/log/help и работа по собственной разрешённой игровой или SDK-задаче,
  включая исправления и сборки в своём build checkout. Продолжай независимую работу.
  При неизвестном исходе Unity-операции не делай retry/replay, clear_stuck или recover
  и не освобождай активную операцию через finish. Если же собственная аренда истекла
  (`RECOVERY_REQUIRED`), исход операции известен и прогон в Unity завершён, владелец билета
  может штатно закрыть блокировку командой `python Tools/agents/editor-broker.py recover --ticket <id> --token <token>`
  (сохраняет снимок `R`), либо — когда изменения и dirty-состояние на worker сохранять не нужно —
  быстрым `python Tools/agents/editor-broker.py recover --ticket <id> --token <token> --force`
  (сбрасывает dirty-сцены/ассеты/Prefab Stage и возвращает worker к `baseline_sha` без снимка `R`).
  После подтверждённого завершения своего подготовленного пакета и cleanup сразу выполни штатный finish.
- At session start/resume, the principal agent refreshes its own native route in the hub
  from a fresh native listing, or clears it when this client cannot address peer sessions.
  Before notifying another task, resolve its candidate and confirm both endpoints in a
  fresh listing. Never infer an address from plan UUID, folder, busy state or an old name;
  unknown/ambiguous endpoints use manual delivery. See [native routes](.agents/rules/agent_coordination.md#адресация-штатных-уведомлений).

## Checkout, Unity and coordination

Compare `git rev-parse --absolute-git-dir` with `git rev-parse --path-format=absolute --git-common-dir`.
- **Same: main checkout.** Work directly with the user's checkout/Editor, without a worker lease. Do not manage worker, queue or .agent-state unless assigned.
- **Different: linked worktree.** Write only there; the main checkout is read-only. Unity mutations require your RUNNING broker lease and guard. The proxy must run from your worktree.
- Before a worker request prepare exact actions/release conditions and fetch/rebase origin/dev. Route: checkpoint → request → watch-ticket → claim → begin → guard → execute/wait → finish → receive.
- Finish immediately after your prepared package and cleanup, including failed checks, before diagnosis, edits or waiting for a person. Do not finish during an active operation. Renew only for ongoing Unity work. If your own lease expired (`RECOVERY_REQUIRED`) after a known completed operation, use `recover --ticket <id> --token <token>` (saves `R`) or `recover --ticket <id> --token <token> --force` (discards worker changes/dirty scenes/Prefab Stage and resets to `baseline_sha` without `R`).
- Never interrupt human Play Mode, dirty scenes, prefab stages or others' tests.
- При ремонте инфраструктуры очередь продолжает работать: сопровождающий отключает coordination-проверки через штатный mode off, сохраняет FIFO, аренды, результаты и guards живой операции. После подготовки, тестов и ревью сопровождающий ставит доставку исправления в конец очереди. Не отменять чужие заявки и не требовать повторной постановки из-за обслуживания.
- При проблемах интеграции MCP параллельно готовить миграцию тестов и автоматизации на Unity CLI через тот же брокер: immutable spec, полный XML/log, durable результат и штатные finish/receive. Подготовка не разрешает запускать второй Editor в открытом worker, обходить аренду или внедрять свой альтернативный клиент. Исполнителя и общий backend выпускает сопровождающий; владельцы готовят входы и условия приёмки своей задачи.
- Read current coordination mode; **missing/off** uses the legacy pipeline, **enforced** requires registration, contracts/stage admission and merge-request/merge-execute. Stage admission is not a Unity lease. Do not change mode yourself.
- Broker/proxy sources and deployment belong to `F:/UnityProjects/agent-infra`; product Tools/agents files are launchers.
- Инфраструктурные launchers/hooks/инструкции выпускаются через проверенный candidate и штатный merge в origin/dev; владельцы обновляют свои ветки локальным rebase с сохранением tracked/staged/untracked изменений. Прямая запись в чужой checkout допустима только как аварийная мера с backup и обязательным незамедлительным штатным выпуском. [Маршрут сопровождающего](F:/UnityProjects/agent-infra/docs/maintenance-guide.md).
- Save complete MCP reports to task reports/ and return only passed/counts/up to 10 examples/path. Use `Tools/UnityMcp/compact-result.js` with a 2000-token output budget.
- `executionCompleted=true` means the action ran; never repeat it for output. After a timeout inspect durable/native state before retrying a mutation. Without MCP use current Editor.log evidence and concrete user edit steps.

## Read only what this task needs

Use rg scoped to the relevant area. For game C# start in Assets/Scripts or Assets/Editor; search ThirdParty/Packages only for the relevant SDK/package. Search .unity/.prefab by class/GUID instead of reading entire files.
Before reading or running a helper, discover its actual path with `rg --files` in the relevant area; then read the found CLI's `--help`. Verify paths in the current checkout even when an old report names them. For infrastructure, list `Tools/agents` and `Tools/UnityMcp` first: product `Tools/agents` files are launchers, implementation lives in agent-infra. See [MCP tool discovery](.agents/rules/unity_mcp.md#поиск-инструментов).
**Do not load every linked document.** Search large indexes/reference files and read the matching section only.

| Task | Required route |
|---|---|
| Code/assets | Relevant section of [workflow details](.agents/rules/project-workflows.md#asset-rules); architecture/naming when applicable |
| Bug | Search [troubleshooting](Docs/troubleshooting.md) by symptom first; read matching SDK [known issue](Docs/UltimateXR/known-issues.md) if relevant |
| Feature | Search [Docs index](Docs/README.md) for existing ownership; read the relevant [gameplay](Docs/gameplay.md) section |
| Match/round/map flow | Relevant [gameplay](Docs/gameplay.md) / [game-manager](Docs/game-manager.md) section |
| Session/roles/devices | [session architecture](Docs/session-architecture.md) |
| Weapons/avatars/menu | Matching asset section; recipes in .claude/skills/add-weapon, setup-avatar or add-menu-screen |
| Map hierarchy/geometry | [scene hierarchy](Docs/scene-hierarchy.md), asset rules and occlusion/validation steps |
| Unity automation/tests | [Unity access](.agents/rules/project-workflows.md#unity-access) and [verification](.agents/rules/project-workflows.md#verification-and-acceptance); multi-process probes: [test stand](Docs/test-stand.md); Play — только через PlayLaunch ([test stand](Docs/test-stand.md), «Как агенту включить Play (через Offline)»), прямой `EditorApplication.isPlaying = true` запрещён (выход `= false` — шаг 4 рецепта) |
| MCP failure | Current sections of [MCP diagnostics](Docs/unity-mcp.md); old 9.x fixes are historical |
| New task, foreign zone, review | [tasks, experts and review](.agents/rules/agent_coordination.md#задачи-эксперты-и-ревью-решение-2026-10-17); zone knowledge: `experts/<id>/AGENT.md` |
| Coordination/integration | [integration route](.agents/rules/project-workflows.md#integration-and-coordination); enforced details: [agent_coordination](.agents/rules/agent_coordination.md) |
| Task docs/delegation/LFS | Matching section of [workflow details](.agents/rules/project-workflows.md); LFS: [worktrees](Docs/agents/lfs-worktrees.md) |
| UI/fonts/arsenal | [UI design](Docs/ui-design-system.md), [menu](Docs/ui-menu-architecture.md), [fonts](Docs/ui-fonts.md) / [arsenal](Docs/Arsenal/Arsenal_Code_Architecture_RU.md) |
| Build/Git/performance/audio | Relevant [release](Docs/release.md), [version control](Docs/version-control.md), [stress test](Docs/perf-stress-test.md) / [sound library](Docs/sound-library.md) |

Detailed operating commands: [editor broker](F:/UnityProjects/agent-infra/docs/editor-broker.md) and [proxy routing](F:/UnityProjects/agent-infra/docs/unity-mcp-proxy.md), only when operating them.


## Временный аварийный режим координационного hook

При infra-блокере пользователь разрешил своему агенту включить `emergency-hook-on`
с причиной и TTL до 60 минут. Каждая попытка инструмента и исходное решение hook
аудируются; expiry возвращает обычный режим. Это не передача чужой области и не
отмена аренды worker, контрактов, ACK или публикации через брокер. Команды и порядок:
[аварийный режим с аудитом](.agents/rules/agent_coordination.md#аварийный-режим-pretooluse-с-аудитом).

Решение пользователя от 2026-10-15: прежнее общее разрешение на локальный
emergency-hook не заменяет явного назначения на инфраструктурный ремонт и не
разрешает обход infra-блокера без такого назначения. Оно не даёт доступа к общему
хабу, worker или MCP для самостоятельного ремонта. Явные разрешения на собственную
игровую/SDK-задачу и build-исправления сохраняются; общий mode off также не назначает
агента сопровождающим.


## Временный общий mode off

Пользователь поручил временно отключить coordination gate до починки. Общий mode off
снимает PreToolUse и координационный SCOPE admission, без регистрации/begin ради
локальных инструментов. Очередь/аренды Unity и snapshot/recovery сохраняются.
После runtime deployment source53f6760 вызовы наблюдаются best-effort redacted аудитом;
ошибка аудита не блокирует off. [Команды и возврат](.agents/rules/agent_coordination.md#полное-временное-отключение-coordination-gate).
Не включайте enforced до завершения off-created Unity заявок.


При будущем technical совместном Unity input используйте явные immutable grants и
recorded own dependency proofs: [контракт](F:/UnityProjects/agent-infra/docs/immutable-input-components.md).
Global mode off остаётся; обычные requests без нового manifest не требуют этой схемы.
