# UltimateXR — Известные проблемы и неочевидные поведения

> Обновлять при обнаружении новых побочных эффектов, неочевидных поведений SDK,
> или исправлении ошибок в UltimateXR.
> Этот файл — первое место, куда смотрит ИИ при расследовании любого бага.

---

## Как пользоваться этим файлом

При расследовании любого неожиданного поведения системы:

1. Прочитать этот файл целиком
2. Прочитать `sdk-patches.md` (список внесённых правок в SDK)
3. Только после этого читать исходный код

---

## Issue 1: «Эффект взрыва» при включении UxrAvatar или Camera

**Компоненты:** `UxrManager`, `IUxrPrecacheable`, `UxrGrenadeWeapon`, `UxrProjectileSource`  
**Статус:** ✅ Исправлено в `UxrManager.cs`  
**Дата обнаружения:** 2026-03-14

### Симптом

При добавлении `UxrAvatar` на сцену, или при каждом переключении компонента `Camera` /
скрипта `UxrAvatar`, виден визуальный «эффект взрыва» прямо перед камерой.

### Причина

`UxrManager` автоматически запускает процесс **Precaching** каждый раз при активации
локального аватара. Этот процесс:

1. Ищет все компоненты с интерфейсом `IUxrPrecacheable` в сцене.
2. Инстанцирует их `PrecachedInstances` **прямо перед камерой** (на 5 метров вперёд).
3. Удаляет эти временные объекты через несколько кадров.

Компоненты с `IUxrPrecacheable`: `UxrGrenadeWeapon` (экспортирует пул взрывных
эффектов), `UxrProjectileSource` (пул при попаданиях), `UxrTeleportLocomotionBase`.

**Ошибка:** `UxrManager` глушил только `AudioSource`, но не `ParticleSystem`.
Взрывные эффекты с флагом **Play on Awake** воспроизводились при инстанцировании.

### Исправление

В `UxrManager.cs`, метод `AddScenePrecachedInstances`, добавлено:

```csharp
// Avoid particle effects
ParticleSystem[] particleSystems = dynamicInstance.GetComponentsInChildren<ParticleSystem>(true);
foreach (ParticleSystem particleSystem in particleSystems)
{
    var main = particleSystem.main;
    main.playOnAwake = false;
    particleSystem.Stop();
}
```

### Альтернативное решение

Если проблема возникнет снова (например, другой эффект), можно полностью отключить
Precaching через поле **Use Precaching** в компоненте `UxrManager` в сцене.

### Триггеры (когда возникает)

| Действие | Результат |
|---|---|
| Добавление `UxrAvatar` в сцену с `AvatarMode = Local` | Precaching запускается |
| Включение отключённого `UxrAvatar` | Precaching запускается |
| Включение/выключение `Camera` на аватаре | `UxrAvatar` переинициализируется → Precaching |
| Включение/выключение компонента `UxrAvatar` | Аналогично |

---

## Issue 2: UxrCameraFade добавляется автоматически

**Компоненты:** `UxrAvatar`, `UxrCameraFade`  
**Статус:** ℹ️ Поведение SDK по умолчанию (не баг)

### Симптом

При инициализации `UxrAvatar` на камере автоматически появляется компонент
`UxrCameraFade`, которого не было в префабе.

### Причина

`UxrAvatar` добавляет `UxrCameraFade` на `CameraComponent` во время `Awake()` или
`Start()`, если компонент не найден. Это нормальное поведение SDK для поддержки
телепортации с затуханием и системы Precaching (затемнение экрана во время прогрева).

### Важно

Precaching сам использует `UxrCameraFade.EnableFadeColor(Color.black)`, чтобы
затемнить экран на время загрузки эффектов. Если убрать `UxrCameraFade` —
Precaching продолжит работать, но экран не будет затемняться.

---

## Issue 3: Precaching повторяется при каждом включении аватара

**Компоненты:** `UxrManager`, `Avatar_Enabled`  
**Статус:** ℹ️ Поведение SDK по умолчанию

### Описание

`UxrManager.Avatar_Enabled()` вызывает `TryPrecaching()` при **каждой** активации
аватара. Это значит:

- Первый запуск сцены → Precaching
- Выключили/включили `UxrAvatar` → Precaching снова
- В мультиплеере: если аватар переключается между `Local` и `UpdateExternally`
  (external mode) → Precaching не срабатывает (есть защита через count > 1)

### Практическое следствие

В режиме разработки в редакторе Unity частое включение/выключение аватара создаёт
повторные инстанцирования временных объектов. Это не влияет на производительность
в рантайме, но может засорять сцену в Editor-режиме при дебаггинге.

---

## Issue 4: UxrActor — смерть не останавливает стрельбу автоматически

