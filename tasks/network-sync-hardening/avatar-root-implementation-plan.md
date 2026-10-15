# План реализации допуска событий корня сетевого аватара

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Не допускать позднее событие недоступного сетевого UxrAvatar в SDK decoder, Execute и forward.

**Architecture:** NetworkStateRelay передаёт существующие Mirror netId и SDK UniqueId рядом с неизменным SDK payload.
Stateless helper проверяет текущее Mirror.spawned и binding; Mirror остаётся владельцем жизни, SDK — идентичности
и исполнения. Нет дополнительного decode, истории GUID, второго lifecycle writer или SDK-патча.

**Tech Stack:** Unity 6, C#, Mirror 96.0.1, UltimateXR; Windows runtime и отдельная физическая Quest-проверка.

**Spec:** [Одобренный проект v1](contracts/avatar-root-event-admission-v1.md), исходная редакция `fb8d736c6`,
уточнение CombineIdSource `7bc8846ba`. Пользователь одобрил подготовку плана 2026-10-15.
**Статус плана:** подготовлен для ревью; реализации и новых игровых тестов нет. Выбор способа исполнения впереди.

## Global Constraints

- Scope: concrete `sourceComponent is UxrAvatar`, UxrMirrorAvatar и NetworkIdentity на том же GameObject.
- Wire: штатные uint avatarRootNetId + Guid avatarRootUniqueId, **17–21 байт аргументов**, unscoped=17; length prefix отдельно.
- Только `(0, Guid.Empty)` означает unscoped; частичная пара запрещена до ветвления по нулю.
- Точный CombineIdSource: `netId.ToString().GetGuid()`. Непустого значения недостаточно.
- SDK byte[] неизменен; прежний ExecuteStateSyncEvent и IsInsideStateSync обязательны; server decode=2, client=1.
- Прежние StateEventAuthority, ammo admission, connection guard, host/origin skip и InitialStateBarrier сохраняются.
- Не сравнивать цель с Session.ActiveAvatar; не добавлять retirement/epoch/cache writer, retries или новую очередь.
- Missing arguments живого root, child targets и corrupt SDK payload остаются строгими ошибками вне исправления v1.
- До проверки gameplay пользователем не создавать и не переписывать игровые тесты, включая существующие ожидания.
- До пользовательской приёмки разрешены только технические checkpoints кода; accepted commit/integration — после неё.
- Только собственный linked checkout; Unity — через собственную RUNNING аренду. Отчёты в reports/, вне Git.
- Никакого самостоятельного ремонта/обхода agent-infra, worker, broker/proxy, hooks или MCP: infra_issue сопровождающему.

## Review Focus

1. Partial pair и downgrade сетевого root в 0: отказ без Execute/forward; детерминированное покрытие — задача 3.
2. Неверный CombineIdSource либо чужой SDK registry instance: ошибка binding, без изменения SDK registry — задача 3.
3. Старое тело ещё spawned, но ActiveAvatar уже новый: финальное освобождение сохраняется — задачи 2 и 3.
4. AOI hide, закрытый barrier, host и origin: не объявлять Unavailable доказанным retirement, не создавать эхо — задачи 2 и 3.
5. Большой payload, burst из gate и редкие late packets: без усечения, лишнего decode и ложного PASS отрицательной ветки — задачи 2 и 3.

## Файлы и границы

| Файл | Ответственность |
|---|---|
| Новый `Assets/Scripts/Network/AvatarRootEventAdmission.cs` + `.meta` | Capture, проверка envelope и binding, без собственного mutable состояния |
| `Assets/Scripts/Network/NetworkStateRelay.cs` | Проводка Cmd/Rpc и порядок admission → SDK → forward, фиксированные счётчики экземпляра |
| Новый `tasks/network-sync-hardening/tools/avatar-root-observation.cs.txt` | Пассивный временный observer в MCP/Editor, без записи в Assets и без генерации игровых событий |
| После приёмки: новый `Assets/Tests/EditMode/Network/AvatarRootEventAdmissionTests.cs` + `.meta` | Детерминированные границы helper |
| После приёмки: `Assets/Tests/EditMode/Network/NetworkStateRelayTests.cs` | Интеграция приёма, доказательство отсутствия decode/Execute/forward на reject |
| После приёмки: `Assets/Tests/EditMode/Network/RemoteCallHashTests.cs` | Новая живая сигнатура, сохранение исторического примера коллизии |
| `tasks/network-sync-hardening/{Readme.md,Details.md,plan.json,changelog/}` | Статус, проверки, приёмка и scope этапов |

