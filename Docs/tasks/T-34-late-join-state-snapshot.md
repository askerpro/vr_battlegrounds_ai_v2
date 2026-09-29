# T-34 · Поздний клиент получает всё состояние мира, а не только будущие изменения

| | |
|---|---|
| Находка | NET-27 (новая, 2026-09-29) — найдена при проектировании экрана «Обзор» ([T-33](T-33-tablet-overview-screen.md)) |
| Блокирована | — |
| Уровень проверки | 1 (EditMode: инвариант снимка) + C (сценарий `late-join-state-parity`) |
| Оценка | 1,5–2,5 дня (рекомендуемый вариант), из них правка SDK — 1 час, остальное — проверялки |

## Требование

Сформулировано пользователем:

> Может это проблема архитектуры и надо это чинить отдельно? Может мы вместо того, чтобы рассылать
> по сети сразу новое состояние хп после получения урона, зря рассылаем damage?

Поводом стал вывод агента T-33: у клиента, подключившегося посреди матча, у раненых 100 хп,
а выбывшие — живые до следующего изменения.

## Короткий ответ на вопрос

**Damage по сети не рассылается. Рассылается ровно то, что предлагает пользователь, — новое
значение хп.** Урон считает только сервер, а клиентам уходит абсолютное значение `UxrActor.Life`
(идемпотентное состояние, не дельта) плюс отдельное событие эффекта (`PlayDamageEffects` — анимация
и звук) или смерти (`DieInternal`). Разделение «состояние + событие для эффектов» уже правильное.

Дефект в другом: **состояние едет только событиями изменения, а в снимок для позднего клиента
не попадает**. `UxrActor` — единственный компонент UltimateXR с синхронизируемым игровым свойством,
который не переопределяет `SerializeState`. Это не перепроектирование сети, а один пропущенный
метод в SDK плюс проверка, которая не даст пропустить такой метод снова.

## Сделано (2026-09-29)

| Шаг | Файл | Результат прогона |
|---|---|---|
| Харнесс класса (UltimateXR) | `Assets/Tests/EditMode/Network/StateSnapshotCoverageTests.cs` | до патча **3 из 3 красные**: сканер — «`UxrActor.Life`: поле `_life` не входит в `SerializeState`»; снимок туда-обратно — 100 вместо 37; регистрация актора — `false` (SDK исключал актор из снимка целиком). После патча — 3 из 3 зелёные |
| Патч SDK 29 | `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrActor.StateSave.cs` | запись в `Docs/UltimateXR/sdk-patches.md`, Issue 25 в `known-issues.md` |
| Харнесс класса (Mirror) | `Assets/Tests/EditMode/Network/RpcCarriesNoStateTests.cs` | реестр 13 RPC с причинами; с удалённой записью `TargetShowNotification` — красный ровно на ней, с полным реестром — зелёный |
| Ворота Android | `AndroidCompileGate.Run()` | PASS |

Решения по ходу:

- **`UxrAvatar.ShowControllerHands` и `UxrGrabbableResizable.IsGrabbable/IsKinematic` — в исключения теста,
  а не в снимок.** У `UxrGrabbableResizable` своего поля нет: сеттер пишет в три дочерних `UxrGrabbableObject`,
  у которых `_isGrabbable` и `isKinematic` в снимке уже есть — состояние доезжает. `ShowControllerHands` в игре
  не переключается (0 вызовов), а чтение одного поля не обновило бы модели контроллеров — «исправление» дало бы
  ложное чувство покрытия. Тест требует убрать исключение, если свойство исчезнет, и назовёт новое, если появится.
- **Реестр RPC — в тесте, а не атрибутом `[NetEvent]` на методах.** Один из RPC (`PlayerHUDManager`) лежит в
  `Assets/Scripts/UI/**`, где параллельно работал другой агент; к тому же реестр показывает решение по всем RPC
  в одном месте. Эффект тот же: новый RPC без записи — красный тест.
- **Правило в `CLAUDE.md` не внесено.** Файл правил меняет только пользователь. Предлагаемая строка в
  «Жёсткие правила»: *«Состояние — `SyncVar`/`SyncList` или снимок UltimateXR (`SerializeState`); `[ClientRpc]`
  и событие-метод — только эффекты. Проверка: `StateSnapshotCoverageTests`, `RpcCarriesNoStateTests`»*.

