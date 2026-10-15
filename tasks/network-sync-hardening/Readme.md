# Сеть: событие снятого объекта, стоимость StateSync, регрессионные тесты

Обновлено: 2026-10-15, продолжение в Codex. Владелец: эксперт `network`. Worktree:
`F:/CodexWorktrees/network/Vr_Battlegrounds_ai`, ветка `claude/network`.

## Статус

**2026-10-15 пользователь поручил коммит и вливание текущего пакета. Код принят к публикации.**
Это явное разрешение на интеграцию; отдельный результат физической Quest/gameplay-проверки не сообщён.
AvatarRoot-код реализован: Unity/Android PASS, Windows E2E GREEN24/24. Устаревшее ожидание существующего
RemoteCallHashTests обновлено под аргументы root netId+Guid; сигнатура и хэш43519 подтверждены по woven DLL.
Дополнительная717 отменена в QUEUED после длительного ожидания чужого CLI first import: claim/begin/Unity не было.
Нового NUnit-прогона обновлённого теста нет; полный набор GREEN не заявляется. Результат вливания точного SHA — в хабе.
Текущий input `beb0b9ae722c`, worker713 DONE: загружен helper/новые Cmd/Rpc, SHA256 исходников совпали,
Android0errors, полный Result429 сохранён (428PASS, один ожидаемый старый Cmd membership, collision check PASS).
Windows Succeeded0errors/104warnings, buildGuid `62c78db2a0c643fa934844bd517d28a6`; полный player экспортирован в
`Build/avatar-root-beb0-713/e2e/`. E2E `20261015-104950-dedicated-server-arsenal`: GREEN/enforce24/24,
unexpected/known defects/native/dropped=0, три роли одной версии, свои PID/порты освобождены.
Отрицательная Unavailable-ветка этим E2E отдельно не доказана: counters в его отчёт не экспортируются.
Native build reply TimeoutError не повторялся; durable report и независимый BuildReport подтвердили завершение.
Finish713: R `3f37b42cf138cae45c2c2b5525d33123850cb651`, unexpected PC.asset сохранён/не импортирован,
accepted=false относится к выходам worker; lease освобождена. Infra7911. Сводка: `reports/avatar-root-current/summary.json`.

Прежние проверки до реализации AvatarRoot (история других inputs):
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
| `teleport-state` (6048) | принят к публикации; фактический SHA в хабе | `bacf045b5` | остаточные проверки ниже |
| `statesync-cost` (5871) | принят к публикации; фактический SHA в хабе | `dec641fa7` | замеры владельцев и Quest отдельно |
| `avatar-event-design` | принят с реализованным кодом | [контракт v1](contracts/avatar-root-event-admission-v1.md), [план](avatar-root-implementation-plan.md) | фиксация вливания в хабе |
| `avatar-root-admission` | принят к публикации; Android PASS/Windows24/24 GREEN | AvatarRootEventAdmission + relay envelope | negative branch/host-client/Quest/perf отдельно; NUnit717 не запускался |
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
   штатные uint+Guid добавляют 17–21 байт аргументов (поправка review к первоначальным20), без нового lifecycle writer/SDK-патча/decode.
   Уточнение avatar-ik 7223 от 2026-10-15: CombineIdSource должен точно соответствовать текущему netId;
   одной проверки непустого значения недостаточно. Формула SDK и её CPU/GC-проверка добавлены в проект.
   2026-10-15 пользователь одобрил проект для подготовки плана. [План реализации](avatar-root-implementation-plan.md)
   подготовлен: baseline A → helper/relay → существующие проверки и приёмка → новые тесты.
   Позднее пользователь уточнил: до вливания исправить именно гонку; отдельного вливания docs не делать.
   Добавлен код helper/relay; source review без production blockers, текущие713/Windows результаты выше.
   Прежняя706 отменена по уточнению пользователя;710 истекла до claim,713 завершена. Неизвестные операции не replay.
