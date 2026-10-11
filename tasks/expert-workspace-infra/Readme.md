# Рабочие места экспертов и awake

Дата: 2026-10-11. Владелец: expert-workspace-infra. Статус: завершена; развитие передано сопровождающему инфры.

Цель: эксперт — главная сессия-оркестратор с изолированной зоной и своим рабочим местом
`experts/<id>/` (AGENT.md, backlog.md, journal/). `awake` собирает стартовый пакет: статичную роль и
архитектурную карту плюс живое состояние задач, контрактов, межзадачных зависимостей, inbox, git и
backlog. Пакет не устаревает и ничего не разрешает. Проект: [design.md](design.md) v2.

Результат (в origin/dev):
- `Tools/experts/awake.py` + тесты (18/18, `python -m unittest discover -s Tools/experts/tests`),
  `Tools/experts/session_handoff.py` (сводка последних сессий Codex/Claude worktree).
- 11 экспертов с рабочими местами, общий `experts/COMMON.md`, реестр зон v2 (`owns`, zone-lint: совладения 2, мёртвых 0).
- Хук SessionStart (`.claude/settings.json`) доставляет пакет блоками role/tasks/contracts/status/backlog;
  профили `.claude/agents/*-expert.md`, `.codex/agents/*-expert.toml` — минимальные адаптеры.
- [Процедура нового эксперта](new-expert.md), [карта экспертов](expert-map.md), шаблоны `templates/`.
- Все 10 продуктовых задач переданы экспертам ([итог миграции](migration-plan.md#итог-миграции-2026-10-11));
  промпты приёма — `handoff/takeover-<task>.md`; в 9 worktree экспертов `.claude/settings.local.json` выбирает агента.

Следующий шаг: нет. Развитие инфраструктуры экспертов (Codex-хук, zone-check в begin, автонастройка
новых worktree, сопровождение awake) передано задаче agent-coordination-protocol — событие хаба 3465 (infra_handover).
Приём задач экспертами — по промптам `handoff/`, запускает пользователь.

Подробности: [Details.md](Details.md); план: [plan.json](plan.json).
