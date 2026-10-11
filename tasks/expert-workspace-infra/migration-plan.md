# Миграция работающих агентов на экспертов

Дата: 2026-10-11. База: origin/dev `107d4a9f` (эксперты, awake, хук). Режим координации: off.
Карта зон — [expert-map.md](expert-map.md).

## Принципы

- Задача, её папка `tasks/<task-id>/`, worktree, ветка и owner в хабе **не меняются**. Меняется только сессия:
  вместо безымянного агента в том же worktree работает эксперт (`claude --agent <id>-expert`).
- Переход — только в безопасной точке: нет своей Unity-аренды и незавершённой операции, этап не на середине
  мутации. Лучше всего — на границе этапа (finish/verified/merged) или на паузе, которую агент сам объявил.
- Работа в полёте не теряется: незакоммиченное остаётся в worktree, rebase — штатно с сохранением
  tracked/staged/untracked (AGENTS.md). Чужой worktree никто, кроме его владельца, не трогает.
- Один эксперт — несколько задач: на каждый активный worktree своя сессия того же профиля; awake показывает
  все задачи эксперта, сессия пишет только в свой worktree.

## Шаги для одной задачи

1. **Текущая сессия** доводит операцию до безопасной точки, отпускает аренду (finish/receive), обновляет
   «Следующий шаг» в `tasks/<task-id>/Readme.md` и пишет одну запись `experts/<id>/journal/YYYY-MM-DD-handoff-<task-id>.md`:
   где остановились, что в незакоммиченном, открытые вопросы. Затем завершается.
2. **Обновить базу worktree**: `git fetch` → `git rebase origin/dev` (≥ `107d4a9f`) с сохранением локальных
   изменений; при конфликте — решает владелец worktree.
3. **Запустить эксперта** в том же worktree: Claude — `claude --agent <id>-expert` (пакет приходит хуком);
   Codex — новая сессия с первой фразой «Работай как `<id>-expert`, выполни
   `python -B -X utf8 Tools/experts/awake.py experts/<id>`».
4. **Проверка эксперта**: в контексте есть блоки `role/tasks/contracts/status/backlog`; inbox задачи доступен
   (owner-check проходит из этого worktree); эксперт пересказывает «Следующий шаг» из handoff.
5. Если в хабе у задачи закреплён `session_id` (сейчас: avatar-renderer-regression, legs-ik), в режиме off это
   не блокирует; при enforced — передача сессии через сопровождающего протокола.

## Волны

Состояние на 2026-10-11 (ahead/behind относительно origin/dev, незакоммиченные пути):

| Задача | Эксперт | Worktree | Этап сейчас | ahead/behind | dirty | Волна |
|---|---|---|---|---|---|---|
| vr-test-stand | launch-infra | `vr-test-stand` | нет активных | 0/4 | 0 | 0 |
| map-runtime-bootstrap | launch-infra | `map-runtime-bootstrap` | нет активных | 0/20 | 0 | 0 |
| avatar-renderer-regression | avatar-ik | `2cf1` | ждёт приёмки/верифицирован | 1/33 | 0 | 0 |
| legs-ik | avatar-ik | `legs-ik` | нет активных (wip) | 3/45 | 3 | 1 |
| hand-rig-quality | avatar-grip | `hands-rig-quality` | mef-grip-audit running | 0/4 | 3 | 1 |
| haptics-system | manipulation | `haptics` | manipulation-kb running | 0/11 | 6 | 1 |
| arsenal-generator | gameplay | `arsenal-generator` | planning running | 0/42 | 5 | 1 |
| weapon-system | weapon-system | `weapon-system-expert` | 6 этапов verified | 1/4 | 36 | 2 |
| bots-fix | bots | `bots-fix` | armed-development running | 0/2 | 72 | 2 |
| map-runtime-bootstrap | launch-infra | `F:/CodexWorktrees/map-runtime-bootstrap/Vr_Battlegrounds_ai` | Claude-агент готовился начать series-smoke-e2e (ждал порты checkout — уже влиты); сценарий согласовать с gameplay |
| lobby-decoration | level-design | **основной checkout** `F:/UnityProjects` | implementation running | 1/21 | 65 | 2 |

