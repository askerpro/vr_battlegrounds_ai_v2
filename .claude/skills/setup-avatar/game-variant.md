# Игровой вариант: `<Model>_Base_Avatar` от базы по типу кисти

**Сначала выбрать базу** (обе — варианты `PlayerBase`, сам `PlayerBase` без поз кисти):

| Кисть модели | База | Позы, которые приходят по наследству |
|---|---|---|
| Скелет SDK — 4 кости на палец с пястной (BigHands/Cyborg, путь А с перчаткой SDK) | `PlayerBase_SdkHands` | `Controller*`, `Demo*` — сделаны на скелете киборга |
| Свой скелет — 3 фаланги (путь Б, как MEF) | `PlayerBase_NonSdkHands` | только позы из пака (`HandsPackPoseImporter`) — нейтральны к скелету |

Позы SDK на скелете из 3 фаланг выглядят плохо, поэтому ветки разведены. Проверка — `AvatarHandPoseChainTests`.
Ниже везде, где написано «`PlayerBase`» как родитель варианта, — выбранная база.

Уже созданный вариант переносится на другую базу `AvatarHandBases.Rebase(variant, base)` (меню
`Tools/VR Battlegrounds/Avatars/Hand Bases/Rebase Selected Avatar → …`). Руками не делать: Unity не меняет
родителя варианта через API, инструмент переписывает YAML так, что id всех объектов варианта остаются
прежними — ссылки из сцен, `AvatarData`, оружия не рвутся.

Превращает риг (`Assets/Prefabs/Avatars/<Model>_Rig.prefab`) в игрового персонажа: сеть,
HUD, карманы, хитбоксы, регистрация. Эталон — `Assets/Prefabs/Player/MEF_Base_Avatar.prefab`.

**Образец — `PlayerBase`, не Heavy.** У `Heavy_Soldier_Base_Avatar` нет хитбоксов (WPN-02),
`NetworkTransform` на кистях, `_hudContainer` смотрит
не в камеру, телепорт был на маске `Default`, два `UxrDummyControllerInput` на корне.

## 1. Создать вариант

Во временной сцене: `PrefabUtility.InstantiatePrefab(<база>)`, имя `<Model>_Base_Avatar`.

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
   `BigHandsIntegration`. Остаются `Animator` и, если есть, `Legs Animator` (нет —
   ставится в разделе 5).
4. Корневой `BigHandsIntegration` из `PlayerBase` **оставить** — на нём полный набор
   трекинга и ввода.

## 2. Корневой `UxrAvatar` и контроллер

- `ClearRigElements()` → `SetupRigElementsFromAnimator()` (ищет Animator в детях) →
  `TryToInferMissingRigElements()`; `_rigType = HalfOrFullBody`.
- `_avatarRenderers` — все рендереры, кроме тех, что под `UxrHandIntegration` и камерой.
- `_handPoses` и `_defaultHandPose` — из рига. `_parentPrefab = <база>`,
  `_prefabGuid = GUID варианта` (после сохранения): через цепочку префабов аватар наследует позы
  базы (`Demo*` у `PlayerBase_SdkHands`, позы пака у `PlayerBase_NonSdkHands`) — проверка
  `avatar.GetAllHandPoses()`.
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
| Хитбоксы | `MeshCollider` на `CyborgGeo/*` | Руками не ставить: после регистрации — `Tools/VR Battlegrounds/Avatars/Build Hitboxes` (T-36) — голова, торс, руки, ноги по скелету UltimateXR, слой `Hitbox`, прежние сплошные коллайдеры снимаются (`HitboxTests`) |
| Трекинг кистей в сети | `NetworkTransformUnreliable` на `Hand_Left/Right`, `coordinateSpace = World` | Тот же компонент с теми же настройками на кость кисти рига `UxrAvatar` (`GetHandBone`), `target` = кость. **Только `World`:** в `Local` после IK поза кисти постоянна и рука у чужих замирает (known-issues, Issue 21). Проверяет `PrefabCompositionTests.У_каждого_аватара_кисти_несут_NetworkTransform` |
| Нажатие UI пальцем | `UxrFingerTip` на кончиках указательных | `Tools/VR Battlegrounds/Avatars/Setup Avatar UI Fingertips` (`AvatarFingertipSetup`, палец — из рига `UxrAvatar`). Если кончики стоят на риге, вариант их наследует — повторять не нужно. Кончик обязан быть на кисти, в которую смотрит риг `UxrAvatar` (у пути А — на кисти SDK, не на родной кости), и его `forward` — вдоль пальца: луч касания идёт по нему. Проверяет `AvatarLoadoutTests.Кончики_пальцев_для_UI_смотрят_вдоль_пальца` |
| Карманы | на `Pelvis` / `Spine02` | Префабы из `Assets/Prefabs/Player/Pockets/` на `Hips` / `UpperChest` модели. Позиция = кость + мировое смещение, снятое с киборга; поворот — мировой киборга. Локальные смещения `AvatarPocketSetup` не годятся: оси костей у моделей разные (у CC бедро повёрнуто на 75°). Если у модели есть кобура — `Anchor_Hip_R` на неё. У `Anchor_Hip_R` из префаба уже есть дочерний `GrabProxy` (хват вокруг кобуры, он же точка укладки) — переносить его отдельно не нужно; проверяет `AvatarLoadoutTests.У_кобуры_есть_прокси_хват` |
| Прокси спины | `Anchor_Back._grabProxy` → отдельный `BackGrabProxy` у правого плеча, **и точка укладки** (`Drop Proximity Transform`) → он же | Так же: клон `BackGrabProxy` на `UpperChest`, `ChangeUniqueId(Guid.NewGuid())` для его UXR-компонентов; `GrabProxy`-ребёнка из префаба `Anchor_Back` удалить; у якоря `_grabProxy` → клон **и** `_dropProximityTransformUseSelf = false`, `_dropProximityTransform` → клон. Иначе игрок подносит оружие к подсвеченному прокси, а SDK меряет укладку от центра спины — оружие падает (так было у MEF). Сторож — `AvatarLoadoutTests.Карман_с_прокси_кладёт_там_же_где_отдаёт` |

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
- В зеркале голова видна: `LocalHeadMirrorVisibility` (ставится сам на аватар с непустым
  списком) включает эти объекты обратно на слое `LocalHead`, который своя камера не рисует,
  а камера зеркала рисует. Кода на аватар не нужно — только правильный список.
  Проверка — `LocalHeadMirrorVisibilityTests`.

