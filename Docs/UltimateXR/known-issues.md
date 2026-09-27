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

В `ArsenalWallController.ReplenishWeaponsNetwork()` инстанцируем оружие **под выключенным
контейнером** (у ребёнка выключенного родителя `Awake` не срабатывает), отключаем флаг
через reflection и только потом выпускаем объект наружу:

```csharp
// 1. Рождаем под выключенным контейнером — Awake не сработает
GameObject spawned = Instantiate(slot.WeaponData.WeaponPrefab, InactiveSpawnRoot, false);

// 2. Отключаем _autoCreateStartAnchor через reflection
DisableAutoAnchor(spawned);

// 3. Выпускаем наружу — здесь и сработает Awake, но Auto Anchor не создастся
spawned.SetActive(true);
spawned.transform.SetParent(null, false);
```

`InactiveSpawnRoot` — пустой выключенный объект, лениво создаваемый под самой стеной,
поэтому уезжает вместе с ней при смене карты.

> **Так было до T-22 — не возвращать.** Раньше вместо контейнера выключали сам префаб:
> `prefab.SetActive(false)` … `prefab.SetActive(wasActive)`. Это мутация **ассета**, а не
> инстанса (находка VR-04): `SetActive` на корне префаб-ассета метит его грязным, и флаг
> не гаснет после возврата состояния — правка утекает в репозиторий. Плюс исключение
> между двумя вызовами оставило бы префаб выключенным насовсем. Сторож —
> `Assets/Tests/EditMode/Arsenal/ArsenalPrefabMutationTests.cs`.

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

---

## Issue 8: Канал состояния SDK рассчитан на «аватар = объект игрока»

**Дата:** 2026-08-18 (задача T-12) · **Статус:** обойдено в проекте, в SDK остаётся

### Описание

`UxrMirrorAvatar` из коробки предполагает, что аватар — это **объект игрока** Mirror,
то есть тот, который передан в `NetworkServer.AddPlayerForConnection`. На этом
предположении держался весь его канал состояния:

- `OnStartLocalPlayer` подписывал локальный аватар на `UxrManager.ComponentStateChanged`;
- оттуда же уходил `CmdNewAvatarJoined` — запрос начального снимка сцены;
- ответ `TargetLoadGlobalState` выставлял `_initialStateLoaded`, без которого
  входящие события состояния молча отбрасывались.

В этом проекте объект игрока — `PlayerSession`, а аватар спавнится отдельно
(`NetworkServer.Spawn(avatar, conn)`): он *owned*, но не *localPlayer*. Значит
`OnStartLocalPlayer` у `UxrMirrorAvatar` **не вызывается никогда**, и весь входящий
канал состояния на клиентах был мёртв — на выделенном сервере до клиентов не доезжали
ни здоровье, ни смерть, ни захваты, ни состояние оружия.

### Почему это не было заметно

На хосте сервер применяет изменения у себя, а `RpcComponentStateChanged` сам себя
игнорирует по `isServer` — флаг не участвует. Отказ проявляется только в связке
«выделенный сервер + отдельный клиент», а это ярус C из [`../testing.md`](../testing.md).

### Что сделано в проекте

Канал вынесен из аватара в `VrBattlegrounds.Network.NetworkStateRelay` — объект уровня
сессии, который сам запрашивает начальный снимок и не зависит ни от объекта игрока,
ни от времени жизни аватара. Подробности — [`sdk-patches.md`](sdk-patches.md), Патч 1.

### На что смотреть при обновлении SDK

Любой код SDK, завязанный на `isLocalPlayer` / `OnStartLocalPlayer` **у аватара**,
в этом проекте не исполняется. Это касается и других интеграций
(`UxrFishNetAvatar`, `UxrPhotonFusionAvatar` — там та же схема со статическим
`s_initialStateLoaded`), если проект когда-нибудь сменит сетевой SDK.

---

## Issue 9: Body IK решается у **всех** аватаров, а не только у локального

> Установлено 2026-08-21 при разборе VR-08. Читалось прямо в исходниках `UxrManager`.

### Симптом

Ошибка обратная привычной: в проекте несколько раз записывали, что правка,
касающаяся тела удалённого аватара, «холостая, потому что IK у него не считается».
Из этого следовал вывод, что позу чужого игрока целиком задают `NetworkTransform`
на костях — а их у большинства аватарных префабов нет.

### Как на самом деле

`UxrManager` в своём цикле обновления разводит две стадии по разным множествам
(`UxrManager.cs:1876-1908`):

