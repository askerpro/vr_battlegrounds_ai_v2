# Внешний лагерь — пакет авторинга

09.10.2026. Реализован offline helper `LobbyExteriorDecorationBuilder.Build(parent, floor)`.
Editor/сцена этим исполнителем не запускались и не изменялись. Принятие пользователем ещё не выполнено.

## Композиция

Фактически просмотрены все четыре PNG из `reports/references/`: переносится приглушённая
оливково-песочная палитра, матовый металл/дерево, рабочие столы и складские детали,
слои леса/гор и боковой вертикальный акцент вышки. Концепты не являются Unity-снимками.
Современные ограничения по шторам, двум окнам и физической арене имеют приоритет.

Группа `ExteriorDecoration` создаётся identity child общего авторинга. Отдельного основания,
канопы, света, terrain, Collider, поведения или доступной игровой территории helper не создаёт.
Направления ±Z остаются широкой центральной перспективой: высокие лагерные объекты отнесены
к флангам. Низкие камни у ±X10.8/11.1, ±Z12.8/12.9 имеют высоту .22–.26 м.
Открытые края дают вид на разные лагерные группы, а боковые окна — на реквизит и близкие лесные островки.

| Группа | Центр X/Z, м | Yaw |
|---|---|---|
| NorthEastMaintenance | 16 / 15.8 | -24° |
| SouthWestMaintenance | -16.2 / -16.1 | 152° |
| NorthWestSupplies | -17.2 / 24.5 | 18° |
| SouthEastSupplies | 17.5 / -25.2 | -16° |
| EastWindowWorkPoint | 18.5 / 6.8 | -78° |
| WestWindowSupplies | -18.5 / -6.5 | 74° |
| StaticWatchtower | 24 / 25 | -12° |
| FuelDepot | -24 / -25 | 18° |

Складские варианты меняют угол ящиков, положение малого ящика и длину мешков;
у юго-восточной группы малый ящик стоит на земле вместо второго яруса.
В рабочей группе у восточного окна нет генератора. Это не одна повторённая площадка.

## Состав и ограничения

- 6 рабочих/складских групп: 2 импортированных генератора, 19 импортированных ящиков
  включая ящик вышки, 3 стола, 13 статических бочек, 6 закрытых оружейных кейсов.
- 71 одиночный мешок в коротких штабелях; исходный CC0 derivative SingleSandbag имеет
  12 треугольников и геометрически упрощённую форму, не фотоскан тканевых складок.
- 1 статическая вышка 7.25 м с лестницей, 1 импортированный подлинный Oil_tank_v1,
  3 раздельных металлических забора (10 пролётов суммарно), 2 столба и 3 рабочих светильника.
  У светильников 5 пустых `ExteriorLightSocket`; Light helper не добавляет.
- 66 импортированных SilverFir: 38 деревьев в шести островках, 28 на дальнем кольце.
  Островки: (27,2), (-27,-2), (33,33), (-30,36), (31,-37), (-34,-33);
  радиусы 2–6.4 м, высоты 8–12.5 м. Дальний лес 74–87 м, высоты 11–17 м.
- 40 каменных объектов включая 12 дальних ridge silhouettes на радиусе 175 м.
  Камни/силуэты используют один собственный 35-треугольный mesh, разные масштабы/повороты.
  Горы — честный lowpoly prototype, не фотореальная модель гор из PNG.
- 6 неправильных плоских грунтовых пятен, mesh 12 треугольников; выбранные подлинные
  Sand1 A/N textures, импорт <=1024 px, normal strength .35. Грунтовый mesh процедурный,
  без физики. Общий OuterGround остаётся ответственностью ведущего.

Исходные Imported assets не редактируются. Создаваемые assets ограничены
`Assets/Art/Lobby/Exterior` и `Assets/Materials/Lobby/Exterior`. Материалы реквизита URP/Lit,
матовые, instancing включён. SilverFir использует существующие исправленные URP материалы
Bark/Atlas без изменения source. Инстансы очищаются до Transform/MeshFilter/Renderer/LODGroup.
Realtime shadows Off, probes Off, motion vectors ForceNoMotion. Static batching намеренно
не навязывается всем деревьям: отмечен только OccludeeStatic.

LOD деревьев: .5/.3/.15/.012, без crossfade. Ящики .45/.23/.10/.015 для присутствующих
именованных LOD уровней. Raw FBX не оставляет все уровни дерева одновременно без LODGroup.
Требуется проверить native render/material resolution и renderer counts после Build.

## Проверено фактически

- Guard implementation epoch 1 успешен; main git dir совпал с common dir; ветка lobby-decoration.
- Roslyn компиляция helper против установленной Unity 6000.4.1f1: exit 0, без diagnostics.
  Команда: `powershell -NoProfile -ExecutionPolicy Bypass -File tasks/lobby-decoration/implementation/exterior/offline_compile.ps1`.
  Лог: `tasks/lobby-decoration/reports/implementation/exterior/offline-compile.txt`.
- Статический поиск: нет Debug.Log, NetworkIdentity, добавления Light, Collider, Animator,
  Animation; DestroyImmediate применяется только к instantiated components/LODGroup.
- Git diff whitespace check не заменяет анализ нового untracked файла; offline compiler
  проверил именно полный новый исходник helper.

## Оставшиеся проверки ведущего и пользователя

