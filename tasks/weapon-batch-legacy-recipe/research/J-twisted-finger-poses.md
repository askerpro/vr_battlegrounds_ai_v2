# J. Выкрученные пальцы поз из паков на кисти CyborgHands

Дата: 2026-10-24. Исследование read-only (без Unity и брокера): код конвейера, YAML поз, история Git.
Скрипты анализа — во временной папке сессии, в Git не входят.

## Вывод

Главная причина — повтор известного дефекта **G6** (`Docs/grip-known-issues.md:256-267`). Перестановки осей
при извлечении нет.

Легаси-рецепт копирует позу `_orig` в `CyborgHands` один к одному. Такая поза записана в осях рига пака и
**без пястных** (`HasMetacarpalInfo = 0`). У кисти Cyborg (пересаженная BigHands) пястные есть у указательного,
среднего, безымянного и мизинца. На такой паре рантайм SDK ставит проксимальную фалангу под пястной, хотя
считал её от запястья. Ошибка равна локальному повороту пястной, около 177–179° вокруг оси кости. Сгибание
превращается в разгибание назад: «выкручено, как будто оси переставлены». Редакторный (мировой) путь ставит
фалангу верно. Поэтому снимки, Fit и выравнивание в редакторе дефекта не видят, а шлем его показывает.

## 1. Цепочка кода

| Шаг | Код | Что происходит |
|---|---|---|
| Извлечение HandsPack | `Assets/Editor/VR_Battlegrounds/Avatars/HandsPackPoseExtractor.cs:62-65,83,96-111` | Риг CAT: Palm → Digit{1..5}{1..3}, три фаланги, **без пястных**; `new UxrHandDescriptor(arm, handAxes, fingerAxes)` в осях рига пака |
| Извлечение Kinemation | `Assets/Editor/VR_Battlegrounds/Avatars/KinemationPoseExtractor.cs:61-64,86,150-162` | UE5: `hand_*` → `*_01..03_*`; `*_metacarpal` намеренно пропущены |
| Оси рига-источника | `HandsPackPoseExtractor.cs:147-165` (`SolveAxes`) | Копия математики SDK `SolveHandAndFingerAxes`: оси ладони и одна система осей пальцев от проксимальной указательного |
| Запись `_orig` | `Assets/Editor/VR_Battlegrounds/Gameplay/PackOrigPoses.cs:607-638` (`WritePose`) | Дескриптор источника как есть, Fixed |
| Копия в CyborgHands | `Assets/Editor/VR_Battlegrounds/Gameplay/AvatarGripRegistration.cs:827-834` (`CreatePose`: `Object.Instantiate(source)`) и `:607` (Rebuild: `EditorUtility.CopySerialized(source, pose)`) | **Перекодирования на риге цели нет** |
| Выравнивание | `AvatarGripRegistration.cs:925` → `GripTargetRig.cs:131` (`UpdateHandUsingDescriptor`) | Мировой путь: поза в редакторе верная, выравнивание тоже |
| Рантайм SDK | `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Avatar/Rig/UxrRuntimeFingerDescriptor.cs:71,83-87,99-103` + `UxrAvatarRig.HandRuntimeTransformation.cs:127-132` | Без пястной: `ProximalRotation = Inverse(wrist.rotation) * proximalWorld`, присваивается `Proximal.localRotation` (родитель — пястная) |
| Мировой путь SDK | `UxrAvatarRig.HandTransformation.cs:87-100` | Без пястной ставит проксимальную в мире от запястья — верно |

Верный путь уже есть в Grip Extraction. `GripPoseEncoder.Encode(..., TargetRigWorld)`
(`Assets/Editor/VR_Battlegrounds/Avatars/GripExtraction/GripPoseEncoder.cs:24-53`) делает так:
мировой путь на риге цели → `new UxrHandDescriptor(rig.Avatar, side)`, поза получает пястные цели.
`GripPoseEncoder.cs:82-96` (`RuntimeVsWorldDeg`) проверяет расхождение рантайма и редактора.
Легаси-регистрация не вызывает ни то, ни другое.

