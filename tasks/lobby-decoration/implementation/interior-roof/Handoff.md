# Vendor roof extraction — frozen editor code, 2026-10-09

Единственный API: `LobbyTentRoofBuilder.Build(parent, tent, floorY)`, namespace VrBattlegrounds.EditorTools. Parent должен принадлежать загруженной сцене и иметь lossyScale one; duplicate child TentRoof отклоняется. Главный авторинг удаляет свой прежний flat8-triangle roof и вызывает этот helper ровно один раз. Сцену/общий authoring/существующие Interior helpers исполнитель не менял и Unity не запускал.

Источник READONLY: `LobbyMilitaryCampSources.Prefab("SM_Tent_a")`, main `Assets/Art/Lobby/Imported/MilitaryCamp/Art/Prefabs/SM_Tent_a.prefab`. Все пять MeshFilter names SM_Tent_a_LOD0..4, источник mesh GUIDabc00000000012600163309846702940, local fileIDs4300000/2/4/6/8. Source transforms root/LOD identity подтверждены prefab YAML; каждый Renderer имеет slot0 clothM_Tent_01_a GUIDabc00000000013196932909995244686, slot1 metalM_Metal_01 GUIDabc00000000007798567330081509954.

Builder не инстанцирует весь source prefab: исходные стены/пол/растяжки/collider не попадают в сцену. Нужны readable Mesh с двумя triangle submeshes.

Ткань: submesh0, min(vertexY)≥2,22, |triangle normalY|>0,2. Референс footprint берётся по реально выбранным vertices **LOD0**, не общему source bounds. Металл: submesh1, minY≥2,22, затем все три vertices должны находиться в reference XZ **со строгим inset0,08 м**. Это преднамеренно более строгая интерпретация margin, исключающая любой дополнительный footprint/тросы. Число before/after metal clipping записывается. Если art director имел в виду расширенный margin, требуется отдельное согласование, а не незаметный overhang.

Source reference ожидается примерноcenter(0,01;2,88;0,01), size7,24×1,32×5,85. Builder вычисляет его реально и не подставляет эти размеры вслепую. Target XZ=**exact tent.size.x/z**, center=tent.center.x/z. Vertex Y=floorY+4,2+(sourceY−2,22)×1,4/1,32. Никакой дополнительной ширины/длины. Simplified LOD cloth vertices, слегка выходящие за LOD0 reference, clamp к его XZ limits; число clamp vertices видно в отчёте. Иначе final roof footprint мог бы «подрасти» при LOD switching.

Исходные UV0..7 копируются поканально без tiling/atlas remap, source Color сохраняется полностью. Normal преобразуется inverse-transpose нового linear transform. Tangent масштабируется, нормализуется и ортогонализуется к normal, w сохраняется. Если normals/tangents отсутствуют или coarse LOD clamped, пересчитываются нормали/тангенты готовой геометрии. Source seam vertex indices не свариваются, source UV splits сохраняются.

Собственные mesh assets `Assets/Art/Lobby/Interior/Roof/TentRoof_LOD0..4.asset`, две собственные копии материалов `Assets/Materials/Lobby/Interior/Roof/RoofCloth.mat`/RoofMetal.mat. CopyMaterial сохраняет shader, все PBR texture/property/UV scale-offset из source. Shared source assets не изменяются. При повторной сборке обновляются только собственные существующие assets через CopySerialized, GUID сохраняются. Сохранение Assets/Scene принадлежит главному интегратору.

Один LODGroup, пять LOD renderers, thresholds0,55/0,28/0,12/0,045/0,008; без ForceLOD, CrossFade, ShadowCasting/ReceiveShadows, Collider, дополнительных scripts, BatchingStatic. Только OccludeeStatic. Каждый LOD mesh имеет два submeshes и source material order. Это копия выделенной верхней геометрии, не shader deformation.

## Validation report и границы

`LastReport`/ignored `reports/implementation/interior-roof/latest-build.json` при native Build: sourcePrefab, исходный clothreference Bounds, targettent/floorY, own material paths, status и для каждого LOD sourceMesh, sourceClothBounds, clothTriangles, metalBeforeClip/AfterClip, outputBounds, vertices, uvChannels, sourceNormals/Tangents/Colors, clampedClothVertices, generatedAsset. Output Bounds относятся к roof child local coordinates: XZцентр0, Yмировой floor/eave; root worldXZ равен tent.center.

Для сравнения ведущий передал native до клипа clothcounts3077/1378/544/196/81, metalcounts384/245/172/92/33. Исполнитель эти native counts сам не запускал, не утверждает новый фактический результат. После Build сверить sourcefilter/counts, отсутствие walls/guywires, точный footprint, пять LOD, folds/seams/UV, XZ границы металла, eaveY/ridge, material нормали и общий screenshot. Native Build может выявить разницу minY или удаление части крайних металлических элементов из-за strict inset: это видно в report, никаких скрытых primitive fallback.

Фактический offline C# compile source helper + roof builder against Unity6000.4.1f1 и текущий VrBattlegrounds.dll: exit0. Helper `implementation/interior-roof/offline_compile.ps1`; report `reports/implementation/interior-roof/offline-compile.txt`. Это не native geometry/UV/LOD/scene визуальная проверка. New script .meta создан offline с GUID6e76c6c5f0d14d5a916c098d0947e2ab после collision search; source GUID не меняются. Fresh stage guard implementationepoch1 ok=true. Новые gameplay tests/commit/Unity/MCP операции не выполнялись. Art acceptance pending native preview.
