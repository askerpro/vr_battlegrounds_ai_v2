# Перенос хватов из оружейных паков (Grip Extraction)

## Назначение

Направление 3 работ по хватам: editor-конвейер снимает хват из кадра клипа оружейного пака (HandsPack,
KINEMATION) и записывает его на предмет под кисть игрового аватара — позу пальцев (`UxrHandPoseAsset`),
align обеих рук и запись `UxrGripPoseInfo` по GUID аватара. Кисть источника и кисть цели — параметры:
«конвейер общий, а киборг, MEF и оригинальная рука из SDK являются параметрами» (решение пользователя
2026-10-13).

Место среди направлений:

- **1 — анализ** ([автоанализ качества хвата](grip-quality-analysis.md)): измеряет готовый хват. Grip Extraction
  вызывает его после записи (`GripFitCheck`) и берёт из отчёта вердикт, лист ревью и подсказки.
- **3 — этот документ**: первичный перенос хвата из пака. Результат — стартовая поза и посадка, а не
  принятый хват.
- **4 — ручная доводка** в редакторе поз («Обхватить», IK кончиков, метрика точки фаланги;
  [grip-pose-editor.md](grip-pose-editor.md)): правит позу, которую записал перенос или кто-то вручную.
  Метрика «точка фаланги» (N из 15 фаланг кисти в контакте) — основная метрика качества контакта
  (решение пользователя 2026-10-20).

Основной аватар игры — семейство CyborgHandsBase: база
`Assets/Prefabs/Player/Avatars/CyborgHands/Base/CyborgHandsBase.prefab` (GUID `71b74b0d08444f49bbb7aa2828d0890a`),
конечные `MEF_cyborg_hands_Player`, `MEF_cyborg_hands_Player_Black`, `PlayerControllersCyborgAvatar`;
позы — `Assets/Prefabs/Player/HandPoses/CyborgHands/`. Все прогоны 2026-10-13…15 шли на `MEF_cyborg_hands`
до переезда префабов (2026-10-16 — база CyborgHandsBase, 2026-10-19 — каталог `Avatars/CyborgHands/`).
Известные дефекты хватов с аватаром и статусом — в [реестре](grip-known-issues.md).

## Конвейер

Код — `Assets/Editor/VR_Battlegrounds/Avatars/GripExtraction/` (далее `GE/`), пространство имён
`VrBattlegrounds.Editor.Avatars`. Окна и пункта меню у конвейера нет: вход — `GripExtraction.RunJson(string)`
или `GripExtraction.Run(GripExtractionRequest)` из `execute_code`. Пункты меню
`VR Battlegrounds/Avatars/Черновики/Cyborg Hands/…` в `GripTargetProfile.cs` относятся к миграции дерева
CyborgHandsBase, а не к переносу хватов.

```text
GripExtractionRequest (JSON)
  │
  ├─ GripTargetProfile.Get(target).With(anchor, encoding, registerPoses, removeReplacedAligns)
  ├─ IGripSource (pack) .Select(recipes) ─────────── точки GripSourcePoint
  ├─ GripTargetRig(profile.RigPrefabPath) + CheckRig ── GUID записи в цепочке рига, пястные
  │
  ├─ для каждой точки GripExtractor.Extract (только чтение, LoadPrefabContents без сохранения):
  │    1. GripPoseEncoder.Encode — поза обеих рук (вторая рука — зеркало), curl
  │    2. поза на риге цели — центр ладони по суставам в этой позе
  │    3. GripPlacement.Compute — опора профиля, масштаб k, сдвиг по нормали ладони
  │    4. сверки: прежняя формула (wrist, s=0), align с именем профиля, align текущей записи,
  │       прямой вызов HandsPackGripAligner.Place (только опора wrist, для R0)
  │    5. рантайм против редактора, форма против источника, против существующего ассета позы
  ├─ EvaluateR0
  ├─ Write (Preview | Apply):
  │    GripPoseAssets → HandsPackPoseImporter.RegisterOnPoseBase → GripRecordWriter.WriteWeapon
  │    → GripFitCheck (Apply и analyze=true)
  └─ отчёт <outDir>/grip-extraction-<mode>.json
```

| Часть | Что делает | Код |
|---|---|---|
| Вход | Разбор запроса, сторожа Play/compile/import, порядок шагов, R0, сводка | `GE/GripExtraction.cs`, `GE/GripExtractionRequest.cs` |
| Источник | Точки и кадр хвата | `GE/IGripSource.cs`, `GE/PackClipGripSource.cs` |
| Нейтральный хват | Кадр без привязки к кисти цели | `GE/GripSample.cs` |
| Захват геометрии источника | Кожа, кадры обеих рук и меши предмета в системе корпуса (не используется `Run`) | `GE/GripSourceContactCapture.cs` |
| Профиль цели | Риг, GUID записи, база поз, папка поз, имя align, опора, кодирование | `GE/GripTargetProfile.cs` |
| Риг цели | Копия префаба аватара в preview-сцене, запястье, центр ладони, нормаль, граббер | `GE/GripTargetRig.cs` |
| Посадка | Align по опоре, k и сдвигу; обратный ход snap для проверки | `GE/GripPlacement.cs` |
| Кодирование позы | Мировой путь на риге цели, curl, проверки рантайм/форма | `GE/GripPoseEncoder.cs` |
| Ассеты поз | Fixed-поза обеих рук, перезапись на месте, проверка регистрации | `GE/GripPoseAssets.cs` |
| Запись хвата | Align, `UxrGripPoseInfo`, сохранение префаба, `_assetId` | `GE/GripRecordWriter.cs` |
| Сторож идентификаторов | Снимок `_uxrUniqueId`, `__prefabGuid`, `__isInPrefab`, `_assetId`, `sceneId` до/после | `GE/GripPrefabIdentityGuard.cs` |
| Проверка результата | Hand Pose Fit по сохранённой точке, подсказки следующей итерации | `GE/GripFitCheck.cs` |
| Отчёт | Метрики точек и рук, R0, предупреждения, записанное | `GE/GripExtractionResult.cs` |

Второго писателя хватов нет: рецепты, регистрация поз в базе, создание align и удаление прежних align —
общие с прежним импортёром `HandsPackPoseImporter`.

