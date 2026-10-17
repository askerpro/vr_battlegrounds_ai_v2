# Замороженный C# API GPU launch inputs r2

2026-10-15, review8649. Нормативное приложение gpu-launch-inputs r2; **это спецификация**,
в product source этих типов ещё нет. Namespace всех типов ниже:
`VrBattlegrounds.DevTools.E2E`. DTO и session размещаются в уже запланированном
E2EDiagnosticSession.cs; scope новых source-файлов не расширяется.

## Общие правила объявления

Все observation DTO — public sealed class с перечисленными ниже `public readonly` полями.
Публичный конструктор принимает **все поля в порядке таблицы**, с теми же типами и именами
параметров camelCase, без optional/default/overload. Например,
`PhaseObservation(string phaseId, long phaseGeneration, DiagnosticPhaseTransition transition,
string observedRole, string source, bool isMeasured, double? remainingWindowSeconds,
DiagnosticObservationStamp stamp)`. Остальные конструкторы следуют тому же точному правилу.
Nullable reference annotations не требуются. Явно разрешённый null описан ниже;
остальные string не null, пустые строки допустимы только где прямо указано.

Observation DTO не сохраняет ссылки на изменяемые входные collections: defensive deep copy
в readonly wrappers при конструировании, включая вложенные JSON-compatible values. Поля после
конструктора не меняются. Session повторно проверяет DTO на входе, не доверяет default struct.
Типы результата/stamp/bounds/value — public readonly struct с public readonly полями,
конструктор по тому же правилу. Это не инструкции вручную создавать Unity .meta.

## Result и clock

`DiagnosticReportResult`:

| Поле | Тип | Контракт |
|---|---|---|
| Accepted | bool | true только после успешной записи/flush либо штатного idempotent replay |
| EventId | string | accepted: `<processInstanceId>:<sequence>`; rejected: пустая строка |
| Reason | string | accepted: пустая строка; rejected: непустой machine-readable code с detail |

Ожидаемые причины отказа: invalid-input, stale-generation, missing-evidence,
unknown-conditions, identity-mismatch, wrong-clock, inactive-session, io-error, wrong-thread.
Readiness refusal — результат, не exception и не Accepted=true с текстом ошибки.
При io-error канал закрывается, последующие записи отвергаются. Ошибку по возможности
пишут в GameLog.Error/E2E result; запись rejected в сломанный stream не обещается.

`DiagnosticObservationStamp`:

| Поле | Тип | Контракт |
|---|---|---|
| ClockId | string | `process:<processInstanceId>:stopwatch` |
| Frame | long | фактический Unity frame, >=0 |
| MonotonicTicks | long | Stopwatch.GetTimestamp(), без перевода в UTC |
| TickFrequency | long | Stopwatch.Frequency, >0 |
| RealtimeSeconds | double | Unity realtime с double precision; отдельная шкала |
| UtcAnchor | string | UTC ISO8601 round-trip; anchor, не clock mapping |

`DiagnosticTimeBounds`:

| Поле | Тип | Контракт |
|---|---|---|
| ClockId | string | объявленная шкала bounds; для runtime — session ClockId |
| StartSeconds | double | наблюдённая нижняя граница, ticks/TickFrequency |
| EndSeconds | double | наблюдённая верхняя граница, >=StartSeconds |
| UncertaintySeconds | double? | null если не измерена; иначе finite >=0 |
| Verified | bool | true только с измеренной uncertainty и установленным методом bounds |
| Method | string | непустое имя метода; для unknown — причина |

Все non-null числа finite; миллисекунды/секунды не смешиваются. Bounds описывают **наблюдение**,
не гарантируют будущий workload/capture интервал. RemainingWindowSeconds — расчётный остаток
фазы от владельца workload; конечный verified sample/capture window формирует performance
после наблюдённых end/invalidation и native clock mapping. Предсказанный конец не verified.

## Observation DTO

`DiagnosticPhaseTransition` — public enum: `Begin = 0`, `End = 1`.

`PhaseObservation`:

| Поле | Тип |
|---|---|
| PhaseId | string |
| PhaseGeneration | long |
| Transition | DiagnosticPhaseTransition |
| ObservedRole | string |
| Source | string |
| IsMeasured | bool |
| RemainingWindowSeconds | double? |
| Stamp | DiagnosticObservationStamp |