Не сделано — **сценарий яруса C `late-join-state-parity`** (описан ниже, в «Проверках»). Причина: нужна
пересборка e2e-плеера из того же редактора (~5 мин блокировки), пока другой агент перестраивает в нём префабы
меню, — сборка захватила бы их промежуточное состояние. Что остаётся без прогона:
(1) что снимок с жизнью действительно применяется у настоящего второго процесса — ID актора у клиента совпадает
(аватары выравнивают `UniqueId` до запроса снимка, по коду); (2) гипотеза о гонке у самого опоздавшего
(событие `Life = 0` приходит раньше снимка). После патча гонка закрыта по построению: снимок берётся на сервере
позже события и уже несёт `Life = 0`, — но это вывод из кода. Статус задачи — 🔧 до этого прогона.

## Что сейчас

### Путь попадания (проверено по коду)

```
Стрелок: UxrProjectileSource.Shoot → событие канала состояния (автор — стрелок, StateEventAuthority)
Каждая машина симулирует снаряд → UxrActor.ReceiveImpact → OnReceiveDamage
  DamageReceiving — на каждой машине (PlayerController отменяет урон в разминке;
                    DamageLedger пишет только сервер)
  Сервер (NoSessionOrSessionOwner):
    не смертельно:  Life -= damage       → событие UxrPropertyChangedSyncEventArgs(Life, новое значение)
                    PlayDamageEffects()  → событие-метод (анимация, звук)
    смертельно:     DieInternal()        → одно событие-метод; Life = 0 внутри него — вложенный вызов,
                                           отдельно не уходит, у получателя обнуляется повтором метода
  Клиент: Life не трогает, DamageReceived поднимает локально (только лог в PlayerController)
```

| Факт | Где | Статус |
|---|---|---|
| Жизнь вычитает только сервер | `UxrActor.cs:172` — `Life -= e.Damage` под `NoSessionOrSessionOwner` | проверено по коду; подтверждено сценарием `weapon-hit-damage` |
| В сеть уходит новое значение, а не урон | `UxrActor.Life` сеттер: `BeginSync` → `_life = value` → `EndSyncProperty(value)` | проверено по коду |
| Эффекты — отдельным событием | `PlayDamageEffects` / `DieInternal`: `BeginSync` … `EndSyncMethod` | проверено по коду |
| Транспорт — `NetworkStateRelay` (`RpcComponentStateChanged`), не Mirror `SyncVar` | `Assets/Scripts/Network/NetworkStateRelay.cs` | проверено по коду |
| Начальный снимок **есть** | `NetworkStateRelay.CmdRequestInitialState` → `UxrManager.SaveStateChanges(ChangesSinceBeginning)` → `TargetLoadInitialState`; запрашивается в `OnStartClient` и после каждой смены сцены | проверено по коду |
| До снимка входящие события **отбрасываются** | `RpcComponentStateChanged`: `if (!_initialStateLoaded) return` | проверено по коду |
| В снимок попадает только то, что компонент пишет в `SerializeState` (плюс `enabled/active` и трансформ, если он их просит) | `UxrStateSaveImplementer<T>.SerializeState` | проверено по коду |
| **`UxrActor` не переопределяет `SerializeState` → `_life` в снимок не попадает** | в `Mechanics/Weapons/` нет `UxrActor.StateSave.cs`; у соседей (`UxrFirearmMag`, `UxrFirearmWeapon`, `UxrWeapon`, `UxrGrabbableObject`) он есть | проверено по коду |
| Выбывший виден призраком правильно | `SpectatorController._isSpectating` — `SyncVar`, применяется в `OnStartClient` | проверено по коду |
| Поздний клиент видит раненого со 100 хп, мёртвого — с `IsAlive = true` | следствие двух строк выше | **гипотеза**, прогоном не проверено |

Итог для позднего клиента: призрак нарисован (Mirror), но `PlayerController.Health == 100`
и `IsAlive == true` (UltimateXR). Два источника одного факта разъехались.

