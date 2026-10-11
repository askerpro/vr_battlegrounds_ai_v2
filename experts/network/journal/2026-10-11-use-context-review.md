# Use-context (weapon-system + bots-fix): сетевое согласие на совместное вливание с условиями

Запрос: событие хаба 3595 от weapon-system, ответ 3597 (копия условий bots-fix — 3599). Объект: bots-fix
`93f67deda` (`WeaponEquipmentBinding`, `IWeaponEquipmentSocketOwner`) и weapon-system `d942dce3d` (SDK71 lifetime
захвата, SDK72 `IUxrWeaponOwnerProvider`, WeaponSystem/WeaponUseContext, префабы AK105/PPK). Только чтение кода.
Сетевую часть SDK/WeaponSystem разбирал субагент, binding и семантику Mirror — network.

Решение: согласие при двух условиях до вливания:
- вливать одним пакетом: в `d942` нет скрипта binding (GUID `bf92bfab…`), отдельно — ошибка компиляции и Missing Script;
- принять ответы по позднему входу, reconnect и порядку hook/OnStartClient относительно UXR-снимка.

Почему допустимо: claim — состояние для опоздавшего в SyncVar, писатель только сервер (`IsWorldAuthority`),
`syncDirection` ServerToClient. Hook в Mirror 96 срабатывает при десериализации спавна, если значение отличается
от default; проекция идемпотентна по epoch. Локальный откат SDK-захвата через `ExecuteStateSyncEvent` в сеть не
уходит (`IsInsideStateSync`, Relay молчит). SDK71 не меняет формат снимка. SDK72 фиксирует стрелка на сервере
при регистрации выстрела. Новых Cmd/Rpc/NetworkMessage нет.

До этапа, где claim станет достижим (clip-cutover):
1. Конфликт «SDK-хват + claim» не снимается: сверка только при смене epoch, выключается лишь корневой grip.
   Нужна локальная сверка при `IsActive && HasSdkGrabs` и закрытие всех grip иерархии.
2. `TryGetOwner`: `GetComponent<UxrActor>` против `GetComponentInChildren` в SDK.
3. Ярус C: ServerOnly и хост, поздний клиент и reconnect с экипированным оружием. Guard-тест «claim в SyncVar» —
   после приёмки.

Остаток вне пакета: handover Issue 38 не меняется. Ревью `PlayerSession` (`_botControlled`, `BotAvatarSource`)
этим согласием не покрыто — см. ниже.

## bots-fix/armed-development: PlayerSession — ACK, GameNetworkManager — условие (3607 → 3611)

`PlayerSession`: `[SyncVar] _botControlled` задаётся фабрикой до Spawn и не меняется, это корректное состояние
для опоздавшего. `BotAvatarSource` только серверный. Бот-сессии в восстановление не попадают (запись только при
отключении с непустым deviceToken). Некритично: проверка `NetworkServer.active` в `SetBotControlBeforeSpawn`.

`GameNetworkManager.OnStartClient` (на ревью не присылали, смотрел по рабочей копии bots) регистрирует
ClipBody-префабы. Повтор при каждом старте нужен: `NetworkClient.Shutdown` очищает prefabs. UniqueId выравнивает
SDK: в вариантах сохранён `UxrMirrorAvatar`. Условие до вливания: отказ регистрации вызывает `StopClient`, после
чего `GameNetworkDiscovery.Start` в плеере снова подключается. Детерминированный отказ даёт бесконечный цикл
переподключения, на хосте — переход в ServerOnly. Решение: ранняя проверка на сервере, валидатор или тест и
терминальный отказ клиента без автоповтора, с launch-infra.
