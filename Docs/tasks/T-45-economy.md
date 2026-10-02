# T-45 · Экономика и покупки как в CS2

> Верификация 2026-10-02: соответствующие проверки экономики, ботов, экранов и часов выполнены
> в Unity (общий целевой запуск 168/168 PASS); AndroidCompileGate PASS. Полный прогон после
> контрактов окружения: 1683/1762 PASS, 79 отказов других групп перечислены в
> [отчёте](verification-2026-10-02.md). Визуальная проверка и два физических клиента этим не заменяются.

| | |
|---|---|
| Находка | — (требование пользователя, шаг 3 серии «оружие → баланс → экономика») |
| Блокирована | — |
| Уровень проверки | 1 (EditMode: правила, сервер в `MirrorTestHarness`) + шлем (подсветка, табло, кобура) |
| Статус | 🔧 код и тесты готовы; прогон в Unity и шлем — см. «Что не проверено» |

## Требование

> надо реализовать систему покупок и экономики как в cs2, в арсенале подсвечиваются и можно брать только оружие
> которое может себе позволить по текущим финансам. каждый арсенал закреплен за игроком и текущие деньги должны
> отображаться на табло арсенала. экономика прироста денег тоже как в cs2.

## Числа (CS2 MR12, сверка 2026-10-01)

