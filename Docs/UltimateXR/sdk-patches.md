# UltimateXR SDK — Патчи и отступления от оригинала

Этот файл документирует **все изменения**, внесённые в код `Assets/ThirdParty/UltimateXR/`.  
При обновлении SDK необходимо **повторно применить** эти патчи вручную.

## Патч 2026-10-02: живые зависимости предметов и диагностика Fade

`UxrGrabbableObject.cs`: пять списков зависимостей очищаются общей функцией `LiveDependencies`
при чтении от Unity-null и `IsBeingDestroyed`. Исходный кэш обновляется при Awake/перестройке,
но уничтожение вложенной детали не инвалидирует списки родителей. Падение в
`UxrGrabManager.UxrAvatar_GlobalAvatarMoved` обрывало переход после затемнения.
Единая очистка защищает также ограничения, направление родителей и запросы захватов.

`UxrCameraFade.cs`: событие `FadeDiagnostic` сообщает запросы StartFade, EnableFadeColor,
FadeAsync, StartFadeCoroutine и переход DrawFade в false. Идентичные покадровые установки
постоянного цвета подавлены. SDK не ссылается на игровую сборку; `CameraFadeDiagnostics`
подписывается при старте и пишет через `GameLog.Debug.Info` со стеком.
Диагностика не выключает Fade автоматически и не меняет длительность переходов.

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

## Патч 13: запрос власти над уже уничтоженным предметом разрывает соединение

**Файл:** `Runtime/Scripts/Networking/Integrations/Net/Mirror/UxrMirrorAvatar.cs` — `CmdRequestAuthority`
**Дата:** 2026-09-27
**Парный код проекта:** `Assets/Scripts/Player/EquipmentStrip.cs`

### Проблема

Захват предмета шлёт серверу `CmdRequestAuthority(NetworkIdentity)`. Если сервер уничтожил
предмет раньше, чем команда дошла (снятие снаряжения при смене режима, паузе, смене карты —
`EquipmentStrip`), Mirror десериализует ссылку как `null`. SDK разыменовывал её без проверки,
`NullReferenceException` в обработчике команды Mirror считает недоверенными данными и
**разрывает соединение отправителя**; на хосте это его собственный клиент — игра обрывается.
Найдено в Play mode: пауза посреди раунда с оружием, только что взятым в руку.

### Применённое изменение

`if (networkIdentity == null) return;` первой строкой `CmdRequestAuthority`. Помечено
`VR Battlegrounds patch 13`.

### Как повторить при обновлении SDK

1. Добавить проверку на null в начало `UxrMirrorAvatar.CmdRequestAuthority`.
2. Прогнать `AuthorityRequestForDestroyedTests`: без патча — NRE, с патчем — зелёный.

---

## Патч 14: DeepCopy падает под IL2CPP на словарях — исключение каждый кадр

**Файл:** `Runtime/Scripts/Extensions/System/ObjectExt.cs` — `DeepCopy<T>`, новый `DeepCopyObject`,
`BinarySerializationCopy`
**Дата:** 2026-09-28

### Проблема

В сборке Quest (IL2CPP) каждый кадр:
`InvalidCastException: Unable to cast object of type 'RuntimeTriggerInfo' to type 'Dictionary`2'`
в `ObjectExt.DeepCopy` ← `UxrStateSaveImplementer.SerializeStateValue`. Состояние оружия
(`UxrFirearmWeapon._runtimeTriggers` — `Dictionary<int, RuntimeTriggerInfo>`) копируется для
сравнения на каждом кадре. `DeepCopy<T>` копировал элементы рекурсивным вызовом `DeepCopy(kvp.Value)`
— то есть `DeepCopy<object>` изнутри `DeepCopy<Dictionary<,>>`. Под IL2CPP вложенный вызов
разделяет обобщённый контекст с внешним и приводит копию элемента к `T` внешнего вызова — к словарю.
В редакторе (Mono) не воспроизводится.

### Применённое изменение

Вся логика перенесена в необобщённый `private static object DeepCopyObject(object)`, рекурсия идёт
через него. `DeepCopy<T>` — одна строка `return (T)DeepCopyObject(obj)`: приведение к `T` одно и
снаружи. `BinarySerializationCopy` тоже необобщённый. Поведение для всех ветвей прежнее.

### Как повторить при обновлении SDK

1. Переписать `DeepCopy<T>` так же: тело — в необобщённый метод, рекурсивные вызовы — к нему.
2. Собрать Quest, запустить на шлеме, `adb logcat | findstr InvalidCastException` — пусто.

Автотестом не закрыто: EditMode работает на Mono, где дефекта нет.

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

## Патч 15: запрет выстрела извне и сведения о спуске

**Файл:** `Runtime/Scripts/Mechanics/Weapons/UxrWeapon.Custom.cs` — `UxrWeapon.IsUseBlocked`,
проверка в `CanUse`; `UxrFirearmWeapon.TriggerCount`, `TryGetTriggerGrip`, `GetTriggerShotIndex`,
`PlayTriggerNoAmmoSound`. Метка в коде — `VR Battlegrounds patch 15`.
**Дата:** 2026-09-28

### Проблема

Снаряд рождается у дула (`ShotSource` на 1 см позади `Tip`), и ствол, просунутый сквозь стену,
стрелял по ту сторону. Запретить выстрел снаружи SDK не даёт: `TryToShootRound` проверяет только
`CanUse`, а он вычисляемый, без сеттера. Узнать, какой рукой жмут спуск и каким звуком щёлкает
пустой спуск, тоже нельзя без рефлексии: `UxrFirearmTrigger` — `internal`, а рефлексия по
приватным полям под IL2CPP ненадёжна.

### Применённое изменение

