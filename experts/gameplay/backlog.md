Обновлено 2026-10-11. Кандидаты вне хаба; порядок — приоритет. MRB = `tasks/map-runtime-bootstrap/`.

1. `direct-play-route-verify` — сверить «Play из сцены карты идёт Offline → Lobby → карта» с PlayLaunch native-play (`PlayLaunch.cs` зовёт ServerStartupRoute) и `DebugOrchestrator.TryAutoLoadMap`, с launch-infra — MRB Details «Известные ограничения».
2. `known-noise-content` — список шума `series-smoke-e2e`: причина и владелец каждого пункта (механизм — launch-infra) — MRB Details «Известные шумы тестов и харнесса».
3. `game-check-multiplayer` — два клиента, поздний вход, шлем, клиент долго в лобби, отмена загрузки при живом сервере — MRB Readme «Следующий шаг» п.4, Details «Не проверено».
4. `map-bootstrap-cleanup` — пути отчётов `MapBootstrapMigration.cs:38`, `MapRunPreflight.cs:72`; комментарий `MapRunContractTests.cs:15`; ветки `codex/tmp/map-bootstrap-*` — MRB Readme п.2.
5. `map-bootstrap-user-fixes` — правки по проверке пользователя 2026-10-07, список ждёт пользователя — MRB Readme п.3.
6. `generated-network-items` — сетевые предметы при перестановках descriptor/снимка и позднем приходе — MRB Details «Не проверено».
7. `slot-item-scene-teardown` — чужой предмет в слоте уничтожается со сценой — MRB «Известные ограничения».
8. `station-uxr-id-canvas` — world-space канвас на станции даст ложный отказ по пустому UXR id — MRB «Известные ограничения».
9. `team-lifecycle-warnings` — teamID0 missing / PlayersManager duplicates из аудита383 — `tasks/vr-test-stand/Details.md` «Финальная проверка383».
10. `minplayers-bots` — определить и записать, считаются ли боты в minPlayers (правило твоё по карте ред. 2) — `tasks/vr-test-stand/Details.md`, поиск `minPlayers`.
