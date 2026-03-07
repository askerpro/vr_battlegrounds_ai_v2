# UltimateXR — Скриптинг: паттерны и примеры API

> Источник: `Assets/ultimate-xr/Docs/guides/scripting.md`, `scripting-how-do-i.md`  
> Скрипты: `Assets/ultimate-xr/Runtime/Scripts/`

---

## Базовая архитектура компонентов

Все компоненты UltimateXR наследуют от `UxrComponent` (? `MonoBehaviour`):

```
MonoBehaviour
    ??? UxrComponent              ? итерация, UniqueId, события включения
            ??? UxrComponent<T>   ? EnabledComponents, AllComponents для типа T
            ??? UxrAvatarComponent<T>  ? компоненты, привязанные к аватару
```

```csharp
// Итерация по всем компонентам типа T в сцене
foreach (UxrGrabbableObject obj in UxrGrabbableObject.EnabledComponents)
    Debug.Log(obj.name);

// Уникальный ID объекта (для сети / сохранений)
string id = component.UniqueId;

// Найти компонент по ID
if (UxrComponent.TryGetComponentById(id, out UxrComponent comp))
    Debug.Log(comp.name);
```

---

## Input (ввод с контроллеров)

```csharp
// Нажата ли кнопка (разовое событие)
bool pressed = UxrAvatar.LocalAvatarInput.GetButtonsPressDown(UxrHandSide.Left, UxrInputButtons.Button1);

// Удерживается ли кнопка
bool held = UxrAvatar.LocalAvatarInput.GetButtonsPress(UxrHandSide.Left, UxrInputButtons.Trigger);

// Две кнопки одновременно
bool both = UxrAvatar.LocalAvatarInput.GetButtonsPress(UxrHandSide.Left, UxrInputButtons.Button1 | UxrInputButtons.Button2);

// Аналоговый ввод (триггер 0.0–1.0)
float trigger = UxrAvatar.LocalAvatarInput.GetInput1D(UxrHandSide.Right, UxrInput1D.Trigger);

// Подписка на события кнопок
UxrControllerInput.GlobalButtonStateChanged += (sender, e) => {
    Debug.Log($"{e.HandSide} {e.Button} {e.ButtonEventType}");
};

// Направление контроллера
Vector3 forward = UxrAvatar.LocalAvatar.GetControllerInputForward(UxrHandSide.Right).forward;

// Левша/правша
UxrAvatar.LocalAvatarInput.Handedness = UxrHandedness.Left;

// Отключить ввод
UxrAvatar.LocalAvatarInput.SetIgnoreControllerInput(UxrHandSide.Left, true);
```

---

## Скорость рук

```csharp
// Мгновенная скорость (текущий - прошлый кадр)
Vector3 vel = UxrAvatar.LocalAvatar.GetGrabber(UxrHandSide.Right).Velocity;

// Сглаженная скорость (несколько кадров)
Vector3 smoothVel = UxrAvatar.LocalAvatar.GetGrabber(UxrHandSide.Right).SmoothVelocity;
```

---

## Haptics (тактильная отдача)

```csharp
// Импульс на конкретную руку
UxrAvatar.LocalAvatar.ControllerInput.SendHapticFeedback(UxrHandSide.Left, UxrHapticClipType.Click, 1.0f);

// Импульс на руки, которые держат объект
UxrAvatar.LocalAvatar.ControllerInput.SendGrabbableHapticFeedback(grabbableObject, UxrHapticClipType.RumbleFreqNormal);

// Вибрация на основе аудиоклипа
UxrAvatar.LocalAvatar.ControllerInput.SendHapticFeedback(UxrHandSide.Left, new UxrHapticClip(audioClip, UxrHapticClickType.Click));
```

---

## Навигация / компас (подсказки для пользователя)

```csharp
// Указать на объект (взгляд)
UxrCompass.Instance.SetTarget(myObject.transform, UxrCompassDisplayMode.Look);

// Указать место на полу (куда идти)
UxrCompass.Instance.SetTarget(floor.transform, UxrCompassDisplayMode.Location);

// Указать объект для захвата
UxrCompass.Instance.SetTarget(weapon.transform, UxrCompassDisplayMode.Grab);

// Выключить компас
UxrCompass.Instance.SetTarget(null);
```

---

## Анимации (твинеры)

```csharp
// Мигание цветом материала
UxrAnimatedMaterial.AnimateBlinkColor(gameObject, "_BaseColor", startColor, endColor);

// Перемещение объекта
UxrAnimatedTransform.Translate(gameObject, UxrTransformTranslationSpace.World, Vector3.forward * 3.0f);

// Анимация позиции (bounce)
UxrAnimatedTransform.PositionInterpolation(gameObject,
    UxrTransformTranslationSpace.Local,
    Vector3.zero, Vector3.up * 2.0f,
    new UxrInterpolationSettings(0.5f, 0.0f, UxrEasing.EaseOutQuart, UxrLoopMode.PingPong));

// Fade in/out Canvas
UxrCanvasAlphaTween.FadeIn(canvasGroup, fadeSeconds, delaySeconds);
UxrCanvasAlphaTween.Animate(canvasGroup, canvasGroup.alpha, 0.0f,
    new UxrInterpolationSettings(fadeSeconds)).SetFinishedActions(UxrTweenFinishedActions.DeactivateGameObject);

// Эффект печатной машинки
UxrTextContentTween.Animate(textComponent.gameObject, string.Empty, playerName,
    new UxrInterpolationSettings(durationSeconds, delaySeconds));
```

---

## Обновление после рендера аватаров

```csharp
private void OnEnable()  => UxrManager.AvatarsUpdated += OnAvatarsUpdated;
private void OnDisable() => UxrManager.AvatarsUpdated -= OnAvatarsUpdated;

private void OnAvatarsUpdated()
{
    // Вызывается каждый кадр после обновления всех аватаров
}
```

---

## Советы по навигации в коде UltimateXR

- `Ctrl+M, Ctrl+O` — свернуть всё до определений (быстрый обзор класса)
- `Ctrl+M, Ctrl+L` — развернуть всё
- Все классы, методы и свойства задокументированы XML-комментариями ? Intellisense/Rider подскажет
