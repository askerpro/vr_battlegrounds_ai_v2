# План инспектора аватара

> План реализации выполнен в editor-коде; текущее устройство и проверенные пределы — в [редакторе аватара](../avatar-editor-workbench.md). Все 42 команды перенесены по прямому поручению пользователя. Пользовательская приёмка, реальный Blender/SDK-маршрут и восемь отказов текущих prefab тестов остаются открыты. Коммит разрешён пользователем 2026-10-05.

**Цель:** одно окно для выбранного аватара, в котором видны его состав, доступные функции и последствия каждого действия.

**Архитектура:** dockable EditorWindow по образцу арсенала и блокаута, с контекстом аватара и небольшими панелями. Существующие вычислительные backend сохраняются; MenuItem перестаёт определять цель и запускать неявные batch. Общая инфраструктура записи используется совместно с арсеналом через совместимые адаптеры.

**Среда:** текущая Unity 6/URP, существующие UltimateXR/Mirror и Editor GUI проекта. Новые Editor-классы только в `Assets/Editor/VR_Battlegrounds/`; runtime-компонент ради интерфейса не нужен.

**Основание:** [аудит инструментов](../audit/avatar-editor-tools-audit-2026-10-04.md), [окно арсенала](../Arsenal/editor-workbench-plan.md), [редактор блокаута](../level-design/blockout-editor.md), [инструмент посадки кисти](../hand-pose-fit-tool.md), маршрут `.claude/skills/setup-avatar/`. Предлагаемые имена классов/сигнатуры ниже ещё не являются существующим API.

## Пользовательский поток

Открыть `Tools/VR Battlegrounds/Avatars/Редактор аватара`. Вверху — поле цели, готовая иконка, тип контекста и ссылки на prefab/AvatarData/источник. Рядом — «Следовать выделению» и «Закрепить». Выбранная дочерняя кость или renderer разрешается в свой аватар. При нескольких кандидатах окно просит выбрать конкретного; первый аватар сцены автоматически не берётся.

| Выбранное | Поведение |
|---|---|
| AvatarData | Разрешить его prefab, показать данные скина и потребителей |
| Prefab asset аватара | Показать состав, цепочку bases/variants и возможности; для SDK-редактирования поз нужен экземпляр |
| Scene instance или его дочерняя часть | Работать с этим экземпляром, показывать источник и overrides; сцену не сохранять автоматически |
| Prefab Stage | Показывать текущий stage и изменения; инструменты редактирования stage используют Undo, прямую запись того же файла извне блокировать |
| FBX | Перейти к подготовке источника; до появления игрового аватара функции loadout/HUD недоступны |
| AvatarRegistry | Открыть список и выбор записи; ghost явно отделён от выбираемых скинов, batch не запускается |
| Нет подходящей цели | Пустое состояние с полем выбора, без поиска/массовой настройки в фоне |

Связь выбранного объекта с prefab и владельцем тела отображается явно. Аватар варианта и база, которую будет менять генератор, могут отличаться. Имя служит подписью, идентичность определяется asset GUID/path и идентичностью экземпляра. Если несколько AvatarData указывают на один prefab, данные скина выбираются отдельно.

### Разделы

| Раздел | Содержимое | Основной сценарий |
|---|---|---|
| **Обзор** | Иконка/AvatarData, prefab chain, источник, тип кистей, rig/controller/camera/network composition, статусы checks и потребители | Понять, что за аватар, чего не хватает, что будет затронуто |
| **Подготовка** | Родные кисти / SDK кисти, вход FBX, выход rig/game variant, Blender-путь машины, таблица этапов | Добавить модель через проверяемый конвейер |
| **Руки и позы** | Реальные риги кистей/grabbers, собственные и унаследованные позы, SDK editor, источник поз, предмет/grab point/side/Blend, HandPoseReview | Настроить хват конкретного аватара на конкретном предмете |
| **Тело** | Renderers/локально скрываемая голова, mesh/LOD, цвета/материал, ноги/locomotion owner, хитбоксы, производные corpses | Проверить и обслужить геометрию/композицию тела |
| **Оснащение** | Карманы и зоны, fingertip references, wrist HUD/эталон установки, pending runtime capture | Настроить игровые взаимодействия выбранного аватара |
| **Обслуживание** | Явные batch по выбранным записям, базы кистей, системный Ghost, общий locomotion, global masks/layers, migration/recovery | Выполнить операцию, область которой шире одного аватара |

