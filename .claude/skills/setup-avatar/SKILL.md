---
name: setup-avatar
description: Настроить новый аватар игрока из стороннего FBX/префаба — от импорта модели до игрового варианта PlayerBase, регистрации в командах и зелёных тестов. Использовать, когда пользователь просит «настроить аватара», «сделать игрового персонажа из модели», «добавить скин», «подключить модель к UltimateXR», или даёт путь к FBX/префабу персонажа.
---

# Настройка аватара игрока

Задача: $ARGUMENTS

Этот файл — маршрут. Подробности этапов лежат рядом, читать по мере прохождения, не впрок:

| Файл | Этап |
|---|---|
| [rig-native-hands.md](rig-native-hands.md) | Риг, путь Б — родные кисти модели |
| [rig-amputation.md](rig-amputation.md) | Риг, путь А — ампутация в Blender + кисти SDK; грабли Blender |
| [game-variant.md](game-variant.md) | Игровой вариант от базы по типу кисти (`PlayerBase_SdkHands` / `PlayerBase_NonSdkHands`), перенос с `Cyborg`, регистрация, позы хвата оружия |

Эталон, собранный по этому маршруту: `Assets/Prefabs/Avatars/MEF_Rig.prefab` →
`Assets/Prefabs/Player/MEF_Base_Avatar.prefab`. **Не бери за образец Heavy** — у него нет
хитбоксов, NT на кистях, `SpectatorController.Geo`, у HUD и телепорта неверные ссылки/маски.
Образец игрового варианта — `PlayerBase`.

## 0. Окружение

- Нужен Unity MCP (`mcp__unityMCP__*`). Нет — `/unity-check`, `.agents/rules/unity_mcp.md`;
  без MCP сцены и префабы не менять, только готовить код и список ручных шагов.
