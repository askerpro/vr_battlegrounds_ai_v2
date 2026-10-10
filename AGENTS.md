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
- Before the user validates changed gameplay in Unity/headset, do not write or rewrite its tests. Check compilation, run applicable existing checks, list outdated expectations and provide acceptance steps. After confirmation, update tests in the same task, including avatar tests.
- Obtain and report actual validation results and their scope. Tool success is not a passed check; pending user acceptance is not task completion.
- Code/assets require user verification before an accepted commit. Documentation-only commits and broker technical checkpoints are exempt; a checkpoint does not accept work.
- `origin/dev` is the accepted-work source. Integrate accepted work fast-forward only; no force-push. Local main-checkout dev mirrors origin/dev.
- Keep task material in `tasks/<task-id>/`: compact dated Readme.md, Details.md, plan.json, tools/, contracts/, changelog/. Generated reports belong in reports/, outside Git; never force-add them.
- Ordinary tasks do not edit shared indexes/changelogs: README.md, Docs/README.md, CHANGELOG.md, Docs/CHANGELOG.md, SDK journals or tasks/README.md. Product Docs changes need an explicit stage and area owner.
- SDK patches: reserve the number through `coordination.py patch-reserve`, record it in `tasks/<task-id>/changelog/<date>-sdk-<number>.md`; Mirror code also gets `VR Battlegrounds patch`. Shared SDK journals are historical.
- New/changed managed source/docs/tools use LF; check only owned physical paths plus staged blobs before checkpoint. Untouched legacy files are not batch-normalized. [EOL contract](Docs/agents/text-eol.md).
- Use native file editing first (Codex apply_patch, Claude Edit/Write). Follow the injected terminal/encoding guidance; use git --no-pager.

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
- At session start/resume, check inbox and hub status. If the client provides a native
  Monitor that delivers process output into the agent context, the principal agent MUST
  attach one bounded read-only watch-inbox for its task/session and restart it after timeout
  while the session continues. Do not create duplicates or substitute an unattended daemon.
  Signals require fresh inbox/status checks; monitoring never ACKs or executes requests.
  See [monitor lifecycle](.agents/rules/agent_coordination.md#штатный-monitor-inbox).
- Report worker, broker/proxy, coordination-hook and agent-infra issues to the hub:
  address task agent-coordination-protocol, kind infra_issue, with an explicit
  'for the worker and agent-infra maintainer' label, symptom/impact, reproduction,
  ticket/event/commit IDs and evidence links. Then notify through the native channel
  when available. Do not leave the report only in chat or self-repair shared infrastructure.
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
- Finish immediately after your prepared package and cleanup, including failed checks, before diagnosis, edits or waiting for a person. Do not finish during an active operation. Renew only for ongoing Unity work.
- Never interrupt human Play Mode, dirty scenes, prefab stages or others' tests. During maintenance, leave broker/worker alone until completion is announced.
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
| Unity automation/tests | [Unity access](.agents/rules/project-workflows.md#unity-access) and [verification](.agents/rules/project-workflows.md#verification-and-acceptance); multi-process probes: [test stand](Docs/test-stand.md) |
| MCP failure | Current sections of [MCP diagnostics](Docs/unity-mcp.md); old 9.x fixes are historical |
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
