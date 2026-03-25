# UltimateXR — ключевые факты
- Все классы библиотеки имеют префикс `Uxr` (namespace: `UltimateXR.*`)
- Базовые классы: `UxrComponent<T>`, `UxrAvatarComponent<T>`, `UxrSingleton<T>`
- Главный менеджер: `UxrManager.Instance` — управляет аватарами, событиями, состоянием
- Аватар управляется через `UxrStandardAvatarController`, не напрямую через `UxrAvatar`
- Захват объектов: `UxrGrabbableObject` + `UxrGrabber` -- обрабатывает `UxrGrabManager`
- Телепортация: `UxrTeleportLocomotion`
- Оружие: `UxrFirearmWeapon` (наследует `UxrGrabbableObject`)
- Скрипты рантайма локально: `Assets/ThirdParty/UltimateXR/Runtime/Scripts/`
- Документация UltimateXR: `Docs/UltimateXR/guides/`

> **Для ИИ-помощника:** исходники UltimateXR SDK доступны через полные пути.
> `UltimateXR.csproj` входит в workspace. При вопросах об API лучше читать исходники напрямую.

## Связанная документация UltimateXR в `Docs/UltimateXR/`
- `README.md` — точка входа
- `architecture.md` — все модули UltimateXR
- `known-issues.md` — **⚠️ ЧИТАТЬ ПЕРВЫМ при расследовании бага:** неочевидные поведения SDK
- `sdk-patches.md` — все правки внесённые в исходники UltimateXR