- `UxrWeapon.IsUseBlocked` (публичное свойство); `CanUse` возвращает `false`, пока оно истинно.
  Его ставит `BarrelObstruction` (`Assets/Scripts/Weapons/`), пока ствол задевает геометрию.
- В тот же файл — частичный `UxrFirearmWeapon` с четырьмя узкими методами о спуске. Тип
  `UxrFirearmTrigger` наружу не выходит.

### Как повторить при обновлении SDK

1. В `UxrWeapon.CanUse` первой проверкой — `if (IsUseBlocked) return false;`, свойство рядом.
2. Вернуть частичный `UxrFirearmWeapon` с `TriggerCount`, `TryGetTriggerGrip`,
   `GetTriggerShotIndex`, `PlayTriggerNoAmmoSound`.
3. `BarrelObstructionTests`: `Стена_на_стволе_запрещает_выстрел_а_свой_аватар_нет` проверяет и
   `CanUse` при `IsUseBlocked`.

---

## Патч 16: подсветка хвата не считается для рук remote-аватаров

**Файл:** `Runtime/Scripts/Manipulation/UxrGrabManager.cs` — `UpdateAffordances`, новый
`IsLocalAffordanceGrabber`. Метка в коде — `VR Battlegrounds patch 16`.
**Дата:** 2026-09-28

### Проблема

`UpdateAffordances` каждый кадр для **каждой пустой руки из `UxrGrabber.EnabledComponents`**
ищет ближайший предмет (`GetClosestGrabbableObject` — перебор всех grabbable, ~1 мс на руку в
лобби со 126 предметами) и перебирает все якоря с положенными предметами (слоты арсенала, карманы
каждого аватара). Руки remote-аватаров туда тоже входят, и цена растёт квадратично: больше
аватаров — больше и рук, и карманов. Стресс-тест на Quest 3: 9 remote-аватаров — +34 мс в
`LateUpdate`, 72 → 18 FPS (стадия `Manipulation` 36,5 мс, из них `UpdateAffordances` 35 мс).

Результат этих двух проходов — только локальная обратная связь: `EnableOnHandNear` у точек
хвата и `ActivateOnHandNearAndGrabbable` у якорей. Remote-аватар хватает по сетевому событию,
близость его руки ни на что не влияет.

### Применённое изменение

В обоих проходах по рукам (ближайший предмет; предмет в якоре) пропускаются руки аватаров не
в режиме `Local`. Проход по предметам в руках (подсказка «можно положить в якорь», события
`AnchorRangeEntered/Left`, на которых стоит `PocketReadiness`) не тронут.

Итог в редакторе с 9 куклами: `Manipulation` 36,5 → 4,1 мс, аллокации ~810 → ~260 КБ/кадр.
На выделенном сервере локального аватара нет — подсветка не считается вовсе.

### Как повторить при обновлении SDK

1. В `UpdateAffordances` оба цикла `foreach (UxrGrabber grabber in UxrGrabber.EnabledComponents)`
   — условие `grabber.GrabbedObject == null && IsLocalAffordanceGrabber(grabber)`.
2. `IsLocalAffordanceGrabber`: `grabber.Avatar != null && grabber.Avatar.AvatarMode == UxrAvatarMode.Local`.
3. Проверка — стресс-тест (`Docs/perf-stress-test.md`): стадия `Manipulation` с 9 куклами
   не должна расти больше чем на ~1 мс против базы.

---

## Патч 17: поиск держащей руки только у предметов, которые кто-то держит

**Файл:** `Runtime/Scripts/Manipulation/UxrGrabbableObject.cs` — `GetDistanceFromGrabber`;
`UxrGrabManager.Querying.cs` — `GetGrabbingHand(obj, point, out grabber)`. Метка — `VR Battlegrounds patch 17`.
**Дата:** 2026-09-28

### Проблема

`GetClosestGrabbableObject` каждый кадр зовёт `GetDistanceFromGrabber` для каждого предмета и точки
хвата, а тот для каждой другой точки — `GetGrabbingHand`, который перебирает все `UxrGrabber.EnabledComponents`
и на каждом копирует список хватов (`GetGrabs`). Для предмета, который никто не держит, ответ всегда
«нет», но стоил ~1 мс на руку при 126 предметах.

### Применённое изменение

1. В `GetDistanceFromGrabber` цикл «штраф рядом с другой рукой» идёт до
   `UxrGrabManager.Instance.IsBeingGrabbed(this) ? GrabPointCount : 0`.
2. В `GetGrabbingHand` ранний `return false`, если предмета нет в `_currentManipulations`; хваты
   перебираются из `manipulationInfo.Grabs` без копии. Результат идентичен прежнему.

### Как повторить при обновлении SDK

Найти цикл `for (int otherGrabbedPoint ...)` в `GetDistanceFromGrabber` и `GetGrabbingHand(..., int point, out UxrGrabber)`,
добавить проверки из п. 1–2.

---

## Патч 18: подсказка «положить в якорь» только для предметов в руках локального игрока

**Файл:** `Runtime/Scripts/Manipulation/UxrGrabManager.cs` — `UpdateAffordances`, новый `HasLocalAffordanceGrab`.
Метка — `VR Battlegrounds patch 18`.
**Дата:** 2026-09-28

### Проблема

Первый цикл `UpdateAffordances` для каждого предмета в руках — в том числе у remote-аватаров —
перебирает все якоря с `CanBePlacedOnAnchor`. Результат (`AnchorRangeEntered/Left`,
`ActivateOnCompatibleNear/NotNear`) — локальные события без сетевой синхронизации; единственный
подписчик `PocketReadiness` фильтрует по своему `Local`-аватару.

### Применённое изменение

Предмет, среди держащих рук которого нет ни одной руки `Local`-аватара (`IsLocalAffordanceGrabber`, патч 16),
пропускается (`continue`). Побочный эффект: `ActivateOnCompatibleNear` не зажигается от предмета в руке
чужого аватара.