- **Волна 0 — сразу.** Чистые worktree без активной работы; переход почти бесплатный. Проверяет процедуру.
- **Волна 1 — на ближайшей границе этапа.** Небольшие незакоммиченные изменения. Для haptics-system в том же
  переходе — замена профиля manipulation (запрос 3272, текст `handoff/manipulation-expert.md`).
- **Волна 2 — после завершения текущего этапа.** Много работы в полёте; сначала чекпоинт/закрытие этапа.
  lobby-decoration работает прямо в основном checkout пользователя: перед переходом согласовать с
  пользователем, переносить ли его в отдельный worktree (по AGENTS основной checkout — пользователя).

Эксперты без задач (network, ui, performance) сессий не получают: первая задача из их backlog — новый
`tasks/<task-id>/`, регистрация в хабе и отдельный worktree по штатному маршруту.

## Как разослать

Каждой задаче — адресное сообщение в inbox (`kind: expert_migration`) с её экспертом, волной и шагами 1–4;
сигнал «прочитай inbox» сессии передаёт пользователь (native-адреса сессий не определены). Сессия отвечает в
inbox `expert-workspace-infra`: точка перехода достигнута / handoff записан. Новую сессию эксперта запускает
пользователь. Итог по каждой задаче — строка в Details этой задачи.

## Приём работы без передачи (Codex без лимитов, остановленный Claude)

Принцип (решение пользователя 2026-10-11): работа каждого прежнего агента переходит к эксперту зоны по карте — для всех задач, независимо от клиента и состояния прежней сессии.

Прежняя сессия не может записать handoff — эксперт восстанавливает его сам по журналам Codex и Claude Code (`session_handoff.py`). Готовые промпты — `handoff/takeover-<task-id>.md`. До rebase в worktree нет
`Tools/experts` и профилей экспертов, поэтому первая сессия — обычный `claude` в worktree задачи с промптом
ниже, инструменты берутся из `F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai` (далее `$EW`).
После rebase следующие сессии запускаются `claude --agent <id>-expert`.

| Задача | Эксперт `<id>` | Worktree (cd) | Где остановился Codex (по журналу) |
|---|---|---|---|
| bots-fix | bots | `F:/CodexWorktrees/bots-fix/Vr_Battlegrounds_ai` | вооружённые runtime-сценарии NPC, первые попадания есть, PASS нет |
| hand-rig-quality | avatar-grip | `F:/CodexWorktrees/hands-rig-quality/Vr_Battlegrounds_ai` | этап mef-grip-audit |
| weapon-system | weapon-system | `F:/CodexWorktrees/weapon-system-expert/Vr_Battlegrounds_ai` | цепочка use-context (6 этапов verified) |
| vr-test-stand | launch-infra | `F:/CodexWorktrees/vr-test-stand/Vr_Battlegrounds_ai` | активных этапов нет |
| avatar-renderer-regression | avatar-ik | `F:/CodexWorktrees/2cf1/Vr_Battlegrounds_ai` | ждёт приёмки / архитектура рендереров |
| legs-ik | avatar-ik | `F:/CodexWorktrees/legs-ik/Vr_Battlegrounds_ai` | Claude; wip-ветка, этапы planned, pose-set ждёт pocket-removal |
| haptics-system | manipulation | `F:/CodexWorktrees/haptics/Vr_Battlegrounds_ai` | Claude; manipulation-kb running; заменить профиль manipulation |
| arsenal-generator | gameplay | `F:/CodexWorktrees/arsenal-generator/Vr_Battlegrounds_ai` | Claude; planning running; decorations согласовать с level-design |
| lobby-decoration | level-design | `F:/UnityProjects/Vr_Battlegrounds_ai` (**основной checkout**) | implementation; rebase — только с согласия пользователя |

