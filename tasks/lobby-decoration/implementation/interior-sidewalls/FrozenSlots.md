# Frozen code после pass-02 review — 2026-10-09

Актуальная версия кода заменяет grid/search placements из прежнего Handoff.md. API unchanged: extended Interior вызывает Sidewall один раз. Scene split выполняет только интегратор; исполнитель не запускал Unity и не менял сцену.

Явные slots, worldXZ относительно arena.center:

- West paid ClothesCabinet(-7,28;+4,47), yaw92, maxXYZ(0,72;1,95;1,10).
- West paid Table(-7,28;−5,90), yaw90, maxXYZ(0,72;0,86;1,10).
- East paid Table(+7,28;+6,25), yaw−90, тот же maxXYZ.
- West chair/backpack(-7,25;−4,65), East chair/backpack(+7,25;+5,35).
- East vest rack(+7,28;+4,66).

Все floor anchors y=arena.max.y+0,005. Full actual mesh bounds после uniform fit проверяются с существующими table collider, station sweeps, mirror/window/column, neighbors и tent footprint. Плохой slot не переносится в проход. Source shared assets не меняются.

На два новых стола добавляются actual source ammo/case и разные личные наборы: настоящая кружка с кофе + мягкая небольшая сумка на западном; кружка + блокнот/карандаш на восточном. Их опора — actual paid table.maxY +0,006, а не старое0,965. MaxY≤1,19, никакой высокой радиоантенны под окнами. Проверяются individual group support/collision bounds. Window guards по Z расширены до±0,8, в соответствии с actual окнами ведущего.

Пять старых tabletop групп сохраняют name/role/variation независимо от пропусков в pockets list. NorthA=Communication0, NorthB=Maintenance0, SouthA=Communication1, SouthB=Maintenance1, WestEndB=Communication2. Backing surface Westpositive[5,10;7,02] сохраняет anchorWestEndBz5,87. Unsupported EndA у коротких side stubs пропускается по collider support, не создаёт competing bundle.

Источник SM_ToolRack не используется как напольная стойка: pass-02 показал tiny wall panel при прежнем yaw/scale, front-plane/native art пригодность не подтверждена. Разрешённый будущий wall mount — отдельное native проверенное решение над сохранённым столом; текущий code не ставит его наугад. Отдельная узнаваемая собственная vest rack уже включена.

LOD arrays сохраняются полностью. У всех source instance descendants выставляется только OccludeeStatic: **BatchingStatic под LODGroup отсутствует**. Собственные non-LOD prototype mesh могут использовать прежний static batching. Runtime components/collider/light удаляются только у decor instances.

Прогноз при подходящих actual source/table bounds: Interior5 tabletop +3 personal floor groups; Sidewall3 furniture +2 supplies +2 personal tabletop groups. **Это прогноз, не фактические placed counts.** Counts/positions/worldBounds/lodLevels/exclusion reasons дают прежние LastReport/JSON при единственном native Build.

Авторизованные scene cuts от интегратора: Westpositive retain[5,10;7,02], Westnegative retain[−7,02;−6,55], Eastpositive clear[4,10;7,02], Eastnegative remain[−4,5;−3,67] /[−7,02;−6,7]. Physics.SyncTransforms нужен ведущему после root/table changes и до измерения collider support.

Фактическая проверка final code: isolated offline C# compile обеих builders against Unity6000.4.1f1 + текущий VrBattlegrounds.dll, exit0, `reports/implementation/interior/offline-compile.txt`; scoped diff check exit0. Новые gameplay tests/коммиты/Unity операции не выполнялись. Native scene validation/capture и user acceptance остаются необходимы.
