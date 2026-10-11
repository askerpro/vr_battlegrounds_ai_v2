Обновлено 2026-10-11. Кандидаты вне хаба; порядок — приоритет. `tasks/bots-fix/` — в worktree владельца.

1. `grip-fit` — support-кисть вне цевья, Rifle вверх (MEF 23.58°, Cyborg 36.25°), хваты Cyborg; отложено пользователем — `tasks/bots-fix/Readme.md` «Блокеры и следующие действия», `grip-contact-plan.md`.
2. `stand-coverage` — все входы/клипы, T02/T04/T09, V01/L01 Unsupported, эталоны поз, защита; выстрел NPC в Unity не подтверждён — `Docs/tasks/bot-combat-stand.md` «Наблюдения», `bots-validation-stand-plan.md` «Этапы реализации» 3–5.
3. `stand-launch-request` — содержимое стенда после ухода `BotCombatStandEditor` от записи EditorBuildSettings/Bootstrap/registry (механизм — launch-infra `stands-migration`), порядок с clip-cutover не согласован — `tasks/vr-test-stand/Details.md`.
4. `team-body-catalog` — Team1 только MEF Black, Team2 обычный MEF, Cyborg нет в TeamRegistry; с gameplay/avatar-ik — `tasks/bots-fix/diagnosis/mef-team-catalog-coverage.md`.
5. `bots-quest-perf` — CPU/GC Quest при N ботах, синхронная запечка NavMesh, нечитаемые MeshCollider в плеере; с performance — `Docs/tasks/T-48-bots-match.md` «Что не проверено (2026-10-01)».
6. `docs-sync` — после clip-cutover обновить прежний гибрид в `Docs/gameplay.md` «Матч с ботами (T-48)», `Docs/tasks/bots-blaze-integration.md`, строках ботов `Docs/game-manager.md`.
7. `low-crouch` — присед ботов после приёмки native legs (avatar-ik) — `Docs/tasks/bots-blaze-integration.md` «Приёмка».
8. `heavy-bot` — судьба Heavy, исключённого из клиповой схемы — `tasks/bots-fix/Details.md` «Цель и мотивация».
