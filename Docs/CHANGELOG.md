# Changelog (Журнал изменений)

Все важные изменения проекта будут фиксироваться в этом файле.

## [2026-08-18]
### Добавлено
- **Ярус C: e2e на двух процессах, автономный прогон одной командой (задача T-27).** `powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-E2E.ps1` поднимает выделенный сервер (`-batchmode -nographics`) и двух клиентов из одного плеера, ждёт от каждого процесса машиночитаемый вердикт, гасит процессы и сводит отчёт. Уровень 4 из [`testing.md`](testing.md) перестал быть ручным «смотреть в два окна»: вердикт выносит сценарий. Коды возврата различают три состояния — зелено, отказ пойман, прогон не состоялся; последнее важно, чтобы «находка воспроизвелась» не путалось с «харнесс не доехал». Полная сборка плеера ~5 мин, инкрементальная после правки C# ~17 с, сам прогон ~40 с.
- **Сценарий `dedicated-server-arsenal` — красный на текущем коде, как и требовалось (находка NET-06).** Семь проверок: пять зелёных гарантируют валидность прогона (роль `ServerOnly`, два клиента с сессиями, карта и восемь стен арсенала, начальное пополнение из `OnStartServer` — 64 слота из 64, фаза раунда сменилась `Setup → Equipment`), две красные — сама находка: `EliminationMode.OnRoundStateChangedLocal` на выделенном сервере не срабатывает ни разу, и ни одна стена не выходит из `Closed`. **Контроль поставлен на клиентах:** они подписаны на то же статическое событие, и на них оно срабатывает — значит красное на сервере это разница ролей, а не дефект харнесса. Без такого контроля красный результат ничего не доказывает.
- **Харнесс внутри плеера — `Assets/Scripts/Debug/E2E/`.** Ставится через `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`, а не через сцену или префаб: сцены и `--- MANAGERS ---` здесь общий ресурс, и тестовый компонент в них ломал бы обычный запуск игры. Без `-e2eScenario` не поднимается вообще. Файл вердикта пишется **всегда** — заглушка со `status: started` до загрузки первой сцены, затем `completed` / `timeout` / `error`; проверки объявляются заранее, поэтому у недошедших в `detail` стоит причина обрыва. Проверено диверсией: неизвестное имя сценария даёт `error`, урезанный таймаут — `timeout`, оба с файлом и осмысленным `detail`. Стрип из релиза — `#if !VRBG_NO_E2E`; инверсия намеренная, чтобы код по умолчанию компилировался и попадал под ворота уровня 0.

### Обнаружено (не исправлено)
- **`ArsenalSlotController.IsItemPresent` всегда `false`, а `NeedsReplenishment()` всегда `true`.** Свойство читает `UxrGrabbableObjectAnchor.CurrentPlacedObject`, но `AssignNetworkItem` только перепарентит объект под якорь и через `UxrGrabManager` не проходит, а сеттер `CurrentPlacedObject` `internal` — то есть остаётся не вызванным. Следствие: `ReplenishWeaponsNetwork(false)` при каждом заходе в фазу `Setup` заспавнил бы оружие во **все** слоты заново. Роли процесса это не касается, врёт везде, включая хост. Найдено харнессом: первая версия проверки использовала `IsItemPresent` и краснела при том, что в логе `ReplenishWeaponsNetwork` честно раздал 64 предмета.
- **`TestMap2` не собирается как сетевая сцена.** Сборка плеера даёт по одному `InvalidOperationException: Scene ... needs to be opened and resaved before building, because the scene object ArsenalWall (N) has no valid sceneId yet` на каждую из четырёх стен. Без `sceneId` Mirror объект не заспавнит: арсенала на карте не будет ни у сервера, ни у клиентов. Лечится открытием и пересохранением сцены. На `TestMap1` проблемы нет — прогоны идут на ней.

