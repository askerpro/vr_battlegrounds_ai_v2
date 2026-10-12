# death-body-defer — детали этапа deferred-body

2026-10-12. Находки реализации; решения — в [Readme.md](Readme.md).

## API синхронизации UltimateXR

- `UxrStateSyncImplementer.SyncCallDepth` (static, `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/StateSync/UxrStateSyncImplementer.cs:55`):
  растёт в `BeginSync` (`UxrStateSyncImplementer_1.cs:168`), падает в `EndSyncState`/`CancelSync`
  (`UxrStateSyncImplementer_1.cs:190-192`, `253-259`). Событие уходит в сеть только с глубины 1
  (`UxrManager.cs:1819`, `UseTopLevelStateChangesOnly`).
- `UxrManager.Instance.IsInsideStateSync` (`UxrManager.cs:186`) — исполнение принятого события
  (`ExecuteStateSyncEvent`, `UxrManager.cs:657-667`).
- Условие «верхний уровень» — как `WeaponSystem.CanCommandNow` (`Assets/Scripts/Weapons/WeaponSystem/WeaponSystem.cs:443-444`):
  `SyncCallDepth == 0 && !(UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync)`.
  В `AvatarManager` — `CanReplaceBodyNow()`.

## Вызывающие смены тела

| Вызов | Откуда | Внутри синхронизации? |
|---|---|---|
| `ServerReconcileBody("смерть")` | `PlayerController.Die` ← `OnActorDied` ← `UxrActor.DieInternal` | да (сервер, `BeginSync` смерти) → отложенно |
| `ServerReconcileBody(reason)` | `ServerEliminateSilently`: `EliminationMode` «бой кончился» (тик фазы) / «новый аватар в матче» (`Begin`, `Admit` в `SpawnAvatar`/`ReplaceBody`) | нет (см. раздел ниже); механизм всё равно решает сам |
| `ServerReconcileBody("возрождение")` | `PlayerController.Respawn` ← фазы раунда | обычно нет → сразу |
| `ChangeAvatar` | `SessionTeamAssigner.Apply` (выбор игрока, админ, автобаланс режима, боты), `PlayerSession` Cmd, `GameNetworkManager.OnServerReady`, `BotStandSession`, E2E | обычно нет; если да → отложенно с изъятием |

`ReplaceBody` остаётся единственным исполнителем; вызывают его только `ChangeAvatar`, `ReconcileBodyNow`
и исполнение очереди.

## Очередь и семантика слияния

- Одна запись на сессию (`PendingBody`: сессия, тело на момент постановки, последний reason, флаг `Change`).
- `Change` липкий (ИЛИ): `ChangeAvatar` в кадре — замена безусловная, снаряжение изымается
  (`ConfiscateBeforeDestroy`). Команда/скин уже записаны в сессию синхронно, поэтому параметры
  `ChangeAvatar` отдельно не хранятся; префаб выбирается при исполнении по сессии (выбывший — призрак).
- Желаемое тело выбирается при исполнении: смерть + возрождение одного кадра → ноль замен.
- Запись о другом теле (прямой вызов с верхнего уровня уже сменил тело) при новой постановке заменяется
  целиком; при исполнении — снимается.
- Исполнение: `AvatarManager.LateUpdate`. Сначала: сервер не активен → очередь снимается (Info);
  не верхний уровень → ждать следующего `LateUpdate`; `NetworkServer.isLoadingScene` → ждать
  (после загрузки тела нет → запись снимется; отменённая смена — исполнится).
- Устаревшая запись: сессия уничтожена/не на сервере; тело сменилось или пропало (смена карты,
  отключение, прямой вызов). Снимается молча с `GameLog.Player.Info`. Исключение одной записи
  ловится (`GameLog.Error`) и не мешает остальным.

## Отпускание при смерти

- Снята подписка `PlayerGrabManager` на `PlayerDied` и вызов `ReleaseAllGrabbedObjects` из
  `PlayerController.RpcOnDied` (RPC оставлен как лог-маркер: удаление требует правки
  `Assets/Tests/EditMode/Network/RpcCarriesNoStateTests.cs`, вне writes этапа accepted-tests).
