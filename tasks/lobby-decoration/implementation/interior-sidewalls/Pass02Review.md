# Pass-02: фактический провал заполнения и конкретный следующий layout, 2026-10-09

Readonly review. Картинки реально открыты через view_image: player-center-north/south, east/west-window, south-edge-right, north-edge-left. Pass-02 внутри практически пустой: однотонные стены и пустые поверхности, никакой бытовой мелочи/стульев/шкафов. Окна дают вид на внешний реквизит, но не заменяют внутреннее оформление. Фото не подтверждают готовность задачи.

Первый report: interior placed0/excluded18, sidewalls placed0/excluded24/missing0. Missing sources не причина. Север/юг tabletop группы использовали collider.bounds z±9,25 при уже перенесённом transform z±7,25: физическая синхронизация отсутствовала в том же editor кадре. Это отдельный дефект чтения. **Все floor sidewall группы имеют другое реальное пересечение с существующими столами**: мебель center|x|≈7,28 + extents до0,36 накладывается на старые столы x≈±7,52, inner edge≈7,29. Physics.SyncTransforms само по себе эту геометрию не исправит.

После Sync свежий interior/latest-build.json подтверждает placed5/excluded12. Кадры pass-02 сняты до исправления и не показывают эти пять. Сохраняются:

- Table_North_EndA/B anchors x±6,60,y0,965,z+7,25.
- Table_South_EndA/B anchors x±6,60,y0,965,z−7,25.
- Table_West_EndB anchor(-7,52;0,965;5,87), actual bounds z≈5,2041..6,2431, x≈−7,6672..−7,3728, y≈0,965..1,373.

## Принятое art-направление

По решению root заменить **части** прежних side table блоков настоящей мебелью. Не сдвигать все группы к центру и не снимать глобально table exclusions. После scene split Builder получает фактические обновлённые collider bounds; новые furniture groups остаются в wall band≈0,7..0,9 м, центральный проход свободен.

Использованные свежие границы ведущего: tent z±7,48; station full sweep x≈6,026..7,490 по модулю, east z−3,613..4,160 / west z−4,160..3,613; mirror east x5,7..7,75,z−6,7..−4,5; окна east z5,2..6,8 и west z−6,8..−5,2, y1,25..2,35 с объёмом обзора. Margin collision0,055 м, tent0,04 м.

Все координаты ниже — **ограниченные кандидаты для native integration**, не проверенные состоявшиеся placement. floorY=arena.max.y+0,005≈−0,005074. Использовать observed source bounds/семейства из failed Sidewall report, затем повторно проверить actual full LOD bounds после постановки.

| Новая группа | Anchor world (x;y;z) | Требуемый actual z envelope / высота | Почему здесь |
|---|---|---|---|
| West paid ClothesCabinet | (−7,28;floorY;4,47), yaw+92°, maxXYZ(0,72;1,95;1,10) | z≈3,922..5,018, y≤1,81 | После sweep west max3,613; до сохранённого столового участка5,10. Window west на отрицательном z |
| West paid Table | (−7,28;floorY;−5,90), yaw+90°, maxXYZ(0,72;0,86;1,10) | z≈−6,448..−5,352, top≈0,73 | Под западным окном, низкая поверхность. Пол не занят дополнительным старым столом после split |
| West chair + backpack | (−7,25;floorY;−4,65), сохранённый inward orientation | z−4,90..−4,40, y≤0,945 | Между paid table и западным sweep min−4,160; ниже окна |
| East vest stand | (+7,28;floorY;4,66), без нового runtime поведения | z4,305..5,015, y≤1,515 | После sweep max4,160; перед window min5,20. Не в mirror zone |
| East chair + backpack | (+7,25;floorY;5,35), сохранённый inward orientation | z5,10..5,60, y≤0,945 | Под восточным окном; gap от стойки≈0,085 и от следующего стола≈0,102 |
| East paid Table | (+7,28;floorY;6,25), yaw−90°, maxXYZ(0,72;0,86;1,10) | z≈5,702..6,798, top≈0,73 | Под восточным окном; до north physical table min7,02 gap≈0,222 |

West cabinet source observation в существующем failed report: halfX0,3003, halfZ0,54815, height1,8028. Table observation: halfX≈0,343..0,349, halfZ0,54833, height≈0,73. East/west chair + pack extents Z0,25, source group height0,95. Rack extents Z0,355 по известной собственной геометрии. Это позволяет задать небольшое число явных slots вместо 12-cell grid, где крайние cells z±6,582 с halfZ0,548 уже накладываются на торцевые столы z7,02.

