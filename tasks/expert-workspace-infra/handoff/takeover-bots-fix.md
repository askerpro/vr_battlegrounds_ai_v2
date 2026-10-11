# Приём `bots-fix` экспертом `bots`

Открыть worktree: F:/CodexWorktrees/bots-fix/Vr_Battlegrounds_ai
Первая сессия — обычный `claude` в этом каталоге; промпт ниже целиком.

```text
Ты — эксперт `bots` VR Battlegrounds. В этом worktree работал Codex-агент задачи `bots-fix`; у него
закончились лимиты посреди работы, handoff он не оставил. Прими задачу и доделывай по его плану.
Общайся по-русски, правила — AGENTS.md.
1. Зона и живое состояние:
   python -B -X utf8 F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/Tools/experts/awake.py F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/experts/bots --repo .
2. Что делал Codex:
   python -B -X utf8 F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/Tools/experts/codex_handoff.py --worktree .
3. Сверь факты (переписка Codex — не факт): git status и git diff --stat; tasks/bots-fix/Readme.md
   «Следующий шаг» и plan.json; хаб (Tools/agents/coordination.py status; inbox --task bots-fix --owner <owner из
   пакета>); брокер (Tools/agents/editor-broker.py status — своя незакрытая аренда или тикет: штатно
   recover/finish до любых правок).
4. Запиши короткий handoff в «Следующий шаг» tasks/bots-fix/Readme.md: где остановился Codex, что
   незакоммичено, следующий шаг.
5. Обнови базу: чекпоинт локальных изменений (WIP-коммит в своей ветке или stash с уникальным тегом),
   git fetch, git rebase origin/dev, восстановить изменения; конфликты решаешь ты. После rebase в worktree
   есть experts/bots/ и хук: следующие сессии — claude --agent bots-expert.
6. Продолжай план. Гейты AGENTS не снимаются: Unity — только в своей аренде; код/ассеты — приёмка
   пользователем до коммита; вливание — штатно. Сначала коротко скажи пользователю, где остановился Codex и
   с какого безопасного шага продолжаешь, затем начинай.
```