### Как повторить при обновлении SDK

В начале тела `foreach (... manipulationInfoPair in _currentManipulations)` в `UpdateAffordances`:
`if (!HasLocalAffordanceGrab(manipulationInfoPair.Value)) continue;`. Хелпер — перебор `Grabs` с
`IsLocalAffordanceGrabber(grabInfo.Grabber)`.

---

## Патч 19: выборка скорости броска без аллокаций

**Файл:** `Runtime/Scripts/Manipulation/UxrGrabber.cs` — `UpdateThrowPhysicsInfo`, поля окна;
`UxrGrabber.PhysicsSample.cs`. Метка — `VR Battlegrounds patch 19`.
**Дата:** 2026-09-28

### Проблема

`FinalizeManipulationFrame` каждый кадр зовёт `UpdateThrowPhysicsInfo` у каждой руки каждого аватара:
`new PhysicsSample` (class) плюс `LastOrDefault/ForEach/RemoveAll/Select/Average/First/Last` — мусор на
каждую руку в каждом кадре.

### Применённое изменение

`PhysicsSample` — struct (`Age` — поле, в конструкторе флаг `hasLastSample` вместо null).
Окно — кольцевой буфер `PhysicsSample[32]` (удваивается при переполнении). Математика та же:
старение каждого сэмпла, удаление старых с головы (возраст монотонен), среднее `TotalVelocity`
в double как `Enumerable.Average`, угловая скорость по самому старому и новому сэмплу.
Для remote-рук выборка **не** отключена: сервер отпускает их предметы сам (`AvatarTeardown`,
`EquipmentStrip`, смерть в `PlayerGrabManager`) и `ReleaseObject` берёт скорость из `SmoothVelocity`.

### Как повторить при обновлении SDK

Перенести `UpdateThrowPhysicsInfo` и `PhysicsSample` из текущей версии целиком; если в SDK
изменилась математика сэмпла — повторить её в struct-версии.

---

## Патч 20: кэш IK-решателей аватара в SolveBodyIK

**Файл:** `Runtime/Scripts/Avatar/Controllers/UxrStandardAvatarController.cs` — `SolveBodyIK`, `RefreshCachedIKSolvers`, `IsAutoUpdateSolver`; `Runtime/Scripts/Animation/IK/UxrIKSolver.cs` — `RegistryVersion`, `Awake`, `OnDestroy`. Метка в коде — `VR Battlegrounds patch 20`.
**Дата:** 2026-09-28

### Проблема

`SolveBodyIK` каждый кадр у каждого аватара трижды перебирал LINQ-выборкой `UxrIKSolver.GetComponents(Avatar)` — решатели ВСЕХ аватаров сцены: ленивая выборка рук считалась дважды, решатели не для рук — третьим запросом. С 10 аватарами цена растёт квадратично, плюс аллокации LINQ.

### Применённое изменение

`UxrIKSolver.RegistryVersion` растёт при регистрации (`Awake`) и снятии (`OnDestroy`) любого решателя. Контроллер хранит два списка решателей своего аватара (руки: `is UxrArmIKSolver`; остальные: `GetType() != typeof(UxrArmIKSolver)`) и собирает их заново только при смене версии или аватара. Проходы идут по спискам циклом `for`; `isActiveAndEnabled && NeedsAutoUpdate` проверяется на каждом проходе заново. Порядок, фильтры и число проходов (включая три прохода у руки с ключицей) не менялись.

### Как повторить при обновлении SDK

1. В `UxrIKSolver` — `internal static int RegistryVersion`, `++` после `base.Awake()` и после `base.OnDestroy()`.
2. В `SolveBodyIK` — три LINQ-выборки заменить циклами по кэшу, фильтр `solver != null && isActiveAndEnabled && NeedsAutoUpdate`.
3. Проверка: локальный аватар — IK рук и тела как раньше; стресс-тест — стадия `PostProcess` с 9 куклами.

---

## Патч 21: LeftGrabber/RightGrabber без LINQ по всем захватчикам сцены

**Файл:** `Runtime/Scripts/Avatar/UxrAvatar.cs` — `LeftGrabber`, `RightGrabber`, `FindEnabledGrabber`, `HookGrabberRegistry`. Метка в коде — `VR Battlegrounds patch 21`.
**Дата:** 2026-09-28

### Проблема

`UxrGrabber.GetComponents(this).FirstOrDefault(...)` перебирал захватчики всех аватаров сцены. Геттеры зовутся из `ProcessHandManipulation` и `IsHandGrabbing` каждого аватара каждый кадр — O(аватаров²) плюс аллокации.

### Применённое изменение

Захватчики аватара (включённые и нет, в порядке статического списка) кэшируются. Кэш собирается заново, когда меняется статическая версия; её увеличивают `UxrGrabber.GlobalRegistered/GlobalUnregistered`. Подписка — в `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` через `-=`/`+=`, поэтому переживает вход в Play Mode без перезагрузки домена. `isActiveAndEnabled` и `Side` проверяются при каждом вызове. Ветка для редактора вне Play Mode (`GetComponentsInChildren`) не тронута.

### Как повторить при обновлении SDK

1. В геттерах `return FindEnabledGrabber(UxrHandSide.Left/Right);` вместо LINQ.
2. Перенести `FindEnabledGrabber`, `HookGrabberRegistry`, `OnGrabberRegistryChanged` и поля `s_grabberRegistryVersion`, `_cachedGrabbers`, `_cachedGrabbersVersion`.

---

## Патч 22: PostUpdate без yield-итераторов и new UxrAvatarUpdateEventArgs

