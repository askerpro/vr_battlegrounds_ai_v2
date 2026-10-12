# Предотвращение renderer ownership и утечек state sync

Статус: **ПРИНЯТО ЧАСТИЧНО** решением пользователя 2026-10-12 (через сопровождающего, событие 4651; оценка
avatar-ik 4623/4627, вердикт network 4625). Исходный DRAFT 2026-10-09.
- **A — принято как основа в упрощённом виде.** Staging-префаб с backup/promotion/restore (OwnershipPlan, задача 3)
  не делается: Validate до сохранения, откат через Git. Реализация — задача `avatar-renderer-ownership` в worktree
  avatar-ik после `legs-ik/pose-set`.
- **B — отложено.** Возврат — когда появятся вызывающие, которым нужна транзакционность, а не чистая глубина.
- **Вместо B — вариант C.** C2 (network): единая LIFO-стопка кадров вместо статического `SyncCallDepth`, End/Cancel
  снимает только свой кадр, сторож на границе кадра в `UxrManager` (лог, очистка, счётчик); формат и момент
  доставки событий не меняются. C1 (avatar-ik): `UxrAvatar.RenderMode` проверяет ссылки до BeginSync, при исключении
  CancelSync, восстановление режима и видимости, rethrow. Оба — после текущих задач владельцев.
База исследования: `f33e5f4e`. Это архитектурное продолжение принятого минимального ремонта.
Планы: [OwnershipPlan.md](OwnershipPlan.md), [StateSyncPlan.md](StateSyncPlan.md).

## Два независимых результата

A. Состав тела задаётся явно; все editor-сборщики используют один контракт записи/проверки.
B. Отказ синхронизируемой операции закрывает её frames и не публикует её событие.
Каждое направление выпускается отдельно, со своей приёмкой и отдельной областью записи.
Направление B не должно ожидать готовности A: исправление владения не делает SDK exception-safe.

## Фактическое состояние

- `UxrAvatar.cs:77,411`: один сериализованный `_avatarRenderers`, наружу `IEnumerable<Renderer>`.
- `FixAvatarRenderers.cs:38-61`: непустой список сохраняется; пустой собирается из descendants;
  отбрасываются invalid, foreign-avatar, hand-integration и EditorOnly ссылки.
- `UxrAvatar.cs:834-838`: SDK fallback берёт все skins без hand integration.
- `UxrAvatar.cs:1448-1457`: OnValidate применяет fallback к пустому списку.
- `UxrAvatar.cs:1280-1350`: Awake использует список для offscreen/bones; нового сбора здесь нет.
- `UxrAvatar.cs:1706-1714`: BeginSync, затем `_renderMode` меняется до обхода renderers.
- `UxrAvatar.cs:1718` и далее: в той же операции изменяются controller models/object lists.
- `UxrStateSyncImplementer.cs:64`: depth общий статический; `..._1.cs:317`: options stack локальный.
- `..._1.cs:166-201`: Begin/Cancel не проверяют соответствие общей вложенности владельцу.
- `..._1.cs:251-260`: End уже уменьшает depth в finally при исключении подписчика.
  Исключение внутри операции до End этот механизм не закрывает.
- `UxrManager.cs:1794-1798`: отбор событий зависит от live depth, включая IgnoreNestingCheck.
- `NetworkStateRelay.cs:195-245`: после фильтров/ворот событие сериализуется и отправляется.
- `AvatarStateEventGate.cs:70-88,177-189`: отдельные очереди ждут avatar admission;
  это downstream-механизм, не транзакция SDK и не место для отмены failed operation.

Пути выше относительно Assets: SDK — `ThirdParty/UltimateXR/Runtime/Scripts/`;
редактор — `Editor/VR_Battlegrounds/Avatars/`; relay/gate — `Scripts/Network/`.
Исторические промежуточные числа в Details.md не становятся архитектурными требованиями.
Принятый Cyborg имеет 15 body refs; 20/EditorOnly не является принятой альтернативой.

## A: минимальный контракт владения

Единственная runtime-коллекция остаётся `_avatarRenderers`.
Новый proposed `AvatarRendererRole` — метаданные роли ветки, без коллекции renderers:
`Body`, `Controller`, `Accessory`, `Authoring`. Ближайшая метка на пути к avatar задаёт роль.
Root avatar не получает Body по умолчанию: иначе прежний сбор всех descendants вернётся.
Body разрешает всю постоянную геометрию ветки, включая неактивные body LOD.
Accessory/Controller/Authoring на дочерней ветке перекрывает Body родителя.
UxrHandIntegration и другой ближайший UxrAvatar всегда исключают renderer из тела.
EditorOnly — дополнительное исключение, а не обязательный способ удалить старую Ghost-копию.
Имена `Ghost`, `Geo`, тип skin и наличие LODGroup не определяют владение.