| Стадия | Кого обновляет |
|---|---|
| `Animation` | только `AvatarMode == Local`; остальным зовётся лишь `UpdateHandPoseTransforms()` |
| `PostProcess` | **всех** — цикл идёт по `EnabledAvatarControllers` |

`SolveBodyIK()` вызывается из `UxrStandardAvatarController.UpdateAvatarPostProcess()`,
то есть со **второй** стадии. Значит тело удалённого аватара собирает тот же
`UxrBodyIK`, а шею он ставит от мировой позиции камеры
(`UxrBodyIK.cs:227`: `_avatar.CameraComponent.transform`).

### Практическое следствие

1. Чтобы чужой аватар выглядел правильно, достаточно довести до чужой машины
   **позу головы и рук** — тело досчитается само. Реплицировать кости не нужно.
2. Обратная сторона: любое локальное смещение камеры, не доехавшее до чужой машины,
   видно не как «камера не там», а как игрок, стоящий не на своей высоте. Ровно так
   проявлялась находка VR-08.
3. Правка приватных векторов `UxrBodyIK` из `PhysicalSpaceSyncManager.ApplyScaleToAvatar`
   нужна и на чужих аватарах — она не холостая, как утверждал прежний комментарий.

### На что смотреть при обновлении SDK

Если VRMADA переведёт `PostProcess` на `LocalAvatarControllers` (такое свойство в классе
есть и используется на других стадиях), чужие аватары застынут в T-позе или в позе
префаба. Компиляция при этом не сломается.

---

## Issue 10: `Activate On Placed` включается и без вставки — звук `Play On Awake` играет при спавне

> Установлено 2026-09-27 при разборе AUD-01. Подтверждено прогоном в Play Mode.

### Симптом

При загрузке карты раздаётся одновременный «щелчок затвора». Это 16 звуков
`Magazine_attach` — по одному от каждого заряженного M16 на стенах арсенала.

### Как на самом деле

Объект `Activate On Placed` у `UxrGrabbableObjectAnchor` SDK включает в трёх местах,
и только одно из них — действие игрока:

| Где | Когда | Событие `Placed` |
|---|---|---|
| `UxrGrabbableObjectAnchor.Start` (`:427`) | в якоре с самого спавна что-то лежит | нет |
| `UxrGrabManager` (`:969`), цикл по якорям | каждый кадр, пока якорь занят | нет |
| `UxrGrabManager.PlaceObject` | вставка — рукой или программно | да; у вставки рукой `Grabber != null` |

`AudioSource` с `Play On Awake` на таком объекте звучит при каждом спавне заряженного
оружия и при каждой смене карты.

Ещё одна странность `PlaceObject`: при переносе предмета из одного якоря в другой он
включает `ActivateOnPlaced` **старого** якоря (`UxrGrabManager.Manipulation.cs:242`).

### Что сделано в проекте

Звук вставки играет `AnchorSound` по событию `Placed` и только при `Grabber != null`.
На объектах, которые включает якорь, `Play On Awake` выключен — это держит
`AnchorActivationAudioTests`.

### На что смотреть при обновлении SDK

Если VRMADA добавит в `UxrManipulationEventArgs` признак программной вставки или
начнёт слать `Placed` от стартового состояния — пересмотреть фильтр в `AnchorSound`.

---

## Issue 11: префаб UltimateXR переписывает `_uxrUniqueId` при первом сохранении

> Установлено 2026-09-27 при правке `M16_Rifle_prefab`.

Если компоненты `UxrComponent` в префабе-ассете записаны с `__isInPrefab: 0` (префаб
собирали в сцене), `UxrComponent.OnValidate` → `UniqueIdImplementer.NotifyOnValidate`
выдаёт им **новые** `_uxrUniqueId` и ставит `__isInPrefab: 1`. Заодно сбрасываются
редакторские поля превью (`_selectedAvatarForGrips`, `_poseBlendValue`).

В диффе это выглядит как десятки изменённых ID при правке одного поля. Это нормально:
ID префаба-ассета нигде не хранятся, сетевой `UniqueId` в рантайме выводится из `netId`
(`NetworkUxrIdentityTests`). Откатывать такие изменения не нужно — вернутся при следующем
сохранении.

**Исключение — виртуальный игрок Multiplayer Play Mode (MPPM-01, 2026-09-27).** Клон
перевыдаёт id так же, но сохранить не может: у него в памяти случайные id, у хоста — из
файла, и сетевые события не находят компонент. Масштаб: несогласованные флаги у 1389
из 1943 UXR-компонентов в префабах (аватары — варианты `PlayerBase` и наследуют его
`__prefabGuid`). Лечится [патчем 7](sdk-patches.md): в клоне `NotifyOnValidate` id не трогает.

