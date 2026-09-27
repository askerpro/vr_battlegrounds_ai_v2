# Игровой вариант: `<Model>_Base_Avatar` от `PlayerBase`

Превращает риг (`Assets/Prefabs/Avatars/<Model>_Rig.prefab`) в игрового персонажа: сеть,
HUD, карманы, хитбоксы, регистрация. Эталон — `Assets/Prefabs/Player/MEF_Base_Avatar.prefab`.

**Образец — `PlayerBase`, не Heavy.** У `Heavy_Soldier_Base_Avatar` нет хитбоксов (WPN-02),
`NetworkTransform` на кистях, `SpectatorController.Geo/Ghost` пусты, `_hudContainer` смотрит
не в камеру, телепорт был на маске `Default`, два `UxrDummyControllerInput` на корне.

## 1. Создать вариант

Во временной сцене: `PrefabUtility.InstantiatePrefab(PlayerBase)`, имя `<Model>_Base_Avatar`.

**До удаления `Cyborg` снять с него данные** — потом их не достать:
- позы карманов относительно их костей (`MagazinePocket`, `Anchor_Hip_R` — на `Pelvis`;
  `Anchor_Back`, `BackGrabProxy` — на `Spine02`);
- настройки `NetworkTransformUnreliable` с `Hand_Left` (`EditorUtility.CopySerialized` во временный компонент);
- копию `BackGrabProxy` (`Object.Instantiate` в ту же временную сцену).

Затем:
1. Удалить `Cyborg` (в Unity 6 удаление объекта из экземпляра — это override варианта).
2. Вложить риг (`InstantiatePrefab(<Model>_Rig, root)`, нулевая локальная поза, имя `<Model>_Rig`).
3. Из вложенного рига удалить `UxrStandardAvatarController`, `UxrDummyControllerInput`,
   `UxrAvatar` (в этом порядке — контроллер требует аватар), объекты `Camera Controller` и
   `BigHandsIntegration`. Остаются `Animator` и, если есть, `Legs Animator`.
4. Корневой `BigHandsIntegration` из `PlayerBase` **оставить** — на нём полный набор
   трекинга и ввода.

## 2. Корневой `UxrAvatar` и контроллер

- `ClearRigElements()` → `SetupRigElementsFromAnimator()` (ищет Animator в детях) →
  `TryToInferMissingRigElements()`; `_rigType = HalfOrFullBody`.
- `_avatarRenderers` — все рендереры, кроме тех, что под `UxrHandIntegration` и камерой.
- `_handPoses` и `_defaultHandPose` — из рига. `_parentPrefab = PlayerBase`,
  `_prefabGuid = GUID варианта` (после сохранения): через цепочку префабов аватар наследует
  оружейные позы `Demo*` — проверка `avatar.GetAllHandPoses()`.
- `UxrHandIntegration.TryToMatchHand()` на обеих корневых интеграциях;
  `UxrGrabber.HandRenderer` ← `UxrAvatarRig.TryToGetHandRenderer`.
- `UxrStandardAvatarController._listControllerEvents` — позы рига вместо поз киборга;
  `_bodyIKSettings._eyesBaseHeight/_eyesForwardOffset` — из рига;
  `_neckBaseHeight/_neckForwardOffset` — по кости шеи модели (у киборга свои).
- `CreateRigInfo` через reflection.
- Телепорт: `ValidTargetLayers = Ground`, `BlockingTargetLayers = 63`.

## 3. Что перенести с `Cyborg`

| Что | У киборга | Для новой модели |
|---|---|---|
| Хитбоксы | `MeshCollider` на `CyborgGeo/*` | Капсулы/сфера дочерними объектами на костях: голова (сфера ~0.13), грудь, живот, бёдра, голени. Не-trigger, слой `Default` (оружие бьёт `Default\|Ground`). Руки не закрывать — мешают хвату. `MeshCollider` скина застывает в bind-позе |
| Трекинг кистей в сети | `NetworkTransformUnreliable` на `Hand_Left/Right` | Тот же компонент с теми же настройками на `animator.GetBoneTransform(LeftHand/RightHand)`, `target` = кость |
| Нажатие UI пальцем | `UxrFingerTip` на кончиках указательных | `Tools/VR Battlegrounds/Avatars/Setup Avatar UI Fingertips` (`AvatarFingertipSetup`, берёт `IndexDistal` из Humanoid) |
| Карманы | на `Pelvis` / `Spine02` | Префабы из `Assets/Prefabs/Player/Pockets/` на `Hips` / `UpperChest` модели. Позиция = кость + мировое смещение, снятое с киборга; поворот — мировой киборга. Локальные смещения `AvatarPocketSetup` не годятся: оси костей у моделей разные (у CC бедро повёрнуто на 75°). Если у модели есть кобура — `Anchor_Hip_R` на неё |
| Прокси спины | `Anchor_Back._grabProxy` → отдельный `BackGrabProxy` у правого плеча | Так же: клон `BackGrabProxy` на `UpperChest`, `ChangeUniqueId(Guid.NewGuid())` для его UXR-компонентов; `GrabProxy`-ребёнка из префаба `Anchor_Back` удалить, `_grabProxy` → клон |
| Наблюдатель | `SpectatorController.Geo = Cyborg/CyborgGeo` | `Geo` рига. `Ghost` пуст — в режиме наблюдателя модель просто скрывается |

## 3а. Своя голова не должна попадать в камеру

Штатный механизм SDK — `UxrMirrorAvatar._localDisabledGameObjects` на корне варианта
(подсказка поля: «disabled when the avatar is in local mode, to avoid intersections with the
camera»). `InitializeNetworkAvatar` выключает эти объекты **только у своего аватара**; чужие
игроки видят голову целиком. Кода не нужно.