### Опаснее всего — сам опоздавший (гипотеза, высокая вероятность)

`EliminationMode.ServerAdmitAvatar` вводит аватар, пришедший посреди матча, выбывшим:
`ServerEliminateSilently` → `_actor.Life = 0`. Порядок на проводе, выведенный из кода:

1. Клиент готов → `SendConnectMessage` (сразу, из `OnClientConnect`).
2. Сервер: спавн сессии и аватара → `Life = 0` → событие (после выравнивания id, `AvatarStateEventGate`) → `Rpc`.
3. Клиент: спавн-сообщения → `NetworkStateRelay.OnStartClient` → `CmdRequestInitialState` — на круг позже шага 2.
4. Событие `Life = 0` приходит **до** снимка и отбрасывается; снимок `Life` не несёт.

Следствие у самого опоздавшего: он призрак, но `IsAlive == true` локально — `PlayerGrabManager.IsGrabAllowed`
разрешает ему хватать оружие (хват авторствует владелец), `WristDisplay` и `HUDWidget_Health` показывают 100,
`TeamSpawnZone` и напоминание HUD считают его живым. Проверяется сценарием ниже.

## Класс ошибки

**Состояние, которое доставляется только событиями изменения, не входит в снимок для позднего
получателя.** Нарушенное допущение: «все, кому важно состояние, были подключены, когда оно менялось».
Код позволил его нарушить, потому что в UltimateXR «синхронизируется» и «сохраняется в снимок» — два
независимых механизма (`EndSyncProperty` и `SerializeState`), и ничто не сверяет, что второй покрывает
первый. У Mirror та же развилка: `[ClientRpc]` против `SyncVar`/`SyncList`.

Поздний получатель — это не только игрок, вошедший посреди матча, но и:
- переподключение (`SessionRecoveryManager`);
- **каждая смена карты**: клиент перезагружает сцену и заново берёт снимок (`HandleClientSceneChanged`);
- наблюдатель/админ с планшета, подключившийся позже.

## Все экземпляры

### Канал состояния UltimateXR (снимок `SaveStateChanges(ChangesSinceBeginning)`)

Все синхронизируемые свойства SDK (`EndSyncProperty`) против `SerializeState` своего компонента:

| Данные | Механизм | Что видит поздний клиент | Серьёзность |
|---|---|---|---|
| **Хп `UxrActor.Life`** (→ `PlayerController.Health`) | свойство-событие, в снимке нет | 100 у раненого | **высокая** — HUD, «Обзор», решения по хп |
| **Смерть** (`Life = 0` из `DieInternal`, `ServerEliminateSilently`, `RestoreHealth(0)`) | то же | мёртвый/выбывший «жив»: `IsAlive = true` при `IsSpectating = true` | **высокая**; у самого опоздавшего — хват оружия призраком (гипотеза) |
| `UxrAvatar.ShowControllerHands` (`_showControllerHands`) | свойство-событие, в снимке нет | значение из префаба | низкая — в игре не переключается (проверить Grep) |
| `UxrGrabbableResizable.IsGrabbable/IsKinematic` | свойство-событие; в снимке только счётчики хвата | значение из префаба | низкая — в префабах игры, вероятно, не используется (проверить по GUID) |
| `UxrGrabbableObject.IsGrabbable` (слоты арсенала, жетон) | свойство-событие, **в снимке есть** (`_isGrabbable`) | верно | — |
| Патроны в магазине, патронник, хват, якоря, владелец оружия | события-методы; **в снимке есть** (`_rounds`, `_runtimeTriggers`, `_currentManipulations`, `_currentPlacedObject`, `_owner`) | верно | — (по коду; прогоном не проверено) |
| Эффекты (`PlayDamageEffects`, анимация и звук смерти, выстрел) | события-методы | ничего — и правильно | — это события, а не состояние |

### Mirror (`[ClientRpc]` / `[TargetRpc]` против `SyncVar`/`SyncList`)

Все 13 RPC в `Assets/Scripts` просмотрены:

| RPC | Что несёт | Есть ли состояние рядом | Поздний клиент | Серьёзность |
|---|---|---|---|---|
| `EliminationMode.RpcOnRoundStarted/Ended` | номер раунда, победитель раунда | фаза, номер, счёт — `SyncVar`; **победитель раунда — только в RPC** (`_lastRoundWinner` серверный) | в фазе «конец раунда» не знает, кто выиграл раунд | низкая (баннер HUD; «Обзор», если захочет показать) |
| `GameMode.RpcOnModeStarted/Finished` | старт, победитель | `MapReferee._currentState`, `Series._results` — `SyncVar`/`SyncList` | верно по состоянию, баннер пропущен | — событие для HUD |
| `EliminationMode.RpcOnSidesSwapped` | «стороны сменились» | `_sidesSwapped` — `SyncVar` с хуком | верно | — |
| `MapReferee.RpcPlayerKilled` | лента убийств | счёт K/D — `SyncVar` сессии | ленты нет — и правильно | — событие |
| `MapReferee.RpcOnMapFinished/RpcOnStopped` | только лог | `_currentState` — `SyncVar` | верно | — |
| `PlayerController.RpcOnDied` | страховка: владелец отпускает предметы | — | не нужно | — событие |
| `PlayerController.RpcDevTeleport` | отладочный перенос | позиция — `NetworkTransform` | верно | — |
| `PlayerHUDManager.TargetShowNotification` | всплывашка | — | не нужно | — событие |
| `NetworkStateRelay.RpcComponentStateChanged/TargetLoadInitialState` | канал UltimateXR | снимок | см. таблицу выше | — |

Остальное игровое состояние уже сделано состоянием (проверено по коду): команда, скин, имя, K/D/очки,
готовность, калибровка, зона спавна, жетон — `SyncVar` `PlayerSession`; наблюдатель — `SyncVar`
`SpectatorController`; фаза, раунд, таймеры (`NetworkTime`), стороны, ожидание готовности — `EliminationMode`;
счёт карт, серия, статистика — `Series`; названия команд — `TeamNameService`; стена арсенала — `SyncVar` +
`SyncDictionary`; карман магазинов — `SyncList`. Дверей и подвижных объектов карт с сетевым состоянием
в `Assets/Scripts` нет.

**Вывод:** на стороне Mirror класс уже вычищен задачами T-13, T-15, T-29 (урок NET-06/NET-07 выучен).
Настоящая дыра одна — канал UltimateXR, и в ней одно игровое поле: `UxrActor._life`.

## Рекомендуемое решение — А. Снимок покрывает каждое синхронизируемое свойство

### 1. Правка SDK (Патч 29 в `Docs/UltimateXR/sdk-patches.md`)

Новый файл `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrActor.StateSave.cs`
(класс уже `partial` по Патчу 2) — по образцу `UxrFirearmMag.StateSave.cs`:

```csharp
protected override void SerializeState(bool isReading, int version, UxrStateSaveLevel level, UxrStateSaveOptions options)
{
    base.SerializeState(isReading, version, level, options);

    // Инкрементальные снимки получают Life событием; полный и «с начала» — отсюда (T-34).
    if (level > UxrStateSaveLevel.ChangesSincePreviousSave)
        SerializeStateValue(level, options, nameof(_life), ref _life);
}
```

Чтение меняет только поле: `Died`, анимация, звук и `PlayerDied` у позднего клиента **не** поднимаются —
так и нужно, смерть случилась до него. Кому нужно «стало мёртвым» при загрузке снимка (сегодня — никому:
`TeamSpawnZone` и HUD читают `IsAlive` опросом или на смене фазы), тот подписывается на
`UxrStateSaveImplementer.StateDeserialized`, а не на `Died`.

`ShowControllerHands` — туда же в `UxrAvatar.StateSave.cs` (одна строка) или в список исключений
теста с причиной; `UxrGrabbableResizable` — так же. Решает исполнитель, записать в патч.

### 2. Инвариант, который ловит весь класс

**Тест `StateSnapshotCoverageTests` (EditMode, уровень 1).** Для каждого класса в
`Assets/ThirdParty/UltimateXR/Runtime/Scripts` и `Assets/Scripts`, где сеттер свойства вызывает
`EndSyncProperty`: поле, которое этот сеттер пишет, обязано встречаться в `SerializeState` того же
класса (`nameof(_поле)`), либо стоять в списке исключений с причиной. Сканирование исходников —
как в `UxrUniqueIdOnDiskTests`/`NetworkAssetIdOnDiskTests`. До правки красный: `UxrActor._life`,
`UxrAvatar._showControllerHands`, `UxrGrabbableResizable`.

