# Допуск событий сетевых целей relay, ревизия v2

2026-10-20. Владелец: network. Задача: e2e-combat. Предыдущая ревизия:
[v1](../../network-sync-hardening/contracts/avatar-root-event-admission-v1.md) (корень сетевого аватара).
Статус: **вариант А выбран пользователем 2026-10-20, код написан, Unity-проверка и приёмка не проведены**.
Совместимость: `breaking` по wire — меняется сигнатура `CmdComponentStateChanged` (Mirror hash), нужна одна сборка
всех участников. Семантика v1 для корней аватаров не меняется.

## Мотивация

E2E раунда Elimination (`tasks/e2e-combat/reports/round-2/main-*.log`): клиент-стрелок отпускает курок
(`UxrFirearmWeapon.SyncTriggerPressStates`) в пределах RTT от входа в Resolution. Сервер в этот момент уже
снял оружие (`EquipmentStrip.ServerStripAll` → `NetworkServer.Destroy`). Команда доходит до SDK-декодера,
UniqueId не разрешается, в логе сервера появляется Error `Target component wasn't found ... SyncTriggerPressStates`.
Так же будет у живого игрока на любом серверном снятии предмета: конец раунда, смена режима или карты, смерть.
Исходящий `DespawnedObjectEventFilter` эту гонку не видит: клиент ещё не знает о снятии.
В v1 расширение на «netId любой ближайшей NetworkIdentity» было отложено «до доказанной необходимости».
Этот прогон её доказывает.

## Что добавлено

Аргумент `uint itemNetId` у `NetworkStateRelay.CmdComponentStateChanged`
(`byte[], uint avatarRootNetId, Guid avatarRootUniqueId, uint itemNetId, NetworkConnectionToClient`).
`RpcComponentStateChanged` не меняется.

| Конверт | Значение |
|---|---|
| avatarRoot = (0, Empty), itemNetId = 0 | Прежний unscoped путь: объект без NetworkIdentity, дети аватара, незаспавненный объект |
| avatarRoot = (N, G), itemNetId = 0 | Scope корня аватара, v1 без изменений |
| avatarRoot = (0, Empty), itemNetId = M > 0 | **Item scope v2**: ближайшая NetworkIdentity источника — сетевой не-аватарный объект |
| avatarRoot ≠ 0 и itemNetId ≠ 0 | InvalidEnvelope: Error, без decode, Execute и forward |

### Отправка (клиент, `Send`, ветка Cmd)

`ItemEventAdmission.CaptureOutgoing` вызывается только при avatarRoot = 0. Шаги:
- взять `source.Component.GetComponentInParent<NetworkIdentity>(true)`;
- netId = 0 → 0;
- на identity есть `UxrAvatar` (корень или дети аватара) → 0;
- запись `NetworkClient.spawned[netId]` не тот же экземпляр → 0;
- иначе вернуть netId.

Значение снимается до сериализации и не переназначается. Сервер и хост собственные события itemNetId не помечают:
RPC не меняется.

### Приём (сервер, `CmdComponentStateChanged`)

Порядок:
1. Прежний guard соединения.
2. Прежняя проверка AvatarRoot v1.
3. `AcceptIncomingItem`. При itemNetId = 0 — прежний путь. Иначе:
   - счётчик `ItemScopedReceived++`;
   - двойной scope → InvalidEnvelope;
   - `NetworkServer.spawned` не содержит живую identity → `ItemUnavailable++` и Verbose
     `server: предмет недоступен (netId=…); SDK/forward пропущены.`, без decode, Execute и forward.
4. Прежний строгий decode, binding v1, ammo policy, Execute и forward.

### Чего v2 не делает

- **Это ворота доступности, не binding.** Decoded target с itemNetId не сверяется. Ближайшая NetworkIdentity
  компонента может смениться в полёте (магазин отделён, предмет перепарентен). Ложный конверт даёт отправителю
  лишь потерю собственного события; живой объект с чужим netId идёт прежним строгим decode.
- Истории снятых netId, tombstones и SDK-патча нет. Владелец жизни — Mirror `spawned`.
- Клиентская сторона RPC не проверяет item scope. Сервер проверил живость перед forward, порядок одного reliable
  канала сохраняет Spawn → event → Destroy. AOI (InterestManagement) в проекте не используется.
  С появлением AOI нужна отдельная ревизия.
- Аватары (корень и дети) и объекты без NetworkIdentity работают как раньше.

## Владельцы состояния

Mirror владеет netId и spawned. Gameplay владеет снятием и уничтожением предметов (`EquipmentStrip`, смерть,
смена карты). SDK владеет UniqueId, decode и Execute. Relay — единственный транспорт. Счётчики `ItemScopedReceived`
и `ItemUnavailable` лежат в экземплярной `AvatarRootAdmissionCounters` relay и в решении не участвуют.

## Стоимость

+1–5 байт на клиентскую команду (Mirror VarUInt; netId до 240 — 1 байт). Возможен рост length prefix.
CPU: один `GetComponentInParent` и один lookup словаря на клиентское событие. Такой же `GetComponentInParent`
уже делает `DespawnedObjectEventFilter`. На сервере — один lookup на item-событие.
Mirror hash новой команды — 6239: формула проверена на прежней сигнатуре (43519), коллизий с 138 вызовами
текущих сборок `VrBattlegrounds`/`Mirror`/`Mirror.Components` нет. Проверка — по строкам из DLL main checkout,
не Weaver этой ветки.

## Поздний вход, reconnect, смена карты

- Поздний вход: снимок (`TargetLoadInitialState`) и barrier не меняются.
- Reconnect: команды снятого соединения отсекает прежний guard. netId в живой серверной сессии монотонен.
  Повтор или сброс netId без Shutdown нарушает этот контракт, как и в v1.
- Смена карты: события объектов выгруженной сцены, пришедшие после их unspawn, теперь отбрасываются
  `ItemUnavailable` с Verbose вместо Error декодера. Клиент между сценами по-прежнему не шлёт (not ready).
- Остаточный риск ложного отброса: событие снято, пока компонент был под identity X; сервер успел вынести
  компонент из X и снять X, оставив компонент живым. В текущих путях снятия (`EquipmentStrip`, смерть)
  дети уничтожаются вместе с X. Нужна проверка на отделённых магазинах.

## Наблюдаемость и приёмка

| Проверка | Требование |
|---|---|
| E2E Elimination с убийством (ServerOnly + 2 клиента) | На сервере нет `Target component wasn't found ... SyncTriggerPressStates`; при Verbose есть `предмет недоступен (netId=…)`, `ItemUnavailable` ≥ 1 |
| Живое оружие (выстрел, затвор, магазин, хват) | Эффект ровно один, эха у автора нет, хост не исполняет дважды |
| События аватара | Без изменений: счётчики v1 ведут себя как раньше |
| Смешанные сборки | Не поддерживаются: старый клиент с новым сервером не совпадёт по hash |

Тесты пишутся после пользовательской приёмки. Устаревшее ожидание:
`Assets/Tests/EditMode/Network/RemoteCallHashTests.cs`, `Расчёт_хэша_совпадает_с_регистрацией_Mirror`
(сигнатура без `System.UInt32` itemNetId, хэш 43519). Новое значение 6239.
