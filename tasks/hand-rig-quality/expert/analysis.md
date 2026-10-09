# Эксперт по кистям и позам хвата аватаров: аналитическое предложение

2026-10-09. Только исследование и черновик; эксперт не установлен, полномочия не переданы.
Проверен linked worktree `hands-rig-quality`, HEAD `f8daff6843f35c569268fc33f0d4b9a8537dd8d0`.
Исходники читаются с имеющимися незакоммиченными изменениями, в том числе HandPoseReview/HandRigQuality.
Источники и точные точки входа — [sources.md](sources.md). Профиль — [draft-profile.agent.md](draft-profile.agent.md).

## Рекомендация

Создать **avatar-grip-expert — диагноста и настройщика кистей и поз хвата аватаров**.
Он различает ошибочную позу/точку/offset, дефект модели/рига/осей/bindposes/скиннинга,
импорт/scale/перенос позы/Hands Integration и runtime IK/tracking. Знает текущий код,
существующие инструменты, умеет готовить их запуск и читать отчёты, затем предлагает
подтверждённую коррекцию. Оружие, магазин, планшет и жетон — виды предметов для проверки кисти.
Образец WeaponSystem использован только для структуры базы знаний и профиля, не для предметного scope.

| Область | Что даёт | Ограничение |
|---|---|---|
| Только настройщик поз/точек | Pose assets, snap/offset и контакт | Не различает дефект позы и модели/скиннинга, рискует маскировать плохой риг |
| Диагност кистей/поз/интеграции — рекомендуется | Модель/кости/оси/веса/scale + pose assets + Hands Integration + HRQ/Hand Pose Fit + штатное применение в игре | Изменения FBX, общего SDK/IK и калибровки требуют согласования действующих владельцев |

Обязательная предметная база эксперта — кисти аватаров, позы и хваты. Машина оружия, учёт патронов и реализация
WeaponSystem в неё не входят. Эксперт не получает право менять их из-за того, что предмет — оружие.

## Границы

**Входит прежде всего:** диагностика формы кисти, костей/осей/skin weights/bindposes/import scale;
Native/SDK hands и интеграция; авторинг Fixed/Blend, перенос поз и покрытие аватаров;
hand align/snap/offset; измерение контакта и качества рига, интерпретация JSON/PNG/HTML;
настройка обеих кистей/поз на согласованной цели. Grab pipeline, выбор точки, две руки,
удержание/отпускание и сеть — контекст для отличия плохой кисти от ошибки её применения.
Это область знаний и согласованной работы, не право писать во все файлы.

**Остаётся у соседей:** механика предмета и её отклик — предметный владелец (для оружия WeaponSystem);
вибрация и её маршрутизация — haptics; общие body/arm IK, калибровка роста и сетевой scale —
аватарные владельцы; текущая коррекция MEF и общий geometry capture — hand-rig-quality;
экипировка и действия NPC — bots; экономика/выдача/стена — arsenal.
Эксперт диагностирует границу и предлагает контракт, вместо нового контроллера пальцев, IK или transport.
Калибровку прицела нельзя «исправлять» перемещением хвата: это отдельная предметная граница.

## Связка Hands Integration: подтверждённая развилка

По первичным исходникам [S14–S16]:

- `AvatarRigPreparation.Inspect/Build` проверяет единственный валидный Humanoid Animator.
  Native требует полный mapping обеих рук, сохраняет родные AvatarRig кости и добавляет
  **BigHandsIntegration как integration/logic prefab**. Это не замена видимой кисти на BigIKHand.
- Sdk требует явно подготовленный источник без родной геометрии кистей. `HandsIntegrationSetup.Setup`
  добавляет BigHandsIntegration, BigIKHandLeft/Right и HandRenderer; `FinalizeRigMappingSetup.Setup/MapHandRig`
  переносит mapping на SDK-кости. Нельзя запускать SDK remap автоматически на Native аватаре.