Плюс поведенческий тест на тот же класс, если EditMode поднимет `UxrComponent` без `Awake`
(см. `testing.md`, «Главная особенность EditMode»): актор A, `Life = 37` → `IUxrStateSave.SerializeState`
на `ChangesSinceBeginning` → чтение в свежий актор B → `B.Life == 37`. Не поднимется — хватит
сканирования и яруса C.

**Правило RPC (Mirror-сторона класса).** Реестр в тесте `RpcCarriesNoStateTests` (сделано так вместо
атрибута `[NetEvent]` — см. «Сделано»): каждый `[ClientRpc]`/`[TargetRpc]` сборки `VrBattlegrounds`
записан с причиной «почему это не состояние». Сегодня все RPC прошли ревью (таблица выше);
новый RPC без записи — красный тест, и автор обязан ответить себе, не состояние ли он везёт.
Правило — строкой в `CLAUDE.md`, «Жёсткие правила»: *«Состояние — `SyncVar`/`SyncList` или снимок
UltimateXR (`SerializeState`); RPC и событие-метод — только эффекты»*.

### 3. Цена

| | Сейчас | После А |
|---|---|---|
| Трафик на попадание | событие `Life` + событие эффекта | без изменений |
| Снимок при входе/смене карты | без хп | + одна запись на каждого актора с `Life ≠` значения из префаба: guid + имя поля + float, порядка 30–40 Б. 20 игроков — < 1 КБ, один раз на подключение |
| Правка SDK | — | 1 файл, ~20 строк, без изменения существующих файлов |

Для Quest и 20 игроков разница не измерима.

## Альтернативы

### Б. Хп как Mirror `SyncVar` на `PlayerController`

Сервер пишет `[SyncVar] _health` из `DamageReceived`/`Died`/`Respawn`/`RestoreHealth`; `Health`/`IsAlive`
читают его. Mirror сам отдаёт значение позднему клиенту.

- **Плюсы:** SDK не трогается; `SyncVar` дешевле блоба канала состояния (~4 Б + заголовок против
  guid и имени свойства в блобе) — экономия порядка 50–100 Б на попадание.
- **Риски:** второй источник того же факта. Именно этот вариант аудит уже отверг (NET-04, остаток):
  `Life` и `SyncVar` приходят разными каналами в разные моменты, и подписчик `PlayerDied`, читающий
  `IsAlive` в обработчике (`TeamSpawnZone`), увидит живого. Чтобы убрать двойственность, пришлось бы
  отключить `_automaticDamageHandling` и вести жизнь целиком в проекте — это переписывание
  урона/смерти/эффектов. **Класс не закрывает**: следующий синхронизируемый компонент UltimateXR без
  `SerializeState` повторит баг.
- **Когда выбирать:** если T-23/T-24 уведут урон из UltimateXR в собственный серверный код — тогда
  жизнь естественно станет `SyncVar`, и А отомрёт вместе с `UxrActor`.

### В. Позднему клиенту — полный снимок `UxrStateSaveLevel.Complete`

Не работает: `Complete` проходит тот же `SerializeState`, где `_life` нет. Упомянут, потому что
первым приходит в голову.

### Г. Прокси-компонент проекта `ActorLifeState : UxrComponent` рядом с `UxrActor`

Без правки SDK: свой `SerializeState` пишет `actor.Life`. Минус — должен стоять на каждом объекте
с `UxrActor` (аватары, будущие мишени); класс возвращается при первом забытом префабе, нужен ещё
тест состава префабов. Хуже А при той же цене.

**Рекомендация — А.** Дефект в SDK и лечится в SDK одной строкой в стиле самого SDK; инвариант
«синхронизируемое ⊂ снимка» проверяется сканированием и не зависит от префабов; трафик не меняется;
единый источник правды о жизни (решение NET-04) сохраняется.

## План

