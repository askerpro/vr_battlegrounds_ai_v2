# MilitaryCamp CardboardTarget — readonly blueprint, 2026-10-09

Дополняет IntegrationContract.md. Источник — **URP** пакет `F:/UnityProjects/EnvironmentPackCatalog/Assets/env_packs/MilitaryCamp/URP/MilitaryCamp_URP.unitypackage`; прочитан только извлечённый cache, принадлежащий ведущему. Sourcecopy/Unity/код не выполнялись. На момент аудита binary FBX и texture bytes отсутствуют в cache, доступны index/prefab/материалы/shadergraph/FBX meta. Поэтому форма печати и фактический visible hit contour не подтверждены.

Cache root: `tasks/lobby-decoration/reports/asset-evaluation/military-base-megapack-372662/urp-source-cache/`. Для GUID ниже текст лежит в `<guid>/asset`, мета — `<guid>/asset.meta`; original path указан в index.json.

## Конкретные candidates

Все prefab paths: `Assets/Hivemind/MilitaryCamp/URP/Art/Prefabs/SM_CardboardTarget_0N.prefab`; FBX paths: `.../Art/Assets/SM_CardboardTarget_0N.fbx`.

| Candidate | Prefab GUID | FBX GUID | LODGroup size / reference center |
|---|---|---|---|
| 01, первый кандидат для native review | abc00000000013348681974927970519 | abc00000000000597718462212395182 | 1,878421 / (0,000669;-0,730583;0,025849) |
| 02, запасной | abc00000000018256637580564942428 | abc00000000016540314784504750619 | 1,847781 / (0,007092;-0,711879;0,025849) |
| 03, запасной | abc00000000006467555231176644261 | abc00000000001915329070080221856 | 1,867664 / (0,010538;-0,739563;0,025849) |

Выбор 01 — порядок исследования, **не утверждение**, что он художественно/геометрически лучше 02/03. YAML не отличает пригодный прямоугольный контур от силуэта. Итоговый candidate определяет art director по binary/native preview и совпадению видимого щита с сохранённым collider.

Для каждого candidate:

- Root и все LOD children имеют local position zero, rotation identity, scale one. Скрытого corrective rotation в prefab нет.
- LOD0 mesh object fileID **4300000**, LOD1 **4300002**, LOD2 **4300004**. FBX meta `fileIDToRecycleName` подтверждает имена `SM_CardboardTarget_0N_LOD0/1/2`. Это fileID **Mesh asset**, не номер submesh.
- Каждому из трёх MeshRenderer назначены два material slots в одном порядке: slot0 mechanism; slot1 cardboard. Значит весь ModelMesh нельзя перенести в существующий PlateRenderer: туда попадёт и механизм.
- Source Colliders содержит два BoxCollider. Thin panel proxy: center `(0,00066941977;-0,8499124;0,02582736)`, size `(0,9233358;1,406425;0,009254813)`. Mechanism proxy: center `(-0,00039045513;-0,025463007;0,024061017)`, size `(0,302287;0,2405224;0,46921247)`. Оба identity; **их не переносить**.
- Panel proxy занимает y≈[-1,553125;-0,146700], z≈[0,021200;0,030455]. Источник висит ниже своего механизма; source origin нельзя принять за основание стойки или центр попадания. Proxy не доказывает точные mesh bounds.

Evidence для 01 `<prefabGUID>/asset`: root transform:20, LODGroup:39, LOD0 transform:84, Renderer/material slots:99..123, MeshFilter/fileID:142..149, panel Collider:350..363. 02 имеет аналогичные LOD секции, panel Collider:428. 03 аналогичен по трём LOD и material slots. Evidence FBX mapping: `<FBXGUID>/asset.meta`, строки `4300000/2/4` и `globalScale:1`, `useFileScale:1`, `externalObjects:{}`.

## Material / MPB — обязательная адаптация

| Назначение | GUID | Original path |
|---|---|---|
| slot0 механизм | abc00000000007133878449802549715 | Art/Materials/M_CardboardTarget_mechanism.mat |
| slot1 щит | abc00000000011422199519173746255 | Art/Materials/M_CardboardTargtets.mat (source spelling) |
| Source ShaderGraph | e6f99812ca2d22a4c98d5e88e4d78f2a | Art/Shaders/S_BasicTextured.shadergraph |
| Cardboard BaseMap | abc00000000015854609525280240127 | Art/Textures/T_CardboardTargets_BC.png |
| Cardboard Normal | abc00000000001249506648285946128 | Art/Textures/T_CardboardTargets_N.png |
| Cardboard ORM | abc00000000013663695519416191054 | Art/Textures/T_CardboardTargets_ORM.png |
| Mechanism BaseMap / Normal / ORM | abc00000000007007787805572581576 / abc00000000002472418990598508941 / abc00000000010308736987786667443 | Art/Textures/T_CardboardTarget_mechanism_BC/N/ORM.png |