Gameplay/AvatarManager/AvatarTeardown, UxrMirrorAvatar, SDK UniqueId/serializer и Mirror не изменяются.
Не редактировать E2E-сценарии, известный шум, общие индексы или инфраструктурные launchers.

### Задача 0: закрепить базу и измерить исходный поток

**Files:** task docs/plan.json, `tools/avatar-root-observation.cs.txt`; продуктовые исходники не меняются.
**Interfaces:** вход — одобренный проект; выход — baseline A с точными source SHA, Assets-tree и buildGuid,
Ncmd/Nrpc/fanout, размерами payload и CPU/GC по сопоставимым фазам.

- [ ] Выполнить startup, свежий inbox и проверку checkout/mode. Сохранить tracked/staged/untracked изменения,
  включая существующие tools/mcp_call.py и tools/test_mcp_call.py; не чистить их и не включать случайно в коммит.
- [ ] После одобрения этого плана добавить в plan.json отдельный `avatar-root-admission` с writes двух production
  файлов, новой .meta, task docs и `tools/avatar-root-observation.cs.txt`; tests оставить отдельным этапом после приёмки. Зарегистрировать Readme.md с актуальной
  revision; mode off не превращать в обязательную выдуманную зависимость ACK или merged старых этапов.
- [ ] `git fetch origin dev`, затем `git rebase origin/dev` с сохранением локальных изменений. Зафиксировать
  SHA/Assets-tree и source drift. При подготовке плана origin/dev=`8eb87791541fda68f67bcf99d35bdffb08879d9e`;
  относительно проверенной базы `97a320c88` в рассмотренной области изменился только PlayerLoadoutManager.
  Network, Mirror/Core, SDK Unique/StateSync/Mirror integration не изменились. Это не текущая runtime-проверка.
- [ ] Подготовить baseline-пакет A: компиляция/Android gate, Windows Development build, экспорт в отдельную
  папку Build задачи и пассивное наблюдение в заранее подготовленном PlayLaunch stand. Затем восстановление
  platform/profile/prefs/scene и finish. Очередь/lease — только на этот пакет.
  Сохранить DLL/source SHA, appVersion/buildGuid, build report и точный состав экспорта.
- [ ] Для event-specific измерения использовать временно скомпилированный штатным MCP инструментом класс
  `AvatarRootObservation : IDisposable` из `tools/avatar-root-observation.cs.txt`: конструктор
  `(uint relayNetId, byte relayComponentIndex, ushort cmdHash, ushort rpcHash, string reportDirectory)`, методы
  `void BeginPhase(string name)`, `void EndPhase()`, `void Dispose()`. Observer подписывается на NetworkDiagnostics In/Out,
  фильтрует relay netId/componentIndex и точные woven functionHash, считает message bytes*count/count без SDK decode.
  PerfFrameRecorder.Tick — один раз на кадр по StageUpdated(UxrUpdateStage.PostProcess), Dispose снимает все
  подписки и закрывает recorder. Гистограмма payload — 32 log2 bucket (0 отдельно, положительные: 1+floor(log2(n))),
  плюс скалярный максимум; без хранения событий.
  Нет инъекции/пересылки/изменения событий и registry, нет нового gameplay test. Полная сводка пишется при EndPhase,
  callback не пишет файл на каждый event. Сначала проверить подписку/cleanup на A, тот же observer применить к B.
  Это измерение Editor stand; из него не делать player/Quest CPU/GC PASS. В собранных player без такого observer
  event-specific метрики считать отсутствующими, а aggregate relayRpcBytes не выдавать за event rate.
