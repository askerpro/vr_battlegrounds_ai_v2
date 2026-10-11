# Приём `map-runtime-bootstrap` экспертом `gameplay`

Открыть worktree: F:/CodexWorktrees/map-runtime-bootstrap/Vr_Battlegrounds_ai
Первая сессия — обычный `claude` в этом каталоге; промпт ниже целиком.

```text
Ты — эксперт `gameplay` VR Battlegrounds. В этом worktree работал предыдущий агент задачи `map-runtime-bootstrap` (Codex или Claude);
он остановился посреди работы, handoff не оставил. Прими задачу и доделывай по его плану.
Общайся по-русски, правила — AGENTS.md.
Работал Claude-агент. Этап series-smoke-e2e пишет E2ERunner.cs — это каркас launch-infra (карта зон ред. 2): правку каркаса согласуй с launch-infra через inbox, сценарий — твой.
1. Зона и живое состояние:
   python -B -X utf8 F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/Tools/experts/awake.py F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/experts/gameplay --repo .
2. Что делал предыдущий агент:
   python -B -X utf8 F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/Tools/experts/session_handoff.py --worktree .
3. Сверь факты (переписка — не факт): git status и git diff --stat; tasks/map-runtime-bootstrap/Readme.md
   «Следующий шаг» и plan.json; хаб (Tools/agents/coordination.py status; inbox --task map-runtime-bootstrap --owner <owner из
   пакета>); брокер (Tools/agents/editor-broker.py status — своя незакрытая аренда или тикет: штатно
   recover/finish до любых правок).
4. Запиши короткий handoff в «Следующий шаг» tasks/map-runtime-bootstrap/Readme.md: где остановился предыдущий агент, что
   незакоммичено, следующий шаг.
5. Обнови базу: чекпоинт локальных изменений (WIP-коммит в своей ветке или stash с уникальным тегом),
   git fetch, git rebase origin/dev, восстановить изменения; конфликты решаешь ты. После rebase в worktree
   есть experts/gameplay/ и хук: следующие сессии — claude --agent gameplay-expert.
6. Продолжай план. Гейты AGENTS не снимаются: Unity — только в своей аренде; код/ассеты — приёмка
   пользователем до коммита; вливание — штатно. Сначала коротко скажи пользователю, где остановился предыдущий агент и
   с какого безопасного шага продолжаешь, затем начинай.
```