- Единственный отпускающий при смерти — сервер, `AvatarTeardown.ReleaseBeforeDestroy` в `ReplaceBody`,
  теперь на верхнем уровне (`LateUpdate`).
- Условие network (ревью 4388, вариант а): смерть без смены тела (стратегия без призрака, `ghostPrefab == null`,
  или префаб не нашёлся). `ReconcileBodyNow` при «замены нет, тело мертво (`!IsAlive`), тело не призрак» зовёт
  `AvatarTeardown.ReleaseHandsOnServer` — только руки, тот же `ReleaseHands` (одно `ReleaseObject` на руку),
  гейт `NetworkServer.active && StateEventAuthority.IsWorldAuthority`. Снаряжение и планшет не трогаются.
  `IsAlive` после `Die` ложен по обоим признакам: `Life = 0` (`DieInternal`) и выбывание на сессии.
  Призрак исключён: он и есть тело выбывшего и держит свой планшет (на хосте сервер видит его руки).
  Возрождение (тело живо) ничего не отпускает. `ReconcileBodyNow` пишет `GameLog.Error`, если вызван
  не с верхнего уровня (по построению недостижимо).

## Записи внутри DieInternal (вне этапа)

Только чтение, для будущего этапа; не исправлялось.

**`PlayerController.ServerEliminateSilently`** — две точки вызова, обе вне синхронизации:

- `EliminationMode.cs:958` «бой кончился»: `EliminationMode.Update` (`:465-468`) → `ServerTick` (`:477`) →
  `ServerSetRoundPhase` (`:515`, `:904`) → при `Resolution` `ServerEndRoundDeaths` (`:925-927`) → `:958`.
  Верхний уровень: смерть только ставит заявку `RoundPhases.RequestRoundEnd` (`RoundPhases.cs:266`, фазу не
  меняет), фазу меняет ближайший тик. Значит, `Life = 0` выживших — отдельное событие глубины 1.
- `EliminationMode.cs:842` «новый аватар в матче»: `ServerAdmitAvatar` ←
  (а) `Begin` (`EliminationMode.cs:375`, корутина старта режима) — верхний уровень;
  (б) `AvatarManager.Admit` (`AvatarManager.cs:579`) ← `SpawnAvatar` (`PlayersManager.cs:124`,
  `BotDirector.cs:343`, `BotStandSession.cs:63`, отложенный допуск `MapRunAdmission`) — верхний уровень;
  ← `ReplaceBody` (`AvatarManager.cs:502`, только при `oldAvatar == null`) — после этого этапа
  `ReplaceBody` исполняется только с верхнего уровня (`ChangeAvatar`/`ReconcileBodyNow`/очередь).
  До этапа он мог идти изнутри `DieInternal`, но с прежним телом (`continuesPrevious: true`) — без этого вызова.

**`PlayerLoadoutManager.ServerHandlePlayerDied`** (`PlayerLoadoutManager.cs:104-105` подписка в
`OnStartServer`, `:148`) зовёт **тот же** `ServerDropEquipment("погиб")` (`:161`), что и
`AvatarTeardown.ReleaseBeforeDestroy` (`AvatarTeardown.cs:59`). Якоря одни и те же — карманы и кобуры самого
аватара (`AnchorRole.IsAvatarPocket`, `:170`; `PlayerLoadoutManager` — компонент аватара), операция —
`UxrGrabManager.RemoveObjectFromAnchor(item, true, true)` (`:175`). Разница — момент: при смерти она
исполняется на сервере внутри `DieInternal` (обработчик `PlayerDied`), глубина > 1, а
`RemoveObjectFromAnchor` открывает `BeginSync` без `IgnoreNestingCheck`
(`UxrGrabManager.Manipulation.cs:393`); при `UseTopLevelStateChangesOnly = true` (`UxrManager.cs:170`, проект
не меняет) вложенное событие отдельно не уходит (`UxrManager.cs:1819`). Клиент, повторяя `DieInternal`, свой
`ServerHandlePlayerDied` пропускает (`!isServer`). Потом верхнеуровневый `ServerDropEquipment` из
`ReleaseBeforeDestroy` находит карманы на сервере уже пустыми и ничего не шлёт. **Риск:** у клиентов предмет может
остаться в кобуре мёртвого тела до его уничтожения. В варианте без призрака (замены нет) верхнеуровневого
повторения нет вовсе. Кандидат будущего этапа: снять подписку или отложить сброс до верхнего уровня
(тот же механизм, что у тела).

