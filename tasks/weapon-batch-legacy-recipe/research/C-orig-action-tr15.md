# Исследование C: `_orig` деталей механики, TR15, Grip/Support AR15/Fabarm (2026-10-23, read-only)

## Состояние
- У AR15 и FabarmSDASS сейчас нет ни Grip, ни Support `_orig` (только Magazine/Shell), узла `Orig` в префабах оружия нет.
  CyborgHandsBase: AR15 Grip — `CyborgHands/Gun.asset`, Support — SDK `GrabClaw`; Fabarm Grip — `Gun`, точки 1 нет
  (`_additionalGrabPoints: []`, `FabarmSDASS.prefab:2360`), помпа — `GrabClaw` (`:2976-2977`); затворы Browning/PPK/Viper —
  SDK `Grab` (guid 44ea1f…).
- Узлы деталей: `BrowningHiPower/Slide/Gate`, `PPK/Slide/Gun02_Detail_01`, `Viper/Slide/Bolt`, `FabarmSDASS/Pump/ForeEnd`
  (граббабл — `Slide`/`Pump` у корня, `HandsPackWeaponBuilder.cs:779`, деталь — `Clean(ActionPart)` `:781`, `:1142`).

## Схема имён деталей механики (сообщить сессии хватов)
- Вид точки = имя граббабла: `Slide` / `Pump` (без `_` — `AvatarGripRegistration.TryParseOrigName` `:139-151`).
- Поза `Assets/Art/HandPoses/Orig/<Пак>/<Пак>_<Предмет>_<Slide|Pump>_orig.asset`: `HandsPack_BrowningHiPower_Slide_orig`,
  `HandsPack_PPK_Slide_orig`, `Kinemation_Viper_Slide_orig`, `HandsPack_FabarmSDASS_Pump_orig`.
- Snap `<оружие>/<Slide|Pump>/Orig/<имя позы>/Left|Right` (внутри граббабла детали; `FindSnap(grabbable, "Slide", side)`
  работает — `GrabsOf` без `Grabs` возвращает узел, `PackOrigPoses.cs:530-538`).
- Вторая рука — зеркало по боковой оси корпуса `MeshContainer/<BodyObjectName>`.
- Регистрация (сессия хватов): поза `<Пак>_<Предмет>_Slide_Grip`/`_Pump_Grip` (`GripPoseName` `:157-165`), задание в `Jobs`
  (`:305-331`) для граббабла детали, `OrigSource`/`FindSnap` от узла граббабла (`:645-677`).

## Выбор кадра детали
- Как `MagazinePoint` (`PackOrigPoses.cs:728-802`), но смещение от покоя (RestClip/`Sample(null)`), а не от первого кадра;
  порог — доля хода (≥30 % наибольшего смещения), плюс деталь должна двигаться внутри окна (размах ≥20 % хода),
  иначе на затворной задержке ложное окно. 60 мм до меша, 5 мм/5°, ≥3 кадра, середина самого длинного окна.
- Помпа Fabarm: кадр позы `Aming_Idle` t=0, левая рука, деталь — цевьё; `Shot` — только проверка жёсткости.
- Клипы: Browning — `Recharge` + все клипы; PPK — `Reload`, `Reload_Fix`; Viper — пары `A_FP_*`/`A_W_*` (`Reload_Empty`,
  `Reload_Tac`, `Inspect`, `Mag_Check`, `Draw`); Fabarm — `Aming_Idle` t=0.

## План правок PackOrigPoses
- `Item` + `ActionSource`; `Items()` — источник из рецепта при `Action != None` (белый список 4 стволов на первом шаге);
  `ActionPoint()`; `OrigPoint` + `SnapRoot`, `MirrorFramePath`, `Experimental`; `PlaceSnaps`/`WriteSnaps` — контейнер на точку,
  `free`/`additive` по всем; ошибки по флагу `Experimental` вместо `MirrorSnaps` (`:366`, `:381`); `OutputPaths`
  (`:221`, `:241`); `CheckBody` как `MagazineBodyCheck` (`:829-837`); отчёт `actionNote`/`actionScan[]`.
- Легаси-рецепты 3+3 вынести в публичный источник (сейчас `HandsPackLegacyRebuild.cs:209-246`, ActionPart AR15 из донора
  `:231-234`); `PackOrigPoses` читает их вместо копий (`:178-189`).

## TR15 — нет окна магазина
- `Kinemation_TR15_orig_sources.json`: `distance = "начало детали"` (пустые `Points`), `minPalmMm` 108,9–127,6 при
  `NearOrigin = 100` (`:72`, `:743`, `:931`). Меш магазина длинный: начало кости у верхнего края, ладонь внизу.
- Вероятно `KinemationRig` берёт первый Transform `Magazine` (`:1063`, `:1089-1090`) — сокет без вершин, а не кость
  с вершинами (`KinemationWeapon.cs:101-114`). Тот же риск в `KinemationPoseExtractor.Extract` (`:78-79`) — snap Viper Magazine
  может быть не в осях `Viper_mag/Mesh`.
- Исправление: общий поиск кости детали по правилу `KinemationWeapon` (Transform из bones с вершинами); при пустых точках —
  вершины сохранённого меша `Assets/Art/Weapons/Kinemation/<Name>/Meshes/<Name>_<Clean(part)>.asset`; `NearOrigin` не
  поднимать; запасной клип `Mag_Check` только как резерв.
- Диагностика на worker: все Transform `Magazine` у `W_TR15` (рендерер, индекс, вершины, readable); что выбирают
  `KinemationRig.Find` и `KinemationWeapon`; покадрово Reload_Empty/Tactical/Mag_Check: Seat, ладонь до начала/вершин, дрейф;
  то же для Viper и AK105; для деталей механики — смещение, ладонь до меша, жёсткость, окна.

## AR15 / Fabarm Grip и Support
- Перевод на `BuildFull` сам их не добавит: Grip/Support — только из `HandsPackPoseImporter.Recipes` (`PackOrigPoses.cs:157`),
  там нет AR15/Fabarm. Предложение: `HandsPackWeaponBuilder.PoseRecipes(r)` по образцу `KinemationWeaponBuilder.PoseRecipes`
  (`:419-433`) — Grip правая `PoseClip` t=0, Support левая при `SupportGrip`; в `Items()` только для оружия без записи в
  `HandsPackPoseImporter.Recipes`. AR15 — Grip+Support (`Aim_Idle`), Fabarm — только Grip (`Aming_Idle`), вторая рука — `Pump`.
  Проверка: `FindSnap(root,"Grip",Right)` ≈ `PalmPoints[0]`.

## Вопросы пользователю (из исследования)
1. Вид точки `Slide`/`Pump` (рек.) или единый `Action`. 2. Охват: 4 ствола белым списком (рек.) или все с Action.
3. Помпа: `Aming_Idle` t=0 (рек.) или окно в `Shot`. 4. Grip/Support AR15/Fabarm: только для PackOrigPoses (рек.) или в
`HandsPackPoseImporter.Recipes` (MEF-импорт, Grip Extraction). 5. Общий поиск кости затрагивает `KinemationPoseExtractor`
(Grip Extraction) — делать здесь с повторным экспортом и проверкой Viper Magazine.
