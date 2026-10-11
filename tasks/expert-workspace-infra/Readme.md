# Рабочие места экспертов и awake

Дата: 2026-10-11. Владелец: expert-workspace-infra.

Цель: эксперт — главная сессия-оркестратор с изолированной зоной и своим рабочим местом
`experts/<id>/` (AGENT.md, backlog.md, journal/). `awake` собирает стартовый пакет: статичную роль и
архитектурную карту плюс живое состояние задач, контрактов, межзадачных зависимостей, inbox, git и
backlog. Пакет не устаревает и ничего не разрешает. Проект: [design.md](design.md) v2.

Область: `Tools/experts/awake.py` и тесты, шаблоны `templates/`, [карта экспертов](expert-map.md),
[процедура нового эксперта](new-expert.md), рабочие места `experts/<id>/` 11 экспертов, адаптеры новых
профилей `.claude/agents/*-expert.md` и `.codex/agents/*-expert.toml`. Без игровой логики, Unity и правок
существующих профилей manipulation/weapon-system/avatar-grip.

Текущее состояние: 11 экспертов с рабочими местами, общий `experts/COMMON.md`, хук SessionStart доставляет
полный пакет awake блоками role/tasks/contracts/status/backlog; профили — минимальные адаптеры. Тесты 16/16.
Пользователь поручил интеграцию 2026-10-11. Профиль manipulation меняет haptics-system (запрос 3272).

Следующий шаг: после интеграции — уведомить владельцев затронутых профилей; Codex-хук не исследован.

Подробности: [Details.md](Details.md); план: [plan.json](plan.json).
