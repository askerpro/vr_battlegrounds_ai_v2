# Единственный correcting roof pass — frozen code, 2026-10-09

Эта правка заменяет whole-triangle minY filter и отсутствие Y90 rotation в предыдущем Handoff.md. Source/material ownership, API, 5LOD, footprint/eave/rise и запрет source walls/floor/guywires остаются.

Фактически открыты pass-04/ref-forest-match.png и player-center-north.png через view_image. На снимках прежней версии видны зубчатые границы/провалы и несоответствие крыши открытым ±Z торцам. Прежний native report имел sourceReference center(0,0106695;2,8781416;0,0139277), extents(3,6218281;0,6577423;2,925241), LOD0 whole-selectedcloth3077. Эти значения — старая сборка; новая ещё не запускалась исполнителем.

## Что изменилось

- Triangle candidate теперь берётся, если **max vertexY≥2,22**, а не если все вершины выше плоскости; cloth normal cutoff |normalY|>0,2 сохраняется.
- Sutherland–Hodgman обрезает polygon по source-root Y≥2,22. Пересечение ребра вычисляет положение, все имеющиеся UV0..7, Color, normal и tangent посредством линейной интерполяции. Polygon затем triangulates fan с прежним winding; degenerate faces исключаются.
- Intersection cache keyed by unordered endpoint vertex IDs + clipping plane ID переиспользует одинаковое новое ребро соседних triangles. Исходная vertex точно на плоскости сохраняет свой ID. Source UV seam IDs не свариваются: UV splits не ломаются.
- Coarse LOD и металл также геометрически клиппируются по XZ границам: cloth в LOD0 reference envelope, metal с inset0,08 м. Старого clamping целых vertices и выбрасывания граничных треугольников больше нет.
- **До reference calculation** полная transform chain = `Y90 * prefab.worldToLocal * filter.localToWorld`. Root/mesh transform действительно участвуют; raw FBX axes не принимаются за Unity axes. Selected LOD0 reference заново вычисляется **по clipped polygons после rotation**, затем X/Z scale приводит его к exact tentWidth/depth.
- Vertex Y=floorY+4,2+(sourceY−2,22)×1,4/1,32. Normal/tangent после interpolation преобразуются новым linear/inverse-transpose transform и ортогонализуются; source PBR maps/UV unchanged. Пять LOD и отсутствие Shadow/Collider/scripts/ForceLOD/Batching подLOD сохранены.

## Нативная ось/плоскости: что возвращает новый report

`interior-roof/latest-build.json` после native Build:

- unrotatedCloth bounds из реальной prefab→mesh transform chain;
- unrotatedRidge/rotatedRidge bounds — vertices верхних4см clipped roof относительно реального maxY;
- unrotatedRidgeAxis/rotatedRidgeAxis = X/Z/ambiguous по реальному extent ratio>1,5, не по имени модели;
- sourceRotationY=90 и полные sourceToRoofMatrices для всех пяти LOD;
- footprintPlanes=(tent.minX,tent.maxX,tent.minZ,tent.maxZ), а perLOD boundaryPlaneY=floorY+4,2;
- perLOD sourceClothBounds, clothCandidateTriangles, итоговые clothTriangles, metalBeforeClip/AfterClip, planeIntersections, outputBounds/vertices, UV channel presence и source normal/tangent/color flags.

Ожидаемое направление после принятого Y90 — конёк вдоль Z/open ends, но **новый native ridge-axis результат пока не получен**. Если report rotatedRidgeAxis не Z, не объявлять направление исправленным: проверить native source transform/visible ridge в единственном root run. Снимки прежнего прохода/общие source bounds не доказывают raw ridge axis.

## Проверки

После окончательной правки: isolated C# compile roof builder + source contract against Unity6000.4.1f1 и текущий VrBattlegrounds.dll exit0; `reports/implementation/interior-roof/offline-compile.txt`. Scoped `git diff --check` exit0. Это проверка компиляции; геометрический clipping, новый footprint/axis/UV и отсутствие зубчатой кромки ещё требуют native Build/report/capture ведущего. Не создавались новые gameplay tests. Scene/main authoring/другие builders/source assets/Unity/MCP не менялись. Own generated roof assets обновятся ведущим при Build с сохранением GUID. Fresh guard epoch1 ok=true.