Generation >=1, монотонно растёт при новом поколении phase у performance adapter;
один Begin и один End на generation. Source отличает server-transition от client-observation;
Stamp всегда локальный момент наблюдения, а не переданные сетевые часы. ObservedRole — роль
наблюдателя, совпадает с session Role. End инвалидирует активный ready этой generation.
Remaining null разрешён вне ready; End использует 0. Новая generation инвалидирует старую.

`ViewObservation`:

| Поле | Тип |
|---|---|
| ViewId | string |
| PhaseId | string |
| PhaseGeneration | long |
| Succeeded | bool |
| ActualPosition | UnityEngine.Vector3? |
| ActualRotation | UnityEngine.Quaternion? |
| DeviationXZMetres | float? |
| DeviationYawDegrees | float? |
| Refusal | string |
| Source | string |
| Stamp | DiagnosticObservationStamp |

Succeeded требует ненулевых actual pose и пустого Refusal; отказ требует непустого Refusal.
Unknown deviation допускает null, но не фиктивный 0. Координаты мировые, метры; quaternion
из фактически прочитанной rotation с указанным Source. E2EViews outcome содержит Euler,
а не Quaternion: адаптер не объявляет его имеющимся полем и не угадывает систему знаков.
Для success допускается read-only camera.rotation того же наблюдения; pose writer не добавляется.
Нормализация quaternion допустима только при конечном ненулевом входе с указанным методом.

`DiagnosticConditionValue`:

| Поле | Тип |
|---|---|
| Value | object |
| Source | string |
| Reason | string |

Value допускает только string, bool, long, finite double, null, readonly JSON array
(`IReadOnlyList<object>`) или readonly object (`IReadOnlyDictionary<string, object>`), рекурсивно.
Входные IList/IDictionary допускаются конструктором как object, копируются в указанные readonly
типы; unsupported CLR objects отвергаются. int/float caller явно переводит в long/double.
Value=null требует непустого Reason; non-null требует пустого Reason. Source обязателен всегда.
Пустая строка Value — наблюдённая строка, не неизвестное значение. Коллекции не содержат
NaN/Inf/циклов/нестроковых keys; snapshot bounded, <=64 уровней и <=1MiB UTF-8 в сериализованном виде.

`ConditionsObservation`:

| Поле | Тип |
|---|---|
| ActualSceneId | string |
| PhaseId | string |
| PhaseGeneration | long |
| ActualFingerprint | string |
| Values | System.Collections.Generic.IReadOnlyDictionary<string, DiagnosticConditionValue> |
| Stamp | DiagnosticObservationStamp |

Values не null, keys соответствуют conditions package v1: deviceModel, deviceId, graphicsApi,
developmentBuild, sceneId, role, viewId, phaseId, workloadId, refreshHz, eyeResolution, ffr,
cpuLevel, gpuLevel, qualityHash; дополнительные observed keys допустимы и сохраняются.
Отсутствующий required key не равен известному значению: session выдаёт unknown-conditions
при ready, нормализатор сохраняет неполноту. Числа представлены long/double, developmentBuild
bool, IDs/graphicsApi/qualityHash string. eyeResolution — readonly object с mode string
(mono/stereo), width/height long; для разных eyes дополнительно eyes array с width/height long.
Неизвестный элемент имеет null и reason в соответствующей condition; requested не actual.
ActualSceneId — фактически наблюдённый scene path/identity с source в Values[sceneId];
пустая строка допустима только если sceneId.Value=null с reason, readiness тогда запрещена.
ActualFingerprint — SHA256 канонического observed snapshot performance (versioned method в
Values[qualityFingerprintMethod]); пустой при unknown. Launch не создаёт второй settings snapshot.
Наблюдение неполных условий может быть принято как evidence; оно не разрешает ready.

`WorkloadReadyObservation`:

| Поле | Тип |
|---|---|
| ReadyId | string |
| PhaseId | string |
| PhaseGeneration | long |
| ActualFingerprint | string |
| ConditionsEventId | string |
| ViewEventId | string |
| RemainingWindowSeconds | double |
| TimeBounds | DiagnosticTimeBounds |
| Stamp | DiagnosticObservationStamp |

ReadyId уникален в session, 1..128 ASCII [A-Za-z0-9._-]; не путь. Event references принадлежат
этому процессу/run и текущей generation, conditions fingerprint совпадает с последним принятым
наблюдением, view success актуален. Фаза измеряемая/открытая, required conditions известны,
settle/warmup завершён по решению performance. Remaining finite >0; это оценка, не обещание.
TimeBounds соответствуют локальному наблюдению готовности; отсутствие verified clock bounds
не выдумывает native synchrony: collector обязан ограничить результат по clock evidence.
Ready сам по себе не GPU PASS. Повтор идентичного ReadyId/DTO idempotent: возвращает исходный
EventId, не дописывает второй ready. Другие данные с тем же ReadyId отвергаются.

