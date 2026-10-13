# Play Launch и адресный стенд Play Mode

Окно `Tools/VR Battlegrounds/Debug/Play Launch` и MCP используют один planner и замороженный
request. `PlayModeTestStand` запускает процессы через MPPM/MPE и доставляет IPC-команды.
Сеть ведёт `GameNetworkDiscovery`, карты — штатные MapLoader/startup route; Mirror владеет
offline/online сценами. Контракт `play-launch-control@1` опубликован вместе с кодом `51c8c047`.

## Профиль и обычный Play

Профиль хранится отдельно для каждого checkout: `UserSettings/VrBattlegrounds/play-launch.json`,
schemaVersion 1. Папка игнорируется Git. Во время request конфигурация заморожена, постоянный
профиль нельзя перезаписать. Legacy EditorPrefs импортируются только явным действием пользователя.

Источники сцен: `active`, `lobby`, `scene` с `ScenePath`. Карта с MapRoot запускается через Offline
и согласованный серверный startup route; standalone с StandaloneSceneMarker — без сети.
Неразмеченная сцена требует явного `AllowUnmarked`. Planner проверяет каталог, Build Settings,
совместимость режима и topology до запуска.

Для обычного toolbar Play с явным источником выбрать одиночный native scenario с ролью,
совпадающей с профилем, и `ClientCount=0`. Многопроцессный профиль запускать через Play Launch.
Несовпадение роли или дополнительные экземпляры дают отказ до запуска. Native request имеет
Owner=`native-play`; его `Status().Ready` не рассчитывается по StandManifest. Фактическую
готовность проверять по Mirror, целевой сцене, MapRunAdmission и валидному MapBootstrap.LocalRunKey.

### Отказ PreviewSceneNetworkObjects

Для сетевой сцены (`SceneKind=Game`) planner отказывает с `Code="PreviewSceneNetworkObjects"`, если
в редакторе есть preview-сцены с NetworkIdentity: содержимое после `PrefabUtility.LoadPrefabContents`
без `UnloadPrefabContents` или открытый Prefab Mode. Mirror `NetworkScenePostProcess` берёт объекты
через `FindObjectsOfTypeAll`, выдаёт им sceneId, сервер спавнит их как scene objects, а у клиентов
их нет: «Spawn scene object not found». `Message` перечисляет до 20 источников (путь префаба или имя
сцены → путь объекта в иерархии, `(+N)` — ещё объекты того же префаба) и общее число.
Что делать: закрыть Prefab Mode, выгрузить содержимое префабов (агент — `PrefabUtility.UnloadPrefabContents`
для каждого загруженного корня) или перезапустить редактор, затем снова вызвать `Plan`.
Проверка срабатывает в `Plan`/`Request`/`Play` и в toolbar Play с явным источником (`lobby`/`scene`);
`Play` после ранее принятого `Request` проверяет ещё раз и при отказе освобождает request.
Toolbar Play с `SceneSource="active"` идёт прежним путём без planner и этой проверки не проходит.

## Локальные сетевые порты

Политика задаётся в том же локальном профиле или разовом JSON запуска:

| NetworkPortPolicy | Поведение |
| --- | --- |
| `default` | Обычный Editor сохраняет порты компонентов приложения; owned managed stand сохраняет автоматическую изоляцию своего сервера. |
| `fixed` | Одна указанная пара NetworkPort/DiscoveryPort для всех участников. Нужны разные числа1..65535. |
| `auto` | Координатор выбирает свободную пару и замораживает её для запуска. Managed topology должна содержать собственный сервер. |

Например, для отдельного checkout:

```json
{
  "NetworkPortPolicy": "fixed",
  "NetworkPort": 29771,
  "DiscoveryPort": 29772
}
```

Для worker рекомендуется `{"NetworkPortPolicy":"auto"}`, для основного Editor — `default`.
Обе настройки остаются вне Git в UserSettings каждого checkout; профиль clone читается из
исходного checkout. Если два запуска используют Fixed, их пары должны различаться целиком:
одного отдельного игрового порта недостаточно при общем discovery порте.

`VRBG_LAUNCH_CONFIG` выбирает другой файл **того же JSON формата**. Переменную задать перед
запуском Editor; relative путь разрешается от исходного checkout. Например:

```powershell
$env:VRBG_LAUNCH_CONFIG = 'UserSettings/VrBattlegrounds/play-launch.worker.json'
```

Внутри checkout разрешены только файлы под игнорируемым UserSettings; абсолютный внешний файл
также допустим. Указанный, но отсутствующий файл даёт ProfileMissing, битый — ProfileInvalid.
Без переменной используется прежний play-launch.json; чтение отсутствующего стандартного
профиля не создаёт файл. Окно показывает выбранный путь и позволяет явно сохранить политику.

