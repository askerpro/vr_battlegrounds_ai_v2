# Перенос арсеналов внутрь физической арены — readonly контракт, 2026-10-09

Новый brief отменяет старые внешние ниши: шатёр по границе физической арены, обе станции и весь interior внутри. Размеры/поза PhysicalArena, её collider и существующие физические опоры сохраняются. Точное новое размещение не предлагается до actual native OPEN/sweep bounds. Старый InteriorDecorationBuilder не изменялся: дальнейшая адаптация принадлежит единственному scene/integration writer.

## Настоящий scene asset и владельцы

В Lobby две ссылки на `Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab`, GUID **88273a27f53bfdd4697ec9e56448a068**, не на CommonOpenArsenalStation. Scene PrefabInstance IDs **837297328** и **945201569**, исходный station root Transform fileID **5467481081662666445**. Evidence `Assets/Scenes/Lobby.unity:13002`, :13140..13156, :14083 и :14222..14237; actual prefab meta найден поиском GUID. Не заменять станцию/PrefabInstance другим prefab ради переноса.

`Docs/Arsenal/Arsenal_Code_Architecture_RU.md:3` и :7..9: корпус неподвижен, координатор владеет сетевым прогрессом, DeploymentAnimator применяет EquipmentPoses; карта владеет разметкой StationAnchor. Общий CommonOpenArsenalStation — архитектурное описание базового корпуса, **не конкретные габариты текущей LobbyDemo**.

`Assets/Scripts/Arsenal/ArsenalStationAnchor.cs:15..28` хранит ссылки Wall/Zone/StandingPoint/ArenaFacing. BoardFacing вычисляется из горизонтального ArenaFacing.forward. `:30..43` RaisedBoundsWorld преобразует authored local Bounds через **Wall.transform**, учитывая rotation/scale. `Configure:48..55` меняет только ссылки: позиции остаются у карты. Не нужен runtime setter корпуса.

`ArsenalStationCompositionBinding.cs:12..27` — пассивные stationKey/controller/anchor/equipment/identity и выбор visual set. `Assets/Scripts/Maps/Runtime/MapArsenalCompositionAdapter.cs:124..131` читает текущую мировую позу EquipmentPoses.transform (fallback station.transform), lossyScale, Anchor.RaisedBoundsWorld, zone/standing/facing. Не переписывает station root pose.

`ArsenalStationComposer.cs:89..145` создаёт свои слоты в рядах, не переставляет station root; `ArsenalSlotRow.cs:58` parent slots к row с worldPositionStays=false. Equipment rows/slots наследуют перенос. Нельзя вручную сохранить generated slot world poses после переноса или создать authored slots в сцене.

`ArsenalDeploymentAnimator.cs:25..49` PresentationRoot — ссылка для стенда, компонент не двигает корпус. `:60..69` обновляет server motion и применяет позы; `:102..105` вызывает только EquipmentPoses.Apply. `ArsenalEquipmentPoses.cs:31..38` пишет **Target.SetPositionAndRotation** по **мировым** ClosedPose/OpenPose.position/rotation. Поэтому direct edit Target/только открытой полки будет перезаписан следующим Update; перенос общего предка вместе с обоими маркерами устойчив. Не добавлять competing runtime writer, новый timer или authority gate.

## Какие transform/refs должны двигаться вместе

Данные **LobbyDemoArsenalStation.prefab**, scale всех перечисленных marker/root = one:

| Узел | fileID / локальная поза | Evidence |
|---|---|---|
| Station root | 5467481081662666445 | :1158 |
| PresentationRoot child station | 4280040810446499778, identity | :678 |
| DeploymentPoses child PresentationRoot | 8738906298995326711, identity | :2912 |
| UpperPanels_ClosedPose | 8645176917889688340, (0;1,018;0,22), X=-10° | :647 |
| UpperPanels_OpenPose | 1125340124447514629, (0;1,104;-0,029), identity | :970 |
| LowerShelf_ClosedPose | 5956452997588951081, (0;0,638;0,18), X=90° | :1948 |
| LowerShelf_OpenPose | 3655348876430089977, (0;0,823;-0,41), identity | :3850 |
| StandingPoint child station | 5110494148631432104, (0,085;0;-1,25) | :3881 |
| ArenaFacing child station | 1946677384923224775, position zero, Y180° | :1113 |

Equipment target transforms **2692291764026598094** (upper) и **3336248420757912670** (lower), component **1670979413833648386**; marker references `:1360..1366`. Anchor component **2506762683251235539**; Standing/Arena refs `:1343..1344`. Binding **5062873377450552718**, Equipment/Anchor refs `:1411..1412`.

Local authored RaisedBounds center(0,1312499;1,1485558;-0,0888657), extents(3,8262496;1,1485558;0,71886575), `:1345..1347`. FoldedBounds center(0,1312499;1,1475;0,2715), extents(3,8262496;1,1475;0,3585), `:1367..1369`. Это metadata-конверты, **не фактический полный renderer/collider sweep**.

Zone обоих scene anchors — существующая общая TeamSpawnZone reference `{fileID:6995293867679397340}`. Ссылки station key/identity/controller/zone не менять. `MapRoot.cs:125..132` проверяет StandingPoint/ArenaFacing присутствуют в той же сцене/Gameplay и не принадлежат другой станции. Они уже свои descendants; root translation перенесёт их вместе, не нужен Configure/reparent.