Проверка — во временной сцене два экземпляра варианта, `InitializeNetworkAvatar(avatar,
isLocal: true/false, …)`: у локального выключены ровно меши списка, у чужого — ничего, число
активных не-trigger коллайдеров одинаковое. Вне Play Mode метод падает на `netId` в строке
лога — это шум харнесса: на время вызова поставить
`UxrGlobalSettings.Instance.LogLevelNetworking = None` и вернуть. Скриншот с точки глаз
симптом не воспроизводит (в bind-позе камера целиком внутри мешей, их грани отсечены) —
визуальная проверка только на шлеме.

Карманы видно в Scene View по цветным меткам `GrabbableAnchorGizmos` (оранжевый — спина,
голубой — бедро, зелёный — магазины, пунктир — к прокси). Двигать их — выделив метку кликом;
у выделенного видны зоны укладки (сплошной контур) и хвата (пунктир).

Размеры зон подбирать в шлеме: `Tools/VR Battlegrounds/Debug/Anchor Zones In Headset`, Play Mode
через Quest Link, менять `Max Place Distance` / `Max Distance Grab` в инспекторе живого аватара —
зона вспыхивает, когда предмет или ладонь достаёт. Затем `Tools/VR Battlegrounds/Avatars/Save
Pocket Zones To Prefab` — после выхода из Play Mode значения лягут в префаб этого аватара.

Вибрацию карманов (`PocketHaptics` на корне) вариант наследует от `PlayerBase` — не удалять:
без неё карманы в игре не найти на ощупь. Аватар не от `PlayerBase` — добавить компонент на
корень (`AvatarLoadoutTests.На_корне_есть_хаптики_карманов`). То же с кнопкой выброса магазина
`MagazineEjectInput` (`На_корне_есть_кнопка_выброса_магазина`).

Звуки карманов (`AnchorSound` + `AudioSource` на самом якоре: укладка — clip источника,
доставание — `Take Out Clip`) приходят с префабами карманов из `Pockets/`. Карман, собранный не
из них, настроить так же; источник — не на объекте `Activate On Placed`
(`AvatarLoadoutTests.Карманы_звучат_при_укладке_и_доставании`).

HUD наследуется от `PlayerBase` (`Camera Controller/Camera/HUDContainer`). Если его нет —
`Tools/VR Battlegrounds/Avatars/Inject HUD to Selected Avatar`.

Проверить: внутри аватара нет дублей `UniqueId` у UXR-компонентов. Хитбоксы — сборщиком после регистрации
(`Build Hitboxes`), другие сплошные коллайдеры на аватаре не нужны (коробка камеры — trigger).

## 4. Сохранить

`SaveAsPrefabAssetAndConnect(root, "Assets/Prefabs/Player/<Model>_Base_Avatar.prefab")` —
из экземпляра `PlayerBase` получится Variant. Затем через `LoadPrefabContents`:
`_prefabGuid`, `_parentPrefab`, тег вложенного рига `Untagged`.

**`_assetId`.** Mirror затирает его при каждом `SaveAsPrefabAsset`, а нормализатор умеет
только переписать существующую строку. Если в файле варианта нет переопределения `_assetId`,
создать его (записать любое значение в `NetworkIdentity._assetId` через `SerializedObject` и
сохранить), затем — после **последнего** сохранения — `Tools/VR Battlegrounds/VersionControl/Normalize Network Asset Ids`.

## 5. Legs Animator (обязателен для каждого аватара)

