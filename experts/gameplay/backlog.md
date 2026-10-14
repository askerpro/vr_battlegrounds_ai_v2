Обновлено 2026-10-14 (передача Claude → Codex). Кандидаты вне хаба; порядок — приоритет. MRB = `tasks/map-runtime-bootstrap/`.
Решение пользователя 2026-10-13: сначала то, что мешает игровой логике и чего ждут другие; `arsenal-generator`
(декор, этапы planning…decorations) отложен.

## Текущая очередь задач хаба (порядок по приоритету пользователя)

1. `catalog-fingerprint/preview-loaded-scene` — Unity-прогон и приёмка (ветка `claude/preview-loaded-scene`, Readme задачи).
2. `arsenal-unload-teardown/accepted-tests`, затем `catalog-fingerprint/accepted-tests`.
3. `series-smoke` — когда launch-infra вольёт `e2e-smoke-kit` (кандидат п.1 ниже).

## Обещания другим агентам (кому — что — событие)

- launch-infra (quest-perf-pipeline): после вливания `e2e-smoke-kit` завести задачу `series-smoke` и прислать id —
  тогда они снимут `map-runtime-bootstrap/series-smoke-e2e` со своего плана (5869, 6403; API kit принят 6403, их 6408).
- launch-infra: ждём их прогон map-run-startup-route с полным вердиктом по логам после вливания arsenal-unload-teardown
  и снятие дефекта `anchored-ignore-collision-prefab` из known-noise.json (просьба 6367).
- launch-infra: после вливания `preview-loaded-scene` сообщить, что обход «открыть пустую сцену перед сборкой» больше не
  нужен (5483).
- level-design (lobby-decoration): если до их вливания снова изменится сцена лобби — сверить новую строку
  ContentFingerprint лобби по расчёту v2 (zone-ack 6060 выдан на текущую строку 2380eef1…).
- manipulation: null-проверка `NetworkImplementation` в патче 75 — их малый этап (6204), с нашей стороны ничего.

## Кандидаты

1. `series-smoke` (+ бывший `known-noise-content`) — смок-тест серии матчей передан от launch-infra (5863/5869): сценарий
   `SeriesSmoke`, критерии и шум сценария — мои; каркас `e2e-smoke-kit` (барьер шагов с data, роль процесса, журнал
   UTC, `-Arg`, abort) — launch-infra после `e2e-log-verdict` (проект: `tasks/quest-perf-pipeline/Details.md`
   «Каркас для смок-теста серии e2e-smoke-kit», ревью 5880/5883, network 5888; API для авторов — Docs/testing.md
   «Мультипроцессный сценарий: барьер, шаги, роли»). План сценария и шум — MRB Details «Этапы series-smoke…» и
   «Известные шумы тестов и харнесса». Завести задачу и сообщить id launch-infra.
2. `direct-play-route-verify` — сверить «Play из сцены карты идёт Offline → Lobby → карта» с PlayLaunch native-play (`PlayLaunch.cs` зовёт ServerStartupRoute) и `DebugOrchestrator.TryAutoLoadMap`, с launch-infra — MRB Details «Известные ограничения».
3. `game-check-multiplayer` — два клиента, поздний вход, шлем, клиент долго в лобби, отмена загрузки при живом сервере — MRB Readme «Следующий шаг» п.4, Details «Не проверено».
4. `map-bootstrap-cleanup` — пути отчётов `MapBootstrapMigration.cs:38`, `MapRunPreflight.cs:72`; комментарий `MapRunContractTests.cs:15`; ветки `codex/tmp/map-bootstrap-*` — MRB Readme п.2.
5. `map-bootstrap-user-fixes` — правки по проверке пользователя 2026-10-07, список ждёт пользователя — MRB Readme п.3.
6. `generated-network-items` — сетевые предметы при перестановках descriptor/снимка и позднем приходе — MRB Details «Не проверено».
7. `slot-item-scene-teardown` — чужой предмет в слоте уничтожается со сценой — MRB «Известные ограничения».
8. `station-uxr-id-canvas` — world-space канвас на станции даст ложный отказ по пустому UXR id — MRB «Известные ограничения».
9. `team-lifecycle-warnings` — teamID0 missing / PlayersManager duplicates из аудита383 — `tasks/vr-test-stand/Details.md` «Финальная проверка383».
10. `minplayers-bots` — определить и записать, считаются ли боты в minPlayers (правило твоё по карте ред. 2) — `tasks/vr-test-stand/Details.md`, поиск `minPlayers`. Факт 2026-10-13 (ответ bots 6030): в автодоборе и составах бот = игрок; записать правило в Docs/gameplay.md вместе с п.12.
12. `maxplayers-enforce` — `MapData.maxPlayers` (10 на TestMap1–3/Lobby, 8 на ServiceYard/ReferenceMap04) только объявлен (MapData.cs:43), нигде не проверяется: решить, что происходит с N+1-м участником (зритель/отказ входа/очередь), зрители (Spectator) вне лимита; связь с `BotMatchPlan.MaxTeamSize=4` (зона bots, стены арсенала T-45) — вопрос bots 6028.
11. `arsenal-legacy-docs-delete` — удалить `Docs/tasks/arsenal-generator-design.md` и `-plan.md` по запросу infra (inbox #1140, механизм `deletes`). Сначала: дополнить `tasks/arsenal-generator/plan.md` (10 КБ против старых 45 КБ) нужным из старого плана; ссылки в `Docs/README.md:108,218,753` (общий индекс — нужен владелец Docs) и `Docs/UltimateXR/sdk-patches.md:1802` (исторический журнал). Затем cleanup-этап в плане arsenal-generator с точными `deletes`.
