# Exception-safe state sync — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Закрывать sync frames при отказе, не публикуя failed scoped operation и её children.
**Architecture:** Одна frame stack для legacy/scoped API, commit-time notifications и explicit Abort.
Первый caller — RenderMode; caller отвечает за prevalidation и компенсацию игровых изменений.
**Tech Stack:** Unity 6, C#, UltimateXR state sync, Mirror relay, Unity Test Runner/NUnit.
**Spec:** [Design.md](Design.md).
**Статус:** ОТЛОЖЕНО решением пользователя 2026-10-12 (событие 4651, вердикт network 4625). Вместо плана —
вариант C (C2 network, C1 avatar-ik), см. статус [Design.md](Design.md). Исходный DRAFT 2026-10-09 сохранён как
справка для возможного возврата.

## Global Constraints

- Scope и его callers синхронны, на Unity main thread; await/yield/переход между кадрами запрещён.

- Новый task `sdk-state-sync-safety`, isolated worktree от свежего origin/dev и scope admission.
- Не зависит от OwnershipPlan; SDK UxrAvatar.cs — shared dependency, последовательная интеграция.
- SDK patch-reserve только при реализации; никаких прямых infra/self-repair или shared journals.
- Gameplay/avatar tests не писать и не переписывать до runtime-приёмки.
- Scope не обещает rollback side effects или атомарность multicast/network delivery.
- Новый logging через GameLog; preserve legacy wire format, authority filters и admission gate.
- Accepted commit после подтверждения, FF origin/dev; без force-push.

## Review Focus

- A→B nested frames разных owners: B abort не снимает чужой options frame.
- Successful child с IgnoreNestingCheck не отправляется, если outer operation затем failed.
- Exception подписчика/reentrant sync: frames чистые, сообщение не отправляется повторно.
- Legacy child пропускает End, scope Dispose удаляет его frame и запрещает outer Commit.
- RenderMode exception после части targets не оставляет requested mode при прежней видимости.

## Задача 1: bounded compatibility/effects audit

