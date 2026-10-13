Обновлено 2026-10-13. Кандидаты вне хаба; порядок — приоритет. MRB = `tasks/map-runtime-bootstrap/`.
Решение пользователя 2026-10-13: сначала то, что мешает игровой логике и чего ждут другие; `arsenal-generator`
(декор, этапы planning…decorations) отложен.

1. `series-smoke` (+ бывший `known-noise-content`) — смок-тест серии матчей передан от launch-infra (5863/5869): сценарий
   `SeriesSmoke`, критерии и шум сценария — мои; каркас `e2e-smoke-kit` (барьер шагов с data, роль процесса, журнал
   UTC, `-Arg`, abort) — launch-infra после `e2e-log-verdict` (проект: `tasks/quest-perf-pipeline/Details.md`
   «Каркас для смок-теста серии e2e-smoke-kit», ревью 5880/5883, network 5888). План сценария и шум — MRB Details
   «Этапы series-smoke…» и «Известные шумы тестов и харнесса». Завести этап и сообщить id launch-infra.
2. `direct-play-route-verify` — сверить «Play из сцены карты идёт Offline → Lobby → карта» с PlayLaunch native-play (`PlayLaunch.cs` зовёт ServerStartupRoute) и `DebugOrchestrator.TryAutoLoadMap`, с launch-infra — MRB Details «Известные ограничения».
3. `game-check-multiplayer` — два клиента, поздний вход, шлем, клиент долго в лобби, отмена загрузки при живом сервере — MRB Readme «Следующий шаг» п.4, Details «Не проверено».
4. `map-bootstrap-cleanup` — пути отчётов `MapBootstrapMigration.cs:38`, `MapRunPreflight.cs:72`; комментарий `MapRunContractTests.cs:15`; ветки `codex/tmp/map-bootstrap-*` — MRB Readme п.2.
5. `map-bootstrap-user-fixes` — правки по проверке пользователя 2026-10-07, список ждёт пользователя — MRB Readme п.3.
6. `generated-network-items` — сетевые предметы при перестановках descriptor/снимка и позднем приходе — MRB Details «Не проверено».
7. `slot-item-scene-teardown` — чужой предмет в слоте уничтожается со сценой — MRB «Известные ограничения».
8. `station-uxr-id-canvas` — world-space канвас на станции даст ложный отказ по пустому UXR id — MRB «Известные ограничения».
9. `team-lifecycle-warnings` — teamID0 missing / PlayersManager duplicates из аудита383 — `tasks/vr-test-stand/Details.md` «Финальная проверка383».
10. `minplayers-bots` — определить и записать, считаются ли боты в minPlayers (правило твоё по карте ред. 2) — `tasks/vr-test-stand/Details.md`, поиск `minPlayers`.
11. `arsenal-legacy-docs-delete` — удалить `Docs/tasks/arsenal-generator-design.md` и `-plan.md` по запросу infra (inbox #1140, механизм `deletes`). Сначала: дополнить `tasks/arsenal-generator/plan.md` (10 КБ против старых 45 КБ) нужным из старого плана; ссылки в `Docs/README.md:108,218,753` (общий индекс — нужен владелец Docs) и `Docs/UltimateXR/sdk-patches.md:1802` (исторический журнал). Затем cleanup-этап в плане arsenal-generator с точными `deletes`.