Физический NORTH/SOUTH стол и четыре текущих tabletop groups не трогать. Side furniture мировые x envelopes остаются примерно|x|6,92..7,67, что соответствует остановке walking area примерно0,8 м перед стенкой. Мебель не выходит за tent.maxX−0,04≈7,71. Window-chair/Table низкие; **новые tabletop props над ними должны иметь actual maxY≤1,19** с учётом clearance0,055 до window minY1,25. Коробки, кружка/кофе, блокнот подходят; высокую радиоантенну под окном не размещать. Личные props ставить по actual paid tabletop после uniform fit (он сейчас около0,73 м, не старое0,96 м), иначе они зависнут.

## Точные side table интервалы для scene split

Исходные интервалы из свежего сообщения ведущего:

- East negative: −7,02..−6,70 и −4,50..−3,55 (зеркало уже вырезано).
- East positive: 4,10..7,02.
- West negative: −7,02..−4,10.
- West positive: 3,55..7,02.

Предлагаемый минимальный split:

1. **West positive:** убрать прежний Body/Top/collider участок `[3,55;5,10]`; оставить `[5,10;7,02]` для существующего Table_West_EndB и исходного waist-high стола. Cabinet занимает `[3,922;5,018]`; между ним и remaining table gap0,082>0,055. Existing tabletop bundle z5,204..6,243 имеет опору.
2. **West negative:** убрать прежний Body/Top/collider участок `[−6,51;−4,34]`; оставить `[−7,02;−6,51]`. Старый короткий хвост `[−4,34;−4,10]` убрать либо обрезать до `−4,215` по sweep margin (лучше убрать ненужный0,125-м stub). После удаления Desk z−6,448..−5,352 и chair z−4,90..−4,40 свободны. Gap desk min−6,448 до remaining table max−6,51≈0,062>0,055; если source yaw расширит actual bounds более чем на0,007, срез перенести к−6,54. Это margin-sensitive место, проверить native bounds.
3. **East positive:** убрать прежний Body/Top/collider участок `[4,10;6,86]`; сохранить corner remainder `[6,86;7,02]` только если нужен для визуальной непрерывности. Весь ряд заменяется rack4,305..5,015, chair5,10..5,60, paid desk5,702..6,798. Gap desk max6,798 до corner remainder min6,86≈0,062>0,055; если фактическая desk rotation даст шире, cut перенести к6,88 или удалить tiny remainder. Наличие платного low Table сохраняет использование этой боковины.
4. **East negative / mirror:** не заполнять mirror viewing z−6,7..−4,5. Corner table−7,02..−6,7 оставить. Второй остаток `[−4,5;−3,55]` обрезать в сторону станции до max `−3,67` (sweep starts−3,613, clearance0,055), либо удалить; новые props сюда не добавлять.

Изменять одновременно mesh и matching BoxCollider (если они принадлежат этой environment table piece), а не только видимый renderer scale. Проверить, что перед редактированием никакой retained piece не несёт PhysicalArenaShape/канонические опоры; решение о scene mutation и actual collider ownership — ведущему. Нельзя переносить/переопределять station equipment markers, PhysicalArena или физические колонны ради этих slots.

Перед следующей сборкой: scene overrides → Physics.SyncTransforms → read actual support/table/sweep/mirror bounds → сохранить пять существующих bundle anchors/содержимое → удалить прежние decor группы через sole authoring → создать bounded replacement groups → native validate → captures. Смена доступных surfaces меняет число pockets: **не привязывать Communication/Maintenance family к общему list index**, иначе contents Existing West_EndB при trim могут смениться. Его имя/world anchor5,87 и backing surface[5,10;7,02] должны остаться стабильными; выдавать ему один supported bundle, не пытаться разместить оба противоположных ends в коротком remaining block.

## Дополнительный art hazard

Source SM_ToolRack в failed report получился tiny upright panel: высота≈0,417, толщинаZ≈0,036 при ширинеX0,72. Это evidence неправильного отношения к wall-mounted предмету и слишком сильного uniform fit, а не готовая напольная стойка. Пока не использовать его как floor rack/cabinet. Если root захочет wall-mounted tool panel, отдельно подтвердить native source plane/нормаль и разместить над столом вдоль Z, с правильным inward normal±X, вне window/mirror/sweep. Наш собственный vest stand в основном предложении узнаваемо имеет ножки/перекладину и сохраняет отдельное назначение.

Проверено чтением только JSON/authoring/current layout helper и шести PNG. Код, assets, scene/Unity/tests не менялись. Записан один review artifact, fresh guard epoch1 ok=true. Exact proposed placements ещё должны пройти full actual bounds/geometry review после scene split. Состояние задачи — размещение исправляется; interior не принят.
