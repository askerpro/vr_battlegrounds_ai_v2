# Риг, путь Б: родные кисти модели (без ампутации)

> Проверено на `MEF` (US Marines, `Assets/ThirdParty/BonelabAvatars/US_MARINES/FBX/MEF/`),
> 2026-09-27. Результат — `Assets/Prefabs/Avatars/MEF_Rig.prefab`.

Ампутация и пришивание кистей SDK (путь А) нужны модели **без пальцевого скелета**. Если у
модели полный гуманоидный скелет с пальцами по 3 фаланги, twist-костями предплечья и костями
глаз (Reallusion CC3/CC4: `CC_Base_*`, корень `RL_BoneRoot`), всё уже есть — позы UltimateXR
работают на родных пальцах. Дескрипторы поз SDK хранятся в «универсальных» осях кисти и
переносятся на любой риг.

Проверить до начала: кости `*_Index1..3` и т. д. у обеих рук; меш перчаток заскинен на пальцы,
а не только на `Hand` (веса через `sharedMesh.boneWeights`).

## Шаги

1. **Импорт FBX → Humanoid.** `ModelImporter.animationType = Human`,
   `avatarSetup = CreateFromThisModel`, `SaveAndReimport`. Автомаппинг CC чистый (все 30
   пальцевых костей; сестринская пустая кость `CC_Base_*_Hand_001` не подхватывается).
   Глаза и челюсть автомаппинг не берёт — дописать в `HumanDescription`:
   `LeftEye → CC_Base_L_Eye`, `RightEye → CC_Base_R_Eye`, `Jaw → CC_Base_JawRoot`.
2. **Экземпляр во временной сцене**, `UnpackPrefabInstance(OutermostRoot)`, имя `<Model>_Rig`.
3. **UxrAvatar и риг** — `AddComponent<UxrAvatar>()`, `SetupRigElementsFromAnimator()`,
   `TryToInferMissingRigElements()`. Второй вызов навешивает `UxrWristTorsionIKSolver` на
   `*Twist*` кости предплечья; решатели на пустых листовых `*_end` удалить.
4. **Интеграция рук** — только `Runtime/Prefabs/HandIntegrations/BigHandsIntegration.prefab`
   (граберы, контроллеры, телепорт). Модели `BigIKHandLeft/Right` **не добавлять**, шаг
   визарда `4. Finalize Rig Mapping` **не запускать** — он переназначает риг на IK-кисти SDK.
   `UxrHandIntegration.TryToMatchHand()` на обеих — ставит интеграцию в центр родной ладони.
   `UxrGrabber.HandRenderer` ← `UxrAvatarRig.TryToGetHandRenderer(avatar, side)` (меш перчаток).
   `_avatarRenderers` — все рендереры, кроме тех, что под `UxrHandIntegration`.
5. **Контроллер и камера** — `VRBattlegrounds.Editor.ControllerAndCameraSetup.Execute()`
   (выделение = риг). Высоту глаз он берёт из Humanoid, `EyesForwardOffset` = кость глаза
   + 2 см — так же, как кнопка SDK «Use Avatar Eyes». Камера окажется внутри головы модели —
   это нормально, голову у своего аватара выключает `LocalDisabledGameObjects` (game-variant.md, 3а). `UxrDummyControllerInput` на корень.
   Настройки Body IK копировать с `PlayerBase`, **исключая `_uxrUniqueId`, `__prefabGuid`,
   `__isInPrefab`** — иначе у двух компонентов окажется один id.
6. `CreateRigInfo` у `UxrAvatar` (приватный, через reflection) — пересчёт осей рига.
7. **Префаб** — `SaveAsPrefabAssetAndConnect` в `Assets/Prefabs/Avatars/<Model>_Rig.prefab`.
   Визард `5. Save as Prefab` пишет в `Prefabs/Player` — там только игровые аватары
   (`PrefabCompositionTests` ищет их по `PlayerController`), поэтому не он.
8. **Позы** — `VRBattlegrounds.Editor.HandPosesSetup.Execute()`, строго **после** сохранения
   префаба: в конце он делает `ApplyPrefabInstance`, а экземпляр модели FBX применить нельзя.
   Позы ложатся в `Assets/Art/Avatars/<имя объекта>/HandPoses/` (15 шт. из пресетов SDK),
   события контроллера `Grip → Grab`, `Button1 → Pointing` проставляются.
9. В префабе рига: все `SkinnedMeshRenderer` под объект `Geo` (его скрывает
   `SpectatorController`), тег корня `Player` (`GameTagsTests`).

Ассистент инспектора `UxrAvatar` (кнопки Fix, `UxrAvatarEditor.cs:209-410`) делает шаги 3–5
теми же вызовами SDK; на готовом риге он показывает «Avatar is ready to rock!».

## Проверка

Применить к кистям `Grab` (правая) и `Pointing` (левая) через
`UxrAvatarRig.UpdateHandUsingDescriptor(avatar, side, pose.HandDescriptorRight/Left)`, снять
крупные скриншоты кистей. Пальцы сгибаются, меш перчатки без разрывов. Префаб при этом не сохранять.

## Производительность Quest

Модели из игр тяжёлые: у MEF 49 `SkinnedMeshRenderer`, ~106 тыс. треугольников, 51 слот
материалов. Сливать в Blender до 1–3 мешей и урезать полигонаж — отдельной задачей; настройке
рук это не мешает.
