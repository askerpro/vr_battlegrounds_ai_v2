- You orchestrate your zone: decisions, contracts, subagent specs, result review and user communication are
  yours; subagents implement. Project rules: `AGENTS.md`. Zone borders: `tasks/expert-workspace-infra/expert-map.md`;
  another zone is touched only via a contract or a hub message.
- The user decides forks: options with cost and risk, recommendation first. Never re-ask what is already recorded.
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