**Уточнение 2026-09-27 (MPPM-02): «вернутся при следующем сохранении» — неверно.** Основной
редактор выдаёт новые id при **реимпорте** префаба (правка его самого, базы варианта,
вложенного) и только помечает компонент грязным — сохранения не происходит, после
перезагрузки домена память снова совпадает с файлом, а следующий реимпорт даёт другие
случайные id. Замер: `ImportAsset(MagGun, ForceUpdate)` — id `d7993c22…` → `6fdae0ac…`,
файл не изменился. Хост MPPM (основной редактор) живёт со случайными id, клон после патча 7 —
с файловыми, и события отвергаются так же, как при MPPM-01. Лечится данными: флаги на диске
приводятся к фактическим, id при этом не меняются. Сторож — `UxrUniqueIdOnDiskTests`.

Откуда неверные флаги берутся снова: **Apply to Prefab** с экземпляра на сцене переносит в
ассет флаги экземпляра (`__isInPrefab: 0`) как обычные переопределения — так `Gun_real` сломался
повторно через полчаса после исправления. Так же пишут `LoadPrefabContents` /
`SaveAsPrefabAsset`: в изолированной сцене `IsInPrefab()` ложно. Поэтому исправление
автоматическое: постпроцессор импорта `UxrUniqueIdPersister` после каждого импорта префаба из
`Assets/Prefabs` правит флаги строками в файле и переимпортирует (основной редактор вне Play
Mode). Ручной прогон по всем — `Tools/VR Battlegrounds/VersionControl/Persist UltimateXR Unique Ids`.

С [патча 10](sdk-patches.md) префаб-ассет с неверными флагами id больше не меняет — корень
устранён в SDK. Постпроцессор остаётся: верные флаги на диске нужны, чтобы экземпляры в сцене
после Apply получали свои id, а не наследовали id первого экземпляра.

## Issue 12: аватар порождает синхронизируемые события до `CombineUniqueId`

> Установлено 2026-09-27 (NET-26), хост + клиент MPPM со шлемом на клиенте.

`UxrAvatar.ControllerInput` при первом обращении с подключённым контроллером зовёт
синхронизируемый `OnControllerInputChanged`. Mirror создаёт аватар в порядке
`Awake`/`OnEnable` → `OnStartClient`, а `UxrMirrorAvatar` выравнивает id только в
`OnStartClient`. Если обращение случилось раньше, событие сериализуется с исходными id
префаба, и другая сторона отвечает `UxrComponentNotFoundException` на **исходный** id
(его можно найти в `.prefab` как `_uxrUniqueId`).

Без контроллера событие не порождается: геттер возвращает dummy раньше. Поэтому в
редакторе без шлема проблема не видна, а на Quest касается каждого клиента.

Обход в проекте, без правки SDK: `AvatarStateEventGate` в `NetworkStateRelay`
придерживает события невыровненного сетевого аватара (`CombineIdSource == Guid.Empty`)
и сериализует их по `UxrMirrorAvatar.AvatarSpawned` — он поднимается сразу после
`CombineUniqueId`. Ждать регистрации на сервере не нужно: id выравниваются локально
из `netId`, который клиент знает уже в `OnStartClient`.

## Issue 13: вторая рука перехватывает предмет вместо хвата двумя руками

> Установлено 2026-09-27, `Gun_real` (основная и дополнительная точки рядом).

Две причины, и обе нужно было снять.

1. **Штраф за близость к занятой точке.** `UxrGrabbableObject.GetDistanceFromGrabber`
   прибавлял 100000 к точке, чьё место ладони (`GripAlignTransform`) ближе
   `MinHandGrabInterDistance` (5 см) к месту ладони на **другой** занятой точке, если у обеих
   `PositionAndRotation`. У `Gun_real` для MEF места разнесены на 2 см — хват поддержки
   обнимает основную руку. Дополнительная точка становилась недосягаемой. Развести места
   нельзя: штраф считается по видимому положению руки. Снято [патчем 11](sdk-patches.md).
2. **Занятая точка не исключается из выбора.** `UxrGrabManager.GetClosestGrabbableObject`
   сравнивает точки одного предмета только по расстоянию, а захват занятой SDK трактует как
   передачу из руки в руку. Основная рукоять ближе ко второй ладони, чем дополнительная
   точка. Снято в проекте: `TwoHandGrabPolicy` через `UxrGrabber.CanGrabDelegate` (его
   ставит `PlayerGrabManager`) запрещает занятую точку, если свободная точка того же
   предмета достижима этой рукой. Нет достижимых свободных — передача из руки в руку работает.
   Цена: если рука у основной рукояти и одновременно в досягаемости дополнительной точки,
   переложить оружие в эту руку нельзя — сначала отпустить первой рукой.

