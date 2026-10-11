# Приём `lobby-decoration` экспертом `level-design`

Открыть worktree: F:/UnityProjects/Vr_Battlegrounds_ai
Первая сессия — обычный `claude` в этом каталоге; промпт ниже целиком.

```text
Ты — эксперт `level-design` VR Battlegrounds. В этом worktree работал предыдущий агент задачи `lobby-decoration` (Codex или Claude);
он остановился посреди работы, handoff не оставил. Прими задачу и доделывай по его плану.
Общайся по-русски, правила — AGENTS.md.
ВНИМАНИЕ: это основной checkout пользователя (Unity Editor пользователя). Rebase, stash и переключение веток — только после явного подтверждения пользователя; Play Mode, открытые сцены и префабы пользователя не трогать.
1. Зона и живое состояние:
   python -B -X utf8 F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/Tools/experts/awake.py F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/experts/level-design --repo .
2. Что делал предыдущий агент:
   python -B -X utf8 F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/Tools/experts/session_handoff.py --worktree .
3. Сверь факты (переписка — не факт): git status и git diff --stat; tasks/lobby-decoration/Readme.md
   «Следующий шаг» и plan.json; хаб (Tools/agents/coordination.py status; inbox --task lobby-decoration --owner <owner из
   пакета>); брокер (Tools/agents/editor-broker.py status — своя незакрытая аренда или тикет: штатно
   recover/finish до любых правок).
4. Запиши короткий handoff в «Следующий шаг» tasks/lobby-decoration/Readme.md: где остановился предыдущий агент, что
   незакоммичено, следующий шаг.
5. Обнови базу: чекпоинт локальных изменений (WIP-коммит в своей ветке или stash с уникальным тегом),
   git fetch, git rebase origin/dev, восстановить изменения; конфликты решаешь ты. После rebase в worktree
   есть experts/level-design/ и хук: следующие сессии — claude --agent level-design-expert.
6. Продолжай план. Гейты AGENTS не снимаются: Unity — только в своей аренде; код/ассеты — приёмка
   пользователем до коммита; вливание — штатно. Сначала коротко скажи пользователю, где остановился предыдущий агент и
   с какого безопасного шага продолжаешь, затем начинай.
```