**Read:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/Components/UxrComponent.cs`;
`Core/StateSync/UxrStateSyncImplementer.cs`, `UxrStateSyncImplementer_1.cs`, `UxrStateSyncOptions.cs`;
`Core/UxrManager.cs`, `Avatar/UxrAvatar.cs`, referenced controller model/input setter files.
SDK paths выше относительно `Assets/ThirdParty/UltimateXR/Runtime/Scripts/`.
**Read:** `Assets/Scripts/Network/NetworkStateRelay.cs`, `AvatarStateEventGate.cs`.
**Output:** task contracts/sync-scope.md, reports/compatibility-effects.json.

- [ ] Зарегистрировать task/scopes; согласовать SDK/avatar/device owners и очередь UxrAvatar.cs.
- [ ] Fetch/rebase origin/dev; зафиксировать actual baseline patch в EndSyncState finally.
- [ ] Найти все чтения SyncCallDepth и прямые StateChanged subscribers в SDK/product code;
  подтвердить synchronous main-thread entry, остановить adoption при async/off-thread caller.
- [ ] Составить successful trace: source depth/options, local callback order, manager selection,
  Network/SaveReplay options и IsInsideStateSync suppression.
- [ ] Для reachable nested events составить payload snapshot mapping: enum/value/Unity refs,
  mutable collections/custom args; проверить observers, читающих промежуточную видимость.
- [ ] Проверить setter `_renderMode`/RenderMode, controller visibility и EnableObjectListLeft/Right:
  side effects, callback reentrancy, возможность prevalidate/restore surviving objects.
- [ ] Stop gate: если внешние callbacks требуют live SyncCallDepth при deferred delivery,
  согласовать explicit metadata migration с owner до изменения core, не вводить fake global depth.
- [ ] Stop gate: irreversible controller effects требуют отдельного caller-contract решения;
  не утверждать полноценную RenderMode компенсацию без аудита.

## Задача 2: frame authority и scope API

**Modify:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/StateSync/UxrStateSyncImplementer.cs`.
**Modify:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/StateSync/UxrStateSyncImplementer_1.cs`.
**Modify:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/Components/UxrComponent.cs`.
**Create:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/StateSync/UxrStateSyncScope.cs`.
**Proposed Interfaces:**

- `public sealed class UxrStateSyncScope : IDisposable`.
- `public bool IsCompleted { get; }`, `public bool HasCommitted { get; }`.
- `public void CommitState(UxrSyncEventArgs e)`.
- `public void CommitMethod(object[] parameters = null, [CallerMemberName] string methodName = null)`.
- `public void CommitProperty(object value, [CallerMemberName] string propertyName = null)`.
- `public void Abort()`; `public void Dispose()` abort незавершённого token.
- Implementer `public UxrStateSyncScope BeginSyncScope(Action<UxrSyncEventArgs> raiseChangedEvent,
  UxrStateSyncOptions options = UxrStateSyncOptions.Default)`.
- UxrComponent `protected UxrStateSyncScope BeginSyncScope(UxrStateSyncOptions options = UxrStateSyncOptions.Default)`
  передаёт существующий OnStateChanged; legacy Begin/End/Cancel signatures сохраняются.

- [ ] Резервировать SDK patch; frames с owner/token/options/depth становятся одним авторитетом.
- [ ] Legacy Begin добавляет frame; End/Cancel до mutation проверяет top owner и completion.
- [ ] Убрать competing `_optionStack`; depth вычислять из общего stack без reset-to-zero healing.
- [ ] Scope Dispose закрывает себя/незавершённые descendants, отбрасывает их events;
  enclosing scoped transaction marked failed, Commit failed outer запрещён.
- [ ] Abort/Dispose idempotent; non-top Commit и повторный Commit дают controlled exception.
- [ ] HasCommitted выставить только после token/frame/poison validation и payload snapshot,
  до delivery; rejection сохраняет false, delivery exception сохраняет true.
  IsCompleted true после Abort/accepted Commit; rejected active token закрывается Abort/Dispose.
- [ ] Nested child HasCommitted означает local completion: outer abort suppresses child event,
  но scope не компенсирует child gameplay effects автоматически.
- [ ] Снаружи scopes legacy event timing/options сохраняются; body exception legacy без scope
  ещё не покрыт — это явно ограниченный постепенный adoption, не «исправлен весь SDK».
- [ ] Compilation и applicable existing state authority/admission/teardown tests без переписывания.

## Задача 3: notification buffer и manager semantics

Самостоятельный stage `committed-notifications` с review SDK/network owners до caller adoption.
Переход local observers к committed timing принимается отдельно; failed review блокирует задачу 4.

**Modify:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/StateSync/UxrSyncEventArgs.cs`.
**Modify:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/UxrManager.cs`.
**Modify:** core implementer/scope из задачи 2.
**Proposed Interface:** `internal int SyncNestingDepth { get; set; }` в UxrSyncEventArgs;
не сериализуется, capture в момент End/Commit, fallback live depth только legacy unset value.

- [ ] Пока scoped ancestor открыт, End/Commit сохраняет callback+event+source depth в его buffer.
- [ ] Envelope snapshot фиксирует owner/options/depth/order; RenderMode object[] и controller List
  копируются, Unity refs не сериализуются до admission; custom mutable payload требует audited copier.
- [ ] Если reachable legacy event не имеет безопасного snapshot, stop adoption, не хранить его mutable alias.
- [ ] Успешный outer Commit сначала закрывает frames, затем delivers в прежнем End-order;
  child options и IgnoreNestingCheck сохраняются, outer event следует после children.
- [ ] OnComponentStateChanged фильтрует captured depth, а не stack depth во время flush.
- [ ] Никакого bypass StateEventAuthority/DespawnedObjectEventFilter/AvatarStateEventGate.
- [ ] Abort до delivery не вызывает ни direct StateChanged, ни manager/relay, ни admission queue.
- [ ] Throw subscriber после начала delivery: drop remaining notifications, clean frames,
  rethrow original; не повторять доставленные события, не обещать их отмену.
- [ ] Reentrant subscriber открывает новую transaction при clean stack; её frames не принадлежат
  уже committed scope и не снимаются cleanup предыдущей операции.
- [ ] Сравнить trace local observers; согласовать с владельцами committed timing и captured depth.
  Flush при depth0 без metadata filter недопустим; successful state не отменять при subscriber error.
- [ ] Проверить remote ExecuteStateSyncEvent: Commit/flush до выхода IsInsideStateSync;
  nested execution не меняет suppression policy без отдельного согласованного решения.

## Задача 4: RenderMode adoption и acceptance

**Modify:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Avatar/UxrAvatar.cs:1706`.
**Checks:** AndroidCompileGate; existing AvatarRendererOwnershipTests, GhostAvatarTests,
AvatarStateEventGateTests, StateEventAuthorityTests и AvatarTeardownTests.

