# Сеть: событие снятого объекта, стоимость StateSync, регрессионные тесты

Обновлено: 2026-10-14 (передача Claude → Codex). Владелец: эксперт `network`. Worktree:
`F:/CodexWorktrees/network/Vr_Battlegrounds_ai`, ветка `claude/network`.

## Статус

**Код обоих кодовых этапов готов, НЕ проверен в Unity, НЕ принят, НЕ влит.** Проверено только офлайн-компиляцией.

| Этап | Хаб | Код | Дальше |
|---|---|---|---|
| `teleport-state` (6048) | planned: begin отказан «один worktree — один выполняемый этап» | `ccfaf5991` | begin после закрытия `statesync-cost` (или одновременно, если хаб разрешит) |
| `statesync-cost` (5871) | running, token `b53217a4c38041ebaaa17c4303b71432` | `859b06777` | Unity → приёмка → вливание |
| `regression-tests` | planned, after обоих merged | — | после приёмки обоих |

## Сделано (ветка `claude/network`)

- `9bd9564c2` — план задачи, перенос этапов `regression-tests` из `state-sync-depth-guard`, `mirror-nt-keyframe`,
  `uniqueid-stale-registry` (их планы ред. 2 без этого этапа), backlog.
- `859b06777` — **statesync-cost**:
  - UltimateXR patch 80 (`Core/StateSync/UxrStateSyncImplementer_1.cs`): кеш `PropertyInfo`/`MethodInfo` по
    (рантайм-тип компонента, имя) в `SyncState` вместо `GetProperty`/`GetMethods`+LINQ на каждое событие. Поведение,
    порядок веток и исключения 1:1; отрицательные результаты не кешируются; только main thread. Патч 74 не тронут.
    Самопроверка вне репозитория (CoreCLR): 351 сравнение со старым кодом, 0 расхождений (Mono/IL2CPP не покрыты).
  - `Assets/Scripts/Network/UxrLogLevelPolicy.cs` (+.meta, guid f417c4c6…): в собранных плеерах (Release и Development)
    уровни логов UltimateXR = min(ассет, Warnings); подробные — при `DebugMode.Enabled` (подписка на `Changed`) или
    флаге `-uxr-log-verbose`. Редактор и `Resources/UxrGlobalSettings.asset` не меняются.
  - Журнал: `changelog/2026-10-14-sdk-80.md`. Компиляция: UltimateXR 555 файлов 0 ошибок, VrBattlegrounds 460 — 0.
- `ccfaf5991` — **teleport-state**:
  - Mirror patch 4 (`Assets/ThirdParty/Mirror/Core/NetworkClient.cs`, `DestroyObject`): `connection.owned.Remove` и
    `spawned.Remove` сразу после `OnStopClient`, до unspawn-обработчика/`Destroy`/`SetActive(false)` — как сервер.
    Причина 6048: `OnDisable` `UxrTeleportLocomotion` внутри синхронного `Destroy` своего аватара слал
    `UpdateTeleportState`, `DespawnedObjectEventFilter` не видел снятия → сервер: `UxrComponentNotFoundException`.
  - Комментарии: `DespawnedObjectEventFilter.cs`, `StateEventAuthority.cs`, `NetworkStateRelay.cs` (логика не менялась).
  - Аудит кода в окне снятия (Mirror, UXR, Assets/Scripts): опасных мест нет. Хост не затронут (там `DestroyObject` не
    зовётся). Журнал: `changelog/2026-10-14-mirror-4.md`. Компиляция: Mirror 75 файлов 0, VrBattlegrounds 459 — 0.
- Диагностика 6048: e2e `Tools/e2e/results/20261013-211416-dedicated-server-arsenal` (worktree launch-infra) —
  6 ошибок, по 2 (TeleportLeft/Right) на каждый уничтоженный свой аватар; стек клиента
  `OnDisable ← Object.Destroy ← NetworkClient.DestroyObject:1781`.

