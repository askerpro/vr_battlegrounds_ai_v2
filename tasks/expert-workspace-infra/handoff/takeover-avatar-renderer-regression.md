# Приём `avatar-renderer-regression` экспертом `avatar-ik`

Открыть worktree: F:/CodexWorktrees/2cf1/Vr_Battlegrounds_ai
Owner задачи в хабе уже `avatar-ik` (task-handoff выполнен 2026-10-11). Первая сессия — обычный `claude` в этом каталоге
(до rebase в worktree может не быть профиля эксперта); промпт ниже целиком.

```text
Ты — эксперт `avatar-ik` VR Battlegrounds. Задача `avatar-renderer-regression` передана тебе (owner avatar-ik); прежний агент (Codex или Claude)
остановился посреди работы, handoff не оставил. Прими задачу и доделывай по его плану.
Общайся по-русски, правила — AGENTS.md.
1. Зона и живое состояние:
   python -B -X utf8 F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/Tools/experts/awake.py F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/experts/avatar-ik --repo .
2. Что делал прежний агент:
   python -B -X utf8 F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/Tools/experts/session_handoff.py --worktree .
3. Сверь факты (переписка — не факт): git status и git diff --stat; tasks/avatar-renderer-regression/Readme.md «Следующий шаг» и
   plan.json; хаб (Tools/agents/coordination.py status; inbox --owner avatar-ik --unread); брокер
   (Tools/agents/editor-broker.py status — своя незакрытая аренда или тикет: штатно recover/finish до любых правок).
4. Запиши короткий handoff в «Следующий шаг» tasks/avatar-renderer-regression/Readme.md: где остановился прежний агент, что
   незакоммичено, следующий шаг.
5. Обнови базу: чекпоинт локальных изменений (WIP-коммит в своей ветке или stash с уникальным тегом), git fetch,
   git rebase origin/dev, восстановить изменения; конфликты решаешь ты. После rebase в worktree есть experts/avatar-ik/ и
   хук. Затем (кроме основного checkout F:/UnityProjects) создай .claude/settings.local.json с {"agent": "avatar-ik-expert"}
   (файл локальный, в Git не попадает): любая новая сессия Claude в этом worktree — в том числе в VS Code —
   стартует экспертом avatar-ik с пакетом awake. Новый маршрут: route-publish.
6. Продолжай план. Перед begin — zone-check --task avatar-renderer-regression. Гейты AGENTS не снимаются: Unity — только в своей аренде;
   код/ассеты — приёмка пользователем до коммита; вливание — штатно. Сначала коротко скажи пользователю, где
   остановился прежний агент и с какого безопасного шага продолжаешь, затем начинай.
```