Источники: [refrag.gg — kill rewards и loss bonus](https://refrag.gg/blog/cs2-economy-crash-course-what-are-kill-rewards-and-loss-bonus/),
[cs2guide.net — loss bonus](https://cs2guide.net/economy-system/loss-bonus-understanding/),
[skinsbook — CS2 economy guide](https://skinsbook.com/blog/cs2-economy-guide/). Награда за убийство — `WeaponInfo.KillAward`
(`kill_award` из `weapons.vdata`, T-38).

| Что | Число | Где в коде |
|---|---|---|
| Деньги в начале половины / матча на карте | 800 | `EconomyRules.StartMoney` |
| Потолок | 16000 | `EconomyRules.MaxMoney` |
| Победа в раунде (устранение) — каждому игроку | 3250 | `EconomyRules.RoundWinReward` |
| Поражение — лестница по счётчику 0…4 | 1400 / 1900 / 2400 / 2900 / 3400 | `EconomyRules.LossBonus` |
| Счётчик на старте половины | 1 (пистолетный раунд проигравшим — 1900) | `HalfStartLossCounter` |
| Победа | счётчик −1, не сброс (MR12: 5 поражений → победа → поражение = 2900) | `CounterAfterWin` |
| Убийство | `KillAward` оружия в руке убийцы; оружие неизвестно — 300 | `KillReward`, `DefaultKillAward` |
| Убийство союзника | −300 | `TeamKillPenalty` |

## Решения

| Вопрос | Решение | Почему |
|---|---|---|
| Где деньги | `MatchEconomy` (NetworkBehaviour) на префабе `EliminationMode` — правило режима, как `RoundMagazineRefill`. Правда — `EconomyAccounts` на сервере, клиентам — копия `SyncDictionary<string,int>` | Деньги — состояние матча на карте: новый режим — новые 800. Поздний клиент получает их начальным значением спавна |
| Ключ игрока | `Series.PlayerKey` (токен устройства) | Переживает переподключение, тот же ключ у статистики серии |
| Разминка | **бесплатно**: на `WarmupMode` экономики нет, стена отдаёт всё любому | Разминка — пристрелка и выбор, деньги там ничего не значат |
| Режим Respawn | без экономики (бесплатно) — отложено | Матч без раундов, CS2-лестнице там нечего считать |
| Ничья (время вышло) | поражение обеим командам: бонус и рост счётчика | В CS2 ничьих нет (время за защиту); «никому 3250» честнее, чем отдать победу случайной стороне |
| Время покупки (buy time) | фаза `Equipment`: стена открыта только в ней (и так было). Зона покупки — у своей стены, а стена стоит в своей зоне спавна | Одно правило вместо второго таймера: закрытая шторка и есть конец закупки |
| Повторная покупка | можно, если хватает денег, но слот стены пустеет до следующего раунда — купить второй такой же ствол со своей стены нельзя | Просто и как в CS2 по деньгам |
| Передумал | повесил купленный в эту закупку ствол обратно на стену — деньги вернулись (чек на `netId` предмета, живёт один раунд) | Иначе «взял, повесил» стоил бы цену ствола; в CS2 есть возврат в закупку |
| Стена ↔ игрок | стены в зоне спавна команды (на картах — 4 на зону) делятся между игроками этой команды по одной; владелец сохраняет стену, пока он в команде зоны; после смены сторон раздаются заново | `ArsenalOwnership` — чистое правило; зона стены — `SpawnZoneMembership` (на картах стена — **сосед** зоны в общей группе, не дочерний объект: поиск по родителям не находил зону ни одной стены — исправлено в [T-47](T-47-laser-grid-screens.md)) |
| Ничья стена (игроков меньше стен) | не продаёт никому, ствол приглушён | Покупка чужими деньгами невозможна по построению |
| Игроков больше стен | лишний без стены (покупать негде, только стартовый пистолет) | На арене до 8 игроков — по 4 стены на сторону хватает |
| Стартовый пистолет | `Viper` (`WeaponRegistry.DefaultSidearm`) бесплатно в кобуру `Anchor_Hip_R` живому игроку без пистолета, раз за раунд, в подготовке/закупке | CS2: Glock/USP при спавне |
| Сохранение оружия выживших (CS2) | **отложено**: у нас оружие раунд не переживает (`EquipmentStrip` на входе в `Resolution`, все выбывают в конце боя) — пистолет получают все каждый раунд | Изменение раундов — отдельная задача; правило «нет пистолета» уже проверяется и заработает само |
| Пауза | деньги и счётчики на начало прерванного раунда уходят в `PauseSnapshot` (`IPauseSnapshotPart`), «Продолжить» возвращает | Тот же принцип, что у счёта: прерванный раунд не засчитывается |
| Оружие убийцы | что у убийцы в руке в момент смерти | `DamageLedger` оружия не хранит; рука — честное приближение (граната и т.п. позже) |

## Как устроено

```
EliminationMode.prefab
 ├─ EliminationMode       RoundBeganServer(round, firstOfHalf), RoundScoredServer(winner) — новые события экземпляра
 ├─ MatchEconomy          деньги (SyncDictionary), доход за раунд, счётчики; награды; ServerTryPurchase / ServerTryRefund
 ├─ ArsenalCheckout       касса: ArsenalWallController.ItemTakenServer → списать или отменить (рука отпускает, ствол домой)
 ├─ ArsenalOwnershipPolicy раз в 0,5 с: зона стены → команда → ArsenalOwnership.Assign → wall.ServerSetOwner
 └─ StartingSidearmPolicy раз в 0,25 с в Setup/Equipment: Viper в кобуру (PlayerLoadoutManager.ServerGiveWeapon)

ArsenalWallController     [SyncVar] _ownerSessionNetId; RefreshOffers() каждый кадр (пустой без изменений)
 ├─ ArsenalSlotController.ApplyOffer(SlotOffer)  IsGrabbable — только автор мира (сервер); свет, приглушение, ценник — все
 ├─ ArsenalPriceTag       «TR15  $2900» под стволом: зелёный — по карману, красный — нет (только при экономике)
 └─ ArsenalWalletDisplay  табло над панелью жетона: имя владельца, $деньги, «+3250» до обратного отсчёта
GrabRules → ArsenalGrabRule  чужая стена и дорогой ствол не берутся рукой (машина игрока; сервер проверяет сам)
```

**Сеть, один автор (Issue 23).** Деньги пишет только сервер. `IsGrabbable` слотов — синхронизируемый, пишет его
только `StateEventAuthority.IsWorldAuthority` (как блокировка закрытой стены). Правило хвата — локальная проверка
машины того, кто тянется, по реплицированным владельцу и деньгам; это удобство, не защита. Защита — касса на
сервере: взял не владелец или не хватает денег — захват отменяется (`ReleaseGrabs` с рассылкой + `ServerReturnHome`),
отмена откладывается на кадр (не менять менеджер захвата посреди его же рассылки).

**Класс ошибки, найденный по дороге: «оружие, выданное не стеной, — не оружие».** `WeaponComponent` ставила только
стена при выдаче (`AssignNetworkItem`), поэтому ствол, созданный иначе (стартовый пистолет в кобуру, оружие бота), у
клиентов не знал своего `WeaponInfo`. Теперь `NetworkUxrIdentity.InstanceCreated` (сервер и клиентский обработчик
спавна — одна точка) → `WeaponComponent.AttachByPrefab` по `WeaponRegistry.GetByPrefab`. Тест —
`WeaponComponentPrefabHookTests`.

## Точки входа для следующих агентов

- **Деньги для HUD/часов:** `MatchEconomy.Current` (null — денег нет), `GetMoney(session)`, `GetRoundIncome(session)`,
  `GetLossCounter(team)`, `NextLossBonus(team)`; свой игрок — `PlayerSession.LocalSession`.
- **События:** `MatchEconomy.MoneyChangedLocal(playerKey, money)` — любая машина, по изменению `SyncDictionary`;
  `MatchEconomy.TransactionLocal(EconomyTransaction)` — разовое уведомление клиенту (`Player`, `Delta`, `Money`,
  `Reason`: RoundWin/RoundLoss/Kill/TeamKill/Purchase/Refund, `Detail` — ствол или команда); на сервере —
  `TransactionServer`. Ключ своего игрока — `MatchEconomy.KeyOf(PlayerSession.LocalSession)`.
- **«Бот покупает»:** `MatchEconomy.ServerTryPurchase(botSession, weaponInfo, 0)` списывает деньги без предмета;
  выдача ствола — `PlayerLoadoutManager.ServerGiveWeapon(info)` (кладёт в свободную кобуру второго оружия) или
  свой спавн бота. Сейчас бот получает стартовый Viper как все и, как раньше, бесплатно берёт ствол в руку
  (`BotGunner`) — покупка ботов не сделана. **Сделана в [T-48](T-48-bots-match.md)** (`BotShopper`, `BotBuyPlan`).

## Что сделано

- Правила: `EconomyRules`, `EconomyAccounts`, `ArsenalPurchaseRules`, `ArsenalOwnership` — чистые классы.
- Сеть и сервер: `MatchEconomy`, `ArsenalCheckout`, `ArsenalOwnershipPolicy`, `StartingSidearmPolicy` на
  `EliminationMode.prefab`; события `EliminationMode.RoundBeganServer` / `RoundScoredServer`; `IPauseSnapshotPart`.
- Стена: владелец (`SyncVar`), `RefreshOffers`, `ServerRejectTake`, события `ItemTakenServer` / `ItemReturnedServer`;
  слот — `ApplyOffer`, приглушение блоком свойств, ценник; табло `ArsenalWalletDisplay`.
- `PlayerLoadoutManager.ServerGiveWeapon`, `HasWeaponOfCategory`; `WeaponRegistry.GetByPrefab`;
  `WeaponComponent.AttachByPrefab` + `NetworkUxrIdentity.InstanceCreated`.
- `MatchEconomy.RpcTransaction` записан в реестр `RpcCarriesNoStateTests` (уведомление; деньги — `SyncDictionary`).

## Как проверить

- EditMode: группа `VrBattlegrounds.Tests.Economy` — `EconomyRulesTests`, `EconomyAccountsTests`,
  `ArsenalPurchaseRulesTests`, `MatchEconomyServerTests`, `WeaponComponentPrefabHookTests`.
- Шлем / Play Mode (два клиента или бот): «Начать матч» → на своей стене табло «Имя / $800»; Viper в кобуре на бедре;
  на стене зелёные ценники Viper/Gun_real/Revolver, остальное красное и приглушённое, не берётся; TR15 у соседа по
  команде не берётся; Revolver (700) взят → $100, перевешен обратно → $800; конец раунда → «+3250» / «+1900».

## Что не проверено (2026-10-01)

Редактор Unity весь сеанс стоял на модальном окне «Scene(s) Have Been Modified» (сохранить `Untitled`) — MCP не
отвечал, снять окно автоматически агенту не разрешено. Поэтому:

- **Проверено:** компиляция `VrBattlegrounds` (редактор и Android-плеер — те же `csc`/`rsp`, что у Unity, вне
  редактора) и `VrBattlegrounds.Tests.EditMode`; 21 тест чистых правил зелёный вне Unity (тот же NUnit), и красный на
  мутации «победа сбрасывает лестницу» (как в CS:GO) — `Победа_не_сбрасывает_лестницу_а_опускает_на_ступень`.
- **Не проверено:** `MatchEconomyServerTests` и `WeaponComponentPrefabHookTests` (нужны Mirror и Unity), полный
  EditMode-прогон, Mirror weaver на новых `SyncVar`/`SyncDictionary`/`ClientRpc`, вид табло и ценников, укладка Viper
  в кобуру на клиенте (`PlaceObject` сервера по каналу состояния — как возврат жетона на крючок).