**Файл:** `Runtime/Scripts/Core/UxrManager.cs` — `PostUpdate`, `FillAvatarSnapshot`, `TryGetUpdatableController`, `GetAvatarUpdateArgs`. Метка в коде — `VR Battlegrounds patch 22`.
**Дата:** 2026-09-28

### Проблема

`PostUpdate` пять раз за кадр перечислял `LocalAvatarControllers`/`EnabledAvatarControllers` (yield поверх LINQ) и на каждый аватар в каждой стадии дважды создавал `new UxrAvatarUpdateEventArgs` — около 60 объектов за кадр при 10 аватарах.

### Применённое изменение

Перед каждым циклом `UxrAvatar.AllComponents` копируется в переиспользуемый `List` (`AddRange` без аллокаций), а прежний фильтр проверяется на каждом элементе в момент обхода. Снимок раз за кадр отвергнут: между циклами `UxrGrabManager` и подписчики могут выключить аватар. Аргументы событий кэшируются по паре (аватар, стадия): класс неизменяемый, подписчиков в проекте нет; записи уничтоженных аватаров вычищаются при добавлении нового. `Update()` и сами свойства не тронуты.

### Как повторить при обновлении SDK

1. Циклы `PostUpdate` — через `FillAvatarSnapshot` + `TryGetUpdatableController(avatar, localOnly, out controller)`; стадия Animation — фильтр `avatar != null && isActiveAndEnabled`.
2. `new UxrAvatarUpdateEventArgs(...)` в `PostUpdate` заменить на `GetAvatarUpdateArgs(avatar, stage)`.
3. Если появится подписчик, который меняет или сравнивает аргументы по ссылке, — вернуть `new`.

---

## Патч 23: один источник выстрела — событие Shoot стрелка

**Файл:** `Runtime/Scripts/Mechanics/Weapons/UxrFirearmWeapon.cs` — `UxrManager_AvatarsUpdated`,
`TryToShootRound`, `OnEnable/OnDisable`; `UxrWeapon.Custom.cs` — `ProjectileShotReplayed`,
`Source_ShotFired`, `SubscribeShotReplay`; `UxrProjectileSource.cs` — событие `ShotFired`, убран
`Debug.Log` в `Shoot`. Метка — `VR Battlegrounds patch 23`.
**Дата:** 2026-09-28

### Проблема

UltimateXR синхронизирует выстрел дважды: состоянием спуска (`SyncTriggerPressStates`), по которому
**каждая** машина пересчитывает выстрел копией оружия (`TryToShootRound` → `UxrProjectileSource.Shoot`),
и самим `Shoot` (синхронизируемый метод с точной позой ствола). В сетевой игре снаряд рождался и
от события стрелка, и от пересчёта копии: у стрелка вторая пуля (серверный `Shoot` возвращался к
нему через `NetworkStateRelay`), на выделенном сервере два снаряда и **двойной урон**, у других
игроков до трёх. На хосте незаметно — стрелок и сервер один процесс. Звук и отдача копии шли от её
пересчёта и расходились с настоящим выстрелом: `UxrFirearmMag.Rounds` не синхронизируется, ствол у
стены копия считает по своей интерполированной позе. Дробь `ShotgunPellets` множилась:
`ProjectileShot` поднимался и на копиях. Плюс в `Shoot` стоял `Debug.Log` на каждый снаряд.

### Применённое изменение

Одно сетевое событие, два локальных уведомления.

- **Решает только стрелок.** Логика выстрела в цикле спусков (решение `shoot`, щелчок пустого
  спуска, `TryToShootRound`, `SyncAmmoLeft`) выполняется только для руки `Local`-аватара. Копия в
  чужих руках берёт из синхронизированного спуска лишь поворот спуска для анимации.
- **`UxrProjectileSource.ShotFired`** — событие на каждый `Shoot` на любой машине, в том числе при
  повторе по сети (внутри `ExecuteStateSyncEvent`).
- **Эффекты у получателя** — `Source_ShotFired`: если идёт не свой `TryToShootRound`
  (`_shootingLocally`), для спуска с этим типом выстрела — звук, отдача, патрон копии и событие
  **`ProjectileShotReplayed`**. Дробинки — другой тип выстрела, на них ничего не играется.
- **`ProjectileShot`** поднимается только у стрелка — подписчик, который сам стреляет (дробь),
  по построению не выстрелит на копии.
- `Debug.Log` в `UxrProjectileSource.Shoot` удалён.

Щелчок пустого спуска у чужих копий больше не звучит (его решал пересчёт) — косметика.

### Как повторить при обновлении SDK

1. В `UxrManager_AvatarsUpdated` после установки `SetTriggerPressedAmount` обернуть логику выстрела
   (от `bool shoot` до `SyncAmmoLeft`) в `if (grabber.Avatar.AvatarMode == UxrAvatarMode.Local)`.
2. В `TryToShootRound` — `_shootingLocally = true` вокруг `_weaponSource.Shoot(...)` (`try/finally`).
3. В `UxrProjectileSource.Shoot` перед `EndSyncMethod` — `ShotFired?.Invoke(shotTypeIndex)`; событие
   объявить рядом с `ShotTypes`.
4. В `OnEnable/OnDisable` оружия — `SubscribeShotReplay(true/false)`; остальное — в `UxrWeapon.Custom.cs`.
5. Проверка: `RemoteShotReplayTests` (копия по спуску не стреляет; пришедший выстрел — один снаряд и
   `ProjectileShotReplayed` без `ProjectileShot`; дробь только у стрелка).

---

## Патч 24: экономия IK невидимых чужих аватаров

**Файл:** `Runtime/Scripts/Avatar/Controllers/UxrStandardAvatarController.cs` — `UpdateAvatarPostProcess`;
новый `UxrStandardAvatarController.Custom.cs`. Метка — `VR Battlegrounds patch 24`.
**Дата:** 2026-09-28