Проверка на настоящих префабах — `GunTwoHandGrabTests`. Вне Play Mode `Awake` не зовётся,
а руки регистрируются в `UxrGrabber.EnabledComponents` именно там: без ручного `Awake`
`GetGrabbingHand` не видит держащую руку, и штраф из п. 1 не воспроизводится.

## Issue 14: вторая рука поворачивает оружие при хвате двумя руками

> Установлено 2026-09-27, `Gun_real` + `MEF_Base_Avatar`.

При хвате двумя руками `UxrGrabManager.SolveUsingLookAtAveraging` всегда доворачивает предмет
к **фактическому** положению второй руки (`RotateObjectTowardsGrab`, центр поворота — основная
рука). Настройки против этого нет: ни `Snap Direction` (`Hand To Object` тоже доворачивает),
ни `First Grab Point Is Main` (он влияет только на позицию). Для винтовки это и нужно, но у
пистолета точки хвата в 2 см друг от друга: сдвиг второй руки на 3 см поворачивает его на 126°.

Обход в проекте, без правки SDK: `MainGripAimLock` на префабе оружия. Порядок кадра в
`UxrGrabManager.UpdateManipulation`: решение позы → плавные переходы → `ConstraintsApplied` →
`KeepGripsInPlace`. Компонент в `ConstraintsApplied` возвращает позу относительно основной руки,
запомненную при хвате одной рукой, а `KeepGripsInPlace` затем прилепляет вторую руку к рукояти.
Проверка — `GunTwoHandAimTests` (без компонента 66° на сдвиг руки в 3 см, с ним меньше 0.5°).

## Issue 15: `Activate On Hand Near And Grabbable` и `PlacedObjectRangeEntered` не срабатывают

> Установлено 2026-09-27, прогоном в Play Mode (`Gun_real` в `Anchor_Hip_R`, ладонь на рукояти).

`UxrGrabManager.UpdateAffordances` находит руку, которая может взять предмет из якоря, но
записывает в `GrabberNear` не её, а `null` (`UxrGrabManager.cs`, первый проход по пустым рукам:
`_grabbableObjectAnchors[anchorCandidate].GrabberNear = null`). В прогоне: `GrabPointNear = 0`,
`LastValidGrabberNear = null`, событие `PlacedObjectRangeEntered` — 0 раз. Поэтому не работают
ни событие, ни поле якоря `Activate On Hand Near And Grabbable`.

Даже исправленное, оно не годилось бы для карманов проекта: меряет до точек хвата самого
лежащего предмета, а не до прокси, и не видит `UxrMagazinePocket` (магазины спрятаны, якорь
для SDK пуст). Проект вместо этого спрашивает `GetClosestGrabbableObject` — тот же вызов, что
у нажатия grip (`PocketReadiness`). Годный сигнал «рука возьмёт это» у SDK — `Enable When Hand
Near` точки хвата (считается тем же вызовом), но он общий для всех аватаров и не знает руку.

Парный сигнал «принять» (`AnchorRangeEntered/Left`, `Activate On Compatible Near`) работает,
но не проверяет, что предмет держит одна рука, — при хвате двумя сигналит, хотя отпускание
ничего не положит. `PocketReadiness` добавляет это условие сам.

## Issue 16: пустой `Compatible Tags` у якоря принимает только предметы без тега

> Установлено 2026-09-27, прогоном в Play Mode (стена арсенала в `Lobby`).

`UxrGrabbableObjectAnchor.IsCompatibleObjectTag` при пустом списке `Compatible Tags` возвращает
`string.IsNullOrEmpty(otherTag)`: пустой список значит «только предметы **без** тега», а не «любые».
У якорей слотов стены арсенала список был пуст, у всего оружия тег есть (`M16_Rifle`, `Shotgun`,
`Gun`) — замер показал `IsCompatibleObject = false` у всех слотов. Повесить ствол обратно руками
было нельзя. На стене оружие держалось только потому, что выдача кладёт его в якорь напрямую
(`SetNetworkAnchor`), минуя проверку совместимости.

Решение в проекте, SDK не тронут: `ArsenalSlotController.ConfigureAnchorCompatibility` в `Awake`
добавляет якорю тег префаба, который слот выдаёт, и валидатор (`AddPlacingValidator`) — только
оружие того же `WeaponInfo` и только на открытой стене. Одного тега мало: у `Gun_real` тег
`M16_Rifle`. Проверка — `LooseItemTests.Слот_принимает_своё_оружие_обратно`.