Сначала показывать доступные постоянные функции; advanced и миграции свернуть. Недоступное действие остаётся видимым с точной причиной: например, «SDK editor требует экземпляр», «нет совместимого finger rig», «эта сборка ног поддерживает только рецепт Cyborg». В узком dock вкладки переносятся либо используют вертикальный список; контент имеет общий scroll. Тяжёлые превью строятся по запросу и кэшируются, Repaint не запускает BakeMesh/Blender/SHA.

У строки действия — название результата, цель/owner, вид изменения и зависимости. Для сложной записи есть подготовленный план и применение; для обычного редактирования числового поля достаточно штатного Undo. Нажатие пользователем ясной кнопки применения — действие в UI, дополнительное подтверждение агента на каждую рутинную операцию не требуется.

## Владение данными и пределы

1. SDK `UxrAvatarEditor` и `UxrHandPoseEditorWindow` остаются владельцами низкоуровневого редактирования. Окно открывает `UxrHandPoseEditorWindow.Open(instance, pose)`; SDK отклоняет prefab assets. Создание рабочего экземпляра — отдельная видимая операция с Undo и cleanup, не скрытая инстанциация при просмотре.
2. Поиск/вкладки/выделение/показ отчёта не вызывают setter, `Configure`, `SaveAssets`, reimport или builder. Read-only checks возвращают finding с контекстом, а не чинят обнаруженное.
3. Любой writer получает цель из захваченного контекста. `Selection`, `GameObject.Find("AutoSetupAvatarTarget")` и первый `UxrAvatar` не являются входом внутреннего backend.
4. Локальные изменения экземпляра/stage и запись prefab на диск — разные операции. Stage редактируется стандартными средствами Unity с Undo; окно не применяет override всей иерархии без перечисления полей. Asset writer не перезаписывает открытый stage.
5. Общие owners/descendants вычисляются одним resolver. Снятие override ног, rebase и регистрация поз показывают весь граф зависимых вариантов; унаследованные позы не выдаются за собственные.
6. Writer поз/точек хвата один для арсенального и аватарного UI. Source extractors остаются отдельными; target hand rig, pose owner и конкретный grabbable — явные параметры. Окно не меняет SDK default grip всем аватарам вслед за одним выбранным.
7. Реестр/команды и сетевой manager меняются явно. Чистая проверка spawnPrefabs отделена от синхронизации; manager задан канонической ссылкой принятой системы, не первым найденным prefab. Ghost сохраняет отдельную системную роль.
8. Runtime вкладки по умолчанию диагностические. Захват после SDK update и зоны локального аватара — узкие явно запрошенные операции; окно не начинает/останавливает человеческий Play Mode. Pending snapshot хранит происхождение и обновляется одним владельцем; после выхода предлагается план применения к исходному prefab.
9. Проверки нового аватара охватывают правила AvatarLoadout/PrefabComposition, GameTags, ссылки HUD/teleport/NetworkIdentity и локально скрываемую голову. UI не вводит второй набор норм вместо проектных контрактов.
10. Все логи обслуживаемого кода — `GameLog`. Никакой правки SDK для открытия существующего редактора не требуется. При будущей необходимой правке SDK действуют sdk-patches/mirror-patches.

Полезность позы оценивается по фактической геометрии и отчётам HandPoseReview. Автоматический «балл качества», допустимые зазоры, признание двух разных кистей совместимыми и принудительная автоподгонка не входят в первый срез. Числа отчёта сохраняют признаки достоверности.

## Предлагаемые контракты

`AvatarEditorContext` содержит kind, корень экземпляра либо prefab/source GUID/path, выбранный AvatarData, source model, owner prefab и цепочку зависимостей, номер версии контекста. Разрешение неоднозначной цели возвращает список кандидатов/причину; не меняет Selection и не создаёт объект. Unsaved scene object остаётся целью текущей сессии; после reload/уничтожения нужен новый контекст.