Пястные рига цели. Кисть BigHands/Cyborg в `Assets/Prefabs/Player/Avatars/Bases/PlayerBase.prefab:1972-1995`:
`_thumb._metacarpal = 0`, у четырёх пальцев пястные есть. Локальные повороты пястных относительно `Wrist_Left`
(тот же донор BigHands): `Index_Palm_Left` 177,0° (`:4233`), `Middle_Palm_Left` 178,9° (`:4759`),
`Little_Palm_Left` ~177° (`:52608`), ось ≈ X. Профиль цели требует пястных: `GripTargetProfile.cs:122,144`
(`RequireMetacarpals = true`). Замер 179° — `experts/avatar-grip/journal/2026-10-13-cyborg-grips.md:8-10`.
Большой палец без пястной, поэтому дефект его не затрагивает.

## 2. Числа (YAML, левая рука; правая — `Mirrored()` и даёт те же числа)

Метрика — разложение swing-twist поворота узла ([right up forward] в осях родителя):
`flex` — поворот вокруг универсальной оси right (сгиб), `off` — остаток вне неё.

Признак, который полностью разделяет хорошие и плохие позы, — `_hasMetacarpalInfo`:

| Поза | Пястные | Оценка пользователя |
|---|---|---|
| HandsPack_AR15_Mag_Grip (eed93eed2, ручная) | 8 (4 пальца × 2 руки) | хорошая |
| HandsPack_AR15_Mag_Grip (5c6f143bd, из пака) | **0** | была плохой |
| Kinemation_AK105_Mag_Grip, HandsPack_Gun_Grip | 24 / 8 | хорошие |
| Gun (Blend) | 16 | хорошая |
| AR15_Support, MKR9_Mag, PPK_Mag, AK105_Slide, SRM12_Slide | **0** | плохие |
| TR15_Mag_Grip в 5c6f143bd | **0** | плохая |
| TR15_Mag_Grip в HEAD (3cb622a49) | 24, байт-в-байт AK105_Mag_Grip | — |

Форма самих дескрипторов правдоподобна, перестановки осей нет:

- средняя и дистальная фаланги у всех поз из паков — почти чистый шарнир: `off` 0–10°, у HandsPack 0°;
- проксимальная пальцев 2–5 — `off` 1–24° (разведение пальцев);
- большой палец (`ProximalNoMetacarpal`) — `off` 76–104°; у хороших ручных поз 76–81°, это норма оппозиции.

Если бы оси пальцев рига-источника не совпадали с осями узла (крен, перепутанный палец), шарнир дал бы
большой `off` у средней и дистальной фаланги. Его нет.

Ручная доводка AR15_Mag показывает тот же механизм. Пересохранение в редакторе поз на Cyborg добавило
пястные (0 → 8). Форма изменилась умеренно: index.Proximal flex −1 → +19°, Intermediate 78 → 77°;
средняя и дистальная по-прежнему без `off`.

Вторично, данные кадров пака: переразгиб дистальных фаланг у PPK_Mag_Grip (большой −30°, указательный −13°)
и у TR15 из пака (мизинец −11°). Это вопрос качества кадра, не оси, и может заметно ухудшать вид.

## 3. Причины по рангу

1. **G6 повторно (основная, доказана).** `AvatarGripRegistration.CreatePose` (`:827-834`) и Rebuild
   (`:607`) пишут в CyborgHands дескриптор источника без пястных. Рантайм SDK
   (`UxrRuntimeFingerDescriptor.cs:83-87,99-103`) накладывает его под пястной: ошибка около 179° у пальцев 2–5.
2. **Защиты нет.** `PoseDefect`/`DescriptorDefect` (`AvatarGripRegistration.cs:676-681`,
   `PackOrigPoses.cs:196-205`) и `HandPoseDescriptorSanityTests` проверяют только ортонормированность и
   зеркальность. Пястные и расхождение рантайма с миром не проверяются. Существующая поза с тем же именем
   берётся как есть (`AvatarGripRegistration.cs:619-625`), поэтому повторный прогон дефект не чинит.
