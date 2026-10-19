# Точные зависимости следующего этапа

2026-10-18. Инвентаризация стала входом stage layout по прямому решению пользователя и9661. Manifest: [layout-manifest.json](layout-manifest.json). После fetch/rebase база остаётся `2e1b6a2f`; перечисленные production updates применены в21Editor source. Test expectations остаются неизменёнными до UserQA.

## Согласованные разделы по событию 9605

Префикс файлов таблицы — `Assets/Editor/VR_Battlegrounds/`; префикс заменяемых asset paths — `Assets/Prefabs/Player/`.

| Путь до → после | Editor source:line |
| --- | --- |
| `WristWatch_HUD.prefab` → `Equipment/Watches/WristWatch_HUD.prefab` | `Avatars/WristWatchInstaller.cs:31`; `Avatars/Workbench/AvatarEditorWindow.cs:255`; `Avatars/Workbench/AvatarEditorToolsPanel.cs:190` |
| `Pockets` → `Equipment/Pockets` | `Avatars/AvatarPocketSetup.cs:23`; `Avatars/Workbench/AvatarEditorWindow.cs:250`; `Avatars/Workbench/AvatarEditorToolsPanel.cs:155,156`; `Avatars/Workbench/AvatarMaintenanceTools.cs:150`; `Avatars/Workbench/AvatarPocketTemplateBindings.cs:13` |
| `Corpses` → `Corpses/MEF` | `Avatars/CorpseBuilder.cs:29`; также `:84`: CreateFolder должен создать `MEF` внутри `Corpses`, а не `Corpses` внутри `Player` |
| `MEF_cyborg_hands/HandPoses` → `HandPoses/CyborgHands` | `Avatars/GripExtraction/GripTargetProfile.cs:57,138,162`: сейчас PoseFolder выводится как `CyborgFolder + /HandPoses`; заменить модель вычисления пути так, чтобы оба профиля читали ровно общий новый каталог без лишнего `/HandPoses` |

Для PlayerSession, обычного WristWatch и отдельных corpse/pocket assets прямых Editor literals не найдено: ссылки GUID/serialized либо генерация из folder constants. Новые parent folders создаёт Unity до MoveAsset.

## Avatars: утверждённые зависимости

| Путь до → после | Editor source:line |
| --- | --- |
| `PlayerBase.prefab` → `Avatars/Bases/PlayerBase.prefab` | `Avatars/AvatarHandBases.cs:23`; `Avatars/CreateBaseAvatars.cs:23`; `Avatars/ExtractPlayerBase.cs:10`; `Avatars/FixHandTrackingCache.cs:12`; `Avatars/PlayerBasePrefabBuilder.cs:15` |
| `PlayerBase_NonSdkHands.prefab` → `Avatars/Bases/PlayerBase_NonSdkHands.prefab` | `Avatars/AvatarHandBases.cs:25`; `Avatars/HandsPackPoseImporter.cs:52` |
| `CyborgHandsBase.prefab` → `Avatars/CyborgHands/Base/CyborgHandsBase.prefab` | `Avatars/AvatarHandBases.cs:24`; `Avatars/GripExtraction/GripTargetProfile.cs:50` |
| `PlayerControllersCyborgAvatar.prefab` → `Avatars/CyborgHands/Cyborg/PlayerControllersCyborgAvatar.prefab` | `Avatars/CyborgLegsBuilder.cs:47`; `Avatars/ExtractPlayerBase.cs:9`; `Avatars/GhostAvatarBuilder.cs:40`; `Avatars/InspectAvatar.cs:9`; `Avatars/RefineBackPocket.cs:10`; `Avatars/PlayerBasePrefabBuilder.cs:14`; `Avatars/GripExtraction/GripTargetProfile.cs:52` |
| `MEF_cyborg_hands_Player.prefab` → `Avatars/CyborgHands/MEF/MEF_cyborg_hands_Player.prefab` | `Avatars/TeamColorVariantBuilder.cs:34`; `Avatars/GripExtraction/GripTargetProfile.cs:53`; `Avatars/HandRigQuality/HandRigQualityWindow.cs:21` |
| `MEF_cyborg_hands_Player_Black.prefab` → `Avatars/CyborgHands/MEF/MEF_cyborg_hands_Player_Black.prefab` | `Avatars/GripExtraction/GripTargetProfile.cs:54` |
| `MEF_cyborg_hands_Player_{suffix}.prefab` → тот же шаблон в `Avatars/CyborgHands/MEF` | `Avatars/TeamColorVariantBuilder.cs:77`, динамический output |
| `Ghost/` → `Avatars/Ghost/` | `Avatars/GhostAvatarBuilder.cs:27,38,41` (включая комментарий MaterialPath); `Avatars/GripExtraction/CyborgHandsPrefabMigration.cs:30` |

