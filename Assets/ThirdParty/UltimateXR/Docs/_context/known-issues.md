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

## Шаблон для добавления нового Issue

```markdown
## Issue N: [Название]

**Компоненты:** `ComponentA`, `ComponentB`  
**Статус:** ✅ Исправлено / ⚠️ Открыто / ℹ️ Поведение SDK
**Дата обнаружения:** YYYY-MM-DD

### Симптом
[Что наблюдается]

### Причина
[Почему это происходит]

### Исправление / Обходное решение
[Как было исправлено или как обойти]
```
