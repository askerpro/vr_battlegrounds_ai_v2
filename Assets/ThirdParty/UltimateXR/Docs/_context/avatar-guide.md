# UltimateXR — Аватар

> Источник: `Assets/ThirdParty/UltimateXR/Docs/guides/avatar.md`  
> Скрипты: `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Avatar/`

---

## Ключевые классы

| Класс | Назначение |
|---|---|
| `UxrAvatar` | Главный компонент аватара на корневом GameObject |
| `UxrAvatarController` | Базовый класс логики управления |
| `UxrStandardAvatarController` | Стандартный контроллер: жесты рук, взаимодействие |
| `UxrAvatarRig` | Описание скелета (кости, руки, голова) |
| `UxrAvatarHand` | Данные руки (кости пальцев по сторонам) |

---

## Доступ к аватару из кода

```csharp
// Получить локальный аватар (под управлением пользователя)
UxrAvatar myAvatar = UxrAvatar.LocalAvatar;

// Ввод контроллеров
UxrAvatar.LocalAvatarInput.GetButtonsPressDown(UxrHandSide.Left, UxrInputButtons.Button1);

// Позиция камеры (глаза пользователя)
Vector3 cameraPos = UxrAvatar.LocalAvatar.CameraPosition;

// Направление взгляда
Vector3 viewDir = UxrAvatar.LocalAvatar.CameraForward;
```

---

## Готовые префабы аватаров

Находятся в `Assets/ThirdParty/UltimateXR/Runtime/Prefabs/Avatars/`:

| Префаб | Описание |
|---|---|
| `BigHandsAvatar_URP` | Большие руки, для URP (наш проект) |
| `SmallHandsAvatar_URP` | Маленькие руки, для URP |
| `BigHandsAvatar_BRP` | Большие руки, для Built-in RP |
| `SmallHandsAvatar_BRP` | Маленькие руки, для Built-in RP |

> ?? Всегда создавай **Prefab Variant** своего аватара через кнопку Fix в инспекторе!  
> Это защищает изменения при обновлении библиотеки.

---

## Структура объекта аватара

```
[AvatarRoot]  ? UxrAvatar + UxrStandardAvatarController
    ??? BigHandsIntegration  ? поддержка всех контроллеров
            ??? LeftHand / RightHand   ? виртуальные руки (IK)
            ?       ??? UxrGrabber     ? компонент захвата на каждой руке
            ??? ForwardLeft / ForwardRight
            ?       ??? UxrTeleportLocomotion  ? телепорт (по умолчанию включён)
            ?       ??? UxrLaserPointer        ? лазер для UI (по умолчанию выключен)
            ??? [FingerTips на пальцах]        ? прямое касание UI
```

> ?? Не перемещай руки внутри аватара — они должны быть выровнены с BigHandsIntegration!

---

## Режимы аватара

```csharp
// Переключить в режим "управляется извне" (для сети/replay)
avatar.AvatarMode = UxrAvatarMode.UpdateExternally;
```

| Режим | Описание |
|---|---|
| `UxrAvatarMode.Local` | Управляется пользователем (по умолчанию) |
| `UxrAvatarMode.UpdateExternally` | Управляется снаружи (сеть, replay) |

---

## Режимы рендера

```csharp
// Показать аватар (руки/тело)
UxrAvatar.LocalAvatar.RenderMode = UxrAvatarRenderModes.Avatar;

// Показать контроллеры + руки поверх (IK)
UxrAvatar.LocalAvatar.RenderMode = UxrAvatarRenderModes.AllControllers;
UxrAvatar.LocalAvatar.ShowControllerHands = true;
```

---

## Доступ к костям скелета

```csharp
// Кость руки
UxrAvatarHand leftHand = UxrAvatar.LocalAvatar.GetHand(UxrHandSide.Left);
Vector3 thumbTip = leftHand.Thumb.Distal.position;

// Голова
Vector3 headPos = UxrAvatar.LocalAvatar.AvatarRig.Head.Head.position;

// Все кости одной руки
foreach (Transform bone in UxrAvatar.LocalAvatar.GetHand(UxrHandSide.Right))
    Debug.Log(bone.name);
```

---

## Fadeout экрана

```csharp
// Затемнить экран
UxrAvatar.LocalAvatar.CameraFade.EnableFadeColor(Color.black, 1.0f);

// Убрать затемнение
UxrAvatar.LocalAvatar.CameraFade.DisableFadeColor();
```

---

## Настройка позы руки по умолчанию

```csharp
UxrStandardAvatarController ctrl = UxrAvatar.LocalAvatar.AvatarController as UxrStandardAvatarController;

// Переопределить позу
ctrl.LeftHandDefaultPoseNameOverride = "myPoseName";

// Сбросить
ctrl.LeftHandDefaultPoseNameOverride = null;
```