```csharp
bool AvatarEditorContextResolver.TryResolve(
    UnityEngine.Object selected, out AvatarEditorContext context, out string reason);

AvatarInspectionReport AvatarInspection.Read(AvatarEditorContext context);

AvatarOperationSession AvatarEditorActions.Prepare(
    AvatarEditorContext context, IAvatarEditorOperation operation);

void AvatarEditorActions.Apply(
    AvatarOperationSession session, Action<AvatarOperationResult> completed);

interface IAvatarEditorOperation
{
    string Id { get; }
    AvatarOperationScope Scope { get; }
    AvatarOperationPlan Describe(AvatarEditorContext context);
    AvatarInspectionReport Validate(AvatarOperationPlan plan);
    AvatarOperationResult Execute(AvatarOperationPlan plan);
}
```

`Describe/Validate` чистые. Plan фиксирует read/write paths, `.meta`, parent/base зависимости, выходы, shared outputs, сцену/stage, выбранный профиль, источник/предмет/позу и версию. Session содержит Plan, состояние Preparing/Ready/Running/Cancelled/Failed/Completed и progress/cancel. Prepare/Apply возвращают управление GUI; очередь работает вне OnGUI, callback результата приходит на главный поток. Хеширование файлов и ожидание Blender выполняются без блокирующего WaitForExit на GUI-потоке; Unity API и запись Unity-объектов остаются на главном потоке. Аренда покрывает только подготовленный Unity-пакет и его необходимое завершение, а не ожидание подтверждения плана.

`AvatarOperationResult` содержит статус, причину, фактически изменённые пути/объекты, partial changes и report path. При отказе шага следующие writer не запускаются. Многофайловые старые builders не объявляются атомарными: пока staging не внедрён, частичная запись явно перечисляется, повторное применение требует нового плана.

### Структура файлов

| Путь относительно Assets/Editor/VR_Battlegrounds | Ответственность |
|---|---|
| `Avatars/Workbench/AvatarEditorWindow.cs` | Окно, target/header, вкладки, lifecycle подписок |
| `Avatars/Workbench/AvatarEditorContext.cs` | Контекст, kind/version, resolver; затем выделить resolver при росте |
| `Avatars/Workbench/AvatarInspection.cs` | Чистый composition report и findings; без builder |
| `Avatars/Workbench/AvatarPrefabOwnership.cs` | Owner/descendants и ancestry, единый resolver для legs/hitboxes/poses |
| `Avatars/Workbench/AvatarEditorActions.cs` | Plans/results/очередь операций и адаптер общего исполнителя |
| `Avatars/Workbench/AvatarOverviewPanel.cs` | Данные/наследование/отчёт, отображение готовой иконки |
| `Avatars/Workbench/AvatarPreparationPanel.cs` | Два профиля кистей и состояния конвейера |
| `Avatars/Workbench/AvatarHandsPanel.cs` | SDK editor, источник поз и переход к существующему HandPoseReview |
| `Avatars/Workbench/AvatarBodyPanel.cs` | Renderers/skins/colors/legs/hitboxes; операции через backend |
| `Avatars/Workbench/AvatarEquipmentPanel.cs` | Pockets/fingertips/watch, pending runtime snapshot |
| `Avatars/Workbench/AvatarMaintenancePanel.cs` | Batch/system routes и advanced migrations |
| `Common/EditorOperationLease.cs`, `Common/EditorFileSnapshot.cs`, `Common/EditorOperationGuards.cs` | Переиспользуемая часть ArsenalEditorActions/FileSnapshot: token lease, SHA worker, чужие изменения; старые арсенальные фасады сохраняют API |

Детальные подпанели создавать при переносе их функции, не писать пустую инфраструктуру заранее. Рецепты сначала остаются типизированными Editor-данными/существующими таблицами; отдельный runtime carrier или универсальная система генерации всех моделей не нужны.

## Этапы реализации

### 1. Контекст, чистый обзор и открытие существующих редакторов

**Создать:** Window, Context, Inspection, OverviewPanel и HandsPanel из таблицы. **Использовать:** текущие AvatarData/AvatarRegistry, `UxrHandPoseEditorWindow.Open`, HandPoseFitWindow. **Документы:** этот план и тематическая документация обзора.

