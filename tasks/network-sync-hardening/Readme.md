# Сеть: событие снятого объекта, стоимость StateSync, регрессионные тесты

Обновлено: 2026-10-14. Владелец: эксперт `network`. Worktree: `F:/CodexWorktrees/network/Vr_Battlegrounds_ai`,
ветка `claude/network`. Решение пользователя 2026-10-14: «сделай всё в одной задаче» (teleport-state 6048,
statesync-cost 5871, регрессионные тесты трёх влитых задач).

## Этапы

| Этап | Суть | Решение пользователя | Состояние |
|---|---|---|---|
| `teleport-state` | 6048: клиент шлёт событие из `OnDisable` своего уничтожаемого аватара (`UxrTeleportLocomotion`), сервер его уже не знает → `UxrComponentNotFoundException` | патч Mirror: `NetworkClient.DestroyObject` вынимает объект из `spawned` сразу после `OnStopClient`, до `Destroy`/`SetActive(false)`, как сервер; `DespawnedObjectEventFilter` работает как задуман | код `ccfaf5991` (Mirror patch 4), офлайн-компиляция 0 ошибок; Unity и приёмка — не начаты |
| `statesync-cost` | 5871: (а) уровни логов UltimateXR = Debug → лог со стеком на каждое событие; (б) рефлексия и LINQ в `SyncState` на каждое событие | (а) рантайм-ограничитель до Warnings во всех плеерах, Verbose — DebugMode или `-uxr-log-verbose`, ассет не трогаем, редактор без изменений; (б) кеш MethodInfo патчем SDK | код `859b06777` (SDK-патч 80 + `UxrLogLevelPolicy`), офлайн-компиляция 0 ошибок, самопроверка кеша 351/351; Unity и приёмка — не начаты |
| `regression-tests` | тесты C2 (`state-sync-depth-guard`), `mirror-nt-keyframe`, `uniqueid-stale-registry`; тесты новых этапов — после их приёмки | правило: тесты, когда нет других задач | ждёт |

Этапы `regression-tests` задач `state-sync-depth-guard`, `mirror-nt-keyframe`, `uniqueid-stale-registry` перенесены сюда.

## Диагностика

- 6048: e2e dedicated-server-arsenal 20261013-211416 (worktree launch-infra): 6 ошибок, по 2 на каждый уничтоженный свой
  аватар (TeleportLeft/Right). Стек клиента: `OnDisable ← Object.Destroy ← NetworkClient.DestroyObject`; `spawned.Remove`
  в Mirror — после Destroy (`NetworkClient.cs:1762-1795`), на сервере — до (`NetworkServer.cs:1709`).
- 5871: `UxrGlobalSettings.asset` — Avatar/Core/Locomotion/Manipulation/Networking = Debug (5); per-event лог
  `UxrManager.cs:672` (+ стек), `:416/:581` на каждый компонент снимка, `UxrGrabManager` Relevant на каждый захват.
  `UxrStateSyncImplementer_1.SyncState` — `GetMethods(FlattenHierarchy)` + LINQ на событие.

## Приёмка

- 6048: e2e dedicated-server-arsenal — 0 `UxrComponentNotFoundException` по `UpdateTeleportState`; смена тела/скина
  хост + клиент без ошибок; EditMode Network.
- 5871: в плеере нет per-event логов UXR без DebugMode; с DebugMode/флагом — есть; события выстрела/хвата/перезарядки
  работают как прежде; замер выстрела до/после — weapon-system (shot-cost), копия performance.

## Состояние на 2026-10-14 (остановка по решению пользователя, рассылка 6539)

- Ветка `claude/network`: `9bd9564c2` план, `859b06777` statesync-cost, `ccfaf5991` teleport-state (поверх `f645d72f9`
  сторожа прогрева). Не влито.
- Допуски этапов: `statesync-cost` begin (token в хабе); `teleport-state` — begin отказан (один выполняемый этап на
  worktree), начать после закрытия statesync-cost.
- Дальше: одна Unity-проверка `ccfaf5991` в аренде (компиляция с weaver, AndroidCompileGate, EditMode Network+Weapons);
  затем приёмка пользователя: e2e dedicated-server-arsenal (0 `UxrComponentNotFoundException` по `UpdateTeleportState`),
  плеер без DebugMode — нет per-event логов UXR, выстрел/хват/перезарядка как прежде. Тесты — этап regression-tests.
- Журналы патчей: `changelog/2026-10-14-mirror-4.md`, `changelog/2026-10-14-sdk-80.md`. Хук sdk-patch-guard про
  общий журнал игнорировать до выпуска сопровождающего (6474).
