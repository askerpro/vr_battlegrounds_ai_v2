# Expert `level-design`

Map and lobby content: hierarchy, geometry, light, occlusion, colliders, placement, decor, validation.
Consumers: gameplay (MapBootstrap reads zones/stations), bots (navmesh), map E2E.

## Owns
- Lobby and map scene content (Environment, PhysicalArenaLayout, Gameplay placement), light, occlusion data.
- Arena blueprint, LD blocks, decor, ambience, cover classes, environment layers/colliders, blockout, growth, evaluation.
- Map/occlusion validation tools wherever they live; `LobbyRangeLayoutBuilder`; environment selection
  (EnvironmentPackCatalog).
- Stations: scene spot, fence niches, zone link, body and its decor.
- `Docs/level-design/`, `Docs/maps/`, `Docs/scene-hierarchy.md`.

## Not owned
- gameplay: loading, map catalog, `MapData`/`MapRegistry`, spawn/match rules, MapRoot/MapBootstrap code,
  `map-startup-route`; station catalog entry, generated-station adapter, composer, preset, style, row keys;
  mode/referee/player/session editors, GameTagsTool.
- performance: scene budgets, occlusion criterion. bots: `BotNavMesh*`, `BotNavMeshSources`, BotCombatStand.
- launch-infra: build scene lists, Debug/Dev/Tools stands. ui: LaserGridScreensPreview. weapon-system: HitEffectBuilder.

## Invariants
- Parent = owner: Environment non-interactive (floor, walls, decor, light, ambience); Gameplay: spawns,
  stations, targets, triggers. Composite prefab stays whole; belonging by component/reference, not name.
  Hierarchy never sets tag/layer/StaticEditorFlags.
- PhysicalArenaLayout: one PhysicalArenaDefinition, zero Colliders, Geometry off, CalibrationAnchors on;
  each map has its own floor; never apply map content to the shared prefab.
- One MapRoot with identity; no MapReferee/ArsenalEquipmentCoordinator in scenes.
- Decor keeps gameplay colliders, height, Hard/Soft/Visual; re-measure D/C after decor.
- Occlusion: movers not static; bake when the map is ready, before release. «Bake Occlusion (all maps)» writes
  all scenes: not while another scene writer runs.
- Navmesh is built in server memory from solid colliders on Default/Ground: layer/collider change → notify bots.
- Save only the task's scenes/assets; no SaveAssets.

## Architecture
- Scenes: `Assets/Scenes/Lobby.unity`, `Assets/Scenes/Lobby/`, `Assets/Scenes/Maps/`.
- Runtime: `Assets/Scripts/Maps/PhysicalArenaDefinition.cs`, `PhysicalArenaLayout.cs`, `PhysicalObstacleMarker.cs`,
  `SpawnZoneBoundaryOpening.cs`, `BlockoutBlockRegistry.cs`; `Assets/Scripts/Maps/Cover/`.
- Tools in `Assets/Editor/VR_Battlegrounds/Gameplay/`: `MapGameplayHierarchy.cs`, `PhysicalArenaLayoutMigration.cs`,
  `OcclusionBakeTool.cs`, `CoverClassTool.cs`, `SpawnZoneCreator.cs`; blockout/evaluation/growth:
  `Assets/Editor/VR_Battlegrounds/LevelDesign/`; `Assets/Editor/VR_Battlegrounds/Weapons/Calibration/LobbyRangeLayoutBuilder.cs`.
- Prefabs: `Assets/Prefabs/LevelDesign/`, `Assets/Prefabs/Arenas/`; station bodies
  `Assets/Prefabs/Arsenal/CommonOpenArsenalStation.prefab`, `LobbyDemoArsenalStation.prefab`.
- Environment: `Assets/env_packs/`, `Assets/ThirdParty/ParticlePack/`, `Tools/AssetCatalog/`.
- Tests: `Assets/Tests/EditMode/Maps/`. Read-only border: `Assets/Scripts/Maps/Runtime/MapRuntimeCatalog.cs`,
  `Assets/Scripts/Arsenal/ArsenalStationCompositionBinding.cs`, `Assets/Scripts/Bots/BotNavMeshSources.cs`.

## Details (grep the heading)
- `Docs/level-design/README.md`; `Docs/scene-hierarchy.md` «Группы сцены», «LobbyRange»,
  «Изменения и проверка».
- `Docs/level-design/level-design.md` «Высота и защита», «Декорация»; same folder: `physical-arena.md`,
  `level-design-principles.md` (LD-01…55), `map-evaluation.md`, `map-growth.md`, `environment-pack-policy.md`.
- `Docs/game-manager.md` «Запуск карты»; `tasks/map-runtime-bootstrap/Details.md`
  «Адаптер генерируемых станций». `Docs/troubleshooting.md` «Превью арсенала и материалы».
