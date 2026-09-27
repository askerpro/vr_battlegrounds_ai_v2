# UltimateXR SDK — Патчи и отступления от оригинала

Этот файл документирует **все изменения**, внесённые в код `Assets/ThirdParty/UltimateXR/`.  
При обновлении SDK необходимо **повторно применить** эти патчи вручную.

---

## Патч 1: UxrMirrorAvatar — канал состояния вынесен на объект уровня сессии

**Файл:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Networking/Integrations/Net/Mirror/UxrMirrorAvatar.cs`
**Дата:** 2026-08-18 (задача T-12, заменяет предыдущую редакцию патча от 2025)
**Парный код проекта:** `Assets/Scripts/Network/NetworkStateRelay.cs`

### Проблема

В SDK транспорт канала состояния UltimateXR (сериализованные `byte[]`-блобы, которыми
едут захваты предметов, состояние оружия и **здоровье со смертью**) живёт прямо
в `UxrMirrorAvatar` — компоненте на аватаре. А аватар в этом проекте пересоздаётся
при смене скина, смене команды и **каждой смене карты**. Держался канал на двух
статических полях:

```csharp
private static bool           _initialStateLoaded;   // получен ли начальный снимок сцены
private static UxrMirrorAvatar _serverBroadcaster;   // кто на сервере вещает события
```

Отсюда три отказа сразу:

| | Что ломалось |
|---|---|
| **NET-02** | `AvatarManager.ChangeAvatar` спавнит новый аватар, потом уничтожает старый. Новый в `OnStartServer` видит непустое `_serverBroadcaster` и вещателем не становится, старый в `OnStopServer` поле обнуляет — вещателя не остаётся. Здоровье и смерть перестают доезжать до клиентов. |
| **NET-01** | `_initialStateLoaded` общий на процесс: один клиент влиял на решение другого, применять ли пришедшее событие. |
| **NET-03** | Подписка на `UxrManager.ComponentStateChanged` стояла и в `OnStartServer`, и в `OnStartLocalPlayer`. На хосте отрабатывали обе — каждое изменение уходило клиентам двумя Rpc, а отписка была одна. |

Отдельно вскрылось прогоном яруса C (см. NET-15 в
[`../audit/network-audit-2026-08.md`](../audit/network-audit-2026-08.md)): весь приём
состояния на клиентах был мёртв. `_initialStateLoaded` выставлялся только
в `OnStartLocalPlayer`, а этот колбэк Mirror зовёт лишь на объекте игрока
(`NetworkServer.AddPlayerForConnection`). В проекте объект игрока — `PlayerSession`,
не аватар, поэтому `OnStartLocalPlayer` у `UxrMirrorAvatar` не вызывался никогда,
флаг оставался `false`, и `RpcComponentStateChanged` выходил по нему в самом начале.

### Прошлая редакция патча и почему её больше нет

Патч 2025 года лечил ту же боль иначе: делал `_initialStateLoaded` полем экземпляра
и добавлял `OnStartServer` с `InitializeNetworkAvatar`. Первая половина **откатилась**
(на момент аудита 2026-08 поле снова было `static`, а рядом появилось второе
статическое поле, которого в документе не было вовсе). Это и есть причина, по которой
лечение симптома заменено на перенос: пока канал живёт в аватаре, любая правка держится
на честном слове.

### Применённые изменения

Из `UxrMirrorAvatar` **удалено** — целиком переехало в `NetworkStateRelay`:

- поля `_initialStateLoaded` и `_serverBroadcaster` (в классе не осталось ни одного
  статического поля);
- обработчик `UxrManager_ComponentStateChanged` и обе подписки на
  `UxrManager.ComponentStateChanged` (в `OnStartServer` и в `OnStartLocalPlayer`),
  а также отписки в `OnStopServer`, `OnStopClient`, `OnDestroy`;
- `CmdComponentStateChanged`, `RpcComponentStateChanged` — транспорт событий;
- `CmdNewAvatarJoined`, `TargetLoadGlobalState`, `RpcLoadAvatarState` — начальная
  синхронизация состояния сцены;
- `OnStopServer` — после выноса в нём не осталось ничего своего;
- `OnNetworkSceneChanged` — публичный метод, который только сбрасывал
  `_initialStateLoaded`; его никто не вызывал (NET-11), а сбрасывать стало нечего.

В `UxrMirrorAvatar` **осталось** только то, что действительно про аватар:
`InitializeNetworkAvatar` с `CombineUniqueId` и флагом `_avatarInitialized`,
`OnStartServer` / `OnStartClient` / `OnStartLocalPlayer` (инициализация и ownership),
`OnStopClient` и `OnDestroy` с событиями `AvatarSpawned` / `AvatarDespawned`,
`RequestAuthority` + `CmdRequestAuthority`.

Наверху класса стоит `<remarks>` с указанием, куда переехал канал.

Пункты 2 и 3 прошлой редакции патча (инициализация GUID в `OnStartServer` и защита
`InitializeNetworkAvatar` от двойного вызова) **сохранены** — они про аватар и по-прежнему
нужны выделенному серверу.

### Куда переехало

`VrBattlegrounds.Network.NetworkStateRelay` — `NetworkBehaviour` на префабе
`Assets/Prefabs/Managers/SessionContext.prefab`, рядом с `SessionManager`. Префаб
спавнится один раз в `GameNetworkManager.OnStartServer` и живёт до остановки сервера,
поэтому канал больше не связан со временем жизни аватара. Экземпляр ровно один
на процесс — состояние канала хранится обычными полями, статики в релее нет.

Начальный снимок сцены релей запрашивает сам: в `OnStartClient` и заново на каждый
`GameNetworkManager.ClientSceneChanged` (после смены сцены прежние объекты уничтожены).

### Как повторить при обновлении SDK

1. Открыть `UxrMirrorAvatar.cs` и удалить из него весь канал состояния: оба статических
   поля, обработчик `UxrManager_ComponentStateChanged` со всеми подписками и отписками,
   `CmdComponentStateChanged`, `RpcComponentStateChanged`, `CmdNewAvatarJoined`,
   `TargetLoadGlobalState`, `RpcLoadAvatarState`, `OnNetworkSceneChanged`.
2. Оставить `InitializeNetworkAvatar` (с `CombineUniqueId` и `_avatarInitialized`),
   `OnStartServer` с вызовом `InitializeNetworkAvatar`, `OnStartClient`,
   `OnStartLocalPlayer` без работы с каналом, `OnStopClient`, `OnDestroy`,
   `RequestAuthority` / `CmdRequestAuthority`.
3. Проверить, что в классе не осталось ни одного статического поля.
4. Убедиться, что `NetworkStateRelay` висит на `SessionContext.prefab`.

Первые три пункта закрыты EditMode-тестами
`Assets/Tests/EditMode/Network/NetworkStateRelayTests.cs`, четвёртый — там же.
Поведение целиком проверяет ярус C: сценарий `avatar-swap-death-replication`
(`powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-E2E.ps1 -Scenario avatar-swap-death-replication`).

### Режимы работы после патча

| Режим | Статус |
|---|---|
| Dedicated Server + 2 клиента | проверено ярусом C: смерть доезжает и до, и после смены аватара |
| Host + Client (один процесс) | подписка ровно одна (EditMode, ярус B) |

---

## Патч 2: Система оружия и событий смерти (Расширяемость)

**Файлы:** 
- `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrActor.cs` (+ `.Custom.cs`)
- `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrWeapon.cs` (+ `.Custom.cs`)
- `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrWeaponManager.cs` (+ `.Custom.cs`)
- `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrFirearmWeapon.cs`

**Дата:** 2026-03-09

### Проблема

1. **Отсутствие события смерти**: В оригинальном `UxrActor` нет публичного события, которое уведомляло бы другие системы о гибели актора (только через `DamageReceived` с проверкой флага `Dies`).
2. **Жесткая логика стрельбы**: Нет возможности глобально запретить стрельбу (например, во время отсчета раунда) без деактивации объектов.
3. **Сложность обновления SDK**: Изменения в коде SDK затираются при обновлении.

### Применённые изменения

Для решения проблем и упрощения будущих обновлений, классы были расширены через механизм `partial class`.

1. **UxrActor (Событие смерти и разрушение)**:
   - В основном файле класс помечен как `partial`.
   - Добавлено поле `_autoDestroyOnDie`, позволяющее отключать автоматическое удаление объекта при смерти.
   - В `DieInternal()` добавлен вызов `Died?.Invoke(this);` и проверка `_autoDestroyOnDie`.
   - Свойства `Died` и `AutoDestroyOnDie` вынесены в `UxrActor.Custom.cs`.

2. **UxrWeaponManager (Глобальный контроль)**:
   - В `UxrWeaponManager.Custom.cs` добавлен:
     - `bool WeaponSystemEnabled` — флаг полного отключения боевых расчётов.
   - В основном файле в `UpdateManager()` добавлен ранний выход, если система отключена.

3. **UxrFirearmWeapon (Enforcement)**:
   - В `TryToShootRound()` добавлена проверка `if (!CanUse) return false;`.

4. **UxrGrabber (Валидация захвата)**:
   - Регистрация хука `public Func<UxrGrabbableObject, int, bool> CanGrabDelegate` вынесена в `UxrGrabber.Custom.cs`.

5. **UxrGrabbableObject (Проверка возможности захвата)**:
   - В основном файле в `CanBeGrabbedByGrabber()` добавлена проверка `grabber.CanGrabDelegate`.

### Как повторить при обновлении SDK

1. Пометить `UxrActor` как `partial`.
2. Добавить вызов `Died?.Invoke(this);` в `UxrActor.DieInternal()`.
3. Добавить `if (!WeaponSystemEnabled) return;` в начало `UxrWeaponManager.UpdateManager()`.
4. Добавить `if (!CanUse) return false;` в начало `UxrFirearmWeapon.TryToShootRound()`.
5. Добавить проверку `grabber.CanGrabDelegate` в начало `UxrGrabbableObject.CanBeGrabbedByGrabber()`.
6. Убедиться, что файлы `.Custom.cs` присутствуют в папках рядом с оригиналами.

---

## Патч 3: Интегрированная система свапа предметов (Slot Swapping)

**Файлы:**
- `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabbableObjectAnchor.cs`
- `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabbableObject.cs`
- `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabManager.Manipulation.cs`
- `Assets/ThirdParty/UltimateXR/Editor/Manipulation/UxrGrabbableObjectAnchorEditor.cs`

**Дата:** 2026-03-15

### Проблема

В оригинальном SDK нет нативной возможности заменить предмет в анчере, просто поднеся к нему другой предмет (Slot Swapping). Анчер либо занят, либо свободен. Это вынуждало писать внешние скрипты-костыли, которые работали неэффективно (через `Update`) и плохо синхронизировались по сети.

### Применённые изменения

1. **UxrGrabbableObjectAnchor**: Добавлено поле `_allowSwap` и свойство `AllowSwap`. Если включено, анчер разрешает "перехват" слота новым предметом.
2. **UxrGrabbableObject**: Метод `CanBePlacedOnAnchor` теперь учитывает флаг `AllowSwap` при проверке занятости слота.
3.- **UxrGrabManager.Manipulation.cs (RemoveObjectFromAnchor):** Теперь принимает `bool unparent = false`.

### 4. Нативная поддержка Grab Proxy (Redirection)
**Файлы:**
- `UxrGrabbableObject.cs`
- `UxrGrabbableObjectAnchor.cs`
- `UxrGrabManager.Manipulation.cs`
- `UxrGrabbableObjectAnchorEditor.cs`

**Описание:** Логика "плечевого кармана" теперь встроена в SDK. Анчер может иметь ссылку на `GrabProxy`. При попытке схватить прокси, менеджер автоматически перенаправляет захват на предмет в анчере.
- **UxrGrabbableObject:** Добавлено свойство `ProxyForAnchor`.
- **UxrGrabbableObjectAnchor:** Добавлено поле `Grab Proxy`. Анчер управляет состоянием `IsGrabbable` у прокси (включает, только если в анчере есть предмет).
- **UxrGrabManager:** Переадресация происходит внутри `GrabObject`, что исключает дублирование событий.

### 5. Безопасное взаимодействие с UxrReturnGrabbableObject
**Файлы:**
- `UxrReturnGrabbableObject.cs`
- `UxrGrabManager.Manipulation.cs`

**Описание:** Решена проблема, когда выброшенный при свапе предмет пытался вернуться в уже занятый слот.
- **UxrReturnGrabbableObject:** Добавлен метод `ClearLastAnchor()` для сброса данных о последнем анчере и отмены запланированного возврата.
- **UxrGrabManager:** При выполнении "ежекта" (выброса из слота), менеджер теперь автоматически вызывает `ClearReturnData()` у предмета, отвязывая его от текущего слота.
 Позволяет принудительно отцепить предмет от иерархии игрока (world-space ejection), вместо стандартного поведения "оставаться у родителя анчера".
   - Метод `PlaceObject` теперь автоматически выбрасывает старый предмет, если `AllowSwap` включен. Логика встроена внутрь `BeginSync` для гарантированной сетевой синхронизации (транзакционность выброса и установки).
4. **Editor**: Кастомный инспектор `UxrGrabbableObjectAnchorEditor` обновлен для отображения галочки "Allow Swap".

### Как повторить при обновлении SDK

1. В `UxrGrabbableObjectAnchor` добавить `[SerializeField] bool _allowSwap` и публичное свойство.
2. В `UxrGrabbableObject.CanBePlacedOnAnchor` изменить проверку занятости: `if (anchor.CurrentPlacedObject != null && !anchor.AllowSwap)`.
3. В `UxrGrabManager.Manipulation.cs`:
   - Обновить сигнатуру `RemoveObjectFromAnchor`, добавить `unparent` в логику смены родителя (строка ~412) и в `EndSyncMethod`.
   - В `PlaceObject` добавить блок выброса старого предмета `RemoveObjectFromAnchor(ejected, propagateEvents, true)` сразу после `BeginSync`.
4. Обновить `UxrGrabbableObjectAnchorEditor.cs`, чтобы рисовать новое поле через `EditorGUILayout.PropertyField`.

---

## Патч 4: Глобальное смещение высоты трекинга (Global Height Offset)

**Файлы:**
- `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Devices/UxrControllerTracking.cs`

**Дата:** 2026-03-22

### Проблема

В UltimateXR при телепортации аватар всегда сбрасывается на высоту Y=0 (уровень виртуального пола). Если игрок ниже или выше стандартного роста, и необходимо откалибровать высоту, нельзя просто сдвинуть `UxrAvatar` (т.к. телепортация это сбросит). Приходится двигать внутренние компоненты: `CameraController` и руки.
Однако компоненты рук жестко привязаны к локальным координатам сенсоров шлема. Необходимо было добавить способ применять глобальный оффсет к считываемым данным трекеров без написания костыльных драйверов (типа `TrackedPoseDriverExt`), которые бы ломали нативную локомоцию.

### Применённые изменения

1. **UxrControllerTracking**: 
   - Добавлено статическое свойство `GlobalHeightOffset`.
   - Свойства `SensorLeftPos` и `SensorRightPos` (возвращающие мировые координаты сенсоров) изменены: теперь они прибавляют `GlobalHeightOffset` к оси Y локальной позиции сенсора, перед тем как перевести её в мировые координаты (`Avatar.transform.TransformPoint`).

### Как связать с игрой
В проекте используется `PhysicalSpaceSyncManager.cs`, который при калибровке сдвигает `CameraController.localPosition.y` и одновременно записывает смещение в `UltimateXR.Devices.UxrControllerTracking.GlobalHeightOffset`.

### Как повторить при обновлении SDK
1. В `UxrControllerTracking` добавить `public static float GlobalHeightOffset { get; set; } = 0f;`
2. Изменить геттеры для `SensorLeftPos` и `SensorRightPos`:
```csharp
   Vector3 pos = LocalAvatarLeftHandSensorPos; // или Right
   pos.y += GlobalHeightOffset;
   return Avatar.transform.TransformPoint(pos);