### Проблема

IK тела и рук считается каждый кадр у всех аватаров, в том числе у чужих за стеной или за спиной.
На Quest 3 с 9 чужими это ~1,4 мс (`uxr_post`).

### Применённое изменение

Статический хук `ShouldSolveRemoteAvatarThisFrame` (`Func<UxrAvatar,bool>`, по умолчанию null) и
`ShouldSolveIKThisFrame()` в partial. В `UpdateAvatarPostProcess` перед `SolveBodyIK()` — ранний
`return`, если хук у `UpdateExternally`-аватара вернул false. Локальный аватар, отсутствие хука и
исключение внутри хука — решение каждый кадр. SDK не ссылается на код игры, поэтому политику ставит
игра: `RemoteAvatarIKThrottle` (видимость по рендерерам тела, раз в 4 кадра со сдвигом, на сервере и
хосте — всегда).

### Как повторить при обновлении SDK

1. Скопировать `UxrStandardAvatarController.Custom.cs`.
2. В `UpdateAvatarPostProcess` перед `SolveBodyIK();` вставить `if (!ShouldSolveIKThisFrame()) return;`
   с меткой патча.
3. Проверка: `RemoteAvatarIKPolicyTests`.

---

## Патчи 25–28: поиск предмета для хвата без полного перебора сцены

**Дата:** 2026-09-29. **Метки:** `VR Battlegrounds patch 25` … `28`. **Проверка:** `GrabQueryPatchTests`
(каждый тест показан красным на откате) + тесты хвата (`GunTwoHandGrabTests`, `WeaponPartGrabTests`,
`AnchoredItemGrabTests`, `TwoHandGrabPolicyTests`, `PumpGrabFollowTests`).

### Проблема

Профиль редактора (Deep Profile, свой аватар в лобби): почти всё время стадий UltimateXR —
`UxrGrabManager.GetClosestGrabbableObject` → `CanBeGrabbedByGrabber` → `GetDistanceFromGrabber`: для каждой
руки каждый кадр перебирались все предметы сцены (~100 в лобби: арсенал, карманы, магазины) с полным
расчётом расстояния, позы хвата аватара и угла, а наши правила хвата (`CanGrabDelegate`) звались для
каждого. В `Update` перебор шёл ради кнопок хвата (75 % стадии), в `LateUpdate` — ради подсветки (86 %).
Там же ~50 из ~70 КБ аллокаций за кадр. На Quest 3: `uxr_update` + `uxr_manip` ≈ 3,4 мс в пустом лобби,
≈ 5,8 мс с 40 брошенными предметами.

### Патч 25 — кнопки хвата: `UxrStandardAvatarController.cs` (`GetRequiredGrabButtonsOverride`),
`UxrGrabbableObject.Custom.cs` (`EnabledWithCustomGrabButtons`, `OnEnable`), `UxrGrabbableObject.cs` (`OnDisable`)

Переопределить кнопки хвата может только ближайшая точка с `UseDefaultGrabButtons == false`. Такие предметы
ведутся списком (добавляются в `OnEnable`, убираются в `OnDisable`). Список пуст или ни один из них не в
досягаемости руки — ближайшая точка заведомо «по умолчанию», полный перебор не делается. Иначе — прежний
полный поиск (ближе может быть обычный предмет, он и побеждает). В проекте такая деталь одна — чека гранаты
(`Pin`), список известных — в тесте.

### Патч 26 — мёртвый проход по якорям: `UxrGrabManager.cs` (`UpdateAffordances`)

Первый проход «пустая рука рядом с предметом в якоре» выключен `#if VRB_UXR_ANCHOR_GRABBER_NEAR`: он искал
ближайший предмет по всем заполненным якорям, но писал `GrabberNear = null` (апстрим), поэтому
`PlacedObjectRange*` и `ActivateOnHandNearAndGrabbable` не срабатывали ни с ним, ни без него. Если апстрим
починит строку на `= grabber` — определить символ.

### Патч 27 — порядок проверок: `UxrGrabbableObject.cs` (`CanBeGrabbedByGrabber`)

Сначала дешёвые проверки (выключенная точка, `IsGrabbable`, рука, dummy-родитель), затем досягаемость,
**делегат правил последним**. Все условия — конъюнкция без побочных эффектов, результат тот же. Убраны
неиспользуемые `isBeingGrabbedBy*` (шли в закомментированную ветку `AllowMultiGrab`); для `BoxConstrained`
расстояние не считается — оно там не участвует.

### Патч 28 — грубая отсечка по расстоянию: `UxrGrabbableObject.Custom.cs` (`IsOutsideCoarseGrabRange`),
`UxrGrabber.Custom.cs` (`CoarseProximityReach`), вызовы в `CanBeGrabbedByGrabber` и `GetClosestGrabbableObject`

По неравенству треугольника: если от захватчика до корня предмета дальше, чем
`CoarseProximityReach` руки + max(смещение proximity-трансформа точки от корня + `MaxDistanceGrab`) + 0,15 м,
ни одна точка не пройдёт проверку расстояния — предмет отбрасывается одним сравнением. Радиус предмета
считается лениво на пару (аватар, рука) и кэшируется. Не применяется к предметам с `UxrGrabPointShape`,
`BoxConstrained` или proximity-трансформом вне иерархии предмета. Запас 0,15 м — на ход подвижных деталей;
тест обходит руку по сфере 0,6 м вокруг каждой точки и требует, чтобы отсечка не отрезала ничего досягаемого.

### Как повторить при обновлении SDK

1. Перенести partial-файлы `UxrGrabbableObject.Custom.cs`, `UxrGrabber.Custom.cs` (блоки patch 25/28).
2. `UxrGrabbableObject.OnDisable`: вызов `UnregisterCustomGrabButtons()`.
3. `CanBeGrabbedByGrabber`: порядок проверок и отсечка, как выше; `GetClosestGrabbableObject`: `continue`
   по `IsOutsideCoarseGrabRange` перед перебором точек.