## death-equipment-drop

2026-10-12. Закрывает риск `PlayerLoadoutManager.ServerHandlePlayerDied` из предыдущего раздела (согласие network 4396).

**Правка.** Снята подписка `PlayerLoadoutManager` на `PlayerDied` (вместе с полем `_player`, которое служило только ей),
удалён `ServerHandlePlayerDied`. `ServerDropEquipment` — единственный путь сброса; зовёт его только `AvatarTeardown`
на сервере. `AvatarTeardown.ReleaseHandsOnServer` заменён на `ReleaseDeadBodyOnServer` (гейт
`NetworkServer.active && StateEventAuthority.IsWorldAuthority`): руки, затем снаряжение — общим private-шагом
`ReleaseHandsAndEquipment`, тем же, что в `ReleaseBeforeDestroy` (его поведение не менялось). Планшет
(`ServerReleaseLocalItems`) мёртвому телу без замены не трогается. `AvatarManager.ReconcileBodyNow` в ветке
«замены нет, тело мертво, не призрак» зовёт новый метод.

**Кто сбрасывает снаряжение** (`ServerDropEquipment`, rg по Assets/Scripts и Assets/Editor) — только `AvatarTeardown`:

| Путь | Откуда | Уровень |
|---|---|---|
| `ReleaseBeforeDestroy` | `AvatarManager.ReplaceBody` (не `confiscate`): смерть → призрак, выбывание без смерти → призрак | верхний (`ChangeAvatar`/`ReconcileBodyNow`/очередь `LateUpdate`) |
| `ReleaseBeforeDestroy` | `GameNetworkManager.OnServerDisconnect`, `BotDirector` («бот убран»), `BotStandSession` («стенд завершён») | верхний; смерть не касается |
| `ConfiscateBeforeDestroy` → `ReleaseBeforeDestroy` | `ReplaceBody(confiscate)` ← `ChangeAvatar` | сначала `EquipmentStrip.ServerStrip` уничтожает, сброс находит пусто |
| `ReleaseDeadBodyOnServer` | `ReconcileBodyNow`, замены нет (нет `ghostPrefab`, префаб не нашёлся) и тело мертво, не призрак | верхний |

Подписчики `PlayerDied` после правки: `TeamSpawnZone`, `GhostViewEffect`, E2E `PlayerDeathSignalScenario` — все локальные,
снаряжения не касаются. Режимы (`EliminationMode.OnPlayerDied` через `MapReferee`), экономика (`StartingSidearmPolicy`,
`RoundMagazineRefill`) на сброс при смерти не опирались. В тестах (`Assets/Tests`, только чтение) ни `ServerHandlePlayerDied`,
ни `ReleaseHandsOnServer` не упоминаются; `MagazineRefillPlannerTests.Менеджер_магазинов_не_знает_о_режимах` читает
исходник `PlayerLoadoutManager.cs` — запрещённых слов правка не добавила.

**Ровно один сброс при смерти.**

- С призраком: `Die` → `ReconcileBody("смерть")` → очередь → `LateUpdate` → `ReconcileBodyNow` → `ReplaceBody` →
  `ReleaseBeforeDestroy` → `ServerDropEquipment("смерть")` → `NetworkServer.Destroy(тело)`. Раньше тот же сброс шёл ещё раньше,
  внутри `DieInternal`, и верхнеуровневый находил кобуры пустыми; теперь он единственный.
- Без призрака: та же очередь → `ReconcileBodyNow` → `ReleaseDeadBodyOnServer` (руки + снаряжение). Раньше снаряжение
  ронялось только внутри `DieInternal` (до клиентов не доходило), руки — здесь.
- Повторный `ReconcileBodyNow` мёртвого тела (например, `ServerEliminateSilently` на мёртвом теле живой ещё сессии) снова
  зовёт метод — руки, кобуры и карман уже пусты, снимать нечего.