Отдельные решения для генераторов: `Avatars/CreatePrefabSetup.cs:25,39` генерирует произвольный avatar в корень Player; `CreateBaseAvatars.cs:69,70` оставляет три отсутствующих military outputs в корне Player (Heavy уже перенаправлен); `Workbench/AvatarEditorToolsPanel.cs:16` использует default `Assets/Prefabs/Avatars/NewPlayerBase.prefab` вне этого дерева. Эти defaults нельзя молча менять как следствие переноса существующих assets.

Общие поисковые корни `AvatarFingertipSetup.cs:29`, `AvatarLegsSetup.cs:37`, `HitboxBuilder.cs:35`, `Workbench/AvatarEditorToolsPanel.cs:178,215` остаются работоспособны: всё остаётся внутри Player. Их не сужать без отдельного решения.

## Зависимые существующие Tests: механические пути после приёмки9743

Пользователь принял layout и разрешил обновление литералов путей. В9файлах ниже обновлены15литералов к уже перемещённым assets; assertions не изменены. Пути исходных pack poses в Assets/Art/HandPoses остаются прежними: эти assets не перемещались. Общие30поз Cyborg/MEF находятся в Player/HandPoses/CyborgHands и в текущих тестах не имеют старого literal folder. Ни Optimized_MEF_Player, ни намеренный LegacySdkHands не подменяются.

Префикс — `Assets/Tests/EditMode/`, кроме явно указанных Editor Tests.

- `Prefabs/AvatarHandPoseChainTests.cs:31–37`: семь avatar/Ghost paths станут obsolete; намеренный legacy/archive :38–39 остаётся.
- `GameModes/GameModeWiringTests.cs:39`, `Prefabs/AvatarRendererOwnershipTests.cs:75`: Controllers.
- `Prefabs/KinemationHandPoseTests.cs:25`, `Prefabs/HandsPackHandPoseTests.cs:35`: NonSdkHands.
- `Prefabs/RegisteredAvatarBodyTests.cs:138`, `UI/WristDisplayTests.cs:18`: HUD watch.
- `Prefabs/GhostAvatarTests.cs:31`: Ghost material path станет obsolete после переноса всей папки.
- `Prefabs/PrefabCompositionTests.cs:144`: исторический комментарий про корень Player, не expectation.
- `Assets/Editor/VR_Battlegrounds/Avatars/HandPoseReview/Tests/SdkHandPreviewParityTests.cs:23`: Controllers. Там же :22 и `SdkPreviewBindingTests.cs:17` уже до переноса указывают на отсутствующий Optimized_MEF_Player.

## Уже устаревшие пути — отдельные findings

Эти пути отсутствуют до будущей реорганизации; не считать их регрессией Heavy и не перенаправлять автоматически на другой avatar.

- `Player/MEF_Base_Avatar.prefab` уже находится в Unused: `Arsenal/ArsenalBuildPreflight.cs:22`; `Arsenal/ArsenalEditorWindow.cs:385,405`; `PuppetStand/SitCandidatesStandMenu.cs:21`; `PuppetStand/ClipScrubStandMenu.cs:20`; `Avatars/AvatarMixamoLocomotionSetup.cs:60`; `Avatars/CyborgLegsBuilder.cs:51`; `Avatars/HandsPackPoseImporter.cs:55`; `Avatars/Workbench/AvatarEditorToolsPanel.cs:208`; `Debug/AvatarPuppetStandMenu.cs:42`.
- `Player/Optimized_MEF_Player.prefab`: `PuppetStand/ClipScrubStandMenu.cs:22`; `Debug/AvatarPuppetStandMenu.cs:28`; два Editor Tests выше.
- `Player/Experimental/Optimized_MEF_Player_VRIK.prefab`: `Debug/AvatarPuppetStandMenu.cs:29`; одноимённый asset уже в Unused.
- `Player/Experimental/MEF_cyborg_hands/MEF_cyborg_hands_Base.prefab`: `Avatars/GripExtraction/GripTargetProfile.cs:51`, исторический fallback; одноимённого asset в текущем Player нет.
- `Player/PlayerBase_SdkHands.prefab`: `Avatars/GripExtraction/CyborgHandsPrefabMigration.cs:23`, намеренный legacy path; архив :24 и Unused :408 сохраняются.
- Три military paths в `Avatars/FixHandTrackingCache.cs:14–16` отсутствуют ещё до Heavy MoveAsset.

Исходная оценка широкой инвентаризации — порядка 73 matches / 36 файлов. Текущий буквальный поиск `Assets/Prefabs/Player` в Editor C# — 68 строк / 33 файла, включая 3 строки в двух Editor Tests. Это счёт совпадений, а не число обязательных изменений.
