# Приём `haptics-system` экспертом `manipulation`

Открыть worktree: F:/CodexWorktrees/haptics/Vr_Battlegrounds_ai
Первая сессия — обычный `claude` в этом каталоге; промпт ниже целиком.

```text
Ты — эксперт `manipulation` VR Battlegrounds. В этом worktree работал предыдущий агент задачи `haptics-system` (Codex или Claude);
он остановился посреди работы, handoff не оставил. Прими задачу и доделывай по его плану.
Общайся по-русски, правила — AGENTS.md.
Работал Claude-агент; этап manipulation-kb running. В рамках этого этапа замени профиль .claude/agents/manipulation-expert.md текстом tasks/expert-workspace-infra/handoff/manipulation-expert.md (после rebase он есть в worktree; запрос в inbox 3272/3279) и подтверди выполнение сообщением expert-workspace-infra.
1. Зона и живое состояние:
   python -B -X utf8 F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/Tools/experts/awake.py F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/experts/manipulation --repo .
2. Что делал предыдущий агент:
   python -B -X utf8 F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/Tools/experts/session_handoff.py --worktree .
3. Сверь факты (переписка — не факт): git status и git diff --stat; tasks/haptics-system/Readme.md
   «Следующий шаг» и plan.json; хаб (Tools/agents/coordination.py status; inbox --task haptics-system --owner <owner из
   пакета>); брокер (Tools/agents/editor-broker.py status — своя незакрытая аренда или тикет: штатно
   recover/finish до любых правок).
4. Запиши короткий handoff в «Следующий шаг» tasks/haptics-system/Readme.md: где остановился предыдущий агент, что
   незакоммичено, следующий шаг.
5. Обнови базу: чекпоинт локальных изменений (WIP-коммит в своей ветке или stash с уникальным тегом),
   git fetch, git rebase origin/dev, восстановить изменения; конфликты решаешь ты. После rebase в worktree
   есть experts/manipulation/ и хук: следующие сессии — claude --agent manipulation-expert.
6. Продолжай план. Гейты AGENTS не снимаются: Unity — только в своей аренде; код/ассеты — приёмка
   пользователем до коммита; вливание — штатно. Сначала коротко скажи пользователю, где остановился предыдущий агент и
   с какого безопасного шага продолжаешь, затем начинай.
```
