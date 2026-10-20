# Неиспользуемое оружие (сэмплы UltimateXR)

2026-10-20, задача `weapon-sight-calibration`, раунд 2. Решение пользователя: SDKGun, SDK ShotGun и Machinegun не
калибровать, а убрать из арсенала. Ассеты, принадлежащие только этим стволам, перенесены сюда через
`AssetDatabase.MoveAsset` — GUID и ссылки сохранены. Исходные модели и префабы сэмплов в `Assets/ThirdParty/UltimateXR`
не трогались.

## Что лежит и откуда перенесено

| Ствол | Файл здесь | Исходный путь |
|---|---|---|
| SDKGun | `SDKGun/SDKGun_Weapon.asset` | `Assets/Data/Weapons/SDKGun_Weapon.asset` |
| SDKGun | `SDKGun/Gun.prefab` | `Assets/Prefabs/Weapons/Gun/Gun.prefab` |
| SDKGun | `SDKGun/SDKGun_Shelf.asset` | `Assets/Data/Arsenal/SlotLayouts/SDKGun_Shelf.asset` |
| SDKGun | `SDKGun/Interaction/PrimaryRegion.asset`, `InsertionRegion.asset` | `Assets/Art/Weapons/Interaction/Gun/` |
| Shotgun | `Shotgun/ShotGun_Weapon.asset` | `Assets/Data/Weapons/ShotGun_Weapon.asset` |
| Shotgun | `Shotgun/Shotgun.prefab` | `Assets/Prefabs/Weapons/Shotgun/Shotgun.prefab` |
| Shotgun | `Shotgun/Shotgun_Pegboard.asset` | `Assets/Data/Arsenal/SlotLayouts/Shotgun_Pegboard.asset` |
| Shotgun | `Shotgun/Interaction/ActionRegion.asset`, `InsertionRegion.asset`, `PrimaryRegion.asset` | `Assets/Art/Weapons/Interaction/Shotgun/` |
| Machinegun | `Machinegun/Machinegun_Weapon.asset` | `Assets/Data/Weapons/Machinegun_Weapon.asset` |
| Machinegun | `Machinegun/Machinegun.prefab` | `Assets/Prefabs/Weapons/Machinegun/Machinegun.prefab` |
| Machinegun | `Machinegun/Machinegun_Pegboard.asset` | `Assets/Data/Arsenal/SlotLayouts/Machinegun_Pegboard.asset` |
| Machinegun | `Machinegun/Interaction/PrimaryRegion.asset`, `InsertionRegion.asset`, `SupportRegion.asset` | `Assets/Art/Weapons/Interaction/Machinegun/` |

Пустые папки `Assets/Art/Weapons/Interaction/{Gun,Shotgun,Machinegun}` удалены.

Остались на месте (общие): магазины `Assets/Prefabs/Weapons/Gun/MagGun.prefab`,
`Shotgun/MagShotgun.prefab`, `Machinegun/MagMachinegun.prefab` — базы магазинов других стволов (варианты префабов) и
константы сборщиков (`HandsPackWeaponBuilder.MagPistolBase/MagRifleBase`, `MagazineBase` Herrington/FABARM); их записи
в spawnable-списке `--- MANAGERS ---` не трогались. Позы кисти `Assets/Art/Avatars/PlayerControllersCyborgAvatar/HandPoses/Demo*.asset`
используют аватары Cyborg и сэмплы ThirdParty.

## Откуда убраны ссылки

- `Assets/Data/Weapons/Resources/WeaponRegistry.asset` (`_weapons`).
- `Assets/Data/Weapons/FullDemoArsenal.asset` (`_entries`; пресет лобби). `CurrentGameplayArsenal.asset` их не содержал.
- `Assets/Data/Arsenal/ArsenalCompositionCatalog.asset` (`_weapons`: Machinegun, SDKGun, Shotgun с магазинами).
- `Assets/Prefabs/Managers/--- MANAGERS ---.prefab` (`GameNetworkManager.spawnPrefabs`: Gun, Machinegun, Shotgun).
- `Assets/Scenes/Debug/BotCombatStand.unity` (`BotCombatStand.Weapons`).
- `Assets/Editor/VR_Battlegrounds/Gameplay/WeaponSystemAuthoring.cs` (строки `Gun`, `Machinegun`, `Shotgun` в `Weapons`,
  ключи `Gun`, `Machinegun` окон выброса).
- `HandsPackWeaponBuilder.ShotgunReal.FirearmDonor` теперь — снимок `Assets/Art/Weapons/HandsPack/LegacyBuildDonors/Shotgun_real.prefab`.

## Как восстановить

1. Вернуть файлы на исходные пути через `AssetDatabase.MoveAsset` (или перетаскиванием в Project) — GUID не меняются.
2. Вернуть записи: WeaponRegistry, ассортимент `FullDemoArsenal`, строки `ArsenalCompositionCatalog` (пересобрать
   каталог штатным инструментом Редактора арсенала), `spawnPrefabs` в `--- MANAGERS ---`, список `BotCombatStand`,
   строки `WeaponSystemAuthoring.Weapons` (и ключи окон выброса), донор FABARM — по желанию.
3. Пересчитать отпечаток пресета лобби: `MapBootstrapMigration.RebakeCatalog` (меню `Tools/VR Battlegrounds/Maps/Map Bootstrap/Rebake Catalog`).