```

---

## Патч 5: Chambered-state для Semi/Full Auto (опционально)

**Файлы:**
- `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrFirearmTrigger.cs`
- `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrFirearmWeapon.cs`

**Дата:** 2026-04-19

### Проблема

В базовом SDK логика `HasReloaded` (патрон в патроннике) полноценно используется для `ManualReload`,
а для `SemiAutomatic`/`FullyAutomatic` поведение не позволяло опционально требовать досылание после
смены магазина.

### Применённые изменения

1. **UxrFirearmTrigger**
    - Добавлено сериализуемое поле `_useHasReloadedForSemiAndFullAuto`.
    - Добавлено публичное свойство `UseHasReloadedForSemiAndFullAuto`.

2. **UxrFirearmWeapon**
    - `Reload(int triggerIndex)` вынесен на общий путь через `SetTriggerHasReloadedSynced(int, bool)`.
    - В ветках `SemiAutomatic` и `FullyAutomatic` добавлен опциональный учет `runtimeTrigger.HasReloaded`
       при включенном `UseHasReloadedForSemiAndFullAuto`.
    - В `MagTarget_Removed` и `MagTarget_Placed` при включенном флаге патронник сбрасывается:
       `SetTriggerHasReloadedSynced(i, false)`.
    - Звук "пустого" триггера (`ShotAudioNoAmmo`) теперь также проигрывается при состоянии "магазин есть,
       но патрон не дослан" (для этого режима).

### Практический результат

- Для оружия с `SemiAutomatic`/`FullyAutomatic` можно включить реалистичную механику:
   после установки/смены магазина требуется ручной cycle затвора (внешним скриптом, например через
   вызов `Reload(triggerIndex)` на возврате затвора).

### Как повторить при обновлении SDK

1. В `UxrFirearmTrigger` добавить:
```csharp
[SerializeField] private bool _useHasReloadedForSemiAndFullAuto;
public bool UseHasReloadedForSemiAndFullAuto => _useHasReloadedForSemiAndFullAuto;
```

2. В `UxrFirearmWeapon`:
    - Добавить `SetTriggerHasReloadedSynced(int triggerIndex, bool hasReloaded)` и использовать его из `Reload()`.
      `EndSyncMethod` обязан получить **оба** аргумента: `new object[] { triggerIndex, hasReloaded }`.
      До 2026-09-27 передавался только `triggerIndex`, и принимающая сторона падала с
      `TargetParameterCountException` на каждом досыле/снятии магазина.
    - В `UxrManager_AvatarsUpdated()` для `SemiAutomatic`/`FullyAutomatic` использовать `HasReloaded` условно,
       по `trigger.UseHasReloadedForSemiAndFullAuto`.
    - В `MagTarget_Removed()` и `MagTarget_Placed()` сбрасывать `HasReloaded` для соответствующего trigger
       при включенном флаге.
    - Обновить условие проигрывания `ShotAudioNoAmmo`, чтобы учитывать состояние "не дослан патрон".

---

## Патч 6: UxrGrabbableObject.Custom — публичный доступ к стартовому якорю

**Файл:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabbableObject.Custom.cs`
**Дата:** 2026-08-22 (находки NET-16 и NET-17)
**Парный код проекта:** `Assets/Scripts/Network/NetworkUxrIdentity.cs`,
`Assets/Scripts/Arsenal/ArsenalSlotController.cs`

