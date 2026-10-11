# Interior + Sidewalls — актуальный handoff, 2026-10-09

**Этот контракт заменяет прежний outside-floor layout в implementation/interior/Handoff.md.** Пользователь сократил шатёр до arena.width × (arena.length−4), по 2 м внутрь обоих торцов. Старые координаты chair x±8,07 и внешняя стойка полностью удалены из выполняемого кода.

## Единственный вызов интегратора

`LobbyInteriorDecorationBuilder.Build(parent, scene, arena, tent, stationSweeps, mirrorViewArea)`.

Он вызывает `LobbySidewallDecorationBuilder.Build(root, arena, tent, exclusions)` ровно один раз. Главный авторинг не должен звать Sidewall отдельно. Старый overload `(parent, scene, floor)` сохранён для временной совместимости компиляции, но всегда бросает понятное исключение о необходимости нового API. Он не собирает устаревшие внешние ниши.

arena/tent — актуальные native измерения/горизонтальные конверты, а не hard-coded 15,5×18,96. Проверяется равенство ширины и сокращение длины на 4 м. stationSweeps должен содержать два полных объёма движения, не CLOSED-only AABB. mirrorViewArea — объём доступа/обзора функционального зеркала, не только плоскость стекла. Builder читает именованные Column/Pillar/столб collider ancestors в сцене, активные четыре table tops и физические table collider. Отдельные объёмы обзора окон: east z=center+6 / west z=center−6, глубина внутрь 1,05 м, высота 1,20 м и ширина по z1,40 м. Если фактические окна/колонны имеют другие имена/размеры, интегратор должен дополнить этот контракт перед native Build.

## Сборка/проверка размещения

Настольные группы измеряются по верхним активным BoxCollider существующих Table_North/South/East/West. Физические столы/станции/зеркало builder не двигает. Неподдерживаемые краевые поверхности записываются как insufficient_surface, не заменяются floor props.

Кейсы и стулья используют реальные MilitaryCamp prefab GUID. Мелкие мешки/брезент/радио/инструмент/жилеты/рюкзаки остаются собственными обозначенными прототипами; кружка — ранее импортированный настоящий HHF source. Два chair + backpack и одна vest rack ищут свободные позиции **внутри боковин**, с wall offset0,50 м и несимметричным набором z candidates. Все реальные mesh.bounds всех LOD преобразуются в world AABB. Нарушающие tent XZ margin0,04 м либо sweep/mirror/window/column/neighbor margin0,055 м группы удаляются с причиной в отчёте. Недостаток личных ниш фиксируется как insufficient_space; центральный проход не используется как fallback.

Sidewall делит usable z длину на ячейки pitch≥1,18 м; меняет семейства по двум бокам, оставляет небольшие yaw±1..2°. Furniture maximum world envelope X0,72 м, Z≤1,10 м, высота по классу0,62..1,95 м. Центр мебели wall offset0,47 м: внутренний край при полной толщине находится ~0,83 м перед стенкой. Шкафы, тумбы, столы, стойка инструмента и ящики берутся из source assets, без primitive fallback. Поверх столов/тумб добавляются настоящие ammo packages/case только при опоре на actual top/bounds и отсутствии пересечений. Fixed physical table collider участвуют в furniture exclusions: builder не накладывает новый шкаф/стол на сохранённый игровой стол. При нехватке места вследствие длинных боковых столов/станций отчёт будет показывать excluded/insufficient_space; менять их canonical layout может только интегратор.

Проверка Inside относится к **горизонтальному XZ footprint**: source TentBounds(arena) сохраняет floor Y-size и не является высотой крыши. Полный world Bounds каждого экземпляра записывается в JSON; ограничения по источникам мебели задают высоту≤1,95 м. Фактический зазор до крыши/скатов, узнаваемость/натуральный масштаб компактно вписанных моделей и плотность боковин нужно подтвердить native preview. Если tent задан полной высотой, текущий helper всё равно использует XZ — не считать это полной проверкой крыши.

Все source LODGroup/LOD arrays сохраняются целиком, без ограничения числом уровней. У созданных decor instances сохраняются только Transform, MeshFilter, MeshRenderer, LODGroup; Collider, Light, Animator, Rigidbody, AudioSource и scripts удаляются. Материалы/source assets не меняются. Static flags — BatchingStatic/OccludeeStatic; никакого gameplay/network/teleportation/light ownership.

## Source GUID needs (копирование — единственный владелец пакета)

| Source | GUID |
|---|---|
| SM_ClothesCabinet | abc00000000018269680259753661502 |
| SM_Table | abc00000000009853783468641101578 |
| SM_cabinet_a | abc00000000015552206632912591366 |
| SM_cabinet_b | abc00000000004349650931825483885 |
| SM_ToolRack | abc00000000010134110494958977669 |
| SM_MilitaryCrate_01 | abc00000000015158636773066772218 |
| SM_Chair | abc00000000015024090355226227209 |
| SM_AmmoCase_01 | abc00000000012017236203265661070 |
| SM_AmmoCartridgeBoxes_01 | abc00000000007223829749494322384 |
| Реальная кружка HHF | abc00000000004222768201726487176 |

GUIDToAssetPath работает независимо от итогового shared source пути. При отсутствующих обязательных chair/case/mug Interior preflight сохраняет missingSourceGuids и fail-fast. Sidewall отсутствующий optional family пишет missing_source и не подменяет кубом. Principal сообщил, что общая closure этих furniture assets READY; исполнитель не копировал/не менял shared источники.

## Возвращаемые результаты и фактические проверки

Оба класса дают публичный `LastReport`. Interior: arena/tent, placed/excluded, missingSourceGuids, placements. Sidewall: arena/tent, placed/excluded/missing/invalid, placements. Placement: name, sourceGuid, world position, full world bounds, суммарное число source LOD levels, decision. JSON сохраняется в ignored `reports/implementation/interior/latest-build.json` и `reports/implementation/interior-sidewalls/latest-build.json` при native Build. Пока Build исполнитель не вызывал, поэтому **фактических placement counts/сценных JSON ещё нет**.

Файлы исполнителя: изменён LobbyInteriorDecorationBuilder.cs; добавлен LobbySidewallDecorationBuilder.cs и автоматически созданная Unity meta GUID14f7e2a5fa0cb7140b36fe7f66c2b057; updated interior/offline_compile.ps1 теперь компилирует оба builder; этот handoff. Shared authoring/scene/gameplay/source/helper не менялись.

Фактическая isolated C# compilation against Unity6000.4.1f1 + текущий VrBattlegrounds.dll после всех изменений: exit0, report `reports/implementation/interior/offline-compile.txt`. GUID нового script найден ровно один раз в Assets; прежний Interior script meta не изменён. `git diff --check` assigned scope: exit0. Это не Unity scene/Android/визуальная приёмка. Новые игровые tests не писались. Интегратор запускает существующие геометрические/scene проверки общего пакета и обеспечивает user acceptance.

Существующий read-only `Tools/agents/asset_pairs.py --source index` выполнен: passed=true, 8975 file pairs, 0 нарушений; полный report `reports/implementation/interior-sidewalls/asset-pairs-index.json`. Scope — tracked Git index, новый untracked builder этим аудитом не покрывается; его пара .cs/.meta и уникальность GUID проверены отдельно выше.
