# Качество рига и скиннинга кисти: исследование (2026-10-07)

Отчёт субагента-исследователя (только чтение; Unity и Blender не трогались). Гипотезы помечены H, метрики — M.
Локальный отчёт, не под Git.

## 1. Факты из репо и SDK

- Cyborg/BigHands: пястные кости `Index/Middle/Ring/Little/Thumb_Palm_*` (имена в `CyborgGeo.fbx`, `BigHandRightGeo.fbx`).
  У Cyborg перчатка — отдельный меш `GlovesRightGeo`, предплечье — жёсткие детали `ForeArmRightGeo`/`ForeArmCoverRightGeo`.
  Непрерывной кожи через запястье нет — нечему портиться при LBS.
- MEF: скелет Reallusion CC (`CC_Base_R_Hand`, `Index1..3`, `ForearmTwist01/02`, без пястных). Меш в игре —
  `MEF_Optimized.fbx`, перчатки децимированы 32 844 → 9 000 треугольников (`Tools/mef-avatar/README.md:61`), веса
  clean 0.001 → limit 4 → normalize (`08_stageD_lods_weights.py:51-60`).
- Кости префаба MEF — Transform из оригинального `MEF.fbx` (`MEF_Rig.prefab`: 52 ссылки на его guid, 0 на
  `MEF_Optimized`), меши `Optimized_MEF_Player` — из `MEF_Optimized.fbx`. Перенос сустава = правка двух источников
  (bindposes меша и Transform рига).
- UltimateXR считает оси кисти по костям: forward = запястье → середина `Middle.Proximal`/`Ring.Proximal`
  (`UxrAvatarArmInfo.cs:231`); `UxrHandIntegration.TryToMatchHand` (`UxrHandIntegration.cs:66-101`) ориентирует
  интеграцию и смещение датчика по `Index/Little.Proximal` (`UxrAvatarHand.cs:201-262`). Ошибка проксимальных костей →
  ошибка ориентации кисти относительно контроллера, оси кручения и плоскости сгиба пальцев.
- Предплечье в IK не крутится (`UxrArmIKSolver.cs:286-312`); кручение — `UxrWristTorsionIKSolver`:
  `ForearmTwist01` `_amount≈0`, `ForearmTwist02` `_amount=0.5` (`MEF_Rig.prefab:5562,7239,11253,11536`); порог
  перескока 150° (`UxrWristTorsionInfo.cs:106`).
- Масштаб кисти = масштаб тела: `Dummy Forward` = рост глаз игрока / `_eyesBaseHeight` (MEF 1,720, Cyborg 1,840) —
  у одного игрока кисть MEF в игре ~на 7 % крупнее, чем у Cyborg. Замеры по префабам этого не учитывают.
- Исключено: на Quest `skinWeights: 4` (`QualitySettings.asset:76`), SMR `m_Quality` Auto.
- Прошлые попытки: пересадка кистей SDK работает на Heavy (путь A —
  `.claude/skills/setup-avatar/rig-amputation.md`, `BlenderScripts/amputate_avatar_hands.py`, `HandsIntegrationSetup`,
  `FinalizeRigMappingSetup`, база `PlayerBase_SdkHands`); twist-веса — `add_wrist_torsion_bones.py` (UE-имена,
  под CC адаптировать). MEF ведут на родных кистях (путь B, `rig-native-hands.md`, «позы на 3 фалангах садятся
  неточно»). `SkinnedMeshTransplant`, `ModularGloveBoneMapper` — перепривязка по именам, не ретаргет. Попыток
  «MEF + BigHands» в `git log --all` нет.

## 2. Гипотезы по MEF (оси: S — датчик контроллера, W — кость кисти)

- H1. Масштаб +7 % → «толстые перчатки». Проверка: `lossyScale` W в игре; эксперимент `Hand.localScale ≈ 0,935`.
- H2. Манжета перчатки скинована на Hand → при сгибе кисти ездит по рукаву, центр вращения «не там». M7.
- H3. Candy-wrapper при пронации (Hand=1 / Forearm|Twist01=0; при 90° сечение ×cos45° ≈ 0,71). M8.
- H4. Ось кисти UltimateXR наклонена из-за проксимальных костей (d на плече ~9 см: 3 мм ≈ 1,9°, 1 см ≈ 6,3°) →
  чистый сгиб частично читается как кручение и дёргает twist-кости — «не плавно». Разброс костяшек MEF по z 5,4 см
  против 4,3 у Cyborg — согласуется с поворотом рамки ладони ~5–8°. Проверка: `WristTorsionAngle` при чистом сгибе
  ±60° (норма < 3°). M3, M14.
