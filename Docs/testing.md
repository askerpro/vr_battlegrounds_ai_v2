# Тестирование: сетевая логика и VR-механики

> Живой документ. Обновлять при добавлении уровней, сценариев и тестовых сборок.
>
> Находки, ради которых план составлен — в [`audit/network-audit-2026-08.md`](audit/network-audit-2026-08.md).
> Очередь работ — в [`tasks/README.md`](tasks/README.md).

Шесть уровней от самого дешёвого к самому дорогому. Смысл лестницы: чем ниже уровень,
тем быстрее и чаще он гоняется, и тем меньше багов должно доезжать до верхних.
Уровни 3 и 4 ловят большинство сетевых расхождений.

**Текущее покрытие: 15 EditMode-тестов** (`VrBattlegrounds.Tests.EditMode`, ~1 с):
6 на чистую логику сета и 9 на серверную логику через сетевой харнесс.

---

## Что уже есть

| Что | Где | Готовность |
|---|---|---|
| `com.unity.test-framework 1.6.0` | `Packages/manifest.json` | установлен |
| `com.unity.multiplayer.playmode 2.0.2` | `Packages/manifest.json` | установлен |
| Чтение тегов виртуальных игроков | `GameNetworkDiscovery.cs:93` | работает |
| Сборка (`.asmdef`) для тестов | `Assets/Tests/EditMode/` | есть |
| Харнесс сетевых тестов (ярусы A и B) | `Assets/Tests/EditMode/Network/MirrorTestHarness.cs` | есть |
| Сборка для PlayMode-тестов | — | **нет, нужна** |

Половина инфраструктуры для уровня 3 уже собрана.

---

## Уровень 0 · Ворота компиляции под Android

| | |
|---|---|
| Когда | каждый коммит |
| Время | 1–3 мин |
| Автоматизация | полная |
| Задача | [T-06](tasks/T-06-android-build-gate.md) |

Единственный способ поймать класс ошибок BUILD-01: код, который компилируется в редакторе
и падает в плеере. Отдельная сборка игры не нужна — достаточно прогнать компиляцию
скриптов под целевую платформу.

Editor-скрипт с `[MenuItem("Tools/VR Battlegrounds/Debug/Проверить сборку под Android")]`,
внутри — `PlayerBuildInterface.CompilePlayerScripts` или `BuildPipeline.BuildPlayer`
во временную папку.

**Ловит:** BUILD-01 и любые будущие editor-only `using` в `Assets/Scripts`.

---

## Уровень 1 · EditMode-юнит-тесты чистой логики

| | |
|---|---|
| Когда | каждое сохранение |
| Время | < 1 с |
| Автоматизация | полная |
| Задача | [T-07](tasks/T-07-test-assembly-and-first-tests.md) |

Самая ценная и самая дешёвая часть. `SetManager` и `RoundManager` написаны как обычные
C#-классы, без `MonoBehaviour`, а `RoundManager.Tick(float deltaTime)` принимает шаг времени
параметром — значит, весь раунд прогоняется за миллисекунды.

**Блокеры** (снимаются в [T-05](tasks/T-05-gamelog-owns-category.md)
и [T-07](tasks/T-07-test-assembly-and-first-tests.md)):

1. `GameLog.*(GameSettings.Instance.LogLevelX, ...)` — обращение к ассету, которого
   в EditMode-тесте нет;
2. `RoundManager.AreAllPlayersReady` ходит в `PlayersManager.Instance`;
3. `RoundManager.StartRound` ходит в `FindObjectsByType<TeamSpawnZone>`.

### Первые пять тестов

| Тест | Проверяет | Находка |
|---|---|---|
| Победа в сете начисляет ровно одно очко | число вызовов `AddScore` за один `SetEnded` | MATCH-01 |
| Раунд проходит все шесть фаз по порядку | последовательность `RoundState` при прогоне `Tick` | MATCH-02 |
| Фаза `Scoreboard` длится 5 секунд | число тиков до возврата в `Setup` | MATCH-02 |
| 2:1 по раундам — победа, не ничья | `SetManager.OnRoundEnded` при разном счёте | MATCH-04 |
| Матч из 5 сетов кончается на третьей победе | `EliminationMode.OnSetEnded`, порог `_maxSets / 2 + 1` | MATCH-01 |