Proposed `AvatarRendererOwnership` в editor выполняет Build, Validate и Apply.
Build выдаёт временный snapshot; Apply — единственный product-editor writer `_avatarRenderers`.
Validate только читает: missing, duplicates, чужой owner, роль не Body,
неполное покрытие размеченного тела, invalid body LOD и конфликт role boundaries — ошибки.
Не вводятся runtime scanner, динамический registry или второй изменяемый renderer list.
Метаданные ролей не управляют enabled; видимость accessories остаётся у текущих owners.

Proposed SDK `UxrAvatarRendererCollectionMode` имеет `LegacyAutomatic = 0`, `Explicit = 1`.
Старые SDK-ассеты по умолчанию сохраняют поведение; зарегистрированные product-аватары Explicit.
OnValidate при Explicit никогда не собирает список; пустое тело PlayerBase допустимо явно.
Валидация зарегистрированного игрового аватара требует непустое размеченное тело.
SDK не ссылается на product role-компонент и не пытается размечать тело сам.
Поведение GetAllAvatarRendererComponents для rig inference не менять в этой задаче.

## Сборщики и варианты

`AvatarMaintenanceTools.cs:36-48`: сохранение проверяет Mirror ID, но не renderer ownership.
`...:111`: new avatar вызывает FixAvatarRenderers после добавления модели.
`ExtractPlayerBase.cs:22-28`: уничтожает Cyborg и сразу сохраняет PlayerBase.
`GhostAvatarBuilder.cs:67-74,117-119`: Strip удаляет конкретный Cyborg/Ghost до сохранения.
Это подтверждённые editor-пути; runtime уничтожение всех часов/лучей здесь не установлено.
`SkinnedMeshTransplant.cs:240-249`: ещё один writer: kept + created; заменить делегированием.
После удаления/трансплантации Build/Apply выполняется до save, Validate — до и после reload.
При ошибке не записывать цель; изменения строятся в preview/prefab contents с cleanup finally.
Сначала save во временный staging prefab, reload и Validate; лишь затем promotion в target.
Перед promotion сохранить scoped backup target bytes/meta и существующий GUID/variant связи;
при ошибке target save/import/reload восстановить backup и проверить повторный reload.
Новый target при failed promotion удалить вместе с созданным meta; cleanup staging в finally.
AssetDatabase save не считать атомарным; promotion/restore входит в тот же guarded Unity пакет.
Register Ghost допускается лишь после успешного ownership reload-check.
`AvatarScopedActions.cs:54-57,68-73`: renderer action и Mirror normalization сохраняют назначения,
но вызывают тот же owner-service, не дублируют правила.

Cyborg и MEF размечаются отдельно без унификации их Transform-иерархий.
Часы в MEF LODGroup не превращаются в Body: LOD валидируется после классификации.
Пять MeshRenderer Cyborg остаются вне body, пока owners не утвердят иной контракт.
Если renderer одновременно нужен SDK body и accessory owner, миграция останавливается:
сначала определить одного владельца enabled или явный существующий composition-контракт.

## B: минимальный контракт state sync

Proposed `UxrStateSyncScope` — sealed IDisposable token с явным Commit и implicit Abort.
Scope синхронный, используется на Unity main thread и не переносится через await/yield или кадры.
Поддержка параллельных/асинхронных операций не входит в этот фикс; entry audit проверяет callers.
Proposed `IsCompleted` означает terminal token, `HasCommitted` — принятую local commit-заявку:
false до frame/transaction validation и payload snapshot, true после них, до delivery.
Abort завершает token без HasCommitted; failure подписчика сохраняет HasCommitted=true.
Begin/End/Cancel и scopes используют одну глобальную LIFO frame-структуру:
frame содержит token identity, owner, options, begin depth и completion state.
Depth вычисляется из frames; отдельный local options stack перестаёт быть вторым авторитетом.
End/Cancel другого owner не забирает чужой frame; несовпадение диагностируется без Pop.
Dispose незавершённого scope закрывает собственные незавершённые descendants и себя,
помечает enclosing scoped transaction failed и отбрасывает её buffered notifications.
Двойной Dispose безопасен; Commit завершённого/неверхнего token запрещён.

