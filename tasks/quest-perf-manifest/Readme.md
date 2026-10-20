# Манифест производительности Quest для карт

2026-10-20. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/quest-perf-manifest` (схема «задача = агент»).

## Цель

Канонический документ Docs/quest-performance-manifest.md: правила производительности Quest (бюджеты, шейдеры, текстуры, свет, LOD, отражения, листва, приём ПК-паков, метод замера) с id, ценой и доказательством; ссылки из AGENTS.md и AGENT.md экспертов; соответствие правилам аудитора ассетов.

## Этапы

Этапы и области записи — в [plan.json](plan.json). Этап `work` пишет манифест, ссылки в `AGENTS.md`, профили и журналы
зон performance и level-design, обратные ссылки в `Docs/level-design/` (план перерегистрирован, rev 2).

## План

- [x] Собрать бюджеты и правила (experts/performance, Docs/perf-stress-test.md, замеры scene-detail)
- [x] Docs/quest-performance-manifest.md: правила QPM, условия цен, статус инструментов, приём пака, метод замера
- [x] Ссылки из AGENTS.md, профилей и Docs/level-design; owns performance (манифест, аудитор)
- [x] Сопоставление с 13 правилами аудитора (влит в origin/dev)
- [ ] Ревью зон performance и level-design (раунд 3), merge в origin/dev, task-close

## Статус

2026-10-20: формат подтверждён пользователем. Ревью раунд 2: level-design approve, performance changes (reviewed_sha
без сверки карты) — исправлено, ждёт раунда 3. Цены финального A/B scene-detail дописать после её замера.
