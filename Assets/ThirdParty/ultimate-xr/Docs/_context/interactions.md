# UltimateXR — Взаимодействие с объектами (Grabbing / Manipulation)

> Источник: `Assets/ultimate-xr/Docs/guides/manipulation.md`  
> Скрипты: `Assets/ultimate-xr/Runtime/Scripts/Manipulation/`

---

## Ключевые классы

| Класс | Назначение |
|---|---|
| `UxrGrabbableObject` | Компонент на объекте — делает его захватываемым |
| `UxrGrabber` | Компонент на руке аватара (внутри BigHandsIntegration) |
| `UxrGrabManager` | Синглтон — управляет всеми захватами автоматически |
| `UxrGrabbableObjectAnchor` | Точка размещения объекта (snap point на уровне) |
| `UxrGrabPointShape` | Расширенные формы точек захвата |

---

## Быстрый старт

1. Добавить компонент `UxrGrabbableObject` на любой GameObject ? объект уже можно хватать
2. `UxrGrabManager` создаётся автоматически, никаких дополнительных настроек сцены не нужно
3. Аватар должен иметь компоненты `UxrGrabber` на руках (в BigHandsIntegration — уже есть)

---

## Проверка состояния захвата

```csharp
// Захвачен ли объект?
bool isGrabbed = UxrGrabManager.Instance.IsBeingGrabbed(grabbableObject);
bool isGrabbed2 = grabbableObject.IsBeingGrabbed; // то же самое

// Какой объект держит левая рука?
if (UxrGrabManager.Instance.GetObjectBeingGrabbed(avatar, UxrHandSide.Left, out UxrGrabbableObject obj))
    Debug.Log(obj.name);

// Какая рука держит объект?
if (UxrGrabManager.Instance.GetGrabbingHand(grabbableObject, 0, out UxrGrabber grabber))
    Debug.Log($"{grabber.Avatar.name} держит {grabber.Side} рукой");
```

---

## Управление захватом из кода

```csharp
// Зафиксировать объект (нельзя двигать)
grabbableObject.IsLockedInPlace = true;

// Принудительно отпустить
UxrGrabManager.Instance.ReleaseGrabs(grabbableObject, true);
grabbableObject.ReleaseGrabs(true); // то же самое

// Разместить объект на anchor
UxrGrabManager.Instance.PlaceObject(grabbableObject, anchor, UxrPlacementType.Immediate, true);
UxrGrabManager.Instance.PlaceObject(grabbableObject, anchor, UxrPlacementType.Smooth, true);

// Включить/выключить точку захвата
grabbableObject.SetGrabPointEnabled(0, false);
grabbableObject.EnableAllGrabPoints();
```

---

## События захвата

```csharp
private void OnEnable()
{
    UxrGrabManager.Instance.ObjectGrabbing += OnObjectGrabbing;   // до захвата
    UxrGrabManager.Instance.ObjectGrabbed  += OnObjectGrabbed;    // после захвата
    UxrGrabManager.Instance.ObjectReleasing += OnObjectReleasing; // до отпускания
    UxrGrabManager.Instance.ObjectReleased  += OnObjectReleased;  // после отпускания
    UxrGrabManager.Instance.ObjectPlacing   += OnObjectPlacing;   // до размещения на anchor
    UxrGrabManager.Instance.ObjectPlaced    += OnObjectPlaced;    // после размещения на anchor
}

private void OnDisable()
{
    UxrGrabManager.Instance.ObjectGrabbing  -= OnObjectGrabbing;
    UxrGrabManager.Instance.ObjectGrabbed   -= OnObjectGrabbed;
    UxrGrabManager.Instance.ObjectReleasing -= OnObjectReleasing;
    UxrGrabManager.Instance.ObjectReleased  -= OnObjectReleased;
    UxrGrabManager.Instance.ObjectPlacing   -= OnObjectPlacing;
    UxrGrabManager.Instance.ObjectPlaced    -= OnObjectPlaced;
}
```

---

## Скорость захваченного объекта

```csharp
Vector3 velocity        = UxrGrabManager.Instance.GetGrabbedObjectVelocity(grabbableObject);
Vector3 angularVelocity = UxrGrabManager.Instance.GetGrabbedObjectAngularVelocity(grabbableObject);
```

---

## Настройка позы руки при захвате оружия

```csharp
// Изменить blend значение позы (например, нажатие триггера на пистолете)
if (UxrGrabManager.Instance.GetGrabbingHand(grabbableGun, 0, out UxrGrabber grabber))
{
    float triggerPress = UxrAvatar.LocalAvatarInput.GetInput1D(grabber.Side, UxrInput1D.Trigger);
    grabbableGun.GetGrabPoint(0).GetGripPoseInfo(grabber.Avatar).PoseBlendValue = triggerPress;
}
```

---

## Скрытие рук при захвате

В инспекторе `UxrGrabbableObject` включи **Hide Hand Renderer** — руки скрываются при захвате.  
Это "Tomato Presence" — объект заменяет руку визуально, ощущение присутствия сохраняется.

---

## Для шутера: оружие как UxrGrabbableObject

Класс `UxrFirearmWeapon` наследует от `UxrWeapon`, который наследует от `UxrGrabbableObject`.  
Оружие — это захватываемый объект с дополнительной логикой стрельбы.  
Требует компонент `UxrProjectileSource` на том же GameObject.

```csharp
// Подписка на событие выстрела
firearmWeapon.ProjectileShot += (triggerIndex) => {
    Debug.Log($"Выстрел из триггера {triggerIndex}");
};
```

> Детали по оружию — в `Assets/ultimate-xr/Docs/_context/architecture.md` ? раздел Weapons