### Проблема

Объект UltimateXR, созданный в рантайме и заспавненный Mirror, обязан родиться одинаково
на всех машинах. В оригинальном SDK этому мешают две приватные вещи.

1. **`_autoCreateStartAnchor`** — сериализованный флаг, по которому `Awake` создаёт объекту
   родителя-якорь «`<имя>` Auto Anchor». Якорь создаётся **в рантайме**, его `UniqueId`
   на каждой машине свой, а событие захвата ссылается на него полем `_grabbableAnchor`.
   На принимающей стороне ссылка не разрешается, и всё событие отвергается с
   `UxrComponentNotFoundException`. Публичного способа погасить флаг нет; проект гасил его
   рефлексией из `ArsenalWallController` — молчаливая зависимость, ломающаяся при
   переименовании поля.

2. **`CurrentAnchor` и `UxrGrabbableObjectAnchor.CurrentPlacedObject`** объявлены с
   `internal set`, то есть недоступны из сборки игры. Сетевая выдача предмета
   (`ArsenalSlotController.AssignNetworkItem`) перепарентит уже заспавненный объект под якорь
   слота, но учёт UltimateXR при этом оставался пустым — и при захвате `UxrGrabManager`
   не поднимал у якоря событие `Removed`. Слот арсенала не узнавал, что оружие унесли
   (NET-17). Публичный `UxrGrabManager.PlaceObject` для этого не годится: он обёрнут
   в `BeginSync` и породил бы сетевое событие на каждую выдачу — на сервере веерную
   рассылку 64 событий, а у клиента ещё и команду серверу «положи предмет»,
   то есть подмену авторитета.