## Источники

| `source` | Состояние | Что даёт |
|---|---|---|
| `pack` | Реализован (`PackClipGripSource`) | Кадр клипа пака: HandsPack — 13 точек (`HandsPackPoseImporter.Recipes`, экстрактор `HandsPackPoseExtractor`), KINEMATION — 16 точек (`KinemationWeaponBuilder.PoseRecipes` по `KinemationWeaponBuilder.All`, экстрактор `KinemationPoseExtractor`) |
| `avatar-record` | Не реализован | Запись хвата другого аватара на предмете (план R3) |
| `manual` | Не реализован | Ручные точки подушечек из `grip-authoring`, заполнил бы `GripSample.Pads` |

Классов `AvatarRecordGripSource` и `ManualGripSource` в коде нет: они названы только в комментарии
`IGripSource`, а `GripExtraction.CreateSource` на любой `source`, кроме `pack`, бросает
«пока не реализован».

Покрытие рецептов (29 точек): HandsPack — BrowningHiPower (позы `HandsPack_Gun_*`), Scar, MP5K, PPK,
Revolver, SniperRifle по две точки, Uzi — только p0 (левая рука пака не на оружии); KINEMATION — SRM12, R08,
AK105, Viper, MKR9, Herrington, Mk14, TR15 по две точки. Основная точка (`*_Grip`, p0) снимается с правой
руки источника, вспомогательная (`*_Support`, p1) — с левой. Кадр — 0 с клипа. Затворы, магазины, патроны,
планшет и жетон в рецептах отсутствуют.

Селекторы `recipes`: `all`, `hands`, `kinemation`, имя позы (`HandsPack_Scar_Grip`) или имя префаба оружия
(`Scar`, `AK105`); регистр не важен, несовпавший селектор — ошибка.

`PackClipGripSource.Sample(point, captureSourceContact: true)` дополнительно снимает `GripSourceContactFrame`:
кадры **обеих прямых рук** клипа со своими ролями (без зеркала), skinning кожи с весами и меши предмета
в системе корпуса (`body-rigid-meters`). `Pads` при этом не заполняются (`ContactTargetsValidated = false`).
`GripExtraction.Run` этот режим не вызывает; потребитель — контактный IK направления 4
(`ContactIkSourceTargetMapping.FromSource` в `GripAuthoring/ContactIk/ContactIkSurfaceAnchor.cs`).

## Нейтральный хват и центр ладони

`GripSample` — то, что источник знает о хвате, без кисти цели:

| Поле | Смысл |
|---|---|
| `Side` | Рука источника, с которой снят кадр |
| `Hand` | Поза пальцев (`UxrHandDescriptor`) в универсальных осях SDK; у паков — без пястных |
| `WristBodyPosition`, `WristBodyRotation` | Корпус предмета в системе кисти: начало — запястье, оси — `HandUniversalLocalAxes`, без масштаба |
| `PalmCenter` | Центр ладони в той же системе |
| `BodyScale` | Масштаб корпуса источника, делитель k |
| `Pads` | Точки касания подушечек в системе корпуса — объявлено, источники не заполняют |
| `SourceContactFrame` | Захват геометрии источника, только по явному запросу (см. выше) |

**Центр ладони** — определение SDK `UxrAvatarHand.GetPalmCenter`: среднее запястья и проксимальных фаланг
указательного и мизинца, `(W + I + L) / 3`. У кисти с пястными он зависит от позы, поэтому на риге цели
считается после применения позы (`GripTargetRig.PalmCenter` → `HandsPackPoseExtractor.PalmCenterWorld`).
Опора по центру ладони введена потому, что точка контакта с предметом лежит у ладони, а не у запястья:
расстояние запястье → центр ладони у Cyborg, MEF и рук паков разное, и перенос «от запястья» уводил
рукоять на сантиметр и больше вдоль пальцев.

**Вторая рука — зеркало.** `GripSample.DescriptorFor` отдаёт `Hand.Mirrored()`, `GripSample.ForSide`
отражает корпус по оси X кисти и **оси X корпуса**: `t.x → −t.x`, `c.x → −c.x`,
`R = (x, y, z, w) → (x, −y, −z, w)`. Правило взято из прежнего `HandsPackGripAligner`; на MKR9 оно даёт
отражение через горизонтальную плоскость оружия (G1).

## Посадка

`GripPlacement` считает align — граббер руки в системе корня предмета: SDK при snap совмещает align
с граббером, поэтому align есть «где должен оказаться граббер, чтобы корпус стоял относительно кисти
цели как в кадре». Обозначения: `t`, `R`, `c` — корпус, его поворот и центр ладони источника в осях
кисти (для руки цели, с учётом зеркала); `Q` — универсальные оси кисти цели в мире; `W`, `C` — запястье
и центр ладони цели; `n` — нормаль из ладони SDK `GetPalmOutDirection`; `s` — сдвиг по нормали, м.

```text
k = масштаб корпуса префаба (body.lossyScale.x) / BodyScale источника

опора wrist:       корпус = W + Q·(k·t)            (формула HandsPackGripAligner.Place)
опора palm:        корпус = C + Q·(k·(t − c))
поворот корпуса:   Q·R
сдвиг по нормали:  корпус −= s·n                   (s > 0 — ладонь ближе к предмету)
```

Затем корень предмета выводится из корпуса, а align — из граббера цели в системе этого корня.
`k` умножает только смещение предмета от опоры: у HandsPack k ≈ 0,71 (BrowningHiPower 0,7065, Uzi 0,7126,
PPK 0,7116, Revolver 0,7058), у KINEMATION k = 1. Углы пальцев, длины фаланг и размер ладони k не трогает.

`normalOffsetMm` ограничен `maxNormalOffsetMm` (по умолчанию 15 мм); превышение обрезается и помечается
`normalOffsetClamped`. Сдвига вдоль рукояти и поворота кисти в конвейере нет.

Проверка посадки (`GripPlacement.Evaluate`) — обратный ход snap: корень по align и грабберу, центр ладони
источника жёстко с корпусом против центра ладони цели со сдвигом. Это самосогласованность расчёта
(сравнение кадра с самим собой), а не контакт с предметом: R2 Measure дал 0,0009 мм, а контакт
пользователь не принял.