Промпт (подставить `<id>`, `<task-id>`):

```text
Ты — эксперт `<id>` VR Battlegrounds. В этом worktree работал Codex-агент задачи `<task-id>`; у него
закончились лимиты посреди работы, handoff он не оставил. Прими задачу и доделывай по его плану.
Общайся по-русски, правила — AGENTS.md. EW=F:/CodexWorktrees/expert-workspace-infra/Vr_Battlegrounds_ai
1. Зона и живое состояние: python -B -X utf8 $EW/Tools/experts/awake.py $EW/experts/<id> --repo .
2. Что делал Codex: python -B -X utf8 $EW/Tools/experts/session_handoff.py --worktree .
3. Сверь факты, переписка Codex — не факт: git status и diff --stat; tasks/<task-id>/Readme.md «Следующий шаг»
   и plan.json; хаб (Tools/agents/coordination.py status, inbox задачи); брокер
   (Tools/agents/editor-broker.py status — своя незакрытая аренда/тикет: штатно recover/finish до любых правок).
4. Запиши короткий handoff в «Следующий шаг» tasks/<task-id>/Readme.md: где остановился Codex, что
   незакоммичено, следующий шаг.
5. Обнови базу: чекпоинт локальных изменений (WIP-коммит в своей ветке или stash с уникальным тегом),
   git fetch, git rebase origin/dev, восстановить изменения; конфликты решаешь ты. Основной checkout
   (F:/UnityProjects) — только после подтверждения пользователя. После rebase в worktree есть experts/<id>/
   и хук: следующие сессии — claude --agent <id>-expert.
6. Продолжай план. Гейты AGENTS не снимаются: Unity — только в своей аренде; код/ассеты — приёмка
   пользователем до коммита; вливание — штатно. Сначала коротко скажи пользователю, где остановился Codex и
   с какого безопасного шага продолжаешь, затем начинай.
```

## Постоянные worktree экспертов (вариант B)

Этап 1 выпущен сопровождающим (событие 3312, runtime b1d115c): owner задачи = эксперт; несколько задач одного
owner в одном worktree; `task-handoff` (CAS) меняет owner и переносит worktree, сохраняя этапы, контракты, ACK и
историю; агрегированный inbox `inbox --owner <expert>`; адресат `owner:<id>`; `zone-check`/`zone-lint` — только
чтение. Подробно — agent-infra `docs/maintenance-guide.md`, «Эксперт: постоянный worktree и task-handoff».

Миграция одной задачи (делает эксперт/оператор миграции, не сопровождающий; по одной задаче):

0. Безопасная точка: running-этап доведён до verify/stop; тикеты брокера задачи DONE/CANCELLED; сессии в
   worktree нет (move не сработает, пока каталог держат Unity, IDE или терминал); локальные правки сохранены.
1. Из старого worktree: `coordination.py register --document tasks/<task>/Readme.md --expected-revision <N>` —
   записывает идентичность worktree (ревизия растёт; при needs контрактов затем contract-ack).
2. `mkdir F:/CodexWorktrees/<expert>`; `git -C F:/UnityProjects/Vr_Battlegrounds_ai worktree move
   F:/CodexWorktrees/<old>/Vr_Battlegrounds_ai F:/CodexWorktrees/<expert>/Vr_Battlegrounds_ai`.
3. Из нового пути: `coordination.py --repo F:/CodexWorktrees/<expert>/Vr_Battlegrounds_ai task-handoff --task <task>
   --owner <старый owner> --expected-revision <N> --to-owner <expert> [--allow-unmerged]`.
4. Проверить `status` (owner, worktree, ревизия N+1, этапы); закоммитить изменённый `tasks/<task>/plan.json`;
   в `experts/<expert>/expert.json` owner уже = id. Новая сессия эксперта: `route-publish`, затем
   `inbox --owner <expert> --unread`; MCP initialize из нового пути.