### Применённые изменения

Файл `UxrGrabbableObject.Custom.cs` уже существовал (частичный класс из **Патча 2**).
В него добавлено:

```csharp
public bool AutoCreateStartAnchor
{
    get => _autoCreateStartAnchor;
    set => _autoCreateStartAnchor = value;
}

public void SetNetworkAnchor(UxrGrabbableObjectAnchor anchor)
{
    if (CurrentAnchor == anchor) return;
    if (CurrentAnchor != null && CurrentAnchor.CurrentPlacedObject == this)
        CurrentAnchor.CurrentPlacedObject = null;
    CurrentAnchor = anchor;
    if (anchor != null) anchor.CurrentPlacedObject = this;
}
```

Оригинальные файлы SDK **не менялись**: `internal set` доступен, потому что частичный класс
компилируется в ту же сборку `UltimateXR`.

Рефлексия по `_autoCreateStartAnchor` из `ArsenalWallController` **удалена** — гасит флаг
теперь `NetworkUxrIdentity.CreateInstance` через новое свойство, и не у одного корневого
компонента, а у всех в иерархии.

### Как повторить при обновлении SDK

1. Восстановить файл `UxrGrabbableObject.Custom.cs` с обоими членами.
2. Проверить, что поля `_autoCreateStartAnchor`, `_currentAnchor` и `_currentPlacedObject`
   не переименованы — иначе правка не скомпилируется, и это **хорошо**: отказ громкий.