## Кодирование позы

| `encoding` | Профиль | Что пишется |
|---|---|---|
| `source` (`AsSource`) | MEF | Дескриптор источника как есть, вторая рука — `Mirrored()`; curl запрещён |
| `rig` (`TargetRigWorld`) | MEF_cyborg_hands, CyborgHands | Риг в bind-позе → `UxrAvatarRig.UpdateHandUsingDescriptor` (мировой путь редактора) → curl → `new UxrHandDescriptor(avatar, side)`: поза получает пястные цели |

Кодирование `rig` появилось после дефекта G6: поза без пястных на кисти с пястными в рантайме SDK
выкручивала пальцы на 179° против редактора. На риге с пястными и `encoding=source` `CheckRig` пишет
предупреждение. Пястные цели берутся из bind-позы рига (мировой путь ставит проксимальные фаланги от
запястья), а не из клипа.

Curl (`curlDegByHand`) — один угол на палец `thumb…little` в пределах ±120°, правило
`FitFingerChain.ApplyCurl` (то же, что подсказка анализатора и предпросмотр правки): у пальцев 1:1:1,
у большого 0,1:0,3:1 по фалангам. Проверки позы в отчёте руки: `runtimeVsWorldDeg` (рантайм-путь
`UxrRuntimeHandDescriptor` против мирового — ноль значит игра покажет ту же позу), `shapeDeg` (форма
против источника), `poseVsExistingDeg` (против ассета, который Apply перезапишет), `metacarpalInfo`.

## Профили цели

| Поле | `MEF` | `MEF_cyborg_hands` | `CyborgHands` |
|---|---|---|---|
| Риг (`RigPrefabPath`) | `Assets/Prefabs/Player/MEF_Base_Avatar.prefab` (из `HandsPackPoseImporter.GripAvatars`) | `…/Avatars/CyborgHands/MEF/MEF_cyborg_hands_Player.prefab` | тот же |
| Запись по GUID (`RecordAvatarPath`) | тот же MEF | CyborgHandsBase, по GUID `71b74b0d…` | тот же |
| База поз `_handPoses` | `Assets/Prefabs/Player/Avatars/Bases/PlayerBase_NonSdkHands.prefab` | CyborgHandsBase | CyborgHandsBase |
| Папка поз | папка рецепта: `Assets/Art/HandPoses/HandsPack`, `…/Kinemation` | `Assets/Prefabs/Player/HandPoses/CyborgHands` | тот же |
| Имя align (`AlignAvatarName`) | `MEF_Base_Avatar` | `MEF_cyborg_hands` | `MEF_cyborg_hands` |
| Опора / кодирование | wrist / source | palm / rig | palm / rig |
| `RegisterPoses` / `RemoveReplacedAligns` | true / true | **false** / false | **true** / false |
| Обязательны пястные | нет | да | да |

`MEF_cyborg_hands` и `CyborgHands` отличаются только регистрацией поз: R2 шёл с `MEF_cyborg_hands`
и `registerPoses=false` (позы уже были в базе). Путь базы разрешается по GUID (`ResolveCyborgBasePath`),
запасные пути — канонический и прежний `Experimental/`. Запись по GUID базы действует для всех трёх
аватаров семейства через GUID-цепочку, но посадка считается только на риге `MEF_cyborg_hands_Player`.
Отдельного профиля для `PlayerControllersCyborgAvatar` нет; совпадение «граббер → ладонь» у него и у
`MEF_cyborg_hands_Player` не проверялось.

`CheckRig` останавливает прогон, если GUID записи не входит в цепочку префабов рига или у рига нет пястных
при `RequireMetacarpals`. `ResolveRecordGuid` сверяет GUID с `ExpectedRecordGuid`.

### Как добавить профиль

1. Проверить кисть нового аватара [анализом рига](hand-rig-quality-tool.md) (раздел «Перед первым
   использованием на новом аватаре»): неверные фаланги перенос не исправит.
2. Добавить фабрику в `GripTargetProfile` и ветку в `Get`; обновить текст ошибки `Get` и комментарий
   поля `Target` в `GripExtractionRequest` (там до сих пор перечислены только `MEF | MEF_cyborg_hands`).
3. Заполнить поля: риг с `UxrAvatar`, запястьями и `UxrGrabber` обеих рук; `RecordAvatarPath` — префаб,
   чей GUID будет ключом записи, и он должен быть в GUID-цепочке рига; `ExpectedRecordGuid`; база поз
   с `UxrAvatar` на корне; папка поз; **своё** `AlignAvatarName`, иначе align перезапишет чужие;
   `Encoding = TargetRigWorld` и `RequireMetacarpals = true` для кисти с пястными.
4. Решить `RegisterPoses` и `RemoveReplacedAligns`. Удаление прежних align безопасно только если на них
   никто не ссылается; варианты префабов оружия проверить на переопределения.
5. Первый прогон — Measure на одной-двух точках; Apply — только после согласования с владельцами
   префабов оружия и базы поз.

## Режимы и что пишет Apply

| Режим | Позы | Префабы оружия | База поз | Fit |
|---|---|---|---|---|
| `measure` | Временные, только для замеров | Только чтение | Не трогается | Нет |
| `preview` | Временные (`HideAndDontSave`) | Правка копии `LoadPrefabContents`, выгрузка без сохранения; в `written` префикс «preview (не сохранено)» | Проверка регистрации — предупреждения | Нет |
| `apply` | Запись | Запись | По `RegisterPoses` | При `analyze=true` |

Apply не начинается, если хотя бы одна точка дала ошибку расчёта. Порядок записи:

1. **Позы** — `<PoseFolder>/<поза>.asset`, тип Fixed, обе руки. Существующий ассет перезаписывается на месте
   (`CopySerialized`): GUID и ссылки из базы поз и записей хвата сохраняются. Сохраняются **только позы
   запроса** (`AssetDatabase.SaveAssetIfDirty`) — общий `SaveAssets` убран после отказа 698, чтобы не записать
   чужие dirty-ассеты worker (scoped-save, согласовано пользователем 2026-10-15).
