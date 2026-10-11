# Черновик владения рендерерами и exception-safe state sync подготовлен, реализация не разрешена

`avatar-renderer-regression/architecture-planning`: verified `7e4a8cf371f9399194071c0bd2aad027177e6f02`
(в checkout задачи, не в origin/dev). Статус документа — DRAFT, требует просмотра пользователя. Два
независимых направления: A — явный состав тела и один контракт записи/проверки для всех
editor-сборщиков (зона avatar-ik); B — отказ синхронизируемой операции закрывает её frames и не
публикует событие (зона network). B не ждёт A.

Проверено: только ревью документа (stage verified); код не менялся. Решения пользователя — unknown.

Источники: `F:/CodexWorktrees/2cf1/Vr_Battlegrounds_ai/tasks/avatar-renderer-regression/architecture/Design.md`
(«Два независимых результата»), `OwnershipPlan.md`, `StateSyncPlan.md`; хаб (stage architecture-planning).
