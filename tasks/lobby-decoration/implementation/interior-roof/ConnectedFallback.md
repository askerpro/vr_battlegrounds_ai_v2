# Reserved connected roof fallback — frozen implementation, 2026-10-09

Этот implementation заменяет source-filter/clip крыши из Handoff.md и CorrectingPass.md. Они исторические. Решение root: один connected own roof fallback, затем native capture/check. Исходный SM_Tent_a/mesh/material не удалялся и не изменялся.

API `LobbyTentRoofBuilder.Build(parent, tent, floorY)` сохранён. Новый `HeightAt(tent,floorY,x,z)` даёт фактическую высоту ткани для **визуальных** надставок физических колонн; сами PhysicalArenaShape/collider/позы колонн helper не меняет.

Два ската, конёк вдоль Z. Footprint exact измеренный tent XZ, на текущем15,5×14,96. Eave=floorY+4,2; ridge=floorY+5,6. Между рамами ткань слегка провисает внутрь: max0,035м, ноль на eave/ridge/frame lines. Удаления треугольников/разрозненных source patches больше нет.

Пять downsample LOD grid, общий индекс вершины на коньке (не две разъединённые половины):

| LOD | cellsX×Z | Вершины | Triangles | Ожидаемые внешние boundary edges |
|---|---|---|---|---|
| 0 | 12×24 | 325 | 576 | 72 |
| 1 | 8×16 | 153 | 256 | 48 |
| 2 | 6×12 | 91 | 144 | 36 |
| 3 | 4×8 | 45 | 64 | 24 |
| 4 | 2×4 | 15 | 16 | 12 |

Native `Audit(mesh)` перед сохранением строит edge counts из реальных mesh vertices/triangles, проверяет что все valence1 edges лежат на внешнем X/Z perimeter, internalBoundaryEdges=0, nonManifoldEdges=0 и boundary count=2(nx+nz). Любое нарушение бросает исключение. Таблица — предписанная сетка; **новый native Audit исполнитель ещё не запускал**, не использовать её вместо его actual JSON. Normals/tangents пересчитываются по собственным connected vertices/UV.

Собственные RoofCloth/RoofMetal материалы обновляются с сохранением GUID, только в Interior/Roof. Cloth shaderURP/Lit, checked source T_Tent_a_BC/N. Source BC реально открыт через view_image; заданный root clean patch UV U0,13..0,35/V0,30..0,45 находится между оконными atlas островками, окна не входят в этот диапазон. `_BaseMap`/`_BumpMap` scale(0,22;0,15), offset(0,13;0,30); normal strength0,25, metallic0, smoothness0,10, Cull0. Это выбранный участок чистой ткани, не full atlas tiling. Исходные textures/imported materials только читаются.

Рамы: десять собственных box strips по двум скатам на z−7,2/−3,6/0/3,6/7,2 (по измеренной tent length и center), плюс три longitudinals по eaves/ridge, всего13. Они находятся под тканью, отступают от footprint edges; Collider CreatePrimitive удаляется сразу. Own metal — тёмный PBR metallic0,65/smoothness0,22. Нет source guywires/walls/floor/scripts/Animator/ShadowCasting/ReceiveShadows/ForceLOD/BatchingStatic подLOD. Только OccludeeStatic.

Пять own asset paths TentRoof_LOD0..4.asset сохраняются, заменяется только их содержимое при native Build. Shared source/Scene/main authoring/Interior/Sidewall helpers исполнитель не менял. Главный интегратор зовёт Build один раз и HeightAt для визуальных extension tops, затем выполняет screenshot/actual geometric audit.

Новый report `interior-roof/latest-build.json`: geometry=connected_own_two_slope_grid, ridgeAxisZ, base/normal texture paths, patchST, eave/ridge/sag, frameBeams; perLOD cells, actual vertices/triangles, bounds, boundary/internal/nonmanifold counts и asset path. Status требует native preview. На момент freeze старый pass05 JSON ещё относится к source clip; новый появится только после Build ведущего.

Фактические проверки: final isolated C# compile source helper + roof builder against Unity6000.4.1f1 и текущий VrBattlegrounds.dll exit0, `reports/implementation/interior-roof/offline-compile.txt`; scoped diff check exit0. Script meta GUID6e76c6c5f0d14d5a916c098d0947e2ab сохранён. Native geometry/UV/roof appearance ещё не приняты пользователем. Новый gameplay test/commit/Unity/MCP не выполнялись.