1. **Харнесс до правки.** Сценарий яруса C `late-join-state-parity` (ниже) и `StateSnapshotCoverageTests`.
   Оба прогнать и зафиксировать **красный** результат. Если сценарий зелёный до правки — гипотеза
   неверна или сценарий не воспроизводит триггер; разобраться, прежде чем править.
2. `UxrActor.StateSave.cs` + решение по `ShowControllerHands`/`UxrGrabbableResizable`; Патч 29 в `sdk-patches.md`.
3. Реестр RPC с причинами и тест на отражении (`RpcCarriesNoStateTests`).
4. Прогнать всё: ворота Android, EditMode, сценарий C — 3 прогона подряд зелёные.
5. Документация: `Docs/UltimateXR/known-issues.md` (новый Issue: «синхронизируется ≠ попадает в снимок»),
   `combat-networking.md` (путь попадания: что на проводе), `testing.md` (сценарий), правило в `CLAUDE.md`,
   `CHANGELOG.md`, находка NET-27 в `audit/network-audit-2026-08.md`, снять оговорку в T-33
   (`OverviewInput` про хп позднего клиента).

## Проверки

### Сценарий `late-join-state-parity` (ярус C, `-Clients 2`)

Сервер + `client-1` подключены с начала; `client-2` — поздний: сценарий сам откладывает его подключение
(или отключает и возвращает, как `session-recovery-on-reconnect`). Матч запущен, не разминка.

До входа `client-2` сервер детерминированно меняет мир: игроку A — настоящий урон до 37
(`UxrActor.ReceiveDamage`), игрок B — настоящая смерть (`DieInternal` через смертельный урон),
плюс контрольное состояние, которое заведомо едет снимком (патроны магазина, `IsGrabbable` слота).

Проверки на `client-2` (свой JSON-вердикт):

| Проверка | До правки (ожидание) |
|---|---|
| контроль: патроны/`IsGrabbable` совпадают с сервером | зелёная — снимок в целом работает |
| контроль: `client-1` видит A = 37, B мёртв | зелёная — события доезжают |
| `client-2`: `A.Health == 37` | **красная** (100) |
| `client-2`: `B.IsAlive == false` | **красная** |
| `client-2`, свой аватар (вошёл выбывшим): `IsAlive == !IsSpectating` | **красная**, если верна гипотеза о гонке |
| инвариант на всех машинах: для каждого аватара в бою `IsAlive == !IsSpectating` | красная у `client-2` |

Последняя строка — проверка **класса**, а не хп: два независимых канала одного факта обязаны сходиться
у любого получателя, в любой момент после входа.

### Уровень 1

- `StateSnapshotCoverageTests` — красный до правки (см. выше).
- `RpcCarriesNoStateTests` — красный на любом RPC без записи в реестре.

### Минимум по `CLAUDE.md`

`AndroidCompileGate.Run()` → PASS; `VrBattlegrounds.Tests.EditMode` → 0 failed.

## Границы

- Не переводить урон на `SyncVar` и не отключать `_automaticDamageHandling` (это вариант Б, отдельное решение).
- Не трогать порядок `SendConnectMessage` / запроса снимка: при полном снимке отброшенные до него события
  безвредны, менять сетевой рукопожатие незачем.
- Не менять путь выстрела и симуляцию снарядов — это T-23/T-24.
- Правка SDK — только новый partial-файл и строки `SerializeState`; прочие отладочные `Debug.Log` в `UxrActor.cs`
  (5 штук, запрещены правилами проекта) — отдельная уборка, не здесь.

## Открытые вопросы

- Показывать ли позднему клиенту победителя текущего раунда (фаза «конец раунда»): сегодня он только в
  `RpcOnRoundEnded`. Нужен ли — решит экран «Обзор» (T-33); если да — `SyncVar` в `EliminationMode`.
- Отправка клиентом изменений в окне смены сцены молча отбрасывается (`NetworkStateRelay.Send`, «клиент ещё
  не готов»). Это тот же класс в обратную сторону (событие автора теряется без снимка), но автор — клиент,
  и сервер его снимка не знает. Сегодня в это окно игрок ничего не держит; при появлении — отдельная задача.
