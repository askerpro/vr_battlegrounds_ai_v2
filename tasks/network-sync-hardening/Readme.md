# Сеть: событие снятого объекта, стоимость StateSync, регрессионные тесты

Обновлено: 2026-10-15, продолжение в Codex. Владелец: эксперт `network`. Worktree:
`F:/CodexWorktrees/network/Vr_Battlegrounds_ai`, ветка `claude/network`.

## Статус

**Unity-компиляция, Android gate и 429 существующих Network/Weapons тестов прошли. Код НЕ принят, НЕ влит.**
Тикет 643 DONE: input `01e8b7748dfbf2545e6642fce2834ffeb1a5a5f4`, база `97a320c88`, все четыре SHA256 совпали,
загружены новые методы/поля, Android 0 ошибок, EditMode 429 passed / 0 failed / 0 skipped. Finish/receive:
paths=[], unexpected=[], result `29583b14aa46c38e0410a831e32b07c17b70e742`.
Windows build 651: Succeeded, 0 ошибок / 105 предупреждений, input `8432b0ce35e6`, тот же Assets-tree.
Аренда освобождена. Побочный PC.asset не импортирован; R сохранён для сопровождающего.
E2E `20261014-211134-dedicated-server-arsenal`: functional 24/24, строгий RED (четыре отклонённых события /
шесть Error-логов SetAvatarRenderMode и SetCurrentHandPose). UpdateTeleportState error=0, dropped=0, native=0.
ID относятся к уже заменённому GhostAvatar netId224; причинная регрессия SDK80/Mirror4 не установлена.
Подробности и границы — [Details.md](Details.md), трасса `reports/2026-10-14-e2e-error-diagnosis.md`.
Повтор той же сборки `20261014-212454-dedicated-server-arsenal`: GREEN/enforce, 24/24, unexpected=0,
known defects=0, native=0, dropped=0. Первый RED сохраняется: окно поздних ghost events нестабильно.
Графический ServerOnly+8 Bots/8 ActiveAvatar: 658 завершён/получен, input `5c5329ec17ba`, Direct3D11,
Ready; три снимка за 92,36 с: precache=false, копии/childCount=0. Это краткая PC-проверка guard, не perf/Quest.
Cleanup Idle; profile/prefs/settings совпали, edit scene Untitled→Offline (infra 7048). Lease освобождена,
R `fa8135f83e01a89f553cb6298ad3285229572c01`, paths=[]unexpected=[]. Режим координации — off.

| Этап | Хаб | Код | Дальше |
|---|---|---|---|
| `teleport-state` (6048) | planned; 643 PASS, Windows 24/24, общий E2E RED | `4224310c4` | уточнить late ghost events → приёмка → вливание |
| `statesync-cost` (5871) | planned после передачи; проверки 643 PASS | `74727da7a` | runtime/Quest → приёмка → вливание |
| `avatar-event-design` | проект одобрен; план для ревью | [контракт v1](contracts/avatar-root-event-admission-v1.md), [план](avatar-root-implementation-plan.md) | ревью плана и способ исполнения → реализация |
| `regression-tests` | planned, after обоих merged | — | после приёмки обоих |

## Сделано (ветка `claude/network`)

- `99163aa9e` — план задачи, перенос этапов `regression-tests` из `state-sync-depth-guard`, `mirror-nt-keyframe`,
  `uniqueid-stale-registry` (их планы ред. 2 без этого этапа), backlog.
- `74727da7a` — **statesync-cost** (раньше `ce7b348eb`, изначально `859b06777`):
  - UltimateXR patch 80 (`Core/StateSync/UxrStateSyncImplementer_1.cs`): кеш `PropertyInfo`/`MethodInfo` по
    (рантайм-тип компонента, имя) в `SyncState` вместо `GetProperty`/`GetMethods`+LINQ на каждое событие. Поведение,
    порядок веток и исключения 1:1; отрицательные результаты не кешируются; только main thread. Патч 74 не тронут.
    Самопроверка вне репозитория (CoreCLR): 351 сравнение со старым кодом, 0 расхождений (Mono/IL2CPP не покрыты).
  - `Assets/Scripts/Network/UxrLogLevelPolicy.cs` (+.meta, guid f417c4c6…): в собранных плеерах (Release и Development)
    уровни логов UltimateXR = min(ассет, Warnings); подробные — при `DebugMode.Enabled` (подписка на `Changed`) или
    флаге `-uxr-log-verbose`. Редактор и `Resources/UxrGlobalSettings.asset` не меняются.
  - Журнал: `changelog/2026-10-14-sdk-80.md`. Компиляция: UltimateXR 555 файлов 0 ошибок, VrBattlegrounds 460 — 0.
