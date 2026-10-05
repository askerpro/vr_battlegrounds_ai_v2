# UltimateXR — Известные проблемы и неочевидные поведения

> Обновлять при обнаружении новых побочных эффектов, неочевидных поведений SDK,
> или исправлении ошибок в UltimateXR.
> Этот файл — первое место, куда смотрит ИИ при расследовании любого бага.

---

### MEF: переплетённые пальцы в grip preview при хорошем хвате в игре (2026-10-05)

Класс ошибки: Editor-preview считал skinning по абсолютным матрицам костей исходных рук
`TransformRelativeToHand`; runtime переносит универсальные ориентации позы на целевой риг.
У импортированных/зеркальных MEF поз матрицы не соответствуют MEF костям. `Mirror` и
интерполяция дескриптора также не обновляют эти cached matrices. Для Cyborg совпадение
исходного и целевого рига маскировало дефект. Настройки AK105 и сами pose assets менять не нужно.

[Патч45](sdk-patches.md#патч-45-grip-preview-использует-игровое-применение-позы-и-скиннинг-unity)
переводит preview на штатный `UxrAvatarRig.UpdateHandUsingDescriptor` и Unity BakeMesh
в собственной preview-сцене, без активации SDK components и изменения исходного аватара.
Native RED→GREEN: MEF Grip/Support Left/Right RMS85,6–144,3 мм → maxменее0,0004 мм;
Cyborg Fixed/Blend — контроль. Постоянные Editor NUnit методы15/15, Android PASS.

После перекомпиляции пересоздать уже открытое grip preview: у Grabbable переключить
`Preview Grip Pose Meshes` на `None`, затем вернуть нужную руку. Машинная проверка подтверждает
деформацию той же позы; положение wrist/контроллера, IK, фактическая хватательная точка и Quest
проверяются отдельно. Пользователь принял MEF preview; рентген-анализатор теперь читает
текущую форму через этот SDK core и размещает её на выбранном snap. Новый UI/cache срез
ожидает отдельной проверки навигации.

## Issue 30: Place Snap On Grabbable Object использует левый граббер для обеих кнопок

На 2026-10-04 исходники `UxrHandPoseEditorWindow.TryPlaceSnapTransform` показывают: кнопки Left/Right передают сторону, но поиск граббера содержит жёсткое условие `grabbers[i].Side == UxrHandSide.Left`. Поэтому правая кнопка создаёт объект с суффиксом Right в трансформе левого граббера. Это ошибка выбора стороны, не подгонки пальцев.

Кнопка требует один выбранный GameObject с `UxrGrabbableObject` в Hierarchy, отказывается работать с persistent prefab, спрашивает создание, создаёт дочерний объект `poseName + Left/Right` с `UxrGrabbableObjectSnapTransform`, копирует world position/rotation граббера и регистрирует Undo. Сам метод не изменяет кости/позу, не проверяет контакт и не назначает новый transform в GripPoseInfo.

Назначение утилиты — сохранить уже выставленное человеком положение руки относительно предмета для последующей настройки точки хвата. Название Snap не означает автоматическое обхватывание меша. SDK в этой задаче не исправлен: исследование и анализатор не вызывают эту кнопку. При отдельном исправлении выбирать граббер по переданному `handSide` и проверять обе стороны. [Рабочий анализатор](../hand-pose-fit-tool.md).

## Как пользоваться этим файлом

### Визуализация касания пальцем и лазер из руки (2026-10-04)

Исходный `UxrFingerTip` содержит ввод и сведения о кончике, но не runtime-визуализатор.
`UxrLaserPointer` создаёт LineRenderer и метку попадания для отдельного лазерного ввода;
его автонаведение работает только с канвасами `LaserPointers`. Для диагностики касания
нельзя добавлять его вместо вида кончика, сохраняя канвас `FingerTips`.
В проекте добавлен [патч 40](sdk-patches.md#патч-40-визуализация-луча-uxrfingertip)
с opt-in API `UxrFingerTip.RayVisualizationEnabled`. Он читает готовое событие касания
после обновления аватара. Конец луча и метка используют одну мировую точку; за плоскостью
контакта отрезок скрывается. Постоянные 8 см при наличии контакта — ошибка визуализации,
а не свидетельство того, что SDK принял касание за поверхностью.
Дальность теперь общая с вводом — `UxrCanvas.FingerTipMinHoverDistance` (планшет 5 см).
Raycaster читает эту же настройку, не сохраняя отдельный действующий снимок.
Сохранённый `AutoEnableDistance = 5` относится к лазеру из руки и не участвует в
fingertip-вводе или его визуализации.

### После спавна бота планшет перестаёт принимать касания (2026-10-04)

**Статус:** исправлено в SDK; причина подтверждена снимком живого Play Mode,
preview-регрессия камеры 13/13 PASS. Повторная проверка в шлеме остаётся этапом приёмки.
У активного планшета `Canvas.worldCamera` указывала на выключенную камеру
`Бот 1 (Remote)`, хотя `UxrAvatar.LocalAvatar` оставался игроком. EventSystem,
`UxrPointerInputModule`, fingertip-raycaster, CanvasGroup и активные кнопки включены;
обработчики нажатия и `raycastTarget` кнопок сохранены, обе руки допущены к UI.
Ошибок в консоли не было. Перед спавном лог фиксирует касания кнопок, включая
`Btn_Добавить бота`; после него штатная цель пальца пропадает.

Класс ошибки — глобальная камера UI назначается до определения сетевого владельца.
`UxrManager.Avatar_Enabled()` считает любой включившийся аватар с временным
`AvatarMode.Local` локальным и назначает его камеру всем `UxrCanvas`. Бот создаётся
из обычного активного префаба; затем `UxrMirrorAvatar.InitializeNetworkAvatar()`
переводит его в `UpdateExternally` и выключает камеру, но ссылка канваса не восстанавливается.
Такое же окно есть при появлении чужого игрока, а не только бота.

`UxrFingerTipRaycaster` проецирует мировую точку через `eventCamera`, затем вызывает
`RectangleContainsScreenPoint`. В снятом состоянии кнопки позади камеры бота
(`WorldToScreenPoint.z` около −3.36 м), проверка прямоугольника возвращает false.
Оранжевый предварительный вид при этом возможен: он проверяет плоскость экрана
без камеры, а штатной цели SDK нет. [Патч 41](sdk-patches.md#патч-41-камера-ui-не-переходит-к-временно-локальному-сетевому-аватару)
использует каноническую `UxrAvatar.LocalAvatarCamera` и обновляет автопривязку перед
SDK-вводом при смене камеры. Бот и чужой игрок больше не перехватывают её;
смена скина, неактивные канвасы и ручное назначение камеры также проверены.

### Fade остаётся после исключения при перемещении аватара (2026-10-02)

`TeleportLocalAvatarRelativeCoroutine` включает затемнение до `MoveAvatarTo`. Если перенос захваченного
предмета падает, обратная часть Fade не выполняется. Подтверждённый стэк: `UxrGrabManager`, обращение
к `grabbableChild.transform` уничтоженного `UxrGrabbableObject`. Класс ошибки — кеш зависимостей
сохранял уничтоженные детали после смены экипировки. Все пять списков зависимостей теперь очищаются
в общей точке доступа от Unity-null и `IsBeingDestroyed`; регрессия проверяет каждый список.
`CameraFadeDiagnostics` пишет операции Fade и стэк инициатора через `GameLog.Debug.Info`.
Это устраняет подтверждённую причину; другие исключения между включением и снятием Fade диагностируются
по новым логам, универсальное восстановление после любых исключений пока не реализовано.

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

## Issue 20: каждый remote-аватар стоит ~4 мс на шлеме — подсветка хвата для чужих рук

**Компоненты:** `UxrGrabManager.UpdateAffordances`, `UxrGrabber`
**Статус:** ✅ Исправлено (патч 16)
**Дата:** 2026-09-28

### Симптом

Стресс-тест на Quest 3: 9 remote-аватаров (куклы) — 72 → 18 FPS. Рендер и скиннинг почти не
растут; всё время — в `LateUpdate`, то есть в `UxrManager.PostUpdate`. Тот же рост в редакторе.

### Причина

Разбивка по стадиям `UxrManager` (`StageUpdating/StageUpdated`) показала стадию `Manipulation`
(36,5 мс), IK (`PostProcess`) — 2 мс. Внутри — `UxrGrabManager.UpdateAffordances`: локальная
подсветка «рядом можно взять» считается для рук **всех** аватаров, по ~1 мс на руку поиск
ближайшего предмета плюс перебор всех якорей. Касается не только кукол: каждый живой игрок в
матче на шлеме стоил бы столько же.

### Исправление

Патч 16 (`sdk-patches.md`): руки не-локальных аватаров в эти проходы не входят.

## Issue 21: руки remote-аватаров не двигаются — `NetworkTransform` кистей в локальных координатах

**Компоненты:** `NetworkTransformUnreliable` на кистях аватаров, `UxrArmIKSolver`
**Статус:** ✅ Исправлено
**Дата:** 2026-09-28

### Симптом

В сетевой игре (выделенный сервер) руки чужих игроков стоят на месте: тело и голова двигаются,
пальцы (позы, канал состояния) меняются, а кисть не уходит от тела. На хосте незаметно — там
сети нет. Нашёл стресс-тест: у всех кукол на клиенте.

### Причина

У всех `NetworkTransform` аватаров `coordinateSpace = Local`. Для кисти это синхронизирует
позу относительно предплечья. `UxrArmIKSolver` (конец решения, `Forearm.SetPositionAndRotation`,
затем `Hand.SetPositionAndRotation`) ставит предплечье так, чтобы дотянуться до кисти, и
возвращает кисти мировую позу — после IK локальная позиция кисти всегда равна длине кости.
Владелец шлёт константу; у получателя кисть встаёт на его предплечье, IK видит цель достигнутой
и руку не двигает. Меняется только поворот запястья.

Сам UltimateXR (`UxrMirrorNetwork.SetupAvatar`) вешает эти компоненты с `worldSpace = true`.

### Исправление

На кистях всех аватаров `coordinateSpace = World`; у `Heavy_Soldier_Base_Avatar` добавлены
недостающие компоненты на `Wrist_Left/Right` (настройки — с камеры). Камера оставлена в `Local`:
её родитель неподвижен относительно корня, решение VR-08 в силе. Сторож —
`PrefabCompositionTests.У_каждого_аватара_кисти_несут_NetworkTransform`.

## Issue 22: гасить детали аватара — только `forceRenderingOff`

**Компоненты:** `UxrAvatar`, `SkinnedMeshRenderer`
**Статус:** ℹ️ Поведение SDK
**Дата:** 2026-09-28

`UxrAvatar.Awake` включает `updateWhenOffscreen` у всех скинов аватара, а `UxrAvatar.RenderMode`
(в том числе в `Start`) переписывает `enabled` у всех рендереров. Выключенный через `enabled` рендерер
аватара вернётся сам; гасить детали (как зубы и глаза Heavy под маской в `RemoteAvatarRenderOptimizer`)
нужно через `forceRenderingOff`, а `updateWhenOffscreen` менять после `Awake`.

## Issue 23: в сетевой игре выстрел даёт две пули, сервер считает урон дважды

**Компоненты:** `UxrFirearmWeapon`, `UxrProjectileSource`, `NetworkStateRelay`
**Статус:** ✅ Исправлено (патч 23 + `StateEventAuthority`)
**Дата:** 2026-09-28

### Симптом

С выделенным сервером пистолет стреляет двумя пулями сразу; на хосте — одной.

### Причина

Две архитектурные дыры сложились вместе.

1. **У выстрела два источника правды.** UltimateXR пересчитывает выстрел на каждой машине по
   синхронизированному спуску и одновременно синхронизирует сам `UxrProjectileSource.Shoot`. Снаряд
   рождался дважды на каждой машине, кроме стрелка.
2. **Канал состояния не знал, кто автор.** В эталонной схеме UltimateXR (`UxrFishNetAvatar`) событие
   шлёт только владелец. `NetworkStateRelay` (патч 1) ретранслировал всё, что родилось на машине, —
   в том числе пересчитанные действия чужих игроков: серверный `Shoot` копии возвращался стрелку
   второй пулей.

### Исправление

- Патч 23: выстрел решает только машина стрелка; остальные получают его одним событием `Shoot` и
  по нему же играют звук и отдачу (`ProjectileShotReplayed`). Это снимает двойной урон на сервере.
- `StateEventAuthority` (`Assets/Scripts/Network/`): событие компонента на предмете в руке шлёт только
  автор держащего аватара (свой — `isOwned`; аватар без владельца — сервер). Вне правила
  `UxrGrabManager` (автор в аргументах, сервер законно отпускает предметы) и `UxrActor` (здоровье
  серверное). Отброшенное считается по «Тип.Метод» и один раз пишется в лог — список компонентов,
  которые ещё пересчитывают чужие действия (кандидаты: затвор, граната, магазин).

### Тот же класс «источник + эффект» — аудит 2026-09-28

Правило: копия чужого действия не вызывает синхронизируемых методов; эффект у получателя создаёт
только событие автора. Событие, исполненное внутри `ExecuteStateSyncEvent`, и вложенный
синхронизируемый вызов в сеть не уходят (`UxrManager.cs:1631`, `:1719`) — опасен только вызов
**верхнего уровня** на машине, которая не автор (Update, таймер, физика, Mirror RPC/хук).
Кто автор — `StateEventAuthority.IsAuthorOfItem` (предмет) и `IsWorldAuthority` (мир).

| Где | Что было | Статус |
|---|---|---|
| `PlayerController.RpcOnDied` → отпускание предметов умершего | каждый клиент отпускал сам (своя скорость броска) и рассылал отпускание | ✅ страхует только владелец (`isOwned`) |
| `AutomaticWeaponSlideFeedback` → `_firearm.Reload` | затвор на копии двигается по чужой руке — `Reload` звали все копии, у брошенного оружия канал не отсекал | ✅ `Reload` — только автор оружия (`IsAuthorOfItem`); звук у всех |
| Арсенал: `IsGrabbable` слотов и жетона | переход фазы на каждой машине писал синхронизируемое свойство | ✅ пишет только сервер (`IsWorldAuthority`) |
| `UxrGrenadeWeapon` (таймер, `Explode`) | взрыв на каждой машине в своё время и месте; на выделенном сервере NRE на `LocalAvatar` | ⏳ не исправлено: в префабах игры не используется |

Урон дублей не даёт: `Life`, `DieInternal`, `PlayDamageEffects` — только у сервера
(`NoSessionOrSessionOwner`), декали и частицы попадания — локально, по разу на машину.

## Issue 24: родителя префаба-варианта нельзя сменить через API; прогон тестов сохраняет открытые сцены

### Симптом

`PrefabUtility.ReplacePrefabAssetOfPrefabInstance` на корне варианта бросает «Replacing the Variant parent is
not supported». Переписать ссылки YAML «в лоб» (`{fileID: X, guid: старый}` → объект новой базы) — ломает
внешние ссылки: `AvatarData.prefab` пустеет, у экземпляра аватара в сцене сотни переопределений без цели.

### Причина

Объект, унаследованный через экземпляр префаба, имеет id `(id в источнике ^ id экземпляра) & 0x7FFF…`.
Сменился источник — сменились id всех унаследованных объектов варианта, включая корень, на который
ссылаются сцены и данные. Вторая грабля: `run_tests` сохраняет изменённые открытые сцены перед прогоном —
сцена с временно сломанными ссылками уходит на диск.

### Решение

`AvatarHandBases.Rebase` (`Assets/Editor/VR_Battlegrounds/Avatars/AvatarHandBases.cs`): новая база —
вариант того же родителя с экземпляром `P`; цели переписываются в `X ^ P`, а id экземпляра в варианте —
в `I ^ P`. Тогда `(X ^ P) ^ (I ^ P) = X ^ I` — id объектов варианта не меняются. Формула проверяется на
stripped-объектах до записи. Перед переносом — слепок всех полей префаба и сверка после; висящие
переопределения в сцене чистит `PrefabUtility.RemoveUnusedOverrides`.

## Issue 25: «синхронизируется» не значит «попадает в снимок» — поздний клиент видит префаб

### Симптом

Клиент, вошедший посреди матча (или переподключившийся, или после смены карты), видит у раненых 100 хп,
а у выбывших — призрака, но `IsAlive == true`. У остальных клиентов всё верно.

### Причина

В UltimateXR два независимых механизма: сеттер свойства шлёт событие (`EndSyncProperty`), а начальный
снимок (`SaveStateChanges`, его отдаёт `NetworkStateRelay`) пишет только то, что компонент перечислил в
`SerializeState`. События, пришедшие до снимка, клиент отбрасывает. Свойство, забытое в `SerializeState`,
у опоздавшего остаётся префабным до следующего изменения. Компонент, не пишущий в снимок ничего, SDK не
регистрирует вовсе. Так было с `UxrActor.Life`.

### Решение

Патч 29 (`sdk-patches.md`): `UxrActor.StateSave.cs`. Весь класс сторожат два теста: `StateSnapshotCoverageTests`
(каждое `EndSyncProperty`-поле SDK и игры — в `SerializeState` или в исключениях с причиной) и
`RpcCarriesNoStateTests` (каждый `[ClientRpc]`/`[TargetRpc]` игры записан как событие, а не состояние).
Правило: **состояние — `SyncVar`/`SyncList` или снимок; RPC и событие-метод — только эффекты.**

## Issue 26: нет записи хвата для аватара — SDK молча берёт default, предмет встаёт в ладонь пивотом

### Симптом

Одним аватаром предмет (планшет, оружие, магазин, жетон) берётся нормально, другим — встаёт в ладонь углом
или центром, пальцы сжаты общей позой или чужой позой под другой скелет. Ошибок в консоли нет.

### Причина

Хват настраивается в точке хвата `UxrGrabbableObject` отдельной записью `UxrGripPoseInfo` на каждый префаб
аватара (по GUID). `UxrGrabPointInfo.GetGripPoseInfo(avatar)` идёт по `UxrAvatar.GetPrefabGuidChain()`
(сам префаб → `_parentPrefab` → … → `PlayerBase`) и берёт первую запись; не нашёл — возвращает
`DefaultGripPoseInfo`. В default нет точек выравнивания рук, и `GetGrabPointGrabAlignTransform` отдаёт сам
предмет — ладонь садится на его пивот. Поза пальцев берётся по имени из `HandPose` записи
(`UxrStandardAvatarController.UpdateGrabPoseInfo`); если такого имени у аватара нет — общая `Grab` без
предупреждения. Своя запись без точки выравнивания руки даёт тот же пивот.

Запись на базе кисти (`PlayerBase_SdkHands` / `PlayerBase_NonSdkHands`) покрывает все её варианты — это
основной способ не множить записи. Призрак выбывшего (T-35) — вариант киборга и наследует его записи; берёт он
только планшет.

### Сторож

`GrabPoseCoverageTests` — каждый хватаемый предмет × каждый аватар реестра, по тест-кейсу на пару.
Новый предмет в `Assets/Prefabs` или `WeaponInfo`, новый аватар в `AvatarRegistry` попадают под проверку сами.

## Issue 27: у хоста `[ClientRpc]` на уничтожаемом в том же кадре объекте теряется

**Симптом.** Играя хостом, не видишь трупов (T-35); у удалённых клиентов то же самое работает.

**Причина (Mirror).** Удалённому клиенту RPC и `ObjectDestroyMessage` приходят одним пакетом по порядку — RPC
исполняется, пока объект жив. Свой клиент хоста разбирает очередь локального соединения только на следующем
кадре, а `NetworkServer.Destroy` уже уничтожил объект — RPC молча отбрасывается.

**Правило.** Действие «до уничтожения» (эффект гибели, отпускание локального) хост исполняет сам и сразу
(`if (isClient) …`), а в обработчике RPC — `if (isServer) return;`. Образец — `PlayerController.ServerBecomeCorpse`,
`ServerReleaseLocalItems`; проверка — `HostDeathEffectsTests`.

## Issue 28: предпросмотр перенесённой позы показывает другую геометрию кисти

При оценке чужой позы на целевом аватаре меш в инспекторе `UxrGrabbableObject` может существенно отличаться от руки после непосредственного применения той же позы. Это отдельный класс ошибки представления: перенос ориентаций не гарантирует корректность сохранённых матриц для другой геометрии кисти.

`UxrPreviewHandBoneInfo.TryResolveBoneInfo` использует `UxrFingerNodeDescriptor.TransformRelativeToHand` из ассета позы для построения меша предпросмотра. `UxrAvatarRig.UpdateHandUsingDescriptor` вместо этого пересчитывает повороты на костях целевого рига. Поэтому пригодность штатного превью для другой кисти нужно проверять отдельно.

Изолированный probe от 2026-10-04 сравнил предпросмотр SDK с `SetCurrentHandPoseImmediately` + `BakeMesh(false)` на MEF, с точным сопоставлением исходных индексов вершин. У основной точки Viper RMS слева/справа — 159,91/118,07 мм, у TR15 — 153,01/114,00 мм; у `Gun_real` обе стороны меньше 0,001 мм. Это расхождение двух представлений руки, не зазор до оружия и не доказательство причины плохого хвата в игре.

Для редакторного анализа получать меш после применения позы к временному экземпляру целевого аватара. Патч SDK и регрессионный тест этого класса пока не реализованы; реальные позы и префабы исследование не меняло. Методика, ограничения и рекомендуемый дизайн — [оценка посадки позы руки](../hand-pose-fit-analysis.md), полный вывод — [машинный отчёт](../tasks/report/hand-pose-fit-2026-10-05/history/hand-pose-fit-research-2026-10-04.json).