2. **База поз** при `RegisterPoses`: недостающие позы дописываются в `_handPoses`
   (`HandsPackPoseImporter.RegisterOnPoseBase`, `SavePrefabAsset`) под `GripPrefabIdentityGuard`. Без регистрации
   выдаётся предупреждение, если поза не зарегистрирована или под её именем лежит другой ассет: SDK ищет
   позу по имени.
3. **Оружие**, по одному префабу (`GripRecordWriter.WriteWeapon`):
   - align `MeshContainer/Grabs/HandsPack/<поза>/<AlignAvatarName>/Left|Right` (или от корня, если
     `MeshContainer/Grabs` нет) с `UxrGrabbableObjectSnapTransform`; у KINEMATION папка источника тоже
     `HandsPack`. Для `CyborgHands`: `…/HandsPack/<поза>/MEF_cyborg_hands/Left|Right`;
   - `BindToPrefab` проставляет новым align GUID префаба в `__prefabGuid`/`__isInPrefab` (в пилоте R1 align
     из `LoadPrefabContents` сохранялся без них);
   - запись `UxrGripPoseInfo` по GUID профиля (`CheckAddGripPoseInfo`): поза, `PoseBlendValue = 0`, align L/R;
   - прежние align — `RemoveUnused` при `RemoveReplacedAligns`, иначе остаются с предупреждением;
   - `PrefabUtility.SaveAsPrefabAsset`, затем `NetworkAssetIdNormalizer` и проверка на диске: `_assetId`
     (прямой или в переопределении варианта) должен равняться `NetworkIdentity.AssetGuidToUint` от GUID
     префаба — `SaveAsPrefabAsset` из изолированной сцены пишет 0, а загруженный ассет этого не показывает;
   - `GripPrefabIdentityGuard.Verify`: изменение `_uxrUniqueId`, `__prefabGuid`, `__isInPrefab`, `_assetId`,
     `sceneId` любого компонента — исключение; удалённые align ошибкой не считаются.
4. **Fit** при `analyze=true` — `GripFitCheck` на каждую руку, отчёты в `<outDir>/fit/<поза>-<сторона>/`.

Транзакции нет: при исключении на третьем префабе позы и первые два префаба уже сохранены. Внешний
драйвер должен снимать хеши до прогона и сверять после (как пакеты R2).

## Запуск

Агенты запускают Unity только через штатный worker брокера, в своей RUNNING-аренде
([unity-mcp.md](unity-mcp.md)). Конвейер вызывается из `execute_code` и выполняется синхронно. В R2 ответ
MCP терялся по таймауту, пока C# продолжал работу (сам Measure — около 1,6 с, хеширование входов драйвером —
около 270 с, Apply с Fit — дольше). Таймаут ответа не означает остановку: исход устанавливается по отчёту
в `outDir` и прогрессу драйвера, а не повтором.

### Поля запроса (`GripExtractionRequest`)

JSON в camelCase, регистр имён не важен.

| Поле | По умолчанию | Смысл |
|---|---|---|
| `source` | `pack` | Источник; других нет |
| `recipes` | `["all"]` | Селекторы точек |
| `target` | `MEF` | Профиль: `MEF`, `MEF_cyborg_hands`, `CyborgHands` |
| `anchor` | из профиля | `wrist` или `palm` (`palmcenter`) |
| `encoding` | из профиля | `source` (`assource`) или `rig` (`targetrigworld`) |
| `mode` | `measure` | `measure`, `preview`, `apply` |
| `outDir` | `Temp/GripExtraction/<UTC>-<mode>` | Каталог отчёта; внутри `Assets` запрещён; относительный путь — от корня проекта, где работает Editor (у worker — его checkout) |
| `normalOffsetMm` | 0 | Сдвиг по нормали ладони для всех рук, мм (+ — ладонь ближе) |
| `normalOffsetMmByHand` | — | Сдвиг по рукам: ключ `<поза>/Left|Right` |
| `curlDegByHand` | — | Curl по рукам: `<поза>/Left|Right` → `{thumb…little: градусы}`; только `encoding=rig` |
| `maxNormalOffsetMm` | 15 | Предел сдвига |
| `registerPoses`, `removeReplacedAligns` | из профиля | Переопределения профиля для Apply |
| `analyze` | false | После Apply прогнать Fit по каждой руке |
| `toleranceMm`, `toleranceDeg` | 0,5 / 0,5 | Пороги R0 |
| `returnFull` | false | `RunJson` вернёт полный отчёт вместо сводки (полный всегда в `outDir`) |

Пример Measure для текущей базы (не запускался в этой форме: см. «Перед следующим применением»):

```json
{
  "source": "pack",
  "recipes": ["HandsPack_Scar_Grip", "Kinemation_AK105"],
  "target": "CyborgHands",
  "mode": "measure",
  "outDir": "tasks/<task-id>/reports/grip-extraction/measure-01",
  "normalOffsetMm": 0,
  "analyze": false
}
```

```csharp
string summary = VrBattlegrounds.Editor.Avatars.GripExtraction.RunJson(System.IO.File.ReadAllText(requestPath));
// summary: status, r0, points, errors, warnings, written, hands[] (palmMm, existingMm/Deg, runtimeVsWorldDeg,
// poseVsExistingDeg, fit, nextOffsetMm); полный отчёт — <outDir>/grip-extraction-measure.json
```

Для Apply добавить `"mode": "apply"`, `"analyze": true`, при доводке — `normalOffsetMmByHand`
и `curlDegByHand` из `fit.suggestedTotalNormalOffsetMm` и `fit.suggestedCurlDeg` прошлого прогона.
Образцы реальных запросов R2 и драйверов с охраной хешей, чужих dirty-ассетов и идентификаторов — в
[tasks/hand-rig-quality/tools/](../tasks/hand-rig-quality/tools/): `r2-measure-request.json`,
`r2-apply-request.json`, `r2-correction-request.json`, `run-r2-measure.cs.txt`, `run-r2-apply.cs.txt`,
`run-r2-correction.cs.txt`; сохранение — `validate-grip-save.cs.txt`. Пути в них указывают на прежние
worktree и профиль `MEF_cyborg_hands` в `Experimental/`. Драйвер R2 перед прогоном KINEMATION вызывал
`KinemationWeapon.RequireReadable`, чтобы остановиться до построения модели пака, которое иначе может
выполнить `SaveAndReimport` источника.

### Отчёт