2. Приёмка пользователя (ниже), затем вливание; после вливания statesync-cost — сообщить weapon-system (замер выстрела
   до/после, этап shot-cost) и performance (копия), находка 5871.
3. `regression-tests` после приёмки: StateSyncFrameStack (C2), NetworkTransformKeyframe (Mirror patch 3),
   UniqueIdStaleRegistry (патч 77), DespawnedObjectEventFilter (порядок снятия у удалённого клиента; устарел
   doc-комментарий в `DespawnedObjectEventFilterTests` строки 22-23 «Destroy отложен»), UxrLogLevelPolicy
   (Clamp/VerboseAllowed), StateSyncMethodCache (5 веток кеша).

## Оставшиеся проверки

Пользователь прямо поручил интеграцию текущего пакета. Сценарии ниже сохраняют статус непроверенных
там, где нет соответствующих доказательств; разрешение на вливание не превращает их в PASS.

- Приёмка `avatar-root-admission`: несколько смен тела/смертей/возрождений при подключённом втором клиенте;
  новый аватар видим и управляется, хват/отпускание и финальное освобождение предметов сохраняются,
  после смены карты и переподключения события продолжают доходить без ошибок старого тела.
  Нужны хост + клиент и выделенный сервер + клиент; физический Quest проверяется отдельно.
  Автоматический Windows E2E 713 прошёл 24/24, но попадание недоступного root в защиту им не доказано.
- Приёмка `teleport-state`: e2e dedicated-server-arsenal — 0 `UxrComponentNotFoundException` по `UpdateTeleportState`;
  смена тела/скина хост + клиент и выделенный сервер + клиент без ошибок; у клиентов нет «Отправка состояния: Teleport*»
  после `OnStopClient … (Local)`.
- Приёмка `statesync-cost`: плеер (шлем) без DebugMode — нет per-event логов UltimateXR; с DebugMode или
  `-uxr-log-verbose` — есть; выстрел, хват, перезарядка, поздний вход синхронизируются как прежде.

## Решения пользователя

- 2026-10-14: «сделай все в одной задаче» (teleport-state 6048, uxr-statesync-cost 5871, regression-tests).
- 2026-10-14, Codex: «Подготовить проект исправления гонки в текущей задаче» — проект v1, без реализации.
- 2026-10-15: «Одобрить проект и подготовить план» — подготовка плана реализации, без запуска implementation.
- 2026-10-15: «доки отдельно ненадо вливать исправляй гонку» — реализация в текущей задаче до вливания.
- 2026-10-15: после текущей исправить перезапись _assetId — следующая задача [mirror-preview-scene-objects](../mirror-preview-scene-objects/Readme.md).
- 2026-10-15: «коммить и вливай» — разрешена интеграция текущего пакета кода с документами; результаты
  Quest/gameplay, отрицательной runtime-ветки и performance этим решением не подменяются.
- 2026-10-14, 6048: «Патч Mirror (Recommended)» — а не обвязка только для аватаров.
- 2026-10-14, 5871: «Все плееры (Recommended)» — ограничение логов в Release и Development.
- Правило по тестам (2026-10-13): «тесты берем когда других задач нет»; тесты — только после приёмки пользователем.

## Риски и ловушки

- Mirror patch 4 при обновлении Mirror переносить вручную. Если ствол NPC когда-нибудь станет дочерним к аватару,
  `WeaponUseContext` в окне снятия получит пустого владельца (сейчас не так).
- Гонка root-события в пути наблюдалась в E2E 651 (первый RED, повтор GREEN). Одобрен проект отсечения
  недоступного UxrAvatar root по spawned+UniqueId; код написан, доказательства впереди. Child targets/missing args вне v1.
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
- Вопросы пользователю — штатным инструментом вопросов. Unity/Android и Windows E2E 713 выполнены;
  перед принятым коммитом остаются пользовательская gameplay-проверка и явно указанные ограничения доказательств.
