Обновлено 2026-10-11. Кандидаты вне хаба; порядок — приоритет. VTS = `tasks/vr-test-stand/Details.md`,
MRB = `tasks/map-runtime-bootstrap/Details.md`,
PH = `tasks/vr-test-stand/pilot-handoff/snapshot.json`.

1. `xr-startup` — запуск без аппаратного XR для сетевых проверок, production-путь не трогать — PH, VTS.
2. `player-launch` — порты собранных плееров: `Run-E2E.ps1` без портов делит 7778/47777 с Play на worker (SocketException); один lifecycle с PlayLaunch — MRB.
3. `async-player-build` — `E2EPlayerBuilder.Run()` блокирует главный поток (таймаут MCP), пачкает `PC.asset`, `ProjectSettings.asset`, `MapRuntimeCatalog.asset`; ложное «устарел» — `Docs/testing.md` «Шероховатость: проверка свежести билда».
4. `xr-input` — один адаптер поз/кнопок/стиков, ревью avatar-ik — VTS §5, §7, §14.
5. `live-smoke-runner` — агрегация ошибок всех процессов и механизм известных шумов для `series-smoke-e2e` — MRB «Известные шумы тестов и харнесса», VTS «Финальная проверка383».
6. `stands-migration` — BotCombatStand сам правит EditorBuildSettings/Bootstrap/registry, ловит `StartupRoute.SceneNotLoadable`; перевести на общий запрос, порядок с bots-fix — VTS.
7. `worktree-isolation` — EditorPrefs профиля (`DebugBootstrapSettings.KeyPrefix`) и UXR focus-pause общие между worktree — VTS.
8. `headless-roles` — клиент `-nographics` считается сервером; общий запуск стресс-теста/сервера/e2e с performance — `Docs/testing.md` «Подводные камни, найденные прогоном».
9. `docs-sync` — `Docs/testing.md` «Уровень 3 · Multiplayer Play Mode» устарел (MPPM-теги вместо PlayLaunch); с gameplay сверить «Play из сцены карты идёт Offline → Lobby → карта» — MRB «Известные ограничения».
10. `interaction-scenarios`, `match-e2e`, `network-faults` — каркасная часть; содержание у manipulation/gameplay/network — PH.

Ограничения: native Oculus boundary, UPM/MPE -noUpm, licensing fallback, Git config lock — отдельные границы;
full suite, клик toolbar, Quest, synthetic XR/gameplay E2E не приняты; предупреждения TeamRegistry из аудита383 —
с gameplay; `Tools/e2e/Run-E2E.ps1`, `Tools/release/`, `ProjectSettings/VirtualProjectsConfig.json` вне writes
этапов — только новым этапом.
