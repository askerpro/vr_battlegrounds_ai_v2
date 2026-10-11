# HUD перенесён на наручные часы с нотификациями (T-46), старый HUD удалён

Табло `WristDisplay`/`RoundClock` — коммит `e461dcf379b07e1bc99498563e3ae9754191d6f0` (2026-09-28);
часы и нотификации T-46 — в сводном коммите `4288d1f77b95e31b7b8093f47f0c2cfcb79a0475` (2026-10-02,
T-38/39/45/46/47/48). Удалены `PlayerHUDManager`, `HUDWidget_*`, `EliminationHUD.prefab`, `HUDInjector`.
Единая точка нотификаций — `WatchNotifications.Post`, очередь `WatchNotificationQueue`, тексты
`WatchNotificationTexts`, переводчик событий `WatchGameEvents`; только локально, без сети.

Проверено: 2026-10-01 вне Unity — компиляция сборок и чистые NUnit (`WatchNotificationQueueTests` 13,
`WatchNotificationTextsTests` 28, `OldHudRemovedTests` 2), красные до правки 34; 2026-10-02 в Unity —
целевой прогон экономики, ботов, экранов и часов 168/168 PASS, AndroidCompileGate PASS; полный прогон
1683/1762, 79 отказов других групп. Окружение (Unity/ОС) — unknown.
Не проверено: вид часов, звук и вибрация в шлеме; два физических клиента. Киборг был без часов на
2026-10-01 — текущий статус unknown (backlog `watch-headset`).

Источники: `Docs/tasks/T-46-wrist-watch-hud.md` (шапка верификации, «Что стало», «Что не проверено»);
`Docs/tasks/verification-2026-10-02.md`; `git show e461dcf37`, `git show 4288d1f77`.
