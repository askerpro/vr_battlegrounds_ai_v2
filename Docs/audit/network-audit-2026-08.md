# Аудит сетевого кода и синхронизации — август 2026

> **Статус: справочник.** Этот файл фиксирует состояние на момент аудита и не меняется
> по мере исправлений. Прогресс работ — в [`Docs/tasks/README.md`](../tasks/README.md).
>
> Разбор корневых причин — в [`architecture-review-2026-08.md`](architecture-review-2026-08.md).
> План проверки — в [`Docs/testing.md`](../testing.md).

**Ветка:** `add-guns` · **HEAD:** `176e657` · **Дата:** 2026-08-16
**Охват:** 78 скриптов `Assets/Scripts/` + Mirror-интеграция UltimateXR
**Метод:** чтение кода, проверка префабов и сцен по GUID. Сцены целиком не открывались.

| Серьёзность | Кол-во |
|---|---|
| Критично | 4 |
| Высокий | 7 |
| Средний | 7 |
| Низкий | 3 |
| **Всего** | **21** |

Из них 13 — производные от пяти корневых решений, 8 — самостоятельные.
Таблица соответствия — в [`architecture-review-2026-08.md`](architecture-review-2026-08.md#что-чинится-само).

---

## Сеть и авторитет (NET)

### NET-01 · Критично · Задокументированный патч UltimateXR не применён

**Где:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Networking/Integrations/Net/Mirror/UxrMirrorAvatar.cs:476`
**Задача:** [T-12](../tasks/T-12-state-channel-to-session.md)

`Docs/UltimateXR/sdk-patches.md`, Патч 1 прямо предписывает: изменить
`private static bool _initialStateLoaded` → `private bool`. В коде поле по-прежнему
статическое. Рядом появилось второе статическое поле `_serverBroadcaster`, которого
в документе нет вообще (нарушение жёсткого правила о фиксации правок SDK).

**Последствие.** Возвращается ровно тот баг, который патч описывает: `_initialStateLoaded`
общий на весь процесс, поэтому второй клиент начинает применять `RpcComponentStateChanged`
до получения глобального состояния сцены. Проявляется как «предметы не там» и
`UxrComponentNotFoundException` в связке выделенный сервер + клиент.

**Проверяется:** уровень 4 (выделенный сервер + 2 клиента). На хосте не воспроизводится.

---

### NET-02 · Критично · После смены аватара сервер перестаёт рассылать state-sync

**Где:** `UxrMirrorAvatar.cs:191, 203, 477` → `Assets/Scripts/Player/Avatars/AvatarManager.cs:138-143`
**Задача:** [T-12](../tasks/T-12-state-channel-to-session.md)

`_serverBroadcaster` — статическая ссылка на аватар, который на сервере вещает события
`UxrManager.ComponentStateChanged` клиентам. Назначается в `OnStartServer` только если
поле пустое, обнуляется в `OnStopServer`. `AvatarManager.ChangeAvatar` сначала спавнит
новый аватар, потом уничтожает старый:

```
Spawn нового  → OnStartServer видит непустое поле → вещателем не становится
Destroy старого → OnStopServer обнуляет поле       → вещателя не остаётся
```

**Триггеры:** смена команды, смена скина, **любая смена карты**
(`GameNetworkManager.OnServerReady` пересоздаёт аватар через тот же `ChangeAvatar`).

**Последствие.** Здоровье и смерти перестают доезжать до клиентов. С двумя и более
игроками поле может случайно перехватиться следующим аватаром — баг плавающий.

---

### NET-03 · Высокий · На хосте каждое событие синхронизации уходит дважды

**Где:** `UxrMirrorAvatar.cs:189` (OnStartServer) и `:227` (OnStartLocalPlayer)
**Задача:** [T-12](../tasks/T-12-state-channel-to-session.md)

Оба метода делают `UxrManager.ComponentStateChanged += ...`. На хосте для собственного
аватара выполняются оба. Комментарий на строке 247 утверждает «Нам не нужно подписываться
дважды», но сама подписка стоит выше проверки `isServer` и выполняется безусловно.

**Последствие.** Двойной `RpcComponentStateChanged` на каждое изменение состояния.
Отписка при этом одна — на хосте остаётся висящий обработчик.

---

### NET-04 · Высокий · `NetworkClient.localPlayer` — это сессия, а не аватар

**Где:** `Assets/Scripts/Maps/TeamSpawnZone.cs:131-139`
**Задача:** [T-11](../tasks/T-11-session-avatar-link.md)

В `PlayersManager.CreatePlayerSession` объектом игрока для Mirror назначается
`PlayerSession` (`NetworkServer.AddPlayerForConnection(conn, sessionGO)`). Аватар
с `PlayerController` — отдельный заспавненный объект. Поэтому
`NetworkClient.localPlayer.GetComponent<PlayerController>()` возвращает `null` всегда.

**Последствие.** `_localPlayer` не находится никогда. В фазе `Combat` ветка «локальный
игрок ещё не заспавнился» скрывает зону у всех — мёртвые игроки своей команды её не
увидят. Плюс `GetComponent` дёргается каждый кадр в `Update` у каждой зоны.

> **Подтверждено тестом 2026-08-17** (`ActiveAvatar_не_долетает_до_клиента`).
> `PlayerSession.ActiveAvatar` — обычное C#-свойство, не `SyncVar`; единственная сетевая
> связь идёт в обратную сторону, через `PlayerController.SessionNetId`.
>
> **Исправлено 2026-08-18 (T-11).** Связь несёт `[SyncVar(hook)] ActiveAvatarNetId`
> на сессии, зона подписана на `PlayerSession.LocalAvatarChanged` и больше не опрашивает
> `Update`. Тест перевёрнут в `ActiveAvatar_долетает_до_клиента` — был красным до правки.
>
> **Остаток находки, вскрытый той же правкой (не исправлен).** Зона обновляет видимость
> по событию `PlayerController.PlayerDied`, а оно поднимается внутри `Die()`, помеченного
> `[Server]`. На выделенном сервере до клиента это событие не доходит вообще: у клиента
> `Die()` — заглушка, а `RpcOnDied` о смерти никого не оповещает. Значит мёртвый игрок
> увидит свою зону не в момент гибели, а только на ближайшей смене фазы раунда.
> На хосте не воспроизводится — там `Die()` исполняется по-настоящему. Из T-11 это уже
> не связь сессия ↔ аватар, а тот же класс проблемы, что NET-06: сигнал фазы/состояния
> ходит через `[Server]`+`Rpc` вместо реплицируемого состояния — см. T-13.
>
> Важно, почему это не всплывало в Play Mode: **host-режим маскирует находку полностью.**
> `NetworkClient.OnHostClientSpawn` кладёт в `NetworkClient.spawned` ссылку на тот же
> объект из `NetworkServer.spawned` — сериализации нет вообще, «клиент» видит серверные
> поля напрямую. Любой тест этой связи на host-режиме будет ложно-зелёным.
> Поэтому в харнессе для проверки репликации введён ярус A+ с прогоном через настоящие
> `SerializeServer` / `DeserializeClient`.

---

### NET-05 · Высокий · Восстановление сессии не сохраняет здоровье и позицию

**Где:** `Assets/Scripts/Managers/PlayersManager.cs:140`
**Задачи:** [T-04](../tasks/T-04-session-recovery-avatar-ref.md) (временно), [T-11](../tasks/T-11-session-avatar-link.md) (окончательно)

Та же путаница, что в NET-04: `conn.identity.GetComponent<PlayerController>()` при
отключении. `conn.identity` — это `PlayerSession`, поэтому `avatar` всегда `null`.

**Последствие.** В `SessionRecoveryManager` ветка сохранения физического состояния не
выполняется никогда: `Health`, `Position`, `Rotation` остаются нулями,
`NeedsPhysicalRestore` — всегда `false`. Весь код восстановления позиции
в `AvatarManager.SpawnAvatar` — мёртвый.

---

### NET-06 · Высокий · На выделенном сервере арсенал не пополняется

**Где:** `GameModes/EliminationMode/EliminationMode.cs:334` → `Arsenal/ArsenalWallController.cs:329`
**Задача:** [T-13](../tasks/T-13-round-phase-as-state.md)

Смена фазы раунда рассылается через `[ClientRpc] RpcOnRoundStateChanged`, который
поднимает статическое событие `OnRoundStateChangedLocal`. В режиме `ServerOnly`
ClientRpc локально не исполняется — событие на сервере не срабатывает.

**Последствие.** `ArsenalWallController.HandleRoundStateChanged` с веткой
`case RoundState.Setup → if (isServer) ReplenishWeaponsNetwork(false)` не вызывается.
На хосте оружие в слотах появляется, на выделенном сервере — нет.
Сам `if (isServer)` внутри клиентского обработчика показывает, что путь задумывался серверным.

---

### NET-07 · Средний · Состояние стены арсенала не реплицируется

**Где:** `Arsenal/ArsenalWallController.cs:37, 268-310` · `Arsenal/DogTagController.cs:94`
**Задача:** [T-15](../tasks/T-15-arsenal-state-replication.md)

Класс наследует `NetworkBehaviour`, но `_currentState` — обычное поле, а `Lock()`/`Unlock()`
и анимация выполняются локально на каждой машине независимо. Захват жетона
(`HandleTagGrabbed`) закрывает арсенал только у того клиента, где сработал
`UxrGrabbableObjectAnchor.Removed`.

**Последствие.** Один игрок берёт жетон — у него слоты заблокированы, у остальных нет.
Подключившийся позже видит арсенал в состоянии по умолчанию (`Closed`) независимо от фазы.

---

### NET-08 · Средний · SyncVar записывается изнутри ClientRpc

**Где:** `EliminationMode.cs:358`
**Задача:** [T-13](../tasks/T-13-round-phase-as-state.md)

`RpcOnRoundStarted` присваивает `_currentRound` — это `SyncVar`. На клиенте такая запись
локальна и будет затёрта следующей синхронизацией с сервера.

---

### NET-09 · Средний · Таймеры раунда синхронизируются каждый кадр

**Где:** `EliminationMode.cs:174-175`
**Задача:** [T-19](../tasks/T-19-round-timers-networktime.md)

`_roundTimer` и `_countdownTimer` — `SyncVar`-float'ы, которым присваивается новое значение
в `Update`. Объект помечается грязным каждый кадр до конца матча.

**Последствие.** Постоянный фоновый трафик и дёрганый таймер в HUD. На Quest по Wi-Fi —
лишняя нагрузка там, где её легко избежать.

---

### NET-10 · Низкий · Хранилище отключённых сессий растёт без ограничений

**Где:** `Managers/SessionRecoveryManager.cs:36`
**Задача:** [T-20](../tasks/T-20-session-recovery-ttl.md)

`_disconnectedSessions` пополняется при каждом отключении и очищается только при успешном
переподключении того же `deviceToken`. Ни TTL, ни лимита.

---

### NET-11 · Низкий · Статические ссылки на локальные объекты не сбрасываются

**Где:** `Player/PlayerSession.cs:18` · `UxrMirrorAvatar.cs:314`
**Задача:** [T-18](../tasks/T-18-local-refs-cleanup.md)

`PlayerSession.LocalSession` присваивается в `OnStartClient` и не обнуляется при отключении.
`UxrMirrorAvatar.OnNetworkSceneChanged` — публичный метод, который должен сбрасывать
`_initialStateLoaded` при смене сцены, но его никто не вызывает: мёртвый код.

---

### NET-12 · Средний · `TeamRuntimeData` падает без `PlayersManager`

> Найдено 2026-08-17 при построении сетевого харнесса (T-26).

**Где:** `Assets/Scripts/GameModes/TeamRuntimeData.cs`

Свойство `Sessions` разыменовывает `PlayersManager.Instance` без проверки на `null` —
NRE в `EliminationMode.PrepareNextRound`.

**Последствие.** Жёсткая связка игрового режима с глобальным менеджером: без
`PlayersManager` режим не запускается вообще. Это же делает режим непроверяемым
в изоляции — очередной случай Корня 5.

---

## Логика матча (MATCH)

### MATCH-01 · Критично · Двойная подписка удваивает счёт сетов

**Где:** `EliminationMode.cs:121` (InitializeActiveGame) и `:201` (StartNextSet)
**Задачи:** [T-02](../tasks/T-02-fix-double-subscribe.md) (временно), [T-09](../tasks/T-09-explicit-round-fsm.md) (окончательно)

`InitializeActiveGame` делает `_setManager.SetEnded += OnSetEnded`. Затем вызывает
`StartNextSet`, который подписывается ещё раз. В списке делегата два одинаковых обработчика.

**Последствие.** При завершении сета `OnSetEnded` выполняется дважды подряд (список рассылки
фиксируется в момент вызова, отписка внутри первого прохода второй не отменяет). Победителю
начисляется **2 очка вместо 1**, и `StartNextSet` запускается дважды — второй запуск
сбрасывает `_syncedRoundScores` и стартует параллельный сет поверх первого.

При `_maxSets = 5` порог победы — 3 сета. С удвоением команда выигрывает матч за два сета.

---

### MATCH-02 · Высокий · Фазы Resolution и Scoreboard не проигрываются никогда

**Где:** `GameModes/EliminationMode/RoundManager.cs:70-82, 141-160` → `SetManager.cs:115-116`
**Задача:** [T-09](../tasks/T-09-explicit-round-fsm.md)

`EndRound` ставит состояние `Resolution` и синхронно поднимает `RoundEnded`. Подписчик
`SetManager.OnRoundEnded` тут же вызывает `StartNextRound`, а тот через `StartRound`
переводит состояние обратно в `Setup` — всё в пределах одного кадра.

**Последствие.** Ветки `case Resolution` и `case Scoreboard` в `Tick` недостижимы.
Константы `ResolutionDuration = 3.0f` и `ScoreboardDuration = 5.0f` ни на что не влияют:
паузы после победы и экрана итогов в игре нет.

---

### MATCH-03 · Высокий · Оружие не блокируется на клиентах вне фазы боя

**Где:** `Managers/GameplayManager.cs:49-66`
**Задача:** [T-13](../tasks/T-13-round-phase-as-state.md)

`Update` выполняется на всех машинах, но читает `_gameMode` — обычное поле, которое
заполняется только в `[Server] StartGameplay`. На клиенте оно `null`, проверка
`_gameMode is EliminationMode elim` не проходит, `weaponsEnabled` остаётся `true`.

**Последствие.** Блокировка стрельбы в фазах `Equipment` и `Countdown` работает только
на хосте. На обычном клиенте можно стрелять до начала раунда.

---

### MATCH-06 · Высокий · Сет не может закончиться по исчерпанию раундов

> Найдено 2026-08-17 юнит-тестами, при аудите пропущено.
> Подтверждено: `SetManagerScoringTests` — три падающих теста.

**Где:** `Assets/Scripts/GameModes/EliminationMode/SetManager.cs:115`
**Задача:** [T-25](../tasks/T-25-setmanager-round-counter.md)

`SetManager.OnRoundEnded` в ветке «сет продолжается» вызывает
`_roundManager.StartNextRound(_eliminationMode)` — метод **`RoundManager`** — вместо
собственного приватного `SetManager.StartNextRound()`. У двух классов методы называются
одинаково, и вызван не тот.

```csharp
private void StartNextRound()          // SetManager — инкрементирует _currentRound
{
    _currentRound++;
    _eliminationMode.RpcOnRoundStarted(_currentRound);
    _roundManager.StartRound(...);
}

// а в OnRoundEnded вызывается:
_roundManager.StartNextRound(_eliminationMode);   // ← RoundManager, счётчик не трогает
```

**Последствия.** `SetManager._currentRound` увеличивается ровно один раз — в `StartSet`.
Отсюда:

1. Условие `_currentRound >= _roundsPerSet` не выполняется никогда. Сет может закончиться
   только по достижению порога побед (`roundsToWin`). При чётном числе раундов или серии
   ничьих сет не заканчивается вовсе.
2. `RpcOnRoundStarted` отправляется только для первого раунда — у клиентов номер раунда
   навсегда остаётся `1`, сколько бы их ни прошло.

**Воспроизведение.** `SetManagerScoringTests` с `roundsPerSet = 2`: счёт 1:1, 1:0 и 0:0
после двух раундов — `SetEnded` не срабатывает ни разу.

**Исправлено 2026-08-17** ([T-25](../tasks/T-25-setmanager-round-counter.md)): все 6 тестов
зелёные.

> **Остаток проблемы — второй владелец перехода.** `RoundManager.Tick` в фазе `Scoreboard`
> (`RoundManager.cs:157`) тоже вызывает `StartNextRound(_eliminationMode)` — свой метод, —
> и этот путь так же минует счётчик `SetManager` и `RpcOnRoundStarted`.
>
> Сейчас ветка недостижима из-за MATCH-02 (фаза `Scoreboard` не проживает ни кадра).
> Но как только MATCH-02 будет исправлена, путь станет живым, и MATCH-06 вернётся
> через него. Юнит-тесты этого не заметят: они дёргают `EndRound()` напрямую,
> не прокручивая `Tick`.
>
> Значит у перехода «раунд → раунд» два владельца — ровно та болезнь, которую описывает
> Корень 4. Устранять в [T-09](../tasks/T-09-explicit-round-fsm.md), там это добавлено
> в требования.

---

### MATCH-04 · Не подтверждено · Определение ничьей на нулевом счёте

**Где:** `SetManager.cs:86-107` · `EliminationMode.cs:227-245`
**Задача:** [T-08](../tasks/T-08-fix-tie-detection.md)

> **Поправка 2026-08-17.** Находка в исходной формулировке **не воспроизвелась**.
> Тесты `Счёт_2_1_отдаёт_победу_а_не_ничью` и `Досрочная_победа_при_достижении_порога`
> проходят: победитель определяется верно. Ветка `isTie = true` при нулевом счёте
> действительно срабатывает, но её всегда перебивает последующее `kvp.Value > highestRounds`,
> а случай «все на нуле» и есть ничья.
>
> Первый прогон теста показал обратное, но это был артефакт харнесса: тест создавал
> `TeamData` на лету с `teamIndex` 0 и 1, а в реестре команды имеют индексы 1 и 2.
> Именно этот эпизод вскрыл настоящую проблему — см. ниже.

**Что осталось реальным.** Обе функции определяют победителя через
`TeamRegistry.Instance.GetByIndex(kvp.Key)` — то есть через глобальный синглтон, а не через
массив `teams`, который им передали в `StartSet`. Скрытая зависимость: при индексе, которого
нет в реестре, победитель молча становится `null`, то есть «ничья». Логика подсчёта верна,
а вот способ получить объект команды — нет.

**Последствие.** Пока реестр и переданные команды согласованы, всё работает. Рассогласование
(новая команда, тест, изменение индексов) даёт тихую ничью вместо победы. Плюс это делает
подсчёт очков непроверяемым без загруженного реестра.

**Что делать** — сузилось: брать команду из переданного массива, а не из реестра.
Логику `isTie` можно оставить, но стоит переписать в два прохода ради читаемости.

---

### MATCH-05 · Средний · Обработчики отложенного респавна накапливаются

**Где:** `EliminationMode.cs:316-328`
**Задача:** [T-10](../tasks/T-10-respawn-subscription-cleanup.md)

Для мёртвого игрока вне зоны вешается одноразовый `zone.PlayerEntered += onEntered`.
Он снимается только если игрок вернулся.

**Последствие.** За несколько раундов на зоне копятся обработчики от прошлых раундов.
Игрок, зашедший в зону в разгар боя, может получить `Respawn` от старой подписки.
Плюс замыкание держит ссылку на уничтоженный `PlayerController`.

---

## VR-механики и калибровка (VR)

### VR-01 · Высокий · Калибровка роста меняет геометрию только локально

**Где:** `PhysicalSpaceUtils/PhysicalSpaceSyncManager.cs:346-398`
**Задача:** [T-14](../tasks/T-14-sync-calibration-scale.md)

`ApplyScale` масштабирует `Dummy Forward` под рост игрока и через рефлексию правит
внутренние векторы `UxrBodyIK`. Всё это — локальные изменения. У `Dummy Forward` нет
своего `NetworkTransform`.

**Последствие.** Игрок ростом 1.5 м видит себя корректно, а на чужих экранах его аватар
остаётся в исходных пропорциях. Коллайдеры едут за костями — стрелять надо в одно место,
а видно другое.

Смещение высоты (`_accumulatedHeightOffset`) при этом реплицируется: оно двигает
`CameraController`, а на камере `NetworkTransform` в мировых координатах есть.
**Расходится именно масштаб.**

> Требует подтверждения в редакторе: предположение о выключенном `syncScale` на префабе
> аватара сделано по коду настройки UltimateXR, сам префаб не открывался.

---

### VR-02 · Высокий · Высота умножается сама на себя при калибровке комнаты

**Где:** `PhysicalSpaceSyncManager.cs:429`
**Задача:** [T-03](../tasks/T-03-fix-apply-avatar-transform.md)

```csharp
Vector3 newPosition = TransformRealToVirtual(UxrAvatar.LocalAvatar.transform.position);
newPosition = Vector3.Scale(newPosition, new Vector3(1, UxrAvatar.LocalAvatar.transform.position.y, 1));
```

Второй строкой Y умножается на текущую Y аватара. Замысел, судя по всему, был
«оставить Y без изменений», но `Vector3.Scale` перемножает компоненты.

**Последствие.** При `y = 0` (аватар на полу — обычный случай) высота обнуляется,
при `y = 2` получается 4.

---

### VR-03 · Средний · Рефлексия во внутренности UltimateXR нигде не зафиксирована

**Где:** `PhysicalSpaceSyncManager.cs:43, 377, 383, 390` · `ArsenalWallController.cs:146`
**Задача:** [T-21](../tasks/T-21-document-sdk-reflection.md)

Игровой код читает и пишет приватные поля SDK: `_bodyIKSettings`, `_bodyIK`,
`_avatarForwardPosRelativeToNeck`, `_neckPosRelativeToEyes`,
`UxrGrabbableObject._autoCreateStartAnchor`. Формально это не правки SDK, поэтому
в `sdk-patches.md` они не попали.

**Последствие.** При обновлении UltimateXR рефлексия не сломает компиляцию — она молча
вернёт `null`, и калибровка роста перестанет работать. Худший вид зависимости:
невидимый до рантайма.

---

### VR-04 · Средний · `ReplenishWeaponsNetwork` мутирует префаб-ассет

**Где:** `Arsenal/ArsenalWallController.cs:98-110`
**Задача:** [T-22](../tasks/T-22-arsenal-prefab-mutation.md)

`prefab.SetActive(false)` … `prefab.SetActive(wasActive)` вызывается на самом префабе,
а не на инстансе. В редакторе это помечает ассет грязным.

---

### VR-05 · Средний · `TeamSpawnZone`: устаревшие границы и отсутствие null-проверок

**Где:** `Maps/TeamSpawnZone.cs:104-111, 169, 322`
**Задача:** [T-16](../tasks/T-16-spawnzone-bounds-and-nulls.md)

`_shrunkenBounds` считается один раз в `Awake` из мирового AABB. Зона, которую двигают,
поворачивают или масштабируют после `Awake`, проверяется по старым границам.
Плюс `player.Session.Team` и `p.Session.Team` вызываются без проверки на `null`.

---

## Сборка и правила проекта (BUILD)

### BUILD-01 · Критично для сборки · Editor-only namespace в рантайм-скрипте

**Где:** `Assets/Scripts/Player/PlayerController.cs:9`
**Задача:** [T-01](../tasks/T-01-remove-codice-using.md)

```csharp
using static Codice.Client.Commands.WkTree.WorkspaceTreeNode;
```

Случайно вставленный автодополнением `using`. `Codice.*` приходит из
`com.unity.collab-proxy` (Plastic SCM) и существует только в редакторе.
Ничего из этого namespace в файле не используется.

**Последствие.** В редакторе компилируется (консоль на момент аудита чистая), потому что
editor-сборки доступны. В плеере под Android они исключаются — `CS0246` на сборке под Quest.
Это ровно тот класс ошибок, который чинил коммит `3f74bb6`.

---

### BUILD-02 · Средний · `Debug.Log` вместо `GameLog` в 9 файлах

**Где:** `GameNetworkDiscovery` (4) · `PlayerHUDManager` (6) · `MenuController` (5) ·
`LocalMenuManager` (3) · `MenuSessionSetup` (2) · `TeamRegistry` · `AvatarRegistry` ·
`WeaponRegistry` · `LocalClientProfile`
**Задача:** [T-05](../tasks/T-05-gamelog-owns-category.md)

Жёсткое правило проекта — логировать только через `GameLog.*` с категорией из
`GameSettings.Instance`. Около 25 вызовов идут мимо.

**Последствие.** Больнее всего в `GameNetworkDiscovery`: это первая точка сетевой
диагностики, и её сообщения не подчиняются категории `LogLevelNetwork`.

Законное исключение — сам `GameLog.cs` и точки, где `GameSettings.Instance` ещё
не существует (загрузка реестров из `Resources`).

---

## Что осталось за рамками аудита

Проверялся только код. Не открывались и требуют отдельного прохода в редакторе:

- настройки `NetworkTransform` на префабах аватаров (в т.ч. `syncScale` — влияет на VR-01);
- наличие `NetworkIdentity` на префабах оружия и магазинов;
- `syncInterval` и Interest Management в `GameNetworkManager`;
- содержимое сцен `Lobby`, `TestMap1`, `TestMap2` (проверено только наличие/отсутствие
  префаба `--- MANAGERS ---` по GUID).

Также обнаружено при чтении: `Docs/README.md` заметно разошёлся с кодом — упоминает
`GameManager`, `VrCalibrationController`, `AppRoleManager`, `SkinRegistry`, которых
в проекте нет, и события `PlayerConnected` / `PlayerDisconnected` на `GameNetworkManager`,
которые на самом деле живут в `PlayersManager` под другими именами. Это задача
для `/docs-sync`, в список находок не включена.