Уроки пилота launch-infra ← vr-test-stand (2026-10-11, выполнен, rev 29): каталог держат не только сессии, но и процессы приложения Codex с cwd в worktree (MCP-прокси, node_repl, node, uvx/blender-mcp) и сам `codex.exe app-server` (наблюдатель каталога) — перед move закрыть приложение Codex; поиск держателей — по cwd процессов, не по командной строке. `task-handoff` запускать launcher-ом из нового worktree и с явным `--to-worktree`. Пути evidence влитых этапов в хабе остаются старыми (сообщено сопровождающему, 3324).

Пары и порядок: launch-infra ← vr-test-stand (выполнено); avatar-ik ← legs-ik; затем на
границе этапа: bots ← bots-fix (running armed-development), avatar-grip ← hand-rig-quality (running
mef-grip-audit), manipulation ← haptics-system (running manipulation-kb), gameplay ← arsenal-generator (running
planning), weapon-system ← weapon-system (6 verified → `--allow-unmerged`). Вторая задача в уже занятый worktree
(map-runtime-bootstrap → launch-infra, avatar-renderer-regression → avatar-ik): ветку задачи сначала освободить в
старом worktree, затем checkout в worktree эксперта и `task-handoff --to-worktree`. network, performance, ui:
минимальная задача-владелец и worktree эксперта — до подключения проверки зон к begin.

## Итог миграции 2026-10-11

Все 10 продуктовых задач переданы экспертам (task-handoff, owner = эксперт):

| Эксперт | Задача | Worktree | Ревизия |
|---|---|---|---|
| launch-infra | vr-test-stand | F:/CodexWorktrees/launch-infra (постоянный) | 29 |
| launch-infra | map-runtime-bootstrap | F:/CodexWorktrees/map-runtime-bootstrap (прежний) | 16 |
| avatar-ik | legs-ik | F:/CodexWorktrees/avatar-ik (постоянный) | 7 |
| avatar-ik | avatar-renderer-regression | F:/CodexWorktrees/2cf1 (прежний) | 13 |
| avatar-grip | hand-rig-quality | F:/CodexWorktrees/hands-rig-quality (прежний: reports держит системный процесс) | 23 |
| bots | bots-fix | F:/CodexWorktrees/bots (постоянный) | 23 |
| manipulation | haptics-system | F:/CodexWorktrees/manipulation (постоянный) | 14 |
| gameplay | arsenal-generator | F:/CodexWorktrees/gameplay (постоянный) | 17 |
| weapon-system | weapon-system | F:/CodexWorktrees/weapon-system (постоянный) | 39 |
| level-design | lobby-decoration | основной checkout F:/UnityProjects (owner-only; plan.json не закоммичен) | 9 |

Остановлены при миграции (stop → planned, файлы на месте; эксперт делает begin заново): bots-fix/armed-development,
hand-rig-quality/mef-grip-audit, haptics-system/manipulation-kb, arsenal-generator/planning,
lobby-decoration/implementation. Порядок: handoff owner из старого worktree (идентичность пишется без register) →
git worktree move → handoff --to-worktree из нового пути → commit plan.json. Перед move остановлены процессы прежнего
приложения Codex (app-server 8432 и дочерние) и осиротевший MCP-прокси arsenal-generator — с разрешения пользователя.
Промпты приёма обновлены под новые пути: handoff/takeover-<task>.md.

## Риски

- Rebase с большим dirty (bots-fix 72, lobby-decoration 65, weapon-system 36) — только владельцем, после чекпоинта.
- Эксперт видит в awake чужие этапы в своих путях: на переходе не начинать новых правок до сверки пересечений
  (bots-fix пишет в зоны network, avatar-ik, manipulation, weapon-system, gameplay).
- Codex не получает пакет хуком: если первая фраза не дана, эксперт стартует без контекста.