- `4224310c4` — **teleport-state** (раньше `6d66c53e2`, изначально `ccfaf5991`):
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

1. Проверки 643 и Windows build 651 завершены. До продуктовых правок уточнить воспроизводимость поздних событий
   GhostAvatar: повтор candidate GREEN, для attribution запрошен сопоставимый baseline у владельца E2E.
   Четыре отправки первого RED идут из Start/Update, до client OnDisable; серверного retired-avatar gate нет.
   658 ServerOnly+8 ботов завершён. Пользователь выбрал подготовку проекта исправления гонки в этой задаче.
   [Проект v1](contracts/avatar-root-event-admission-v1.md): узкий AvatarRoot admission по Mirror netId и SDK UniqueId,
   +20 байт на событие, без нового lifecycle writer/SDK-патча/дополнительного decode. Реализация не начата.
   Уточнение avatar-ik 7223 от 2026-10-15: CombineIdSource должен точно соответствовать текущему netId;
   одной проверки непустого значения недостаточно. Формула SDK и её CPU/GC-проверка добавлены в проект.
   2026-10-15 пользователь одобрил проект для подготовки плана. [План реализации](avatar-root-implementation-plan.md)
   подготовлен: baseline A → helper/relay → существующие проверки и приёмка → новые тесты. Код ещё не менялся.
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
- 2026-10-14, Codex: «Подготовить проект исправления гонки в текущей задаче» — проект v1, без реализации.
- 2026-10-15: «Одобрить проект и подготовить план» — подготовка плана реализации, без запуска implementation.
- 2026-10-14, 6048: «Патч Mirror (Recommended)» — а не обвязка только для аватаров.
- 2026-10-14, 5871: «Все плееры (Recommended)» — ограничение логов в Release и Development.
- Правило по тестам (2026-10-13): «тесты берем когда других задач нет»; тесты — только после приёмки пользователем.

## Риски и ловушки

- Mirror patch 4 при обновлении Mirror переносить вручную. Если ствол NPC когда-нибудь станет дочерним к аватару,
  `WeaponUseContext` в окне снятия получит пустого владельца (сейчас не так).
- Гонка root-события в пути наблюдалась в E2E 651 (первый RED, повтор GREEN). Одобрен проект отсечения
  недоступного UxrAvatar root по spawned+UniqueId; код ещё не написан. Child targets/missing args вне v1.
- Двойной `ChangeAvatar` за кадр («новый аватар в матче» → сразу новый) — вопрос к gameplay, не задан.
- Хук `.claude/hooks/sdk-patch-guard.ps1` требует запись в исторический `Docs/UltimateXR/sdk-patches.md` — игнорировать
  до выпуска сопровождающего (6447/6474); журнал патча — `changelog/` задачи.
- Вне этапа (рекомендации, не делались): `TypeExt.GetType(string)` без кеша на каждое событие (зона Extensions),
  двойная десериализация события на сервере (`NetworkStateRelay.cs:288/290`, зона network).

## Координация

- Старые маршруты Claude очищены; собственные network-sync-hardening/precache routes опубликованы по свежему
  Codex listing (channel codex-app). Ownership/worktree сохранены. Canonical inbox: `python -B -X utf8
  Tools/agents/coordination.py inbox --owner network --unread --as "01a13af5-485c-7ef1-99c6-9e1f1dd753aa"`.
  Повторять при `has_more=true`; `/root` не является межсессионным адресом. СТОП снят объявлением 6805.
- Вопросы пользователю — штатным инструментом вопросов; приёмка пока не запрашивается: сначала Unity-проверки.
