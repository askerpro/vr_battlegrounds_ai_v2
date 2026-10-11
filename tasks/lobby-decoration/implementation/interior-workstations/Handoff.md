# Дополнение рабочих зон — 2026-10-10

Замороженный offline helper: `LobbyInteriorWorkstationsBuilder.Build(Transform parent, Bounds arena, Bounds tent, Bounds[] exclusions)`. Интегратор передаёт parent=InteriorDecoration после существующего Sidewall, после `Physics.SyncTransforms`. Создаёт только новый child `InteriorWorkstations`, не меняет сценовые столы или существующие помощники. FindSurface учитывает Table_* ancestor реальных collider Top/BoundaryBlock и выбирает highest maxY<1.2; декор самого parent также включён в препятствия.

## Размещения

Координаты X/Z относительно arena.center; Y снизу = arena.max.y+.005.

| Группа | Центр X/Z | Максимальный world envelope XYZ | Источник |
|---|---|---|---|
| Западный металлический шкаф | −7.28 / −4.68 | .68 × 1.4 × .62 | SM_cabinet_b |
| Восточный металлический шкаф | +7.28 / +4.70 | .68 × 1.6 × .82 | SM_cabinet_a |
| Западные кейсы, 2 стопки по 2 | −7.48 / +5.50 и +6.72 | каждый .36 × .28 × .34 | SM_AmmoCase_01 / _02 |
| Восточные кейсы, 1 стопка из 4 | +7.43 / +5.34 | каждый .36 × .28 × .34 | SM_AmmoCase_01 / _02 |

Шкафы по X вычисляются через tent.min/max+.47, Z по arena.center. Это верхние ограничения, источник никогда не увеличивается: scale=min(1, envelope/source). Нативные итоговые размеры фиксирует generated latest-build.json. Все исходные LOD сохраняются; collider, Light, Animator и любые скрипты удалены только с новых инстансов, shadows отключены, BatchingStatic не выставляется. Материалы/source assets не изменяются.

Западные стопки требуют существующего активного BoxCollider столешницы Table_West: опора выбирается по точке X−7.45/Z+5.55, измеряется после SyncTransforms. Только совпадающий Bounds этой опоры убирается из passed exclusions для предметов сверху; проверяются края столешницы. Остальные exclusions и точные bounds всех MeshFilter существующих Interior/Sidewall, включая все LOD, остаются запретными. Нижний кейс — разрешённая опора собственного верхнего, полная bounds стопки запрещает пересечение соседней стопки.

## Необходимые действия интегратора

Удалить старые два вызова ChairNiche и ArmorNiche, как согласовано ведущим; шкаф/стопка иначе получают excluded, не force-placement. Дополнительных вырезов старых столов не требуется. Восточные две стопки по две не помещаются между новым шкафом (maxZ≤5.11) и существующим desk (minZ≈5.70): выбран один компактный четырёхпредметный столбик, maxY≤floor+1.125. Стулья/стойка жилетов явно отмечены needs_free_niche; переносить их в центр или sweep нельзя.

Наблюдаемые четыре worktables: два уже существующих платных Sidewall стола + активные Table_North/Table_South с MeshRenderer. Count — структурная проверка, не доказательство узнаваемости металлической поверхности: нужен native art review. Несколько шкафов: два новых плюс существующий ClothesCabinet. Нативная проверка внешнего вида cabinet_a/b также у ведущего.

## Проверки и пределы

`offline_compile.ps1`: exit 0 против установленной Unity 6000.4.1f1 Managed API и текущей VrBattlegrounds.dll. `git diff --check` scoped: exit 0. GUID новой .meta d688f798f0ab40b6885bb3fb8d0d956a проверен по Assets до записи, совпадений нет. Fresh guard успешен перед code/artifact writes. Unity/MCP, Scene, другие helpers, vendor assets, gameplay/tests, commit не изменялись.

Лог: `tasks/lobby-decoration/reports/implementation/interior-workstations/offline-compile.txt`. Нативный Build ещё не выполнял этот исполнитель. Его latest-build.json должен подтвердить cabinets≥2, west/eastCases≥3, worktables≥4 и minimumCompositionMet=true. При missing/blocked helper оставляет явную причину и не объявляет состав готовым. Отдельно требуется визуальная оценка компактной восточной стопки, читаемости столов/шкафов и свободного доступа обоих арсеналов.

BaseBuildings затем реализованы и переданы ведущему; данный исполнитель больше их не изменяет. Их отдельный authoritative handoff — implementation/base-buildings/Handoff.md.

## Повторная сверка с горным референсом

Original `reports/references/exec-4d899d54-d590-4e86-9266-13b5de3c0417.png` повторно открыт через view_image 2026-10-10. Внутри читаются непрерывные боковые рабочие линии: вертикальные оружейные панели, настоящие столы, металлические комоды и шкафы, банки кейсов непосредственно под/рядом с оружием. Предметы организованы по назначению, центр остаётся широким свободным пространством. Мелочь дополняет полноразмерное оборудование; само количество мелких props не доказывает соответствие композиции.

На момент этой сверки Workstations/latest-build.json ещё отсутствует. Новые шкафы и 4+4 кейса — runnable план, а не подтверждённое размещение. Родные SM_AmmoCase с cap .36×.28×.34 могут читаться слишком мелко; это отдельный вопрос native art review, несмотря на count4. CountTables≥4 тоже не заменяет визуального подтверждения металлических рабочих столов. Root/ведущий уведомлены; дальнейшее изменение размеров/позиций требует actual all-LOD bounds и согласованной ниши. Common lighting остаётся у ведущего.
