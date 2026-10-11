# Pass-05 readonly: side band и причина незавершённой крыши, 2026-10-09

Открыты через view_image pass-05 player-north-edge-left/right, player-south-edge-left/right, player-west/east-window, ref-forest-match и player-center-north. Прочитаны live-layout.txt и три latest-build.json (native snapshot около19:31UTC). Scene/MCP/code не менялись в рамках review.

Interior placed8/excluded0; Sidewalls placed7/excluded0/missing0/invalid0. Шкаф, два низких стола с кофе/мелочами, два стула с рюкзаками и стойка жилетов теперь фактически видны у стен. Центр свободен. Side/window кадры показывают доступный обзор; ни высокий шкаф, ни стойка не стоят перед соответствующим окном.

| Группа | Actual X bounds | Actual Z bounds | MaxY | Отступ внутреннего front edge от стены |
|---|---|---|---|---|
| West шкаф | −7,5814..−6,9789 | 3,9200..5,0196 | 1,8032 | 0,7713 м |
| West paid desk | −7,6240..−6,9364 | −6,4500..−5,3500 | 0,7332 | 0,8138 м |
| East paid desk | 6,9360..7,6236 | 5,7000..6,8000 | 0,7332 | 0,8138 м |
| West chair/backpack | −7,6697..−7,0402 | −4,9000..−4,4000 | 0,9449 | 0,7100 м |
| East chair/backpack | 7,0434..7,6693 | 5,1000..5,6000 | 0,9449 | 0,7064 м |
| East vest stand | 7,0298..7,5298 | 4,3050..5,0150 | 1,5199 | 0,7200 м |

Новые группы читаемо останавливают walking space примерно0,71..0,814 м до sidewall, соответствуют0,5–1 м. Window lowerY1,25: low desk supplies maxY0,9392 оставляют≈0,311 м; chairs maxY0,9449≈0,305 м. Vest stand высокий, но заканчивается z5,015 перед east window min5,20 (gap0,185 м). West cabinet на положительном z, west window отрицательный.

Гапы между новыми groups не нулевые: Westdesk→chair≈0,45 м поz; Eaststand→chair0,085м; Eastchair→desk0,10м; cabinet→retainedWesttable≈0,080м. Полоса не является непрерывной физической оградой: у декора Collider отсутствуют. Это намеренный визуальный front edge; фактические walking/teleport границы принадлежат физической арене.

Functional exceptions: большие существующие арсеналы/sweep занимают центральные side spans eastz−3,613..4,160, west−4,160..3,613 и inwardX≈6,026 (до sidewall≈1,724м); размещать там мебель для формального0,5–1м запрещено. Mirror east x5,7..7,75/z−6,7..−4,5 оставляет viewing/access нишу глубиной≈2,05м. Старые retained tabletop lips frontX≈7,29 дают≈0,458м — близко к пользовательскому «примерно0,5».

Root art decision по этому review: новый band0,71..0,814 принят как соответствующий; старые0,458м допустимы, не двигать их ради4см. Arsenal/mirror — функциональные исключения, центр свободен. Это scoped art decision, не общая user acceptance всей сцены/крыши.

## Roof evidence и диагноз

Pass-05 всё ещё показывает зубцы/дырки. Native report подтверждает реальный поворот: unrotatedRidgeAxis=X, rotatedRidgeAxis=Z, rotationY90. Footprint/eave корректны (±7,75/±7,48, minY4,189926). Но эти envelope/axis проверки **не доказывают**, что ткань — связная оболочка без дырок.

Readonly декодирование сохранённых Mesh asset CPU buffers: Float32 position stream stride56, UInt16 cloth indices. LOD0 indexCount11949/3=3983 совпадает с новым report, значит saved CPU mesh действительно новый; дефект нельзя объяснить лишь старым JSON. Geometric position weld с округлением1e−4м, undirected edge counts:

| LOD | Cloth tris | Boundary edges | Boundary edges не на Y4,189926/X±7,75/Z±7,48 planes | Nonmanifold edges valence>2 |
|---|---|---|---|---|
| 0 | 3983 | 1497 | 889 | 0 |
| 1 | 2032 | 956 | 519 | 0 |
| 4 | 279 | 223 | 99 | 0 |

Примеры внутренних boundary verticesLOD0: (6,5215;4,2120;−7,3974)→(6,6184;4,1899;−7,3877), (6,5504;4,2486;−7,3719)→(5,8866;4,2804;−7,3727). Это рваные открытые границы внутри footprint и выше ровного eave, не только разрешённый perimeter. У фабричных UV seams позиционный weld приблизителен; отдельно каждый счётчик не доказывает число физических дыр. Но magnitude, внутренние координаты и фактические spikes в кадре согласуются с разорванным source subset.

Конкретная причина подхода: angular per-triangle mask |normalY|>0,2 удаляет соседние nearvertical/fold faces и допускает разрозненные cloth patches; Sutherland clipping корректно режет только **сохранившиеся** polygons и не восстанавливает выброшенную связность/контур исходного submesh. Envelope/UV/rotate checks исправлены, connected roof topology не была установлена. Ещё один Yclip, ForceLOD0 или atlas tiling не устранят эту причину. Source shader/UV также не являются объяснением CPU internal boundaries.

Ведущий после review зарезервировал единственный own connected roof fallback (grid/skats/PBR clean patch); никаких roof изменений до этой reservation review-исполнитель не делал. Native capture/connected topology acceptance этой новой реализации — отдельный следующий результат.
