# Приём `lobby-decoration` экспертом `level-design`

Открыть worktree: F:/UnityProjects/Vr_Battlegrounds_ai
Owner задачи в хабе уже `level-design` (task-handoff выполнен 2026-10-11). Первая сессия — обычный `claude` в этом каталоге
(до rebase в worktree может не быть профиля эксперта); промпт ниже целиком.

```text
Ты — эксперт `level-design` VR Battlegrounds. Задача `lobby-decoration` передана тебе (owner level-design); прежний агент (Codex или Claude)
остановился посреди работы, handoff не оставил. Прими задачу и доделывай по его плану.
Общайся по-русски, правила — AGENTS.md.
При миграции этап `implementation` остановлен (stop → planned, файлы на месте): после сверки сделай begin заново.
ВНИМАНИЕ: это основной checkout пользователя (Unity Editor пользователя). Rebase, stash и переключение веток — только после явного подтверждения пользователя; Play Mode, открытые сцены и префабы пользователя не трогать. tasks/lobby-decoration/plan.json после task-handoff не закоммичен.
1. Зона и живое состояние:
   python -B -X utf8 F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/Tools/experts/awake.py F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/experts/level-design --repo .
2. Что делал прежний агент:
   python -B -X utf8 F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai/Tools/experts/session_handoff.py --worktree .
3. Сверь факты (переписка — не факт): git status и git diff --stat; tasks/lobby-decoration/Readme.md «Следующий шаг» и
   plan.json; хаб (Tools/agents/coordination.py status; inbox --owner level-design --unread); брокер
   (Tools/agents/editor-broker.py status — своя незакрытая аренда или тикет: штатно recover/finish до любых правок).
4. Запиши короткий handoff в «Следующий шаг» tasks/lobby-decoration/Readme.md: где остановился прежний агент, что
   незакоммичено, следующий шаг.
5. Обнови базу: чекпоинт локальных изменений (WIP-коммит в своей ветке или stash с уникальным тегом), git fetch,
   git rebase origin/dev, восстановить изменения; конфликты решаешь ты. После rebase в worktree есть experts/level-design/ и
   хук: следующие сессии — claude --agent level-design-expert. Новый маршрут: route-publish.
6. Продолжай план. Перед begin — zone-check --task lobby-decoration. Гейты AGENTS не снимаются: Unity — только в своей аренде;
   код/ассеты — приёмка пользователем до коммита; вливание — штатно. Сначала коротко скажи пользователю, где
   остановился прежний агент и с какого безопасного шага продолжаешь, затем начинай.
```