4. `UxrStandardAvatarController.GetRequiredGrabButtonsOverride`: ранний выход по списку.
5. `UxrGrabManager.UpdateAffordances`: `#if VRB_UXR_ANCHOR_GRABBER_NEAR` вокруг первого прохода по якорям.
6. Прогнать `GrabQueryPatchTests` и тесты хвата.

---

## Патч 29: жизнь актора входит в снимок состояния

**Дата:** 2026-09-29. **Задача:** [T-34](../tasks/T-34-late-join-state-snapshot.md), находка NET-27.
**Файл:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrActor.StateSave.cs` (новый partial,
метка `VR Battlegrounds patch`). **Проверка:** `StateSnapshotCoverageTests` (три теста, все красные до патча).

### Проблема

`UxrActor.Life` синхронизируется событием (`EndSyncProperty`), но `SerializeState` у актора не было. Начальный
снимок для клиента, пришедшего позже (`NetworkStateRelay.CmdRequestInitialState` → `SaveStateChanges`),
пишет только то, что компонент перечислил в `SerializeState`, — жизни там не было. Хуже: компонент, который
не пишет в снимок ничего, UltimateXR не регистрирует вовсе (`UxrStateSaveImplementer.RegisterComponent`,
пробная сериализация), так что актор выпадал из снимка целиком. Клиент, вошедший посреди матча,
переподключившийся или перезагрузивший сцену при смене карты, видел раненых со 100 хп, а мёртвых живыми
(`IsAlive = true` при включённом призраке).

### Решение

`SerializeState` с одной переменной `_life` для уровней выше `ChangesSincePreviousSave` — тот же приём, что
у `UxrFirearmMag._rounds`: в инкрементальных снимках жизнь едет событием, в полном и «с начала» — отсюда.
Чтение меняет только поле: `Died`, анимация смерти и звук у получателя не поднимаются — смерть случилась
до него. Трафик на попадание не меняется; снимок растёт на одну запись на каждого актора, чья жизнь
отличается от префабной.

### Как повторить при обновлении SDK

1. Перенести `UxrActor.StateSave.cs` (класс `partial` с Патча 2).
2. Прогнать `StateSnapshotCoverageTests`. Он же сканирует **все** синхронизируемые свойства SDK: если
   в новой версии появится свойство с `EndSyncProperty`, не записанное в `SerializeState`, тест назовёт его.
   Сознательные исключения (`UxrGrabbableResizable.IsGrabbable/IsKinematic`, `UxrAvatar.ShowControllerHands`)
   перечислены в тесте с причинами.

## Патч 30: сила пули в событии урона

**Дата:** 2026-09-29. **Задача:** [T-35](../tasks/T-35-avatar-swap-ghost-corpse.md) (толчок трупа от оружия).
**Файлы:** `Mechanics/Weapons/UxrDamageEventArgs.cs` (свойство `ImpactForce`, конструктор с ним),
`UxrActor.cs` (перегрузка `ReceiveImpact(…, Vector3 impactForce)`), `UxrWeaponManager.cs` (передаёт силу при
попадании в актора). Метка `VR Battlegrounds patch (Патч 30)`. **Проверка:** `CorpseTests.Толчок_по_полёту_пули_и_дробины_складываются`.

### Проблема

Попадание пули в физический предмет SDK толкает силой `скорость пули × ProjectileImpactForceMultiplier × направление`,
а в актора — нет, и в `UxrDamageEventArgs` нет ни описания выстрела, ни силы. Трупу (T-35) нечем учесть оружие:
толчок зависел только от урона, дробовик толкал как одна дробина.

### Решение

Та же сила считается при попадании в актора и едет в событии урона (`ImpactForce`, мировые координаты). Старые
сигнатуры сохранены и передают ноль — вызовы SDK и сторонний код не меняются.

### Как повторить при обновлении SDK

1. `UxrDamageEventArgs`: свойство `ImpactForce` и конструктор попадания с ним; старый конструктор — через новый с нулём.
2. `UxrActor.ReceiveImpact`: перегрузка с силой; старая зовёт её с нулём.
3. `UxrWeaponManager.UpdateProjectiles`: перед `targetActor.ReceiveImpact` посчитать силу так же, как для `rigidbody.AddForceAtPosition`, и передать в оба вызова (прямое попадание и отражённое).

## Патч 31: поправка урона пули до события (зоны попадания)

**Дата:** 2026-09-29. **Задача:** [T-38](../tasks/T-38-weapon-roster-and-balance.md) (урон по зонам).
**Файлы:** `Mechanics/Weapons/UxrActor.cs` (статическое `ImpactDamageModifier`, применяется в `ReceiveImpact`).
Метка `VR Battlegrounds patch (Патч 31)`. **Проверка:** `HitZoneDamageTests`.

### Проблема

Урон у `UxrDamageEventArgs` неизменяемый, а «смертельный ли» (`Dies`) SDK считает при создании события. Множитель
зоны в обработчике `DamageReceiving` невозможен: пришлось бы отменять урон и бить заново, а `Dies`, журнал урона
и метки попаданий видели бы исходное число.

### Решение

`UxrActor.ImpactDamageModifier` — `Func<UxrActor, RaycastHit, float, float>`, вызывается в `ReceiveImpact` до создания
события. Ставит игра (`HitZoneDamage.Install`): урон × множитель зоны хитбокса. Нет поправки — поведение SDK.
Взрывы и `ReceiveDamage(float)` не затронуты.

### Как повторить при обновлении SDK

1. `UxrActor`: статическое свойство `ImpactDamageModifier`.
2. `UxrActor.ReceiveImpact(…, Vector3 impactForce)` (патч 30): первой строкой `damage = ImpactDamageModifier(this, raycastHit, damage)`, если задано.

## Патч 32: прострел стен — хук пробития препятствия

**Дата:** 2026-09-30. **Задача:** [T-41](../tasks/T-41-wall-penetration.md).
**Файлы:** `Mechanics/Weapons/UxrProjectilePenetration.cs` (новый: `UxrPenetrationKind`, `UxrPenetrationResult`,
делегат `UxrProjectilePenetrationHandler`), `UxrWeaponManager.Custom.cs` (статическое `ProjectilePenetration`),
`UxrWeaponManager.ProjectileInfo.cs` (`DamageMultiplier`, `Penetrations`), `UxrShotDescriptor.cs` (поле
`_penetrationPower` = 1 — пробитие по CS, свойство `PenetrationPower`), `UxrWeaponManager.cs` (`UpdateProjectiles`).
Метка `VR Battlegrounds patch (Патч 32)`. **Проверка:** `WallPenetrationTests`.

### Проблема

`UpdateProjectiles` уничтожает пулю на первом же не-акторе. Пробить тонкое укрытие или пролететь сквозь листву
нельзя, а решать это вне SDK нечем: пуля живёт в приватном списке менеджера.

### Решение

Одна точка решения — `UxrWeaponManager.ProjectilePenetration`, её ставит игра (`WallPenetration.Install`, формула CS).
Для не-актора SDK считает текущий урон пули (спад по дистанции × `DamageMultiplier`), передаёт его хуку и получает
одно из трёх:

- `Stop` — как в оригинале (хук не задан — тоже `Stop`);
- `Penetrate` — эффекты попадания на входе как обычно, та же декаль на выходной грани (`ExitHit`), пуля переносится
  в `ExitPoint`, `Penetrations++`, множитель урона — из результата;
- `PassThrough` — без эффектов попадания и без события `NonActorImpacted`, пуля переносится в `ExitPoint`.

При попадании в актора урон и толчок (патч 30) умножаются на `DamageMultiplier` пули. Сила пробития — поле типа
выстрела `PenetrationPower`: у оружия проекта его пишет `Apply Weapon Balance` из `WeaponInfo`; у сэмплов SDK — 1.

### Как повторить при обновлении SDK

1. Перенести `UxrProjectilePenetration.cs`; в `UxrWeaponManager.Custom.cs` — свойство `ProjectilePenetration`.
2. `ProjectileInfo`: свойства `DamageMultiplier` (= 1) и `Penetrations`.
3. `UxrShotDescriptor`: поле `_penetrationPower` последним в списке полей и свойство `PenetrationPower`.
4. `UpdateProjectiles`, ветка «не отражатель»: после поиска `UxrActor` посчитать `penetration` (для не-актора — хук
   с текущим уроном `Lerp(Near, Far, пройдено/макс) × DamageMultiplier`, иначе `Stop`); `PassThrough` — пропустить обе ветки; в ветке актора умножить `damage` и `impactForce` на
   `DamageMultiplier`; в ветке не-актора после `NonActorImpacted` для `Penetrate` — декаль на `ExitHit`; вместо
   безусловного `Destroy` — при не-`Stop` перенести пулю (`transform.position` и `ProjectileLastPosition`) в
   `ExitPoint`, иначе уничтожить как раньше.
5. Прогнать `WallPenetrationTests`.

## Патч 33: пауза UltimateXR, пока редактор не в фокусе

**Дата:** 2026-09-30. **Файлы:** `Core/UxrManager.cs` (`HandleEditorFocusChange`, начало `Update`, поля
`_shouldUpdate`, `_savedPostUpdateMode`, свойства `IsPausedByEditorFocus`, `EditorFocusPauseEnabled`),
`Devices/Integrations/EditorWindowFocusHelper.cs` (новый файл). Метка `VR Battlegrounds patch 33`. Сама пауза старше этой
записи и до 2026-09-30 не была описана. **Меню:** `Tools/VR Battlegrounds/Debug/Pause XR When Editor Unfocused`.
**Проверка:** `EditorFocusPauseTests`.

### Проблема

Пауза нужна, когда на одной машине открыто несколько редакторов (хост и клиенты): тот, что не в фокусе, не должен
забирать шлем и процессор. Пока окно не в фокусе, `HandleEditorFocusChange` ставит `PostUpdateMode = None` и
останавливает XR-подсистемы, а `Update` возвращается сразу — не идут ни стадии, ни `StageUpdated`, ни IK.

Из-за этого Play в фоне — тесты, стенды (`LegsComparisonRig`), боты при работе агента — показывает аватары в T-позе
и не двигает их. Что было не так в исходной версии:

- включалось только флагом `OptimizeEditorFocus` в общем ассете `Resources/UxrGlobalSettings.asset` — переключение
  попадало в git;
- флаг, снятый во время паузы, её не снимал: `HandleEditorFocusChange` выходил раньше проверки фокуса, и
  `UxrManager` стоял до перезапуска Play;
- `PostUpdateMode` менялся только внутри блока XR Management — без XR-менеджера пауза не снималась;
- проверка фокуса каждый кадр создавала объекты `Process` (без `Dispose`) и перебирала все окна редактора через
  `Resources.FindObjectsOfTypeAll` с LINQ — мусор для GC и системные вызовы на каждом кадре;
- `Debug.Log` на каждую смену фокуса, публичное изменяемое поле `prevIsEditorFocused`.

### Решение

- Паузу включает один флаг — `UxrManager.EditorFocusPauseEnabled` в EditorPrefs этой машины (ключ
  `VrBattlegrounds.PauseXrWhenEditorUnfocused`, по умолчанию `true`), в git не попадает. Меню переключает его,
  тесты и стенды выставляют из кода и возвращают после себя. Прежний флаг `OptimizeEditorFocus` в
  `UxrGlobalSettings` (поле `_optimizeEditorFocus`, раздел «General» инспектора) удалён — он дублировал этот и
  лежал в общем ассете.
- Выключенная пауза считается фокусом. Если снять её во время паузы, срабатывает ветка «фокус вернулся».
- `PostUpdateMode` переключается при любой смене фокуса, XR-подсистемы — как раньше, при наличии XR Management.
- `EditorWindowFocusHelper.IsThisEditorInstanceFocused()` работает без аллокаций: PID своего процесса запоминается
  один раз, PID окна на переднем плане — `GetForegroundWindow` + `GetWindowThreadProcessId`; запасная проверка —
  `Application.isFocused && EditorWindow.focusedWindow != null`. Неиспользуемые методы помощника удалены.
- Состояние — закрытое `_shouldUpdate` (меняется только в `HandleEditorFocusChange`, там же — побочные действия смены), наружу — `IsPausedByEditorFocus`; лог смены фокуса — только при
  `LogLevelCore >= Verbose`.

### Как повторить при обновлении SDK

1. Перенести `EditorWindowFocusHelper.cs`.
2. В `UxrManager` под `#if UNITY_EDITOR`: поля `_shouldUpdate`, `_savedPostUpdateMode` (его пишет и сеттер
   `PostUpdateMode`), свойства `IsPausedByEditorFocus` и `EditorFocusPauseEnabled` (EditorPrefs).