## Issue 17: менеджер захвата не знает об уничтоженной руке

> Установлено 2026-09-27, прогоном в Play Mode (хост в `Lobby`, смена скина с оружием в руках).

`UxrGrabber` в `OnDisable`/`OnDestroy` зовёт `UxrGrabManager.ReleaseObject`, но первой строкой
там `base.OnDisable()`, после которой рука уже не `isActiveAndEnabled` и выпадает из
`UxrGrabber.EnabledComponents`. `ReleaseObject` начинается с проверки
`EnabledComponents.Any(grb => grb.GrabbedObject == obj && grb == grabber)` — и молча выходит.
Итог: рука, уничтоженная или выключенная с предметом, **ничего не отпускает**, а в
`_currentManipulations` остаётся запись с Unity-null рукой. `UxrGrabbableObject.OnDestroy`
снимает захват только у самого предмета — о руке менеджер не узнаёт никак.

Проявление: первый же `UxrAvatar_GlobalAvatarMoved` (телепорт, поворот, любое перемещение
аватара) бросает `MissingReferenceException` на `g.Avatar` мёртвой руки. Телепорт и поворот
с затемнением (`UxrTranslationType.Fade`, `RotateLocalAvatarCoroutine`) гасят экран до
перемещения — корутина обрывается, `UxrCameraFade` остаётся с альфой 1: **чёрный экран**.
Каждый кадр падают и запросы `IsBeingGrabbedBy` (у нас — `GrabOnlyWhenParentHeld.AllowsGrab`).

В проекте так уничтожался аватар при смене скина/команды (`AvatarManager.ChangeAvatar`) и при
отключении игрока. Смену карты это не задевает: предметы гибнут вместе со сценой, и их
`GlobalDisabled` стирает запись.

Решение — два слоя:
- **игра:** сервер перед уничтожением аватара отпускает руки и снимает снаряжение —
  `AvatarTeardown.ReleaseBeforeDestroy` (зовут `AvatarManager.ChangeAvatar` и
  `GameNetworkManager.OnServerDisconnect`);
- **SDK (патч 12 в [sdk-patches.md](sdk-patches.md)):** `UxrGrabManager` каждый кадр и перед
  обработкой перемещения аватара вычищает записи с уничтоженным предметом или рукой.

Проверка — `AvatarTeardownTests`. Правило на будущее: уничтожаешь или выключаешь объект с
`UxrGrabber` — сначала отпусти его предмет сам.

---

## Issue 18: команда захвата о предмете, уничтоженном сервером, рвёт соединение

**Симптом.** `Disconnecting connection: connection(N) because handling a message of type
Mirror.CommandMessage caused an Exception … NullReferenceException` со стеком в
`UxrMirrorAvatar.CmdRequestAuthority`; на хосте — `OnChangeOwner: Could not find object with netId`.

**Причина.** Захват шлёт серверу `CmdRequestAuthority(NetworkIdentity)`. Если сервер уничтожил
предмет раньше, чем команда дошла, Mirror отдаёт null, и SDK разыменовывает его без проверки.
Исключение в обработчике команды Mirror считает атакой и разрывает соединение отправителя.

**Когда.** Любое серверное уничтожение предмета в момент захвата: в проекте — снятие
снаряжения при смене режима, паузе, смене карты (`EquipmentStrip`).

**Решение.** Патч 13 в [`sdk-patches.md`](sdk-patches.md): проверка на null.

## Issue 19: деталь из пака Hands Weapons Animations съезжает, если ставить её по трансформу рендерера

> Установлено 2026-09-27 при сборке `Shotgun_real`. Это не UltimateXR, а сторонний пак — но
> встречается ровно при интеграции оружия в UltimateXR.

**Симптом.** Меш детали (затвор, спуск, помпа), перенесённый из `SkinnedMeshRenderer` пака в
`MeshFilter` с тем же трансформом, стоит не на своём месте; приходится двигать на глаз.

**Причина.** Каждая деталь пака жёстко привязана к одной кости, и вершины ставит
`кость × bindpose`, а не трансформ рендерера. Они расходятся (до 1.5 в элементах матрицы).
Клипы пака двигают те же кости, поэтому ход и углы деталей тоже надо брать из костей.

**Решение.** `HandsPackWeapon.PartInBody` (`Assets/Editor/VR_Battlegrounds/Gameplay/`), сборщик —
`HandsPackWeaponBuilder`, маршрут — скилл `/add-weapon`. Проверка — `HandsPackWeaponTests`.
