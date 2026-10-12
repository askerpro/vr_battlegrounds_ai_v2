Обновлено 2026-10-11. Кандидаты вне хаба; порядок — приоритет. VTS = `tasks/vr-test-stand/Details.md`,
MRB = `tasks/map-runtime-bootstrap/Details.md`,
PH = `tasks/vr-test-stand/pilot-handoff/snapshot.json`.

Порядок пересмотрен 2026-10-11 — `journal/2026-10-11-e2e-path-priority.md`.
`player-launch` взят в хаб: `vr-test-stand/e2e-run-isolation`.

0. `quest-perf-pipeline` — взят в работу 2026-10-11 по поручению пользователя: автоматический конвейер на шлеме —
   сборка Android → adb install → запуск перф-теста → закрытие → сбор логов и перф-результатов. Конвейер, сборка и
   запуск — launch-infra; содержание замеров, метрики и бюджеты — performance (контракт формата результатов).
   Блокер сборки 2026-10-12: Android-сборка из редактора показывает модальное «Unsupported Input Handling on
   Android» (Active Input Handling = Both, ключ opt-out `AndroidInputHandlingWarning`) — worker простоял ~1 ч.
   Симптом закрывает этап `quest-build` (opt-out перед сборкой + предупреждение в отчёте); сопровождающему —
   infra_issue 4176 (детект модальных окон в брокере).
0a. `input-handling-new-only` — корень: перейти на Input System only. В рантайме сторонних пакетов есть legacy
   `Input`: UXR `UxrGamepadInput`/`UxrKeyboardInput`, FImpossible `LegsAnimator`, Mirror `GUIConsole`/
   `RemoteStatistics`/`ToggleHotkey`, KINEMATION `KShooterMenu`/`TacticalShooterPlayer`. Нужен аудит путей
   вызова на Quest с avatar-ik (ввод, ноги) и manipulation; `ProjectSettings` — моя зона, решение — совместное.
1. `live-smoke-runner` → этап `e2e-log-verdict` — агрегация ошибок всех процессов и механизм известных шумов для `series-smoke-e2e` — MRB «Известные шумы тестов и харнесса», VTS «Финальная проверка383».
2. `async-player-build` — `E2EPlayerBuilder.Run()` блокирует главный поток (таймаут MCP), пачкает `PC.asset`, `ProjectSettings.asset`, `MapRuntimeCatalog.asset`; ложное «устарел» — `Docs/testing.md` «Шероховатость: проверка свежести билда». Диагностика 2026-10-11 (чтение кода): `PC.asset` — URP
   `ShaderBuildPreprocessor` под Standalone+Development, мусор, зона performance; `ProjectSettings.asset` — снимок
   Player Settings посреди сборки (XR `preloadedAssets`), мусор, моя зона (`GameBuilder.cs:181-203` восстанавливает,
   E2E-сборщик нет); `MapRuntimeCatalog.asset` — `MapCatalogBuildStep` перезапекает отпечаток намеренно, для Git шум,
   зона gameplay. E2E-сборщик без `XrBuildSettingsScope` — Oculus-лоадер в Windows-плеере (связь с `xr-startup`).
   Ложное «устарел»: `Run-E2E.ps1` берёт `Assets\Editor` в корни свежести + сравнение по mtime → штамп по
   содержимому (`build-stamp.json`, SHA-256 runtime-исходников). Неблокирующая сборка: основной путь — batch-Unity
   на своей копии; в редакторе — delayCall + статус-файл (главный поток всё равно занят, renew аренды) и откат
   трёх файлов с согласия performance/gameplay. Аренда 421: сборка пачкает ещё `ProjectSettings/GraphicsSettings.asset`
   и `Assets/Settings/Graphics/UniversalRenderPipelineGlobalSettings.asset`; обход MCP-таймаута через `delayCall` +
   статус-файл работает (`tasks/vr-test-stand/tools/e2e-isolation-build.cs.txt`, 75 с).
   (`SessionRecoveryOnReconnectScenario.ConnectDirectly` мимо шва портов — передан network: `e2e-reconnect-port-seam`,
   inbox 3701.)
3. `xr-startup` — запуск без аппаратного XR для сетевых проверок, production-путь не трогать — PH, VTS.
4. `xr-input` — один адаптер поз/кнопок/стиков, ревью avatar-ik — VTS §5, §7, §14.
6. `stands-migration` — BotCombatStand сам правит EditorBuildSettings/Bootstrap/registry, ловит `StartupRoute.SceneNotLoadable`; перевести на общий запрос, порядок с bots-fix — VTS.
7. `worktree-isolation` — EditorPrefs профиля (`DebugBootstrapSettings.KeyPrefix`) и UXR focus-pause общие между worktree — VTS.
8. `headless-roles` — клиент `-nographics` считается сервером; общий запуск стресс-теста/сервера/e2e с performance — `Docs/testing.md` «Подводные камни, найденные прогоном».
9. `docs-sync` — `Docs/testing.md` «Уровень 3 · Multiplayer Play Mode» устарел (MPPM-теги вместо PlayLaunch); с gameplay сверить «Play из сцены карты идёт Offline → Lobby → карта» — MRB «Известные ограничения».
10. `interaction-scenarios`, `match-e2e`, `network-faults` — каркасная часть; содержание у manipulation/gameplay/network — PH.

Ограничения: native Oculus boundary, UPM/MPE -noUpm, licensing fallback, Git config lock — отдельные границы;
full suite, клик toolbar, Quest, synthetic XR/gameplay E2E не приняты; предупреждения TeamRegistry из аудита383 —
с gameplay; `Tools/e2e/Run-E2E.ps1`, `Tools/release/`, `ProjectSettings/VirtualProjectsConfig.json` вне writes
этапов — только новым этапом.

- `troubleshooting-link` — запись в `Docs/troubleshooting.md` (раздел «Сборка и инструменты»): Link не подключается
  после `-StopLink`, помогает только перезапуск среды Meta Quest Link на ПК (2026-10-12). Пока описано в
  `Docs/testing.md`; в troubleshooting — когда этап `hand-rig-quality/documentation` освободит файл.