3. `HandleEditorFocusChange`: `shouldUpdate = !EditorFocusPauseEnabled ||
   EditorWindowFocusHelper.IsThisEditorInstanceFocused()`; при смене — `_postUpdateMode` и старт/стоп XR-подсистем.
   В начале `Update` — вызов и `return`, пока `!_shouldUpdate`.
4. Прогнать `EditorFocusPauseTests`.

## Патч 34: поправка направления выстрела (с 2026-10-02 — только дробь)

**Дата:** 2026-10-01. **Задача:** [T-38](../tasks/T-38-weapon-roster-and-balance.md) (разброс по CS2).
**Файлы:** `Mechanics/Weapons/UxrFirearmWeapon.cs` (свойство `ShotOrientationModifier`, вызов в `TryToShootRound`).
Метка `VR Battlegrounds patch 34`. **Старая проверка:** `WeaponSpreadTests` — требует пересмотра после проверки
пользователем новой логики в шлеме/Unity; сейчас тесты не изменяются и не запускаются.

### Проблема

`TryToShootRound` стреляет `_weaponSource.Shoot(index)` — строго по дулу. Разброс (конус CS2) вне SDK поставить
некуда: `ProjectileShot` поднимается уже после того, как снаряд создан, а крутить трансформ дула — значит крутить
вспышку, прицельные метки и всё, что к нему привязано.