- [ ] До BeginSyncScope проверить live renderer refs и controller target/input collection;
  не очищать/расширять ownership list в runtime.
- [ ] Snapshot old mode и затрагиваемые visibility states по effects audit задачи 1.
- [ ] Открыть scope, применить renderer/controller изменения, присвоить `_renderMode` последним,
  CommitMethod с тем же methodName/parameters для reflection/wire compatibility.
- [ ] Catch охватывает apply и CommitMethod: при HasCommitted=false (apply failure или rejection
  до commit) Abort, old mode/surviving visibility restore, rethrow original; incomplete restore log
  не скрывает исходную ошибку. При HasCommitted=true ошибка subscriber delivery не компенсируется.
- [ ] Не превращать corrupted body ref в silent skip/success; ошибка не уходит в сеть.
- [ ] Prepared Unity package: checkpoint → request → watch-ticket → claim → begin → guard
  → execute/wait → finish → receive; finish до diagnosis/edit/person wait.
- [ ] Получить compilation и XML existing checks; указать их scope и residual failures.
- [ ] Runtime-приёмка: local/remote hide/show, connected controllers, Ghost/respawn,
  teleport/grab после контролируемого renderer отказа; depth baseline восстановлен,
  нет cascade mismatch/failed mode event, old mode соответствует surviving targets.
- [ ] Пользователь отдельно подтверждает успешный обычный RenderMode и поведение при injected failure.

## Задача 5: новые regression contracts после приёмки

**Create:** `Assets/Tests/EditMode/Network/StateSyncScopeTests.cs`.
**Create:** `Assets/Tests/EditMode/Prefabs/AvatarRenderModeFailureTests.cs`.
**Modify:** existing tests только при утверждённых изменившихся ожиданиях.

- [ ] `BodyExceptionAbortsWithoutEvent`: depth baseline, no callbacks, следующий operation работает.
- [ ] `CrossOwnerNestedAbortKeepsFramesConsistent`: options каждого owner сохранены; чужой End не Pop.
- [ ] `OuterAbortDropsCommittedIgnoreNestingChild`: direct/manager/relay/gate counts zero.
- [ ] `CaughtChildAbortPoisonsOuterCommit` и `ForgottenLegacyEndUnwindsWithinScope`:
  outer cannot publish, stack вернулся к entry baseline без reset чужих frames.
- [ ] `DoubleDisposeAndCommitAfterDispose`: dispose safe, illegal commit fail без depth mutation.
- [ ] `SuccessfulScopePreservesLegacyTrace`: callback order/options/top-level selection;
  `RemoteReplayDoesNotEcho`: ни нового relay send, ни admission insertion.
- [ ] `BufferedPayloadKeepsCommitSnapshot`: изменение исходного controller List/object[] после End
  не меняет envelope; Unity refs сохраняют identity, wire format совместим.
- [ ] `SubscriberThrowLeavesCleanFrames` и `ReentrantDeliveryUsesIndependentScope`:
  original error, no retry, no pending buffer, next transaction valid.
- [ ] `FailedRenderModeRestoresSurvivingVisibilityAndOldMode`: fault после первого apply,
  old mode/states restored, event suppressed; separately destroyed target residual documented.
- [ ] `RejectedCommitRestoresRenderMode`: poison/frame/snapshot rejection после apply,
  HasCommitted=false, old mode/visibility restored, no event; `DeliveryFailureKeepsCommittedMode`:
  HasCommitted=true, new mode/states remain, clean frames, no retry.
- [ ] `ChildCommitIsLocalBeforeOuterAbort`: child HasCommitted=true, outer false, notifications zero;
  child side effects сохраняются без explicit caller compensation.
- [ ] Запустить tests native Runner и Android gate, сравнить XML с baseline и проверить scope.
- [ ] Code review SDK/avatar owners, runtime acceptance record, accepted commit;
  enforced merge-request/merge-execute и FF origin/dev с remote SHA proof.
- [ ] Rollback — согласованный revert SDK/caller bundle; не менять Origin history.

## Self-review

Охвачены real shared depth/local options mismatch, nested abort, buffers и actual relay path.
Proposed API consistent; captured depth не подменяет frame authority; wire fields неизменны.
Ограничения multicast delivery и gameplay compensation явны; весь SDK не объявляется покрытым.
Все новые тесты стоят после runtime-приёмки; upstream/downstream owners имеют gates.