`grip-extraction-<mode>.json` (схема 1): запрос, профиль (риг, GUID записи, база поз), R0, точки, предупреждения,
записанное. По точке: `k`, `posePath`, `existingPose`; по руке: `alignLocalPosition/Rotation/Euler`,
`palm.errorMm` и `palm.errorHandMm` (`[вправо, вверх (тыл), вперёд (пальцы)]` в осях кисти цели),
`vsLegacyWrist`, `existingNamed`, `existingRecord`, `vsHandsPackAligner`, `runtimeVsWorldDeg`, `shapeDeg`,
`poseVsExistingDeg`, `metacarpalInfo`, `curlDeg`, `fit`.

**R0** применим только к `target=MEF, anchor=wrist, encoding=source` без сдвига и curl: новый расчёт должен
совпасть с align и позами на префабах и с прямым вызовом `HandsPackGripAligner.Place` в пределах
`toleranceMm`/`toleranceDeg`. Для остальных профилей `r0.applicable = false`.

## Проверка результата

1. `status` (`ok`, `partial`, `error`), `errors` и `warnings` сводки; для Apply — `written` и отсутствие
   исключения сторожа идентификаторов.
2. Числа конвейера (пороги плана 2026-10-13): `palm.errorMm` ≤ 0,5 мм, `runtimeVsWorldDeg` ≤ 0,5°,
   четыре пястных в `metacarpalInfo` у профилей Cyborg, сдвиг в пределах 15 мм. Они доказывают
   согласованность расчёта, а не хват.
3. Fit (`analyze=true`): `fit.overall`, `fit.palmSeverity`, лист `fit.reviewSheet` — **открыть лист**;
   подсказки `suggestedTotalNormalOffsetMm` (если `normalOffsetRecommended`) и `suggestedCurlDeg`.
   Как читать отчёт анализатора и его ограничения — в [автоанализе](grip-quality-analysis.md).
4. Diff префабов: только записи хвата и align своих точек; идентификаторы и `_assetId` прежние.
5. Шлем — единственная приёмка. Уровни доказательства (конфигурация, статический Fit, runtime-проба, шлем) —
   раздел «Уровни доказательства» [автоанализа](grip-quality-analysis.md); называть свой уровень явно.

Автоматического цикла доводки нет: `GripFitCheck` только возвращает подсказки, следующую итерацию
запускает человек или агент новым запросом. План допускал не более двух итераций; урок R2 — улучшение
полосы SDK одновременно увеличивает глубину проникновения пальцев (G4) и не даёт плотного обхвата (G5).

## Ограничения

- **Переносятся углы, а не контакт.** Длины фаланг, ширина ладони и пропорции кисти источника не
  адаптируются; при одинаковых углах подушечки цели оказываются в другом месте. Пястные — из bind-позы
  рига цели. k масштабирует только смещение предмета.
- **Доводка — только сдвиг по нормали и один curl на палец.** Нет развода пальцев, отдельных суставов,
  сдвига вдоль рукояти, поворота кисти.
- **Нет контактной доводки и метрики точки фаланги.** Ни «Обхватить», ни `ContactIkPreviewSession`
  направления 4 в конвейер не встроены, `Pads` не заполняются; `GripFitCheck` возвращает только вердикт
  по полосам SDK.
- **Зеркало второй руки** по оси X корпуса — G1 (MKR9); обе руки клипа не используются, хотя захват
  геометрии источника их уже снимает.
- **Только точки рецептов.** Магазины, патроны, затворы, планшет и жетон не поддержаны (нужен
  `avatar-record` или ручная доводка).
- **Один кадр, статика.** Кадр 0 с, Fixed-поза, `PoseBlendValue = 0`; Blend, анимация курка, динамика двух рук
  и `AlignToController` не переносятся.
- **Посадка на одном риге.** Для трёх аватаров CyborgHandsBase посадка считается на `MEF_cyborg_hands_Player`.
- **Тестов нет.** EditMode-тестов Grip Extraction нет; в прогонах использовались существующие
  `NetworkAssetIdOnDisk`, `UxrUniqueIdOnDisk`, `UxrUniqueIdStability`, `AvatarHandPoseChain`, `GrabPoseCoverageTests`.

## История применения

| Шаг | Дата | Объём | Итог |
|---|---|---|---|
| Ядро, R0 | 2026-10-13 | паки → MEF, wrist, 29 точек / 58 рук | PASS: align против текущих ≤ 0,0004 мм / 0°, позы 0°, `HandsPackGripAligner.Place` 0 |
| Замер пилота | 2026-10-13 | паки → `MEF_cyborg_hands`, palm, 12 точек / 24 руки | Прежние записи (копии align MEF) ставят центр ладони в 9–20 мм от клипа и поворачивают оси ладони на 9,6°; новая посадка 0 мм / 0° |
| Пилот R1 | 2026-10-13…14 | Scar, MP5K, AK105, MKR9, SRM12, Viper | Проход 1 (s = 0): gross 11 из 24; проход 2 (подсказки, кроме MKR9 p0 L): gross 6, notable 1, ok 17. Шлем: «стало гораздо лучше», пальцы местами входят в меш, MKR9 левая мимо |
| save-validation | 2026-10-14…15 | Viper, 2 точки / 4 руки | `_assetId` канонический, байты трёх ассетов прежние; EditMode 683: 56/57 (FAIL — своя голова Cyborg, не хваты). Пользователь принял вливание 2026-10-15 |
| R2 Measure 695 | 2026-10-15 | 17 точек / 34 руки / 9 стволов | PASS: записано 0, 2400 хешей прежние, ладонь ≤ 0,0009 мм, рантайм 0° |
| Отказ 698 | 2026-10-15 | — | Остановлен до записи: общий `SaveAssets` записал бы чужие dirty-ассеты → scoped-save (701: компиляция и 17/17 существующих тестов) |
| R2 Apply 704 | 2026-10-15 | 17 / 34, s = 0, без curl | Изменены 9 префабов, позы прежние; Fit: gross 19, notable 7, ok 8 |
| R2 коррекция 712 | 2026-10-15 | 24 curl на 14 руках в 7 позах | Fit: gross 10, notable 4, ok 20; при этом полная глубина пальца выросла у 16 из 24, глубина подушечки — у 21, площадь проникновения — у 22 |
| Шлем 715 | 2026-10-15 | ассеты = 712 | **Отклонено**: «неплотный обхват всей поверхностью кожи кисти и пальцев» |