- [ ] Подготовить временный Editor probe с двумя разными аватарами, AvatarData, child selection, prefab asset, FBX и пустым выделением. Записать до/после hashes всех затронутых файлов, scene dirty/object counts и ownership Selection. Проверить отрицательный контроль намеренно меняющим fixture callback; затем убрать его.
- [ ] Реализовать resolver/контекст, готовую иконку и findings без записи; не создавать временный scene avatar при выборе asset.
- [ ] Добавить явный SDK editor route для instance, отдельную команду создания рабочего экземпляра для asset; существующий HandPoseReview открывать с переданным target через небольшой `OpenFor(UxrAvatar)` adapter, сохранив `Open()`.
- [ ] Проверить все вкладки/поиск/repaint/Undo/reload на отсутствие записей. Закреплённая цель сохраняется при выборе оружия; неподходящее выделение не заменяет её скрытым fallback.
- [ ] Под Unity lock получить импорт/консоль и AndroidCompileGate PASS, удалить fixture/рабочие объекты и сразу отпустить lock. Пользователь оценивает окно в dock и обе кисти в SDK editor.

**Критерий:** полезный read-only инспектор и явный доступ к существующей работе с позами. Никакой миграции и массовой пересборки. Это первый самостоятельный срез.

### 2. Общая защита операций и индивидуальное оснащение

**Создать:** Common-файлы, Actions, EquipmentPanel и PrefabOwnership по потребности. **Изменить:** арсенальные фасады `ArsenalEditorActions.cs`/`ArsenalFileSnapshot.cs`; `AvatarFingertipSetup.cs`, `AvatarPocketSetup.cs`, `WristWatchInstaller.cs`, `FixAvatarRenderers.cs`, `AvatarRegistryEditor.cs`. **Зависимость:** этап 1 и API общего executor.

- [ ] Выделить существующие lease/guards/snapshot primitives из арсенала с сохранением его публичных contracts и async поведения. Не клонировать второй независимый lock executor. Если выделение задевает нерешённое владение стендом арсенала, сначала решить только этот контракт; read-only этап остаётся полезным.
- [ ] Вынести из MenuItem узкие overloads с явной целью и структурированным результатом: fingertips, pocket setup, renderers, watch/reference. Старые entry points временно оставлять совместимыми; UI не вызывает их через ExecuteMenuItem/подмену Selection.
- [ ] Отделить чистую network validation от sync с явным manager. Изменение массива реестра больше не должно скрыто сохранять найденный manager; способ сохранения существующего списка spawnPrefabs остаётся принятой сетевой системы.
- [ ] Добавить Prepare/Apply для файловых операций, guards чужого lock/Play/import/stage/dirty assets и изменение контекста после подготовки. Scene/stage изменения идут одним Undo group без SaveAssets.
- [ ] Проверить чужой token и смену lease, изменённый source/parent `.meta`, отказ середины chain, новый Selection после Prepare, dirty scene и несохранённый prefab stage. Отчёт каждого writer перечисляет фактические изменения; отмена прекращает новые шаги.
- [ ] Получить Unity/Android PASS, регрессию существующего окна арсенала и relevant текущих tests. Перед передачей пользователю отпустить lock.

**Критерий:** оснащение выбранного аватара обслуживается без скрытого batch и потери чужих изменений; арсенал сохраняет прежние контракты. Общая инфраструктура — отдельная зона повышенного риска, требующая ревью.

### 3. Настройка в runtime и сохранение зон

**Изменить:** `PocketZonesPrefabWriter.cs`, EquipmentPanel/Actions. **Использовать:** текущий HandPoseFitRuntimeCapture без второго SDK update subscriber. **Зависимость:** этап 2.

- [ ] Оставить один сервис pending snapshot. Capture принимает явный local avatar, показывает исходный prefab GUID/path и ограниченный список полей; захват не означает сохранение.
- [ ] После выхода из Play перейти в состояние «Есть снимок», проверить текущую идентичность/свежесть prefab и подготовить Apply. Изменённый prefab требует нового плана/явного разрешения конфликта; выбор другого скина не перенаправляет снимок.
- [ ] Проверить Capture → выход → Apply, отмену, reload, уничтожение аватара, повторный Capture и конфликт исходного файла. Сохранить existing narrow field transfer и защиту Mirror assetId.
- [ ] Проверить, что просмотр runtime не меняет аватар и не начинает Play Mode. Серии HandPoseReview используют уже существующий queue/cancel API и сохраняют признаки статического/игрового источника.
- [ ] Unity/Android и существующие релевантные проверки; пользователь проверяет карманы/зоны и чтение HUD в шлеме. После подтверждения уточнить постоянные avatar composition ожидания.

**Критерий:** полезная настройка из шлема сохраняется контролируемо, отложенная операция не пишет неожиданно при выходе из игры.

