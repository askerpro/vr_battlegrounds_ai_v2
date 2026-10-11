# ShootingTarget — контракт замены визуала, 2026-10-09

Readonly исследование. Не менялись C#, prefab, scene, source-пакеты, настройки Unity или interior builder. Новый military source ещё не проверен; импорт HDRP/default в качестве финального источника не выполнялся.

## Рекомендуемый адаптер

Сохранить существующие десять корней/PrefabInstance, компонент ShootingTarget и всю физическую иерархию. Заменять только mesh/material существующего `Pivot/Plate` через editor adapter. Из выбранного URP military source брать реальный щит без стойки; нормализованный новый Mesh приводить к размеру существующего plate mesh (локальный envelope x/y/z≈±0,5) с лицом +Z, центром в (0,0,0). Существующие Plate transform/collider не масштабировать и не двигать. Собственные baked meshes/materials сохранить ассетами; никаких новых runtime-компонентов.

Если источник нельзя отделить на щит и стойку либо его визуальный контур сильно отличается от существующего hit rectangle, он не подходит под требование сохранения collider: выбрать другой источник. Полный prefab нельзя инстанцировать вместо gameplay-корня.

Старые `RingOuter`, `RingMiddle`, `Bullseye` оставлять как узлы с прежними transform/component fileID. Их MeshRenderer можно отключить, чтобы не накладывать круги поверх новой мишени. Нельзя деактивировать весь объект RingOuter: он остаётся явным центром фото/измерения. Рендерер Plate сохранить; исходный материал URP должен реагировать на `_BaseColor` или `_Color`. ShootingTarget остаётся единственным писателем MaterialPropertyBlock этого рендерера.

Стойку military source размещать отдельным visual-only child корня мишени **вне Pivot**, ориентируя к существующим Post/Foot. У imported instance удалить Collider/Rigidbody/Animator/скрипты/свет, не добавлять Ground/NetworkIdentity/UxrActor/UxrGrabbableObject. Post/Foot физические узлы, collider и fileID сохранить. Их старые renderers можно отключить; новые static MeshRenderer стойки не входят в flash/pivot photo. Не менять root/Pivot axes ради ориентации источника: поворачивать/запекать только визуальный mesh/child. Нельзя расширять стойку так, что выстрел по старому collider опоры выглядел бы попаданием в визуальный щит.

Сборка должна закончить визуал **до Awake/EnsureInitialized**. Prefab `_flashRenderers` сейчас пуст: ShootingTarget один раз кеширует все Renderers под Pivot. При замене только mesh/material прежних рендереров кеш и ownership сохраняются. Добавление нового board renderer после Awake недопустимо без отдельного обоснованного обновления кеша; SetPivot сбрасывает initialized, но не очищает уже заполненный `_flashRenderers`.

## Неизменяемые ID и физические величины

Prefab `Assets/Prefabs/Environment/Lobby/ShootingTarget.prefab`, GUID `f61633d1a47b0e5428a1140232fbb7cc`.

| Узел/компонент | fileID | Evidence |
|---|---|---|
| Root GameObject | 1047321241992423261 | prefab:3 |
| Root Transform | 4725157455750678309 | prefab:20 |
| ShootingTarget | 119023400935772993 | prefab:38 |
| Pivot GameObject / Transform | 9124133003302048363 / 5202872690175229223 | prefab:664 / 680 |
| Plate GameObject / Transform | 5230871049665357106 / 3997625040285362566 | prefab:238 / 257 |
| Plate MeshFilter / MeshRenderer | 4703857167406685188 / 823574351588950679 | prefab:272 / 301 |
| Plate BoxCollider | 1529918563781521835 | prefab:280 |
| RingOuter Transform / MeshFilter / Renderer | 8333480341267238254 / 3189505140952320561 / 2853436295909006481 | prefab:166 / 181 / 189 |
| RingMiddle Transform / MeshFilter / Renderer | 3214078776008952520 / 4216594604457376635 / 3602290048180096971 | prefab:368 / 383 / 391 |
| Bullseye Transform / MeshFilter / Renderer | 1217728115866379717 / 8425085114278706248 / 6786496894483757685 | prefab:76 / 91 / 99 |
| Post Transform / BoxCollider | 6166158280273730165 / 1704736543649238888 | prefab:459 / 482 |
| Foot Transform / BoxCollider | 6699710155056107622 / 4636273627636339520 | prefab:571 / 594 |

Не пересоздавать эти компоненты/объекты, не менять prefab GUID, layer/tag/active state физических объектов, collider enabled/isTrigger/center/size, root world pose, parent, name ShootingTarget_01..10 или RangeDistanceLabel. Post/Foot collider остаются снаружи Pivot.

Prefab Plate: local position (0;0,36;0), scale (0,6;0,7;0,03), BoxCollider size (1;1;1), center zero, layer0, tag Environment. Итоговая hit area 0,6×0,7×0,03 м. Pivot prefab y=1,0, но **в текущей Lobby y=1,14**; центр Plate/RingOuter получается y=1,50. RingOuter center local (0;0,36;0,017), scale (0,5;0,003;0,5), rotation X90°. Foot scale(0,5;0,06;0,35), y=0,03. Scene Post y=0,57, scale y=1,14, z=-0,05. Эти scene overrides не заменять prefab defaults.