Процедурные ноги против проваливания ботинок при приседании. Обязателен у каждого аватара
реестра, исключений нет: у киборга ноги робота Kyle (`Tools/VR Battlegrounds/Avatars/Build Cyborg Legs`,
`CyborgLegsBuilder` — образец пришивания чужих ног к модели без ног). Проверка —
`AvatarLoadoutTests.Legs_Animator_настроен_на_своих_костях` и
`PrefabCompositionTests.У_каждого_аватара_один_Legs_Animator_на_humanoid_риге`.

Таз плагина (`Hips`) — **таз `UxrAvatarRig`**, а ноги плагина — те же кости, что ноги `UxrAvatarRig`
(по ним строятся хитбоксы). Обычно это и humanoid-Hips; у киборга нет: его таз и позвоночник —
соседи под `CyborgRig`, humanoid-Hips — `CyborgRig`, а его UltimateXR при старте переносит под
`Dummy Forward` (корень тела `UxrBodyIK`) — мост вернул бы ему локальную позу из-под старого
родителя. Если объект с `Animator` не на полу (у киборга `Cyborg` висит на 1.55 м), поле
`baseTransform` плагина — корень аватара: плагин инициализируется до привязки моста и мерил бы
таз от висящего объекта.

Компоненты — на объекте рига с `Animator` (`<Model>_Rig`, не на корне варианта):
- `LegsAnimator` (FImpossible Creations), **включён** в префабе. Плагин запоминает опорную
  высоту таза при инициализации, и это должна быть поза модели. Включённый позже (как раньше
  делал мост) он инициализировался в позе под камерой и держал таз на +14 см;
- `LegsAnimatorUxrBridge` (`Assets/Integration/`), **включён**. Когда UltimateXR создаст
  `Dummy Forward`, мост подставляет плагину свой корень на полу (`LegsAnimator_RootAnchor`),
  каждый кадр возвращает таз в позу префаба (вместо отсутствующей анимации) и сам бросает
  луч до пола под ногами. Устройство — XML-комментарий класса; тесты — `LegsGroundingTests`.

**Настройки не выставлять руками, а копировать с MEF** (эталон — `MEF_Base_Avatar` → `MEF_Rig`;
в коде так делает `CyborgLegsBuilder.SetupLegsAnimator`). Через `execute_code`, оба
префаба в `LoadPrefabContents`:
1. `AddComponent` обоих типов на риг нового аватара, `EditorUtility.CopySerialized(mef, new)`.
   Сборки плагина и моста — `Assembly-CSharp`, из `execute_code` тип брать по имени через
   `GetComponents<Component>()`.
2. Ссылки, указывающие в риг эталона, переназначить на свои: `Mecanim`,
   `CustomModules[i].Parent`, `Legs[i].Owner` → свой `Animator`/`LegsAnimator`; `Hips` →
   таз `UxrAvatarRig` (см. выше); `Legs[0]` = левая `UpperLeg/LowerLeg/Foot`, `Legs[1]` = правая
   (`BoneStart/BoneMid/BoneEnd`). Модули (`ModuleReference`) — ассеты, остаются общими.
   **Ловушка:** у Heavy в `LoadPrefabContents` `Animator.GetBoneTransform` возвращает `null`,
   поэтому соответствие «кость Heavy → кость нового» через него не строится, а ссылки на
   чужой префаб при сохранении молча обнуляются. Кости нового аватара брать его
   `GetBoneTransform` (у MEF работает) или по `avatar.humanDescription.human` по имени.
3. `Calibrate = FixedCalibrate` (2) — у игровых ригов нет контроллера анимации. В режиме
   `Calibrate` (1) плагин в начале кадра сбрасывает только повороты костей, а позицию таза ждёт
   от анимации. Сброс таза в мосте это страхует, но режим всё равно 2 — тест его требует.
4. **`Legs[i].AnkleToHeel`/`AnkleToFeetEnd` своего рига** — `leg.RefreshLegAnkleToHeelAndFeet(корень
   варианта)` в позе префаба (кнопка обновления в инспекторе Legs Animator делает то же). Это
   высота лодыжки над подошвой: плагин ставит на пол пятку, а не лодыжку. Не копировать с эталона —
   у другого рига другие оси стопы. У Heavy |AnkleToHeel| = 0.111, у MEF 0.135, у киборга 0.112.
   Если кости стопы не выровнены по корню — `RefreshLegAnkleToHeelAndFeetAndAxes` (и оси стопы).
5. `LegsAnimatorUxrBridge.footHeightOffset = 0`. Прежние 0.15 при нулевом `AnkleToHeel` были
   костылём: плагин принимал поднятый пол за возвышение и поднимал под него всё тело — таз
   +15 см, голова в плечах.
6. Сохранить, перечитать с диска и убедиться, что ни одна ссылка не пуста. **Не через
   `SaveAsPrefabAsset` без проверки диффа**: он обнуляет переопределения `UxrAvatar._rigInfo`
   (данные пальцев, которые SDK сам не пересчитывает) — после сохранения сверить дифф префаба и
   вернуть `_rigInfo` к прежним значениям, если он поменялся.
7. После последнего сохранения — `Normalize Network Asset Ids` (см. раздел 4).

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