**Ловит:** MATCH-01, MATCH-02, MATCH-04 — без запуска игры.

---

## Уровень 2 · PlayMode-тесты: хост в одном процессе

| | |
|---|---|
| Когда | перед push |
| Время | 10–60 с |
| Автоматизация | полная |

Mirror поднимается целиком внутри одного процесса: `NetworkServer.Listen(...)`
плюс `NetworkClient.ConnectHost()`. Тест — корутина `[UnityTest]`, ждущая кадры через
`yield return null`.

**Что покрывать:** подключение создаёт `PlayerSession` и аватар; `SyncVar` доезжают
до клиента; смена команды пересоздаёт аватар; отключение и переподключение по тому же
`deviceToken` восстанавливает счёт, команду **и позицию**.

**Чего не ловит:** настоящую сериализацию по сети, задержку, порядок доставки между
процессами и всё, что различает роли `Host` и `ServerOnly`. Хост — один процесс, где
сервер и клиент видят одни объекты в памяти; половина сетевых багов на нём
не воспроизводится.

**Ловит:** NET-04, NET-05. **Не ловит:** NET-01, NET-06.

---

## Как тестировать сетевую логику

Главная проблема сетевого кода в этом проекте: почти вся серверная логика помечена
`[Server]`, а Mirror вне активного сервера такие методы **молча заглушает**. Поэтому
обычный юнит-тест до них не достаёт — он вызывает метод, тот ничего не делает, тест
зеленеет и не проверяет ничего. Проверено экспериментально: `EliminationMode.Initialize`
вне сервера оставляет `TeamStates.Count == 0`.

Отсюда три уровня сетевых тестов, по возрастанию охвата и цены.

Ярусы A и B реализованы: базовый класс `MirrorTestHarness`
(`Assets/Tests/EditMode/Network/`). Ниже — рецепт, проверенный прогоном, а не чтением
исходников. Первоначальный вариант рецепта был неполон и не работал; расхождения
отмечены явно.

### Главная особенность EditMode: Unity не вызывает `Awake`

Ни `AddComponent`, ни `Instantiate` в EditMode не запускают `Awake` — он вызывается
только в Play-режиме. Для Mirror это значит:

| Что не проинициализировано | Что ломается |
|---|---|
| `KcpTransport.Awake` | внутренние `KcpServer`/`KcpClient` равны null → NRE в `ServerStop()` при TearDown |
| `NetworkIdentity.Awake` | пуст массив `NetworkBehaviours`, у компонентов null в `netIdentity` → NRE при любом обращении к `isServer`, `netId`, `SyncList` |
| `Awake` менеджеров проекта | `PlayersManager.Instance` и прочие синглтоны остаются null |

Поэтому харнесс дёргает `Awake` вручную через рефлексию. У `NetworkIdentity` метод
`internal` — Mirror сделал его таким намеренно, комментарий в исходнике:
«Awake is only called in Play mode. internal so we can call it during unit tests too».

### Ярус A · Сервер без сокета (в одном процессе)

Mirror поднимается сервером, **не открывая сеть**: флаг `NetworkServer.listen = false`
заставляет `Listen()` пропустить `Transport.active.ServerStart()`. Транспорт при этом
нужен как объект — `Initialize()` делает `Debug.Assert(Transport.active != null)`
и подписывается на его события.

```csharp
// SetUp
_transportObject = new GameObject("TestTransport");
KcpTransport transport = _transportObject.AddComponent<KcpTransport>();
ВызватьAwake(transport);        // ← иначе NRE в TearDown
transport.enabled = false;      // ← ServerEarlyUpdate не должен тикать сокет
Transport.active = transport;

NetworkServer.listen = false;   // сокет не открывается
NetworkServer.Listen(4);        // NetworkServer.active == true

// TearDown — порядок важен
NetworkClient.Shutdown();
NetworkServer.Shutdown();
Transport.active = null;
Object.DestroyImmediate(_transportObject);
```

