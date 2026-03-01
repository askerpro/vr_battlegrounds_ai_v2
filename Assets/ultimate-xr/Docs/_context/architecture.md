# UltimateXR — Архитектура и структура модулей

> Обновлено: первичное сканирование кодовой базы и документации  
> Источники: `Runtime/Scripts/`, `Docs/guides/`

---

## Базовые классы (иерархия наследования)

```
MonoBehaviour
    ??? UxrComponent                   ? основа всех компонентов UXR
            ??? UxrComponent<T>        ? EnabledComponents / AllComponents для типа T
            ??? UxrAvatarComponent<T>  ? компоненты, привязанные к конкретному аватару
            ??? UxrSingleton<T>        ? паттерн Singleton (Instance)
```

**Правило:** любой компонент UltimateXR имеет:
- `UniqueId` — уникальный идентификатор (для сети и сохранений)
- `EnabledComponents` — статическая итерация по всем активным компонентам типа
- `GlobalEnabled` / `GlobalDisabled` — события включения/выключения

---

## Центральный менеджер — `UxrManager`

**Путь:** `Runtime/Scripts/Core/UxrManager.cs`  
**Доступ:** `UxrManager.Instance` (Singleton, создаётся автоматически)

**Отвечает за:**
- Обновление всех аватаров каждый кадр в правильном порядке
- Перемещение / телепортацию аватаров
- Единую точку всех изменений состояния (`ComponentStateChanged`)
- Сериализацию состояния сцены (save / load / replay / sync-on-join)
- Прекэширование префабов при загрузке (`IUxrPrecacheable`)

**Ключевые события:**

| Событие | Когда |
|---|---|
| `AvatarsUpdating` / `AvatarsUpdated` | До/после обновления аватаров |
| `StageUpdating` / `StageUpdated` | Каждый этап кадра |
| `AvatarMoved` | Аватар переместился |
| `ComponentStateChanged` | Любое изменение состояния компонента в сцене |

---

## Модуль 1: Avatar — `Runtime/Scripts/Avatar/`

| Класс | Назначение |
|---|---|
| `UxrAvatar` | Главный компонент аватара |
| `UxrAvatarController` | Базовый класс логики управления |
| `UxrStandardAvatarController` | Стандартная логика: жесты рук, взаимодействие |
| `UxrAvatarRig` | Описание скелета (кости рук, голова, тело) |
| `UxrAvatarHand` | Данные одной руки (кости пальцев) |

**Префабы:** `BigHandsAvatar_URP`, `SmallHandsAvatar_URP` (в `Runtime/Prefabs/Avatars/`)  
**Подробнее:** `Assets/ultimate-xr/Docs/_context/avatar-guide.md`

---

## Модуль 2: Manipulation — `Runtime/Scripts/Manipulation/`

| Класс | Назначение |
|---|---|
| `UxrGrabbableObject` | Делает объект захватываемым |
| `UxrGrabber` | Компонент руки (в BigHandsIntegration) |
| `UxrGrabManager` | Синглтон управления захватами (авто-создаётся) |
| `UxrGrabbableObjectAnchor` | Точка размещения объекта |
| `UxrGrabPointShape` | Расширенные формы точек захвата |

**Подробнее:** `Assets/ultimate-xr/Docs/_context/interactions.md`

---

## Модуль 3: Locomotion — `Runtime/Scripts/Locomotion/`

| Класс | Назначение |
|---|---|
| `UxrLocomotion` | Базовый класс любого передвижения |
| `UxrTeleportLocomotion` | Телепортация через дугу из контроллера |
| `UxrSmoothLocomotion` | Плавное FPS-подобное передвижение |

**Подробнее:** `Assets/ultimate-xr/Docs/_context/locomotion.md`

---

## Модуль 4: Mechanics / Weapons — `Runtime/Scripts/Mechanics/Weapons/`

| Класс | Назначение |
|---|---|
| `UxrWeapon` | Базовый класс оружия |
| `UxrFirearmWeapon` | Огнестрельное оружие (стрельба, патроны, отдача) |
| `UxrFirearmTrigger` | Один триггер: тип снаряда, цикл, частота, отдача |
| `UxrProjectileSource` | Источник снарядов (обязателен на том же GameObject) |

