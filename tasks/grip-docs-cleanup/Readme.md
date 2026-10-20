# Уборка хватов: документация автоанализа, экспортёра и редактора поз

2026-10-20. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/grip-docs-cleanup` (схема «задача = агент»).

## Цель

Перед закрытием hand-rig-quality разложить по Docs/ знания трёх направлений работ по хватам: (1) автоматический анализ качества хвата, (3) экспорт хватов из оружейных паков под кисть Cyborg (Grip Extraction), (4) ручная доводка в редакторе Hand Poses (IK, «Обхватить», рентген).

## Этапы

Этапы и области записи — в [plan.json](plan.json). Заготовленный этап `work` пишет только в `tasks/grip-docs-cleanup/**`: перед `begin` объявите нужные пути (`writes`) и перерегистрируйте план (`register`).

## Этап quality-analysis

2026-10-20. Направление 1 — автоматический анализ качества хвата.

**Сделано:**

- [Docs/grip-quality-analysis.md](../../Docs/grip-quality-analysis.md) — входной документ: состав анализатора
  (снимок Fit, знаковое расстояние, подушечки, полосы SDK, авто-тип, правила gross/notable, подсказка curl,
  лист ревью, предпросмотр правки), запуск через окно и API, калибровка `calib-fbb5bd5b-01`, чтение результата,
  ограничения с уроком R2, уровни доказательства, состояние на 2026-10-20. Каждое утверждение о поведении
  сверено с кодом `Assets/Editor/VR_Battlegrounds/Avatars/HandPoseReview/`.
- [Docs/grip-known-issues.md](../../Docs/grip-known-issues.md) — реестр F1–F8 (аудит MEF) и G1–G8 с аватаром
  находки (MEF native или CyborgHands), причиной, доказательством, статусом и направлением исправления.
- Шапки со ссылками в `Docs/hand-pose-fit-tool.md` и `Docs/hand-pose-fit-analysis.md`; строка в
  `experts/avatar-grip/AGENT.md` (Details).

**Проверено статически на dev `ade7d06ef`** (чтение YAML `UxrGrabbableObject`, прямые записи и переопределения
вариантов; GUID базы CyborgHandsBase `71b74b0d08444f49bbb7aa2828d0890a`):

- записи CyborgHandsBase с позой и align есть у SDK Gun, Machinegun, Shotgun, их магазинов, Grenade и обеих
  ручек `Tablet_Base`; у `DogTagPanel` записей нет ни для одного аватара;
- пустая поза затворов у 11 стволов; общие позы SDK у проверенных магазинов (AR15, BrowningHiPower,
  патрон FabarmSDASS), цевья AR15 и помпы FabarmSDASS;
- данные хвата `MKR9.prefab` не менялись с 2026-10-14; `_localDisabledGameObjects` у
  `PlayerControllersCyborgAvatar` задан (3 объекта); этот аватар — вариант CyborgHandsBase;
- отказы `GrabPoseCoverageTests` прогона 772 разобраны по отчёту (43 из 56 отказов).

**Не перепроверено** (Unity, Play и шлем не запускались): геометрия хватов на CyborgHandsBase, прохождение
`GrabPoseCoverageTests`, `RegisteredAvatarBodyTests` и `CorpseTests` после реструктуризации 2026-10-18,
записи 9 стволов R2 в префабах против результата 712 (ассеты поз сверены: 12 из 15 `*_Grip` совпадают), захват ручек планшета в игре,
совместимость полос `contact-bands-v1` с изменённым 2026-10-16 снимком.

**Расхождения старых документов с кодом:**

- раздел «Встроенная диагностика SDK Hand Pose Editor» в `Docs/hand-pose-fit-tool.md` устарел: рентген-панель —
  заглушка (`HandPoseSdkContactDiagnostics.Enabled = false`), `HandPoseEditorCapture.Export` не вызывается;
- `Docs/hand-pose-fit-tool.md` описывает версию `mesh-fit-0.4-local-volume`, схему 2; в коде схема 4,
  `mesh-fit-0.6-grip-rules`; знаковые метрики, правила, лист ревью и предпросмотр там не описаны.

## Этап grip-extraction

2026-10-20. Направление 3 — перенос хватов из паков HandsPack и KINEMATION под кисть Cyborg (Grip Extraction).

**Сделано:**

- [Docs/grip-extraction.md](../../Docs/grip-extraction.md) — входной документ: место среди направлений, схема
  конвейера, источники (реализован только `pack`), нейтральный хват и центр ладони, посадка (формула, k),
  кодирование позы, профили цели и добавление профиля, режимы Measure/Preview/Apply и что пишет Apply,
  поля запроса и пример для `CyborgHands`, проверка результата, ограничения, история R0–R2 с числами,
  план R3–R5 и причина остановки, «Предлагаемое направление» (перенос контактов через контактный IK,
  помечено как предложение), «Состояние на 2026-10-20», «Перед следующим применением».
- Ссылки на него в `Docs/grip-quality-analysis.md` (назначение, раздел API) и `Docs/grip-known-issues.md`
  («Как читать», G1); строка в `experts/avatar-grip/AGENT.md` (Details).

**Сверено с кодом** `Assets/Editor/VR_Battlegrounds/Avatars/GripExtraction/` (HEAD ветки совпадает с `origin/dev`
по этим путям): порядок шагов `Run`, поля `GripExtractionRequest` и умолчания, профили и их константы,
формула `GripPlacement`, зеркало `GripSample.ForSide`, кодирование и проверки `GripPoseEncoder`,
запись `GripPoseAssets` и `GripRecordWriter` (align, `BindToPrefab`, `_assetId`, сторож), scoped-save,
`GripFitCheck`, R0; определение центра ладони — SDK `UxrAvatarHand.GetPalmCenter`; публичный API
`ContactIkPreviewSession` и `ContactIkSourceTargetMapping.FromSource`.

**Сверено с деревом и историей Git:**

- пути профилей после переезда 2026-10-19 (`a62039a26`): все константы Cyborg существуют, GUID базы
  совпадает; нет прежнего `Experimental/…/MEF_cyborg_hands_Base.prefab` (запасной путь) и
  `Assets/Prefabs/Player/MEF_Base_Avatar.prefab` (перенесён в `Unused/` 2026-10-17);
- ассеты поз результата 712 (ветка `archive/avatar-grip/r2-rejected-result-712-verified`, `1a71e6af6`)
  против `origin/dev`: 26 из 30 совпадают побайтово; отличаются `HandsPack_Gun_Grip`,
  `Kinemation_Herrington_Grip`, `Kinemation_Viper_Grip`, `Gun` — версии dev из `1b2f88612`, не равны ни 712,
  ни его базе `04fc9de88`; пять из семи поз коррекции 712 на dev — версии 712;
- коммиты истории на dev: `67e3b06f5`, `6190ce80d`, `e83a2d4a5`, `e16b86b82`, `846fa4bca`, `1b2f88612`;
  scoped-save, профиль `CyborgHands` и захват геометрии источника пришли в dev в `1b2f88612`.

**Не проверено** (Unity, Play и шлем не запускались): компиляция и прогон конвейера на текущем dev,
цепочка префабов `CheckRig` (только чтение YAML), совпадение «граббер → ладонь» у `PlayerControllersCyborgAvatar`
и `MEF_cyborg_hands_Player`, текущее состояние этапов в хабе (взято из Readme hand-rig-quality), содержимое
отчётов R1 (числа — из Details.md и changelog), публикация архивных веток на origin (ls-remote пуст).

**Расхождения:**

- профиль `MEF` и R0 не работают: `HandsPackPoseImporter.GripAvatars` указывает на отсутствующий
  `Assets/Prefabs/Player/MEF_Base_Avatar.prefab`; тот же путь — у импортёра паков, `ArsenalEditorWindow`,
  `ArsenalBuildPreflight`, `AvatarMixamoLocomotionSetup`, `CyborgLegsBuilder` и в Architecture map
  `experts/avatar-grip/AGENT.md`;
- `AvatarRecordGripSource` и `ManualGripSource` не существуют даже как заглушки — только упоминание в
  комментарии `IGripSource` и исключение `CreateSource`;
- комментарий поля `Target` в `GripExtractionRequest` перечисляет только `MEF | MEF_cyborg_hands`;
- реестр `Docs/grip-known-issues.md` (G5) и бриф этапа называли «13 из 16 поз `*_Grip` совпадают»; фактически
  из 15 поз `*_Grip` результата 712 совпадают 12 (из всех 30 ассетов — 26). В `grip-extraction.md` записаны
  проверенные числа; G5 и раздел этапа quality-analysis исправлены основным агентом в этом же этапе;
- `reports/resume-2026-10-14/r3-source-prerequisites.md` называет прежний путь
  `Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab`;
- захват геометрии источника (`GripSourceContactFrame`, обе прямые руки, `PackClipGripSource.Sample(point, true)`)
  уже есть в коде (Details: «opt-in source capture»), но конвейер `Run` его не вызывает, а `Pads` остаются null;
  в `grip-extraction.md` он учтён как основа «Предлагаемого направления».

## Этап pose-editor

2026-10-20. Направление 4 — ручная доводка хватов в SDK Hand Pose Editor.

**Сделано:**

- [Docs/grip-pose-editor.md](../../Docs/grip-pose-editor.md) — входной документ: место среди направлений, состав
  (что в dev используется, что заглушка или не вызывается), как открыть окно и требование «аватар-владелец позы»,
  панель «Автоснап и IK конечных фаланг» (элементы, «Обхватить», IK кончиков), рентген v4 и его цвета,
  Undo и сохранение (AutoSave, глобальный `SaveAssets` при закрытии окна, общие позы SDK), рабочий процесс,
  модель контактного IK по контрактам v2/v3 и коду `GripAuthoring/ContactIk/`, API для агентов, ограничения,
  история 733–757 и рентгена v4, «Состояние на 2026-10-20», «Перед следующим применением», кандидаты массовой доводки.
- `Docs/hand-pose-fit-tool.md`: раздел «Встроенная диагностика SDK Hand Pose Editor» помечен устаревшим
  с 2026-10-16 и сокращён до ссылки; прежний раздел «Ограничения пользовательского рентгена» (о той же панели) снят
  вместе с ним; фраза об открытой UI-приёмке рентгена заменена ссылкой.
- Ссылки на `grip-pose-editor.md` в `Docs/grip-quality-analysis.md` (назначение, состояние),
  `Docs/grip-extraction.md` (место среди направлений), `Docs/grip-known-issues.md` («Как читать», G5);
  строка в `experts/avatar-grip/AGENT.md` (Details).

**Сверено с кодом** (HEAD ветки = `origin/dev` по этим путям): `HandPoseSdkContactEditing.cs` (UI, `AutoWrapAndCommit`,
`SolveFingerIkAndCommit`, пределы суставов, сбор треугольников), `UxrHandPoseEditorWindow.ContactEditing.cs`
(проверки `TryCommitContactEdit`, Undo), `UxrHandPoseEditorWindow.cs` (хуки, AutoSave, read-only унаследованных поз,
меню), `HandPoseSdkContactDiagnostics.cs` (заглушка), `HandPoseSdkContactBackend.cs` (нет вызовов),
`HandPoseXrayRenderer.cs`/`HandPoseFit.shader`/`HandPoseFitRenderer.cs` (цвета, где используется),
`ContactIkPreviewSession`/`ContactIkModel`/`ContactIkSolver` (`DistalPriority`, проверки входа),
`HandContactIkPreviewCapture` (DOF −35…+65°, бюджет 96). YAML: `_handPoses` (65) на CyborgHandsBase без
привязок `_wrist`; `_handPoses.Array.size: 0` у `MEF_cyborg_hands_Player` и `PlayerControllersCyborgAvatar`.
История Git: `1b2f88612` и технические снимки `14d401177`, `d4f918053`, `c3f7a75d9`…`b4c156416`.
Отчёты (основной checkout): 757-Review, `sdk-contact-20261015-r2` и `sdk-contact-20261016-xray-v4`
(`candidate.json`/`receipt.json`: 8 → 9 из 15, `changedAssets = []`, одинаковые числа), 733/735/740/749,
`_preserved-2026-10-20/MANIFEST.md`.

**Не проверено** (Unity, Play и шлем не запускались): компиляция и работа панели на текущем dev; блокер правки поз
CyborgHands (вывод из YAML и кода SDK); число шагов Undo при перетаскивании; поведение `EnsureValidAvatarRigAxes`;
что именно пользователь проверил при приёмке 2026-10-16; состояние этапа `grip-contact-authoring` в хабе
(взято из брифа, хаб не читался); исход билета 764 (в документах и отчётах не найден).

**Расхождения:**

- код dev ≠ контракт v3 и отчёт 757: панель в dev — эвристика по костям («Обхватить» с радиусами фаланг,
  IK кончика без поверхности), без BakeMesh, опорных точек, `DistalPriority`, переноса ладони, профилей обхвата,
  непрерывного режима и рентгена в окне; `HandPoseSdkContactBackend` (v3) не вызывается, `HandPoseSdkWrapProfile`
  не используется. Замена произошла в технических снимках 2026-10-16 01:47–02:42 и пришла в dev с `1b2f88612`;
- `tasks/hand-rig-quality/plan.json` `current_work.last_worker` (757): `numerical_targets_reached: true`,
  `UI_interaction_checked: true`, два префаба в `changed_assets` — записано в `1b2f88612`; отчёт и `receipt.json`
  прогона, а также снимок `b4c156416` — `false`, `[]`, `false`. В документе принят отчёт прогона;
- подсказка панели «выберите CyborgHandsBase» устарела после иерархии 2026-10-18 (у базы нет рига);
- статус «Обхват … (N/15 фаланг в контакте)» и текст ошибки упоминают коллайдеры, а код читает только `MeshFilter`;
- SDK-патч 83 (и 85) отсутствует в журнале `Docs/UltimateXR/sdk-patches.md` (последний — 55); запись 44 там
  называет подписчиком модуль HandPoseReview, а контроллер с 2026-10-15 в `GripAuthoring`. Журнал не правился (запрещён этапом);
- `1b2f88612` изменил данные пястных общих поз SDK (`GrabClaw`, `Default`, `ControllerWindowsMixedReality`,
  `DemoLabLaser`) и четырёх поз CyborgHands; источник не установлен;
- меню маски в `contact-authoring-integration-20261015/Summary.md` — `VR Battlegrounds/Avatars/Hand Contact Mask`,
  в коде — `VR Battlegrounds/Avatars/Черновики/Площадь контакта кисти`.

## Этап contact-direction

2026-10-20. Исправление рамки документов направлений 1, 3 и 4 под решение пользователя о контакте.

**Решение пользователя** (сообщено 2026-10-20, по смыслу): живой рентген в окне редактора поз сильно лагал
и тормозил, а качество контакта можно упростить до проверки одной точки на поверхности фаланги пальца. Поэтому
2026-10-16 полностью перешли на панель «Автоснап и IK конечных фаланг» (технические снимки ik-wrap
`c3f7a75d9`…`b4c156416`, в dev — `1b2f88612`); режим оказался эффективным, кнопка «Обхватить» работает хорошо.
Метрика «точка фаланги» (N из 15 фаланг кисти в контакте) — основная метрика качества контакта для
анализаторов; это практическое упрощение, и дальше развитие идёт в этом направлении. Реализация v3 (BakeMesh,
`HandPoseSdkContactBackend`, `DistalPriority`, непрерывное решение, живой рентген) — заменённая ветка,
отложена из-за производительности; код остаётся в dev как запас и для офлайн-анализа.

**Переписано:**

- [Docs/grip-pose-editor.md](../../Docs/grip-pose-editor.md): блок «Важно: на dev не та панель…» заменён блоком
  «Решение пользователя 2026-10-16 / 2026-10-20»; в «Составе» v3 и рентген-панель — «заменено решением 2026-10-16
  (производительность)», панель — основной режим; в «Обхватить» добавлен подраздел «Метрика «точка фаланги»»
  (кость + радиус 6,8 / 5,8 / 5,0 мм, точки оси на 55 % и 90 %, касание при просвете ≤ 0,4 мм, откат при заходе
  глубже 1,2 мм, итог N/15) и таблица двух реализаций точки фаланги в dev (панельная и материальная точка кожи
  `HandContactProbeCapture.Measure`); оценка «эвристика по скелету, а не контакт кожи» заменена нейтральной
  «осознанное упрощение ради скорости»; «Чего нет по сравнению с контрактом v3» → «Что из контракта v3 не вошло
  в панель»; в «Перед следующим применением» вопрос «какая панель нужна» заменён направлением развития
  (вынос метрики и «Обхватить» в общий API для анализатора и Grip Extraction); обновлены «Ограничения»,
  «Состояние», история, рабочий процесс.
- [Docs/grip-quality-analysis.md](../../Docs/grip-quality-analysis.md): ранний раздел «Основная метрика: контакт
  точки фаланги (решение 2026-10-20)»; анализатор Fit по полосам SDK назван вторичным (грубый фильтр, листы
  ревью); интеграция метрики в отчёт — следующий шаг, не сделано (в «Состоянии» и «Перед следующим применением»).
  Урок R2 сохранён.
- [Docs/grip-extraction.md](../../Docs/grip-extraction.md): критерий приёмки переноса — метрика точки фаланги,
  кандидат на доводку — «Обхватить»; private — вопрос рефакторинга, а не качества. Пометки «не реализовано»
  сохранены.
- [Docs/grip-known-issues.md](../../Docs/grip-known-issues.md): направление 4, G4, G5 — «Обхватить» и метрика
  точки фаланги как основной инструмент доводки и проверки.
- [Docs/hand-rig-quality-tool.md](../../Docs/hand-rig-quality-tool.md): ссылка на прежнюю версию
  `tasks/hand-rig-quality/tool.md` (коммит `3def410af`) вместо ссылки на нынешний указатель.

**Сверено с кодом** `Assets/Editor/VR_Battlegrounds/Avatars/GripAuthoring/ContactPoints/`:
`HandPoseSdkContactEditing.cs` (`AutoWrapAndCommit`, `SweepJointCurlUntilContact`, `SweepDistalCurlUntilContact`,
`EvaluateSegmentClearance`, `SignedDistanceToWeaponSurface`, `GetDistalTipWorld`, `GetFingerAngleBounds`,
`CollectWeaponTrianglesInAvatarSpace`), `HandContactProbeCapture.Measure` и `HandPhalanxContactProbes.Evaluate`
(беззнаковое расстояние, `SignedDistanceMm = null`, `WithinBand` ≤ 2 мм), `HandPoseSdkContactDiagnostics`
(`Enabled = false`); вызовов `HandPoseSdkContactBackend` вне его файла нет. Отчёт 757 (основной checkout):
те же материальные точки, 8 → 9 из 15.

**Уточнения по коду, записанные в документы:**

- касание — просвет ≤ 0,4 мм без нижней границы (отрицательный просвет — заход); заход глубже 1,2 мм
  отвергается только на шаге догиба своего сустава (откат на 1,5°);
- итог N/15 — счёт фаланг, дошедших до касания при догибе, а не переизмерение итоговой позы: при откате
  фаланга засчитана при просвете до одного шага; заход, возникший до собственного догиба фаланги (сгиб
  предыдущего сустава), не исправляется и засчитывается как касание; разбивки по пальцам и счёта
  просветов/проникновений панель не выводит.