Порты применяет один Editor-владелец перед штатным стартом любой роли и reconnect; сцены и
префабы не сохраняются. Native Auto требует Play главного Editor, который публикует пару для
clones; для native сценария без главного Play использовать Fixed/default или Play Launch.
Занятый порт даёт PortOccupied и отказ запуска; ошибка принадлежит конкретному RunId.
Проверка свободного сокета не резервирует его: реальный transport bind тоже может отказать.
Фактические NetworkPort/DiscoveryPort доступны в Participants адресного Status.
Эта конфигурация относится к Editor/MPP; release-приложение сохраняет свои штатные порты.
Собранные e2e-плееры получают пару от дирижёра `Tools/e2e/Run-E2E.ps1` (`-e2eNetworkPort`/`-e2eDiscoveryPort`) —
[testing.md](testing.md) «Изоляция прогона»; это отдельный писатель только в плеере, в редакторе не действует.

## Порядок работы агента

В linked worktree: fetch/rebase → checkpoint → request/watch-ticket → claim/begin → guard,
затем MCP из своего worktree. Перед операциями проверить фактический worker root, idle,
отсутствие чужого request/Play, dirty scenes и prefab stage. Допуск этапа хаба не заменяет lease.
Полный JSON и логи сохранять в ignored `tasks/<task-id>/reports/`; выводить компактный итог.

### Как агенту включить Play (через Offline)

Агент включает Play **только** через `PlayLaunch.Plan` → `PlayLaunch.Play` → `PlayLaunch.Status`. Не использовать
`EditorApplication.isPlaying = true`, кнопку Play, `EditorSceneManager.playModeStartScene` и галочку
«Start from Offline Scene»: это обычный Play по личному профилю checkout, у него нет готовности, портов и уборки.
При профиле `SceneSource="active"` (по умолчанию) обычный Play в Untitled или в сцене без `MapRoot` стартует
саму эту сцену, а не Offline → Lobby.

PlayLaunch сам выбирает стартовую сцену и возвращает прежнюю после Play:

| Цель | Конфигурация | Старт |
|---|---|---|
| Лобби | `SceneSource="lobby"` (или `"active"` при активной Untitled/Offline) | Offline → Lobby |
| Карта, один процесс | `SceneSource="scene"`, `ScenePath` карты, `Role="host"`, `ClientCount=0`, `ModeId` | Offline → маршрут старта → карта, без Lobby |
| Карта, сервер + клиенты | `Role="server"`, `ClientCount=2` (пример ниже) | каждый процесс через Offline, общая пара портов |
| Сцена стенда (`StandaloneSceneMarker`) | `SceneSource="scene"`, путь стенда | сама сцена, без сети |

Порядок:
1. `Plan(json)` — Play не запускает. Проверить `Passed=true`, `SceneKind` (`Game` для сетевой сцены) и
   `StartScenePath="Assets/Scenes/Offline.unity"`, `TargetScenePath` — нужная сцена. Иначе не запускать: разобрать `Code`.
2. `Play(json, "<свой-owner>")` — тот же JSON, один раз. Повтор после таймаута MCP — только после `Status()`.
3. `Status()` до `State="Ready"`/`Ready=true` (для `map-playable` — карта собрана и допущена). `Failed` — читать `Error`.
4. Выход: `Cancel(runId, owner)` или `EditorApplication.isPlaying = false`; не `EditorApplication.Exit`.

Однопроцессный запуск карты:

```csharp
var config = new VrBattlegrounds.DevTools.PlayLaunchConfiguration {
    Enabled = true, Role = "host", ClientCount = 0, HostIsAdmin = true,
    SceneSource = "scene", ScenePath = "Assets/Scenes/Maps/TestMap1.unity",
    ModeId = "elimination", AutoGoLive = false, BotCount = 0,
    PauseOnFocusLoss = false, Readiness = "map-playable"
};
return VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Plan(UnityEngine.JsonUtility.ToJson(config));
```

Многопроцессный план (не запускает Play):

```csharp
var config = new VrBattlegrounds.DevTools.PlayLaunchConfiguration {
    Enabled = true, Role = "server", ClientCount = 2, HostIsAdmin = false,
    SceneSource = "scene", ScenePath = "Assets/Scenes/Maps/TestMap2.unity",
    ModeId = "elimination", AutoGoLive = false, BotCount = 0,
    PauseOnFocusLoss = false, Readiness = "map-playable"
};
return VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Plan(UnityEngine.JsonUtility.ToJson(config));
```

Проверить `Passed`, `Code`, resolved scene/topology. Затем однократно передать **тот же JSON**
в `PlayLaunch.Play(json, "<свой-owner>")`. Методы facade возвращают JSON-строки.
`Role=server, ClientCount=2` означает Server+2Client; для Client+Host — Role=client,
ClientCount=1, AdditionalPlayerRoles=new[] { "host" }. Роль `ask` не разрешена автоматическому запуску.
`StartProbe()` прежнего маркерного пилота сохранён; новые потребители используют общий facade.

