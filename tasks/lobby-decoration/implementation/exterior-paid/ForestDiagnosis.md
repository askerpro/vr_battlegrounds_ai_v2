# Диагноз SilverFir и подтверждённые ограниченные исправления

09.10.2026. Исследование source metadata, shadergraph и binary FBX; native evidence слотов
получено от единственного Unity owner. Shader/material/FBX source этим исполнителем не изменялись.

## Материалы: подтверждённая ошибка

До source AddRemap владельца `SM_SilverFir_M_01.fbx.meta` externalObjects указывал Atlas на
`1f4cf8103c0dc5d43838c4049b59acf6`, Bark на `a881e77283126f64b808979197d0c05f`.
На момент диагностики оба GUID не разрешались ни в одном Assets meta. Скопированные source MAT имеют GUID
Atlas `abc00000000016780034756723253600`, Bark `abc00000000002588266041845012720`.
Owner native probe подтвердил: все4 raw FBX LOD имеют sharedMaterials=[null,null],
scene copies после старого Tree() имеют [Atlas,Atlas]. Fallback по имени/null терял Bark slot.

Read-only binary FBX parser подтвердил порядок на всех уровнях: slot0=MI_SilverFir_Bark_01,
slot1=MI_SilverFir_Atlas_01. Root разрешил precise code fix: каждый renderer должен иметь
MeshFilter/sharedMesh с2 submesh, иначе исключение; назначается ровно [Bark,Atlas].
Материалы загружаются из существующих правильных source paths, missing MAT → исключение.
Исправление сохранено; source AddRemap делает только главный owner. Ни fallback Atlas для
неизвестного слота, ни изменение tree count не остались в текущем Tree().

## Atlas/alpha: что действительно проверено

Фактически открыт `Textures/T_SilverFir_01_D.png`:4096×4096 RGBA, alpha0..255, corner alpha0.
Текстура содержит тонкую приглушённую зелёную хвою, светлые серо-коричневые веточки и полосу коры.
Active S_Foliage property `_BaseMap` правильно указывает этот D atlas GUID
`abc00000000008247812855925903405`; `_NormalMap` и `_ORMH` используют SilverFir N/M.
Legacy Material_Texture2D_0 с EagleFern normal в MAT не является active property графа;
его нельзя ошибочно объявлять текущим base-color texture.

Граф имеет UniversalTarget, both-face rendering и alpha clip. `_AlphaClipping=.25`,
`_Tint=(1,1,1)`, `_Saturation=1`: явного material desaturation/tint-to-grey не обнаружено.
Texture metadata: mipmaps on, mipMapsPreserveCoverage0, alphaIsTransparency0,
alphaTestReferenceValue=.5 (не активен для coverage пока coverage выключен).
Fine needles могут терять плотность при mip/downsample и давать алиасинг; это дополнительная
гипотеза, не доказанная этим code fix. Metadata maxTextureSize неоднородны: top8192,
DefaultTexturePlatform1024, Standalone/Android8192 с overridden0. Effective native texture
dimensions/format следует снять владельцу, не угадывать по одному полю YAML.

Следующий evidence gate: rebuild после explicit mapping/AddRemap, снять один instantiated
tree по4 LOD с submesh/material names/GUID/shader/active BaseMap+native width/height,
повторить два window и центр captures. Только затем решать coverage/tint/плотность.
Если корректное изображение всё ещё мало зелёное, raw texture/style или LOD могут быть
причиной; не увеличивать104 тонких M_01 автоматически. Shared source не перекрашивать.

## Реальный source geometry budget

`reports/implementation/exterior-paid/fir-fbx-slot-audit.json` — actual binary source audit;
все полигоны треугольные, это не Unity imported GPU mesh count.

| LOD | Всего tris | Bark tris | Atlas tris |
|---|---:|---:|---:|
| 0 | 11809 | 4632 | 7177 |
| 1 | 5905 | 1026 | 4879 |
| 2 | 2953 | 25 | 2928 |
| 3 | 1477 | 8 | 1469 |

66 far trees при одновременном LOD3 дают97,482 tris;38 near/mid при LOD1 дают224,390 tris.
Это консервативные суммы, не measured visible frame cost; frustum/LOD могут уменьшать число.
Прежний20k far target не достигнут такой полной суммой. До дополнительного source audit
и native visible counts104 дерева сохранены; увеличение не предлагается.

Near layer: боковые островки20–35 м для окон/поворотов. Mid layer: сохранить фланги и не
закрывать |X|<11 на протяжении shooting lane до45 м. Far layer: после улучшения source
сначала шире разнести имеющиеся деревья внутри островков и перекрыть силуэты в видимых
секторах; менять количества только после LOD/triangle/overdraw evidence.

## Горизонт: root approved actual geometry

Два finite ridges заменены единым closed terrain belt.96 angular segments,5 кольцевых рядов
радиусов200/220/240/260/280; inner/outer y−10 относительно ground, crest6..13 по ограничению.
Между основанием и гребнем промежуточные пологие склоны; smooth sine/Gaussian profile.
Аналитические sampled crest min6.56/max12.28,480 vertices/768 triangles. Shared seam vertices
замкнуты modulo96: прямоугольных торцов нет. Один own muted grey-blue material, без Collider,
cast/receive shadows Off. Source assets не изменялись; mesh создаётся при Build в own Exterior.
Far Ground continuity остаётся задачей другого owner — собственное основание не добавлялось.

Own Roslyn compile после Tree mapping и approved belt: exit0.
Frozen C# SHA `E9959CEA8686F4359A9BEFDC72412AC467A4888E4E2D91E648501CDDC355D2E7`.
Native rendered quality/Quest/user acceptance ещё не заявляются passed этим исполнителем.