- [ ] На A выполнить существующий dedicated-server-arsenal и наблюдение потока по протоколу задачи 2.
  Исходная гонка может остаться RED; не подавлять Error, не повторять до случайного GREEN как критерий базы.
  Старые результаты 643/651/658 остаются историей других inputs, не заменяют этот baseline.

### Задача 1: helper и атомарная проводка relay

**Files:** AvatarRootEventAdmission.cs + `.meta`, NetworkStateRelay.cs; task docs.
**Interfaces:** consumes Mirror spawned dictionary и существующие SDK API; produces следующие internal типы
в namespace `VrBattlegrounds.Network` (в одном новом файле, без нового asmdef):

```csharp
internal enum AvatarRootAdmissionResult
{ Unscoped, Allowed, Unavailable, InvalidEnvelope, InvalidBinding, InvalidAlignment, InvalidRegistry }
internal readonly struct AvatarRootEventScope // readonly NetId:uint, UniqueId:Guid; constructor(uint, Guid)
internal readonly struct AvatarRootEventCapture // readonly Avatar:UxrAvatar, Identity:NetworkIdentity,
                                           // Scope:AvatarRootEventScope, ExpectedCombineIdSource:Guid
internal static class AvatarRootEventAdmission
{
    internal static AvatarRootAdmissionResult CaptureOutgoing(IUxrStateSync source,
        IReadOnlyDictionary<uint, NetworkIdentity> spawned, out AvatarRootEventCapture capture);
    internal static AvatarRootAdmissionResult RevalidateOutgoing(in AvatarRootEventCapture capture,
        IReadOnlyDictionary<uint, NetworkIdentity> spawned);
    internal static AvatarRootAdmissionResult CheckIncoming(in AvatarRootEventScope scope,
        IReadOnlyDictionary<uint, NetworkIdentity> spawned, out UxrAvatar root);
    internal static AvatarRootAdmissionResult CheckDecodedTarget(in AvatarRootEventScope scope,
        UxrAvatar admittedRoot, IUxrStateSync decodedTarget);
}
```

Это объявления интерфейсов плана, не готовый компилируемый исходник. `Unscoped` и `Allowed` продолжают pipeline;
остальные результаты прекращают его. Capture для unscoped — default, без ссылок/вычисления netId-хеша.

- [ ] Реализовать CaptureOutgoing: concrete UxrAvatar с same-GO UxrMirrorAvatar обязан иметь live same-GO
  NetworkIdentity, nonzero netId/UniqueId, точный spawned instance и CombineIdSource текущего netId.
  Сетевой root при неготовности никогда не превращать в unscoped. Несетевой root/child/item — unscoped.
  Сопоставление экземпляров — ReferenceEquals после проверки Unity destroyed-object semantics.
- [ ] Вычислять expected CombineIdSource существующим SDK GetGuid один раз на scoped CaptureOutgoing;
  сохранить Guid только в локальном capture. RevalidateOutgoing сравнивает прежний netId и сохранённый expected
  с текущими данными после SerializeEventBinary. При изменении netId/instance/UniqueId/membership — отказ;
  не пересчитывать header и не переносить байты новому телу. Кеша между событиями нет.
- [ ] Реализовать CheckIncoming: сначала полная пара, затем spawned и same-GO root/marker, точные ID/alignment,
  затем untyped `UxrUniqueIdImplementer.TryGetComponentById(Guid, out IUxrUniqueId)` и ReferenceEquals.
  Нет/уничтожена spawned identity — Unavailable; live неправильный root/ID — InvalidBinding;
  неправильный CombineIdSource — InvalidAlignment; неправильный SDK mapping — InvalidRegistry.
  Здесь expected вычисляется один раз на scoped приём. Registry самостоятельно не Register/Unregister/Combine.
  Штатный SDK lookup может вызвать собственный PurgeDeadOwner; это не исправление registry со стороны helper.
- [ ] Реализовать CheckDecodedTarget: scoped требует тот же concrete root, identity и IDs; unscoped запрещает
  decoded concrete сетевой UxrAvatar root. Никакого разрешения по holder/ActiveAvatar/Mirror.owned.