Readonly YAML подсчёт: ровно **10** `m_SourcePrefab` references целевого GUID. Все десять имеют `_fallOnHit=0`, Pivot.y=1,14; placement z north≈19,419..38,394 / south≈-19,419..-38,394, разные x и quaternion. Пример `Lobby.unity:12601` — ShootingTarget_03; `_fallOnHit` override:12610, Pivot.y value:12671. Повторное выполнение старого layout-builder ради декора запрещено: он переставляет мишени и станции.

## Зависимости поведения

- `Assets/Scripts/Maps/ShootingTarget.cs:161`: NonActorImpacted сначала сообщает ProjectileContact при collider под Pivot, затем TryRegisterHit. Событие и HitCount нельзя менять.
- `ShootingTarget.cs:176`: попадание допустимо только в collider IsChildOf(Pivot) и состоянии Up. Новые source collider под Pivot расширят hit region; source collider вне Pivot могут заслонить существующий щит.
- `ShootingTarget.cs:182`: flash, fall/state, audio принадлежат ShootingTarget. Сохранить `_flashColor`, duration, `_impactAudio`, clips, fall/rise/down параметры и scene `_fallOnHit=false`.
- `ShootingTarget.cs:112`: lazy initialization запоминает Pivot.localRotation и кеширует Renderers. `SetPivot:125` не является безопасным API для смены набора рендереров после первого попадания.
- `ShootingTarget.cs:236`: при падении локальный X отрицательный; лицо +Z. Preserve root rotation/Pivot.forward, source face адаптировать отдельно.
- `ShootingTarget.cs:246`: MPB on читает прежний block и пишет `_BaseColor`/`_Color`; off делает SetPropertyBlock(null), стирая **весь** чужой MPB. LOD/script flicker/MPB tint overlays не добавлять. Материал разделяется безопасно: сам sharedMaterial в runtime не изменяется.
- `Assets/Scripts/Maps/ShootingRangePhotoCapture.cs:71`: копируются только активные MeshRenderer под Pivot с MeshFilter.sharedMesh; SkinnedMeshRenderer/sprite/UI или динамический geometry shader для щита не подходят.
- `ShootingRangePhotoCapture.cs:84`: фото копирует sharedMesh и sharedMaterials, не MaterialPropertyBlock. Source shape/UV/material должны быть валидны сами по себе; попадание фиксируется до flash.
- `ShootingRangePhotoCapture.cs:90`: RingOuter.position — центр смещения в миллиметрах; Pivot.forward/up определяют фото-камеру. Не удалять/двигать RingOuter и не менять Pivot rotation ради source.
- `Assets/Editor/VR_Battlegrounds/Weapons/Calibration/LobbyRangeLayoutBuilder.cs:39`: обязательны имена Plate, RingOuter, Post, Foot; :69..76 применяет размеры/позы и `_fallOnHit=false`, :78..83 создаёт RangeDistanceLabel. Визуальный адаптер должен быть совместим с сохранёнными именами, но сам этот builder не запускать для art-пакета.

ShootingTarget не NetworkBehaviour и не имеет вызовов StateEventAuthority. Его существующая документация (`ShootingTarget.cs:20..29`) объясняет локальную симуляцию попадания каждой машиной UltimateXR и ограничение late-join/краевого попадания. Readonly аудит **не утверждает**, что текущий код реализует authority gate. В этой art-задаче нельзя добавлять новый сетевой writer/gate или менять существующую схему; отдельный вопрос соответствия общему правилу authority остаётся владельцу gameplay, вне визуального адаптера.

## Существующие проверки и границы

`Assets/Tests/EditMode/Maps/ShootingTargetTests.cs` содержит четыре проверки:

1. `Пуля_в_щит_роняет_мишень` (:58) — логика fall при default=true на искусственном target.
2. `Пуля_в_стойку_и_в_чужое_не_считается` (:73) — ownership по иерархии Collider.
3. `Лежачий_щит_не_считает_попаданий_и_поднимается_сам` (:87) — state/down/reset.
4. `Префаб_мишени_не_хватается_и_досягаем_для_пуль` (:107) — Pivot, no Rigidbody/grabbable, solid enabled collider, weapon layer mask.

`LobbyLayoutTests.cs:246` — наличие мишеней, outside physical floor, no Ground/grabbable, raycast от ближайшей точки арены попадает в collider под Pivot. **Устаревшее ожидание :299 — sides.Count=4**, тогда как текущая Lobby намеренно имеет ±Z две стороны; тесты до user acceptance не переписывать и не трактовать этот expected failure как новую art-регрессию.

Ни одна найденная проверка не гарантирует сохранение всех десяти pose/fileID/collider чисел, RingOuter фотоцентра, MPB ownership, соответствия видимого контура collider или постоянного `_fallOnHit=false` в сцене. Должен быть native before/after snapshot этих данных и визуальная стрельба/фото через все десять мишеней. Проверить центр/край/стойку, мгновенный flash/reset, повторный выстрел по неподвижному щиту, материалы URP, фотоцентр/offset и отсутствие новой телепортации. После визуальной приёмки ведущий решает тестовые изменения.

Фактически выполнены только readonly rg, чтение соответствующих C#/prefab/meta/scene sections и YAML подсчёт 10 instances/overrides. Unity/EditMode/PlayMode/Android checks не запускались, C# не изменялся и повторная компиляция для этого audit не требовалась. `guard` перед записью данного артефакта: ok=true, implementation epoch1.
