# Аренда 458 (2026-10-12) — сборка комплектов и проверки

Вход A cc6d81d91 = B 1fc34c84c + файлы этапа; результат R f58016d75 (accepted, unexpected пусто), receive выполнен.

- Preflight: масштаб/оси импорта комплектов совпали с исходниками пака; отличались только material/animation/extras (выставлены: материалы None, анимация None, без камер/света/blendshapes/коллайдеров).
- Build: SightKit_TR15: R=(0, 0.039323, 0.006436) F=(0, 0.043764, 0.306436), R→F 300.0 мм, F выше R на 4.44 мм, M_TR15_Ironsights.
  SightKit_MX68: R=(0, 0.037521, 0.007714) F=(0, 0.036605, 0.373660), R→F 365.9 мм, F ниже R на 0.92 мм, M_MX68_IronSights.
- Модели FBX: все объекты с нулевым поворотом и масштабом 1; pivot Post/Aperture = ожидаемые F/R до 1e-6 м.
- Таблица: Assets/Data/WeaponSystem/Sights/WeaponSightTable.asset, entries=0.
- Наложение (исправленная мировая сверка, execute_code): TR15 Rear base 0.0000 мм, TR15 Front base/post 0.0000 мм,
  MX68 все 4 объекта 0.0000 мм (3018/3018 вершин), TR15 RearSight_Aperture 6.39 мм — барабан повёрнут малым отверстием вверх (намеренно).
  Встроенная VerifyOverlay в A считала в системе корня FBX (ошибка 23–262 мм) — исправлено после аренды.
- Тесты VrBattlegrounds.Tests.EditMode.Sights: 16/18; упали ScaledParent (ошибка теста: ход) и KitFbx_OverlaysPackSource (ошибка сверки) — исправлены.
- Apply на временной копии TR15 (_Test, удалена): PREFLIGHT ok, APPLY1 +1.00 мм, APPLY2 (на месте) −1.00 мм, один nested-экземпляр;
  отказ при сдвинутом отпечатке ShotSource и при сдвиге 10 мм вне хода; _assetId канонический (проверка внутри Apply).
- Smoke-решатель TR15 (комплект 5 см над осью, 0.5 м за дулом): требуется −6.17 мм > хода 3 мм → корректный отказ без записи.
- AndroidCompileGate: passed=True, errors=0.

# Аренда 463 — повторная проверка после правок
Вход A 0b9e845cc (B + файлы этапа + R 458), R 88788ed23: accepted, paths пусто (повторный Build идемпотентен).
- Preflight: импорт совпадает. VerifyOverlay: все объекты 0.0000 мм, кроме намеренно повёрнутого барабана TR15 (6.39 мм).
- VrBattlegrounds.Tests.EditMode.Sights: 18/18 passed.
- Регресс по папке Assets/Prefabs/Weapons (WeaponMaterial/Scale/DropPhysics/OutOfWorldGuard/SlideTravel/Hitbox/LobbyLayout): 36/37;
  упал LobbyLayoutTests.За_краем_мишени_видимые_с_арены («мишени не со всех четырёх сторон») — про мишени лобби, комплектов не касается.

# Аренда 468 — реальный размер комплекта (ревью главного)
Вход A bd7b81e57 (новая B bf8048cd3 + файлы этапа), R b673e8038 (accepted; новые только .meta SightKitScale.cs и SightKitScaleTests.cs).
- Tests Sights: 22/22 passed (+4: scale 0,7/4,533/0,2087 и отказ при неравномерном масштабе).
- Writer на копии AR15 (корень 0,7): kit localScale 1,428571, lossy 1,000000; сдвиг столбика 1,0000 мм мира; 52/52 узла вне комплекта без изменений; assetId 3927931820 = канон.
- Writer на копии MP5K (4,5333): kit localScale 0,220588, lossy 1; сдвиг 1,0000 мм; 55/55 без изменений; assetId 3759740508 = канон. Копии удалены.

# Аренда 494 — «Родной прицел», ход TR15 8/3 мм
Вход A 72d363a7d (checkpoint --onto-baseline, база 68950d0be), R d71b4e578 accepted; изменён только SightKit_TR15.prefab (_travelDown 0.008) + .meta новых файлов.
- Tests Sights + SightSplit: 104/104 passed. AndroidCompileGate: passed, 0 ошибок.
- Решатель на стенде «Родной прицел» (Z=15 м, ход ±3 мм): сходятся AK105 +0.91, R08 +0.57, MP5K −0.90, Uzi −0.90, Herrington +1.65,
  Revolver +0.39, Gun −0.19, Scar −2.03 мм; не хватает хода: Mk14 +6.67, Viper −3.07 мм; PPK — только измерение (|e| 16.1/15.4/14.1 мм на 5/10/20 м).