Сетевой объект создаётся так (порядок компонентов важен):

```csharp
GameObject go = new GameObject("Mode");
go.AddComponent<NetworkIdentity>();       // сначала идентити, иначе OnValidate пишет Error
EliminationMode mode = go.AddComponent<EliminationMode>();
ВызватьAwake(go.GetComponent<NetworkIdentity>());  // связывает netIdentity у всех NetworkBehaviour
NetworkServer.Spawn(go);
```

Что становится доступно: `[Server]`-методы исполняются, `[ClientRpc]` не пишет
`called without an active server`, `NetworkServer.Spawn` работает, `NetworkIdentity`
получают `netId`.

**Проверяет:** серверную логику матча, счёт сетов, двойные подписки, снимок сессии
при отключении. **Не проверяет:** репликацию — клиента нет.

### Ярус B · Host-режим в одном процессе

`NetworkClient.ConnectHost()` **недостаточно** — он только создаёт пару локальных
соединений. Полный рецепт:

```csharp
NetworkClient.ConnectHost();
HostMode.InvokeOnConnected();                          // регистрирует соединение в NetworkServer.connections
NetworkServer.localConnection.isAuthenticated = true;  // в бою флаг ставит NetworkManager
NetworkClient.connection.isAuthenticated = true;
NetworkClient.Ready();
ПрокрутитьСеть();                                      // разобрать очереди сообщений
```

Без `InvokeOnConnected` список `NetworkServer.connections` пуст, `Broadcast()` не зовёт
`connection.Update()`, и очередь сообщений не разбирается никогда. Без `isAuthenticated`
сервер рвёт соединение на первом же сообщении: *«Received message Mirror.ReadyMessage
that required authentication»*.

В EditMode не крутится PlayerLoop, поэтому сетевой цикл нужно гонять руками —
через рефлексию, все четыре метода `internal`:

```csharp
NetworkServer.NetworkEarlyUpdate();
NetworkClient.NetworkEarlyUpdate();
NetworkServer.NetworkLateUpdate();   // разбирает очередь клиент → сервер
NetworkClient.NetworkLateUpdate();   // разбирает очередь сервер → клиент
```

Сообщение проходит в одну сторону за два прохода (сначала flush батча, потом разбор
очереди), поэтому харнесс делает три итерации.

**Проверяет:** доходят ли `SpawnMessage` до клиента, регистрацию объекта
в `NetworkClient.spawned`, серверную обработку `ReadyMessage`.

> ⚠️ **Ярус B почти ничего не доказывает про SyncVar.** В host-режиме сервер и клиент
> делят **один и тот же экземпляр** объекта: `NetworkClient.OnHostClientSpawn` просто
> кладёт ссылку из `NetworkServer.spawned` в `NetworkClient.spawned`. Никакой
> сериализации не происходит, поэтому проверка «SyncVar долетел» на ярусе B зеленеет
> сама собой и не ловит ничего.

> Ограничение яруса B: `NetworkClient` в Mirror статический. Двух независимых клиентов
> в одном процессе не сделать — только сервер плюс один локальный клиент.

### Ярус A+ · Репликация через настоящую сериализацию

Обходит слепое пятно яруса B, оставаясь в одном процессе. Состояние серверного объекта
прогоняется через реальный сериализатор Mirror и применяется к **отдельному**
объекту-двойнику — ровно так это работает у удалённого клиента:

```csharp
NetworkWriter owner = new NetworkWriter(), observers = new NetworkWriter();
serverIdentity.SerializeServer(true, owner, observers);          // internal
clientIdentity.DeserializeClient(new NetworkReader(observers.ToArray()), true);
```