- `execute_code` компилируется **CodeDom (C# 6)**: без локальных функций, `Object` неоднозначен —
  пиши `UnityEngine.Object`, `Array.Find` с лямбдой не выводит тип — бери LINQ.
- Работай во **временной additive-сцене** (`EditorSceneManager.NewScene(EmptyScene, Additive)`),
  закрывай её без сохранения. Открытую карту не пачкать; перед стартом проверить `isDirty`.

## 1. Разведка модели — определяет весь путь

Через `execute_code` на FBX (не читать `.prefab`/`.fbx` целиком):

- импорт: `animationType`, `globalScale`; габариты, высота головы и глаз (≈1.6–1.7 м);
- кости: есть ли пальцы по 3 фаланги на обеих руках, twist-кости предплечья, кости глаз;
- на какие кости заскинен меш кистей/перчаток (веса по `boneWeights`);
- число `SkinnedMeshRenderer`, треугольников, слотов материалов — бюджет Quest.

Развилка:

| Скелет | Путь |
|---|---|
| Полный пальцевой (Reallusion CC `CC_Base_*`, Mixamo с пальцами, UE-манекен) | **Б — родные кисти**, [rig-native-hands.md](rig-native-hands.md) |
| Пальцев нет / кисти вшиты «варежкой» | **А — ампутация + кисти SDK**, [rig-amputation.md](rig-amputation.md) |

При сомнении — спроси пользователя, показав найденные кости.

## 2. Риг (`Assets/Prefabs/Avatars/<Model>_Rig.prefab`)

Путь Б, коротко (детали — [rig-native-hands.md](rig-native-hands.md)):
1. FBX → Humanoid, проверить автомаппинг; глаза/челюсть дописать в `HumanDescription`.
2. `UxrAvatar` + `SetupRigElementsFromAnimator()` + `TryToInferMissingRigElements()`; снять
   torsion-решатели с пустых `*_end`.
3. Только `BigHandsIntegration` + `TryToMatchHand()`; **без** `BigIKHand*` и без шага
   `4. Finalize Rig Mapping`.
4. `ControllerAndCameraSetup.Execute()`; настройки Body IK — как у `PlayerBase`.
   Копируя сериализованные поля с другого компонента, **исключай `_uxrUniqueId`,
   `__prefabGuid`, `__isInPrefab`** — иначе дубль ID.
5. Кончики пальцев для UI — `AvatarFingertipSetup.Setup(avatar)`: `UxrFingerTip` на концевой
   кости указательных из рига, `forward` вдоль пальца. Без него планшет пальцем не нажать.
6. Сохранить префаб → `HandPosesSetup.Execute()` (именно в этом порядке).
7. Меши под `Geo`, тег корня `Player`.

**Проверка этапа (обязательна):** применить к кистям позы `Grab` и `Pointing`
(`UxrAvatarRig.UpdateHandUsingDescriptor`), снять скриншоты кистей крупно
(`manage_camera screenshot` с `view_position`/`view_target`, свет — временный Directional).
Пальцы должны сгибаться без разрывов меша. Не видно — не идти дальше.

## 3. Игровой вариант (`Assets/Prefabs/Player/<Model>_Base_Avatar.prefab`)

База по типу кисти: скелет SDK (4 кости на палец) — `PlayerBase_SdkHands`, свои 3 фаланги —
`PlayerBase_NonSdkHands` (таблица в [game-variant.md](game-variant.md)).

Экземпляр базы во временной сцене → удалить `Cyborg` → вложить риг → удалить из
вложенного рига `UxrStandardAvatarController`, `UxrDummyControllerInput`, `UxrAvatar`,
`Camera Controller`, `BigHandsIntegration` → `SaveAsPrefabAssetAndConnect` (получится Variant).

Перед удалением `Cyborg` сними с него данные (позы карманов, настройки NT кистей) — после
удаления их не достать. Что переносить — таблица в [game-variant.md](game-variant.md): хитбоксы, NT кистей, `UxrFingerTip`, карманы, `SpectatorController.Geo`,
корневой `UxrAvatar` (риг, позы, события контроллера, высота глаз/шеи), `_parentPrefab = <база>`.

**Своя голова в камере** («вижу голову изнутри») — меши головы в
`UxrMirrorAvatar._localDisabledGameObjects`, раздел 3а [game-variant.md](game-variant.md).
Не лечить подкруткой `EyesForwardOffset` — так было сделано у старых аватаров, это обход.

Клонируя объекты с UXR-компонентами (`BackGrabProxy`), выдай им новый id:
`component.ChangeUniqueId(Guid.NewGuid())`, затем проверь, что дублей id внутри аватара нет.

## 4. Регистрация

`AvatarData` (+ иконка — отрендерить модель в preview-сцене в PNG, импорт как у
`AvatarsIcons/cyborg.png`) → `AvatarsRegistry` → слот в `Data/Teams/*_Team.asset` →
`spawnPrefabs` на `--- MANAGERS ---`. После правки `MANAGERS` посмотреть `git diff`: должна
добавиться одна строка.

Позы хвата оружия: у `UxrGrabbableObject` записи по GUID аватара, наследования от `PlayerBase`
нет. Дописать запись нового варианта во все точки хвата, где есть запись Heavy
(копия записи, GUID — нового варианта). Сохранять `AssetDatabase.SaveAssetIfDirty` и
проверять по файлу, что GUID записан: несохранённый грязный префаб оружия теряется и валит
`ArsenalPrefabMutationTests`.

## 5. Самопроверка — задача не закрыта без неё

1. После последнего сохранения варианта — `Tools/VR Battlegrounds/VersionControl/Normalize Network Asset Ids`
   (Mirror затирает `_assetId` при каждом `SaveAsPrefabAsset`).
2. Перекомпиляция (`refresh_unity compile=request`) — `AvatarLoadoutTests` берёт аватары из
   реестра через `TestCaseSource`, без неё новый аватар в выборку не попадёт.
3. `run_tests EditMode VrBattlegrounds.Tests.EditMode`. Новый аватар должен пройти
   `PrefabCompositionTests`, `GameTagsTests`, `NetworkAssetIdOnDiskTests` и все
   `AvatarLoadoutTests(<аватар>)`, кроме тех, что падают **одинаково** у Heavy (общие дефекты
   префабов карманов, `PlayerLoadoutManager`). Сравни списки поаватарно. Позы хвата предметов —
   `GrabPoseCoverageTests(<предмет>, <аватар>)`: запись хвата на базе кисти покрывает все её варианты.
   Обязательно зелёный — `Кончики_пальцев_для_UI_смотрят_вдоль_пальца`: иначе планшет пальцем
   не нажать (у Heavy он красный — левого кончика нет на кисти SDK, это не образец).
   Если тестовая сборка не собирается из-за чужих правок, тест можно вызвать отражением:
   `AvatarLoadoutTests` → метод по имени → `Invoke(instance, path)`.
4. Проблемы харнесса MCP: задачу снимает перезагрузка домена — дождись `refresh_unity
   wait_for_ready`, `clear_stuck`, повтори. Падение, которое не воспроизводится в
   отдельном прогоне фикстуры, проверить повторным полным прогоном, прежде чем чинить.
5. `read_console` — без ошибок; `git status` — только ожидаемые файлы.

Чего автономно не проверить (уровень шлема, `Docs/testing.md`): посадка оружия в руке,
положение карманов и хитбоксов в движении, производительность на Quest. Перечислить это
пользователю отдельным списком, а не выдавать за проверенное.

## 6. Документация и итог

- `Docs/CHANGELOG.md` — запись об аватаре; новое неочевидное — в файлы этого скилла (тот этап, где споткнулся).
- Отчёт пользователю: что создано (пути), что проверено и чем, что осталось на шлем,
  бюджет модели (меши/треугольники) и нужна ли оптимизация.
- Не коммитить без подтверждения (`/commit`).
