Обновлено 2026-10-15 (Codex, inbox разобран). Кандидаты вне хаба; порядок — приоритет. MRB = `tasks/map-runtime-bootstrap/`.
Решение пользователя 2026-10-13: сначала то, что мешает игровой логике и чего ждут другие; `arsenal-generator`
(декор, этапы planning…decorations) отложен.

## Текущая очередь задач хаба (порядок по приоритету пользователя)

1. `catalog-fingerprint/preview-loaded-scene` — принят и FF-влит origin/dev97a320c88, база опубликована639 DONE;
   Unity 3/3 preflight PASS, Windows E2E build PASS. Побочный PC.asset не принят; зависимые владельцы уведомлены.
2. `arsenal-unload-teardown/accepted-tests` — принят пользователем и FF-влит origin/dev09d514cc2; stage_merged7553.
   Unity668:8/8 PASS, cleanup без новых managers/live/previews. Документальное закрытие1812dc5bf;
   публикация базы693 DONE. Прежние640/653 остаются без NUnit verdict.
   `catalog-fingerprint/accepted-tests` —684:5PASS/0FAIL/2SKIP, terminal Result сохранён до finish; DONE.
   Ограничение roots0 заменено проверкой сохранности исходных root IDs, compile-only284refs PASS.
   Новый source9ec0d4a6/inputc7ee905d0580: Unity6947/7PASS, failed/skipped0; finish DONE с пустым diff.
   Каталог принят пользователем и FF-влит origin/dev7fbacc825; все этапы закрыты. Старый676 остаётся UNKNOWN.
3. `series-smoke` — launch-infra7139 сообщил о влитом `e2e-smoke-kit` be120d2805; каркас готов по сообщению
   владельца. Кандидат п.1 ниже; отдельная задача ещё не заведена. Worker восстановлен сопровождающим (7257).

## Обещания другим агентам (кому — что — событие)

- launch-infra (quest-perf-pipeline): после вливания `e2e-smoke-kit` завести задачу `series-smoke` и прислать id —
  тогда они снимут `map-runtime-bootstrap/series-smoke-e2e` со своего плана (5869, 6403; API kit принят 6403, их 6408).
- launch-infra: прогон map-run-startup-route выполнен (6892): 14/14 strict GREEN, полные логи, дефектов/потерь нет.
  Снятие `anchored-ignore-collision-prefab` у владельца ещё не принято/не влито.
- launch-infra: о вливании `preview-loaded-scene` уведомлён; обход «открыть пустую сцену перед сборкой» больше не нужен.
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
12. `maxplayers-enforce` — `MapData.maxPlayers` (10 на MilitaryBase, Warehouse, BlockStand, Lobby, 8 на ServiceYard/ReferenceMap04) только объявлен (MapData.cs:43), нигде не проверяется: решить, что происходит с N+1-м участником (зритель/отказ входа/очередь), зрители (Spectator) вне лимита; связь с `BotMatchPlan.MaxTeamSize=4` (зона bots, стены арсенала T-45) — вопрос bots 6028.
11. `arsenal-legacy-docs-delete` — удалить `Docs/tasks/arsenal-generator-design.md` и `-plan.md` по запросу infra (inbox #1140, механизм `deletes`). Сначала: дополнить `tasks/arsenal-generator/plan.md` (10 КБ против старых 45 КБ) нужным из старого плана; ссылки в `Docs/README.md:108,218,753` (общий индекс — нужен владелец Docs) и `Docs/UltimateXR/sdk-patches.md:1802` (исторический журнал). Затем cleanup-этап в плане arsenal-generator с точными `deletes`.
