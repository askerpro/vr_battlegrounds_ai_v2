Обновлено 2026-10-14. Кандидаты вне хаба; порядок — приоритет.

1. `quest3-target` — `Assets/XR/Settings/OculusSettings.asset`: `TargetQuest2: 1`, `TargetQuest3/3S/Pro: 0`; в собранном Test APK (ревизия d51aa3a9ecf7) в манифесте нет `com.oculus.supportedDevices` и VR-категории. Выяснить, не идёт ли Quest 3 в режиме совместимости Quest 2 (разрешение глаз, частота, доступные уровни CPU/GPU) — это меняет все замеры; сверить с release-сборкой; `Assets/XR/Settings` ни у кого не в owns — решение пользователя 2026-10-13: пока НЕ включать (владелец `Assets/XR/Settings` не назначен); вернуться перед релизом/`budget-baseline` — находка launch-infra 4873, проверка APK 2026-10-12.
2. `budget-baseline` — согласовать бюджеты: частота 72/90 Гц, кадр по подсистемам, GC/кадр, draw calls/SetPass/треугольники, память (`gfx_mb`), кадр выделенного сервера, сеть; записать в `Docs/perf-stress-test.md` (сейчас есть только кадр) — journal 2026-09-29.
3. `quest-rerun` — прогон на шлеме: `ovr_gpu` не нулевой, `ovr_cpu_lvl` = 4, в заголовке `FFR: 0.75`; `ovr_gpu` p95 против прошлого (до 20 мс) — `Docs/perf-stress-test.md` «Дальше».
4. `remote-avatar-geometry` — ТЗ avatar-ik: слить SMR тела/снаряжения с атласом (MEF 49 SMR / 27 материалов, Heavy 52 SMR), LOD чужих ~15–20k треугольников; критерий — замер «по скинам» — `Docs/perf-stress-test.md` «Дальше», `Tools/mef-avatar/README.md`.
5. `scene-budgets` — с level-design: бюджеты сцены (видимые draw calls/треугольники, свет, тени) и критерий окклюзии; `OcclusionCullingBakedTests` не ловит устаревшие данные; ссылка `Docs/README.md` на `level-design.md` «Occlusion culling» ведёт в несуществующий раздел — сообщить level-design — `Docs/README.md`.
6. `puppet-weapons` — куклы повторяют оружие (копия предмета, выстрелы, гранаты); счётчики `Shot`/`Explosion`/`Grab`/`Release` в `PerfEvents` уже есть — `Docs/perf-stress-test.md` «Дальше».
7. `projectile-cost-review` — методика и ревью замера T-24 на Quest (`UxrWeaponManager.UpdateManager`, 2/4 стрелка) у weapon-system — `Docs/combat-networking.md` «Что измерить на Quest — и как».
8. `default-pipeline` — `ProjectSettings/GraphicsSettings.asset` ссылается на сэмпловый URP-Balanced из ThirdParty; нужен ли свой default — journal 2026-09-28 quest-graphics-baseline.
9. `ffr-intermediate-texture` — у `Default_Forward_Renderer` Intermediate Texture = Always; не теряет ли FFR основные проходы — `Docs/perf-stress-test.md` «Дальше».
10. `hand-tracking-permission-name` — Horizon OS: `com.oculus.permission.HAND_TRACKING` устарело, советует `horizonos.permission.HAND_TRACKING` (logcat прогона 2026-10-13); добавить новое имя в `QuestControllerFreeManifest` (Test) и проверку валидатора, проверить запуск без контроллеров на шлеме — зона сборки launch-infra.
11. `precache-guard-regression` — после вливания network precache-server-guard (f645d72f9): perf-stress с сервером с графикой (редактор/оконный сервер) — mem_mb сервера по фазам ровный, без ступеньки на спавне кукол; в логе «[HeadlessPrecacheGuard] сервер без своего игрока» — network 6326, дефект bots 6233.

## Обещания другим агентам и ожидания (на 2026-10-14, передача Codex)

- level-design `scene-detail` (6485 → ответ 6499, условия приняты 6504; zone-ack core на `QualitySettings.asset`
  выдан, scope 739e82ce…): ревью diff `QualitySettings.asset` до их checkpoint (группы DetailBand0..8 вместо
  LobbyBand0..8, limitBias 0 на всех уровнях, globalTextureMipmapLimit не трогать); перф-ревью `core`
  (SceneDetailApplier — только OnEnable/OnDisable, без Update/поиска по сцене/аллокаций); приёмка `lobby-apply` —
  мой обход 8 точек: худшая база ≤ 7 мс, куклы ovr_gpu не хуже 12,09, tex_mb не выше 1770. Файл точек —
  перенести в `Tools/Perf/views/Lobby.json` (согласовано 6504; этап `docs` perf-stress-scenario).