## Дальше (конкретный следующий шаг)

1. Одна Unity-проверка в аренде worker вершины ветки (≥ `ccfaf5991`; включает и `precache-server-guard`): компиляция с
   weaver, AndroidCompileGate, EditMode `VrBattlegrounds.Tests.Network` + `Weapons` (затронуты `LedgerSpawnReadyTests`,
   `PlayerCalibrationOwnerTests` — путь `ExecuteStateSyncEvent → SyncState`). Отчёт — `tasks/network-sync-hardening/reports/`.
   Worker/брокер на 2026-10-14 выключены — ждать объявления сопровождающего; перед заявкой fetch/rebase origin/dev.
2. Приёмка пользователя (ниже), затем вливание; после вливания statesync-cost — сообщить weapon-system (замер выстрела
   до/после, этап shot-cost) и performance (копия), находка 5871.
3. `regression-tests` после приёмки: StateSyncFrameStack (C2), NetworkTransformKeyframe (Mirror patch 3),
   UniqueIdStaleRegistry (патч 77), DespawnedObjectEventFilter (порядок снятия у удалённого клиента; устарел
   doc-комментарий в `DespawnedObjectEventFilterTests` строки 22-23 «Destroy отложен»), UxrLogLevelPolicy
   (Clamp/VerboseAllowed), StateSyncMethodCache (5 веток кеша).

## Ждёт пользователя

- Приёмка `teleport-state`: e2e dedicated-server-arsenal — 0 `UxrComponentNotFoundException` по `UpdateTeleportState`;
  смена тела/скина хост + клиент и выделенный сервер + клиент без ошибок; у клиентов нет «Отправка состояния: Teleport*»
  после `OnStopClient … (Local)`.
- Приёмка `statesync-cost`: плеер (шлем) без DebugMode — нет per-event логов UltimateXR; с DebugMode или
  `-uxr-log-verbose` — есть; выстрел, хват, перезарядка, поздний вход синхронизируются как прежде.

## Решения пользователя

- 2026-10-14: «сделай все в одной задаче» (teleport-state 6048, uxr-statesync-cost 5871, regression-tests).
- 2026-10-14, 6048: «Патч Mirror (Recommended)» — а не обвязка только для аватаров.
- 2026-10-14, 5871: «Все плееры (Recommended)» — ограничение логов в Release и Development.
- Правило по тестам (2026-10-13): «тесты берем когда других задач нет»; тесты — только после приёмки пользователем.

## Риски и ловушки

- Mirror patch 4 при обновлении Mirror переносить вручную. Если ствол NPC когда-нибудь станет дочерним к аватару,
  `WeaponUseContext` в окне снятия получит пустого владельца (сейчас не так).
- Остаётся гонка «событие уже в пути» при `ChangeAvatar` (дуга активна в момент смены тела) — в прогоне не видна;
  её закрыли бы «надгробия» недавно уничтоженных id на сервере (не делалось).
- Двойной `ChangeAvatar` за кадр («новый аватар в матче» → сразу новый) — вопрос к gameplay, не задан.
- Хук `.claude/hooks/sdk-patch-guard.ps1` требует запись в исторический `Docs/UltimateXR/sdk-patches.md` — игнорировать
  до выпуска сопровождающего (6447/6474); журнал патча — `changelog/` задачи.
- Вне этапа (рекомендации, не делались): `TypeExt.GetType(string)` без кеша на каждое событие (зона Extensions),
  двойная десериализация события на сервере (`NetworkStateRelay.cs:288/290`, зона network).

## Координация

- Inbox: `python Tools/agents/coordination.py inbox --owner network --unread --as "<свой адрес>"`, повторять пока
  `has_more`; ожидание — `wait-inbox --owner network --as "<свой адрес>"` фоновой командой. Route опубликует новая сессия.
- Вопросы пользователю: в Codex — явным сообщением с пометкой «ЖДЁТ ОТВЕТА» и дублировать в «Ждёт пользователя».