- Смерть и `ChangeAvatar` в одном кадре (липкий `Change`): изъятие (`EquipmentStrip.ServerStrip`) уничтожает снаряжение,
  сброс находит пусто. Раньше сброс внутри `DieInternal` успевал уронить оружие, и оно переживало смену ничьим; теперь —
  ровно правило слияния из раздела «Очередь».

**`ServerEliminateSilently`** (`Life = 0` без `Died`, с верхнего уровня; ранний выход, если тело мертво и сессия выбыла —
поэтому после настоящей смерти не повторяется):

- «бой кончился» (`ServerSetRoundPhase(Resolution)`): сначала `ServerEndRoundDeaths` → для живых `ServerEliminateSilently` →
  `ReconcileBodyNow` сразу. С призраком: `ReleaseBeforeDestroy` роняет оружие из кобур на пол, магазины уничтожаются, тело
  снимается (так было и до этапа). Без призрака: `ReleaseDeadBodyOnServer` — то же без уничтожения тела (до этапа — только руки,
  кобуры оставались). Затем `EquipmentStrip.ServerStripAll("конец раунда")`: у тел (призраки без `PlayerLoadoutManager`,
  мёртвые тела без снаряжения) уже нечего забирать, `LooseItems.RemoveAll` убирает упавшее с пола. Итог для игры прежний —
  снаряжение раунд не переживает; цена — лишняя пара «снять с якоря → уничтожить» по сети в том же кадре.
- «новый аватар в матче» (`ServerAdmitAvatar` из `Begin` / `AvatarManager.Admit` при спавне): тело только что создано,
  руки и кобуры пусты — сброс ничего не роняет (без призрака `ServerClearMagazines` чистит пустой карман).

**Без `AvatarManager.Instance`** (`PlayerController.ReconcileBody` молчит): сброса при смерти больше нет. В коде
(`Assets/Scripts`, `Assets/Editor`) таких путей нет: игровые сцены, E2E-сценарии и `BotStandSession` работают через
`AvatarManager`. EditMode-тесты смерти (`PlayerLifeRulesTests`, `SeriesStatsTests`, `RespawnSubscriptionTests`, `BotTests`,
`OverviewStateReaderTests`) `PlayerLoadoutManager` не создают и сброса не проверяют; `OnStartServer` менеджера (где была
подписка) в EditMode не вызывался. Вход для `accepted-tests`: теста на сброс при смерти (оба пути) нет — добавить после приёмки.

**Итоговый порядок при смерти.**

Сервер, кадр смерти:

1. `UxrActor.DieInternal`: `BeginSync`, `Life = 0`, `Died` → `OnActorDied`: `DeathDropEjection.ScheduleFor` (толчок
   предметам в руках и кобурах), `PlayerDied` (локальные подписчики), `Die()`: выбывание на сессии, `RpcOnDied`,
   `MapReferee.OnPlayerDied`, `ServerBecomeCorpse`, `ReconcileBody("смерть")` → очередь. Записей SDK о снаряжении нет.
2. `EndSync` — событие смерти уходит клиентам (тело ещё живо в сети).
3. `AvatarManager.LateUpdate` → `ReconcileBodyNow`:
   - с призраком: `ReplaceBody` → спавн призрака → `ReleaseBeforeDestroy`: локальное (`ServerReleaseLocalItems`), руки
     (`ReleaseObject` на руку), снаряжение (`RemoveObjectFromAnchor` из кобур, магазины `NetworkServer.Destroy`) →
     `NetworkServer.Destroy(тело)`;
   - без призрака: `ReleaseDeadBodyOnServer`: руки, затем снаряжение; тело остаётся.

Клиенты: событие смерти (`DieInternal` повтором: `Died` → `ScheduleFor`, `PlayerDied`; `Die` — заглушка), затем
`ServerBecomeCorpse` (Rpc); затем верхнеуровневые события сервера каналом состояния — отпускание рук, снятие оружия
с якорей (предмет становится динамическим и получает толчок `DeathDropEjection`), уничтожение магазинов (Mirror),
`SyncList` кармана пуст; с призраком — спавн призрака и уничтожение тела (Mirror, после событий UltimateXR тем же
надёжным каналом).