```csharp
return VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status();
```

Managed `Ready=true` требует свежих heartbeat, фактических ролей/соединений, ожидаемой сцены,
реального допуска и ключа карты. Один локально созданный сервер и его клиенты получают общую
свободную пару UDP network/discovery портов. Успех MCP-вызова не означает Ready или прохождение проверки.

## Адресные команды и reconnect

Из свежего Status выбрать участника по RunId + ParticipantId + ProcessSessionId. PID или одна
роль адресом не являются. Отдельный MCP-сервер каждого clone не требуется: команды доставляет MPE IPC.

```csharp
var request = new VrBattlegrounds.EditorTools.TestStand.StandRequest {
    RunId = "<текущий-run>", ParticipantId = "<цель>", ProcessSessionId = "<текущая-сессия>",
    RequestId = "marker-1", Action = "create-marker", MarkerName = "probe", TimeBudgetMs = 5000,
    ExpectedConnectionEpoch = 1, ExpectedAvatarNetId = 0
};
return VrBattlegrounds.EditorTools.TestStand.PlayModeTestStand.Send(request);
```

Epoch и avatar netId брать из текущего участника, числа выше — placeholders. Действия:
`create-marker`, `read-marker`, `remove-marker`, `state`, `disconnect`, `reconnect`, `run-e2e`.
Маркер локальный, несетевой и без физики. Для сетевых команд MarkerName пустой, актуальный
ExpectedConnectionEpoch обязателен; ненулевой ExpectedAvatarNetId дополнительно защищает цель.

Disconnect адресуется отдельному Client-only, а не Host. Сервер и наблюдающий клиент должны
оставаться живыми. После reconnect заново получить epoch/avatar и полный адрес; проверить admission,
новую identity, восстановленное состояние и число сессий. Старый epoch/avatar должен быть отклонён.
`run-e2e` задаёт Scenario/ScenarioTimeoutSeconds и использует уже запущенную сеть. Для готового
сценария восстановления использовать пакет managed_e2e_worker_probe ниже.

`Send` возвращает operation; пока `Completed=false`, читать `Operation(RequestId)`. После завершения
проверить Reply.Passed, полный адрес/PID исполнителя и EffectUnknown. Тот же RequestId с прежним
payload возвращает сохранённый результат; новый payload под прежним ID отклоняется.
EffectUnknown, timeout или executionCompleted=true не разрешают повтор мутации ради вывода.
Сначала прочитать durable/native состояние. Domain reload меняет ProcessSessionId.

## Завершение и звук

```csharp
return VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Cancel("<свой-run>", "<свой-owner>");
```

Дождаться State=`Idle`, Playing=false, пустого Owner и CleanupPassed=true для managed run.
Native cleanup проверять напрямую: Editor idle, request освобождён, сеть выключена; сохранённый
backend CleanupPassed не является его собственной квитанцией. Проверить восстановление профиля,
токена, native scenario и стартовой сцены. После конечного пакета сразу finish/receive,
**до** диагностики, правок или ожидания пользователя; активную операцию не прерывать.

### Архив логов участников

Каждый участник managed run (главный редактор — `main`, клоны — `Player N`) при входе в прогон
запоминает длину своего `Application.consoleLogPath`, а при Release/Leave копирует хвост лога от
этой отметки в `<ProjectRoot>/Logs/VRBattlegroundsTestStand/<RunId>/<participant>-<pid>.log`.
ProjectRoot — исходный checkout: клон MPPM пишет туда же, а не в свою `Library/VP` копию. Если прошлый
прогон завершился без Release (например, reload в момент остановки), хвост сохраняется при следующем
входе в новый прогон. Ошибка копирования не мешает Play — только предупреждение `[TestStand]` в логе.
`Logs/` игнорируется Git. `PlayLaunch.Status()` возвращает `LogArchiveDirectory` и `LogArchives`
(готовые файлы); список заполняется после выхода из running, во время прогона он пуст.

Ограничение: архив `main` верен, только если у главного редактора свой лог. Worker-редактор агентов
сейчас пишет в общий `%LOCALAPPDATA%/Unity/Editor/Editor.log` вместе с основным редактором. Длина
такого файла не совпадает с позицией записи worker, поэтому архив `main` там неполный. Архивы клонов
верны: у каждого клона свой лог в `Library/VP/<клон>/Logs/Editor.log`.

### Descriptor прогона и параллельный доступ

