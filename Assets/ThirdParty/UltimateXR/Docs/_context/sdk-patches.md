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