3. **Патч SDK не сделан** (бэклог `sdk-no-metacarpal-pose`, `experts/avatar-grip/backlog.md:15`): любая
   поза без пястных на этой кисти сломается так же.
4. (Вторично) переразгиб в отдельных кадрах пака — см. раздел 2.

## 4. Исправление

A. Минимальное, в зоне рецепта. В `AvatarGripRegistration.Process` перед `CreatePose`/`CopySerialized`
перекодировать обе руки на уже открытом `GripTargetRig rig` (`:504`):
`GripPoseEncoder.Encode(rig, side, source.GetHandDescriptor(side), GripPoseEncoding.TargetRigWorld, null)`,
затем записать Fixed-ассет с этими дескрипторами. После записи проверить
`GripPoseEncoder.RuntimeVsWorldDeg(rig, side, pose) < 1°`, иначе отказ точки. `ShapeDeg` ≈ 0 по построению,
выравнивание не меняется: оно уже считается мировым путём. В ветку «существующая поза»
(`:619-625`) добавить проверку пястных: при их отсутствии — ошибка с подсказкой Rebuild.

B. Системное, отдельной задачей. Патч SDK (`patch-reserve`): в `UxrRuntimeFingerDescriptor` ветка без пястной
при `avatarFinger.Metacarpal != null` должна считать `ProximalRotation = Inverse(avatarFinger.Metacarpal.rotation) * proximalWorldRotation`.
Нужна регрессия по всем аватарам.

## 5. Тест (только после приёмки пользователем в шлеме, по правилу проекта)

- Полный тест. Новый EditMode-тест на API UltimateXR (сборка тестов ссылается на `UltimateXR`, но не на
  Assembly-CSharp-Editor, поэтому `GripTargetRig` недоступен). Загрузить `MEF_cyborg_hands_Player.prefab`
  (`LoadPrefabContents`) и запомнить bind-повороты кисти. Для каждой позы `_handPoses` базы CyborgHandsBase и
  каждой руки:
  1. применить мировой путь `UxrAvatarRig.UpdateHandUsingDescriptor` и запомнить повороты костей;
  2. восстановить bind;
  3. применить рантайм-путь `new UxrRuntimeHandDescriptor(...)` + `UxrAvatarRig.UpdateHandUsingRuntimeDescriptor`;
  4. проверить, что максимальный угол расхождения меньше 1°.

  Ловит G6 и любые будущие расхождения рантайма с редактором.
- Дешёвый статический вариант (`HandPoseDescriptorSanityTests`): у поз, зарегистрированных на аватаре с
  пястными у пальцев 2–5, у этих пальцев должно быть `HasMetacarpalInfo = true`.
- Опционально, сторож суставов: средняя и дистальная фаланги `off` ≤ 20°, `flex` ≥ −15°. Ловит переразгиб PPK.

## 6. Затронутые позы (у всех `_hasMetacarpalInfo = 0`, все зарегистрированы в CyborgHandsBase)

`Assets/Prefabs/Player/HandPoses/CyborgHands/`:
HandsPack_AR15_Support, HandsPack_FabarmSDASS_Pump_Grip, HandsPack_PPK_Mag_Grip, Kinemation_AK105_Slide_Grip,
Kinemation_Herrington_Shell_Grip, Kinemation_MKR9_Mag_Grip, Kinemation_Mk14_Mag_Grip, Kinemation_SRM12_Mag_Grip,
Kinemation_SRM12_Slide_Grip, Kinemation_Viper_Mag_Grip.

TR15_Mag_Grip в HEAD уже равна шаблону AK105 и здорова. Если в шлеме она всё ещё плохая, причина в
выравнивании по шаблону (`TemplatePoint`), не в пальцах.

Потенциально затронуты все будущие копии: 55 из 68 поз `Assets/Art/HandPoses/Orig/**/*_orig.asset`
без пястных, 13 — с ними.

Пересоздание после исправления A: Rebuild этих точек. Либо разовое перекодирование ассетов на месте
(`Encode` → запись, GUID прежний). Затем проверка `RuntimeVsWorldDeg` ≈ 0 и шлем.