Оба материала сохранены со значениями `_BaseColor`/`_Color` white, `_AlphaClip=0`, `_Surface=0`, `_Roughness=1`. Cardboard `_Cull=0` (двусторонний), mechanism `_Cull=2`.

**Source ShaderGraph фактически экспонирует цвет `_Tint`, а не `_BaseColor`/`_Color`.** `<shaderGUID>/asset:3940..3943` содержит `m_Name:"Tint"`, `m_DefaultReferenceName:"_Tint"`; перечень graph property reference names не содержит `_BaseColor`/`_Color`. Их наличие в YAML материала — старые сохранённые значения, не доказательство поддержки shader. ShootingTarget.SetFlash пишет только `_BaseColor`/`_Color`; использование исходного shader без адаптации оставит hit flash неработающим.

Новый собственный board материал должен использовать установленный URP/Lit (shader GUID `933532a4fcc9baf4fa0491de14d08ed7`) с настоящим source BaseMap, NormalMap и ORM conversion; сохранить UV tiling/offset, двусторонность `_Cull=0`, непрозрачность и выключенный alpha clip. ORM.b -> URP metallic.r, 1-ORM.g -> smoothness.a, ORM.r -> occlusion.g. Провести native `material.HasProperty("_BaseColor")` и пробное попадание/сброс. Не перенести source upgrade MonoBehaviour `da692...` (HDRP historical) и не менять ShootingTarget.cs ради `_Tint`. Runtime MPB writer остаётся ShootingTarget.

## Mesh blueprint для ведущего

1. В единственной штатной Unity операции загрузить FBX 01 по GUID, через LoadAllAssetsAtPath получить именно `SM_CardboardTarget_01_LOD0` Mesh и подтвердить его localFileID=4300000. Проверить subMeshCount и соответствие slot1 карточному щиту. GUID+fileID не указывает отдельный submesh.
2. Извлечь **только material slot/submesh1** в новый собственный Mesh: triangles, используемые vertices, normals, tangents, UV0; пересчитать bounds. Если slot1 содержит не только щит или не соответствует YAML порядку — остановить выбор и проверить source. Не выбирать по имени одного ModelMesh, не брать весь mesh вместе с механизмом, не инстанцировать source LODGroup/Colliders под Pivot.
3. Подтвердить flat face plane и front-facing печать по texture/native view. Source proxy расположен в XY с малой толщиной Z; знак лицевой стороны YAML не определяет, потому что cardboard двусторонний. Bake orientation так, чтобы печать смотрела в **+Z существующей Plate/Pivot**, вертикаль была +Y; Pivot/root rotations не менять. При развороте на 180° корректно преобразовать normals/tangents, не использовать negative transform scale. Не делать автоматический flip Y только потому, что исходные координаты y отрицательны: это offset подвесного щита, не доказательство upside-down печати.
4. Сцентрировать extracted board по реальным mesh.bounds и bake dimensions в локальный Plate envelope x/y≈[-0,5;+0,5], z внутри прежнего cube thickness envelope. Existing Plate transform scale(0,6;0,7;0,03) сохраняется, реальный hit rectangle 0,6×0,7×0,03 м. Для proxy ориентировочный physical bake коэффициент x=0,6/0,9233358≈0,64981, y=0,7/1,406425≈0,49772; применять к настоящим board bounds, не source proxy вслепую.
5. **Uniform fit не совпадает с прежней hit area:** source proxy aspect≈0,65651, target≈0,85714. Uniform fit по высоте оставит ширину≈0,4595 м и невидимые hittable полосы ~0,0702 м с каждой стороны. Требуется согласованный baked horizontal stretch≈1,3056 либо другой реальный source с подходящим прямоугольным контуром. Если source silhouette имеет вырезы/сужения, совпадение только outer bounds недостаточно: прежний rectangle collider остаётся hittable в видимо пустых углах. Art director/native review должны принять contour consistency; collider менять запрещено.
6. Назначить новый Mesh существующему Plate MeshFilter fileID4703857167406685188 и board Material существующему Plate Renderer fileID823574351588950679. RingOuter/RingMiddle/Bullseye renderers скрыть, GO/transform/fileID сохранить. На Root/Pivot/Plate/collider/name/pose/gameplay не писать. Source mechanism submesh0 можно отдельно использовать на visual-only stand child root вне Pivot; он не заменяет оригинальные Post/Foot collider и не включается в flash/photo.

Предпочтение для первой native проверки — 01 submesh1, с собственным URP/Lit материалом slot1. После binary/preview переходить к 02/03 только если контур/печать 01 не подходит. Ни один candidate ещё не объявлен принятым финальным ассетом.

Фактически выполнено: readonly сравнение трёх prefab/FBX.meta, двух материалов, ShaderGraph property names и общего index; обнаружены combined mesh/material slots, несовместимость `_Tint` с flash и различие aspect ratio. Source textures и binary meshes ещё не просмотрены. Unity/compile/tests не запускались; изменён только этот audit artifact. Fresh guard перед записью: ok=true, implementation epoch1.