- В список — все `SkinnedMeshRenderer` модели, у которых ≥ 80 % вершин главной костью имеют
  голову или её потомков (лицо, глаза, шлем, очки, гарнитура, нашивки на шлеме). Считать по
  `sharedMesh.boneWeights` (`boneIndex0`), не по именам. У MEF — 15 мешей.
- Поэтому меши головы должны быть **отдельными объектами**. Если голова слита с телом в один
  меш — отделить её в Blender при оптимизации модели.
- `EyesForwardOffset` не трогать: SDK («Use Avatar Eyes», `UxrIKBodySettingsDrawer`) ставит его
  на кость глаза + 2 см, камера по замыслу стоит в глазах. Подкрутка offset до 0.11 у старых
  аватаров (Heavy, Military) — обход: тело уезжает назад от реальной головы игрока.
- Цена: у своей головы нет тени. Хитбокс головы — на кости, он не выключается.

Проверка — во временной сцене два экземпляра варианта, `InitializeNetworkAvatar(avatar,
isLocal: true/false, …)`: у локального выключены ровно меши списка, у чужого — ничего, число
активных не-trigger коллайдеров одинаковое. Вне Play Mode метод падает на `netId` в строке
лога — это шум харнесса: на время вызова поставить
`UxrGlobalSettings.Instance.LogLevelNetworking = None` и вернуть. Скриншот с точки глаз
симптом не воспроизводит (в bind-позе камера целиком внутри мешей, их грани отсечены) —
визуальная проверка только на шлеме.

HUD наследуется от `PlayerBase` (`Camera Controller/Camera/HUDContainer`). Если его нет —
`Tools/VR Battlegrounds/Avatars/Inject HUD to Selected Avatar`.

Проверить: внутри аватара нет дублей `UniqueId` у UXR-компонентов; не-trigger коллайдеров
столько, сколько поставлено хитбоксов (коробка камеры — trigger).

## 4. Сохранить

`SaveAsPrefabAssetAndConnect(root, "Assets/Prefabs/Player/<Model>_Base_Avatar.prefab")` —
из экземпляра `PlayerBase` получится Variant. Затем через `LoadPrefabContents`:
`_prefabGuid`, `_parentPrefab`, тег вложенного рига `Untagged`.

**`_assetId`.** Mirror затирает его при каждом `SaveAsPrefabAsset`, а нормализатор умеет
только переписать существующую строку. Если в файле варианта нет переопределения `_assetId`,
создать его (записать любое значение в `NetworkIdentity._assetId` через `SerializedObject` и
сохранить), затем — после **последнего** сохранения — `Tools/VR Battlegrounds/VersionControl/Normalize Network Asset Ids`.

## 5. Legs Animator (по желанию)

Процедурные ноги против проваливания ботинок при приседании. Компоненты — на объекте
рига с `Animator` (не на корне варианта):
- `Legs Animator` (FImpossible Creations) — подхватит `Animator` и таз; модули
  `Extra_Rotation Stability` и `UxrLamStepFurther`;
- `LegsAnimatorUxrBridge` (`Assets/Integration/`): `Use Dynamic Floor Offset = true`,
  `Foot Height Offset = 0.15`.

## 6. Регистрация

1. `AvatarData` в `Assets/Data/Player/Avatars/<Name>.asset`: `displayName`, `icon`, `prefab`.
   Иконку отрендерить: preview-сцена (`EditorSceneManager.NewPreviewScene`), риг без
   интеграции и камеры, два Directional, камера `fov≈22` на грудь-голову, 512×512 в
   `Assets/Art/Textures/AvatarsIcons/<name>.png`, импорт как у `cyborg.png` (Sprite).
2. `Assets/Data/Player/Avatars/AvatarsRegistry.asset` → `avatars`.
3. `Assets/Data/Teams/*_Team.asset` → первый пустой слот `avatars` (пустые остались от
   удалённых скинов) или в конец.
4. `spawnPrefabs` на `Assets/Prefabs/Managers/--- MANAGERS ---.prefab`. После — `git diff`:
   ровно одна добавленная строка.

## 7. Позы хвата оружия

`UxrGrabbableObject` хранит запись позы на каждую точку хвата **по GUID конкретного
аватара**; записи `PlayerBase` там нет, наследовать нечего. Без записи рука держит оружие
раскрытой ладонью.

Для каждого `UxrGrabbableObject` в `Assets/Prefabs` (не во вложенных экземплярах), в каждой
точке (`_grabPoint`, `_additionalGrabPoints[]`) с записью Heavy в `_avatarGripPoseEntries`:
вставить копию записи Heavy (`InsertArrayElementAtIndex` дублирует), `_avatarPrefabGuid` =
GUID варианта, `_handPose` — одноимённая поза нового рига, если исходная из папки Heavy.
Сохранить `AssetDatabase.SaveAssetIfDirty(root)` и **проверить по файлу**, что GUID записан:
грязный несохранённый префаб оружия перечитывается с диска и валит `ArsenalPrefabMutationTests`.

Сейчас это `Gun_real`, `M16_Rifle_prefab`, `M16_Magazine` (7 точек). В диффе появятся
перевыданные `_uxrUniqueId` — это нормально (known-issues #11).

## 8. Проверка

См. раздел «Самопроверка» в `SKILL.md`. Руками на шлеме (автономно не проверить): своя голова
не видна изнутри, посадка оружия в руке, положение карманов и кобуры, хитбоксы в движении, телепорт, нажатие UI
пальцем, FPS на Quest.