Двойник — обычный объект с `NetworkIdentity`, не заспавненный на сервере. Так проверено,
что `PlayerSession.TeamIndex`, `PlayerName`, `Score` действительно уезжают по сети,
а `ActiveAvatar` — нет (находка T-11: это обычное C#-свойство, не SyncVar).

**Не проверяет:** порядок доставки, дельта-сериализацию (тесты гоняют только
`initialState = true`), разницу ролей `Host` и `ServerOnly`.

### Чего харнесс не умеет (границы EditMode)

| Не работает | Почему | Куда переносить |
|---|---|---|
| `PlayersManager.HandlePlayerConnect`, `AvatarManager.SpawnAvatar`/`ChangeAvatar` | внутри `Instantiate` + `NetworkServer.Spawn`; у клона не вызван `Awake`, `NetworkBehaviours` пуст → NRE в `OnStartServer` | уровень 2, PlayMode |
| `NetworkServer.SpawnObjects()` | подхватывает **все** `NetworkIdentity` открытой в редакторе сцены, у которых тоже не вызван `Awake` → NRE, а затем и в `Shutdown` → `CleanupSpawned` | никогда не звать в EditMode |
| Корутины (`StartGameplayWhenReady`, `WaitUntil`) | без Play-режима не крутятся | уровень 2 или вызов приватного метода напрямую |
| Всё, что зависит от `Time.deltaTime` в `Update` | `Update` не вызывается | `RoundManager.Tick(deltaTime)` — принимает шаг параметром |
| Дельта-репликация SyncVar | харнесс гоняет только `initialState = true` | уровень 2 |
| Различие `Host` и `ServerOnly` | сервер в одном процессе всегда host | ярус C |

Отдельно: логи Mirror. `LogAssert.ignoreFailingMessages` тестовый фреймворк сбрасывает
после `SetUp`, поэтому флаг надо ставить **первой строкой каждого теста** — иначе
`Error` из Mirror валит тест до проверки утверждений.

### Ярус C · Два процесса (настоящий e2e)

Единственное, что ловит NET-01, NET-06 и VR-01: они не воспроизводятся там, где сервер
одновременно является клиентом.

Схема, пригодная для автономного прогона агентом:

1. Собрать один плеер под Windows. Роль определяется на старте: `GameNetworkDiscovery`
   уже умеет это через `Mirror.Utils.IsHeadless()` и теги Multiplayer Play Mode.
2. Скриптом PowerShell запустить сервер с `-batchmode -nographics`, затем два клиента.
3. Каждый процесс проигрывает сценарий и пишет **структурированный результат в файл**
   (JSON: что проверялось, что получилось), а не только в лог.
4. Агент читает файлы результатов и парсит их.

Ключевое отличие от ручного прогона уровня 4: вердикт выносит не человек, глядя в два
окна, а сам сценарий, записывающий машиночитаемый файл. Без этого шага ярус C остаётся
ручным и агент не может закрыть на нём задачу.

Что нужно построить: сценарии-проверки внутри билда (компонент, который по CLI-аргументу
проигрывает один сценарий и пишет вердикт), плюс скрипт-дирижёр. Задачи —
[T-26](tasks/T-26-network-test-harness.md) и [T-27](tasks/T-27-two-process-e2e.md).

### Что каким ярусом закрывается

| Задача | Ярус | Почему |
|---|---|---|
| T-02 двойная подписка | A | `InitializeActiveGame` помечен `[Server]`. **Закрыто тестом** `Победа_в_сете_даёт_одно_очко` |
| T-04 восстановление сессии | A + PlayMode | снимок при отключении проверяется на ярусе A (**закрыто**); восстановление позиции при переподключении — только уровень 2: код внутри делает `Instantiate` + `Spawn` |
| T-11 связь сессия ↔ аватар | A+ | `ActiveAvatar` — не SyncVar, до клиента не доезжает. **Зафиксировано тестом** `ActiveAvatar_не_долетает_до_клиента`; на ярусе B проверка была бы ложно-зелёной |
| T-12 канал состояния | **C** | ломается только при сервере, не являющемся клиентом |
| T-13 фаза как состояние | **C** | `ClientRpc` в `ServerOnly` — суть находки NET-06 |
| T-14 масштаб калибровки | **C** | нужны два разных клиента |

---

## Уровень 3 · Multiplayer Play Mode: 2–4 виртуальных игрока

| | |
|---|---|
| Когда | перед закрытием задачи |
| Время | 2–5 мин |
| Автоматизация | ручной прогон |

Главный рабочий инструмент, уже установлен. Unity запускает несколько независимых
экземпляров редактора, каждый со своим процессом и сетевой ролью. Шлем не нужен —
аватар управляется мышью и клавиатурой через симулятор UltimateXR.

### Как включить

1. `Window → Multiplayer → Multiplayer Play Mode`
2. Активировать Player 2 и Player 3
3. Проставить теги: `Host`, `Client`, `Client` — читаются в
   `GameNetworkDiscovery.TryGetRoleFromPlayerTag`
4. Запуск из сцены `Offline`

### Сценарии

- **Полный матч на двоих:** подключение → выбор команды → загрузка карты → раунд →
  смерть → следующий раунд. На каждом шаге смотреть *оба* окна и сверять, что видят
  разные игроки.
- **Смена скина в лобби** — вскрывает NET-02.

**Ловит:** NET-02, NET-03, NET-07, MATCH-03, MATCH-05, VR-01 (визуально).

---

## Уровень 4 · Выделенный сервер плюс два клиента

| | |
|---|---|
| Когда | перед релизом и после правок сетевого ядра |
| Время | 15–30 мин |
| Автоматизация | ручной прогон |

Единственная конфигурация, где сервер не является одновременно клиентом. NET-01 и NET-06
не воспроизводятся ни на хосте, ни в Play Mode.

Сборка под Windows с `-batchmode -nographics`: `GameNetworkDiscovery` сам определит роль
через `Mirror.Utils.IsHeadless()`. Клиенты — два обычных билда или билд плюс редактор.

**На что смотреть:** логи сервера отдельно от клиентских. Появляется ли оружие в слотах
арсенала (NET-06). Есть ли `UxrComponentNotFoundException` при подключении второго клиента
(NET-01). Совпадает ли рост аватара в двух окнах (VR-01).

**Ловит:** NET-01, NET-06, VR-01 — и ничто ниже их не поймает.

---

## Уровень 5 · Чек-лист в шлеме на Quest

| | |
|---|---|
| Когда | перед релизом |
| Время | 30–60 мин |
| Автоматизация | невозможна |

Часть VR не автоматизируется в принципе: ощущение хвата, попадание рук по оружию,
укачивание, читаемость HUD на реальном экране. Ценность не в самом прохождении, а в том,
что порядок шагов не меняется от раза к разу.

| Область | Что проверять | Красный флаг |
|---|---|---|
| Калибровка | по двум якорям, затем по росту (пол → рост) | телепорт уводит по вертикали (VR-02) |
| Хват | взять / бросить / передать оружие из руки в руку | предмет дублируется или рассинхронен |
| Оружие | магазин, затвор, стрельба, попадания | можно стрелять до фазы Combat (MATCH-03) |
| Арсенал | взять со стены, вернуть, схватить жетон | у второго игрока стена в другом состоянии (NET-07) |
| Рост | два игрока разного роста рядом | рост в чужом окне не совпадает (VR-01) |
| Смерть | гибель, наблюдение, респавн в зоне | зона спавна не видна мёртвому (NET-04) |
| Комфорт | 15 минут непрерывной игры | просадки кадров, дрожание чужих аватаров |

**Ловит:** VR-02, NET-04, NET-07, MATCH-03 в реальных условиях — и всё, что не
предусмотрено ни одним тестом.

---

## Размещение тестов

```
Assets/Tests/
  EditMode/
    VrBattlegrounds.Tests.EditMode.asmdef   ← includePlatforms: ["Editor"]
  PlayMode/
    VrBattlegrounds.Tests.PlayMode.asmdef
```

> **Важно.** Тестовые сборки должны быть исключены из плеера. EditMode-сборка —
> через `includePlatforms: ["Editor"]`. Иначе повторится ситуация BUILD-01:
> код компилируется в редакторе и ломает сборку под Quest.