### 4. Конвейер создания и работа с моделями

**Создать:** PreparationPanel и BodyPanel. **Изменить:** CustomAvatarPipelineMenu, Core/HandsIntegration/ControllerAndCamera/FinalizeRigMapping/CreatePrefab/HandPosesSetup, ApplyEyeMapping, три Blender helpers, ModularGloveBoneMapper, SkinnedMeshTransplant, TeamColorVariantBuilder. **Зависимость:** этап 2; gameplay variant contract `/setup-avatar`.

- [ ] Ввести `AvatarBuildProfile` с hand mode Native/Sdk, source GUID/path, rig output, game variant output и принятой hand base. Отличать подготовленный rig от игрового варианта и регистрации.
- [ ] Передать один captured target во все шаги; вернуть статусы и остановить chain при первом отказе. Для Native исключить ампутацию/BigIKHand/finalize SDK mapping; для Sdk выполнить только предусмотренные профильные шаги.
- [ ] Blender пишет подготовленный output, исходник остаётся отдельным. Применимость костей/весов/уже выполненного шага проверять до экспорта. Blender executable — EditorPrefs; не помещать машинный путь в git asset.
- [ ] Выделить общий bone resolver из glove/transplant, с duplicate/missing/bind pose diagnostics. Сохранить разные политики additive glove и replacement mesh/LOD/head hide. MEF naming/default removals становятся явным рецептом.
- [ ] Параметризовать source/mask/material/outputs цветового variant; изменение исходного материала отражать в плане. Не применять MEF shader mask к произвольной модели автоматически.
- [ ] Проверить одну модель каждого пути, повторную сборку в те же выходы, разные FBX при уже существующем target, отказ missing bones и существующие output assets. Тестовый отказ шага обязан остановить последующие writers.
- [ ] Проверить компиляцию/Android, композицию, native/SDK Grab и Pointing на реальной кисти, локально скрываемую голову, fingertip/HUD/teleport. Новые игровые ожидания закреплять только после пользовательского принятия.

**Критерий:** создание нового аватара идёт по одному понятному маршруту, источники и база не меняются из-за неверного fallback. Обобщение реализует измеренную совместимость, не универсальный retarget всех скелетов.

### 5. Владельцы тела, позы и системные генераторы

**Изменить:** AvatarLegsSetup/LegsRigBaker/MixamoLocomotionSetup, CyborgLegsBuilder, HitboxBuilder, CorpseBuilder, GhostAvatarBuilder, AvatarHandBases, HandsPackPoseImporter; adapters extractors/GripAligner по необходимости. **Создать:** MaintenancePanel и недостающие Body/Hands операции. **Зависимость:** этап 2; согласование с владельцами текущей legs/HandPoseReview работы.

- [ ] Единым PrefabOwnership разрешить body owner/descendants и hand pose owner. Выделить узкие `BuildFor/SetupOwner` routes без неявного вызова global rebuild; batch работает по конкретному списку.
- [ ] В Hitboxes показать отдельно avatar owner, corpses/ghost, projectile masks/layers. В Cyborg recipe зависимости видны до записи; генератор остаётся специализированным.
- [ ] В Legs отделить постоянную настройку и shared controller от одноразового удаления obsolete components. Не повторять второй native-legs pipeline и не менять принятую механику сидения этой UI задачей.
- [ ] Pose importer принимает целевой rig, owner поз, конкретный grabbable/grab point/side, source recipe. Существующие weapon builders и UI арсенала используют тот же writer. Preview/оценка не сохраняют новую позу.
- [ ] Проверить варианты с общим owner, SDK/Native базы и несовместимую кисть, сохранение других точек хвата/аватарных записей, вывод зависимых файлов и прежние prefab цепочки.
- [ ] Unity/Android и текущие AvatarHandPoseChain/HandsPack/Kinemation/RegisteredAvatarBody/Hitbox/Corpse/Ghost проверки. Механические изменения и новые thresholds не подмешивать к переносу UI.

**Критерий:** регулярные rebuild доступны, их область честна; полезные специализированные рецепты сохранены, дублирующих владельцев состояния нет.

### 6. Сокращение меню и приёмка

**Изменить:** MenuItem declarations переносимых инструментов, совместимые legacy wrappers; документацию `/setup-avatar`, актуальные тематические Docs, README/CHANGELOG. **Зависимость:** функции этапов 1–5 приняты для соответствующего среза.