`Temp/VRBattlegroundsTestStand/active.json` пишет только координатор (`main`); клоны читают его каждый
кадр. Читатель открывает файл с общим доступом на чтение, запись и удаление, поэтому не мешает
координатору заменить или удалить файл. Если чтение всё же кратко не удалось, участник получает
последнее прочитанное значение, а не «прогона нет» — иначе он вышел бы из идущего прогона.
«Прогона нет» означает: файла нет, он битый или процесс координатора завершился. Запись и удаление
повторяются при занятом файле. Если запись так и не прошла, остановка не взводится: следующий кадр
повторяет и запись, и `Stop` (`[TestStand] Остановка отложена` в логе). Раньше в этом случае прогон
навсегда оставался в `Stopping` с включённым Play.

ServerOnly беззвучен. Host/client используют штатный звук; на Offline/reconnect fallback listener
уступает фактически активному listener локального аватара. Временная ServerOnly audio pause
восстанавливается при выходе. Стенд не пишет EditorPrefs/PlayerPrefs для временных профилей/токенов.
Descriptor и IPC-маркеры удаляются своим владельцем; общий IPC-сервис не закрывается.

## Готовые проверки

```powershell
dotnet run --project Tools/TestStand/ProtocolHarness.csproj
dotnet build Tools/TestStand/EditorBinding.csproj
# Только в своей RUNNING-аренде после guard:
uv run --with 'mcp>=1.20,<2' tasks/vr-test-stand/tools/launch_worker_probe.py --instance '<worker>' --editor-root '<worker-root>' --output tasks/vr-test-stand/reports/launch.json
uv run --with 'mcp>=1.20,<2' tasks/vr-test-stand/tools/managed_e2e_worker_probe.py --instance '<worker>' --editor-root '<worker-root>' --output tasks/vr-test-stand/reports/recovery.json
uv run --with 'mcp>=1.20,<2' tasks/vr-test-stand/tools/native_bot_worker_probe.py --instance '<worker>' --editor-root '<worker-root>' --output tasks/vr-test-stand/reports/native-bot.json
```

Launch probe поддерживает --case (server-two-clients, client-host, game-server, disabled-game-server,
standalone), --compile-only, --network-policy и Fixed --network-port/--discovery-port.
Для отрицательной пробы PortOccupied: --case game-server --network-policy fixed с парой портов,
--occupy-game-port --expect-error PortOccupied. --observation-timeout10..180 (default45) меняет
только бюджет разрешённых наблюдений. Native probe с --native-only пропускает BotT01 и
поддерживает те же параметры политики; точный baseline и полный capture остаются обязательными.
Helper может ограниченно повторять только чистые `PlayLaunch.Status()` и адресный
`PlayModeTestStand.Operation(requestId)`: при пустом отказе или строго распознанном JSON-отказе
«Unity plugin session … disconnected while awaiting command_result» с `hint=retry`.
Каждая попытка сохраняется в отчёте. Запуски, сетевые команды, неизвестные ошибки,
transport exceptions и timeout не повторяются; после обрыва мутации сначала проверить её
durable состояние. Отрицательный тест порта должен доказать новый runtime отказ и затем
успешный запуск после освобождения порта; одной ошибки в Status недостаточно.
Recovery probe запускает **сервер и одного клиента**, как требует
существующий сценарий SessionRecoveryOnReconnectScenario. Spectator/двухклиентная изоляция — отдельная
launch проверка. Все пакеты сохраняют логи actual PID до, во время и после собственного запуска,
отдельно учитывая старые baseline bytes, ротацию и полноту capture. Логи не очищать; исключать только
оговорённые lighting assets, остальные ошибки сохранять в реестре и передавать владельцу.

ProtocolHarness выполняет production protocol/profile/config код с заглушками Unity: 32/32 на
2026-10-09. EditorBinding — компиляция привязок к Unity DLL, не доказательство сети. Runtime пакеты:
worker358 launch56/56; worker364 Android и recovery server10/10/client5/5; worker365 native/Bot33/33,
cleanup/capture PASS. BotT01 завершён с NeedsReview/Passed=null; запуск и capture доказаны,
выстрелы не проверены. Подробные результаты и raw evidence — [задача](../tasks/vr-test-stand/Readme.md).

## Границы текущей итерации

SelectedEditMode365 не стартовали до init timeout;366 потерял WebSocket до job receipt. Эти проверки
не объявляются зелёными; диагностика передана сопровождающему (infra2350/2352/2359).
Не повторять run_tests при неизвестном исходе; terminal MCP job сам по себе не доказывает остановку
underlying Unity TestRunner. Перед новым запросом проверить native readiness.

Следующие этапы: HardwareXR startup policy и реальные OVR/UPM/licensing ошибки окружения;
единый XR-input adapter для поз/кнопок/UI/хвата/ходьбы; packet faults и смерть координатора/сервера.
Профиль локален и вне Git. Не добавлять второй writer поверх UltimateXR.
Полный test suite, физический toolbar click, качество хвата/stereo/Quest текущими отчётами не доказаны.