Нативный старый baseline `reports/implementation/station-target-poses.json` фиксировал восток (7,627;0;0,405), запад(-7,604;0;-0,405), full aggregate closed bounds≈1,582×4,022×7,683 м. Scene YAML западного instance имеет **local** (+7,604;0;+0,40899986), quaternion Y90°: parent повёрнут, мировая поза отличается! Ни знак local X, ни Quaternion Euler hint нельзя принимать за world placement. Использовать station.transform.position/rotation в единственной native операции ведущего, сохранять parent/scale.

## Канонический маршрут scene-authoring

1. До операции сохранить native snapshots двух станций: root world/local pose, parent, lossyScale, identity/station refs, anchor markers, вся физическая арена/опоры, закрытые/открытые/промежуточные реальные bounds. Это точка сравнения, baseline не заменяет свежего замера.
2. В Edit Mode, до runtime composition и без человеческого Play Mode, изменить **только world position (при необходимости согласованную yaw) существующего station root** в Lobby. Сохранить parent, локальный/мировой scale, children/collider размеры, marker local poses, NetworkIdentity sceneId и binding. Предпочтение — чистая трансляция внутрь с прежним inward-facing yaw, чтобы не менять физические размеры, владение и пользовательскую ориентацию.
3. Все descendants, Closed/Open markers и StandingPoint/ArenaFacing перемещаются одним корнем. Не тянуть отдельно PresentationRoot, Equipment.Target, slots или standing marker. После переноса BoardFacing должен смотреть внутрь, standing point и **полный OPEN+sweep** находиться в физической арене с запасом, не пересекать столы/колонны/навес/декор/другую станцию/стрельбу и доступ пользователя.
4. Записать только scene prefab overrides root Transform через штатный RecordPrefabInstancePropertyModifications и сохранить сцену ведущим. Никаких runtime переписывателей, prefab-global Apply, миграции/пересоздания станции, новых координаторов, смены зоны или network state.
5. Выполнить штатные validation/catalog действия ведущего для изменённой сцены; compile и existing checks по общему пакету. Снимок runtime placements по MapArsenalCompositionAdapter должен соответствовать новой scene-authored позе; один scene asset даёт одинаковые значения серверу и клиентам.

`Assets/Editor/VR_Battlegrounds/Arsenal/ArsenalMapMigration.cs:83..114` — **исторический migration**, не helper переноса. Он создаёт новые stations, выталкивает их front edge к boundary plane +0,008 и задаёт standing point на plane-0,6; в MapBootstrap scenes сам отказывается выполнять старую замену. Эти старые boundary-edge формулы противоречат новому brief и не должны повторяться. `LobbyRangeLayoutBuilder` также не запускать ради переноса: у него собственные задачи удаления/расстановки стен и мишеней.

## OPEN и sweep, реальные проверки

Closed/Open — progress 0/1 через EquipmentPoses. `EquipmentFraction:25..28` smoothstep монотонный 0..1; Apply:37..38 интерполирует translation и Quaternion.Slerp. Lower shelf поворачивается 90°→0° и сдвигается z=0,18→-0,41, y=0,638→0,823. Upper panels -10°→0° и z=0,22→-0,029, y=1,018→1,104. В середине вращения объём может выходить за endpoint union; CLOSED metadata или один OPEN AABB недостаточны.

`Assets/Tests/EditMode/Arsenal/ArsenalMotionGeometryAudit.cs:35..52` проверяет также **видимые статические MeshRenderer без collider**, а не только физические препятствия. `:56..102` клонирует движущуюся геометрию и markers в preview scene, вычисляет консервативный radius sweep и nearby obstacles; `:103..123` samples полный progress с ComputePenetration. Исходная сцена не двигается. Sweep sampling имеет minimum100 steps; :79 повышает число до ceil(1,5×distance/0,002), :80 до ceil(1,5×Quaternion.Angle/0,25). Проверить весь навес/шторы/новый interior как visual obstacle, даже без collider.

`ArsenalMapGeometryTests.cs:24..62` уже вызывает visibility + motion audits для scene maps, требует обе pose groups, проверяет сохранность всех original transforms, collider/renderer enabled, active state и clean scene. `ArsenalMotionGeometryTests.cs:34` ловит rotation sweep obstacle, когда оба endpoints чисты; :47 тонкие препятствия между прежними samples, :67 missing markers. Эти tests не запускались данным readonly исполнителем.

`LobbyLayoutTests.cs:80..89` проверяет общую нейтральную зону, StandingPoint/HasBoardDirection и dot(BoardFacing, inward)>0,5. Сам этот тест не доказывает containment внутри пола и свободный player standing footprint. Runtime-generated оружие/карточки/магазины могут выступать дальше authored корпуса: проверить compiled station/actual props, потому что composer не проверяет «влезает ли» (`Docs/Arsenal/Arsenal_Code_Architecture_RU.md:32..35`).

Нативный baseline пола (`live-baseline.json`): center(-0,000199794769;-0,5100739;0), size(15,4999943;1;18,960001). Это прежний реально измеренный snapshot, **не текущий замер**. Не выводить новый inset из 15,5×18,96 и closed aggregate вслепую; считать containment/sweep по новым actual native poses и всем видимым/физическим границам. Новые interior placements определить после этого, без старых outside-floor anchors.

Фактически выполнены только readonly rg/чтения C#/Docs/prefab/meta/scene refs и сравнение прежнего native evidence. Новых игровых tests/code/assets/Unity операций нет. Записан только данный artifact; fresh guard implementation epoch1 ok=true. Компиляция не запускалась, поскольку меняется только audit Markdown.