Коммиты на dev: ядро `67e3b06f5`, пилот R1 `6190ce80d`, нормализация `_assetId` `e83a2d4a5` и `e16b86b82`,
приёмка сохранения `846fa4bca`. Scoped-save, профиль `CyborgHands` и захват геометрии источника пришли
в dev одним коммитом `1b2f88612` (2026-10-16).

R2 — BrowningHiPower p0/p1, Uzi p0, PPK, Revolver, SniperRifle, R08, Herrington, Mk14, TR15 (по p0/p1).
Ориентиры пользователя в ревью 715 — Viper, TR15 с глушителем, SRS Desert Tech (SRM12), AK107 (соответствие
AK105 не подтверждено); у них тоже есть небольшие проникновения. Ориентиры Viper, SRM12 и AK105 — результат
пилота R1 с k = 1; причина разницы не установлена.

**Где версии R2.** Результат 712 (технически проверенный, отклонённый) — ветка
`archive/avatar-grip/r2-rejected-result-712-verified` (`1a71e6af6`) в общем Git-хранилище основного checkout;
на origin архивные ветки не опубликованы (ls-remote 2026-10-20). Рядом архивы входов и результатов 695, 701, 715.

**Что в dev.** R2 пришёл в dev коммитом `1b2f88612` (2026-10-16) вне учёта хаба: записи хвата 9 стволов R2.
Сверка ассетов поз результата 712 с `origin/dev` (2026-10-20, побайтово, с учётом переноса папки
`Experimental/MEF_cyborg_hands/HandPoses/` → `HandPoses/CyborgHands/`): из 30 ассетов поз совпадают 26;
отличаются `HandsPack_Gun_Grip` и `Kinemation_Herrington_Grip` (обе в объёме R2 и в коррекции 712),
`Kinemation_Viper_Grip` (пилот R1) и `Gun`. Версии этих четырёх на dev пришли в `1b2f88612` и не совпадают
ни с результатом 712, ни с его базой. Остальные пять поз коррекции 712 (Uzi, PPK, Revolver Grip, SniperRifle
и TR15 Support) на dev — версии 712. **Решение о судьбе поз R2 в dev за пользователем.**

Отчёты — локально, вне Git, в основном checkout: R0 — `tasks/hand-rig-quality/reports/grip-extraction/r0-01/`,
пилот — `tasks/hand-rig-quality/reports/grip-extraction/` (`r1-measure-01`, `r1-apply-01`, `r1-apply-02`,
`r1-apply-03`; листы `fit/*/review-sheet.png`), проект конвейера — `tasks/hand-rig-quality/reports/cyborg-fit/pipeline-design.md`,
сохранение — `tasks/hand-rig-quality/reports/resume-2026-10-14/save-validation/`, R2 —
`tasks/hand-rig-quality/reports/extraction-run/` (Measure, Apply, Correction), сравнение —
`tasks/hand-rig-quality/reports/r2-fit-comparison-712/Assessment.md`, ревью пользователя —
`tasks/hand-rig-quality/reports/r2-human-review-715/Review.md`, ориентиры —
`tasks/hand-rig-quality/reports/reference-grips-715/Review.md`.
Хроника решений — [Details задачи hand-rig-quality](../tasks/hand-rig-quality/Details.md), разделы
«Grip Extraction Tools и план извлечения хватов», «R2 Measure», «R2: первый Apply и пакет коррекции»,
«Пользователь отклонил production-приёмку R2»; changelog `2026-10-14-save-validation`, `2026-10-15-scoped-save`,
`2026-10-15-r2-*`.

## План R3–R5 и остановка

| Шаг | Источник → цель | Объём |
|---|---|---|
| R3 | записи Cyborg → кисть Cyborg игры | SDK-оружие, магазины, патроны, планшет, жетон |
| R4 | паки → MEF, опора palm | 29 точек, сравнение с текущим MEF (вход `mef-correction`) |
| R5 | записи/паки → Cyborg, BigHands | покрытие `GrabPoseCoverageTests` |

**Почему остановлено.** После отказа 715 пользователь не принял R2 как production; «дальнейшие R2 Apply
и R3–R5 не запускаются до пересмотра подхода» (Readme задачи). Этап хаба `grip-extraction-run` по Readme
задачи остаётся planned и зависит от merged R2; R2 в хабе verified только в техническом объёме, а в dev он
попал вне хаба. С тех пор изменились и входы: MEF native перенесён в `Unused/` (2026-10-17), коррекция MEF
закрыта решением пользователя 2026-10-20 (G8 реестра), поэтому R4 и часть R5 могли потерять цель —
решение за пользователем. Записи CyborgHandsBase для SDK-оружия и планшета уже скопированы в `1b2f88612`
без расчёта посадки (F1 реестра).

**Что нужно до возобновления:**

1. **Адаптер `avatar-record`** (R3): разрешение записи источника по точному GUID (`GetGripPoseInfo(avatar, true)`),
   поза и align **обеих** рук источника независимо (зеркало потеряет различия authored-сторон), provenance
   записи, фиксированный кадр Blend, отказ на default, dynamic snap и неравномерный масштаб. Источником был
   `PlayerControllersCyborgAvatar` (GUID `8e0e06323e4754f479a9f19788554652`); теперь он лежит в
   `Avatars/CyborgHands/Cyborg/` и сам стал вариантом CyborgHandsBase, поэтому какая запись служит источником,
   нужно определить заново. Разбор — локально, вне Git: `tasks/hand-rig-quality/reports/resume-2026-10-14/r3-source-prerequisites.md`
   в основном checkout.
2. **Рамка отражения** (G1): явная физическая рамка оружия (+X вправо, +Y вверх, +Z вперёд) вместо осей
   корпуса; прежнее отражение по X корпуса оставить отдельным режимом для R0. Числовая проба 647 на MKR9
   показала сдвиг 208 мм / 180° (основная Left) и 97 мм / 180° (support Right); контакт не проверялся.
   Разбор — локально, вне Git: `tasks/hand-rig-quality/reports/resume-2026-10-14/mkr9-diagnosis.md`.
   Лучше — отказаться от зеркала и брать обе руки клипа (см. ниже), где они на оружии.