3. Прогнать EditMode-набор `NetworkUxrIdentityTests` и сценарий яруса C `arsenal-item-grab`.

---

## Патч 7: NotifyOnValidate не выдаёт id в виртуальном игроке MPPM

**Файл:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/Unique/UxrUniqueIdImplementer_1.cs`,
метод `NotifyOnValidate`
**Дата:** 2026-09-27

### Проблема

`NotifyOnValidate` при загрузке компонента в редакторе сверяет сохранённые `__isInPrefab` /
`__prefabGuid` с фактическими и при расхождении выдаёт **новый случайный** `_uxrUniqueId`.
В проекте расходятся 1389 из 1943 UXR-компонентов в префабах: аватары — варианты `PlayerBase`
и наследуют его `__prefabGuid` ([known-issues #11](known-issues.md)).

Виртуальный игрок Multiplayer Play Mode (клон) грузит ассеты сам и перевыдаёт им id у себя
в памяти, а сохранить не может. У хоста id из файла, у клиента — случайные, и канал состояния
отвергает события в обе стороны: `IsGrabbable` прокси карманов, `SetAvatarRenderMode`,
пропуски в начальном снимке (`LoadStateChanges … Cannot deserialize`). Замер: id хоста
в точности `Combine(id с диска, netId)`, id клиента не выводятся ни из одного префаба;
с отключённым `NotifyOnValidate` ошибки пропадают, с включённым — возвращаются.

### Применённое изменение

В условие входа добавлено `Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor`
(модуль `UnityEngine.MultiplayerModule`, дополнительных ссылок asmdef не нужно).
Основной редактор выдаёт и сохраняет id как раньше.

### Как повторить при обновлении SDK

1. В `NotifyOnValidate` добавить `CurrentPlayer.IsMainEditor` к условию с `AutomaticIdGenerationPrefs`.
2. Проверить хост + клиент MPPM: на хосте нет `UxrComponentNotFoundException`, на клиенте нет
   `LoadStateChanges(): Cannot deserialize a component`.

### Дополнение 2026-09-27: процесс-импортёр

`CurrentPlayer.IsMainEditor` в процессе-импортёре (`[Worker1]`) бросает `NullReferenceException`
изнутри `SystemDataStore.GetMain()` — на каждом `OnValidate` каждого UXR-компонента при
фоновом импорте. Перед ним в условие добавлено `!AssetDatabase.IsAssetImportWorkerProcess()`:
импортёр ассеты не сохраняет, выдавать id ему незачем.

### Чего патч не чинит

Несогласованные флаги в самих префабах остаются, и основной редактор при **реимпорте** такого
префаба (правка его самого, базы, вложенного) выдаёт компонентам новые случайные id — только
в памяти, на диск они не попадают. Хост MPPM после этого живёт со случайными id, а клон — с
файловыми: это MPPM-02 (troubleshooting). Лечится не патчем, а данными: флаги на диске
приводятся к фактическим инструментом `Tools/VR Battlegrounds/VersionControl/Persist UltimateXR
Unique Ids`, сторож — `UxrUniqueIdOnDiskTests`.

---

## Патч 8: ссылка на компонент в событии называет предмет

**Файлы:**
- `Runtime/Scripts/Extensions/System/IO/BinaryWriterExt.cs` — `WriteUniqueComponent`, константы меток;
- `Runtime/Scripts/Extensions/System/IO/BinaryReaderExt.cs` — `ReadUniqueComponent`;
- `Runtime/Scripts/Exceptions/UxrComponentNotFoundException.cs` — `FormatMessage`;
- `Runtime/Scripts/Core/Unique/UxrUniqueIdDebugInfo.cs` — **новый файл проекта**, в SDK его нет.

**Дата:** 2026-09-27

### Проблема

Ссылка на `IUxrUniqueId` в событии состояния — флаг и `Guid`. Если принимающая сторона id
не находит, `UxrComponentNotFoundException` сообщает только «Id is ae118e67-…»: что это за
предмет, она знать не может — компонента у неё нет. Опознание сводилось к перебору
`Combine(id префаба, netId)` по всем префабам.

Заодно в `FormatMessage` было перевёрнуто условие: переданный текст терялся, а без текста
сообщение начиналось с «`: Could not find…`».

### Применённое изменение

Флаг `bool` заменён байт-меткой: `0` — null, `1` — id (байт-в-байт прежний `Write(true)`),
`2` — id и строка `Путь/В/Иерархии [Тип]` (не длиннее 160 символов). Читатель понимает все
три метки и дочитывает строку всегда, даже если компонент нашёлся. Не найден — строка
уходит в исключение: «… Id is ae118e67-…. Sender: ArsenalWall (2)/…/Shotgun(Clone) [UxrGrabbableObject]».

Метку `2` пишет машина с `UxrUniqueIdDebugInfo.IncludeInSerialization` — по умолчанию
редактор и development-сборка; release пишет прежний формат и лишнего трафика не несёт.
Флаг можно включать выборочно: читатель у всех один. Условие одно — у всех участников
этот патч; сборка без него прочтёт метку `2` как `true` и съедет по потоку (но сборки
разных версий проекта и так несовместимы по id).

Проверка — `UniqueComponentDebugInfoTests` (до патча красный: сообщение без имени предмета).

### Как повторить при обновлении SDK

1. Вернуть `UxrUniqueIdDebugInfo.cs` в `Core/Unique/`.
2. В `WriteUniqueComponent` писать байт-метку и, при метке `2`, `UxrUniqueIdDebugInfo.Describe(component)`.
3. В `ReadUniqueComponent` читать байт-метку, строку при метке `2`, передавать её в исключение.
4. В `UxrComponentNotFoundException.FormatMessage` дописывать переданный текст в конец.
5. Прогнать `UniqueComponentDebugInfoTests` и сетевые тесты.

---

## Патч 9: деспавн сетевого аватара не рассылает уничтожение

**Файлы:**
- `Runtime/Scripts/Networking/Integrations/Net/Mirror/UxrMirrorAvatar.cs` — `OnDestroy`;
- `Runtime/Scripts/Core/Instantiation/UxrInstanceManager.cs` — `NotifyNetworkDespawnInternal`.

**Дата:** 2026-09-27

### Проблема

`OnDestroy` (добавлен проектом в `d215b77`, в исходном SDK его нет) снимал аватар с учёта
`UxrInstanceManager` через `DestroyGameObject`. Тот — синхронизируемый метод: вызов
`DestroyGameObjectInternal` уходит по сети. Но аватар уничтожает Mirror на каждой машине
сам, и на другой стороне его уже нет — при каждой смене скина и выходе игрока в логе
`Error deserializing invoked method DestroyGameObjectInternal()` с
`UxrComponentNotFoundException … Sender: Player_… (Local) [UxrAvatar]`.

### Применённое изменение

1. `OnDestroy` зовёт `NotifyNetworkDespawn` — локально, без сети (так же поступает
   интеграция FishNet).
2. `NotifyNetworkDespawnInternal` снимает объект с учёта (`_currentInstancedPrefabs`,
   `_currentInstances`) всегда, а не только при `destroy`. Без этого деспавненный аватар
   оставался бы в учёте и уходил в начальный снимок состояния следующему клиенту — ровно то,
   ради чего в `d215b77` и звали `DestroyGameObject`.

### Как повторить при обновлении SDK

1. В `UxrMirrorAvatar.OnDestroy` заменить `DestroyGameObject(Avatar.gameObject)` на
   `NotifyNetworkDespawn(Avatar.gameObject)` с проверкой `Avatar != null`.
2. В `UxrInstanceManager.NotifyNetworkDespawnInternal` вынести два `Remove` из-под `if (destroy)`.
3. Хост + клиент MPPM, сменить скин: в логах нет `DestroyGameObjectInternal`.

---

## Патч 10: префаб-ассет не получает новый id при неверных флагах

**Файл:** `Runtime/Scripts/Core/Unique/UxrUniqueIdImplementer_1.cs`, метод `NotifyOnValidate`
**Дата:** 2026-09-27

### Проблема

При расхождении `__isInPrefab` / `__prefabGuid` с фактическими `NotifyOnValidate` выдаёт
компоненту новый случайный `_uxrUniqueId` — задумано для экземпляров на сцене, чтобы два
экземпляра одного префаба не делили id. Но срабатывало и на самом префабе-ассете: неверные
флаги туда заносят Apply to Prefab со сцены, `SaveAsPrefabAsset`, наследование варианта от
базы. Новый id жил только в памяти редактора, на диск не попадал — хост MPPM расходился с
клоном и сборками, которые читают файл (MPPM-02, [known-issues #11](known-issues.md)).

### Применённое изменение

В ветке расхождения флагов: если компонент в префабе-ассете (`IsInPrefab()`) или в
изолированной сцене `LoadPrefabContents` (`EditorSceneManager.IsPreviewScene`), флаги
исправляются, а id остаётся. Экземпляру в сцене id перевыдаётся, как раньше.

Сетевой уникальности это не вредит: у сетевых объектов id совмещается с `netId`
(`CombineUniqueId`). Цена — копия префаба-ассета (Ctrl+D) и вариант делят id компонентов
с оригиналом; экземпляры в сцене всё равно получают свои.

Флаги исправляются только в памяти (как и раньше, без сохранения) — на диск их пишет
постпроцессор `UxrUniqueIdPersister`. Он остаётся нужен: без верных флагов на диске новый
экземпляр в сцене, поставленный после Apply, унаследовал бы id первого экземпляра.

### Как повторить при обновлении SDK

1. В `NotifyOnValidate`, в ветке `IsInPrefab() != refIsInPrefab || prefabGuid != refPrefabGuid`,
   вызывать `assignId` только если компонент не в префабе-ассете и не в preview-сцене.
2. Прогнать `UxrUniqueIdStabilityTests`: без патча красный `Ассет_с_неверными_флагами_сохраняет_id`,
   `Экземпляр_на_сцене_получает_свой_id` зелёный в обоих случаях.

---

## Патч 11: точка хвата рядом с занятой другой рукой не штрафуется

**Файл:** `Runtime/Scripts/Manipulation/UxrGrabbableObject.cs`, метод `GetDistanceFromGrabber`
**Дата:** 2026-09-27

### Проблема

Держу `Gun_real` правой рукой за основную точку, подношу левую к дополнительной — пистолет
перескакивает в левую руку. `GetDistanceFromGrabber` прибавлял 100000 к расстоянию до точки,
если место ладони на ней ближе `MinHandGrabInterDistance` (половина ширины ладони + 1 см,
5 см) к месту ладони на **другой** занятой точке, при условии, что у обеих режим привязки
`PositionAndRotation`. У `Gun_real` для MEF эти места разнесены на 2 см: хват поддержки
по определению обнимает основную руку. Дополнительная точка становилась недосягаемой,
ближайшей оказывалась занятая основная, а её захват SDK трактует как передачу из руки в руку.
Разбор — [known-issues, Issue 13](known-issues.md).

Развести места ладоней нельзя: штраф считается по `GripAlignTransform`, а это и есть
видимое положение руки. `GrabProximityTransform` влияет только на дотягивание.

### Применённое изменение

Ветка `else if` со штрафом за близость к другой занятой точке удалена, на её месте
комментарий `VR Battlegrounds patch 11`. Штраф для второй руки на той же фигуре хвата
(`UxrGrabPointShape`, ветка выше) остаётся: там точка одна, и руки иначе сядут друг в друга.

Цена: у предметов, где несколько точек — варианты хвата **одной** рукой в одном месте
(в проекте такое только у сэмплов UltimateXR), вторая рука может сесть почти туда же,
где первая. Игровых предметов с такой разметкой нет.

### Как повторить при обновлении SDK

1. В `GetDistanceFromGrabber`, в цикле «Do not allow to grab if there is a hand grabbing
   another grabPoint nearby», удалить ветку `else if (Vector3.Distance(GetGrabPointGrabAlignTransform(...
   grabPoint ...), GetGrabPointGrabAlignTransform(... otherGrabbedPoint ...)) <= MinHandGrabInterDistance)`.
2. Прогнать `GunTwoHandGrabTests`: без патча красные оба теста, с патчем — зелёные.

---

## Патч 12: захват уничтоженной руки обрывает телепорт — чёрный экран

**Файл:** `Runtime/Scripts/Manipulation/UxrGrabManager.cs` — методы `UxrAvatar_GlobalAvatarMoved`,
`InitializeManipulationFrame`, `OnDestroy`, новый `RemoveOrphanedManipulations`
**Дата:** 2026-09-27
**Парный код проекта:** `Assets/Scripts/Player/Avatars/AvatarTeardown.cs`

### Проблема

Рука (`UxrGrabber`), уничтоженная с предметом, ничего не отпускает: её `OnDisable`/`OnDestroy`
зовёт `ReleaseObject`, но рука к этому моменту уже не в `EnabledComponents`, и отпускание
молча пропускается. В `_currentManipulations` остаётся запись с Unity-null рукой. Разбор —
[known-issues, Issue 17](known-issues.md).

Первое же перемещение любого аватара (`UxrAvatar_GlobalAvatarMoved`) бросает
`MissingReferenceException` на `g.Avatar` мёртвой руки. Телепорт и поворот с затемнением
к этому моменту уже погасили экран — корутина обрывается, и `UxrCameraFade` остаётся чёрным
навсегда (`IsFading=True`, `_fadeTimer=-1`). В проекте так было после смены скина с оружием
в руках.

Попутно: в `OnDestroy` менеджера отписка `UxrGrabbableObject.GlobalDisabled` была написана
как `+=` — повторная подписка вместо отписки.

### Применённое изменение

- Новый приватный `RemoveOrphanedManipulations()`: из записей с живым предметом удаляет
  захваты (`RuntimeGrabInfo`) с Unity-null рукой; запись удаляется целиком, если уничтожен
  предмет или рук не осталось. Событий не шлёт — отпускать от имени несуществующей руки некому.
  Пишет `LogWarning` (уровень `LogLevelManipulation >= Warnings`) — в норме срабатывать не должен.
- Вызов `RemoveOrphanedManipulations()` в начале `InitializeManipulationFrame` (каждый кадр,
  до всех обходов) и в `UxrAvatar_GlobalAvatarMoved` после проверки аватара — перед обходом
  `_currentManipulations`.
- `OnDestroy`: `UxrGrabbableObject.GlobalDisabled -= GrabbableObject_Disabled` вместо `+=`.

Все места помечены `VR Battlegrounds patch 12`. Живую руку патч не трогает: предмет, который
держат две руки, одна из которых уничтожена, остаётся у второй.

Основное исправление — в игре (`AvatarTeardown`): сервер отпускает руки до уничтожения аватара,
и отпускание расходится по сети. Патч — страховка на любой другой путь.

### Как повторить при обновлении SDK

1. Добавить в `UxrGrabManager` метод `RemoveOrphanedManipulations` (см. текущую версию файла)
   и вызвать его первой строкой `InitializeManipulationFrame` и в `UxrAvatar_GlobalAvatarMoved`
   сразу после `if (avatar == null || avatar.AvatarMode == UxrAvatarMode.UpdateExternally) return;`.
2. В `OnDestroy` исправить `UxrGrabbableObject.GlobalDisabled += ...` на `-=`, если SDK
   ещё не исправил сам.
3. Прогнать `AvatarTeardownTests`: без патча красный
   `Перемещение_аватара_не_бросает_на_захвате_мёртвой_руки` (`MissingReferenceException`),
   с патчем — зелёный.
4. Если в новом SDK `UxrGrabber` отпускает предмет до `base.OnDisable()` — корень исправлен
   у вендора, патч можно оставить как безвредную страховку.

---

## Зависимости от приватных членов SDK (рефлексия)

**Дата:** 2026-08-19 (задача T-21, находка VR-03)
**Файлы проекта:** `Assets/Scripts/PhysicalSpaceUtils/PhysicalSpaceSyncManager.cs`

### Почему это здесь

Это **не патчи**: исходники UltimateXR в перечисленных случаях не менялись. Но зависимость
от внутренних имён чужой библиотеки ничуть не слабее, а ломается она хуже — молча.
При обновлении SDK переименованное приватное поле **не даёт ошибки компиляции**: рефлексия
просто не находит его, метод тихо выходит, и калибровка роста перестаёт работать.
Худший вид зависимости — невидимый до рантайма.

Список живёт в этом файле, потому что при обновлении SDK смотрят именно сюда.

### Точки

| Где в проекте | Член SDK | Доступ | Зачем | Что сломается, если член исчезнет |
|---|---|---|---|---|
| `PhysicalSpaceSyncManager.ExpectedEyeHeight` | `UxrStandardAvatarController._bodyIKSettings` | чтение | достать `EyesBaseHeight` — эталонный рост глаз аватара, знаменатель в расчёте масштаба игрока | масштаб считается от запасных 1.75 м: у игроков другого роста уезжают пропорции тела и длина рук |
| `PhysicalSpaceSyncManager.ApplyScale` | `UxrStandardAvatarController._bodyIK` | чтение | добраться до самого объекта `UxrBodyIK`, чьи смещения надо пересчитать под новый масштаб | смещения IK не пересчитываются; после калибровки роста голова и шея аватара стоят не на месте |
| `PhysicalSpaceSyncManager.ApplyScale` | `UxrBodyIK._avatarForwardPosRelativeToNeck` | чтение + запись | вектор «шея → перёд аватара» посчитан один раз в масштабе 1; масштабируем вместе с телом | тело разворачивается не туда при масштабе, отличном от единицы |
| `PhysicalSpaceSyncManager.ApplyScale` | `UxrBodyIK._neckPosRelativeToEyes` | чтение + запись | вектор «глаза → шея», та же история | голова садится не на шею |
| `AvatarSwapDeathReplicationScenario` (ярус C) | `UxrMirrorAvatar._serverBroadcaster` | чтение, static | сценарий проверяет, что после **Патча 1** этого поля в SDK больше нет | ничего: отсутствие поля здесь — ожидаемый результат, а не поломка |

### Громкий отказ вместо тихого

Раньше ненайденное поле означало молчаливый `return`, и после обновления SDK поломка
вылезала неделю спустя, жалобами на калибровку. Теперь:

- `PhysicalSpaceSyncManager.ResolveSdkField` — единственная точка поиска приватного поля
  в этом классе. На ненайденное поле пишет `GameLog.Error` с именем поля, полным именем
  типа и ссылкой на этот раздел. Через неё идут все четыре обращения.
- Отдельно логируется случай, когда поле нашлось, но `_bodyIK` пуст: пересчитывать
  смещения не от чего.
- Сценарий яруса C **намеренно молчит**: он и рассчитан на отсутствие поля.

### Кандидаты на вынос в публичный API

Механизм `.Custom.cs` (см. **Патч 2**) позволяет добавить публичный доступ, не трогая
оригинальный файл SDK. В рамках T-21 это **не делалось** — замена рефлексии на публичный
API меняет поведение и требует проверки в шлеме.

| Член | Насколько просто | Комментарий |
|---|---|---|
| ~~`UxrGrabbableObject._autoCreateStartAnchor`~~ | **сделано 2026-08-22** | Вынесено в публичное свойство `AutoCreateStartAnchor` — см. **Патч 6**. Рефлексии по этому полю в проекте больше нет |
| `UxrStandardAvatarController._bodyIKSettings` | просто | Свойство только на чтение в `.Custom.cs` закрывает случай целиком |
| `UxrStandardAvatarController._bodyIK` | просто | Аналогично, свойство на чтение |
| `UxrBodyIK._avatarForwardPosRelativeToNeck`, `._neckPosRelativeToEyes` | сложнее | Нужна запись во внутреннее состояние IK. Правильнее не открывать поля, а добавить в `UxrBodyIK` метод вида `RescaleBodyProportions(float relativeScale)` — тогда знание о том, что именно надо домножить, останется в SDK |

### Как проверить

1. Временно переименовать поле в исходнике SDK (например, `_bodyIK` → `_bodyIK2`).
2. Запустить калибровку роста: в консоли обязана появиться внятная ошибка с именем поля
   и типа, а не тишина.
3. Вернуть имя обратно.

Автотестом это не закрыто: рефлексия смотрит в чужую сборку, и «поле на месте» проверяется
ровно тем же вызовом, который проверяют. Ближайший дешёвый сторож — сам `Error` в консоли.
