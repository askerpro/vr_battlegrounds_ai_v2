# 2026-10-22 — тесты принятого, самостоятельные магазины, удаление баз SDK; прогоны 6–8 (tickets 1059, 1061, 1065)

Ничего не закоммичено. Отчёты — `../reports/run6/`, `../reports/run7/`, `../reports/run8/` (вне Git).

## Решения пользователя

- После просмотра пересобранного арсенала на worker: «да все в порядке». В задачу входят код и пересобранный арсенал.
  Шарнир отдачи: «Проверено, принято».
- Покрытие хвата Cyborg у магазинов — «В известные красные».
- Поручение, переданное сессией по хватам: «удаление баз магазинов SDK и SDK-оружия из Unused (с отвязкой магазинов)
  передай оружейной сессии». Сначала решено «Отдельной задачей», затем «Выполни эту задачу здесь».

## Тесты на принятое поведение (после приёмки)

- `Gameplay/Tests/WeaponRebuildTransactionTests` (3):
  - откат возвращает байты, удаляет созданное и отбрасывает память;
  - несохранённое до начала откат не трогает;
  - `ReleaseForeign` отбрасывает чужое несохранённое вне области.
- `Gameplay/Tests/PackMagazineStructureTests` (2×17):
  - у магазинов паков нет геометрии SDK и записей сэмпловых аватаров;
  - экспортированная поза магазина — со snap-точкой `Grabs/Orig`.
- `Tests/EditMode/Interaction/RecoilPivotTests` (3):
  - шарнир в запястье, оси от оружия;
  - без запястья — точка сборки;
  - отключение возвращает точку сборки.
  Для теста выделен `RecoilAccumulator.PlacePivot`.
- `Tools/e2e/editmode-known-red.json`:
  - добавлено 51 покрытие хвата Cyborg у магазинов (17 объектов × 3 аватара семейства);
  - убран позеленевший `GameTagsTests.Prefabs_TagsMatchRules`.

## Исправления по прогону 6

- **Записи сэмпловых аватаров SDK по GUID.** У доноров набора Hands (BrowningHiPower, AR15) их точки лежат в
  собственном `Grabs`, и правило «по удалённым узлам» их не находило.
  - `HandsPackWeaponBuilder.SdkSampleAvatars` — CyborgAvatar, BigHands, SmallHands.
  - `RemoveOrphanGripEntries` удаляет записи этих аватаров и их точки (`HandsPackPoseImporter.RemoveUnused`).
  - `WeaponGripCarryOver.Restore(..., droppedAvatars)` их не переносит.
- **Herrington** при rebase конфликтовал с ручной доводкой хватов Cyborg в dev (bd718b86d). Взята версия dev и
  пересобрана поверх, перенос сохранил записи.
- **Snap-точка позы магазина BrowningHiPower.**
  - В первой пересборке на чистом состоянии точки не было, хотя поза создана.
  - Повторный экспорт (`ExportWeapon`, затем пересборка набора) записал её без предупреждений.
  - Похоже, при первом создании позы магазина у распакованного магазина-донора snap-точка не пишется. Известная
    особенность экспериментального экспорта поз магазина; в итоговом состоянии точка есть, тест это проверяет.

## Самостоятельные магазины и удаление баз SDK

- `HandsPackWeaponBuilder.BuildMagazine`:
  - шаблон — текущий магазин ствола (`MagazineTemplate`), у первой сборки нового ствола — `MagazineBase`;
  - экземпляр полностью распаковывается и сохраняется **самостоятельным префабом**;
  - localFileID корня сверяется до и после: изменение — отказ, внешние ссылки WeaponInfo/spawn не рвутся.
  - Если шаблон — магазин другого ствола, его записи хвата и `Grabs/*` не берутся.
  - У патрона ручного заряжания (Herrington_mag, FabarmSDASS_Ammo) счётчика `UxrFirearmMag` в шаблоне нет: сборщик
    добавляет его, `ConfigureCartridge` снимает, как раньше.
- `MagazineBase` первой сборки: Scar_mag (винтовки), PPK_mag (пистолеты), Herrington_mag (дробовик, `MagShotgunBase`).
- `ArsenalBuildPreflight` проверяет фактический шаблон; `UxrFirearmMag` обязателен только без ручного заряжания.
- Удалены:
  - `Assets/Prefabs/Weapons/{Gun/MagGun, Machinegun/MagMachinegun, Shotgun/MagShotgun}.prefab` с папками;
  - `Assets/Unused` целиком (SDKGun, Shotgun, Machinegun с WeaponInfo, раскладками, областями, README);
  - три строки баз в `GameNetworkManager.spawnPrefabs` (`--- MANAGERS ---`), правка через SerializedObject.
  На GUID баз и на ассеты Unused больше никто не ссылается (проверено `grep` по Assets/ProjectSettings).
- Упоминания поправлены:
  - Docs/UltimateXR/sdk-patches.md (патч 92), Docs/gameplay.md, Docs/weapons/sight-calibration.md;
  - комментарий HandPoseGripRulesTests;
  - SKILL add-weapon (магазин — самостоятельный префаб).
  Сэмплы UXR в `Assets/ThirdParty/UltimateXR/Samples` не тронуты.

## Прогон 8 (ticket 1065)

- Компиляция без ошибок. Пересобраны все 17 стволов: все 17 магазинов — `PrefabAssetType.Regular` без источника,
  localFileID корней не изменились.
- После удаления баз консоль без ошибок.
- EditMode, 274 теста в 11 группах: 218 зелёных, 56 красных — все из `editmode-known-red.json`, новых нет:
  - DogTagPanel ×3;
  - покрытие хвата Cyborg у магазинов ×51;
  - `HandsPackWeaponTests(Scar)`, бюджет треугольников R08.
- Отдельно 21/21: HandPoseGripRulesTests, NetworkSpawnableRegistrationTests, RecoilAccumulatorOrderTests.
- Тест «магазин — самостоятельный префаб» не добавлен: этап ещё не принят пользователем.