- level-design `lobby-decoration`: замер обхода 2026-10-14 отправлен (6467); повтор — по их готовности.
- launch-infra `quest-perf-pipeline/build-checkouts` (6455, после e2e-smoke-kit): замер первой и инкрементальной
  сборки Prod и Quest-Test на новых checkout; база сравнения 5416 (Prod APK 355 с, постановка 634 с; Test APK 150 с,
  пара 396 с; пара с переключением 2026-10-14 — 2163 с).
- weapon-system shot-cost (метод 6156): `--phases` в `Tools/Perf/quest_gpu_breakdown.py` (этап `gpu-tool-phases`),
  ревью плана до прогона и цифр после.
- avatar-ik legs-ik/`quest-perf`: тот же `Docs/perf-stress-test.md` — согласовать порядок до этапа `docs`.
- `experts/performance/AGENT.md`: карта сверена по owns/watch_paths от 716743336f до ce8a365b4,
  уточнены обход и GPU-инструмент. `reviewed_sha` обновлён; это ревью карты исходников, не Unity/Quest PASS.
- `perf-stress-scenario/view-tour`: FIFO возобновлена сопровождающим (6695); предложение 622 истекло,
  аренда 626 закрыта после `Session terminated` без результата компиляции и без запуска EditMode.
  Аренда 634 закрыта: AndroidCompileGate PASS (0 ошибок), EditMode потерял plugin session до `job_id`/итога.
  Admission этапа освобождён (planned); подтверждённого результата Unity-тестов нет, merge закрыт этим блокером;
  обход уже принят пользователем. Infra_issue 6678 остаётся свидетельством исходного блокера.
  Текущий MCP-блокер направлен сопровождающему событием 6790. После worker_ready_resume 6805 заявка 645
  завершила чтение TestJobsV1: свой634 не найден, currentJob пуст, старый исход неизвестен. Worker освобождён;
  report-only receive остановлен (6940), R не принимается. Независимый EditMode654 снова потерял plugin session
  до job_id/итога; DONE/received, paths0, admission освобождён. Infra_issue7008; новых заявок нет.
- weapon-system 6747: 30 SDK/WS/bots-маркеров и native buffer capture пока uncommitted, только offline-результаты.
  Ревью точного кандидата — после bots driver/runtime probe; отсутствие Reset в Update и overhead не признаны
  подтверждёнными на Quest. Сбой MCP нашего пакета отправлен сопровождающему (6768).
- weapon-system 6903: будущие точные GPU-фазы `shot-cost: bot` / `shot-cost: away`; producer
  ShotCostScenario ждёт BotShotCostDriver. Runtime-маркеров ещё нет, Editor synthetic probe не заменяет
  Quest-замер. Уточнение внесено в gpu-tool-phases-notes; источник старого T23/T24 для этого не используется.
- weapon-system6984: synthetic Editor recorder probe648 PASS по отчёту владельца (345 кадров, 41 emission,
  mismatches0; analyzer warm3/steady38/errors0/warnings11, comparison_complete=false). GC baseline включает
  фон Editor; GC=0, стоимость оружия/сети и Quest не подтверждены. Реальный bot observer ждёт driver/clip-cutover.
  Это не наше независимое ревью кандидата и не hardware acceptance.
- Проверка точности обхода: `view.ok` сейчас не ограничивает deviationXZ/yaw/floor. В принятом прогоне
  16/16 поз точные (XZ ≤ 2,39e-7 м, yaw/floor = 0). Согласовать допуски до изменения автоматического вердикта;
  не считать исторический GREEN проверкой отрицательного случая. Подробности: task reports/view-tour-review-20261014-codex.md.
- `scene-detail` 6813 → ревью 6875: Quality/140 meta/event-driven source проверены на стабильном snapshot
  cc485d9bd + diff 895a39b7a72e…; кодовых блокеров нет. Нужен export 112 protected GUID того же snapshot
  для независимой проверки пересечения. Apply имеет burst allocations; runtime/UI/Quest бюджеты открыты.
- Карта owns/watch_paths после rebase на origin/dev 97a320c88 сверена до 5cfc55248: diff исходников пуст;
  reviewed_sha обновлён. Это не новое подтверждение Unity/Quest.