### Исправлено
- **Связь сессия ↔ аватар реплицируется (задача T-11, находки NET-04 и NET-05, «Корень 2»).** Связь была собрана из двух разнородных половин: `[SyncVar] PlayerController.SessionNetId` в одну сторону и обычное C#-свойство `PlayerSession.ActiveAvatar` в другую. На клиенте `ActiveAvatar` был `null` всегда, спросить «где мой аватар» клиентскому коду было негде — и два места угадали одинаково неправильно (`TeamSpawnZone` искал `PlayerController` в `NetworkClient.localPlayer`, `PlayersManager` — в `conn.identity`; там лежит сессия). Теперь источник правды один: `[SyncVar(hook)] uint PlayerSession.ActiveAvatarNetId`, а `ActiveAvatar` — кэш поверх него, доступный и на клиенте, и на сервере. Разбор — в [`session-architecture.md`](session-architecture.md#связь-сессия--аватар-t-11).
- **Гонка спавна закрыта с обеих сторон.** Порядок доставки спавн-сообщений Mirror не гарантирует. Если хук получил netId раньше самого объекта — связь закрывает аватар, вызывая `PlayerSession.NotifyAvatarSpawned` из `OnStartClient`/`OnStartServer`; чужой аватар при этом игнорируется, авторитет остаётся за `ActiveAvatarNetId`, который ставит только сервер. Третья страховка — ленивое разрешение в геттере. Отдельная тонкость: `SyncVar`-хук Mirror зовёт на сервере **только в host-режиме** (`NetworkServer.activeHost`), поэтому на выделенном сервере связь проставляет сеттер свойства, а не хук.
- **`PlayerController.Session` кэшируется** в `OnStartServer`/`OnStartClient` вместо поиска в `spawned` на каждое обращение: через это свойство идут `Team` и `TeamIndex`, которые вызываются в циклах по всем игрокам (`GetTeamPlayersInZone`, `GetAlivePlayers`). Кэш сбрасывается хуком `SessionNetId`.
- **`TeamSpawnZone` больше не опрашивает `Update`** (это и была NET-04). Зона подписана на новое событие `PlayerSession.LocalAvatarChanged` и берёт аватар из `PlayerSession.LocalSession.ActiveAvatar`. Побочное следствие правки: ветка `UpdateVisibility` для активного раунда раньше не исполнялась **никогда** (локальный игрок не находился), теперь она рабочая — в неё добавлена проверка `Session` на null, иначе мёртвый игрок ронял бы зону вместо того, чтобы её увидеть.
- **`AvatarManager.SpawnAvatar` и `ChangeAvatar` проставляют связь после `NetworkServer.Spawn`,** а не до: до спавна `netId` равен нулю и клиенты получили бы пустую ссылку. Присвоение до спавна теперь пишет предупреждение в лог.

### Обнаружено (не исправлено)
- **Мёртвый игрок на выделенном сервере всё ещё не увидит зону вовремя.** Правка T-11 включила ветку видимости, которая раньше не исполнялась, и обнажила вторую половину NET-04: зона обновляется по событию `PlayerController.PlayerDied`, а оно поднимается внутри `Die()`, помеченного `[Server]`. У клиента `Die()` — заглушка, `RpcOnDied` о смерти никого не оповещает, поэтому зона появится только на ближайшей смене фазы раунда. На хосте не воспроизводится. Это уже не связь сессия ↔ аватар, а тот же класс проблемы, что NET-06 (состояние через `Rpc` вместо репликации) — записано в [`audit/network-audit-2026-08.md`](audit/network-audit-2026-08.md) к NET-04, лечится в T-13.

### Добавлено
- **Три теста на ярусе A+** (`PlayerSessionReplicationTests`), тестов в сборке стало 18. `ActiveAvatar_долетает_до_клиента` — перевёрнутый прежний `ActiveAvatar_не_долетает_до_клиента`, был красным до правки. `Смена_аватара_переключает_связь_на_клиенте` — старая ссылка не залипает при смене скина. `Аватар_заспавненный_позже_сам_чинит_связь` и `Session_аватара_кэшируется_а_не_ищется_каждый_раз` проверены диверсией: с отключённым `NotifyAvatarSpawned` и без кэша оба краснеют, то есть проверяют именно заявленный механизм.

