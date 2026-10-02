# T-46 · HUD → наручные часы + нотификации

> Верификация 2026-10-02: соответствующие проверки экономики, ботов, экранов и часов выполнены
> в Unity (общий целевой запуск 168/168 PASS); AndroidCompileGate PASS. Полный прогон после
> контрактов окружения: 1683/1762 PASS, 79 отказов других групп перечислены в
> [отчёте](verification-2026-10-02.md). Визуальная проверка и два физических клиента этим не заменяются.

| | |
|---|---|
| Находка | — (требование пользователя, 2026-10-01, шаг 4 серии «оружие → баланс → экономика → часы → табло зоны») |
| Блокирована | T-45 (деньги на часах — `MatchEconomy`) |
| Уровень проверки | 1 (EditMode) + шлем |
| Оценка | 1 день |

## Требование

> убираем худ в текущем виде — переносим его на наручные часы. часы всегда должны отображать актуальную информацию
> по текущей ситуации — в игре уже отображают. эта информация прерывается только нотификациями от менеджеров игры,
> нотификация — звук + дрожание контроллера с часами.

Крупный обратный отсчёт и «готовность раунда» — на табло в лазерной сетке зоны (следующая задача); на часах компактно.

## Что было (аудит 2026-10-01)

- **HUD перед глазами**: `PlayerHUDManager` (NetworkBehaviour на корне `PlayerBase` и `PlayerControllersCyborgAvatar`)
  спавнил `GameModeData.hudPrefab` активного режима в World Space-канвас `HUDContainer` перед камерой. У Elimination —
  `EliminationHUD.prefab`: `HUDWidget_Health` (ХП полосой), `HUDWidget_RoundTimer` (остаток фазы), `HUDWidget_MapTimer`
  (время с начала карты), `HUDWidget_EliminationMode_TeamRoundScore` (счёт раундов), `HUDWidget_GameNotification`
  (сообщения со звуком: фазы, итог раунда и карты, смерти, смена сторон, напоминания), `HUDWidget_PhysicalSpaceSync`
  (подсказки калибровки — копия). У Respawn и разминки HUD не было. Optimized MEF получал его наследованием
  (`PlayerBase` → `PlayerBase_NonSdkHands` → `MEF_Base_Avatar` → `Optimized_MEF_Player`). `PlayerHUDManager.SendToPlayer` / `TargetShowNotification`
  — не вызывались нигде. `HUDWidget_TeamScore` — ни в одном префабе.
- **Часы** (`WristDisplay` в `WristWatch_HUD` на левом запястье Optimized MEF): кольцо ХП и остаток фазы.

## Что стало

| Было в HUD | Где теперь |
|---|---|
| ХП (`HUDWidget_Health`) | часы: кольцо по периметру + число |
| Остаток фазы (`HUDWidget_RoundTimer`) | часы: центр, цвет по фазе (`RoundClock`) |
| Счёт раундов (`…TeamRoundScore`) | часы: «свои : чужие» (`WatchScore`, и в Respawn — фраги) |
| Время с начала карты (`HUDWidget_MapTimer`) | убрано: не игровая информация, остаток фазы полезнее |
| Сообщения (`HUDWidget_GameNotification`) | нотификации часов (`WatchNotifications`) |
| Подсказки калибровки (`HUDWidget_PhysicalSpaceSync`) | остались на `--- MANAGERS ---` (не HUD матча, вне задачи) |
| — | **новое**: деньги экономики на часах, нотификации о деньгах, пауза, «мало времени», респаун |

Удалены: `PlayerHUDManager`, `HUDWidget_GameNotification/Health/MapTimer/RoundTimer/TeamScore/EliminationMode_TeamRoundScore`,
`EliminationHUD.prefab`, поле `GameModeData.hudPrefab`, меню `Inject HUD to Selected Avatar` (`HUDInjector`), объекты
`HUDContainer` в `PlayerBase`, `PlayerControllersCyborgAvatar`, `Heavy_Soldier_Base_Avatar` (и висящий override там же),
запись `PlayerHUDManager.TargetShowNotification` в `RpcCarriesNoStateTests`. `HudNotificationTexts` → `WatchNotificationTexts`.

## Архитектура нотификаций

```
события режимов / MapReferee / MatchEconomy (локальные)          любой код этой машины
        │ + опрос SyncVar (пауза, остаток, жизнь, зона)                  │
        ▼                                                                ▼
WatchGameEvents ──(WatchNotificationTexts: текст, важность, звук)──► WatchNotifications.Post(WatchNotification)
                                                                         │ очередь (WatchNotificationQueue)
WatchNotificationRunner (DontDestroyOnLoad, сам) ── Tick раз в кадр ─────┤
                                                                         ▼ Started / IsShowing / Current
                                              WristDisplay (часы своего аватара): статус ↔ нотификация,
                                              звук с запястья, вибрация руки, ближайшей к часам
```

**API для других менеджеров** (например, табло лазерной сетки):

```csharp
WatchNotifications.Post(new WatchNotification("Все в зоне — отсчёт!", WatchPriority.Normal, WatchSound.Beep,
                                              duration: 2f, key: "zone"));
// или короче:
WatchNotifications.Post("Все в зоне — отсчёт!", WatchPriority.Normal);
```

- `WatchPriority`: `Low` (фон) < `Normal` (ход раунда) < `High` (перелом) < `Critical` (своя смерть, итог карты).
  Важнее показанной — прерывает сразу; от важности — сила вибрации (0,25 / 0,45 / 0,7 / 1) и цвет текста.