> `UxrFirearmWeapon` требует `UxrProjectileSource` на том же объекте.  
> Оружие является `UxrGrabbableObject` — его можно подбирать руками.

```csharp
firearmWeapon.ProjectileShot += (triggerIndex) => { /* выстрел */ };
```

---

## Модуль 5: UI — `Runtime/Scripts/UI/`

| Класс | Назначение |
|---|---|
| `UxrPointerInputModule` | Замена Unity EventSystem модуля для VR |
| `UxrCanvas` | Добавляется на Canvas для VR-взаимодействия |
| `UxrLaserPointer` | Лазерный указатель с руки |
| `UxrFingerTip` | Прямое касание UI пальцем |

**Подробнее:** `Assets/ultimate-xr/Docs/_context/ui.md`

---

## Модуль 6: CameraUtils — `Runtime/Scripts/CameraUtils/`

| Класс | Назначение |
|---|---|
| `UxrCameraWallFade` | Затемнение при столкновении головы со стеной |

**Режимы:**
- `AllowTraverse` — затемнение при прохождении сквозь геометрию
- `Strict` — экран чёрный до возврата на прежнее место

```csharp
bool peeking = UxrCameraWallFade.IsAvatarPeekingThroughGeometry(UxrAvatar.LocalAvatar);
```

---

## Модуль 7: Networking — `Runtime/Scripts/Networking/`

| Класс | Назначение |
|---|---|
| `UxrNetworkManager` | Синглтон сетевой синхронизации |
| `UxrNetworkImplementation` | Абстрактный адаптер под конкретный сетевой SDK |
| `UxrNetworkVoiceImplementation` | Адаптер голосового чата |

**Принцип:** все изменения ? `UxrManager.ComponentStateChanged` ? сериализация ? сеть ? `ExecuteStateSyncEvent` на другом устройстве.

---

## Модуль 8: Animation / Tweening — `Runtime/Scripts/Animation/`

| Класс | Назначение |
|---|---|
| `UxrAnimatedTransform` | Анимация позиции/поворота/масштаба |
| `UxrAnimatedMaterial` | Анимация параметров материала |
| `UxrCanvasAlphaTween` | Fade in/out для Canvas |
| `UxrTextContentTween` | Анимация текста (эффект печатной машинки) |
| `UxrInterpolationSettings` | Настройки интерполяции (easing, delay, loop) |

---

## Модуль 9: Devices — `Runtime/Scripts/Devices/`

Абстракция над контроллерами. Определяет устройство автоматически.  
Поддерживает: Oculus/Meta Quest, SteamVR, WaveXR, PicoXR, Windows Mixed Reality.

---

## Диаграмма зависимостей

```
UxrManager (Singleton)
    ??? обновляет ??? UxrAvatar
    ?                     ??? управляется ??? UxrStandardAvatarController
    ?                                               ??? содержит ??? UxrGrabber (руки)
    ?
    ??? управляет ??? UxrGrabManager (Singleton, авто)
    ?                     ??? следит за ??? UxrGrabbableObject
    ?                                             ??? UxrFirearmWeapon (оружие)
    ?
    ??? управляет ??? UxrNetworkManager (Singleton)
    ?
    ??? события ????? ComponentStateChanged ??? Network / Replay / Save
```

---

## Статус изучения модулей

| Модуль | Путь | Статус |
|---|---|---|
| Core / UxrManager | `Runtime/Scripts/Core/` | ? Базовое знакомство |
| Avatar | `Runtime/Scripts/Avatar/` | ? Базовое знакомство |
| Manipulation | `Runtime/Scripts/Manipulation/` | ? Базовое знакомство |
| Locomotion | `Runtime/Scripts/Locomotion/` | ? Базовое знакомство |
| Weapons | `Runtime/Scripts/Mechanics/Weapons/` | ? Базовое знакомство |
| UI | `Runtime/Scripts/UI/` | ? Базовое знакомство |
| Animation | `Runtime/Scripts/Animation/` | ? Базовое знакомство |
| CameraUtils | `Runtime/Scripts/CameraUtils/` | ? Базовое знакомство |
| Networking | `Runtime/Scripts/Networking/` | ? Не изучено |
| Devices | `Runtime/Scripts/Devices/` | ? Не изучено |