- [ ] В Send сохранить прежние фильтры и gate. После gate CaptureOutgoing → прежняя SerializeEventBinary →
  RevalidateOutgoing → прежняя server/ready-client ветка. Не передавать SDK payload в новый serializer.
  Для server-origin исходный эффект уже выполнен, второго Execute не добавлять; ammo-published остаётся после RPC.
- [ ] Заменить обе сигнатуры и все их вызовы одним изменением:

```csharp
CmdComponentStateChanged(byte[] serializedEvent, uint avatarRootNetId, Guid avatarRootUniqueId,
                         NetworkConnectionToClient sender = null)
RpcComponentStateChanged(byte[] serializedEvent, uint originNetId,
                         uint avatarRootNetId, Guid avatarRootUniqueId)
```

- [ ] В Cmd сохранить connection guard первым; CheckIncoming → прежний строгий DeserializeEventBinary →
  CheckDecodedTarget → прежний client-ammo запрет → ExecuteStateSyncEvent и IsError → RPC с теми же bytes/IDs.
  У клиента сохранить host/origin/barrier skip первыми; CheckIncoming проверяет также partial pair до unscoped;
  затем единственный прежний ExecuteStateSyncEvent. Не добавлять direct SyncState или клиентский второй decode.
- [ ] В relay добавить два экземплярных набора `ulong`-счётчиков: `_serverAvatarRootCounters` и
  `_clientAvatarRootCounters`; read-only snapshot свойства `ServerAvatarRootCounters`/`ClientAvatarRootCounters`.
  Тип `internal struct AvatarRootAdmissionCounters` поместить в helper-файл: ScopedReceived,
  ScopedDecoderEntered, Unavailable, InvalidEnvelope, InvalidBinding, InvalidAlignment, InvalidRegistry, ScopedApplied.
  ScopedReceived — валидная nonzero пара после прежних guards, ScopedDecoderEntered — один вход pipeline перед
  первым SDK decode/Execute, не число внутренних decoder calls; ScopedApplied — успешный SDK result.
  Ошибочные пары/binding учитывать отдельно, unavailable — без Error, невозможные binding — отдельный GameLog.Network.Error.
  Не интерполировать Verbose при выключенном уровне; не создавать историю netId. Обнуление — при завершении
  подписки/сессии relay; счётчики ничего не решают о доступности root.
- [ ] Source-review diff против spec; сохранить LF, проверить новую .meta без дубликата GUID.
  Создать только технический checkpoint для worker, не accepted commit. Новых тестовых файлов пока нет.

### Задача 2: существующие проверки, A/B и пользовательская проверка

**Files:** task reports/ и task docs. Игровые тесты/сценарии не изменяются.
**Interfaces:** вход — checkpoint B и baseline A; выход — проверяемый отчёт с пределами покрытия и пакет приёмки.

- [ ] Заранее подготовить пакет B и cleanup, затем checkpoint → request → watch-ticket → claim → begin → guard.
  Прочитать актуальные MCP schema; proxy из своего worktree. Компиляция/Weaver: 0 errors; AndroidCompileGate.Run():
  Passed=true и 0 ошибок. Запустить существующие EditMode namespace VrBattlegrounds.Tests.Network и
  VrBattlegrounds.Tests.Weapons в assembly VrBattlegrounds.Tests.EditMode, сохранить полный Result/job id.
- [ ] Известное устаревшее ожидание: RemoteCallHashTests.Расчёт_хэша_совпадает_с_регистрацией_Mirror ищет
  прежнюю двухпараметрическую Cmd и hash14651. Зафиксировать отдельный ожидаемый RED до пользовательской приёмки;
  не переписывать/skip этот тест и не объявлять весь набор PASS. Общая проверка коллизий обязана остаться GREEN.
  Исторический тест прежней CartridgeIntake-коллизии сохраняет старую сигнатуру намеренно.