**Компоненты:** `UxrActor`, `UxrFirearmWeapon`, `UxrWeaponManager`  
**Статус:** ✅ Обработано через `Патч 2` в `sdk-patches.md`

### Описание

В оригинальном SDK смерть актора (`UxrActor.DieInternal`) не остановит оружие
автоматически — `UxrFirearmWeapon` продолжит стрелять, пока не будет явно отключён.

Подробности исправления — в [`sdk-patches.md`](sdk-patches.md) (Патч 2).

---

## Issue 5: Precaching не запускается в мультиплеере при > 1 локальном аватаре

**Компоненты:** `UxrManager.Avatar_Enabled`  
**Статус:** ℹ️ Защита SDK от мультиплеерного сценария

### Описание

```csharp
if (UxrAvatar.AllComponents.Count(a => a.AvatarMode == UxrAvatarMode.Local) == 1)
{
    TryPrecaching();
}
```

Если на сцене более одного аватара в режиме `Local` — Precaching не запустится.
Это защита для мультиплеерных сценариев, где аватары могут переключаться между
`Local` и `UpdateExternally`.

---

## Issue 6: RemoveObjectFromAnchor сохраняет объект в иерархии игрока

**Компоненты:** `UxrGrabManager`, `UxrGrabbableObjectAnchor`  
**Статус:** ✅ Исправлено через `Патч 3` (добавлен параметр `unparent`)  
**Дата обнаружения:** 2026-03-15

### Симптом

При вызове `RemoveObjectFromAnchor(obj, ...)` предмет визуально исчезает из слота, но продолжает "висеть" в воздухе и двигаться вместе с игроком. В инспекторе объект остается дочерним элементом по отношению к родителю анчера (обычно это сам Player или его подсистемы).

### Причина

Это штатное поведение UltimateXR для поддержки "составных" предметов. Когда предмет (например, магазин — Magazine) изымается из анчера (Pistol slot), но еще не взят в руку, SDK считает, что он всё еще должен быть привязан к общему родителю, чтобы не упасть и не потеряться при движении.

По умолчанию `RemoveObjectFromAnchor` всегда делает:
`ChangeGrabbableObjectParent(obj, grabbableObject.CurrentAnchor.transform.parent)`

### Исправление / Обходное решение

В `Патч 3` (см. [`sdk-patches.md`](sdk-patches.md)) в метод добавлен флаг `unparent`.

- Если нужно "выбросить" предмет в мир (как при Slot Swapping на спине): вызвать `RemoveObjectFromAnchor(obj, true, true)`.
- Если нужно оставить предмет привязанным к владельцу: вызвать стандартно (третий параметр `false`).

---

## Issue 7: _autoCreateStartAnchor перехватывает parent при сетевом спавне

**Компоненты:** `UxrGrabbableObject`, `ArsenalWallController`, Mirror `NetworkServer`  
**Статус:** ✅ Обработано в `ArsenalWallController.DisableAutoAnchor()`  
**Дата обнаружения:** 2026-04-12

### Симптом

При сетевом спавне оружия через `Instantiate` + `NetworkServer.Spawn()` оружие появляется
в **root иерархии сцены** вместо дочерних слотов арсенала. В иерархии видны объекты
`"Gun(Clone) Auto Anchor"`, `"Machinegun(Clone) Auto Anchor"` и т.д.

Вызов `transform.SetParent(slotAnchor)` в `AssignNetworkItem` не помогает — parent
сбрасывается обратно.

### Причина

`UxrGrabbableObject.Awake()` (строки 1893-1903) при `_autoCreateStartAnchor = true`:

```csharp
if (_autoCreateStartAnchor)
{
    UxrGrabbableObjectAnchor newAnchor = new GameObject($"{name} Auto Anchor", ...)
        .GetComponent<UxrGrabbableObjectAnchor>();
    newAnchor.transform.SetParent(transform.parent);   // root
    transform.SetParent(newAnchor.transform);           // оружие под Auto Anchor
    _startAnchor = newAnchor;
}
```

Это происходит **внутри `Instantiate()`** (Awake вызывается немедленно), до любого
нашего кода. Поэтому:

1. `Instantiate(weaponPrefab)` -> Awake -> создается "Auto Anchor" в root -> оружие
   становится child Auto Anchor
2. Наш `SetParent(slotAnchor)` перемещает оружие в слот, но Auto Anchor остается в root
3. UXR-код может обратиться к `_startAnchor` и вернуть оружие обратно

**Затронутые префабы:** `Gun`, `Machinegun`, `Shotgun` (`_autoCreateStartAnchor = true`).
Не затронуты: `Grenade`, `MagGun`, `MagMachinegun`, `MagShotgun` (`false`).

### Решение

В `ArsenalWallController.ReplenishWeaponsNetwork()` инстанцируем префаб **деактивированным**,
отключаем флаг через reflection, затем активируем:

```csharp
// 1. Деактивируем префаб, чтобы Awake не сработал при Instantiate
var prefab = slot.WeaponData.WeaponPrefab;
bool wasActive = prefab.activeSelf;
prefab.SetActive(false);
GameObject spawned = Instantiate(prefab);
prefab.SetActive(wasActive);

// 2. Отключаем _autoCreateStartAnchor через reflection
DisableAutoAnchor(spawned);

// 3. Активируем — Awake сработает, но Auto Anchor не создастся
spawned.SetActive(true);
```

Метод `DisableAutoAnchor`:

```csharp
private static void DisableAutoAnchor(GameObject obj)
{
    var grabbable = obj.GetComponent<UxrGrabbableObject>();
    if (grabbable == null) return;
    var field = typeof(UxrGrabbableObject).GetField(
        "_autoCreateStartAnchor",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    if (field != null)
        field.SetValue(grabbable, false);
}
```

### Важно для будущих разработчиков

- **Любой `UxrGrabbableObject` с `_autoCreateStartAnchor = true`**, спавнящийся
  динамически (не через Editor), должен проходить через этот паттерн
  «деактивация -> отключение флага -> активация».
- Альтернатива: отключить `_autoCreateStartAnchor` в самом префабе, но тогда
  оружие не будет работать корректно при дропе на землю (UXR не создаст якорь возврата).

### Альтернатива A: NotifyNetworkSpawn() после Mirror-спавна

`UxrInstanceManager.NotifyNetworkSpawn(instance)` — метод специально для случаев,
когда объект спавнится **внешним сетевым SDK** (Mirror, Netcode и т.д.).
Он уведомляет UXR о новом объекте и обеспечивает корректную генерацию UniqueId
для будущей синхронизации (replays, save-files).

```csharp
// После Mirror-спавна — уведомить UXR
GameObject spawned = Instantiate(prefab);
DisableAutoAnchor(spawned);  // всё ещё нужно!
spawned.SetActive(true);

slot.AssignNetworkItem(spawned);
NetworkServer.Spawn(spawned);

// Уведомить UXR о сетевом спавне
if (UxrInstanceManager.HasInstance)
    UxrInstanceManager.Instance.NotifyNetworkSpawn(spawned);
```

**Плюсы:** UXR корректно трекает объект (replays, state sync).  
**Минусы:** `DisableAutoAnchor` всё ещё необходим — `NotifyNetworkSpawn` не решает
проблему Auto Anchor. Требует `IUxrUniqueId` на объекте + `CombineUniqueId()`.

### Альтернатива B: Спавн через UxrInstanceManager.InstantiatePrefab()

Полностью перейти на UXR-спавн вместо `Instantiate` + `NetworkServer.Spawn`.
UXR сам управляет инстанцированием, уникальными ID и синхронизацией.

**Требования:**
1. Зарегистрировать оружейные префабы в `UxrInstanceManager` (Inspector)
2. На parent-объекте (slot anchor) должен быть компонент с `IUxrUniqueId`
   (например, `UxrSyncObject`)
3. На оружейных префабах должен быть компонент с `IUxrUniqueId`
   (`UxrGrabbableObject` уже реализует его)

```csharp
// Спавн через UXR
var anchorTransform = slot.ItemAnchor.transform;
Vector3 pos = anchorTransform.TransformPoint(slot.WeaponData.WeaponPositionOffset);
Quaternion rot = anchorTransform.rotation * Quaternion.Euler(slot.WeaponData.WeaponRotationOffset);

GameObject spawned = UxrInstanceManager.Instance.InstantiatePrefab(
    slot.WeaponData.WeaponPrefab,
    anchorTransform,  // parent
    pos, rot
);

// Отдельно всё ещё нужно:
// 1. NetworkServer.Spawn(spawned) — чтобы Mirror тоже знал об объекте
// 2. RPC для инициализации на клиентах
```

**Плюсы:**
- Полная интеграция с UXR ecosystem (replays, save-files, state sync)
- `InstantiatePrefab` сам назначает parent, UniqueId

**Минусы:**
- Требует регистрации каждого префаба в Inspector `UxrInstanceManager`
- Двойная синхронизация: UXR sync + Mirror sync — потенциальные конфликты
- `_autoCreateStartAnchor` **может всё ещё сработать** внутри `InstantiatePrefab`,
  т.к. он вызывает `Instantiate` -> `Awake()` напрямую. Нужно тестировать.
- Сложнее интегрировать с Mirror RPC-потоком

### Текущий выбор: Вариант с DisableAutoAnchor (основной)

Выбран как наименее инвазивный и совместимый с текущей Mirror-архитектурой.
Если в будущем проект перейдёт на UXR Networking — рассмотреть Альтернативу B.