Cross-field admission для **нового** ReadyId: PhaseId/PhaseGeneration у ready, последнего
открытого measured PhaseObservation, указанного view и conditions должны совпадать.
Conditions.Values[phaseId/viewId/role/sceneId] равны соответственно PhaseId, view.ViewId,
session.Role и Conditions.ActualSceneId. ActualSceneId также совпадает с фактически
наблюдённой runtime сценой и requestedSceneId по согласованной canonical scene identity.
WorkloadId/quality fingerprint проверяются по frozen intent и observed conditions;
разрешённый вариант A/B не делает requested значением observed. ConditionsEventId и ViewEventId
ссылаются на последние принятые подходящие observations; view success/pose должен быть
снят не раньше Begin этой measured generation (не cached outcome settle generation).
После settle adapter делает свежий read-only pose observation: повторного переноса не требуется.
Смена actual сцены, view или fingerprint инвалидирует старый active ready до нового admission.
Несовпадение identity возвращает identity-mismatch, устаревшая generation — stale-generation;
session не переинтерпретирует данные под запрошенную карту/ракурс.

## Точная поверхность session

В E2EContext: `public E2EDiagnosticSession Diagnostics { get; }` (null у legacy run).
Session создаёт Runner; performance не конструирует его и не открывает второй event stream.
Публичного session.Spec/RunSpec accessor r2 не обещает: workload/phase plan и ожидаемые quality
параметры adapter получает из своего существующего performance input. Launch сверяет их с
frozen intent при Report/ready. Если требуется прямое чтение spec через session, нужен новый
явный typed API договор; нельзя писать adapter в расчёте на несуществующий getter.

```csharp
public string RunId { get; }
public string ProcessInstanceId { get; }
public string Role { get; }
public string ClockId { get; }
public long TickFrequency { get; }
public bool IsActive { get; }

public DiagnosticObservationStamp CaptureStamp();
public DiagnosticReportResult ReportPhase(PhaseObservation value);
public DiagnosticReportResult ReportView(ViewObservation value);
public DiagnosticReportResult ReportConditions(ConditionsObservation value);
public DiagnosticReportResult ReportWorkloadReady(WorkloadReadyObservation value);
public DiagnosticReportResult InvalidateWorkload(string readyId, string reason);
```

Все вызовы — на Unity main thread, синхронные. CaptureStamp только читает часы/Unity frame,
не пишет событие; неактивная/чужая-thread session возвращает default stamp (ClockId=null),
последующий Report отвергает его с reason. Сетевой/native worker не вызывает CaptureStamp.
Report stamp должен принадлежать этой session ClockId/TickFrequency, быть конечным,
не из будущего и не старше предыдущего принятого observation (равенство допустимо).
Session добавляет текущие emission ticks/UTC отдельно от observation stamp, sequence long
увеличивается только при успешном событии. Ошибка flush закрывает канал, оставляет incomplete
tail evidence и не возвращает Accepted=true. Expected validation/I/O failures не бросают наружу.

InvalidateWorkload: непустые readyId/reason обязательны. Повтор уже записанной инвалидизации
возвращает Accepted=true и её исходный EventId; неизвестный/чужой readyId — Accepted=false.
Смена generation/focus/map/settings/process end использует этот же единственный writer;
Reason сохраняется явно. Вызывать capture.ready/capture APIs у session нельзя — их здесь нет.

Порядок idempotency: сначала main-thread/basic shape/принадлежность session, затем lookup
уже **успешно записанного** readyId и точное сравнение исходного DTO (включая stamp/bounds).
Идентичный ReportWorkloadReady replay возвращает прежний receipt **до** проверок stale stamp,
текущей generation/фазы/IsActive; он никогда не активирует readiness и не пишет новое событие.
Invalidate replay также возвращает исходный receipt до stale/phase/IsActive. Другая непустая
причина повторной Invalidate не переписывает первую причину. Лишь новые записи проходят все
текущие guards. Неуспешный/неизвестный исход записи не попадает в cache успешных receipts.
Accepted=true у replay доказывает только прежнюю запись: capture adapter использует актуальную
generation и event stream/invalidation, не это значение как новую готовность после End.