- [ ] По каждому из 42 прежних пунктов подтвердить новый путь/системный batch/архив. Отсутствие C# references не считать основанием удаления: проверить документы, ручные menu workflows и script GUID consumers.
- [ ] Убрать дубли InspectAvatar/CheckSkeletons/ApplyEyeMapping из верхнего меню. Архивировать PlayerBase constructors, fixed soldier bootstrap и RefineBackPocket только после проверки, что принятое создание не зависит от них.
- [ ] Общие hand base migration и calibration reset оставить в обслуживании только при подтверждённой пользе. Базы/outputs не пересоздавать ради сокращения списка.
- [ ] Итоговое меню: «Редактор аватара» как постоянный вход; migration/diagnostic shortcuts оставлять лишь с ясным назначением. Не удалять backend, на которые ссылаются оружейные сборщики.
- [ ] Получить свежие Editor/Android PASS и relevant EditMode результаты; провести пользовательский walkthrough одного SDK и одного Native аватара в Unity, затем шлем. Постоянные тесты принятой композиции обновить в той же задаче; код коммитить только после пользовательской проверки по AGENTS.md.

**Критерий:** все сохранённые функции найдены из окна; обычное меню не содержит конкурирующих реконструкций PlayerBase и скрытых batch.

## Фокус ревью и проверок

| Сценарий | Ожидаемое поведение | Где проверять |
|---|---|---|
| Два аватара/другой FBX/child selection/смена Selection после Prepare | Captured target сохраняется; неоднозначность и старый план блокируются, чужой экземпляр не выбирается | Этапы 1, 2, 4 |
| Variant наследует позы/ноги от общей базы | UI показывает owner и потомков; выбранный скин не маскирует изменение общей зависимости | Этапы 2, 5 |
| Чужой Unity lock, stage, dirty assets, SHA изменился, cancel/reload | Writer не запускается; не снимается чужой token, не сохраняются чужие данные; завершение своей асинхронной операции до release | Этап 2 и арсенальная регрессия |
| Pending runtime snapshot после смены аватара/выхода/reload | Применение только к исходному prefab и ограниченным полям, конфликт виден; Play lifecycle принадлежит существующим системам | Этап 3 |
| Native руки, отсутствующие/повторяющиеся bones, partial writer failure | SDK-ампутация недоступна Native; нет записи несовместимого mesh; следующий шаг не запускается, partial changes перечислены | Этапы 4, 5 |

Новая/изменённая игровая механика до проверки пользователем не закрепляется постоянными тестами. Для технических контрактов UI использовать временные probes с отрицательным контролем и существующие regressions; старые ожидания не переписывать для получения зелёного результата. При изменении аватарных assets перечислить затронутые AvatarLoadoutTests/PrefabCompositionTests, после принятия обновить их вместе с принятым контрактом.

## Альтернативы и цена

**Рекомендуется окно с контекстом.** Оно повторяет фактический тип UI арсенала/блокаута, обслуживает prefab/instance/FBX/data без нового runtime-компонента и сохраняет SDK inspector. Потребуются context resolver и явные адаптеры backend.

**Альтернатива — расширять CustomEditor UxrAvatar.** Удобно непосредственно при selection, но конкурирует с SDK editor и неудобно для исходного FBX, AvatarData и системных операций; перенос низкоуровневого editor SDK увеличит поддержку патчей. На первом этапе не рекомендуется.

**Альтернатива — окно, вызывающее старые menu handlers.** Быстро даёт видимый список, но сохраняет незафиксированную цель, hidden writes и цепочки без остановки. Допустимо только для безопасного открытия другого окна; writer так не подключать.

Стоимость сосредоточена в разделении массовых builders и общего executor, а не в рисовании вкладок. Этап 1 даёт самостоятельную пользу; каждый последующий срез должен проходить свою проверку. Обобщение Cyborg mesh surgery и автоматический retarget/quality scoring всех кистей — отдельные возможные проекты, в этот план не включены.

## Текущий результат

Готовы аудит и этот предложенный план. Инспектор, адаптеры и перенос меню пока не реализованы. Документы проверяются на полноту каталога/ссылки/кодировку; будущий UI и безопасность writers потребуют перечисленных Unity-проверок.