Внутри scoped transaction уведомления StateChanged откладываются до успешного внешнего Commit.
Legacy End внутри scope также буферизуется: иначе IgnoreNestingCheck успевает отправить child.
Снаружи scopes legacy API сохраняет немедленную публикацию и options.
Буфер обязателен для обещания «outer abort не публикует successful child»;
это временная очередь событий, не renderer registry и не очередь NetworkStateRelay.
События сохраняют source nesting depth; proposed internal `SyncNestingDepth` в UxrSyncEventArgs
используется UxrManager вместо live depth для отбора buffered событий, без изменения wire schema.
Перед delivery frames закрыты; reentrant subscribers открывают независимую новую операцию.
Remote execution сохраняет IsInsideStateSync suppression; scoped Commit остаётся синхронным.
Frame identity и committed-event metadata раздельны: captured depth не является открытым frame.
Committed envelope фиксирует owner, options, depth, порядок и snapshot payload перед buffering.
Для RenderMode копировать object[] и List<UxrControllerInput>; enum/value values неизменяемы,
Unity refs сохраняют identity и проходят существующий downstream admission, не раннюю serialization.
Custom mutable event args нельзя буферизовать по одной ссылке: audit всех reachable nested events
определяет snapshot-контракт; unsupported mutable payload останавливает adoption caller до решения owner.
Deferred local StateChanged меняет момент наблюдения: observer видит committed итог, не child intermediate.
Это отдельный принимаемый этап B, с trace всех local observers; несовместимость — stop, не silent change.

Scope гарантирует чистые frames/queue и suppression до публикации, **не rollback игры**.
Caller обязан prevalidate входы, применить изменения и вызвать Commit только после успеха.
Если caller ловит inner exception, transaction всё равно failed; Commit outer отвергается.
Исключение подписчика после начала delivery уже не позволяет отозвать увиденное событие.
При нём очистить остаток буфера, сохранить чистый stack, пробросить исходную ошибку;
не повторять delivery автоматически. Гарантии атомарной доставки multicast/сети нет.
Commit фиксирует успех caller до delivery; subscriber failure не запускает gameplay compensation.
Nested child HasCommitted фиксирует local completion, а не публикацию enclosing transaction.
Outer abort удаляет buffered child event, но не откатывает уже совершённые child side effects;
caller внешней операции отдельно отвечает за prevalidation/compensation своего эффекта.

## RenderMode как первый caller

До scope проверить refs, controller inputs и targets без мутации.
Сохранить прежний mode и видимость конкретных затрагиваемых объектов как transient snapshot.
Менять `_renderMode` последним после успешного применения всех targets, затем CommitMethod.
При apply exception или Commit rejection до HasCommitted: Abort; восстановить surviving targets
и прежний mode; ошибку пробросить. Catch охватывает apply и Commit, но компенсирует только
при HasCommitted=false; после успешного commit ошибка delivery не откатывает mode/targets.
Компенсация destroyed targets невозможна; лог фиксирует исходный отказ и неполное восстановление.
Не обещать полный rollback controller setters до аудита их побочных эффектов.
Ограниченный audit до реализации: setters моделей и EnableObjectListLeft/Right,
reentrant callbacks и уничтожение targets; результат — таблица effects/compensation.
Если setter публикует необратимые эффекты, stop gate и отдельное решение owner SDK/devices.

## Альтернативы, миграция и откат

- Только сохранить список: принятому ремонту достаточно, но новые builders/OnValidate обходят контракт.
- Только body root: проще, но accessory под body и MEF LOD смешивают ownership; нужны role boundaries.
- Renderer marker на каждом объекте: допустим, но больше prefab overrides; branch metadata минимальнее.
- finally EndSync: публикует failed operation; finally Cancel без identity не защищает nested leak.
- Только scope без буфера: чистит depth, но не отменяет уже опубликованный nested event.
- Перевод всего SDK сразу: слишком широкий риск; сначала совместимый core и RenderMode.

Мигрировать A с report старого/нового состава; на Cyborg сохранить принятое множество из 15 refs.
MEF/другие variants принимаются по собственным body mappings, не по фиксированному общему count.
В B сравнить успешный legacy trace/options/remote echo suppression до и после.
SDK patch numbers резервировать только в реализации; два независимых patch при отдельных выпусках.
Откат — отдельный согласованный revert product/SDK/asset bundle, без rewrite origin/dev.
Убрать Explicit только вместе с корректным прежним списком; не вернуть fallback на смешанную ветку.

## Границы проверки

Этот DRAFT проверен чтением исходников; Unity, Android, Test Runner и Quest не запускались.
Исторические 19/19 относятся к минимальному ремонту, не доказывают предложенную архитектуру.
До новой runtime-приёмки: compilation и применимые существующие проверки, список отличий ожиданий.
После приёмки: новые regression expectations, фактический зелёный результат, accepted commit.
Общие Docs indexes/changelogs, SDK journals и чужие checkout не входят в scope.
