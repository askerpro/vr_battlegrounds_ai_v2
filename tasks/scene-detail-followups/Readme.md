# Неблокирующие замечания ревью scene-detail

2026-10-22. Задача создана `task-new`; один агент, worktree `F:/CodexWorktrees/scene-detail-followups`, ветка
`task/scene-detail-followups` от origin/dev c7b8d6ab3. Источник пунктов — вердикты ревью scene-detail/quest-gpu-lobby
(раунды 1–3): [review-sources.md](review-sources.md).

## Цель

Закрыть P2/P3 замечания ревью scene-detail (launch-infra, performance, gameplay, свет Lobby) и свести два конвертера
Baked Lit в один фиксер аудитора ассетов (QPM-SHADER-007).

## План

- [x] performance: Tools/Perf/README.md — ссылки на существующие планы/контракты; раздел «Per-draw захват Meta RenderDoc
  на Quest» (порядок снимок → изменение → захват → откат, разрешение на изменения шлема); заметка о буфере
  PerfDiagnosticRecorder в Docs/perf-stress-test.md «Метрики» (≈7 МиБ по коду, не замер; ревьюер писал ≈8 МБ).
- [x] performance: PerfPlanarFloor.shader вынесен из Resources в `Assets/Scripts/Debug/E2E/Scenarios/PerfDiagnostics/`;
  в Development-сборку его кладёт `PerfDiagnosticShaderBuildProcessor` (скрытый выключенный рендерер в копии сцены 0),
  рантайм — `Shader.Find`. Prod его не получает.
- [x] performance: меню Lobby GPU — под `Tools/VR Battlegrounds/Performance/Lobby GPU/`.
- [x] gameplay: PerfStressScenario — отказ `MapLoader.LoadMap` (CanAcceptLoad) сразу в `result.Summary`.
- [x] свет (порядок пользователя 2026-10-22: гипотеза → проверка → правило аудитора ловит → исправление фиксером):
  гипотеза подтверждена на сохранённом ассете — LobbyNightDoorLamp: Baked Lit, `m_LightmapFlags 4` (EmissiveIsBlack),
  `_EMISSION` нет, `_EmissionColor (1, 0.74, 0.46)`, карта T_Door_lamp_EM (ярус scene-detail принял лампу за неэмиссивную).
  Новое правило `material.emission-off` (QPM-SHADER-006) на Lobby ловит дверную лампу и не ловит LobbyNightLampLong
  (RealtimeEmissive) и M_Emissive (Unlit). Фиксер `EmissionOffFixer` применён только к ней: Simple Lit без бликов,
  `_EMISSION`, `m_LightmapFlags 1` — проверено по файлу; повторный аудит находки не даёт. Корневая причина в авторинге:
  `LobbyNightLighting.CopyEmissive` ставит RealtimeEmissive (лампы не переписывает).
- [x] один писатель: `AssetAudit/Fixers/BakedLitMaterialFixer` (правило `shader.non-mobile`) заменил
  `LobbyGpuMaterialTier` и `BoxUv/BakedLitMaterialConverter`; box-UV зовёт `PlanWorldAligned`; меню
  `Asset Audit/Fix Mobile Shaders in Active Scene` и `Revert Mobile Shader Fix in Selected Folders`; данные отката
  прежние (маркер `lobby-gpu-tier-v1`). В ядре: `AuditFinding.FixTargets` — все материалы группы без лимита 25.
  Dry-run на Lobby: правило даёт 33 группы / 294 материала, план фиксера 0 (scene-detail уже перевёл), 297 пропусков
  с причинами (179 вне корней окружения, 75 не рисуются в сцене, 22 встроены в модели, остальное — шейдеры без конвертера).
- [x] манифест: QPM-SHADER-006 → таблица аудитора; порядок «правило до исправления» в «Как читать и поддерживать»; журнал.
- [x] launch-infra (субагент): KEYCODE_SLEEP в конец finally, readlink-проба в try, Docs/testing.md путь,
  удалить Tools/e2e/.gitattributes, GpuSceneIdentity в build.json/pair manifest, rev3 gpu-launch-inputs в
  `contracts/` этой задачи (хаб запрещает запись в tasks/quest-perf-pipeline/).
- [x] heavy: владельцу Tools/e2e/heavy (задача e2e-heavy-editor) отправлено в inbox — сверять свежесть worker по
  содержимому игровых путей.
- [x] Unity (ticket 994): компиляция без ошибок; существующие EditMode AssetAudit, LobbyQuestRenderStateTests,
  BoxUvProjectorTests, PerfStressScenarioArgsTests — 59/59. Уточнения (ticket 996, скомпилировано): Unlit не даёт находку
  «свечение потеряно» (GlowMat_URP ушёл), фиксер эмиссии меняет только корни окружения — в плане по Lobby остался только
  M_Glass_Inst (не применён, решение пользователя). Скриншоты «до/после» дверных ламп (казарма A, ворота склада) —
  `reports/door-lamp-shots/` (вне Git).
- [ ] Не проверено: Development/Prod-сборка с PerfDiagnosticShaderBuildProcessor (шейдер в Test есть, в Prod нет);
  Run-QuestE2E на шлеме (сон в конце finally, readlink в try) — только парсер PowerShell и чтение кода.
- [ ] Приёмка пользователем (лампа, фиксер), затем тесты, heavy, ревью зон, merge.

## Статус

2026-10-22: всё сделано и проверено в Unity, кроме сборок и шлема; ничего не закоммичено. Ждём проверки пользователем
(дверные лампы Lobby светятся; аудит/фиксер). После неё — тесты правила/фиксеров/лампы, heavy, ревью зон
(performance, launch-infra, gameplay, level-design), merge.

Найдено попутно (вне задачи): `material.emission-off` ловит ещё M_Glass_Inst (Lobby), OwnerIndicator (станция арсенала),
DustPuffParticle_NoSoft и 2 сэмпла UXR; в Lobby 3 розовых материала (MI_Emissive, MI_TopLight, «No Name» —
InternalErrorShader).
