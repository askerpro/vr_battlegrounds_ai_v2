# Тестирование: сетевая логика и VR-механики

> Живой документ. Обновлять при добавлении уровней, сценариев и тестовых сборок.
>
> Находки, ради которых план составлен — в [`audit/network-audit-2026-08.md`](audit/network-audit-2026-08.md).
> Очередь работ — в [`tasks/README.md`](tasks/README.md).

Шесть уровней от самого дешёвого к самому дорогому. Смысл лестницы: чем ниже уровень,
тем быстрее и чаще он гоняется, и тем меньше багов должно доезжать до верхних.
Уровни 3 и 4 ловят большинство сетевых расхождений.

**Текущее покрытие: 92 EditMode-теста** (`VrBattlegrounds.Tests.EditMode`, ~3.5 с):
6 на чистую логику сета (с 2026-09 — `MapScoreTests`, счёт карты), 5 на фазы раунда, 17 на серверную логику и репликацию
через сетевой харнесс, 17 на арсенал, 6 на зону спавна, 5 на срок годности сессий,
3 на команду матча, 2 на время жизни `LocalSession`, **5 на состав префабов**.

> Из них **три красные** — и это их работа: `PrefabCompositionTests` сторожат размещение
> компонентов и держат открытыми ARCH-01 и VR-07 до правки префабов. Подробности —
> [«Состав префабов»](#состав-префабов--слепое-пятно-всех-остальных-ярусов).

---

## Как запускать тесты

### Контракт окружения (2026-10-02)

Перед проверкой механики общий харнесс проверяет свои предусловия через `TestEnvironmentContract`.
Ошибка с префиксом `[TestEnvironment]` означает отказ стенда. Стенд оружия требует единственный
активный `UxrWeaponManager` и совпадение singleton с объектом сцены; стенд хвата — единственный
`UxrGrabManager` и по одной руке каждой стороны; Mirror — живой сервер, свой активный транспорт
и, для host-сценария, один готовый локальный клиент. Выключенные дубликаты также считаются.
Тесты контракта отдельно проверяют отсутствующий, двойной, выключенный и уничтоженный объект.
Перед получением singleton стенд сбрасывает только уничтоженную кешированную ссылку SDK:
в EditMode пропущенный OnDestroy оставлял её между тестами. Живой singleton сохраняется.
Чистым тестам расчётов сцена не требуется. Прямые стенды вне этих общих харнессов пока не переведены:
контракт каждого такого стенда должен описывать именно его зависимости, без глобального создания всех менеджеров.

Все тесты — одна сборка EditMode `VrBattlegrounds.Tests.EditMode` (`Assets/Tests/EditMode/`).

**В редакторе.** Window → General → Test Runner → вкладка **EditMode**. Run All — всё;
выделить класс (например `AvatarLoadoutTests`) → Run Selected — один класс; двойной клик
по строке — один кейс. Текст отказа — внизу окна при выделении красного теста.

**Агентом через MCP.**

```
run_tests(mode="EditMode",
          assembly_names=["VrBattlegrounds.Tests.EditMode"],
          group_names=["VrBattlegrounds.Tests.Prefabs.AvatarLoadoutTests"])
get_test_job(job_id, wait_timeout=60, include_failed_tests=true)
```

- В `group_names` — **полное имя с namespace**. Короткое `AvatarLoadoutTests` даёт
  задание с `total: 0`, которое висит вечно.
- Перезагрузка домена во время прогона (кто-то поменял скрипт) теряет задание: оно
  остаётся `running` с нулём тестов. Лечится `run_tests(clear_stuck=true)` и повторным запуском.

**Из командной строки (CI).** Только при **закрытом** редакторе — Unity не пускает два
экземпляра на один проект.

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.4.1f1\Editor\Unity.exe" `
  -batchmode -projectPath "F:\UnityProjects\Vr_Battlegrounds_ai" `
  -runTests -testPlatform EditMode `
  -assemblyNames VrBattlegrounds.Tests.EditMode `
  -testFilter "VrBattlegrounds.Tests.Prefabs.AvatarLoadoutTests" `
  -testResults "$PWD\TestResults.xml" -logFile -
```

Без `-testFilter` — все тесты. Результат — NUnit XML в `-testResults`, код выхода 0 = всё зелёное.

---

## Что уже есть

| Что | Где | Готовность |
|---|---|---|
| `com.unity.test-framework 1.6.0` | `Packages/manifest.json` | установлен |
| `com.unity.multiplayer.playmode 2.0.2` | `Packages/manifest.json` | установлен |
| Чтение тегов виртуальных игроков | `GameNetworkDiscovery.cs:93` | работает |
| Сборка (`.asmdef`) для тестов | `Assets/Tests/EditMode/` | есть |
| Харнесс сетевых тестов (ярусы A и B) | `Assets/Tests/EditMode/Network/MirrorTestHarness.cs` | есть |
| Харнесс двух процессов (ярус C) | `Assets/Scripts/Debug/E2E/` + `Tools/e2e/Run-E2E.ps1` | есть, двенадцать сценариев |
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

Самая ценная и самая дешёвая часть. `RoundPhases` написан как обычный
C#-класс, без `MonoBehaviour`, а `RoundPhases.Tick(float deltaTime)` принимает шаг времени
параметром — значит, весь раунд прогоняется за миллисекунды.

**Блокеры сняты:**

1. `GameLog.<Категория>.*(...)` — категорию знает сам логгер, а `GameSettings.Instance`
   создаёт ассет на лету, если его нет (T-05);
2. Проверка готовности игроков ходила в `PlayersManager.Instance` — теперь спрашивает
   `IPlayerRoster`, в тестах это `StubPlayerRoster` (T-09);
3. `RoundPhases.StartRound` искал `TeamSpawnZone` через `FindObjectsByType` — поле было
   мёртвым, удалено (T-09).

### Как прогнать раунд целиком

`EliminationMode.ServerTick(float deltaTime)` — серверный шаг матча отдельно от `Update`:
шаг времени приходит параметром, поэтому весь матч крутится без Play-режима. Оснастка —
`Assets/Tests/EditMode/RoundFlowSupport.cs`:

- `StubPlayerRoster` — подставной `IPlayerRoster`: «кто жив и готов» для машины раунда
  и «сколько всего игроков» для проверки готовности матча. Без него фаза `Equipment`
  вечна: живой аватар в EditMode не поднимается, поэтому `PlayersManager.GetAlivePlayers`
  всегда пуст. После NET-18 реестр — единственный источник сведений об игроках,
  поэтому `PlayersManager` в тестах матча вообще не поднимается.
- `RoundFlowDriver` — прокрутка фиксированным шагом с записью наблюдённых фаз,
  `AdvanceUntil(...)`, `PhaseSequence()`, `FirstRunLength(...)` и читаемый след
  `Setup x3 -> Equipment x1 -> ...` для сообщений об ошибке.

Шаг **0.25 с** выбран не случайно: это двоично точное число, поэтому сумма шагов не
«уползает» и число тиков в фазе точно равно длительность / шаг. С шагом 0.1 накопление
30 раз даёт 2.9999998, и тест на длительность врал бы на один тик.

> **Гонять фазы надо через `Tick`, а не через «раунд закончился».** Тест, дёргающий конец
> раунда напрямую, в `Tick` вообще не заходит — именно так в проекте прятался второй
> экземпляр MATCH-06, и именно поэтому пропущенные фазы `Resolution`/`Scoreboard`
> (MATCH-02) годами не ловились.

**Сквозной смок сессии — `SeriesSmokeTests`** (`Assets/Tests/EditMode/GameModes/`). Звенья матча закрыты своими
тестами (`RoundPhaseFlowTests`, `SideSwapTests`, `MapScoreTests`, `MatchFlowTests`); смок проверяет стыки одним
прогоном: серия из двух карт на настоящих `Series`, `MapReferee` (запуск карты — как у `MapBootstrap`) и
`EliminationMode` — разминка → «Начать матч» → все фазы раунда → раунды подряд → смена сторон после половины →
конец карты по большинству → разминка и счёт серии → «Следующая карта» → вторая карта → лобби, команды распущены.
Сцены не грузятся (загрузчик серии подменён). Живой прогон того же пути на сценах — план, этап
`series-smoke-e2e` задачи map-runtime-bootstrap.

### Первые пять тестов

| Тест | Проверяет | Находка |
|---|---|---|
| Победа в раунде начисляет ровно одно очко (было: в сете) | число вызовов `AddScore` за раунд | MATCH-01 |
| Раунд проходит все шесть фаз по порядку | последовательность `RoundPhase` при прогоне `Tick` | MATCH-02 |
| Фаза `Scoreboard` длится 5 секунд | число тиков до возврата в `Setup` | MATCH-02 |
| Лидер по раундам — победа, не ничья (было: 2:1 в сете) | `EliminationMode.DecideAfterScoreboard` при разном счёте | MATCH-04 |
| Карта кончается на большинстве раундов (было: матч из 5 сетов) | порог `_roundsPerHalf + 1` | MATCH-01 |

**Ловит:** MATCH-01, MATCH-02, MATCH-04 — без запуска игры.

---

## Состав префабов — слепое пятно всех остальных ярусов

| | |
|---|---|
| Когда | каждое сохранение (тот же прогон EditMode) |
| Время | < 0.1 с |
| Автоматизация | полная |
| Где | `Assets/Tests/EditMode/Prefabs/PrefabCompositionTests.cs` |

Все ярусы ниже проверяют **код**. Ни один не проверял, **где размещены компоненты** —
и в эту дыру провалились сразу две находки:

- **ARCH-01**: `SessionRecoveryManager` не лежит ни в одной сцене и ни в одном префабе,
  поэтому `Instance` всегда `null`, а все вызовы обёрнуты в `if (Instance != null)`.
  Задачи T-04 и T-20 чинили код, который никогда не исполнялся. Их EditMode-тесты
  создают менеджер сами и о его отсутствии в игре ничего не знают.
- **VR-07**: у одного аватарного префаба пивот камеры лежит на уровень глубже,
  чем ищет `UxrAvatar`, и на камере нет `NetworkTransform`.

Общий класс: «компонент есть в коде, но его нет там, где его ищут». Такие дефекты
не ловятся ни юнит-тестом (он создаёт объекты сам), ни ярусом C (он гоняет уже собранный
плеер и молчит про сцены), ни воротами компиляции.

Что проверяется:

| Тест | Утверждение |
|---|---|
| `Все_постоянные_менеджеры_лежат_на_префабе_MANAGERS` | каждый постоянный менеджер физически есть на `--- MANAGERS ---.prefab` |
| `Аватарных_префабов_найдено_столько_же_сколько_ожидается` | поиск аватаров вообще что-то нашёл (иначе остальные тесты зелены впустую) |
| `У_каждого_аватара_на_корне_есть_обязательные_компоненты` | `NetworkIdentity`, `NetworkTransform`, `PlayerController`, `UxrAvatar`, `UxrActor`, `UxrMirrorAvatar` |
| `У_каждого_аватара_пивот_камеры_прямой_потомок_корня` | контракт `UxrAvatar.InitializeCamera` соблюдён |
| `У_каждого_аватара_камера_несёт_NetworkTransform` | смещение высоты реплицируется |

Два правила при расширении:

- **Аватары ищутся, а не перечисляются.** Список путей устарел бы на первом же новом
  скине; вместо него — все префабы из `Assets/Prefabs/Player`, у которых на корне есть
  `PlayerController`.
- **Утверждать только то, отсутствие чего ломает игру.** Косметические различия между
  префабами (`UxrCameraFade`, `LegsAnimator`, `Ghost`, карманы) зафиксированы в аудите,
  а не в `Assert` — иначе тест краснеет на вкусовщине и его начинают игнорировать.

**Ловит:** ARCH-01, VR-07 и любое будущее «забыли положить компонент на префаб».

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

**Проверяет:** серверную логику матча, счёт карты, двойные подписки, снимок сессии
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
что `PlayerSession.TeamIndex`, `PlayerName`, `Score` действительно уезжают по сети.
Этим же ярусом закрыта T-11: до правки `ActiveAvatar` был обычным C#-свойством и до
клиента не доезжал, после — за связь отвечает `[SyncVar] ActiveAvatarNetId`, и тест
`ActiveAvatar_долетает_до_клиента` (красный до правки) стал зелёным.

> Оговорка про двойника. Отсутствие объекта в `spawned` эмулируется изъятием записи
> из `NetworkServer.spawned`: в EditMode это тот же словарь, из которого читает
> `Mirror.Utils.GetSpawnedInServerOrClient`, когда сервер активен. Запись обязательно
> вернуть в конце теста, иначе объект минует штатную уборку Mirror.

**Не проверяет:** порядок доставки, дельта-сериализацию (тесты гоняют только
`initialState = true`), разницу ролей `Host` и `ServerOnly`.

### Чего харнесс не умеет (границы EditMode)

| Не работает | Почему | Куда переносить |
|---|---|---|
| `PlayersManager.HandlePlayerConnect`, `AvatarManager.SpawnAvatar`/`ChangeAvatar` | внутри `Instantiate` + `NetworkServer.Spawn`; у клона не вызван `Awake`, `NetworkBehaviours` пуст → NRE в `OnStartServer` | уровень 2, PlayMode |
| `NetworkServer.SpawnObjects()` | подхватывает **все** `NetworkIdentity` открытой в редакторе сцены, у которых тоже не вызван `Awake` → NRE, а затем и в `Shutdown` → `CleanupSpawned` | никогда не звать в EditMode |
| Корутины (`BeginWhenReady`, `WaitUntil`) | без Play-режима не крутятся | уровень 2 или вызов приватного метода напрямую |
| Всё, что зависит от `Time.deltaTime` в `Update` | `Update` не вызывается | `RoundPhases.Tick(deltaTime)` — принимает шаг параметром |
| Дельта-репликация SyncVar | харнесс гоняет только `initialState = true` | уровень 2 |
| Различие `Host` и `ServerOnly` | сервер в одном процессе всегда host | ярус C |

Отдельно: логи Mirror. `LogAssert.ignoreFailingMessages` тестовый фреймворк сбрасывает
после `SetUp`, поэтому флаг надо ставить **первой строкой каждого теста** — иначе
`Error` из Mirror валит тест до проверки утверждений.

> **Не создавать объекты в открытой сцене.** Разведка через `execute_code` и тесты
> создают `GameObject` в той сцене, которая сейчас открыта в редакторе, и Unity
> сохраняет их в её файл. Дальше два неприятных следствия:
>
> - мусор попадает в `.unity` и в коммит;
> - откат файла сцены (`git checkout`) из-под открытой сцены вешает редактор
>   на модальном окне «Scene has been modified externally — Reload?». Процесс жив,
>   но главный поток заблокирован, и MCP перестаёт отвечать: `Unity session not ready`.
>   Разблокировать может только человек кликом — то есть агент теряет автономность
>   именно там, где она нужна.
>
> Правило: всё создавать и уничтожать в пределах теста (`DestroyImmediate` в `TearDown`),
> ничего не оставлять в сцене. Если объект в сцене всё-таки появился — удалять его
> через MCP, **не** откатывать файл сцены через git при открытом редакторе.

### Ярус C · Два процесса (настоящий e2e)

Единственное, что ловит NET-01, NET-06 и VR-01: они не воспроизводятся там, где сервер
одновременно является клиентом.

**Реализовано (T-27). Сценарии: `dedicated-server-arsenal` (NET-06, зелёный после T-13),
`avatar-swap-death-replication` (NET-01/NET-02/NET-15, зелёный после T-12),
`calibration-scale-replication` (VR-01, зелёный после T-14, гоняется на обеих картах),
`session-recovery-on-reconnect` (ARCH-01, зелёный после исправления NET-20, `-Clients 1`),
`player-death-signal` (остаток NET-04, зелёный после правки `PlayerController`, `-Clients 1`),
`arsenal-item-grab` (NET-16/NET-17/NET-23, зелёный после выравнивания `UniqueId`, `-Clients 1`),
`shot-pipeline-budget` (измерительный, для решений T-23/T-24, `-Clients 1`),
`round-readiness-match` (T-29/RDY-01 плюс последний пункт T-09, зелёный, `-Clients 2`),
`weapon-hit-damage` (T-28, **зелёный** — маски в префабах исправлены 2026-08-23, `-Clients 1`),
`avatar-spawn-point` (WPN-03, зелёный после правки `AvatarManager`, `-Clients 1`),
`calibrated-position-persists` (CAL-01, зелёный после T-30, `-Clients 2`, гоняется на паре `TestMap1` → `TestMap2`),
`connect-place-across-map` (CAL-02, зелёный после правки пути подключения, `-Clients 1`, та же пара карт),
`map-run-relay-barrier` (барьер начального снимка Relay, `-Clients 2 -LateClientDelay 40`: второй клиент входит на
идущую карту, затем сервер перезагружает ту же карту; клиенты проверяют, что снимок применён только для ключа своего
запуска и все его адресаты зарегистрированы),
`map-run-startup-route` (то же, но сервер до старта сети запрашивает первую сцену через `ServerStartupRoute` и
стартует сразу в карту прогона с режимом elimination, минуя Lobby; клиенты, включая позднего, входят в уже идущую
карту, `-Clients 2 -LateClientDelay 40`).**
Вердикт выносит не человек, глядя в два окна, а сам сценарий: каждый процесс пишет
машиночитаемый JSON.
Без этого ярус C остаётся ручным уровнем 4 и агент не может закрыть на нём задачу.

#### Как запустить

```
powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-E2E.ps1
```

Дирижёр проверяет свежесть билда, выбирает пару UDP-портов прогона, поднимает сервер
и клиентов, ждёт вердикты, гасит процессы и сводит отчёт. Параметры: `-Scenario`,
`-Map` (по умолчанию `TestMap1`), `-Clients`, `-Timeout`, `-SkipStaleCheck`, `-LateClientDelay` (пауза в секундах
перед запуском последнего клиента — поздний вход на идущую карту), `-NetworkPort`/`-DiscoveryPort`.

**Изоляция прогона.** Каждый прогон получает свою пару портов KCP и Discovery: по умолчанию две свободные
(выбор повторяется до трёх раз, если порт заняли перед стартом), либо явно `-NetworkPort`/`-DiscoveryPort`
(только вместе). Пара уходит всем процессам аргументами `-e2eNetworkPort`/`-e2eDiscoveryPort`, её видно в
«Конфигурации прогона», в `summary.json` и в логах плееров («Порты применены»). Прогон не конфликтует с Play
редактора, worker и плеерами других worktree на 7778/47777, а клиент не найдёт через Discovery чужой сервер.
Неполная или битая пара — не откат на порты по умолчанию, а отказ: сценарий не запускается, результат `error`.
Гасит дирижёр только свои процессы и их потомков, не по имени. В плеере пару применяет
`E2EPlayerPorts` (шов `DebugBootstrapGate.PlayerBeforeNetworkStart`, Mirror patch 2); в редакторе порты ведёт
Play Launch ([test-stand.md](test-stand.md) «Локальные сетевые порты»).

Коды возврата: `0` — всё зелёное, `1` — сценарий поймал отказ, `2` — прогон
не состоялся (нет билда, нет вердикта, битый JSON), вердикт вынести нельзя.
Третий код важен: он отличает «находка воспроизвелась» от «харнесс не доехал».

Плеер собирается в редакторе — `Tools > VR Battlegrounds > Debug > Собрать e2e-плеер
(Windows)`, агентом `E2EPlayerBuilder.Run()`, при закрытом редакторе через
`-executeMethod VrBattlegrounds.EditorTools.E2EPlayerBuilder.RunBatch`. Из-под живого
редактора батч не работает: проект залочен `Library/`.

| Что | Время |
|---|---|
| Первая полная сборка плеера | ~5 мин |
| Инкрементальная пересборка после правки C# | ~17 с |
| Сам прогон (сервер + 2 клиента) | ~40 с |

#### Прогон на шлеме (Run-QuestE2E.ps1)

Контракт: [quest-device-run](../tasks/quest-perf-pipeline/contracts/quest-device-run.json) (rev3). Шлем — настоящий
клиент выделенного сервера на ПК по LAN (prod flow): дирижёр поднимает Windows-плеер сервера
(`-batchmode -nographics`, роль `server`), затем запускает установленное приложение на шлеме клиентом `client-1`.

```
powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-QuestE2E.ps1
powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-QuestE2E.ps1 -Scenario device-smoke -Arg artifact=1
powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-QuestE2E.ps1 -Apk Build\Quest\VrBattlegrounds_Quest.apk
powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-QuestE2E.ps1 -Build
powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-QuestE2E.ps1 -HeadPose off
```

**Сборки одной ревизии.** e2e-плеер сервера и APK Quest (конфигурация Test) несут штамп ревизии исходников в версии:
`<база>+<ревизия>` ([release.md](release.md), «Штамп ревизии»). Собирать их парой — одной ревизией, посчитанной один раз:

- **`-Build`** (редактор этого checkout закрыт): дирижёр после проверок устройства запускает Unity batch
  `-executeMethod VrBattlegrounds.EditorTools.E2EPlayerBuilder.RunPairBatch -pairStatus <прогон>/pair-status.json
  -logFile <прогон>/unity-build.log` (предел `-BuildTimeoutMinutes`, по умолчанию 90), затем ставит
  `Build\Quest\VrBattlegrounds_Quest.apk` и сервер `Build\e2e\VrBattlegrounds.exe`; условия шлема перепроверяет после
  сборки. В `run.json` — блок `build` (`status`, `revision`, `durationSeconds`, код Unity). Несовместим с `-Apk` и
  `-ServerPlayer`. `-DryRunBuild` — только проверить условия и напечатать команду Unity (код 0). Только в checkout с
  полными ассетами (основной, worker): в лёгком агентском worktree (LFS-роль `agent`, ассеты — указатели) `-Build`
  отказывает — агент собирает пару на worker через `StartPairBuild`, копирует `Build\` к себе и запускает с `-Apk`
  и `-ServerPlayer`. **Проверка LFS** (до запуска Unity, и в `-DryRunBuild`): роль `main` не гарантирует полных файлов,
  поэтому дирижёр берёт `git lfs ls-files -n` и у каждого существующего файла читает первые 200 байт; начало
  `version https://git-lfs.github.com/spec/v1` — указатель. Есть указатели — отказ (код 2): до 10 путей, общее число и
  подсказка `git lfs checkout -- <пути>` / `git lfs pull --include=...` (много путей — `git lfs checkout` или
  `git lfs pull` целиком). git-lfs недоступен — тоже отказ: проверку не выполнить.
- **Редактор открыт** (`Temp\UnityLockfile` занят) — `-Build` отказывает (код 2). Пара собирается в редакторе:
  `VrBattlegrounds.EditorTools.E2EPlayerBuilder.StartPairBuild("Build/e2e-pair-status.json")` (агент — на worker под
  арендой) ставит сборку в `delayCall` и сразу возвращает; пустой файл статуса — сборка идёт, JSON (`serverPassed`,
  `questPassed`, `revision`, `serverSummary`, `questSummary`, `apkPath`, `serverPath`, `error`) — закончена. Затем
  прогон без `-Build` с `-Apk`.

**Сверка ревизий.** Ревизия APK — часть versionName установленного пакета после `+` (читается до запуска и печатается),
ревизия сервера — из `appVersion` его `server.json`. После прогона дирижёр сравнивает их: разные — причина «сборки из
разных ревизий», нет штампа у одной стороны (или `unknown`) — «ревизия сборки не определена»; обе дают код 2. В
`run.json`: `builds.revisionServer`, `builds.revisionClient`, `builds.check`. С `-Build` расхождение установленного APK
с ревизией пары — отказ ещё до запуска сервера. Совпадение штампов с `-dirty` не гарантирует одинаковых исходников —
об этом пишет `builds.check`.

Параметры: `-Scenario` (по умолчанию `device-smoke`), `-Topology client|host` (`host` — код 2: нужен шов выбора роли
в плеере, зона network), `-Map`, `-Timeout` (не меньше 60 с; сценариям уходит на 30 с меньше), `-Arg ключ=значение`
(каждый уходит обеим сторонам как `-e2eArg`; в режиме `-File` несколько — через запятую; без пробелов и запятых
в значениях), `-Apk` (`install -r` и SHA-256 в `run.json`), `-Serial`, `-Adb` (иначе PATH, затем Android SDK Unity
версии проекта), `-ServerPlayer` (по умолчанию `Build\e2e\VrBattlegrounds.exe`), `-NetworkPort`/`-DiscoveryPort`
(0 и 0 — авто, как у Run-E2E), `-ServerAddress` (по умолчанию IPv4 ПК в подсети `wlan0` шлема), `-Package`, `-Activity`,
`-ViewsFile` (точки обзора, ниже), `-HeadPose` (поза головы, ниже; по умолчанию `1.6`).

Отказ с кодом 2 до прогона, ничего не трогая: нет adb или устройства (нужно ровно одно в состоянии `device`, либо
`-Serial`), приложение уже запущено (`pidof`) — не наш процесс, нет плеера сервера, нет общей подсети, неверные
`-Arg`/порты. **Брандмауэр**: если активный профиль Windows включён и нет разрешающего входящего UDP-правила для exe
сервера (или есть блокирующее), дирижёр отказывает и печатает готовые `New-NetFirewallRule`/`netsh` — их выполняет
человек с правами администратора; сам скрипт брандмауэр не меняет.

Порядок: сервер → заглушка `server.json` → `KEYCODE_WAKEUP`, `prox_close`, `logcat -c` → `am start -S -W -n
<pkg>/<activity> -e unity "<аргументы>"` с `-e2eQuitOnFinish` и результатом в `/sdcard/Android/data/<pkg>/files/e2e/
<прогон>/client-1.json` → опрос обоих вердиктов до статуса ≠ `started` или таймаута. В `finally` всегда: `am
force-stop`, гашение своего серверного PID и потомков, `automation_disable`, logcat, сбор, `run.json`.

**Шлем должен быть свободен.** Пока на шлеме открыт системный диалог (`…Dialog…` от `com.oculus`/`com.meta`:
«нужны контроллеры», «Link отключён», граница), игра не получит фокус — дирижёр проверяет это до прогона и отказывает
с именем диалога; закрывает его человек. Контроллеры держать включёнными: при спящих Quest показывает
`LaunchCheckControllerRequiredDialog`. **Quest Link**: в Link шлем работает дисплеем ПК, и игра не стартует — без
флага дирижёр отказывает; `-StopLink` закрывает Link-клиент (`com.oculus.xrstreamingclient`) на прогон и **не
возвращает** его (решение пользователя 2026-10-12: перезапуск со шлема не восстанавливает связь с ПК надёжно) —
переподключить Link вручную; в `run.json` — блок `link`.

> **После `-StopLink` Link может не подключиться совсем** (наблюдалось 2026-10-12 после нескольких прогонов подряд):
> шлем показывает выбор Air Link/Quest Link, а связь с ПК не встаёт. Выбор ПК на шлеме, запуск приложения Meta Quest
> Link и переподключение кабеля не помогли — помог только аварийный перезапуск Link на ПК (приложение Meta Quest Link →
> перезапуск среды выполнения; нужен человек). Причина, по наблюдению: `OVRServer_x64` на ПК держит оборванную
> `force-stop` сессию. Поэтому `-StopLink` — только когда Link в ближайшее время не нужен; на шлеме при этом ничего
> не выключается (Link-клиент остаётся установленным и включённым).

Папка `Tools/e2e/results/<yyyyMMdd-HHmmss>-quest-<сценарий>/`: `server.json`, `server.log`, `client-1.json`,
`client-1.logcat.txt` (процесс приложения, все теги), `unity.logcat.txt` (тег Unity), `logcat-stream.txt` (весь
logcat за прогон потоком — кольцевой буфер шлема мал), `logcat-full.txt` (только если pid не найден),
`artifacts/client-1/` и `artifacts/server/` (то, что сценарии объявили в `E2EResult.Artifacts`), с `-Build` —
`pair-status.json` и `unity-build.log`, `summary.json`
(как у Run-E2E) и `run.json` — манифест условий: пакет и версия, APK и SHA-256, модель, fingerprint, серийник, UTC
начала и конца, аргументы обеих сторон целиком, батарея до и после, Wi-Fi шлема, CPU ПК, порты, адрес сервера,
условия сборки обеих сторон (`buildGuid`, `appVersion`, `unityVersion`, `developmentBuild`, `platform` из их
результатов), ревизии обеих сторон и сверка (`builds.revisionServer`/`revisionClient`/`check`), шаг сборки `build`
(с `-Build`), итоговый код. Без `run.json` прогон не состоялся. Разные ревизии, ревизия без штампа или разные
`unityVersion` сторон — код 2.

Для сценариев: параметры читаются только через `E2EContext.Arg(ключ)` (ошибка разбора `-e2eArg` — `error` до запуска
сценария); файлы для забора объявляются `E2EResult.AddArtifact(путь)` — только абсолютный путь внутри
`Application.persistentDataPath`, иначе красная проверка «артефакт вне persistentDataPath». Самопроверка каркаса —
сценарий `device-smoke` (сервер ждёт подключение с сессией, клиент — `NetworkClient.isConnected`, с `artifact=1`
обе стороны пишут `persistentDataPath/e2e-smoke/<utc>.txt`). Во время прогона редактор и MCP на этом ПК не трогать.

**Точки обзора (`-ViewsFile <json>`).** Файл задаёт, куда встать и куда смотреть, — для замеров «что видно отсюда»:

```json
{ "views": [
  { "name": "arena-center", "x": 0, "y": -0.01, "z": 0, "yaw": 0 },
  { "name": "spawn-a", "x": 12.5, "z": -4, "yaw": 90 }
] }
```

`x`, `z` — точка пола, над которой окажется камера; `y` — уровень пола (необязателен: тогда текущий пол аватара);
`yaw` — курс взгляда в градусах (0 — +Z, 90 — +X). Имена уникальны, символы — как у `-Arg` (`[A-Za-z0-9_.:/=+@%-]`),
других полей нет; порядок точек сохраняется. Ошибка формата — отказ (код 2) до устройства; `-Arg views=...` вместе с
`-ViewsFile` — тоже отказ. Задаются **только пол и курс**: высота глаз, тангаж и крен остаются от трекинга шлема.
С симуляцией позы головы (по умолчанию, ниже) высоту глаз и горизонт задаёт она; с `-HeadPose off` для
повторяемости ставить шлем на подставку на высоте глаз ровно — по вертикально смотрящей камере курс не определён,
и точка не обходится.

Дирижёр копирует файл в папку прогона (`views.json`), в persistentDataPath сервера
(`%USERPROFILE%\AppData\LocalLow\<companyName>\<productName>\e2e\<прогон>\views.json`, имена из ProjectSettings этого
checkout) и на шлем (`mkdir -p`, `adb push` в `/sdcard/Android/data/<pkg>/files/e2e/<прогон>/views.json`) и передаёт обеим
сторонам `-e2eArg views=e2e/<прогон>/views.json` — путь от persistentDataPath. В `run.json` — блок `views` (`source`,
`sha256`, `count`, `names`, `arg`, пути копий, `pushed`, `serverCopied`).

В сценарии: `E2EViews.TryLoadFromContext(context, out views, out error)` читает файл (путь — внутри persistentDataPath
или относительный к нему), `yield return E2EViews.GoTo(точка, outcome)` переносит свой аватар штатным
`UxrManager.MoveAvatarTo` (единственный писатель позы; без записи в трансформы, трекинг, ввод и калибровку), ждёт
3 кадра и снимает фактическую позу камеры: позицию, курс, тангаж, крен, высоту глаз над полом, пол аватара и отклонения
от запроса по XZ, курсу и уровню пола. `E2EViews.ToJson(outcomes)` — артефакт. В `device-smoke` клиент после своей
сессии и аватара обходит точки, пишет `persistentDataPath/e2e-smoke/views-<utc>.json` (забирается в
`artifacts/client-1/`) и выносит проверку «обход точек обзора» (все точки достигнуты, отклонение XZ < 0.05 м, курс
< 2°); сервер — проверку «файл точек обзора прочитан» с именами по порядку.

**Поза головы (`-HeadPose`, контракт r5).** Перф-замер не должен требовать, чтобы человек держал шлем на высоте глаз.
По умолчанию дирижёр передаёт шлему `-e2eHeadPose eye=1.6`, и `E2EHeadPose` (только собранный плеер-клиент с
`-e2eScenario`; в редакторе не включается) у `UxrAvatar.LocalAvatar` выключает трекинг камеры штатным
`CameraTrackingEnabled = false` и сам пишет local pose камеры в `UxrManager.StageUpdating(Update)`: камера над пивотом,
высота глаз над корнем = цель, тангаж и крен 0, курс — от корня (его ставит `E2EViews`). Пивот, кости, BodyIK и
калибровка не пишутся; при смене локального аватара трекинг прежнему возвращается, новому выключается; при выходе
трекинг возвращается.

- `-HeadPose <м>` — высота глаз над корнем аватара (`0.9..2.5`, точка — разделитель) → `-e2eHeadPose eye=<м>`;
  `-HeadPose calib` — цель из применённой калибровки (`Applied.EyeHeight`, без неё 1.6) → `-e2eHeadPose calib`;
  `-HeadPose off` — флаг не передаётся, **реальный трекинг шлема**: для замеров с настоящей позой. Неверное значение —
  отказ (код 2) до устройства. Серверу флаг не передаётся. В `run.json` — блок `headPose` (`value`, `mode`, `eye`,
  `clientArg`, `renderCheck`, `render`).
- **Что симулируется:** только голова — высота глаз и горизонт (тангаж/крен 0). **Что нет:** руки. Лежащие
  контроллеры по-прежнему ведут IK рук — руки могут попасть в кадр и влиять на GPU; статус трекинга контроллеров
  (Unity XR `isTracked`) пишется в detail проверки. С `eye=<м>` замер роста калибровки в сценарии не запускать —
  он измерил бы цель симуляции (`calib` измерит ту же цель).
- **Условие рендера:** OculusSettings `LateLatching` и `SpaceWarp` выключены — иначе рантайм подменял бы позу рендера
  позой шлема. Дирижёр до устройства разбирает манифест APK (`-Apk` или собранного `-Build`) через
  `aapt dump xmltree` из build-tools Android SDK Unity (ключ `com.unity.xr.oculus.LateLatching`; SpaceWarp
  Oculus XR Plugin 4.5 в манифест не пишет): включён любой при симуляции — отказ (код 2); проверить нельзя (нет
  `-Apk`, нет aapt, ключа нет в манифесте) — предупреждение и `headPose.renderCheck = unknown`, без отказа. На шлеме
  `E2EHeadPose` проверяет `OculusSettings.s_Settings` сам: включены — `GameLog.Error`, симуляция не включается.
- Ошибка разбора `-e2eHeadPose` в плеере — `GameLog.Error` (`E2EContext.HeadPoseError`), симуляция не включается.
- **`device-smoke`** с флагом: после своей сессии и аватара — 30 кадров выдержки, затем 300 кадров замера
  `camera.y − root.y`, тангажа и крена; проверка «поза головы симулирована»: |высота − цель| ≤ 0.01 м,
  |тангаж| и |крен| ≤ 0.5°, разброс ≤ 0.005 м и ≤ 0.2°; в detail — числа, посторонние изменения позы камеры,
  итог проверки рендера и статус контроллеров. С `-ViewsFile` высота головы в каждой точке тоже в пределах цели ±0.01 м,
  а views-артефакт получает блок `headPose` (цель, режим, высота и отклонение по точкам).

#### Что где лежит

| Что | Где |
|---|---|
| Дирижёр | `Tools/e2e/Run-E2E.ps1`; шлем — `Tools/e2e/Run-QuestE2E.ps1` |
| Артефакты прогона (JSON + логи плееров) | `Tools/e2e/results/<дата>-<сценарий>/` |
| Харнесс внутри плеера | `Assets/Scripts/Debug/E2E/` |
| Сценарии | `Assets/Scripts/Debug/E2E/Scenarios/` |
| Сборка плеера | `Assets/Editor/VR_Battlegrounds/Debug/E2EPlayerBuilder.cs` |
| Плеер | `Build/e2e/` (в `.gitignore`) |

Аргументы плеера: `-e2eScenario`, `-e2eRole`, `-e2eResult`, `-e2eTimeout`, `-e2eMap`,
`-e2eClients`, `-e2eLate <роль>`, `-e2eServerAddress`, `-e2eDeviceToken`, `-e2eNetworkPort`/`-e2eDiscoveryPort`,
`-e2eArg ключ=значение` (повторяемый), `-e2eQuitOnFinish` (выход после вердикта в любой роли; относительный `-e2eResult` — от
`persistentDataPath`). Без `-e2eScenario` харнесс
не поднимается вообще, поэтому в обычном билде он инертен.

Харнесс ставится не через сцену и не через префаб, а через
`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`. Причина: сцены и префабы здесь —
общий ресурс, и тестовый компонент внутри `--- MANAGERS ---` ломал бы обычный запуск игры.

Стрип из релиза — `#if !VRBG_NO_E2E`. Прод-сборка `GameBuilder` (конфигурация Prod) добавляет `VRBG_NO_E2E` всем
профилям сама ([release.md](release.md)); вручную — в Scripting Define Symbols. Инвертированный вид намеренный: по умолчанию код **компилируется** и попадает
под ворота уровня 0, а прячется только явным решением. Обычный `#if VRBG_E2E` вывел бы
харнесс из-под компиляции и вернул бы класс ошибок BUILD-01.

#### Формат вердикта

```json
{ "scenario": "dedicated-server-arsenal", "role": "server", "passed": false,
  "status": "completed", "summary": "...", "durationSeconds": 21.27,
  "checks": [ { "name": "...", "passed": false, "detail": "..." } ] }
```

`status`: `started` (заглушка) · `completed` · `timeout` · `error`. Файл пишется
**всегда**: заглушка со `started` — ещё до загрузки первой сцены, вердикт — по
завершении, отдельные статусы — на таймаут, исключение и досрочный выход процесса.
Проверки объявляются заранее, поэтому у невыполненных в `detail` стоит причина обрыва,
а не пустота. JSON выводится чистым ASCII (кириллица уходит в `\uXXXX`) — так файл
читается независимо от кодовой страницы консоли.

#### Вердикт по логам и известный шум

Контракт [quest-device-run](../tasks/quest-perf-pipeline/contracts/quest-device-run.json) r4. Зелёные проверки
сценария — ещё не зелёный прогон: каждый процесс (сервер, клиенты, шлем) сам собирает ошибки своего лога, а дирижёр
(`Run-E2E.ps1`, `Run-QuestE2E.ps1`, модуль `Tools/e2e/E2ELogVerdict.psm1`) сводит их в вердикт по логам.
`passed` результата от этого не меняется — это по-прежнему вердикт сценария.

**Что ловится.** `E2ELogCapture` подписывается на `Application.logMessageReceivedThreaded` записью заглушки
результата и снимается финальной записью (`completed`/`error`/`timeout`). Собираются `Error`, `Exception` и `Assert`
— в том числе `GameLog.Error` и необработанные исключения; Log и Warning отбрасываются первой проверкой, без
аллокаций (условие perf-зоны). Повторы агрегируются по паре «тип + первая строка сообщения»; в ключе числа, части GUID
и хеши (слова из шестнадцатеричных символов с цифрой) заменяются на `#`, чтобы одна ошибка с разными id не дробилась.
В записи остаётся исходный текст первого появления. В результат уходят
`logIssues` `[{type, message, stack, count, firstAtSeconds}]` (`stack` — продолжение многострочного сообщения и
первые кадры, до 5 строк; `firstAtSeconds` — от создания результата), до 200 уникальных записей, сверх лимита —
счётчик `logIssuesDropped`, и `knownNoise` сценария. Ошибки до создания результата и после финальной записи в файл
не попадают. Нативные падения обработчик не видит: дирижёр ищет в сырых логах `Crash!!!` (лог плеера Windows) и
`Fatal signal` (logcat шлема — только строки pid приложения, поток logcat содержит все процессы устройства).

**Классификация.** Запись — known, если regex известного шума (.NET, с учётом регистра) совпал с текстом
`message + "\n" + stack`: сначала шум сценария этого процесса, затем шум каркаса `Tools/e2e/known-noise.json`
(`{ "version": 1, "entries": [ { "id", "regex", "reason", "scope": "any|server|client" } ] }`, владелец —
launch-infra, запись только с доказательством безвредности в `reason`). Остальное — unexpected. Нативные падения
шумом не гасятся.

**Известный дефект** — запись того же списка с `"kind": "defect"`, `owner` (зона-владелец) и `ticket` (задача или
событие хаба). Это не шум: прогон с такой ошибкой остаётся red, в отчёте она идёт отдельным списком `defects` с
владельцем и задачей — видно, что ошибку уже разбирают. Запись удаляется, когда дефект исправлен. Ошибки
исправляются, а не фильтруются: «объявить шумом», чтобы прогон позеленел, нельзя.

**Шум сценария** объявляет эксперт зоны в коде сценария — до того, как ошибка случится:

```csharp
// Сценарий сам рвёт соединение клиента: ошибка ожидаема, её следствие проверяет «переподключение».
result.DeclareKnownNoise("forced-disconnect",
    @"^\[GameNetworkManager\] Соединение потеряно",
    "сценарий рвёт соединение на шаге 3 намеренно; восстановление проверяет проверка «переподключение»");
```

`id`, `regex` и `reason` обязательны; повтор `id` заменяет прежнее объявление; неверный regex — `GameLog.Error`, шум
не объявлен (а эта ошибка сама станет unexpected). Объявлять узко: якорь `^`, характерный кадр стека, причина —
почему безвредно и откуда это известно. Шум, общий для всех сценариев, — запрос владельцу каркаса (launch-infra),
а не копия в каждом сценарии.

**Где смотреть.** `log-verdict.json` в папке прогона: `mode`, `verdict` (`green`/`red`/`error`), `reasons`,
`unexpected[]`, `known[]` (с `id` и `source`: `scenario`/`framework`), `native[]` (файл, строка, контекст),
`dropped`, `perProcess[]` (`captured=false` — в результате нет `logIssues`, например сборка без захвата), `notes`
(что не проверено). Сводный блок `logVerdict` — в `summary.json` (Run-E2E), в `run.json` и `summary.json`
(Run-QuestE2E); в консоли — строка `ЛОГИ: …` и до 10 примеров. `red` — есть unexpected, нативное падение или
`dropped > 0` (ошибки сверх лимита не классифицированы).

**`-LogVerdict enforce|report`.** `enforce` (по умолчанию): `red` превращает прогон, который иначе был бы `0`, в код
`1`; битый список шума (неверный regex, нет `version`, пустой `reason`) — `verdict=error` и код `2`. Коды `1` и `2`
не меняются. `report` — вердикт считается и пишется, код прогона не меняется: для диагностики, когда известная, ещё
не исправленная ошибка вне вашей зоны мешает увидеть результат сценария, и для замеров, где важен только сценарий.
Постоянным обходом `report` не делать — ошибку исправить или объявить шум с доказательством.

#### Мультипроцессный сценарий: барьер, шаги, роли

Каркас этапа e2e-smoke-kit (`Assets/Scripts/Debug/E2E/E2EBarrier.cs`): сервер и клиенты синхронизируются **своими**
шагами, а не игровым состоянием (`Score`, `HasGrabbedDogTag` и т.п. игра сбрасывает).

**Роли.** `server` (или `host`) — индекс 0; `client-1..client-N`, N — `-e2eClients` (дирижёр шлёт всем процессам).
В контексте: `ctx.ClientIndex`, `ctx.ClientCount`, `ctx.IsServerSide`, `ctx.IsLateClient`, `ctx.LateRole`. Поздний
клиент — последний (`Run-E2E.ps1 -LateClientDelay`), всем процессам уходит `-e2eLate client-N`; сервер не ждёт его в
барьерах без `includeLate`.

**API.**
- `ctx.Step(name, data = null)` — шаг достигнут: журнал; клиент ещё и отправляет его серверу. Повтор идемпотентен:
  сервер хранит первое достижение и последнее data. Имя ≤64, data ≤256, ≤512 шагов — лишнее отброшено с ошибкой в логе.
- `ctx.ServerStep(name, data = null)` — шаг сервера: журнал и рассылка клиентам.
- `yield return E2EBarrier.WaitClients(ctx, step, timeout, includeLate: false, onReached: d => …)` — сервер ждёт всех
  ожидаемых клиентов, `d` — роль → data. `yield return E2EBarrier.WaitServer(ctx, step, timeout, onReached: data => …)` —
  клиент ждёт шаг сервера. Имена шагов динамические (`$"map-{i}-ready"`).
- Провал барьера (таймаут, шаг `failed` ожидаемого клиента, ошибка роли) — красная проверка `barrier <шаг>` с
  диагнозом «дошли …; нет …», прогон завершается сразу (RED, код 1). Красный сервер рассылает abort: клиенты сразу
  завершаются проверкой «сервер не прерывал прогон». Красный клиент шлёт шаг `failed` (data — причина), сервер его видит.

```csharp
// Сервер: серия из двух карт.
for (int i = 1; i <= 2; i++)
{
    string map = ctx.Arg("map" + i, "TestMap" + i);
    ctx.ServerStep($"map-{i}-load", map);                // клиенты увидят, какую карту ждать
    MapLoader.Instance.LoadMap(map);
    yield return E2EBarrier.WaitClients(ctx, $"map-{i}-ready", 180f,
        onReached: d => result.Set($"карта {i}", true, string.Join(", ", d.Keys)));
}
yield return E2EBarrier.WaitClients(ctx, "late-joined", 240f, includeLate: true);

// Клиент.
for (int i = 1; i <= 2; i++)
{
    string map = null;
    yield return E2EBarrier.WaitServer(ctx, $"map-{i}-load", 240f, onReached: d => map = d);
    yield return E2EWait.Until(onMap, "карта " + map, 180f, () => SceneManager.GetActiveScene().name == map);
    ctx.Step($"map-{i}-ready", map);
}
```

**Смена карты.** Пока клиент между сценами (NotReady) или переподключается, шаги копятся у него и уходят после нового
`e2e-ready`; сервер дедуплицирует. Таймауты барьеров вокруг загрузки карты — с запасом: 120–240 с на сборке, общий
`-Timeout` прогона — сумма всех с запасом. Поздний клиент ждите отдельным барьером (`includeLate: true`).

**Параметры.** `Run-E2E.ps1 -Arg map1=TestMap1,map2=TestMap2` → `-e2eArg` всем процессам; в сценарии —
`ctx.Arg("map1", "TestMap1")`. Пары — через запятую, без пробелов и кавычек.

**Журнал шагов.** В результате процесса — `steps [{name, role, data, utc, outcome}]` и `stepsDropped`; `outcome`:
`reached`, `updated`, `received` (шаг другой стороны), `failed`, `aborted`, `barrier-passed`, `barrier-failed`. Пока
сценарий идёт, файл переписывается не чаще раза в секунду — у убитого процесса видно, до какого шага он дошёл.
`Run-E2E.ps1` сводит журналы в общую ленту по UTC: блок `steps` в `summary.json` (поле `process` — чей файл), последние
30 строк — в консоли.

**Как гонять.** Локально: собрать e2e-плеер и `Run-E2E.ps1 -Scenario <имя> -Clients 2 [-LateClientDelay 60] [-Arg …]`.
В редакторе: PlayLaunch-стенд (выделенный сервер + клиенты, [test stand](test-stand.md)) и команда `run-e2e` каждому
участнику — разрешён любой сценарий из `E2ERunner`; роль `client-N` — по индексу участника, `-e2eClients` — число
клиентов стенда; `-e2eArg` и поздний клиент в редакторе не передаются.

**Самопроверка каркаса.** `Run-E2E.ps1 -Scenario e2e-kit-selftest -Map TestMap1 -Clients 2 -Timeout 300` — сервер и
клиенты проходят барьеры `client-on-map-1` / `client-on-map-2` через перезагрузку той же карты; сервер проверяет, что
пересланные после смены сцены шаги клиентов приняты без дублей. Красный — сначала чините каркас, потом игровые сценарии.

**Как устроено (для ревью).** Сообщения Mirror только для e2e (`#if !VRBG_NO_E2E`), регистрирует их каркас сам,
опросом старта сети, — только при `-e2eScenario` (в редакторе — в прогоне стенда). Сервер шлёт `e2e-ready` соединению,
когда оно аутентифицировано и готово; до него клиент ничего не шлёт (неизвестный id сообщения разорвал бы соединение).
Роль шага — из ожидаемого набора, одна роль — одно живое соединение (при переподключении прежнее соединение
отпускается до 15 с), иначе ошибка запуска и провал барьеров. Обработчики только пишут журнал. Исключение в
`condition`/`abortIf` у `E2EWait.Until` — провал ожидания (`Faulted`), а не обрыв прогона; вложенные ожидания
исполнитель прокручивает сам, поэтому общий таймаут и abort проверяются каждый кадр.

#### Сценарий `dedicated-server-arsenal` (NET-06, NET-13, NET-07, RDY-01)

Десять проверок на сервере. Первые пять служат гарантией валидности прогона: роль
`ServerOnly`, клиенты подключились, карта и стены арсенала на месте, начальное
пополнение из `OnStartServer` отработало, серверная машина раунда сменила фазу
`Setup → Equipment`. Последние две — сама находка NET-06: срабатывает ли
`RoundPhaseChangedLocal` на сервере и выходит ли хоть одна стена из `Closed`.

**Зелёный с T-13.** До правки эти две были красными: `RoundPhaseChangedLocal` на
сервере не срабатывало ни разу при том, что `SyncVar`-фазы шли `Setup → Equipment`,
и стены оставались `Closed x8`. После — событие приходит на `Equipment`, стены
`Closed x8 -> Open x8`.

**Контрольная проверка на клиентах.** Клиенты подписаны на то же самое статическое
событие. До правки оно срабатывало на них, но не на сервере — это изолировало причину
до роли процесса: код, событие и способ наблюдения одни и те же. Без такого контроля
красный результат на сервере нельзя было отличить от дефекта харнесса. После правки
контроль сменил содержание: первым событием на клиенте приходит `Setup` — начальное
значение спавна, которого при раздаче через `ClientRpc` не существовало в принципе.

**Проверка общей стены двухтактная (T-29).** Сначала сервер объявляет готовность
за **одного** игрока и держит это состояние десять секунд: стена обязана остаться
открытой и на сервере, и у обоих клиентов — это дефект **RDY-01**, из-за которого
первый взявший жетон закрывал арсенал всем. Затем готовность объявляется за всех,
и закрытие обязано доехать до обоих клиентов — это NET-07. Момент «готов ровно один»
клиенты ловят по реплицированному `PlayerSession.ReadyState`: договариваться о времени
между двумя клиентскими процессами было бы гаданием.

Фазы дальше `Equipment` этим сценарием не проверяются — для них есть отдельный
`round-readiness-match`, который прогоняет раунд целиком.

#### Сценарий `avatar-swap-death-replication` (NET-01, NET-02, NET-15)

Восемь проверок на сервере. Первые четыре — гарантия валидности прогона: роль `ServerOnly`,
две сессии, карта загружена и у каждой сессии живой аватар, обратный канал наблюдения
поднялся у **всех** клиентов. Дальше сервер дважды убивает одного и того же игрока:
до смены аватара (контроль) и после (находка), а между ними возвращает здоровье, чтобы
наблюдение заведомо не залипло.

**Смерть здесь — это `Life = 0`** через `PlayerController.RestoreHealth(0f)`. Именно
`UxrActor.Life` едет по каналу состояния, и именно из него клиент считает
`PlayerController.IsAlive`. `PlayerController.Die()` для проверки не годится: сам он
`Life` не меняет и шлёт только `RpcOnDied` по каналу Mirror, то есть мимо проверяемого
канала. Оружия в headless нет, поэтому штатный путь урона недоступен.

**Аватар меняется у другого игрока, не у жертвы.** Тогда единственное отличие между
контролем и находкой — сама смена аватара, а не состояние жертвы. Цель смены выбирается
детерминированно: пока в SDK есть `UxrMirrorAvatar._serverBroadcaster`, сценарий читает
его рефлексией и целится в владельца — иначе поле пережило бы смену и находка проявлялась
бы через раз. После T-12 поля нет, и цель берётся по порядку спавна.

**Вердикт целиком на сервере.** Клиент не знает, кого выбрали жертвой, а на коде до T-12
серверную рассылку получал не каждый клиент (владелец аватара-вещателя игнорировал
собственный `ClientRpc`). Поэтому клиенты не судят, а **зеркалят** серверу своё наблюдение
«вижу мёртвого игрока» через `PlayerSession.HasGrabbedDogTag`, а фазы прогона сервер
раздаёт им через `PlayerSession.Score`. Оба поля — канал Mirror, независимый от
проверяемого канала состояния, поэтому связь остаётся даже когда проверяемый канал мёртв.
Матч сценарий не запускает, так что игровой смысл этих полей никем не используется.

Приёмы, которые стоит повторять в следующих сценариях:

| Приём | Зачем |
|---|---|
| Рукопожатие обратного канала до измерений (сервер просит поднять флаг, потом опустить) | «никто не отчитался» в конце нельзя спутать с «отчитаться было нечем» |
| Фазы строго возрастающим числом, клиент реагирует на «фаза не меньше» | пропущенный кадр не сбивает клиента |
| Держать фазу ещё 3 с после выполнения условия | клиенты опрашивают фазу раз в кадр; мгновенно проскочившую фазу часть из них не увидит, и их собственные проверки станут ложно-красными |
| `PhaseDone` выставляется в `finally`, на любом выходе сервера | иначе клиентские процессы досидят до своего таймаута, и прогон станет INCONCLUSIVE вместо честного красного |
| Клиентская проверка сформулирована импликацией («увидел контроль ⇒ увидел и после») | клиент, которому наблюдать было нечего, не даёт ложного красного |

Красный прогон до правки T-12 показал больше, чем ожидалось: не доезжала и **контрольная**
смерть — то есть канал состояния не работал вообще (NET-15). Диагностика это различила
сразу: сервер писал в лог `Sending state sync`, ни один клиент — `Receiving state sync`,
`UxrComponentNotFoundException` не было ни у кого.

#### Сценарий `session-recovery-on-reconnect` (ARCH-01)

**Красный на текущем коде и обязан таким быть.** Запуск:

```
powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-E2E.ps1 `
    -Scenario session-recovery-on-reconnect -Clients 1 -Timeout 420 -SkipStaleCheck
```

**Один клиент, без матча.** Двух клиентов требует только старт матча
(`EliminationMode.IsPlayersReady` — игрок в каждой команде), а матч этому сценарию не
нужен. Второй процесс добавил бы только источников недетерминизма.

Замысел: игрок набирает состояние (имя, команда, скин, счёт, убийства, смерти, здоровье,
позиция), сервер рвёт ему соединение, клиент возвращается **тем же** `deviceToken` —
и должен вернуться со своим состоянием. Одинаковый токен обеспечивает дирижёр:
`-e2eDeviceToken` кладётся в `PlayerPrefs` до первого подключения, а
`GameNetworkManager.SendConnectMessage` читает его при **каждом** подключении.

Десять проверок на сервере. Четыре первые — гарантия валидности прогона (роль `ServerOnly`,
сессия создана, карта и живой аватар на месте, обратный канал клиента отвечает). Дальше
проверка самого размещения (`SessionRecoveryManager.Instance != null`), фиксация набранного
состояния, наличие снимка после разрыва, **контроль возврата** и две проверки того, что
именно восстановилось.

**Роль клиента — целиком контроль.** Клиент не судит о восстановлении: он доказывает,
что переподключение как таковое работает (подключился, получил сессию, переехал на карту,
увидел разрыв, вернулся и получил новую живую сессию). Зелёный клиент при красном сервере
и означает находку: связь есть, сессия есть, она просто пустая. Красный клиент означал бы
другое — что вердикт сервера недостоверен.

Результат прогона 2026-08-19: клиент зелёный целиком; на сервере зелёные все проверки
валидности **и контроль возврата** (`netId 74 -> 150`, новая сессия отвечает на
`CmdSetDogTagGrabbed`), красные — размещение менеджера, сохранение снимка, и обе проверки
восстановления: имя `Player_7648` вместо `e2e-recovered`, команда 0 вместо 2, скин 0
вместо 1, K/D/S `0/0/0` вместо `4/3/7`, здоровье 100 вместо 42, промах позиции 7.77 м.

Приёмы, добавившиеся к общему списку:

| Приём | Зачем |
|---|---|
| Разрыв инициирует **сервер** (`conn.Disconnect()`) | момент разрыва детерминирован и приходится ровно на набранное состояние |
| Проверка размещения не прерывает прогон | иначе контроль возврата не отработает, и красный станет неотличим от «клиент не вернулся» |
| Ожидаемая позиция снимается **непосредственно перед** разрывом | аватар на сервере может слегка осесть, и сравнение с задуманным значением было бы шумным |
| Команда назначается **до** загрузки карты | смена сцены уничтожает аватары, а `OnServerReady` пересоздаёт их через `ChangeAvatar`, который молча выходит при команде 0 — аватара после переезда не будет вовсе |
| Флаг обратного канала шлётся **до подтверждения эхом**, а не один раз | см. первую строку таблицы подводных камней ниже |

#### Сценарий `round-readiness-match` (T-29, RDY-01, последний пункт T-09)

```
powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-E2E.ps1 `
    -Scenario round-readiness-match -Clients 2 -Timeout 420 -SkipStaleCheck
```

**Единственный сценарий, прогоняющий раунд целиком.** До T-29 ярус C упирался в фазу
`Equipment`: выйти из неё можно было, только физически встав в зону спавна и взяв жетон
рукой, а рук в прогоне без шлема нет. Готовность стала явным состоянием с серверной
точкой записи (`PlayerSession.ServerSetReady`), и сервер сценария выставляет её сам.

Четырнадцать проверок на сервере, восемь на каждом клиенте. Что доказывается:

1. **RDY-01.** Готовность объявлена за одного игрока из двух, состояние держится восемь
   секунд: стена арсенала обязана остаться открытой и на сервере, и у обоих клиентов,
   а фаза — остаться `Equipment`. Плюс состав неготовых обязан содержать ровно второго
   игрока и доехать до клиентов (`EliminationMode.PendingReadiness`).
2. **Сквозной раунд.** `Equipment → Countdown → Combat →` настоящая смерть →
   `Resolution → Scoreboard →` следующий раунд, и второй раунд снова ждёт готовности
   (она обязана сброситься). Смерть настоящая: сервер бьёт аватары через
   `UxrActor.ReceiveDamage`, и раунд заканчивается штатной цепочкой
   `UxrActor.Died → PlayerController → MapReferee → EliminationMode.OnPlayerDied →
   RoundPhases.RequestRoundEnd`.
3. **T-09, длительности фаз по настоящим часам.** `Resolution` (3 с) и `Scoreboard` (5 с)
   до этого были проверены только в тиках EditMode, то есть в модельном времени.
   Сценарий ведёт ленту фаз по `Time.realtimeSinceStartup` и сверяет замер с заявленной
   длительностью, допуск ±0.5 с. Измеренное: **3.00 с и 5.00 с** во всех пяти прогонах.
4. **RDY-04, кому зона засчитывает «в зоне».** Сервер уводит игрока в базу противника
   и спрашивает `PlayerSession.IsInSpawnZone`. Физическое нахождение подтверждает сама
   зона (`TeamSpawnZone.IsPlayerFullyInZone`) — иначе «в чужой базе» могло бы оказаться
   «нигде». Проверок две: игрока возвращают на его спавн и требуют признак обратно,
   потому что «флаг не взвёлся» так же выглядело бы у полностью сломанной зоны.
   Шаг стоит **до** старта матча: в фазе `Equipment` идёт предел ожидания готовности
   (45 с), и прогулка по чужой базе съедала бы его.
   До правки: `IsInSpawnZone=True` при 16,12 м до своей зоны и 0,00 м до чужой.

Замеренная стабильность: **5 прогонов подряд зелёные** (2026-08-23; после добавления
проверок RDY-04 — ещё 5 подряд 2026-08-28).

#### Сценарий `weapon-hit-damage` (T-28)

```
powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-E2E.ps1 `
    -Scenario weapon-hit-damage -Clients 1 -Timeout 420 -SkipStaleCheck
```

**Красный на текущем коде и обязан таким быть.** Ровно одна проверка из десяти серверных —
«попадание отняло здоровье у игрока» — краснеет, пока в префабах `M16_Rifle_prefab`
и `Gun_real` маска слоёв у действующего `UxrProjectileSource` равна нулю
(см. [T-28](tasks/T-28-weapon-prefab-config.md)).

Что делает сервер: выбирает жертву, снимает оружие со стены арсенала, наводит его так,
чтобы луч из дула упирался в игрока, и стреляет через `UxrFirearmWeapon.TryToShootRound`.
Именно через него, а не через `UxrProjectileSource.Shoot`: нужен весь тракт — боезапас,
темп стрельбы, `CanUse`, выбор источника снарядов (`GetCachedComponent<UxrProjectileSource>()`
берёт **первый** компонент, а их на префабе два) и симуляция снаряда в `UpdateProjectiles`.
Прямой `UxrActor.ReceiveDamage` был бы зелёным и при нулевой маске: он входит в цепочку
урона ниже трассировки.

Приёмы, добавившиеся к общему списку:

| Приём | Зачем |
|---|---|
| **Контрольный опыт с подменённой маской** рядом с самой проверкой | «зелёное после правки» доказывается в том же прогоне, тем же оружием и той же наводкой. Маска живого `UxrShotDescriptor` на несколько секунд подменяется рефлексией на эталонную `Default\|Ground` и возвращается обратно. Опыт отличается от красной проверки **ровно одним полем**, поэтому вывод «причина — маска» не с чем спутать. Правку префаба это не заменяет: снаряд читает маску в момент выстрела |
| Двигается **оружие**, а не жертва | трансформы аватаров клиент-авторитетные (`syncDirection = ClientToServer`), серверный сдвиг аватара затёрло бы ближайшим снимком клиента |
| Направление выстрела ищется перебором: 12 азимутов × 5 углов подъёма, дистанция подгоняется под свободное место | после смены карты аватар создаётся в начале координат и вполне может стоять вплотную к реквизиту. Годным считается направление, у которого дуло не в геометрии, а первый же коллайдер на обратном луче принадлежит жертве |
| Положение оружия подтверждается **каждый кадр** стрельбы | предмет сетевой, у него `Rigidbody` и подписчики UltimateXR; `UxrGrabbableObject` доводит объект до якоря в `LateUpdate`, и «поставили и забыли» не работает |
| Жертве назначается скин, у которого **есть** непроходной коллайдер | у четырёх скинов из семи их нет вовсе (см. [WPN-02](audit/network-audit-2026-08.md)), и без явного выбора этот дефект подменял бы вердикт по T-28 |

Измерительные строки вердикта (всегда зелёные, читать как замер): обзор скинов, состав
`UxrProjectileSource` на выбранном оружии и настройки выстрела **всех** стволов реестра.

Замеренная стабильность: **5 прогонов подряд** (2026-08-23), результат побайтово
одинаковый — 14 зелёных, 1 красная, контрольный опыт снимает ровно 25 HP одним выстрелом.

#### Сценарий `shot-pipeline-budget` (замер для T-23 и T-24)

Единственный **измерительный** сценарий яруса C: он не сторожит поведение, а снимает числа
для решений в [`combat-networking.md`](combat-networking.md). Порогов «хорошо/плохо» внутри
нет намеренно — измерительный сценарий, который сам себе назначил порог, краснеет на смене
железа, и его перестают читать. Красным он становится только когда замер невозможен:
не нашлось оружия, выстрел не состоялся, клиент не подключился.

Запуск: `-Scenario shot-pipeline-budget -Map TestMap1 -Clients 1`.

Что снимает:

| Сторона | Величина | Зачем |
|---|---|---|
| сервер | настройки выстрела: скорость, дальность, маски слоёв, вычисленное время жизни снаряда | из них считается число трассировок в кадре (T-24) |
| сервер | состав `UxrProjectileSource` на предмете | так вскрылся дубль компонента (T-28) |
| сервер | `conn.rtt` по подключениям, мин/сред/макс | |
| клиент | `NetworkTime.rtt`, `NetworkClient.bufferTime`, множитель буфера | фактическая задержка отрисовки чужого объекта (T-23) |
| клиент | **появились ли у него снаряды после выстрела сервера** | единственная фальсифицируемая проверка: её краснота опровергла бы посылку T-24 |

Снаряды считаются по корню активной сцены, по имени инстанса (`Projectile(Clone)`), а не
рефлексией в приватный `UxrWeaponManager._projectiles`: наблюдаемо ровно то же, а новой
зависимости от закрытого поля SDK не заводится.

Приёмы, добавившиеся к общему списку:

| Приём | Зачем |
|---|---|
| Обе стороны выбирают предмет с **наименьшим** `netId` | `netId` раздаёт сервер, поэтому выбор одинаков на всех машинах без всякого согласования; в вердикт печатается `netId`, и расхождение сразу видно |
| Стрельба через `Shoot(0, position, rotation)`, а не `Shoot(0)` | не зависит от того, куда смотрит лежащее на стене оружие, и одинаково воспроизводится от прогона к прогону |
| Годным считается источник с заполненными `ShotSource` и `Tip` | на префабах оружия два `UxrProjectileSource`, и второй роняет `Shoot` на `NullReference` |
| Пик снарядов добирается ещё `BurstShots × BurstInterval + 1,5` с после первого | условие ожидания выходит на первом же снаряде, а очередь только начинается |

**Что важно знать про его числа.** RTT по петле loopback измеряет конкуренцию трёх процессов
Unity за процессор, а не сеть, и как нижнюю границу канала брать его нельзя. Буквально
читается только `bufferTime`: его минимум (33,3 мс = `2 × sendInterval`) — настроенный пол,
ниже которого Mirror не опускается никогда.

#### Сценарий `calibrated-position-persists` (CAL-01)

```
powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-E2E.ps1 `
    -Scenario calibrated-position-persists -Clients 2 -Map TestMap1 -Timeout 420 -SkipStaleCheck
```

Сторож требования «после калибровки место игрока задано физически и переживает смену
карты». Правильных ответов на вопрос «куда ставить» два, и сценарий проверяет **оба
в одном прогоне**: клиент-1 объявляет калибровку, клиент-2 нет, после смены карты они
обязаны оказаться в разных местах. Одна ветка без другой ничего не доказывает — «всех
в зону» и «никого не двигать» по отдельности выглядят одинаково правдоподобно.

| Проверка | Что делает сервер | До правки |
|---|---|---|
| откалиброванный остался на своём месте в арене | уводит его на известную точку арены, затем `LoadMap(TestMap2)` | **красная**: игрок в своей зоне, 12,06 м от своего места |
| неоткалиброванный оказался в зоне своей команды | ничего дополнительно | зелёная, это контроль |
| ветки различаются | сравнивает расстояние откалиброванного до его зоны | **красная**: 0,00 м при нужных 4,5 м |

Приёмы, добавившиеся к общему списку:

| Приём | Зачем |
|---|---|
| **Две разные карты, а не перезагрузка одной** | обе карты — одна и та же арена, но в `TestMap1` она была повёрнута на 90° вокруг Y. Перезагрузка одной карты не отличила бы «позиция сохранена относительно якорей» от «позиция сохранена в мировых координатах». **С 2026-09 арены выровнены** (`MapAlignmentTests`), и сценарий отвечает «вердикт вынести нельзя»; перенос по якорям проверяет `SpawnPlaceRegistryTests` |
| **Точка замера сдвинута от центра арены на 4 м** | первая версия брала сам центр — и это была дыра: арену поворачивали вокруг её собственного начала координат, поэтому центр у карт общий и в мировых координатах тоже, и наивное сохранение мировой позиции проходило проверку. Сдвинутая точка расходится на 5,66 м, втрое больше допуска |
| Эталон вычисляется **из зон спавна**, а не из якорей | сравнивать место откалиброванного игрока с тем же кодом, который его и посчитал, значит проверять код им самим. Зоны ставит художник, к калибровке они отношения не имеют |
| Направление сдвига задано **командой игрока** («от своей базы к чужой») | только так одна и та же точка арены получается на обеих картах и у сервера, и у клиента — порядок объектов на сцене для этого не годится |
| Отдельная проверка-предусловие «якоря обеих карт задают одну систему координат» | если якоря карт разъедутся с ареной, одна калибровка на сессию работать не может, и прогон обязан стать INCONCLUSIVE, а не ложно-красным на самой правке |
| Калибровка **объявляется**, а не выполняется | процедура читает положение шлема и контроллеров, то есть требует железа. Клиент шлёт `CmdSetCalibrated(true)` — ровно то, чем закончилась бы настоящая калибровка. Тот же приём в `calibration-scale-replication` |

Вердикт печатает три расстояния разом: до своего места, до зоны команды и до **старой
мировой позиции**. Третье отличает «позицию не сохранили вовсе» от «сохранили, но
в мировых координатах».

#### Сценарий `connect-place-across-map` (CAL-02)

```
powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-E2E.ps1 `
    -Scenario connect-place-across-map -Clients 1 -Map TestMap1 -Timeout 420 -SkipStaleCheck
```

Сторож того же правила, что и `calibrated-position-persists`, но на **пути подключения**,
а не смены карты: клиент подключается на одной карте, набирает своё место, уходит; сервер
меняет карту; клиент возвращается. Прогон состоит из двух кругов на одном игроке —
первый **без** калибровки, второй **с** ней:

| Круг | Что обязано получиться | До правки |
|---|---|---|
| без калибровки | зона своей команды | **красный**: 9,00 м до зоны, 0,00 м до старой мировой точки с прошлой карты |
| с калибровкой | своё место в арене | **красный**: 5,66 м до своего места, 0,00 м до старой мировой точки |

Приёмы поверх общего списка и того, что уже описано у `calibrated-position-persists`:

| Приём | Зачем |
|---|---|
| **Один клиент, а не два** | здесь дважды рвётся соединение, а разрыв — самая недетерминированная часть харнесса (NET-20). Второй процесс добавил бы только источников мигания, а обе ветки правила проверяются и последовательно |
| **Карта меняется после разрыва, а не до** | так снимок отключённой сессии снят на старой карте — ровно та ситуация, в которой мировая позиция врёт. Меняй карту раньше, и путь не воспроизведётся |
| Клиент возвращается **прямым `StartClient`**, дождавшись смены экземпляра `NetworkManager` | рецепт из `session-recovery-on-reconnect`: подключение в промежутке уходит в уже обречённый менеджер и навсегда вешает `connectState` в `Connecting` |
| Отдельная клиентская проверка «вернулся дважды» | красный вердикт сервера иначе не отличить от «клиент не доехал», и прогон стал бы ложно-красным на самой правке |

**Чего сценарий не доказывает.** Бит `isCalibrated` в самом сообщении подключения:
клиент в харнессе не калибруется, а объявляет результат командой `CmdSetCalibrated`,
поэтому `PhysicalSpaceSyncManager.IsCalibrated` у него остаётся ложным, и во втором круге
признак приезжает на сервер снимком сессии. Случай «откалиброванный игрок, которого
сервер видит впервые» закрыт ярусом A (`SavedAvatarPlaceTests`).

Замеренная стабильность: **5 прогонов подряд красные до правки, 5 подряд зелёные после**
(2026-08-28). В зелёном прогоне поза откалиброванного игрока относительно якорей
не изменилась вовсе: `(-7.60, 0.01, 2.74)` до разрыва и столько же после смены карты.

#### Сценарий `avatar-spawn-point` (WPN-03)

```
powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-E2E.ps1 `
    -Scenario avatar-spawn-point -Clients 1 -Timeout 420 -SkipStaleCheck
```

Сторож выбора точки спавна аватара. Один вызов `AvatarManager.ChangeAvatar` обслуживает
три разных события, и правильная точка у каждого своя — сценарий проверяет все три подряд,
на одном игроке, за один прогон:

| Проверка | Что делает сервер | До правки |
|---|---|---|
| после смены карты аватар в зоне спавна своей команды | `MapLoader.LoadMap` → `OnServerReady` пересоздаёт аватар сам | **красная**: `(0.00, 0.00, 0.00)` при зоне в `(-8.06, 0, 0)` |
| смена скина не двигает игрока | уводит игрока из зоны на 6 м, затем `ChangeAvatar(та же команда, другой скин)` | зелёная, это контроль |
| смена команды переносит в новую базу | `ChangeAvatar(другая команда, скин 0)` | **красная**: игрок остаётся в 17,20 м от зоны новой команды |

Клиент проверяет то же самое с той стороны, с которой это болит: где он видит **свой**
аватар в момент, когда сервер объявил фазу замера.

Приёмы, добавившиеся к общему списку:

| Приём | Зачем |
|---|---|
| **Игрока уводят из зоны перед контрольной проверкой** | пока он стоит в зоне, «остался на месте» и «переехал в зону» дают один ответ, и контроль не различает исправленный код от наивного «всегда брать точку спавна» |
| Увод — через `PlayerController.Respawn`, а не записью `transform.position` | трансформы аватаров клиент-авторитетные (`syncDirection = ClientToServer`); `Respawn` рассылает `RpcOnRespawned`, и переезд делает сам владелец. Прямая запись оставлена запасным вариантом на случай, если владелец за 15 с не отчитался |
| Позиция читается **после выдержки** 3 с, а не в кадре создания | по той же причине: замер обязан отвечать на вопрос «где игрок оказался», а не «где его создали». В вердикт печатаются оба значения и разница между ними |
| Отдельная проверка-предусловие «зоны разведены с началом координат» | на карте, где база стоит в `(0, 0, 0)`, сломанный и исправленный код неотличимы; такой прогон обязан быть INCONCLUSIVE, а не ложно-зелёным |
| `DebugOrchestrator` гасится первой же строкой | именно он маскирует WPN-03 в отладочных запусках: увидев первый аватар сессии, он телепортирует игрока в зону его команды |

Замеренная стабильность: **5 прогонов подряд красные** до правки `AvatarManager`
и **5 подряд зелёные** после (2026-08-23), числа в вердикте побайтово одинаковые.

#### Подводные камни, найденные прогоном

Всё ниже вскрылось не чтением кода, а первыми прогонами. Экономит следующему агенту
часы.

| Грабли | Как проявляется | Что делать |
|---|---|---|
| **`.ps1` без BOM** | Windows PowerShell 5.1 читает файл как ANSI: кириллица в комментариях превращается в мусор, скрипт не парсится вообще | сохранять `Tools/e2e/*.ps1` в **UTF-8 с BOM**. После правки редактором проверять: `[Parser]::ParseFile(...)`. Правка через инструменты агента BOM теряет — восстанавливать |
| **`$Args` как имя параметра функции PowerShell** | конфликт с автоматической переменной, массив аргументов молча теряется — процесс стартует без флагов и ведёт себя «как обычная игра» | называть параметр иначе (`$Extra`) |
| **Свежесть билда по `.exe`** | инкрементальная пересборка **не трогает** `VrBattlegrounds.exe`, обновляются только сборки в `<плеер>_Data\Managed` | сравнивать по новейшей `*.dll` в `Managed`, как делает дирижёр |
| **Общий `PlayerPrefs` у двух клиентов** | `GameNetworkManager.SendConnectMessage` берёт `DeviceToken` из `PlayerPrefs`, а два процесса одного билда на одной машине делят его — сервер видит два подключения как одно устройство | передавать `-e2eDeviceToken` каждому клиенту, харнесс кладёт его в `PlayerPrefs` до первого подключения |
| **`-nographics` определяет роль** | `Mirror.Utils.IsHeadless()` смотрит на `SystemInfo.graphicsDeviceType == Null`, поэтому клиент с `-nographics` станет **сервером** | клиентам графику не отключать, гасить окно через `-screen-width/-screen-height` |
| **`ArgumentNullException: Parameter name: shader`** на сервере | `UxrAvatar.SetAvatarRenderMode` создаёт `Material` от `Shader.Find`, а под `-nographics` шейдеры не грузятся | это штатный шум headless-сервера, не отказ логики. Игру не ломает, аватары спавнятся |
| **Матч не стартует с одним клиентом** | `EliminationMode.IsPlayersReady` требует `minPlayersToStart = 2` **и** игрока в каждой команде — сервер без клиентов или с одним навсегда висит в `WaitingForPlayers` | сценарию нужно ровно два клиента, и он сам раскладывает сессии по командам режима |
| **`DebugOrchestrator` как второй дирижёр** | его поведение задаётся личными настройками разработчика (Bootstrap Settings) — результат прогона начинает от них зависеть | сценарий останавливает его `DebugBootstrapGate.Suppress` и водит матч сам; в собранном e2e-плеере оркестратора нет вовсе |
| **`Command`, отправленная до `NetworkClient.Ready()`, молча теряется** | Mirror пишет `Command … called while NetworkClient is not ready` и наружу ничего не отдаёт. Активная сцена карты становится текущей **раньше**, чем Mirror объявит клиента готовым: `Ready()` зовётся из `FinishLoadSceneClientOnly`, то есть уже после `SceneManager.GetActiveScene()` | не слать команду один раз по факту «сцена загрузилась». Слать в цикле, пока не придёт эхо через `SyncVar`, и только при `NetworkClient.ready` |
| **`StartClient` сразу после разрыва уходит в обречённый менеджер** | Mirror после разрыва грузит `offlineScene`, и эта загрузка уничтожает тот `NetworkManager`, который её начал. Подключение, начатое в промежутке, оставляет `NetworkClient.connectState = Connecting` навсегда: клиент статический и переживает смерть менеджера, а каждый следующий `StartClient` выходит по «Client already started» | дождаться, пока `NetworkManager.singleton` **сменится на другой экземпляр** и `loadingSceneAsync` опустеет, и только потом подключаться |
| **После разрыва клиент вообще не может переподключиться** | новый `NetworkManager` из перезагруженной сцены `Offline` оказывался выключенным (`isActiveAndEnabled == false`), его `LateUpdate` не вызывался, `NetworkClient.isLoadingScene` навсегда `true`, а при взведённом флаге входящие сообщения не разбираются вовсе | это была находка **NET-20**, а не свойство харнесса. **Исправлено 2026-08-21**: отсев дубликата ветки менеджеров переехал в `PersistentRoot.Start`. Обход из сценария снят, сценарий зелёный без него. Если симптом вернётся — первым делом смотреть в `client-N.log` строку «Состояние клиента после возврата в Offline» |
| **Сборка под Standalone пачкает три файла** | после сборки плеера в `git status` появляются `ProjectSettings/ProjectSettings.asset` (`preloadedAssets` от XR-плагина, `m_BuildTargetBatching` для Standalone), `Assets/URPDefaultResources/PC.asset` и `Assets/Settings/Graphics/UniversalRenderPipelineGlobalSettings.asset` (пересчёт стриппинга шейдеров под PC) | это штатный побочный эффект Unity, не правка кода. В коммит e2e-прогона их не включать; при следующей сборке под Android значения пересчитаются обратно |

Отдельно: карту брать **`TestMap1` или `TestMap2`**, не `Lobby`. `MapReferee` живёт
в `MapReferee.prefab`, а тот стоит только в сценах карт — в `Lobby` матч запускать нечем.

#### Дефекты, вскрытые самим харнессом

Не исправлены (T-27 — только инфраструктура), но мешают писать проверки про арсенал:

- **`ArsenalSlotController.IsItemPresent` всегда `false`.** Свойство читает
  `UxrGrabbableObjectAnchor.CurrentPlacedObject`, а `AssignNetworkItem` только
  перепарентит объект под якорь и через `UxrGrabManager` не проходит — сеттер
  `CurrentPlacedObject` `internal` и остаётся не вызванным. Следствие:
  `NeedsReplenishment()` всегда `true`, то есть `ReplenishWeaponsNetwork(false)`
  при каждом заходе в фазу `Setup` заспавнил бы оружие во **все** слоты заново.
  Роли процесса это не касается — врёт везде, включая хост.
  Занятость слота проверять по `WeaponComponent` под `slot.ItemAnchor`.
- **`TestMap2` не собирается корректно как сетевая сцена.** Сборка плеера даёт
  `InvalidOperationException: Scene ... needs to be opened and resaved before building,
  because the scene object ArsenalWall (N) has no valid sceneId yet` — по одной на каждую
  из четырёх стен. Без `sceneId` Mirror не заспавнит объект, то есть арсенала на карте
  не будет ни у сервера, ни у клиентов. Лечится открытием и пересохранением сцены
  (нужен человек или MCP со сценой). На `TestMap1` этого нет.
- **На `TestMap1` восемь стен арсенала.** Не дефект сам по себе, но проверки про арсенал
  обязаны агрегировать по всем стенам, а не брать `FindFirstObjectByType`.

#### Что в headless доступно вопреки ожиданиям

Замерено 2026-08-22. Два действия, которые считались требующими шлема (уровень 5),
на самом деле доступны сценарию яруса C, и это заметно поднимает потолок автономной проверки.

- **Настоящий захват предмета.** `UxrGrabManager.Instance.GrabObject(grabber, grabbableObject,
  grabPoint, propagateEvents)` публичен, а `UxrGrabber` у аватара есть и без шлема
  (`PlayerSession.LocalSession.ActiveAvatar.GetComponentInChildren<UxrGrabber>()`).
  Захват проходит целиком: события `Grabbing`/`Grabbed`/`Removing`/`Removed`, перепарент,
  запрос авторитета, отправка события в канал состояния.
  **Оговорка:** предмет в руке не остаётся. Ближайший `UxrGrabManager.UpdateManipulation`
  в том же кадре видит «кнопка не нажата» (ввода в headless нет) и отпускает предмет.
  Поэтому факт захвата надо ловить событием `UxrGrabManager.ObjectGrabbed`, а не проверкой
  `grabber.GrabbedObject != null` — иначе проверка меряет отсутствующий ввод, а не код.
- **Настоящий смертельный урон.** `UxrActor.ReceiveDamage(damage)` публичен и на сервере
  работает: урон уводит актора в приватный `DieInternal`, тот обнуляет `Life`, поднимает
  `Died` и уезжает каналом состояния — у клиента метод переигрывается целиком.
  Это единственный способ проверить путь `UxrActor.Died`; присвоение `Life = 0`
  (`PlayerController.RestoreHealth(0)`) даёт только число и мимо `Died` проходит.

Общее для обоих: аватар у клиента появится, только если сессии назначена команда,
которую знает `TeamRegistry`, — `AvatarManager.ChangeAvatar` молча выходит иначе.
Поэтому даже сценарий без матча обязан звать `SessionManager.SetSession(map, "elimination")`
и разложить сессии по командам до `MapLoader.LoadMap`.

#### Чего на ярусе C ещё нет

Заготовлено место в реестре сценариев `E2ERunner.All()`; каждый следующий сценарий —
это класс, реализующий `IE2EScenario`, и строка в этом реестре.

| Сценарий | Находка | Что понадобится дополнительно |
|---|---|---|
| `calibration-scale-agreement` | VR-01 | рост берётся из калибровки, а её без шлема нет: понадобится подставлять высоту симулятору UltimateXR и сравнивать `UxrAvatar` одного клиента в окне другого |

### Шероховатость: проверка свежести билда даёт ложное «устарел»

Наблюдалось 2026-08-18 при приёмке T-12. Дирижёр берёт максимум из времени `.exe`
и свежайшей DLL в `<плеер>_Data\Managed` — логика верная. Но после пересборки через
`E2EPlayerBuilder.Run()` (успех, 14 с) проверка всё равно сообщила «устарел», указав
на исходник, который заведомо попал в билд: прогон с `-SkipStaleCheck` показал, что
плеер содержит правку (сценарий сам рапортует об отсутствии `_serverBroadcaster`).

То есть отметка времени на выходных файлах инкрементальной сборки не всегда обновляется.
Практическое следствие неприятное: страж, который врёт, приучает всегда запускать
с `-SkipStaleCheck`, и тогда он бесполезен.

Пока не разобрано. Если наступишь — прогоняй с `-SkipStaleCheck`, но **сначала**
убедись, что билд действительно свежий: пересобери и сверь по содержимому, а не
по времени. Разбираться стоит вместе с переносом сборки в CI.

### Что каким ярусом закрывается

| Задача | Ярус | Почему |
|---|---|---|
| T-02 двойная подписка | A | `InitializeActiveGame` помечен `[Server]`. **Закрыто тестом** `Победа_в_раунде_даёт_одно_очко` |
| T-04 восстановление сессии | A + PlayMode | снимок при отключении проверяется на ярусе A (**закрыто**); восстановление позиции при переподключении — только уровень 2: код внутри делает `Instantiate` + `Spawn` |
| T-11 связь сессия ↔ аватар | A+ | **Закрыто.** Связь несёт `[SyncVar] PlayerSession.ActiveAvatarNetId`; тест `ActiveAvatar_долетает_до_клиента` был красным до правки. На ярусе B проверка была бы ложно-зелёной. Гонку спавнов и кэш `PlayerController.Session` покрывают ещё два теста |
| T-12 канал состояния | **C** | ломается только при сервере, не являющемся клиентом. **Закрыто:** сценарий `avatar-swap-death-replication` был красным до правки (не доезжала даже контрольная смерть — NET-15), стал зелёным после переноса канала в `NetworkStateRelay`. Ярус A/B добавочно стережёт число подписок и отсутствие статики (`NetworkStateRelayTests`) |
| T-13 фаза как состояние | **C** | `ClientRpc` в `ServerOnly` — суть находки NET-06. **Есть проверялка:** `dedicated-server-arsenal`, красная на текущем коде |
| T-14 масштаб калибровки | **C** | нужны два разных клиента |
| ARCH-01 восстановление сессии | **C** + состав префабов | сам факт отсутствия компонента ловит `PrefabCompositionTests`, а последствие — сценарий `session-recovery-on-reconnect`. Оба красные на текущем коде |
| WPN-03 точка спавна аватара | **C** + A | ветка «старого аватара нет» достижима только настоящей сменой сцены: Mirror уничтожает аватары в `ServerChangeScene`, а пересоздаёт их `OnServerReady`. **Закрыто:** сценарий `avatar-spawn-point` (5 красных прогонов до правки, 5 зелёных после). Ярус A добавочно стережёт сам выбор точки — `AvatarSpawnPointResolverTests` |

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
| Автоматизация | частично: то, что покрыто сценарием, гоняется само — см. [ярус C](#ярус-c--два-процесса-настоящий-e2e) |

Единственная конфигурация, где сервер не является одновременно клиентом. NET-01 и NET-06
не воспроизводятся ни на хосте, ни в Play Mode.

> Этот уровень больше не обязан быть ручным. Инфраструктура автономного прогона —
> ярус C: `powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-E2E.ps1`. Руками
> смотреть только то, на что сценария ещё нет.

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

### Один шлем против бота

Второй игрок для строк «Оружие» и «Смерть» не нужен: планшет → «Обзор» → **«Матч с ботами»**
(T-48: игрок один на сервере — без админки и режима отладки) поднимет матч `elimination` 2×2 с тремя
ботами; отладочные входы — «Ботов» в `Tools/VR Battlegrounds/Debug/Bootstrap Settings…` или меню
`Tools/VR Battlegrounds/Debug/Bots/Add Bot` у редактора-сервера/хоста. Бот выравнивает команды, в
закупке идёт на базу, покупает по деньгам и объявляет готовность, в бою идёт к врагу и стреляет,
после раунда возвращается на базу, погибший — призраком идёт оживать в свою зону
([T-48](tasks/T-48-bots-match.md)).

Тот же бот годится агенту без шлема: хост в редакторе, `AddBot`, `SessionManager.SetSession`
+ `StartSession`, затем урон по боту (`UxrActor.ReceiveImpact` от актора игрока) или ожидание,
пока бот застрелит игрока редактора, — раунд, счёт и `Series` проверяются через
`execute_code`. Три ловушки такого прогона:

- **Тег Multiplayer Play Mode `Client` на основном редакторе** — Play поднимает клиента, а не хост,
  и он находит сервер в сети. Хост — `GameNetworkDiscovery.RestartAsHost()` из кода.
- **Локально запущен `VrBattlegroundsServer`** — его порты (игра 7778, discovery 47777) заняты.
  Перед `RestartAsHost` поменять `KcpTransport.port` (например, 7790); реклама discovery упадёт
  с `SocketException`, хост при этом поднимется.
- **Игрок редактора без шлема**: камера на полу, тело в позе префаба; бот целится в твёрдые
  коллайдеры, а не в голову-камеру. Между зонами на `TestMap1` стоят бочки — игрока редактора
  для дуэли переносят `UxrManager.MoveAvatarTo` на открытое место (например, `(2, 0, 0)`).

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
