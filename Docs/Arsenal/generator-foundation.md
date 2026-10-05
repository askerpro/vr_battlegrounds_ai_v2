# Foundation генерируемого арсенала

Цель — получать состав только из ordered `ArsenalPreset.Entries`, а Item/Magazine/Card/Support targets только из Style выбранного Preset. Этот foundation не включает runtime composer, выдачу, admission, Prefab Mode overlay или production migration.

## Мы здесь

Семь source-файлов импортированы в Unity: immutable input/description, resolver, catalog и resource metadata, passive binding и Editor compiler. Scoped baseline после импорта и native catalog:9 native assets +236 source/scene SDK rows +4 Lobby stations сохранены; compile/import idle, current console0. Actual transient compiler прошёл; первоначальный actual matrix дал 80 PASS и 1 FAIL: итоговый список группировал зоны вместо исходного порядка Entries. После исправления список сортируется по captured Entry index; повторная матрица 82/82, включая сохранение prefab resource ref после изменения WeaponInfo clone. Native catalog создан отдельно по отсутствующему пути `Assets/Data/Arsenal/ArsenalCompositionCatalog.asset`, GUID b8538636e1c0b6c4f92d6b8d8e4737b0. Выгрузка/reload сохранили все serialized refs/roles/surface и оба description hashes:9/9. Повтор current-source matrix82/82; свежий Android PASS/Errors=[], console0. Independent source/spec review ещё впереди; production Common/Demo/Lobby остаются Authored, FullDemo.Style null.

Независимое review выявило два P2: новый Capture не отказывал при смене prefab refs относительно compiled metadata, а selection proof не проверял occupied usableBounds. Коррекция source ограничена `ArsenalStationDescription`: Item/Magazine Resource и Present сверяются с текущими WeaponInfo refs; несоответствие даёт typed MissingMetadata и требует compiler. Новый producer геометрии не добавлен. Старый captured input сохраняет frozen refs.

Actual before-Capture RED:13 checks/7 failures; после коррекции GREEN13/13. Отдельный occupied bounds/frame oracle10/10: достаточные capacities с маленькими bounds исключаются, larger/exact fit и независимо вычисленный translated/rotated frame выбираются корректно, universal не скрывает map overflow. Selection code не менялся. Current matrix82/82, existing persistent catalog readonly7/7, fresh Android PASS/Errors=[], console0. Native nested DropAlign fixture с ненулевыми offset/rotation и uniform scales прошёл10/10 по независимым восьмиугловым расчётам; exact-owned asset/meta/folder очищены. Это compiler/envelope proof, не SDK smooth-return/registration proof. Scoped before/after10files/236SDK/4Lobby exact; не whole-project audit. Повторное независимое review — следующий gate, task1 пока не принят.

## API и владельцы

- `ArsenalStationBuildInput.Capture(StationKey, Preset, Catalog, VisualRequest, PlacementInput)` замораживает значения Entries/Style, card primitives, geometry и roles. До capture compiled Item/Mag Resource и Present должны совпадать с текущими WeaponInfo refs, иначе MissingMetadata. Item/Magazine Resource refs захватываются отдельно от изменяемого WeaponInfo; materializer не должен перечитывать оттуда prefab. UObject refs — только локальные ресурсы будущего materializer, не editable master поз. Проверка не перечитывает mesh/material geometry того же UObject: её свежесть относится к следующему editor resource/build gate.
- `ArsenalStationResolver.ResolveDescription(input)` возвращает typed failures/selection, ordered Slots, structural/open/closed/conservative-swept envelopes и отдельные Layout/LogicalRole fingerprints. Success не означает server Ready/LocalPlayable.
- `ArsenalStationCompositionBinding`: единственный serialized StationKey, exclusive Authored/Generated mode (default Authored), read-only ссылки на shell owners. Нет Preset copy, MapRunKey, таймера или выдачи admission.
- `ArsenalCompositionCatalog` хранит compiled functional/geometry/decor resources, не состав и не Style targets. `ArsenalCompositionMetadataCompiler.Compile(...)` читает native ресурсы и возвращает собственный transient catalog. Его адресное сохранение — отдельный guarded шаг.

Card snapshot содержит название, цену и balance primitives. Нынешний `ArsenalPriceTag.Show(WeaponInfo,...)` читает source; composer integration должен дать frozen-input adapter общему formatter, без второго форматирования в resolver. Это task3, сейчас карточки production не меняются.

## Surface projection

Accepted профиль измерен в native Demo: Peg .4×1×.025m/tilt10°/gap.025m; Shelf .4×.66×.03m/gap.03m. Standalone source Peg шириной .45m, Shelf имеет старые размеры и zone. Compiler не скрывает эту разницу: хранит source GUID/localFileID/pose/scale/mesh+collider bounds отдельно от projected panel TRS/bounds и provenance accepted profile.

Роль поверхности — единственная direct `PegboardSection` с MeshFilter/MeshRenderer/BoxCollider. Source/projected mesh и collider shape должны совпадать; изменяется только явно объявленный panel projection, не whole slot root/оружие. Другие structural parts перечисляются отдельно. Если роль/shape неоднозначны — compile refusal, не угадывание.

Row Open/Closed берутся из authored EquipmentPoses с fixed ancestor/root compensation. Каждый ряд центрируется `(i-(N-1)/2)*(width+gap)`, без legacy+.085; ноль entries не создаёт резервных cells. Shelf3width1.26m/centers−.43/0/.43; Shelf9width3.84m.

## Выбор fixed artwork

Обе capacities и bounds должны вмещать description. Sized comparator: usable volume → сумма capacities → Peg capacity → Shelf capacity → ordinal DecorationId. Explicit missing/undersized сообщает diagnostic, затем тот же fallback; один DefaultUniversal, затем Bare. Missing decoration не блокирует функции; invalid functional inputs или real placement overflow не превращаются в успех Bare. Artwork/предметы не уменьшаются ради fit.

## Identity и пределы

NetworkIndex — Entry index; LogicalSlotKey — WeaponId. LogicalRole fingerprint отдельно от decoration/layout. Actual source Peg/Shelf anchors имеют разные source UID. Поэтому один Combine(sourceUID,zone-independent seed) ещё не доказывает zone-independent runtime identity. Canonical semantic roles и native registration требуют отдельного task2 proof/high ruling; SDK/source IDs сейчас не подменяются.

Task0 проверял loaded Lobby+Debug Stand; незагруженные карты — только GUID inventory. Это не whole-project external-ref closure и не отмена raw8880/8535of17416 старых audit limits. ArtisticAll20FitComplete=false/productionLinked=false; accepted Stand110/24 не является generated production proof. Remaining gates: independent source/spec review foundation; preview/resources; bootstrap handoff/identities/composer/admission; только затем destructive cutover и human acceptance. Граница task1 не меняет map geometry, поэтому Bake Occlusion этого среза не требуется.