Запустить общую сборку, проверить точные bounds всех фланговых props, 10 мишеней из центра
и четырёх краёв арены, вид через два окна с y≈1.6 и повороты назад/вбок у обоих открытых торцов.
Проверить material errors, renderer/shadow/LOD/collider/script inventory, memory/drawcalls
на Quest и отсутствие визуального приглашения пройти за физический край. Без этих результатов
Unity/Android/Quest и визуальное принятие не заявляются пройденными.

Маскировочная сеть ожидает целого проверенного источника от ведущего; складские мешки ею
не подменяются. Машина добавлена как явно обозначенный lowpoly candidate, описание ниже.

## Selected source follow-up

По поручению ведущего фактически просмотрены T_Ground_BC.png и T_Sand1_A.png из каталога:
первый содержит лесную подстилку с хвоей/листвой, второй — нейтральный зернистый песок.
Для рабочих пятен выбран Sand1; лесная текстура не копируется ради неизбыточного набора.
TGA бака открыт после конвертации только формата в ignored PNG-preview: светлый металл,
клёпка, умеренные следы ржавчины. Это источник Oil_tank, не переименованный посторонний prop.
Деревянная лестница ModularWoodenBuildings и резервуар TheSewers inspected serialized;
их зависимости не копируются, металлический prototype лестницы вышки сохранён.

Источник: `F:/UnityProjects/EnvironmentPackCatalog/Assets/env_packs/`.
Доступных дубликатов выбранных GUID перед переносом не было. Поддерево destination:
`Assets/Art/Lobby/Exterior/Imported/`.

| Asset | GUID |
|---|---|
| RPG_FPS_game_assets_industrial/Oil_tanks/Oil_tank_v1/Oil_tank_v1.prefab | b2be9b4ffc291194c96942bd60bdc25c |
| Source/Oil_tank_v1.FBX | f0722bb0f9795f24eb2e7c82f55738a6 |
| Source/Oil_tank_v1.mat | 8573c79e2e3e6c14c8dc545c942c8a60 |
| Source/Oil_tank_v1.tga | c74571de37cd5b647ade058cabb457a7 |
| PostApocalypticTown/URP/Art/Textures/LandscapeMaterial/T_Sand1_A.png | abc00000000002094398110741054851 |
| PostApocalypticTown/URP/Art/Textures/LandscapeMaterial/T_Sand1_N.png | abc00000000010966334330632806938 |

Скопированы ровно 6 payload + 6 исходных meta; новые папки имеют собственные meta.
GUID source сохранены. Только copied texture metadata адаптированы maxTextureSize<=1024;
payload SHA каждого файла совпадает source. Материал бака использует существующий package
URP/SimpleLit, дополнительные shaders не копируются. Исходные prefab/model могут содержать
Collider: scene copy очищает Sanitize; после native Build требуется инвентаризация компонентов.
Авторинг находит source по GUID, не навязывает новый путь повторно используемому asset.
Высота бака задана 2.2 м через реальные renderer bounds при Build; native preview и его
соседство с бочками ещё не просмотрены, фактическая ширина не заявляется известной.

Provenance/хэши: `reports/implementation/exterior/selected-assets.json`.
Проверка `verify_sources.py`: passed=true, assets=6, errors=0. Scope: payload SHA,
уникальность выбранных GUID, разрешение prefab/material GUIDs и import limits.
Отчёт `reports/implementation/exterior/source-integrity.json`.
При GUID-indexing пропущены только три чужих Mirror meta без GUID; список
`guid-index-skipped.json`, файлы не исправлялись и не изменялись.
Roslyn после source integration: exit 0. Native import/Unity/Android/Quest не выполнялись.

## Supply truck candidate

По отдельному поручению выполнен ограниченный offline конвертер по reference логике
`prepare_assets.py`, без импортирования/исполнения общего helper и без его side effects.
Source: `reports/acquired-assets/supply-truck.glb`,
`https://3dassets.dev/assets/armoured-column-and-field-camp-supply-truck-a0414d94`, CC0 1.0.
Проверен полный header/length и точный SHA256
`042ce7bc8dabe9af4dc3eb4a17f3b3627653dd7e2efe70ee3aa65672a1dd2ea2`.

Конверсия: `Assets/Art/Lobby/Exterior/Imported/Truck/SupplyTruck.obj` + `.mtl` и meta.
Иерархические transforms применены, normalized quantized accessors сохранены, determinant
учитывается для winding; сохраняются 8 material groups и source PBR цвета/roughness/metallic.
Unity source slots разрешаются по уникальному `truck_<index>_` префиксу в собственные URP/Lit
materials, прозрачность не добавляется. 744 triangles / 2232 vertices, source bounds
4.70×2.70×8.90 м из GLB-геометрии (это не native scene measurement).
`SupplyTruckCandidate` расположен на (22,-34), yaw 18°, authoring target height 2.2 м,
uniform scale без изменения aspect. Фактические native bounds/материалы требуют ведущего.

Проверенный глазами offline geometry preview: `reports/implementation/exterior/truck-geometry-preview.png`.
Это не Unity capture. Читается шестиколёсный cargo truck с olive cab/деревянной платформой,
но detail level явно blockout: колёса визуально отделены, тент плоский/угловатый, текстур нет.
Качество не равно PNG concepts; root art review и пользовательское принятие остаются pending.
Source/OBJ SHA, topology/counts/material data: `truck-conversion.json`.
После подключения и исправления совместимости LINQ с Unity API профиль: Roslyn exit 0.
