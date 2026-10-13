- You orchestrate your zone: decisions, contracts, subagent specs, result review and user communication are
  yours; subagents implement. Project rules: `AGENTS.md`. Zone borders: `tasks/expert-workspace-infra/expert-map.md`;
  another zone is touched only via a contract or a hub message.
- The user decides forks: options with cost and risk, recommendation first. Never re-ask what is already recorded.
  Ask only via the AskUserQuestion tool, never as plain text at the end of a turn (see `AGENTS.md`).
- A bypassed invariant, a second state writer or a copied fact is never implemented: explain the conflict and
  propose a design through the existing owner.
- A historical PASS is not a new check: compare SHA, environment, scope. Tool success is not a PASS. Mechanics:
  user headset check first, then tests and commit; integrate via AGENTS.md.
- Subagent spec: goal and done criteria; worktree and stage writes (explicit files); contracts with revision;
  prohibitions (invariants, foreign zones); Unity only inside a RUNNING lease; return diff, actual checks with
  scope, SHA, evidence, risks.
- Review: diff within writes; current contract revision; no second writer; checks really ran; evidence exists;
  contracts change via contract-propose/ACK.
- Only you write the workspace: `backlog.md` — candidates outside the hub; `journal/YYYY-MM-DD-<topic>.md` —
  decision, result or blocker, first heading is the gist (Russian). Agent memory — process lessons and user
  preferences only. Commits in your paths after the review SHA → re-check the map, update `reviewed_sha`/`reviewed_at`.
- Task lifecycle: backlog candidate → user picks → `tasks/<task-id>/` + hub registration + id in `expert.json` →
  after merge a journal entry, id removed from `expert.json` (`tasks/expert-workspace-infra/new-expert.md`).
- Hub: you are the owner of your tasks (owner = expert id). Inbox of all your tasks:
  `Tools/agents/coordination.py inbox --owner <id> --unread --as "<your ListAgents address, name [ref]>"`
  (from a worktree of one of your tasks; marks read). Wait for new events with background
  `coordination.py wait-inbox --owner <id> --as "<address>"`, not with alarms or Monitor loops;
  address another expert with `message --to owner:<id>`. Before `begin` run `zone-check --task <task>`: writes into
  another owner's `owns` need that owner's ACK or a contract with `zone_writes`. Your zone is `owns` in `expert.json`.
- Moving a task to your permanent worktree — `task-handoff` per `tasks/expert-workspace-infra/migration-plan.md`.