- [ ] E2EPlayerBuilder.Run(): успешный BuildReport, source/build identity B и экспорт полного Windows player.
  Выполнить тот же пассивный PlayLaunch stand/observer B, что A, до cleanup.
  Восстановить platform/profile/prefs/scene, дождаться конца всех операций, немедленно finish и затем receive
  только разрешённых ожидаемых paths. Ошибка — тоже результат. Unknown outcome не повторять; durable status/log
  read-only, infra_issue в canonical agent-coordination-protocol и подтверждённый native канал сопровождающего.
- [ ] После освобождения аренды выполнить для A и B из одного player на все роли:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/e2e/Run-E2E.ps1 -Scenario dedicated-server-arsenal -Map TestMap1 -Clients 2 -Timeout 300 -Player <полный-путь-соответствующего-экспорта>/VrBattlegrounds.exe -LogVerdict enforce
```

  Для B: functional 24/24, log verdict GREEN, unexpected/known defects/native/dropped=0; свежий stamped input,
  один buildGuid у всех ролей. Не использовать SkipStaleCheck. Проверить cleanup только собственных PID/портов.
- [ ] Через существующий PlayLaunch и штатный UI/API проверить body-swap/respawn с финальным release,
  ServerOnly+2 clients и host+client, смену карты/reconnect, ожидание prealignment и barrier. Фиксировать
  пары root IDs/counter deltas и эффект ровно один на наблюдателе; origin/host не применяют повторно.
  Отдельно child/item/ammo действия сохраняются; чужой live root с плохим binding не должен попасть в Execute.
  Если естественного late-root пакета не было, записать «Unavailable branch не наблюдалась»; не изобретать
  до приёмки новый gameplay test/injection и не считать отсутствие ошибки доказательством ветки отказа.
- [ ] A/B: одинаковые accepted base, build options, роли, сценарий/event mix, verbosity и measurement hooks;
  различается только admission и его метаданные. У каждого варианта свои точные SHA/buildGuid, одинаковые внутри ролей.
  NetworkDiagnostics.OutMessageEvent отбирать по CommandMessage/RpcMessage.functionHash именно relay;
  суммировать count и bytes*count. Сохранить Ncmd, фактических RPC recipients, payload-size distribution,
  scoped/unscoped mix, T; логический прирост — сумма `d_i=16+VarUIntSize(rootNetId_i)` по Cmd и фактическим RPC
  recipients, делённая на T. Для длины прежних аргументов L_i учитывать также
  `VarUIntSize(L_i+d_i+1)-VarUIntSize(L_i+1)` внешнего payload prefix; Batcher/transport отдельно.
  На B mix брать из wire metadata при обычном Mirror чтении параметров; на A без metadata не разбирать SDK
  payload ради классификации: сопоставлять заданные действия, а точный scoped/unscoped mix A отметить неизвестным.
  Origin handler-skip не убирает wire; transport headers/batching/retransmit/fragmentation/host loopback отдельно.
- [ ] Снять PerfFrameRecorder CPU/GC/memory и транспортные counters на normal/drop/error/warmup и gate burst
  раздельно. SDK GetGuid использует SHA-256 и временные массивы; untyped registry lookup O(K implementer types)
  и может чистить dead-owner. Нулевые GC/CPU-издержки не заявлять. Сравнить полный максимум payload/metadata/prefix с реальным
  лимитом согласованного канала; без усечения. Нового бюджета или числового performance PASS этот план не вводит.
  Если измеренная цена неприемлема, согласовать изменение проекта, не вводить кеш жизни/другой codec скрыто.
- [ ] Передать пользователю build/SHA, таблицу actual checks и оставшиеся пробелы; дать шаги на физическом Quest:
  join → смена тела → death/respawn с предметом в руках → map change → reconnect, проверить позы/видимость/отпускание,
  отсутствие повторных эффектов и лог-ошибок. PC/симулятор не заменяют Quest. Дождаться проверки изменённого gameplay.

### Задача 3: после приёмки — детерминированные регрессии и интеграция

**Files:** три тестовых файла из таблицы, `.meta` нового файла, task docs/plan.json.
**Interfaces:** consumes helper и relay из задачи 1; produces доказательство границы и accepted candidate.

- [ ] Только после явного подтверждения gameplay и когда нет более приоритетной работы открыть соответствующий
  test-этап в plan.json. Проверить, что отрицательные тесты действительно ловят прежний приём без admission
  в изолированном техническом input, затем GREEN на B; не тестировать только счётчик. Не менять принятый live checkout.
- [ ] В AvatarRootEventAdmissionTests покрыть: partial пары в обе стороны, missing/destroyed root, live foreign GUID,
  неправильный CombineIdSource, registry отсутствует/чужой instance, same-GO scope, concrete source vs wrapper,
  несетевой root/child/item, изменение netId/ID/membership после capture, downgrade и decoded foreign target.
  Assertions: точный результат, helper не выполняет repair registry, capture не переносится на новое тело;
  разрешённую внутреннюю очистку SDK dead-owner учитывать отдельно.
- [ ] В NetworkStateRelayTests доказать на реальном woven приёмном пути: unavailable/invalid до decode означают
  **0 decoder entry, 0 Execute, 0 forward**; malformed live payload/missing argument остаются ошибкой;
  корректный live event исполняется один раз, payload bytes/metadata сохраняются; host/origin/barrier и ammo прежние.
  Для финального release прежнего spawned root допустимы при уже новом ActiveAvatar; AOI absence только unavailable.
  Использовать существующий MirrorTestHarness и наблюдаемые SDK события/результаты, не добавлять runtime delegates
  в production ради подставного decoder. Если настоящий вызов нельзя наблюдать, тест не объявлять доказанным.
- [ ] Там же закрепить wire/gate границы: штатный woven uint+Guid roundtrip добавляет 16+VarUIntSize(netId) байт,
  для netId 0/240/241/2287/2288/67823/67824/16777215/16777216/uint.MaxValue проверить размеры 17/17/18/18/19/19/20/20/21/21;
  SDK byte[] побайтно сохранён; payload у фактического лимита канала не усечён, превышение не отправлено частично.
  Burst от AvatarStateEventGate создаёт header в Send после выравнивания каждого root, без повторной отправки;
  partial pair с netId=0 проверяется на клиенте до unscoped Execute. Это тесты Review Focus 1/5, а не новые codec/API.
- [ ] Обновить только живую сигнатуру/hash в RemoteCallHashTests по фактическому woven B; исторический14651
  оставить историческим. Запустить полный Network/Weapons и новые тесты, Android gate; 0 неожиданных failures,
  collision check GREEN. Если исправление после приёмки меняет gameplay, повторить пользовательскую проверку.
- [ ] Обновить Readme/Details/changelog результатами, SHA и пределами Quest/perf. Проверить LF физических путей
  и staged blobs, review diff. Accepted commit и FF integration в origin/dev только принятого scope по текущему
  coordination mode; request-base и подтверждение DONE отдельно. Никакого force-push/чужого checkout.

## Самопроверка плана

Scope/wire/send/server/client/lifecycle/strictness из spec покрыты задачей 1; наблюдаемость и обе роли — 1/2;
payload limit, fanout, CPU/GC/gate burst — 2; пять Review Focus — 2/3. Новые тесты отложены за пользовательскую
проверку по AGENTS.md, поэтому обычная последовательность TDD навыка здесь применяется только в задаче 3.
Source-review владельцев: gameplay7180, avatar-ik7223, performance7191; это статический обзор, не ACK приёмки.
API-аудит: локальный `reports/avatar-root-plan-api-audit.md`; GetGuid/explicit IUxrUniqueId/lookup и stale
hash-membership проверены по исходникам. Unity/benchmark в ходе подготовки плана не запускались.
Поправка implementation-review: прежняя константа20 ошибочно предполагала WriteUInt; Weaver выбирает
WriteVarUInt. Production codec сохраняется, критерии приёмки исправлены на 17–21 Б и отдельные prefix deltas.
Рекомендуемое исполнение — основным агентом в этой сессии с независимым итоговым review: изменения helper и relay
тесно связаны, а code scope мал. Пользователь сначала оценивает этот план и выбирает способ исполнения.
