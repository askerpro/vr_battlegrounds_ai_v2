# UltimateXR — Передвижение (Locomotion)

> Источник: `Assets/ultimate-xr/Docs/guides/locomotion.md`  
> Скрипты: `Assets/ultimate-xr/Runtime/Scripts/Locomotion/`

---

## Ключевые классы

| Класс | Описание |
|---|---|
| `UxrLocomotion` | Базовый класс любого передвижения |
| `UxrTeleportLocomotion` | Телепортация через дугу из контроллера |
| `UxrTeleportLocomotionBase` | Базовый класс телепортации |
| `UxrSmoothLocomotion` | Плавное передвижение (джойстик, как FPS) |

---

## Телепортация (по умолчанию в аватаре)

Компоненты `UxrTeleportLocomotion` уже есть в BigHandsIntegration ? ForwardLeft / ForwardRight.  
Один компонент на каждую руку.

### Ключевые параметры в инспекторе

| Параметр | Описание |
|---|---|
| `Controller Hand` | Какая рука управляет телепортом |
| `Translation Type` | Тип телепорта: Immediate / Smooth / Fade |
| `Translation Fade Color` | Цвет затемнения при Fade |
| `Translation Fade Seconds` | Длительность затемнения |
| `Rotation Type` | Тип поворота: Immediate / Smooth / Fade |
| `Rotation Step Degrees` | Градусы поворота за один шаг |
| `Allow Joystick Back Step` | Разрешить шаг назад джойстиком |
| `Back Step Distance` | Дистанция шага назад |
| `Parent To Destination` | Привязывать аватар к платформе назначения |

### Параметры дуги

| Параметр | Описание |
|---|---|
| `Arc Segments` | Количество сегментов дуги (2–1000) |
| `Arc Width` | Ширина дуги (0.01–0.4) |
| `Arc Material Valid` | Материал дуги при допустимой цели |
| `Arc Material Invalid` | Материал дуги при недопустимой цели |
| `Raycast Steps Quality` | Качество raycast дуги |

---

## Телепортация из кода

```csharp
// Мгновенная телепортация
UxrManager.Instance.MoveAvatarTo(UxrAvatar.LocalAvatar, Vector3.zero);

// С fadeout/fadein (быстрый вариант)
UxrManager.Instance.TeleportLocalAvatar(Vector3.zero, Quaternion.identity, UxrTranslationType.Fade);

// С fadeout/fadein + async/await
await UxrManager.Instance.TeleportLocalAvatarAsync(Vector3.zero, Quaternion.identity, UxrTranslationType.Fade);

// С привязкой к платформе (движущиеся объекты)
UxrManager.Instance.TeleportLocalAvatarRelative(destination, true, destination.position, Quaternion.identity, UxrTranslationType.Fade);

// С коллбэками (делать что-то пока экран затемнён)
UxrManager.Instance.TeleportLocalAvatar(
    Vector3.zero, Quaternion.identity, UxrTranslationType.Fade, 0.3f,
    () => { /* экран чёрный — меняй сцену */ },
    () => { /* экран снова виден — играй звук */ }
);
```

---

## Событие перемещения аватара

```csharp
private void OnEnable()  => UxrManager.AvatarMoved += OnAvatarMoved;
private void OnDisable() => UxrManager.AvatarMoved -= OnAvatarMoved;

private void OnAvatarMoved(object sender, UxrAvatarMoveEventArgs e)
{
    Debug.Log($"Аватар переместился: {e.OldPosition} ? {e.NewPosition}");
}
```

---

## Плавное передвижение (UxrSmoothLocomotion)

Альтернатива телепортации. Более иммерсивно, но может вызывать укачивание.

**Подключение:**
1. Отключить все `UxrTeleportLocomotion` на аватаре
2. Добавить `UxrSmoothLocomotion` на любой объект в иерархии аватара

**Управление (по умолчанию):**
- Левый джойстик ? движение
- Правый джойстик ? поворот

**Ключевые параметры:**

| Параметр | Описание |
|---|---|
| `Meters Per Second Normal` | Скорость обычного движения |
| `Meters Per Second Sprint` | Скорость бега |
| `Walk Direction` | Относительно чего считать вперёд: контроллер / аватар / камера |
| `Rotation Degrees Per Second` | Скорость поворота |
| `Gravity` | Гравитация (?9.81 = земная) |
| `Max Step Height` | Максимальная высота ступеньки |
| `Max Slope Degrees` | Максимальный угол подъёма |
| `Capsule Radius` | Радиус тела (в метрах) |

---

## Создание своей системы передвижения

```csharp
public class MyLocomotion : UxrLocomotion
{
    // Плавное (каждый кадр) или дискретное (по событию)?
    public override bool IsSmoothLocomotion => true;

    protected override void UpdateLocomotion()
    {
        // Используй методы UxrManager для перемещения:
        UxrManager.Instance.TranslateAvatar(Avatar, delta);
        UxrManager.Instance.RotateAvatar(Avatar, degrees);
    }
}
```

> Использовать методы `UxrManager` (не `transform.position`) — это важно для сетевой синхронизации и LOD.