3. **Приёмка и доводка по точке фаланги** вместо автоматического curl по полосам SDK: критерий приёмки
   переноса — метрика точки фаланги (N/15 на кисть и по пальцам); кандидат на доводку после переноса —
   алгоритм «Обхватить». Для этого метрику и «Обхватить» нужно вынести из private-методов панели редактора
   поз в общий API ([grip-pose-editor.md](grip-pose-editor.md), «Перед следующим применением»). Не реализовано;
   см. «Предлагаемое направление».

## Предлагаемое направление (не реализовано)

> Это предложение для обсуждения, а не принятое решение и не план работ. Направление обсуждалось
> с пользователем 2026-10-20 при разборе уборки; ниже — как его можно собрать из существующего кода.
> Принятое решение здесь одно: метрика точки фаланги — основная метрика качества контакта (2026-10-20),
> поэтому она же — критерий приёмки переноса.

**Переносить контакты, а не углы.** Из кадра клипа брать не только углы фаланг, но и места, где кожа
источника касается предмета, и ставить кисть цели так, чтобы её соответствующие участки оказались там же.

1. **Цели в системе предмета.** `PackClipGripSource.Sample(point, true)` уже снимает `GripSourceContactFrame`:
   обе прямые руки клипа, кожу с весами, меши предмета в системе корпуса. Из него получить для каждого
   пальца точку подушечки конечной фаланги и точку ладони источника и их материальные цели на предмете
   (renderer, submesh, треугольник, barycentric). Соответствие участков кожи источника и Cyborg задаётся
   явно (анатомические маски), а не «ближайшей поверхностью»; `ContactIkSourceTargetMapping.FromSource`
   уже требует явной роли, меша и якоря. Итог мог бы лечь в `GripSample.Pads` или более полную структуру.
2. **IK кисти Cyborg к целям.** Посадка по центру ладони, как сейчас, затем `ContactIkPreviewSession`
   на `GripTargetRig` с фактическим BakeMesh: `Solve` или `SolveForwardWrap` → `ApplyFinal` →
   `CaptureDescriptor`. Сессия уже поддерживает перенос ладони (`palmPlacementRoot`,
   `maxPalmTranslationMeters`); если ладонь сдвинулась, align пересчитывается по новому положению граббера.
   В редакторе поз этот живой режим заменён из-за производительности (решение 2026-10-16); в офлайн-конвейере
   его стоимость допустима, но более простой вариант — сразу доводка «Обхватить» (шаг 5).
3. **Запись прежним путём.** Дескриптор → `GripPoseAssets.Write`, align и запись → `GripRecordWriter`
   с нормализацией `_assetId` и сторожем идентификаторов. Второго писателя и runtime-писателя нет.
4. **Обе руки независимо.** Захват источника хранит обе руки с ролями, поэтому зеркало (и G1) не нужно там,
   где обе руки клипа на оружии; Uzi p1 и подобные случаи остаются без источника.
5. **Доводка.** Кандидат — алгоритм «Обхватить» (догиб фаланг до касания точки фаланги) на перенесённой
   позе и align; сейчас он private в панели редактора поз, конвейер его вызвать не может — нужен вынос в API
   (рефакторинг, а не вопрос качества алгоритма).
6. **Проверка.** Критерий приёмки переноса — метрика точки фаланги до/после (N/15 на кисть и по пальцам);
   Fit по полосам SDK — грубый фильтр и листы ревью; затем шлем.

Связь с [контрактом `distal-palm-contact-ik-v2`](../tasks/hand-rig-quality/contracts/distal-palm-contact-ik-v2.md):
те же цели — фиксированная кожная точка конечной фаланги к явной точке предмета, своя точка ладони, промежуточные
точки в полосе ±2 мм, `DistalPriority` от 0 до 1 (по умолчанию 0), полная кожа — диагностика, а не цель.
Отличие предложения — цели берутся из клипа, а не ставятся вручную. Границы контракта действуют и здесь:
только редакторские копии, без runtime-писателя, без автоматического Apply; новые тесты — после проверки
качества пользователем.

Открытые вопросы: соответствие подушечек при разных пропорциях кистей (k ≈ 0,71 у HandsPack),
недостижимые цели и проникновение кожи источника в предмет, кто владеет общим кодом (контактный IK
относится к направлению 4), стоимость BakeMesh на 29 точек × 2 руки — либо отказ от BakeMesh в пользу
точки фаланги на кости и радиусе, как в панели. «Обхватить» (`AutoWrapAndCommit` в `HandPoseSdkContactEditing`)
сейчас private и читает статическое состояние окна редактора поз; чтобы конвейер мог его вызывать, алгоритм
и метрику нужно вынести в общий API — это вопрос рефакторинга, не качества.

## Состояние на 2026-10-20

- Код на dev: ядро, профили `MEF`, `MEF_cyborg_hands`, `CyborgHands`, scoped-save, нормализация `_assetId`,
  захват геометрии источника. 2026-10-19 (`a62039a26`) константы путей профилей переведены на
  `Avatars/CyborgHands/`. Источник реализован один — `pack`.
- Последний прогон — R2 712 (2026-10-15) на `MEF_cyborg_hands` в `Experimental/`. После переезда в
  CyborgHandsBase (2026-10-16), иерархии 2026-10-18 и каталогов 2026-10-19 конвейер не запускался;
  компиляция и совместимость с новыми путями в Unity не проверены.
- Профиль `MEF` и R0 сломаны: риг `Assets/Prefabs/Player/MEF_Base_Avatar.prefab` перенесён в `Unused/`.
- R2 в dev (вне хаба), производственно не принят; три позы R2 на dev в других версиях (см. «Что в dev»).
- G1 (MKR9) открыт, данные хвата MKR9 не менялись с 2026-10-14. G4, G5 открыты.
- Метрики точки фаланги и «Обхватить» в конвейере нет: обе живут в private-методах панели редактора поз;
  `GripFitCheck` судит только по полосам SDK.
- Тестов Grip Extraction нет.

## Перед следующим применением