### Решение

`UxrFirearmWeapon.ShotOrientationModifier` — `Func<int, Quaternion, Quaternion>` (индекс спуска, поворот дула →
поворот снаряда). Задан — `TryToShootRound` зовёт `Shoot(index, позиция дула, поправленный поворот)`, иначе — как
в оригинале. Ставит игра (`WeaponSpread`). `TryToShootRound` по патчу 23 исполняет только машина стрелка, а
`Shoot(int, Vector3, Quaternion)` синхронизируется значениями — поворот снаряда у всех машин один, общего зерна
случайности не нужно.

**Решение пользователя 2026-10-02:** случайный конус пули отменён, но патч сохранён для первой дробины
Nova / Herrington. Без него основной снаряд SDK остался бы всегда в центре, а остальные — в конусе.
`WeaponSpread` регистрирует модификатор только при активном `ShotgunPellets` с несколькими дробинами;
он применяет только spread каждой дробины, без inaccuracy. У пулевых префабов компонент удаляется через
`Apply Weapon Balance`; даже у старого префаба без дроби модификатор не регистрируется.
Патч 23 (единственный автор выстрела) и сетевой путь `Shoot` сохранены.

### Как повторить при обновлении SDK

1. `UxrFirearmWeapon`: свойство `ShotOrientationModifier`.
2. `TryToShootRound`: вместо `_weaponSource.Shoot(trigger.ProjectileShotIndex)` — при заданном модификаторе и
   допустимом индексе `Shoot(index, ShotSource.position, ShotOrientationModifier(triggerIndex, ShotSource.rotation))`.
3. После подтверждения логики пользователем заменить старые ожидания `WeaponSpreadTests`:
   пуля совпадает с осью дула, каждая дробина имеет собственный разлёт без общей неточности залпа.

## T-39. Чтение выбранного якоря и снимок ручного хвата оружия

`Manipulation/UxrGrabManager.PlacementReadiness.cs` добавляет read-only
`GetAnchorPlacementCandidate(anchor)` из уже рассчитанной SDK пары. Игровой
`AnchorPlacementReadiness` повторно проверяет актуальную локальную руку, предмет,
совместимость, свободное гнездо и единственную удерживающую руку. Это исключает
устаревший event-cache и повторный полный поиск якорей на каждом оружии.

`Manipulation/UxrGrabManager.WeaponHandoff.cs` и
`UxrManipulationEventArgs.CaptureObjectPose()` обновляют положение объекта относительно
grabber в существующем событии Grabbing после переноса визуального residual в physical Slide.
Snap руки и сериализация события не меняются; Compute и синхронизация получают актуальную
позу из того же события. Новый сетевой вызов не создаётся. При обновлении SDK сохранить
оба partial-файла и метод event args. Полный сетевой replay ещё требует двух клиентов.

Реализация, проверки и ограничения — [T-39](../tasks/T-39-kinemation-fixes-plan.md).