- H5. «Proximal на 1 см впереди костяшки»: (а) шарнир слишком дистально → перенос сустава; (б) зона веса Proximal
  начинается проксимальнее шарнира → только веса. Различают M4–M6.
- H6. Ладонь без пястных — жёсткое тело, нет свода при хвате. Проверка — Hand Pose Fit против базы Cyborg/BigHands.
- H7. Перчатка толще кожи (тактическая перчатка поверх тела CC) + H1. M9.

## 3. Принципы и источники

- Сустав — в центре сечения; радиус сустава = расстояние до кожи; оси −Z вдоль кости, −Y от ладони:
  [W3C WebXR Hand Input](https://www.w3.org/TR/webxr-hand-input/),
  [OpenXR XrHandJointLocationEXT](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrHandJointLocationEXT.html).
- Grip pose — центр кулака, Z вдоль рукояти: [MDN gripSpace](https://developer.mozilla.org/en-US/docs/Web/API/XRInputSource/gripSpace),
  [Unity Palm Pose](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.11/manual/features/palmposeinteraction.html).
  Если датчик UltimateXR = grip pose (проверить), центр кулака аватара в позе Grip должен совпадать с началом S —
  абсолютный якорь, не зависящий от эталона.
- Valve: корень кисти неподвижен относительно reference pose контроллера, меш строят по эталонному скелету
  ([OpenVR Hand Skeleton](https://github.com/ValveSoftware/openvr/wiki/Hand-Skeleton)); Meta: свой меш на их
  арматуру ([Meta ISDK](https://developers.meta.com/horizon/documentation/unity/unity-isdk-customize-hand-model/)).
  Индустрия: кисть подгоняют под эталонный скелет, а не наоборот.
- Анатомия: MCP на 8–10 мм ниже дорсальной вершины костяшки; ладонная складка пальца на 1,5–2 см дистальнее MCP;
  пронация распределена по предплечью; ниже линии запястья — жёсткое тело, зона смешивания ±1–1,5 см у центра
  запястья, манжета принадлежит предплечью. LBS при кручении — candy-wrapper, лечение twist-костями; DQS раздувает
  сгиб ([Autodesk](https://help.autodesk.com/cloudhelp/ENU/MayaCRE-CharacterAnimation/files/GUID-630C335C-B63E-4F2E-A4A4-AEA1DD00B0D6.htm),
  [DQS для Unity](https://github.com/kosrud/dq-skinning-for-unity)).
- Размер виртуальной руки меняет восприятие размеров мира: [Linkenauger](https://eprints.lancs.ac.uk/id/eprint/65730),
  [arXiv 2206.15218](https://arxiv.org/pdf/2206.15218). Размеры кисти — ANSUR II
  ([обзор](https://ph.health.mil/PHC%20Resource%20Library/ANSURIIDatabasesOverview.pdf)); средние длина ~19–19,5 см,
  ширина ~8,5–9 см — по памяти, сверить с CSV.
- Перенос весов между формами: [Robust Skin Weights Transfer (Epic, 2023)](https://www.dgp.toronto.edu/~rinat/projects/RobustSkinWeightsTransfer).

## 4. Варианты

- (a) Пересадка кисти SDK на MEF маршрутом Heavy — 2–4 дня, качество эталона. Blender-этап в `Tools/mef-avatar`
  (срез по манжете 2–3 см проксимальнее W, заглушка рукава, twist-веса) → `HandsIntegrationSetup` →
  `FinalizeRigMappingSetup` → fingertips → Rebase на `PlayerBase_SdkHands`; перетекстурировать перчатку BigHands;
  перерегистрировать позы пака и хваты 17 оружий (`HandsPackPoseImporter`/`HandsPackGripAligner`). Риски: стиль, шов
  от третьего лица, часы на `ForearmTwist02`, трупы/призрак, +1 skinned draw. Хитбоксов кисти нет.
- (b) Перериг родной кисти MEF — 4–8 дней, высокий риск: перенос суставов (± пястные) в Blender; веса Cyborg не
  переносить (жёсткие сегменты дадут ступеньки) — параметрический профиль или BigHands через параметрические
  координаты; обязательно пересобрать риг на `MEF_Optimized.fbx`; затем `CreateRigInfo`, `TryToMatchHand`,
  пересчёт хватов, Hand Pose Fit.
- (b0) Только веса и толщина, кости не трогать — 1–2 дня, низкий риск: лечит H2, H3, H5б, H7; скелет, позы и хваты
  без изменений.
- (c) Отдельная FP-рука для своего игрока — 4–6 дней: кисти внутри объединённого меша Body (маска в шейдере или
  отдельный SMR), покадровый ретаргет FP → родная кисть для третьего лица = второй писатель пальцев; дефекты MEF у
  удалённых остаются. Не рекомендуется.

## 5. Конвейер оценки

- Эталоны: Cyborg, BigHands; опционально рука игрока (hand tracking Quest 3) и grip pose как абсолютный якорь.
- Позы при одинаковом S, масштаб тела как в игре: пальцы Rest/Grab/Pointing/Fist/хват AK105 и Glock; запястье сгиб
  −70…+70° шаг 10°, отведение −20…+30° шаг 10°, пронация −90…+90° шаг 15°, диагонали (~60 кадров); непрерывный
  проход 1° для гладкости.

| Метрика | Что меряет | «Хорошо» |
|---|---|---|
| M1 | Положение запястья | ≤ 3 мм |
| M2 | Рамка ладони по мешу (центры сечений, PCA ладони) против эталона | ≤ 3° |
| M3 | Оси UltimateXR против рамки меша (тест H4) | ≤ 2° |
| M4 | Центровка шарнира в сечении ê = ‖c−p‖⊥/r | ≤ 0,15 |
| M5 | Центр перехода весов относительно шарнира (t*: w_child = 0,5) | \|Δ\| ≤ 2 мм |
| M6 | Глубина MCP под вершиной костяшки в кулаке | 8–10 ± 3 мм |
| M7 | Утечка веса Hand на рукав (> 2 см проксимальнее W) | < 2 % |
| M8 | Профиль кручения τ и сжатие сечения | max \|Δτ\|/см ≤ 0,15; A(90°)/A(0) ≥ 0,9 |
| M9 | Толщина кость → поверхность | Δr ≤ +2 мм |
| M10 | Длины сегментов и кисти | ± 3 % |
| M11 | Жёсткость ладони (RMS от Kabsch), деформация рёбер/объёма | RMS ≤ 1 мм |
| M12 | Зазор до рукояти, центр «трубки» пальцев против начала S | 0–3 мм |
| M13 | Гладкость весов (энергия Дирихле на ребро) | по зонам против эталона |
| M14 | Изменение `WristTorsionAngle` при чистом сгибе | < 3° |

Пороги «внимание/плохо» — в 2–3 раза мягче; стартовые откалибровать по Cyborg/BigHands как «хорошо».

- Отчёт: JSON с идентичностью входов (как Hand Pose Fit), PNG в 4 видах в осях S (эталон голубой, подопытный
  оранжевый каркас), векторы смещения суставов, профили весов, `index.html`.
- Автопоправки (только предложения): сдвиг сустава Δp = −e + поправка M6, затем перебор ±1 см шагом 1 мм с минимумом
  деформации; параметрические веса (smoothstep с центром на шарнире, полуширина 0,6·r; манжета по градиенту τ) →
  JSON для Blender; масштаб кисти; усушка вдоль нормалей по M9.
- Куда встроить: `AvatarPuppetStand` (программа «запястье» в `PuppetPrograms`, зонд `PuppetHandProbe`); из Hand Pose
  Fit — извлечение треугольников кисти (`HandPoseFitSnapshot.cs:285-310`, `BakeMesh(false)`, доминирующая кость,
  `_worldToFrame` → S⁻¹), `HandPoseFitGeometry`, `HandPoseGpuField`, `HandPoseFitRenderer`/`FitRasterLabels`;
  новый анализатор `Assets/Editor/VR_Battlegrounds/Avatars/HandRigQuality/` со своим asmdef; тесты — после приёмки.

## 6. Рекомендация

Сначала измерить, потом (b0). Если M3 > 5° или M4 «плохо» на MCP — (a) пересадка кисти SDK (маршрут отлажен на
Heavy, одна кисть для всех будущих аватаров). (b) — только если стиль SDK-кисти не принят, с пересборкой рига на
`MEF_Optimized.fbx`.

Минимальный эксперимент (~1 день, без коммитов, временные правки экземпляра): 1) масштаб кисти MEF ×0,935 (H1);
2) `WristTorsionAngle` при чистом сгибе у MEF и Cyborg (H4); 3) `_amount` twist-решателей 0,33/0,66 вместо 0/0,5
(H2, H3); 4) статический Editor-скрипт только на чтение: M4, M5, M7 по `MEF_Optimized`.

Этапы: 0 — эксперимент; 1 — конвейер метрик (2–3 дня), пороги по Cyborg/BigHands; 2a — (b0) этап E в
`Tools/mef-avatar` + масштаб кисти в runtime; 2b — (a), если b0 не хватит; 3 — позы и хваты оружия, Hand Pose Fit;
4 — после приёмки: `HandRigQualityTests`, `AvatarLoadoutTests`/`PrefabCompositionTests`/`GrabPoseCoverageTests`,
документация (README, CHANGELOG, шаг «оценка кисти» в `/setup-avatar`).

Архитектурная развилка: масштаб кисти неявно равен масштабу тела. Рекомендация — отдельный параметр «масштаб кисти» с
одним владельцем (компонент рядом с применителем калибровки, не в `PhysicalSpaceSyncManager`), применяемый и к
удалённым аватарам тем же сетевым путём.

## 7. Hand Pose Fit: встраивать или отдельно (дополнение 2026-10-07)

Рекомендация — **(Б)**: отдельный анализатор `HandRigQuality` поверх общего слоя «геометрия кисти», вынесенного из
Hand Pose Fit.

- Hand Pose Fit — «рука против предмета»: запрос требует Grabbable/GrabPoint (`HandPoseFitTypes.cs:14-26`), рамка —
  оси предмета (`HandPoseFitSnapshot.cs:44,130`), рамки датчика нет. `CaptureHand` (`HandPoseFitSnapshot.cs:274-316`)
  отдаёт только `FitTriangle` (`HandPoseFitGeometry.cs:8-18`) — индексы вершин, веса и топология теряются.
- Без изменений годятся: `FitSurface` (BVH, Distance/NearestHit — дифф поверхностей, M9, M12), `HandPoseGpuField`
  (тепловая карта), `FitAssetIdentity`/`Identity()` (`HandPoseFitAnalyzer.cs:157`), `FitRasterLabels`; весь Hand Pose
  Fit — для M12 на реальных рукоятях.
- Расширить: извлечение кисти (вершины, топология, веса), рамка как параметр (предмет / датчик S / запястье),
  построение меша и каркаса из рендерера (`HandPoseFitRenderer.cs:123-176`) — в публичный помощник.
- Нет: два аватара и сопоставление скелетов (M1–M3, M10), сечения (M4, M6), профили весов и кручения (M5, M7, M8,
  M13), сетка вращений запястья (M11, M14 — задача стенда).
- Статус: код — один checkpoint `f29fedc2` (2026-10-05, в `origin/dev`); незакоммиченных правок в worktree нет; плана в
  `coordination` нет; открыто: приёмка UI рентгена, игровое IK/Quest, quality-improvement (калибровка объёмов).
  Риск конфликта низкий, пока не меняются формат отчёта и калибровочный манифест (195 отчётов).
- (А) новый режим внутри — ~4–5 дней, ломает предметную модель запроса/снимка, раздувает схему 2; аудит
  (`Docs/audit/avatar-editor-tools-audit-2026-10-04.md`) запрещает второй формат отчёта в `HandPoseFitTypes`.
  (Б) — ~1–1,5 дня на вынос слоя + 3–4 дня на анализатор; один источник геометрии. (В) — дублирует извлечение, BVH и
  рендер.

Общий слой: `Assets/Editor/VR_Battlegrounds/Avatars/HandPoseReview/Core/`, namespace
`VrBattlegrounds.Editor.HandGeometry` (лучше своя asmdef `VrBattlegrounds.HandGeometry.Editor`); владелец — владелец
Hand Pose Fit, HandRigQuality только потребляет; изменения — через план в `coordination`.

- `HandMeshSample` — запечённые вершины/нормали в рамке, треугольники, веса (`GetBonesPerVertex`/`GetAllBoneWeights`)
  с картой кость → сегмент, доминирующая кость, путь рендерера, `ToFitTriangles(zoneMap)`.
- `HandSkeletonSample` — суставы `UxrAvatarFinger`, запястье, предплечье, twist-кости с `_amount`, универсальные оси.
- `HandFrame` — `FromObject(root)`, `FromControllerSensor(avatar, side)` (S = W · inv(`_localSensorHandPos/Rot`),
  `UxrControllerTracking.cs:370-386`), `FromWrist`.
- `HandCaptureKey` — ключ геометрии и идентичность (из `HandPoseSdkPreviewSession` с `FitAssetIdentity`).
- `FitRaster` — публичное построение меша/каркаса и ортографические виды с параметром рамки.
- Вне слоя: предметы, объёмы, контакты, калибровка — Hand Pose Fit; метрики M1–M14 — `Avatars/HandRigQuality/`
  (свой тип отчёта + общий `FitAssetIdentity`); прогон поз и вращений — `AvatarPuppetStand`, зонд `PuppetHandProbe`.

Порядок: 1) вынос слоя без изменения поведения Hand Pose Fit (равные JSON и SHA на зафиксированных входах, тесты
`HandPoseFitGeometryTests`, `SdkHandPreviewParityTests`, `SdkPreviewBindingTests` зелёные); 2) HandRigQuality —
статические M1–M10 и M13 в preview-сцене без Play; 3) стенд для M11 и M14.