- В обоих режимах выполняются `UxrHandIntegration.TryToMatchHand`, выбор HandRenderer,
  ControllerAndCameraSetup, fingertips, `UxrAvatar.CreateRigInfo`, сохранение рига; позы создаются
  отдельным действием. `CanRunSdkSteps` запрещает SDK remap для NonSdkHands и проверяет provenance.
- `TryToMatchHand` требует wrist/finger references, размещает integration по wrist/palm и вычисляет
  ориентацию отдельно для каждой стороны. Grabber центрируется относительно ладони с
  `palmOut * 0.02f`; метод не исправляет веса/bindposes/форму пальцев. Awake переподчиняет integration
  кости кисти. True означает выполненное выравнивание, не хороший контакт.
- `HandPosesSetup.Setup` применяет Fixed или Open/Closed descriptors к целевому ригу,
  создаёт новые descriptors/ассеты обеих рук и назначает pose/controller events.
  Сгенерированный SDK preset — кандидат, а не автоматически принятая поза Native модели.

## Каталог существующих инструментов

| Инструмент / вход | Проблема и входы | Артефакт / предел |
|---|---|---|
| **Hand Rig Quality**: `Tools/VR Battlegrounds/Avatars/Hand Rig Quality`; `HandRigQualityAnalyzer.Analyze(HandRigQualityRequest)`; `HandRigQualityCalibration.CalibrateSdk(height)` [S17] | Риг/скиннинг prefab против Cyborg/BigHands; обе стороны, одинаковая высота глаз, общая SDK Default/Blend0, wrist frame для калибровки; sensor frame отдельно | report.json, geometry-target.json, geometry-reference.json, четыре каркасных PNG и index.html; static posed mesh без runtime IK |
| **Hand Pose Fit / Hand Pose Review**: Avatar Editor → «Открыть Hand Pose Review»; `HandPoseFitWindow.OpenFor`, `HandPoseFitAnalyzer.Analyze`, runtime `HandPoseFitRuntimeCapture.Request` [S12/S18] | Контакт: avatar/object prefab, grabbable/point/side/pose/Blend; объектная рамка, маска, regions/normals. Runtime — реально удерживаемый предмет после SDK | report.json и до24 PNG/HTML; zones/distances/intersections/reliability. Нет универсального балла и автоматического диагноза рига |
| **Avatar Editor / Workbench**: `Tools/VR Battlegrounds/Avatars/Редактор аватара`; `AvatarEditorWindow.OpenFor`; `AvatarRigPreparation.Inspect/Build`, ToolsPanel [S14/S15] | Model/rig/game variant, provenance, Native/Sdk, mapping/poses; рабочая сцена/новый output | Новый rig prefab, опционально pose assets, UI/план действия. Авторинг с мутациями; выбрать явную цель, не запускать весь pipeline ради диагностики |
| **SDK Hand Pose Editor**: `UxrHandPoseEditorWindow.Open(avatar,pose)`; Workbench «Открыть редактор поз SDK» [S19] | Fixed/Blend и unsaved rotations на target rig; встроенная диагностика текущего хвата выбранного point/side | Pose assets при явном Save, frozen preview/contact. Preview не доказывает tracking/controller/IK. Place Snap не является автоподгонкой меша |
| **Hand Integration**: `UxrHandIntegration.TryToMatchHand`; `HandsIntegrationSetup.Setup` / `FinalizeRigMappingSetup.Setup` [S16] | Integration/grabber alignment и SDK hand mapping при правильном wrist/finger rig | Transform/mapping/renderer данные, не quality report. Native имеет integration logic; SDK replacement/remap — отдельная ветка |
| **Blender/MEF pipeline**: Workbench Blender preparation; `CustomAvatarPipelineMenu`; `Tools/mef-avatar/README.md`, скрипты00–10 [S20] | Geometry/weights/LOD/export/bind; удаление родной кисти только при выбранном SDK replacement | Копия FBX/rig, .blend/экспорт/diff. MEF scripts — историческая реконструкция, не доказанный end-to-end repair pipeline; будущий «stage E» среди00–10 не найден |

