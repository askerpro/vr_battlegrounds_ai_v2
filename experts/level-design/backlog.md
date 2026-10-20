Обновлено 2026-10-11. Кандидаты вне хаба; порядок — приоритет. LD = `Docs/level-design/`.

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
