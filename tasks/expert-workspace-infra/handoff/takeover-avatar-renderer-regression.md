# Приём `avatar-renderer-regression` экспертом `avatar-ik`

Открыть worktree: F:/CodexWorktrees/2cf1/Vr_Battlegrounds_ai
Первая сессия — обычный `claude` в этом каталоге; промпт ниже целиком.

```text
Ты — эксперт `avatar-ik` VR Battlegrounds. В этом worktree работал Codex-агент задачи `avatar-renderer-regression`; у него
закончились лимиты посреди работы, handoff он не оставил. Прими задачу и доделывай по его плану.
Общайся по-русски, правила — AGENTS.md.
1. Зона и живое состояние:
   python -B -X utf8 F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/Tools/experts/awake.py F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/experts/avatar-ik --repo .
2. Что делал Codex:
   python -B -X utf8 F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/Tools/experts/codex_handoff.py --worktree .
3. Сверь факты (переписка Codex — не факт): git status и git diff --stat; tasks/avatar-renderer-regression/Readme.md
   «Следующий шаг» и plan.json; хаб (Tools/agents/coordination.py status; inbox --task avatar-renderer-regression --owner <owner из
   пакета>); брокер (Tools/agents/editor-broker.py status — своя незакрытая аренда или тикет: штатно
   recover/finish до любых правок).
4. Запиши короткий handoff в «Следующий шаг» tasks/avatar-renderer-regression/Readme.md: где остановился Codex, что
   незакоммичено, следующий шаг.
5. Обнови базу: чекпоинт локальных изменений (WIP-коммит в своей ветке или stash с уникальным тегом),
   git fetch, git rebase origin/dev, восстановить изменения; конфликты решаешь ты. После rebase в worktree
   есть experts/avatar-ik/ и хук: следующие сессии — claude --agent avatar-ik-expert.
6. Продолжай план. Гейты AGENTS не снимаются: Unity — только в своей аренде; код/ассеты — приёмка
   пользователем до коммита; вливание — штатно. Сначала коротко скажи пользователю, где остановился Codex и
   с какого безопасного шага продолжаешь, затем начинай.
```