Будущий запуск: проверить входы → согласованный scope/stage → точный пакет worker с lease/guard →
analyze/export или отдельно разрешённый authoring → finish/receive → интерпретация отчётов.
HRQ отвергает Play/compile/import и использует временные captures; runtime Fit требует Play и реального
хвата, не создаёт grip команды. **Текущий HRQ OutputDirectory жёстко требует Docs/tasks/report/hand-rig-quality/**:
новый reports путь нельзя просто передать в request. Миграция экспорта предусмотрена static-analyzer;
её нужно штатно выполнить до нового запуска по действующим правилам. Сейчас инструменты не запускались.

## Чтение отчётов

1. Проверить SHA/dirty scope, algorithm/schema/UnityVersion, GUID/fileID/dependency hash avatar/mesh/pose,
   renderer/LOD, side, pose/Blend, frame, SourceScale/AppliedScale и высоту глаз. MEF final
   eyesBaseHeight1.7202504 сохраняется; исходный риг глаз её не заменяет.
2. HRQ `Status=measured_static` — получен статический пакет. Отдельно читать `CalibrationStatus`,
   `Metrics[].Status/Reason/Value/ReferenceValue/Unit`, `ScoredMetrics` и `CriterionScores[].Samples`.
   unavailable — нет измерения; unreviewed — число без принятой нормы, не нормальная кисть.
3. StaticScore — среднее семейств покрытых метрик. В wrist0.5 балл M4/M9/M10; M2/M3/M5 предварительные,
   исключены. Среднее может скрывать один плохой сустав; смотреть raw subject/unit и coverage.
4. Калибровка проверяет algorithm, высоту, wrist/CommonSdkPose/Blend0, GUID/dependency hashes общей
   позы/SDK assets и точный reference Cyborg GUID/fileID/hash. После изменения входов калибровать снова.
5. Geometry JSON — поверхность/нормали/топология, WeightOffsets/Weights/BonePaths и renderer path.
   Transform[] Bones помечен JsonIgnore; положения размеченных суставов находятся в report Joints.
   PNG — сетки и каркасы в той же рамке: проверить видимую форму/манжету и кости относительно поверхности.
   Вычисленные контуры сечений и маски весов на PNG не нарисованы; ими нельзя подтвердить выбранное
   сечение или весовую область. Для этого нужны raw-данные и отдельная проверка расчёта. HTML —
   навигация, не отдельный результат. Старые HRQ0.1/0.2 изображения до scale fix не визуальный эталон.
6. Fit: source static/runtime, выбранный point/pose/GUID fallback, зоны/маски, distances/area по
   сегментам, intersections и reliability/reasons. Близость к чужой детали или неверные normals
   дают ошибочный вывод; принятые volume/reference fingerprints важны для знака/сравнения.
7. Preview parity — та же поза в двух представлениях. Static не доказывает динамику/IK; runtime
   snapshot не заменяет Quest/observer приёмку. Static вторая кисть не доказывает двухручный хват.

## Диагностические сценарии и последовательность настройки

| Симптом | Маршрут | Условие доказательства / коррекция |
|---|---|---|
| Вся рука смещена/повёрнута | Actual avatar GUID→entry→point→hand align, snap reference, integration wrist/grabber, calibrated scale; static/runtime | Неверный entry/offset при исправной HRQ форме → данные выравнивания. Только runtime расходится → tracking/IK/controller frame, не менять все позы |
| Пальцы входят в предмет | Fit на точном point/pose/Blend с проверенной маской/regions/normals; HRQ общей позы | Одна поза усиливает гипотезу pose/snap, разные позы/предметы — model/weights/rig. Это не доказательство: дефект оси/весов тоже может проявиться при одном сгибе; нужен контролируемый эксперимент |
| Перчатка/манжета деформируется | Полные weights/bindposes/provenance FBX; M4/M5/M7/M9; серии вращений для twist | Пронация требует динамического доказательства. Mesh/weights → Blender по согласованному источнику; не двигать кость отдельно от bindposes |
| SDK поза не совпадает/preview иной | Native/Sdk, mapping/universal axes, Fixed/Open/Closed, pose-name resolution, descriptor apply и baked mesh parity | Preview-only → preview/capture path; везде неверно на target rig → mapping/retarget/model. Перенос asset не гарантирует геометрию целевой кисти |
| Не тот grip point/предмет | GrabRules/TwoHandGrabPolicy, proximity/дальность/сторона, support/anchored rule; actual event point | Исправлять выбор/правило/proximity, не skin weights. Обе руки и support проверяются отдельно |

Порядок: input matrix → Native/Sdk/provenance → import/scale/mapping/оси → штатный descriptor/baked mesh →
точки/pose/контакт → динамика IK/tracking → минимальная коррекция → before/after одинаковых входов →
Unity/Quest/observer при сетевом влиянии → тесты принятого результата. Перериг/замена кисти/общий scale —
отдельное решение/контракт. Blender нужен для подтверждённого mesh/weights/bind дефекта, не любой ошибки snap.

## Дополнительная карта применения хватов

| Обязанность | Реальный исполнитель/точка входа | Следствие для эксперта |
|---|---|---|
| Подключение игровых разрешений | `PlayerGrabManager.OnEnable/OnDisable`, `IsGrabAllowed` → `GrabRules.IsGrabAllowed` [S1] | Сохранять одну цепочку CanGrabDelegate и ghost/map admission |
| Новый хват и свободная точка второй руки | `TwoHandGrabPolicy.IsGrabAllowed/ShouldYieldToFreePoint` [S2] | Это политика выбора, не отдельный механизм удержания |
| Поддержка/зависимые детали/вставленные предметы | `SupportGripRequiresMain.AllowsGrab`, `GrabOnlyWhenParentHeld.AllowsGrab`, `AnchoredItemGrabRule.AllowsGrab/GetHost` [S3] | Различать точку поддержки, деталь и предмет внутри гнезда |
| Данные позы для конкретного аватара | `UxrGrabPointInfo.GetGripPoseInfo`, `UxrGripPoseInfo`, `UxrGrabbableObject.GetGrabPointGrabAlignTransform` [S4] | GUID-цепочка аватара, pose asset, обе hand align ссылки и fallback — разные проверки |
| Выбор допустимого кандидата | `UxrGrabbableObject.CanBeGrabbedByGrabber`, `UxrGrabManager.Querying/GrabCandidates` [S5] | Исполняемый код важнее старого комментария о вызове делегата до расстояния |
| Ввод и смена позы | `UxrStandardAvatarController.ProcessHandManipulation`, `UpdateGrabPoseInfo` [S6] | Использовать SDK grab/pose pipeline, не писать пальцы параллельно |
| Grab/release/place и constraints | `UxrGrabManager.Manipulation`, `UpdateManipulation`, `KeepGripsInPlace` [S7] | Проверять ветку события и фазу; подписчик вызывается внутри sync операции |
| Особые удержания предмета | `MainGripAimLock.GrabbableObject_ConstraintsApplied`, `PumpGrabFollow.Pump_ConstraintsApplied` [S8] | Совместимость с предметным адаптером, не передача его владельца новому эксперту |
| Карман/якорь | `UxrMagazinePocket.OnProxyGrabResolving/ExtractMagazine/ChooseMagazine`, `AnchorPlacementReadiness.TryGetReadyGrabber` [S9] | Карман выбирает содержимое; proxy/grab manager завершают grab. Ready проверяется повторно |
| Сеть | `StateEventAuthority.IsAuthorOfItem/IsWorldAuthority/ShouldSend`, `NetworkStateRelay.HandleComponentStateChanged/Send/CmdComponentStateChanged/RpcComponentStateChanged` [S10] | Нет второго сетевого пути; публикационная фильтрация не заменяет guard действия |
| Физика после отпускания | `UxrGrabbableObject.RegularPhysicsSyncCoroutine` [S11] | Положение/скорость идут до сна Rigidbody; collider/identity/authority диагностировать отдельно |
| Авторинг и измерения | `HandPosesSetup.Setup`; `HandPoseFitRuntimeCapture.Request`; `HandPoseFitSnapshot.CaptureHand`; `HandMeshCapture.Capture`; `HandRigCapture` [S12] | Контакт руки и качество рига — разные результаты; общий capture имеет одного владельца |

Названия классов устанавливают технических исполнителей. Они не доказывают организационное владение
целыми папками; допуск на запись должен следовать актуальному плану/контракту хаба.

## Подтверждено исходниками

1. Запись хвата выбирается по `GetPrefabGuidChain`; нет записи — `DefaultGripPoseInfo`. Нет нужного
   hand align или snap reference — выравнивание возвращает Transform предмета. Позы ищутся по имени
   в аватаре; недоступный override сбрасывается. Покрытие аватара и качество контакта проверяются отдельно.
2. В текущем `CanBeGrabbedByGrabber` доступность/сторона/coarse range/точная дальность идут перед
   `CanGrabDelegate`. Комментарии GrabRules/TwoHandGrabPolicy о более раннем вызове делегата устарели.
3. `TwoHandGrabPolicy` уступает занятую точку достижимой свободной. Если свободной нет, возвращает
   разрешение. `SupportGripRequiresMain` допускает поддержку только при основной руке другого grabber
   того же аватара; `AnchoredItemGrabRule` запрещает предмет внутри якоря другого grabbable.
4. В `UpdateManipulation`: расчёт предметов → smooth transitions → ConstraintsApplying → ConstraintsApplied
   → KeepGripsInPlace → ConstraintsFinished. Это последовательность внутри SDK, не доказательство всей
   runtime динамики. `MainGripAimLock`/`PumpGrabFollow` подключены в ConstraintsApplied.
5. Grab internal заканчивается `OnObjectGrabbed`/предметным событием; при извлечении из неконстрейненного
   якоря Removed следует после Grabbed; затем `EndSyncMethod` с рассчитанными grab event args.
   При handoff Released прежней руки возникает перед Grabbed новой. У release Releasing предшествует
   `NotifyEndGrab`/очистке GrabbedObject; Released и `EndSyncMethod` идут после обновления состояния.
   Нельзя переносить этот порядок на любую place/proxy ветку без чтения нужного метода.
6. `ShouldSend` специально пропускает события `UxrGrabManager`: сервер может отпускать чужие руки.
   Поэтому «всё отфильтрует relay» не заменяет `IsAuthorOfItem/IsWorldAuthority` для нового sync действия.
   Relay применяет событие на сервере, отражает observers, пропускает повтор на host/авторе.
7. Hand Pose Fit runtime запрещает pose override/временное смещение и требует реально удерживаемого
   предмета. Общий geometry capture использует `BakeMesh(true)` и полную matrix; новый capture не нужен.

Это подтверждение устройства кода. Ни один пункт не объявляет проверку в шлеме или двухклиентный PASS.

## Решения и незакрытые вопросы

**Зафиксировано текущими task docs:** Hand Pose Fit — контакт с предметом; HRQ — риг/скиннинг;
общая геометрия не зависит от двух анализаторов; сохранить MEF `_eyesBaseHeight=1.7202504`;
динамический вход Puppet и владение MEF до дальнейших этапов требуют согласования [S13].

**Решения другого checkout, не текущая реализация:** образец сообщает запрет перехвата огнестрела
через ManipulationConstraints. В этом checkout GrabRules такой ветки нет. Это пример необходимости
фиксировать ревизию источника, не основание включать оружейную базу в обязательное чтение эксперта.

**Не установлено:** исчерпывающий список всех grabbable/аватаров и их качества; единый постоянный
writer данных хватов у сборщиков; поведение руки в любой фазе общего IK; сетевые гонки двух grabber;
реальная динамика MEF; текущий проход существующих тестов после rebase. HRQ0.5 — статический срез:
старые числа и тесты относятся к прежней базе, а не к новому runtime результату [S13].

## Минимальная база знаний

Предложение будущего тематического каталога `Docs/grips/`; ничего туда сейчас не переносится.

| Документ | Назначение/режим чтения |
|---|---|
| README.md | Короткий вход, область, владельцы соседей, карта источников/уровней доказательства |
| architecture.md | Model/rig/import → Native/Sdk integration → mapping/universal axes/rig info → pose descriptors → snap/grab/IK → capture/сеть; читать перед изменениями |
| invariants.md | Один state/pose writer, GUID coverage, обе руки, authority/replay, frame/scale, несовпадение preview/runtime; читать перед проектированием/ревью |
| decisions.md | Только принятые решения: дата, кем, область, причина, статус/заменённое решение, ссылка на исходный event/commit; проверить перед вопросом |
| diagnostics.md | Каталог HRQ/Fit/Workbench/SDK tools, входы/API/артефакты и чтение отчётов; дерево pose vs model/rig/weights vs import/integration vs runtime; искать конкретный симптом |
| vision.md | Правильная форма/деформация кистей, позы и контакт, воспроизводимый диагноз и понятный маршрут настройки; обе руки и реальные движения; критерии результата |
| workflow.md | Ссылка на AGENTS/хаб, делегирование, пакет Unity, scope доказательств и пользовательская приёмка; без второй копии протокола |
| roadmap.md | Открытые пробелы/зависимости, ссылка на живой plan.json; не независимая машина статусов |

Не заводить отдельные большие справочники по каждому виду оружия/магазина. Для редкого SDK случая
достаточна адресная карточка диагностики. Профиль читает README/architecture/invariants/decisions;
остальное ищет по затронутой ветке. Код/serialized asset — факт конфигурации; журнал — принятое требование;
plan/хаб — scope и статус; датированный отчёт с SHA/GUID/входом — доказательство измерения.

Обновление KB: новое принятое решение — новая запись; изменение lifecycle/ownership — архитектура
в том же разрешённом этапе; новый симптом — diagnostics с ревизией SDK. Исторический отчёт не
переписывать как текущий PASS. Документация в Docs, установка профиля и маршруты в общих индексах —
отдельный согласованный этап. Постоянную память не создавать и не обновлять без прямого запроса пользователя.

Контракт со смежным экспертом должен назвать канал/писателя, факт/данные, момент потребления,
authority/replay, версию и проверки обеих рук/observer. Это существенно для предметных адаптеров,
Puppet input, haptics, сетевой калибровки, loadout и выдачи из арсенала.

## Проверки и дальнейший выбор

В этом исследовании выполнены чтение исходников/документов, проверка linked-worktree/HEAD/dirty state,
сопоставление профильных документов и точек входа. Unity, сборка, тесты, prefab аудит, broker/Quest
и два клиента не запускались. Никакие игровые файлы, планы, Docs, профили установки или память не менялись.

Для последующей реализации выбрать существующие проверки по области: GrabPoseCoverage/GrabProximity,
TwoHandGrabPolicy/GunTwoHandGrab/GunTwoHandAim, SupportGrip/WeaponPartGrab/AnchoredItemGrab,
HandPoseFitGeometry/SdkHandPreviewParity/SdkPreviewBinding, WeaponDropPhysics. Названия оружейных
проверок здесь обозначают полезные текущие случаи предметов, а не обязанности WeaponSystem.
Результаты этих проверок сейчас неизвестны. До приёмки новой игровой логики — компиляция и существующие
проверки; после приёмки — тесты подтверждённого поведения в той же задаче.

Главные риски: второй писатель руки/предмета; молчаливый default по GUID/pose; объявление preview или
старого статического отчёта доказательством реального хвата/сети. Все три должны быть в обязательных правилах профиля.

Цель пользователя уже определена: диагност кистей/модели/поз и существующих инструментов.
Формальных вопросов для анализа нет. Установка, модель/skills/платформенный формат,
области записи и межзадачные контракты остаются отдельной согласованной работой.
