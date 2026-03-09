# UltimateXR SDK — Патчи и отступления от оригинала

Этот файл документирует **все изменения**, внесённые в код `Assets/ThirdParty/UltimateXR/`.  
При обновлении SDK необходимо **повторно применить** эти патчи вручную.

---

## Патч 1: UxrMirrorAvatar — исправление синхронизации в режиме Server+Client

**Файл:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Networking/Integrations/Net/Mirror/UxrMirrorAvatar.cs`  
**Ветка:** `dev`  
**Дата:** 2025

### Проблема

В оригинальном SDK `UxrMirrorAvatar` не работал корректно в режиме **выделенного сервера + отдельного клиента** (Server+Client).

При подключении клиента к серверу `LoadStateChanges` выдавал предупреждения:

```
UxrManager.LoadStateChanges(): Cannot deserialize a component. Skipping:
UxrComponentNotFoundException: Could not find the given component using
UxrUniqueIdImplementer.TryGetComponentById(). Id is <guid>.
```

Результат: начальное состояние сцены не применялось к клиенту.

### Причина

**Проблема 1 — `_initialStateLoaded` был `static`:**

```csharp
// ОРИГИНАЛ (неправильно)
private static bool _initialStateLoaded;
```

Статическое поле разделялось между всеми экземплярами `UxrMirrorAvatar` в процессе.  
Когда Player 1 (сервер-хост) устанавливал `_initialStateLoaded = true`, это мгновенно
влияло на Player 2 — `RpcComponentStateChanged` начинал обрабатываться до того, как
клиент 2 получил глобальное состояние.

**Проблема 2 — отсутствие инициализации GUID на сервере:**

В оригинале `InitializeNetworkAvatar` (и внутри него `CombineUniqueId`) вызывался
только из `OnStartClient`. На **выделенном сервере** `OnStartClient` **не вызывается**
для объектов чужих игроков — только `OnStartServer`. Поэтому при получении
`CmdNewAvatarJoined` аватар клиента ещё не имел правильных GUID в реестре UltimateXR,
и `LoadStateChanges` не мог найти компоненты по ID.

### Применённые изменения

1. **`_initialStateLoaded` изменён с `static` на instance-поле:**

```csharp
// ИСПРАВЛЕНО
private bool _initialStateLoaded;
```

2. **Добавлен `OnStartServer`** — инициализирует аватар (и рекурсивно GUID всех
   дочерних компонентов) на сервере при спавне объекта:

```csharp
public override void OnStartServer()
{
    Avatar = GetComponent<UxrAvatar>();
    InitializeNetworkAvatar(Avatar, netIdentity.isOwned, netId.ToString(),
        $"Player {netId} ({(netIdentity.isOwned ? "Local" : "External")})");
    base.OnStartServer();
}
```

3. **`InitializeNetworkAvatar` защищена от двойного вызова** через флаг `_avatarInitialized`,
   чтобы повторный вызов из `OnStartClient` после `OnStartServer` не вызывал
   `CombineUniqueId` дважды (что сломало бы GUID):

```csharp
if (_avatarInitialized && Avatar == avatar)
{
    // обновляем только ownership, без повторного CombineUniqueId
    return;
}
```

4. **`OnStartLocalPlayer` выделен отдельно** для явной инициализации локального
   аватара и подписки на события синхронизации.

### Режимы работы после патча

| Режим | Статус |
|---|---|
| Host + Client (один процесс) | ? Работает |
| Dedicated Server + Client | ? Работает |

### Как повторить при обновлении SDK

При обновлении UltimateXR SDK нужно:

1. Найти файл `UxrMirrorAvatar.cs`
2. Изменить `private static bool _initialStateLoaded` ? `private bool _initialStateLoaded`
3. Добавить `OnStartServer` с вызовом `InitializeNetworkAvatar`
4. Добавить защиту от двойного вызова в `InitializeNetworkAvatar` через флаг `_avatarInitialized`
5. Убедиться что `AvatarSpawned` и `UxrInstanceManager.NotifyNetworkSpawn` вызываются
   внутри `InitializeNetworkAvatar`, а не отдельно в `OnStartClient`

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