## [2026-08-17]
### Добавлено
- **Харнесс сетевых тестов в одном процессе** (задача T-26), `Assets/Tests/EditMode/Network/MirrorTestHarness.cs`. Поднимает Mirror сервером без открытия сокета (`NetworkServer.listen = false`), опционально — с локальным клиентом. Это снимает главное препятствие: вне активного сервера Mirror молча заглушает `[Server]`-методы, и юнит-тест зеленеет, ничего не выполнив. Контроль проведён в обе стороны: без харнесса `EliminationMode.Initialize` оставляет `TeamStates.Count == 0`, с харнессом — 2; возвращённая вручную двойная подписка на `SetEnded` (дефект T-02) даёт счёт 2 вместо 1 и валит тест. Тестов в сборке стало 15 (было 6), прогон ~1 с.
- **Исходный рецепт из `testing.md` оказался неполон — исправлен по результатам прогона.** Четыре расхождения: (1) в EditMode Unity не вызывает `Awake` ни при `AddComponent`, ни при `Instantiate`, поэтому у `KcpTransport` нет внутреннего `KcpServer` (NRE в `ServerStop` при TearDown), а у `NetworkIdentity` пуст массив `NetworkBehaviours` (NRE при любом обращении к `isServer`) — оба `Awake` приходится звать рефлексией; (2) одного `NetworkClient.ConnectHost()` мало, без `HostMode.InvokeOnConnected()` соединение не попадает в `NetworkServer.connections` и очередь сообщений не разбирается никогда; (3) без `isAuthenticated = true` на обоих концах сервер рвёт локальное соединение на первом же сообщении; (4) в EditMode не крутится PlayerLoop, сетевой цикл (`NetworkServer/Client.NetworkEarly/LateUpdate`, все четыре `internal`) надо гонять руками.
- **Ярус A+ — репликация через настоящую сериализацию.** Обнаружено, что ярус B (host-режим) про `SyncVar` не доказывает ничего: `NetworkClient.OnHostClientSpawn` кладёт в `NetworkClient.spawned` ссылку на **тот же** объект из `NetworkServer.spawned`, сериализации не происходит, и любая проверка «долетело» зеленеет сама собой. Взамен состояние прогоняется через `SerializeServer`/`DeserializeClient` в отдельный объект-двойник — как у удалённого клиента. Этим зафиксирована находка T-11: `PlayerSession.TeamIndex` доезжает, `ActiveAvatar` (обычное C#-свойство, не SyncVar) — нет.
- **Границы EditMode задокументированы** ([`testing.md`](testing.md#чего-харнесс-не-умеет-границы-editmode)). Недостижимы в EditMode: любой код, который сам делает `Instantiate` + `NetworkServer.Spawn` (`PlayersManager.HandlePlayerConnect`, `AvatarManager.SpawnAvatar`/`ChangeAvatar`) — у клона не вызван `Awake`; `NetworkServer.SpawnObjects()` — подхватывает все `NetworkIdentity` открытой сцены и роняет и спавн, и последующий `Shutdown`; корутины. Поэтому из T-04 закрыта только половина цикла (снимок при отключении), восстановление позиции при переподключении уходит на уровень 2 (PlayMode).

### Исправлено
- **`SetManager`: сет не мог закончиться по исчерпанию раундов** (находка MATCH-06, задача T-25). `OnRoundEnded` вызывал `_roundManager.StartNextRound()` — одноимённый метод класса `RoundManager` — вместо собственного приватного. Из-за этого `_currentRound` инкрементировался только один раз, в `StartSet`: условие `_currentRound >= _roundsPerSet` не выполнялось никогда, а `RpcOnRoundStarted` уходил клиентам только для первого раунда (в HUD навсегда «раунд 1»). Подтверждено тремя юнит-тестами, которые были красными до правки и зелёными после. Найдено не чтением кода, а прогоном тестов — аудит эту находку пропустил.
- **Остаток той же проблемы зафиксирован, но не исправлен:** `RoundManager.Tick` в фазе `Scoreboard` (`RoundManager.cs:157`) тоже стартует следующий раунд своим методом, минуя счётчик `SetManager`. Сейчас ветка недостижима из-за MATCH-02, но станет живой сразу после её исправления. Требование «один владелец перехода раунд → раунд» добавлено в T-09.

### Добавлено
- **Харнесс самопроверки для агентов.** `AndroidCompileGate` (`Assets/Editor/VR_Battlegrounds/Debug/`) — ворота компиляции под Android через `PlayerBuildInterface.CompilePlayerScripts`, ловят editor-only утечки в рантайм-сборку. Проверены в обе стороны: с возвращённым `using Codice` дают `CS0246`, без него PASS. Тестовая сборка `Assets/Tests/EditMode/` + `SetManagerScoringTests` (6 тестов, ~0.6 с). В `CLAUDE.md` добавлен раздел «Самопроверка»: задача не закрыта, пока агент сам не получил зелёный результат; сначала харнесс, потом правка; тест обязан быть красным до правки.
- **Разбор проверяемости сетевого кода** ([`testing.md`](testing.md#как-тестировать-сетевую-логику)). Обнаружено экспериментально: `[Server]`-методы вне активного сервера Mirror молча заглушает, поэтому обычный юнит-тест их не проверяет — он зеленеет, ничего не выполнив. Найден рабочий рецепт поднять Mirror сервером **без сокета**: `NetworkServer.listen = false` заставляет `Listen()` пропустить `Transport.active.ServerStart()`, транспорт нужен только как объект для `AddTransportHandlers`. Отсюда три яруса сетевых тестов (сервер без сети → host-режим в процессе → два процесса с машиночитаемым вердиктом), задачи T-26 и T-27.
- **Модель делегирования задач** ([`tasks/README.md`](tasks/README.md#как-выполнять-модель-делегирования)): последовательные субагенты с чистым контекстом на общем дереве, строго по одному за раз (рабочее дерево и редактор Unity — общие синглтоны). Отдельные worktree по-прежнему отвергнуты: главная причина не в стоимости `Library/`, а в том, что Unity MCP цепляется к одному редактору, то есть worktree лишает субагента способности себя проверить.

### Изменено
- **Поправки к аудиту по результатам прогонов.** MATCH-04 в исходной формулировке не воспроизвелась: подсчёт победителя верен, тесты на 2:1 и досрочную победу проходят. Первый прогон показывал обратное, но это был артефакт теста — он создавал `TeamData` с `teamIndex` 0 и 1, тогда как в реестре команды имеют индексы 1 и 2. Реальная проблема оказалась другой и уже: `SetManager` и `EliminationMode` определяют победителя через глобальный `TeamRegistry.GetByIndex()`, а не через переданный массив команд, поэтому рассогласование индексов даёт тихую «ничью» вместо победы. T-08 переформулирована. Заявленная блокировка T-07 через T-05 тоже оказалась выдумкой: `GameSettings.Instance` в EditMode-тесте работает, `Resources.Load` в редакторе доступен.

---

## [2026-08-16]
### Добавлено
- **Аудит сетевого кода и оценка архитектуры**: проведён разбор 78 скриптов `Assets/Scripts/` и Mirror-интеграции UltimateXR. 21 находка ([`audit/network-audit-2026-08.md`](audit/network-audit-2026-08.md)), из них 4 критичных: не применённый Патч 1 в `UxrMirrorAvatar` (статические `_initialStateLoaded` и `_serverBroadcaster`), потеря вещателя state-sync при горячей замене аватара, удвоение счёта сетов из-за двойной подписки на `SetEnded`, editor-only `using Codice.*` в рантайм-скрипте. Отдельно — [оценка архитектуры](audit/architecture-review-2026-08.md): 13 из 21 находки оказались производными от пяти корневых решений (два канала репликации через объект-однодневку; несинхронизированная связь сессия ↔ аватар; фаза раунда как событие вместо состояния; машина состояний из обратных вызовов; логгер, требующий категорию от вызывающей стороны). Проверено и подтверждено: модель урона серверная — `UxrNetworkManager` с заполненным `_networkImplementation` лежит в префабе `--- MANAGERS ---` в `Offline.unity`.
- **План тестирования** ([`testing.md`](testing.md)): шесть уровней от ворот компиляции под Android до чек-листа в шлеме. Ключевое: `SetManager` и `RoundManager` — чистые C#-классы с `Tick(float deltaTime)`, поэтому логика матча юнит-тестируема; мешают три зависимости (`GameSettings.Instance` в каждом вызове `GameLog`, `PlayersManager.Instance`, `FindObjectsByType`). Тестов в проекте пока нет, сборки для них тоже.
- **Очередь работ** ([`tasks/`](tasks/README.md)): аудит нарезан на 24 самостоятельные задачи с графом блокировок и статусами. Там же зафиксировано решение выполнять их последовательно одним агентом, а не раздавать в отдельные worktree: каждый worktree требует своего `Library/` с полным реимпортом, Unity MCP цепляется к одному экземпляру редактора, и проверка в Unity всё равно упирается в одного пользователя. Параллельно отдаются только `T-23` и `T-24` — исследовательские решения про компенсацию задержки и симуляцию снарядов, производящие документ, а не код.

### Исправлено
- **Сборка под Android (Quest)**: Editor-скрипты лежали в семи папках `Assets/Scripts/**/Editor/`, накрытых `VrBattlegrounds.asmdef` с пустым `includePlatforms`. Своей editor-сборки они не образовывали (в `Library/ScriptAssemblies/` был только `VrBattlegrounds.dll`), поэтому `using UnityEditor` попадал в рантайм-сборку для всех платформ. Пять файлов при этом были без `#if UNITY_EDITOR`. Все скрипты перенесены в `Assets/Editor/VR_Battlegrounds/` (`Arsenal/`, `Gameplay/`), опустевшие папки удалены. Это то самое нарушение, от которого предостерегает `.agents/rules/editor_scripts.md`.
- **Хук `post-commit` (Plastic SCM)**: не работал с 2026-03-28. Пофайловый вариант через `git diff-tree ... | cm ci -a -c "$MSG" -` был построен на допущении, что `cm ci` читает пути из stdin — такого режима у Plastic CLI нет, и `-` уходил ему как ещё один путь. Ошибки `cm add` / `cm rm` при этом глушились в `/dev/null`, поэтому поломка была не видна. Хук переписан на один чекин всего воркспейса (`cm ci . -a --applychanged --private`), ошибки теперь выводятся. Добавлен фолбэк на путь к `cm.exe`, если его нет в `PATH`.
- **`.gitattributes`**: правило `* text=auto eol=crlf` при свежем clone превращало `.githooks/post-commit` в CRLF, ломая shebang. Добавлено исключение `.githooks/** text eol=lf` и `*.sh text eol=lf`.
- **Unity MCP не видел редактор**: все инструменты падали с `No Unity Editor instances found` при живом бридже. Рассинхрон транспортов: Unity 10.1.2 подключается к HTTP-хабу, который поднимает сам (`ws://127.0.0.1:8080/hub/plugin`, pid в `Library/MCPForUnity/RunState/`), а `.mcp.json` со `stdio` порождал второй серверный процесс, искавший редактор сканом старых TCP-портов. `.mcp.json` переведён на `{"type": "http", "url": "http://127.0.0.1:8080/mcp"}` — сессия цепляется к уже запущенному хабу, попутно включились project-scoped tools (`execute_custom_tool`). Разбор и диагностика — в `.agents/rules/unity_mcp.md`.

### Добавлено
- **Инфраструктура для ИИ-агентов**: `CLAUDE.md` как единый источник правды (автозагрузка), `.claude/settings.json` с allowlist прав и `GIT_PAGER=cat`, слэш-команды `/commit`, `/debug`, `/feature`, `/unity-check`, `/docs-sync`, хук `.claude/hooks/sdk-patch-guard.ps1` (требует фиксировать правки исходников UltimateXR в `sdk-patches.md`).

### Изменено
- **Правила агентов схлопнуты**: четыре параллельных набора (`.agentrules`, `.cursorrules` на 26 КБ, `.clinerules`, `.github/copilot-instructions.md`) разошлись между собой — в них были устаревшие версии пакетов (URP 17.0.3 против 17.4.0, xr.oculus 4.4.0 против 4.5.4, inputsystem 1.11.2 против 1.19.0) и ссылка на несуществующий путь `_agent/workflows/`. Все четыре превращены в указатели на `CLAUDE.md`. Из `.agents/rules/` удалены шесть файлов, поглощённых `CLAUDE.md` и слэш-командами.
- **Обязательное чтение отменено**: «Шаг 0» требовал 840 строк документации перед любой задачей. Заменён таблицей маршрутизации «задача → что читать» в `CLAUDE.md`.
- **`.mcp.json`**: был в формате VS Code (ключ `servers`), из-за чего Claude Code его не читал. Переведён на `mcpServers` со stdio-транспортом `mcpforunityserver`.
- **`Docs/README.md`**: в индекс добавлены пропущенные `session-architecture.md`, `level-design.md`, `Roadmap.md`, `Arsenal/`, `LegsAnimator_UI_Reference_RU.md`. Файл `Docs/AI_Navigation.md` удалён — правила поиска переехали в `CLAUDE.md`.
- **`.gitignore` — `*.slnx` и `.vscode/`**: Unity 6 генерирует solution в новом XML-формате `.slnx` рядом со старым `.sln`, а тот игнорировался с самого начала. Файл перечисляет 118 путей к `*.csproj`, которые тоже в игноре, порядок проектов в нём не отсортирован — в репозитории он давал бы шумные диффы и ссылки в никуда. `*.slnx` добавлен рядом с `*.sln`. Для `.vscode/` заведён белый список (`settings.json`, `tasks.json`, `launch.json`, `extensions.json`): конфиг «Attach to Unity» и YAML-ассоциации для `.unity`/`.prefab`/`.meta` полезны и переносимы, а локальный мусор расширений в репозиторий не попадёт.

---

## [2026-03-28]
### Добавлено
- **Документация Архитектуры Сессий**: Добавлен файл [`Docs/session-architecture.md`](session-architecture.md), подробно описывающий паттерн разделения логики `PlayerSession` и визуальных кукол аватаров.
- **Инструментарий `Avatar Maintenance` (`Assets/Editor/`)**: Создан набор редакторских утилит (`CheckSkeletons`, `CreateBaseAvatars`, `FixAvatarRenderers`, `FixHandTrackingCache`) для массовой настройки, экстракции и поправки скелетов базовых `UltimateXR` аватаров прямо в инспекторе. 
- **Модель сессий и аватаров (`Avatar Subsystem`)**: Внедрены реестры скинов (`AvatarRegistry`, `SkinRegistry`), профили девайсов клиентов (`ClientDeviceType`), логика стратегий спавна аватаров (`AvatarSpawnStrategy`, `TeamAvatarStrategy`) и обработчик потери сессий (`SessionRecoveryManager`). Сетевая логика теперь отделяет "сессию" (счет, команда) от "тела" игрока.
- **Поддержка Spectator Connect**: Реализованы раздельные сообщения при подключении к серверу для игроков (`GamePlayerConnectMessage`) и наблюдателей (`SpectatorConnectMessage`) вместо единого `PlayerJoinMessage`.

### Изменено
- **UI и HUD Интеграция**: Виджеты счета, таймеры команд, меню и `PlayerHUDManager` адаптированы к новой системе сессий. Так как `PlayerSession` теперь является локальным игроком (`isLocalPlayer=true`), HUD-составляющие куклы завязаны на событие `OnStartAuthority` от Mirror.
- **Игровые Режимы (Elimination / Respawn)**: Менеджеры раундов и игровые циклы (`GameplayManager`, `GameMode`, `RoundManager`) переписаны для взаимодействия со стойкими `PlayerSession`. Очки и статистика сохраняются при уничтожении куклы скина.
- **Plastic SCM Хуки (`.githooks/post-commit`)**: Хук для дублирования коммитов в `cm ci` переведен с монолитного `cm ci -a` на пофайловое сопоставление дерева через `git diff-tree`. Это позволяет изолированно коммитить и пушить в Plastic только выбранные stage файлы (Interactive commits).

---

## [2026-03-23]
### Добавлено
- **Модульный Level Design**: Внедрена система блокаутов `LD_Alphabet`, содержащая 10 стандартных префабов (укрытия, змейки, доритос, цилиндры, инкапсуляторы столбов).
- **Материал для Greyboxing**: Создан PBR-совместимый материал `PrototypeGrid_URP` с UV-разметкой для четкого понимания масштабов при тестировании карт.
- **Утилиты генерации**: Написаны Unity Editor скрипты `CreateDoritoMesh.cs` (процедурная генерация пирамид) и `BuildVegasMap.cs` (для автоматической сборки асимметричных турнирных схем NXL в Unity).

### Изменено
- **Иерархия Сцен (Map Hierarchy)**: Карты реструктурированы для четкого разделения физических характеристик зала и игровой геометрии. Внедрена связка шаблона `Arena_Core` (где собраны пол, спавн-зоны и колонны) и префабов `Map_Layout`.
- **Документация**: В `gameplay.md` прописаны обязательные правила инкапсуляции бетонных колонн 15x18 арены и концепт Art Pass для естественной интеграции киберспортивных макетов в реалистичное окружение.

---

## [2026-03-22]
### Добавлено
- **Сохранение позиции между сценами**: Реализован перенос абсолютных координат аватара при загрузке новых сцен. `PhysicalSpaceSyncManager` сохраняет координаты по событию `UxrAvatar.GlobalAvatarMoved` и передает их на сервер через расширенное сообщение `PlayerJoinMessage` (бывшее `RoleJoinMessage`), чтобы сервер сразу спавнил аватара в нужной точке.
- **Калибровка роста в VR**: Создана система подгонки виртуального роста `PhysicalSpaceSyncManager` и соответствующие экраны настраиваемого UI (MenuPhysicalSpaceSync, HUD). Наложен патч на ядро трекеров `UxrControllerTracking`, позволяющий глобально сдвигать руки по высоте (`GlobalHeightOffset`), чтобы телепортация не сбрасывала выставленное смещение.
- **MVC Архитектура Меню**: Внедрен переработанный UI-стек. Созданы `MenuController` (глобальный контроллер), `LocalMenuManager` (управление инстансами по контексту сцены) и `MenuScreen` (базовый класс для экранов).
- **Иерархический Реестр Префабов Меню**: `MenuPrefabRegistry` теперь использует древовидную структуру (Role -> Scene Context -> Game Mode), избавляя от жесткого кодирования имен сцен.
- **Компонент Session Setup**: Добавлен `MenuSessionSetup` с поддержкой красивой сетки (Grid) и визуальных вкладок (Tabs) для выбора игровых режимов и карт.
- **Инструментарий Editor**: Добавлены скрипты для генерации/запекания красивых меню-префабов.

### Изменено
- **Чистка старого UI**: Удалены устаревшие `AdminMenuController` и `PlayerMenuController`.
- **Логика вращения планшета**: Физика `TabletMenuBase` стабилизирована: удалён проблемный скрипт `SpringOnRelease`, добавлен Kinematic Rigidbody, а иерархия префаба оптимизирована для ровного спавна перед игроком.

---

## [2026-03-21]
### Добавлено
- **Архитектура Ролей (Role Architecture)**: Логика идентификации платформы и прав в сети выделена в модуль ядра `AppRoleManager`. Введены роли устройства (`DeviceRole` — VR, PC, Server) и сетевые роли (`NetworkRole` — Host, Client, DedicatedServer).
- **Поддержка режимов по картам**: В `MapData` добавлено поле `supportedModes`, позволяющее привязывать конкретные игровые режимы к картам.
- **Интеграция UI библиотек**: Импортированы `SlimUI` и `TextMeshPro`. Добавлена новая категория `LogLevelUI` в настройки логирования `GameSettings`.
- **Magazine Pocket**: Внедрена механика карманов для магазинов. Добавлен `ColliderVisualizer` для удобной настройки коллайдеров. Оформлен `Docs/Roadmap.md`.

### Изменено
- **Глобальные Менеджеры**: Введен `PersistentRoot` для управления глобальными объектами. `SessionManager` (бывший `GameManager`) отделён от сетевого транспорта и переведён в формат спавнящегося префаба для упрощения загрузки оффлайн сцен и тестирования.

---

## [2026-03-19]
### Изменено
- **Документация и ИИ**: Внедрена система M.A.P. (быстрая навигация для ИИ-агентов). Создан файл `AI_Navigation.md`. Обновлены `README.md` и `.agentrules` для устранения устаревших PowerShell скриптов, предотвращения раздувания базы знаний и уточнения политик. Устаревшие правила чтения перенесены в `.github/copilot-instructions.md`.

---

## [2026-03-15]
### Добавлено
- **Авто-рюкзак (Back Slot)**: Внедрена MVP-система невидимого инвентарного слота на спине (`Anchor_Back`) для ношения крупного оружия.
- **Grab Proxy (Перехват хвата)**: Нативная поддержка редиректа в UltimateXR. При попытке схватить невидимую зону за плечом, игрок моментально достает оружие из слота на спине.
- **Нативный Swap (Быстрая замена)**: При попытке вставить предмет в занятый слот инвентаря, старый предмет автоматически выталкивается, освобождая место новому.
- Утилита `RefineBackPocket` для настройки позиционирования слотов через Inspector.

### Исправлено
- **UxrReturnGrabbableObject**: Безопасная отвязка компонента возврата (`ClearLastAnchor`) при срабатывании механизма Swap.
- **Грип-позы (Grip Poses)**: Обновлены и исправлены позы хвата для аватара CyborgAvatar и добавлены/перемещены Demo-хваты.
- Отключен спам (verbose logs) в `UxrGlobalSettings` и `GameSettings`.

---

## [2026-03-09]
### Добавлено
- **Готовность к раунду**: Новое состояние `RoundState.WaitingForPlayers` в `RoundManager`. Раунд не начнется, пока все живые игроки не зайдут в свои `TeamSpawnZone`.
- **Архитектура GameMode**: Метод `CanStartGameplay()` для настройки условий старта (например, наличие игроков в обеих командах для `EliminationMode`).
- **EliminationModeEditor**: Кастомный инспектор для режима "Ликвидация" с отображением таймеров и счета в реальном времени.
- **Стабильность триггеров**: Автоматическое добавление `Rigidbody` в `TeamSpawnZone` для надежной детекции VR-аватаров.

### Изменено
- **Рефакторинг менеджеров**: `MatchManager` переименован в `GameplayManager` для подготовки к PvE режимам.
- **HUD**: Обновлен `HUDWidget_GameNotification` для отображения статуса ожидания игроков.
- **Ограничение оружия (Refactored)**: Введен глобальный флаг `WeaponSystemEnabled` и хук `GlobalUsageCheck` в `UxrWeaponManager`. Теперь `GameplayManager` централизованно управляет всей системой боя, отключая расчёты пуль и использование оружия вне активных фаз раунда.
- **События смерти**: Добавлено событие `Died` в `UxrActor` и `PlayerDied` в `PlayerController` для уведомления систем игры о гибели игрока в контексте UltimateXR.
- **Документация**: Обновлены `README.md`, `gameplay.md`, `game-manager.md`, обновлен `walkthrough.md`.

---

## [2026-03-09] - Предшествующие изменения
- `12de155` feat: implement pre-round waiting state and game start readiness logic
- `3a406b4` Refactor: Implement Pure Semantic Events for HUD notifications

## [2026-03-08]
- `c323b19` chore: update scenes, prefabs and settings after manager rename refactoring
- `ec2680b` refactor: rename GameManager -> SessionManager, MatchManager -> GameplayManager
- `d2a82af` feat: add Player HUD system with widgets and prefab generation tools
- `6c34595` refactor: add TeamRuntimeData and support N teams in EliminationMode
- `2e6d29f` fix: use MaterialPropertyBlock in TeamSpawnZone to avoid prefab material errors
- `0a30497` chore: track missing folder meta files
- `a731cc3` feat: add PlayModeStartFromOffline editor tool
- `0e1b8dd` docs: add summary for UxrAvatar and UxrManager modules
- `d6d180e` docs: update agent rules to require user validation before commit
- `25f375b` feat: teleport player to team spawn zone on connect
- `20ba947` feat: setup spawn zones on map and create SpawnZone editor tool
- `66c9d26` feat: TeamSpawnZone inherits color from TeamData
- `4ae57f8` docs: document TeamSpawnZone and generalize AI instructions
- `cb7dc09` feat: add TeamSpawnZone component for team area tracking

## [2026-03-07]
- `5633c68` refactor: move EliminationMode scripts to dedicated folder
- `84f4b6e` docs: add unified .agentrules
- `4f1425f` GameModes + Orchestrator
- `7acb4d2` chore: обновление copilot-instructions и UltimateXR context
- `ea665c3` docs: реструктуризация документации игры
- `729ec50` feat: реализация команд и менеджеров матча
- `59bd59c` chore(editorconfig): add dotnet style rules
- `8b26c3c` feat(maps): add TestMap2 scene and data
- `250334b` refactor(network,debug): integrate GameLog logging
- `afd2791` feat(managers): add MapManager
- `8d690c1` feat(core): add logging system
- `b8ddc63` refactor(copilot): consolidate encoding rules
- `828a2c5` fix(docs): replace remaining garbled '?' symbols
- `b287f07` docs(copilot): add VS UTF-8 setup instructions