1. **Пути профилей** (сверка с деревом 2026-10-20, ветка от `origin/dev`):
   - существуют: `Avatars/CyborgHands/Base/CyborgHandsBase.prefab` (GUID `71b74b0d…` совпадает),
     `Avatars/CyborgHands/MEF/MEF_cyborg_hands_Player.prefab` (`2aa0ab67…`), `…_Player_Black.prefab`,
     `Avatars/CyborgHands/Cyborg/PlayerControllersCyborgAvatar.prefab` (`8e0e0632…`),
     `Assets/Models/Avatars/MEF_CyborgHands/MEF_CyborgHands.fbx`, `HandPoses/CyborgHands/` (30 поз),
     `Avatars/Bases/PlayerBase_NonSdkHands.prefab`, `Assets/Art/HandPoses/HandsPack` и `…/Kinemation`.
     `MEF_cyborg_hands_Player` и `PlayerControllersCyborgAvatar` по YAML — варианты CyborgHandsBase; цепочку
     `CheckRig` проверит в Unity;
   - нет: `Experimental/MEF_cyborg_hands/MEF_cyborg_hands_Base.prefab` (запасной путь, не используется, пока
     GUID базы разрешается) и **`Assets/Prefabs/Player/MEF_Base_Avatar.prefab`** — он теперь в
     `Assets/Prefabs/Player/Unused/` (GUID `b6fe59db941fa944696ece5e1aabc032`). Путь зашит в
     `HandsPackPoseImporter.GripAvatars`, поэтому профиль `MEF` упадёт на `GripTargetRig` («Нет префаба рига»);
     тот же путь — в самом импортёре паков и ещё нескольких editor-инструментах. Починка или отказ от
     профиля `MEF` — решение владельца avatar-grip.
2. Обновить рабочую ветку на свежий `origin/dev`; через worker убедиться, что сборка компилируется, и
   прогнать Measure с `target=CyborgHands` на одной-двух точках: `palm.errorMm`, `runtimeVsWorldDeg`,
   `existingRecord`, `poseVsExistingDeg`.
3. **Apply перезаписывает позы на месте.** Ненулевой `poseVsExistingDeg` значит, что Apply затрёт текущую
   версию позы на dev — в том числе ручную доводку направления 4 и три позы, изменённые в `1b2f88612`.
   Перед Apply согласовать это с пользователем.
4. Имя align у профилей Cyborg — `MEF_cyborg_hands`; существующие записи ссылаются на эти узлы. Переименование
   align — отдельное решение с переносом ссылок.
5. Драйвер прогона — по образцу пакетов R2: охрана хешей входов, чужих dirty-ассетов, идентификаторов;
   `outDir` — в `tasks/<task-id>/reports/`, не в Temp; отчёты и листы показывать человеку.
6. Новые записи в префабах оружия — только с ACK владельцев (weapon-system для стволов, avatar-ik для поз
   и базы) и с явным уровнем доказательства в отчёте.

## Исходные позы паков `_orig`

С 2026-10-22 сборщики оружия из паков (`HandsPackWeaponBuilder`, `KinemationWeaponBuilder`,
`HandsPackLegacyRebuild`) не создают и не регистрируют позы и хваты аватаров. Записи хвата всех аватаров и их
трансформы выравнивания переносятся из текущего префаба (`WeaponGripCarryOver`, режим только `KeepCurrent`).
Калибровочного хвата MEF больше нет: точка отдачи, `MainGripAimLock`/`SupportGripRequiresMain` (порог 0,10 м
между ладонями) и зоны подсветки хвата считаются от центра ладони руки пака (`HandsPackWeaponBuilder.PalmPoints`).
У нового оружия до отдельной регистрации хватов нет записей, SDK берёт пустой хват по умолчанию.

Последним шагом сборки и отдельной командой `Tools/VR Battlegrounds/Avatars/Экспортировать исходные позы паков
(_orig)` `PackOrigPoses` готовит сырьё для будущей регистрации:

- позы `<Пак>_<Предмет>_<Точка>_orig` (`HandsPack` — 13, `Kinemation` — 16) в `Assets/Art/HandPoses/Orig/<Пак>/`,
  снятые теми же экстракторами с руки пака; ни у одного аватара не регистрируются, перезапись на месте с тем же
  GUID только при изменении дескриптора;
- пустые snap-точки `<Grabs>/Orig/<имя>_orig/Left|Right` в префабах оружия и магазина — рамка ладони руки пака
  (`GripSample.PalmInBody`), без компонентов UXR; запись добавлением объектов в префаб со сверкой YAML;
- позы магазина/патрона (`_Magazine_orig`, `_Shell_orig`) — эксперимент: кадр клипа перезарядки, где деталь
  вынута и неподвижна в ладони; выбор кадра проверять глазами;
- отчёт источников `Orig/<Пак>/<Пак>_<Предмет>_orig_sources.json`.

Регистрация `_orig` у аватара — отдельная задача: поза в `_handPoses` базы, align = snap `_orig` · (рамка ладони →
граббер аватара) (`GripPlacement` с опорой `PalmCenter`), запись — `GripRecordWriter`; доводка — редактор поз.
Перенос при пересборке не копирует прочие поля точки хвата (`_snapMode`, зоны близости, `_defaultGripPoseInfo`
и т.п.) — они от сборщика.

## Связанные документы

- [grip-quality-analysis.md](grip-quality-analysis.md) — автоанализ хвата (направление 1): Fit, правила,
  подсказки, уровни доказательства.
- [grip-known-issues.md](grip-known-issues.md) — реестр дефектов: G1 (зеркало MKR9), G4, G5 (R2), G6 (позы без
  пястных), F1 (записи без посадки).
- [hand-rig-quality-tool.md](hand-rig-quality-tool.md) — анализ рига кисти перед новым профилем.
- [unity-mcp.md](unity-mcp.md) — запуск Unity через worker.
- [UltimateXR/known-issues.md](UltimateXR/known-issues.md) — Issue 26 (нет записи хвата, тихий default),
  Issue 28 (предпросмотр перенесённой позы).
- [Details задачи hand-rig-quality](../tasks/hand-rig-quality/Details.md),
  [контракт distal-palm-contact-ik-v2](../tasks/hand-rig-quality/contracts/distal-palm-contact-ik-v2.md).