- `WatchSound`: `Beep`, `Alert`, `Money`. Звук и вибрация есть у каждой нотификации — это требование.
- `key`: повторяющееся сообщение (напоминание, статус зоны) — новая заменяет ожидающую с тем же ключом, такая же
  на экране не повторяется.
- **Только локально, только на клиенте с игроком.** Сеть не добавлять: поднимать на машине, до которой событие уже
  доехало (SyncVar-хук, ClientRpc). Для «нужно знать позднему клиенту» — состояние (SyncVar), а не нотификация.
- Тексты и решения «что важно» держать в чистом классе рядом (как `WatchNotificationTexts`) — под юнит-тест.

Правила очереди (`WatchNotificationQueue`, тест `WatchNotificationQueueTests`): свободно — показ на ближайшем кадре;
ожидающие — по важности, внутри по порядку; при ожидающих показанная держится 1,5 с вместо своего срока;
больше 4 ожидающих — выбрасывается самая неважная; ожидающая старше 8 с выбрасывается.

## Решения (агент, без пользователя)

- **Очередь не на аватаре.** Часы — часть аватара и пропадают при смене (смерть → призрак, смена скина), а «вы погибли»
  случается ровно в этот момент. Поэтому очередь — статическая (`WatchNotifications`), переводчик — у хоста
  `WatchNotificationRunner`, который ставит себя сам (как `PerformanceLevelInstaller`); часы только рисуют.
- **Рука вибрации** — ближайшая к часам кисть аватара, по расстоянию (часы MEF висят на предплечье, не на кисти).
- **Звук с запястья** (3D, без затухания в пределах метра), а не 2D, как у старого HUD: в VR слышно, откуда.
- **Время с начала карты убрано** — на часах нет места, остаток фазы полезнее.
- **«Раунд N» отдельной нотификацией не поднимается** — фаза «Подготовка раунда» звучит в тот же момент.
- **Первая фаза в режиме — без нотификации** (как было у HUD без звука): поздний клиент видит её статусом.
- **Цвета** — сериализованы на часах значениями `MenuTheme` (Accent, Success): часы не планшет, тему не тянут.
- **Касса** — `CASH_REGISTER_Cha-ching_05_mono` из библиотеки (0,7 с), остальное — прежние `Hud_Beep` / `Hud_Alert`.
- **Покупка — фоновая нотификация**: игрок сделал её сам, она не перебивает ход раунда.
- **Пороги «мало времени»** — 30 и 10 с, только в бою Elimination и в Respawn (закупку и отсчёт показывает табло).

## Как проверить

- EditMode: `WatchNotificationQueueTests`, `WatchNotificationTextsTests`, `OldHudRemovedTests`, `WristDisplayTests`,
  `RegisteredAvatarBodyTests` (`На_руке_часы_с_табло`, `На_часах_деньги_и_нотификации`),
  `PrefabCompositionTests.На_аватарах_нет_старого_HUD`, `RpcCarriesNoStateTests`.
- Редактор: открыть `WristDisplay_Oval` — статус (время в центре, «2 : 1» сверху, ХП слева и «$800» справа внизу) не
  налезает на кольцо; включить `Content/Notification` — текст помещается в овал.
- Шлем: в матче на левом запястье деньги и счёт; конец раунда — «Раунд выигран!» затем «+$3250», каждая с пиком/кассой
  и вибрацией **левого** контроллера; своя смерть — тревога; пауза админом — «Пауза».

## Что не проверено (2026-10-01)

Редактор Unity весь сеанс стоял на модальном окне «Scene(s) Have Been Modified» — MCP не отвечал (`ping not answered`),
снять окно агенту нельзя. Поэтому:

- **Проверено вне Unity:** компиляция `VrBattlegrounds` (редактор и Android-плеер `PLATFORM_ANDROID` — тот же `csc` и
  `rsp` из `Library/Bee`), `VrBattlegrounds.Tests.EditMode`, `Assembly-CSharp-Editor`, `Assembly-CSharp`,
  `VrBattlegrounds.DebugBootstrap`, `VrBattlegrounds.LevelDesign.Editor`. Чистые тесты тем же NUnit вне Unity:
  `WatchNotificationQueueTests` (13), `WatchNotificationTextsTests` (28), `OldHudRemovedTests` (2), чистые кейсы
  `WristDisplayTests` — зелёные; до правки на заглушках — 34 красных, `OldHudRemovedTests` — красный по
  `PlayerBase`/`Cyborg`/`Heavy`. YAML префабов проверен скриптом: все `fileID` внутри файлов разрешаются.
- **Не проверено:** тесты, которым нужен Unity (`WristDisplayTests.Prefab_IsWiredAndWorldSpace`,
  `RegisteredAvatarBodyTests`, `PrefabCompositionTests`), полный EditMode-прогон, импорт правленных YAML-префабов
  редактором, вид часов, звук и вибрация в шлеме.
- **Киборг без часов.** `PlayerControllersCyborgAvatar` в реестре, но часов у него не было и до задачи
  (`На_руке_часы_с_табло` для него красный). С уходом HUD киборг остаётся без статуса и нотификаций. Установщик —
  `Tools/VR Battlegrounds/Avatars/Install Wrist Watch (registered avatars)` (`WristWatchInstaller`): переносит позу часов
  MEF через универсальные оси предплечья UltimateXR. Призрак (`GhostAvatar`) — вариант киборга и получит часы вместе
  с ним. Запустить под замком, посмотреть позу в сцене, прогнать тесты аватаров.
