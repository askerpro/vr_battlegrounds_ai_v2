# Исследование A/B: патрон в гнезде Herrington/Fabarm, кнопки-наследие (2026-10-23, read-only)

## Патрон в гнезде (Herrington и FabarmSDASS одной правкой)

- Сборщик кладёт патрон вложенным экземпляром (`HandsPackWeaponBuilder.cs:605-614`: InstantiatePrefab, удалить
  NetworkIdentity, `_startAnchor`, `_rigidBodySource`, затем `WeaponInternalAmmoAlignment.ConfigureAnchor`).
- Разворачивает его `ManualLoadingAuthoring.ConfigureWeapon` (`ManualLoadingAuthoring.cs:51-62`, UnpackPrefabInstance
  Completely, введено 1319a79a7) — на шаге `ConfigureHost` (KINEMATION `BuildSteps` `KinemationWeaponBuilder.cs:368-374`;
  Fabarm — `HandsPackLegacyRebuild.Rebuild` `:98-103`, рецепт `ShotgunReal`, `ManualLoading`/`MagazineIsInternal`
  `HandsPackWeaponBuilder.cs:134-135`). Далее (`:63-87`) удаляются Cartridge, UxrFirearmAmmoUnit, OutOfWorldGuard,
  MagazineManipulationHistory, NetworkIdentity; Renderer/Collider выкл., Rigidbody kinematic, тег `Tube:<ствол>`,
  Grabbable выкл., `ConfigureFixedStore`.
- «Патрон в гнезде» — скрытый встроенный запас трубки, игрок его не видит и не берёт; красный тест формальный.
- Правка: убрать UnpackPrefabInstance (`:61-62`), всё остальное — overrides на вложенном экземпляре
  (`RecordPrefabInstancePropertyModifications` после прямых присваиваний `:76-82`, как `WeaponBalanceApplier.cs:217`);
  защитная проверка в коде: у запаса нет NI/Cartridge/OutOfWorldGuard, исходник — патрон своего ствола.
- Ссылки/якорь не меняются: `ConfigureAnchor` на вложенном экземпляре; `LoadingProximity`, `EjectionPose`,
  `WeaponInternalAmmoEjection`, `CartridgeIntake`, `_ammunitionMagAnchor` — на якоре. `WeaponBalanceApplier.
  ApplyInstalledMagazines` (`:203-235`) совместим; `WeaponGripCarryOver.OwnGrabbables` (`:239-241`) пропускает якорь.
- `_uxrUniqueId` унаследуются (те же id, что у `Herrington_mag`); `__prefabGuid` пишет `UxrUniqueIdPersister`;
  NI вложенного — `m_RemovedComponents`.
- Тесты прогнать: ManualLoadingPrefabTests, WeaponBalanceTests, WeaponSlideTravelTests, KinemationWeaponTests(Herrington),
  WeaponFeedbackTests, UxrUniqueIdOnDiskTests, NetworkAssetIdOnDiskTests, AnchorActivationAudioTests,
  MagazinePocketSelectionTests, GrabPoseCoverageTests, PackMagazineStructureTests. Ожидается зелёный
  `GrabPoseCoverageTests(Herrington, CyborgHandsBase)` (вложенные пропускаются `IsInsideNestedPrefab`).
- Риски: изменения `Herrington_mag` доходят до запаса без пересборки; «Apply All» в инспекторе испортит выдаваемый
  патрон; removed-overrides привязаны к fileID. Шлем: заряжание Herrington, 7 в трубке, выброс, перезарядка.
- `KinemationFixReview.ApplyMagazine` (`:113-116`) сейчас бросает на собранном Herrington — после правки перестанет.

## Кнопки-наследие вкладки «Взаимодействие» (`ArsenalEditorWindow.cs:428-430`)

- «Обновить механические анимации KINEMATION»: `KinemationWeaponBuilder.Build` уже вызывает `KinemationFixReview.Apply`
  (`:371`) + ConfigureHost + Restore + общий конец — сборка надмножество. Кнопка на готовом префабе вредит
  (возвращает WeaponMechanismVisuals, не возвращает хваты, нет нормализации _assetId/UXR, падает на Herrington).
  Удалить кнопку; `KinemationFixReview.Apply` оставить (шаг сборки, можно internal, summary `:12`);
  `KinemationFixReview.Report()` (`:150-153`) не вызывается — ссылка только `Docs/Arsenal/editor-workbench-plan.md:175`.
- «Обновить внутренний патрон двух дробовиков»: `ConfigureAnchor` в каждой сборке (`HandsPackWeaponBuilder.cs:613-614`);
  кнопка на готовых префабах, вероятно, падает (`WeaponInternalAmmoAlignment.cs:102-110` требует включённые коллайдеры).
  Удалить кнопку, `ApplyCurrentShotguns` (`:191-226`), лишний `using` (`:5`); `ConfigureAnchor`/`BoundsInFrame` оставить.
- Docs: `Docs/Arsenal/editor-workbench-plan.md:12` (строка «Взаимодействие»); `Docs/README.md:478/:488` — общий
  индекс, задача не правит.
