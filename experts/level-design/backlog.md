Обновлено 2026-10-14 (передача Codex). Кандидаты вне хаба; порядок — приоритет. LD = `Docs/level-design/`.

## Активные задачи (в хабе)

- `scene-detail` — профиль детализации сцены, этап `core` в работе: WIP в ветке `scene-detail`
  основного checkout, не скомпилирован. Состояние и следующий шаг — `tasks/scene-detail/Readme.md`.
- `lobby-decoration` — лобби влито (eea14fe3a, окклюзия в LFS e78dc38ad, материалы MilitaryCamp
  77ef97d15 — сопровождающий). Бюджет GPU не выполнен: обход 8 точек 6467, худшая 10,83 мс > 7 мс —
  решается задачей `scene-detail` (этап `lobby-apply`).

## Обещания другим агентам

- ui (`debug-menu-refactor`, события 6493/6495/6506): после влития `scene-detail/core` прислать ui
  замену блока LobbyDetailTuner на `SceneDetailDebugSection.Build(content, invalidate)` +
  `Snapshot()`; секция — только MenuKit Section/Row/Label/Button, без Clear/Title и своих цветов.
- performance (`perf-stress-scenario`, 6499/6504/6508): diff `ProjectSettings/QualitySettings.asset`
  (группы DetailBand0..8 вместо LobbyBand0..8, limitBias 0) — на ревью ДО checkpoint core; сообщить
  о готовности core (перф-ревью applier); точки обзора сцены → `Tools/Perf/views/<Сцена>.json`
  (переносит performance, этап docs); приёмка `lobby-apply` — его обход 8 точек, худшая ≤ 7 мс.

## Лобби: известные дефекты

- Розовые куски домов: у `SM_Wall_*`, `SM_Building_wall_window_*` материалов больше подмешей —
  обрезка: `Tools/VR Battlegrounds/Detail/Проверка слотов материалов` (scene-detail).
- Прототип `LobbyDetailTuner` (в dev): ForceLOD снимает отсечение — появлялись деревья; заменяется
  `SceneDetailApplier`. Полная детализация в dev — замер 6467.
- `lobby-range-split` (п.3 ниже) закрыт раскладкой навеса: LobbyRange удалён, столы в
  Environment/ArmoryRangeZone/InteriorDecoration/Tables, мишени в Gameplay/ShootingRange.

## Кандидаты

1. `testmap2-lighting-data` — `TestMap2/LightingData.asset` на worker — LFS-указатель, тесты сцен падают — `tasks/map-runtime-bootstrap/Details.md` «Проверки и пределы».
2. `testmap1-beam-overlap` — `TrayFrontLip` корпуса станции (твой) пересекает `LD_Beam_Low` на 1,3–1,5 мм; тест `ArsenalMapGeometryTests` — gameplay — `tasks/arsenal-generator/Details.md`.
3. `lobby-range-split` — LobbyRange: столы в Environment, мишени в Gameplay; после `lobby-decoration/implementation` — `Docs/scene-hierarchy.md`.
4. `hierarchy-headset-acceptance` — шлем: разделение PhysicalArenaLayout шести сцен — `Docs/scene-hierarchy.md`.
5. `lighting-policy` — правил света/запекания для Quest нет, запечён только TestMap2; с performance — journal 2026-09-28-occlusion-first-bake.
6. `occlusion-bake-scope` — Bake Occlusion пишет все сцены; нужен bake одной сцены или порядок — `Assets/Editor/VR_Battlegrounds/Gameplay/OcclusionBakeTool.cs`.
7. `env-catalog-tools` — инструменты подбора не адаптированы к внешнему каталогу — `Docs/plans/2026-10-05-environment-pack-separation.md`.
8. `arena-protection` — ручная сборка как защита; мастер создания карты — `LD/README.md`.
9. `map-growth-acceptance` — приёмка выращивателя, benchmark, тесты — `LD/map-growth-implementation-plan.md`.
10. `evaluation-adapter` — адаптер разметки, коридоры, все контакты — `LD/map-evaluation.md`.
11. `grid-palette-review` — проверка палитры, старые зависимости — `LD/blockout-editor.md`.
12. `reference-calibration` — TestMap1/ReferenceMap04, калибровка LD-01…55 — `LD/maps/testmap1-review.md`.
13. `service-yard` — аудит, композиция, плейтест, арт-пасс — `LD/maps/service-yard.md`.
14. `assembly-hall` — цех-кузница: модели, стенд, Quest — `LD/maps/automated-assembly-hall-decoration-plan.md`.

Пересечение: `lobby-decoration/implementation` пишет `Lobby.unity`, его окклюзию и `MapRuntimeCatalog.asset` (gameplay).
