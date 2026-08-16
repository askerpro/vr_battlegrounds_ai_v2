# UltimateXR — ключевые факты

Фреймворк VRMADA, вендорится локально в `Assets/ThirdParty/UltimateXR/`.

- Все классы с префиксом `Uxr`, namespace `UltimateXR.*`
- Базовые классы: `UxrComponent<T>`, `UxrAvatarComponent<T>`, `UxrSingleton<T>`
- Главный менеджер: `UxrManager.Instance` — аватары, события, состояние
- Аватар управляется через `UxrStandardAvatarController`, не напрямую через `UxrAvatar`
- Захват: `UxrGrabbableObject` + `UxrGrabber`, обрабатывает `UxrGrabManager`
- Телепортация: `UxrTeleportLocomotion`
- Оружие: `UxrFirearmWeapon` (наследует `UxrGrabbableObject`)
- Урон и смерть: `UxrActor` — события `Death`, `DamageReceived`

## Где смотреть API

**Исходники точнее документации.** Runtime-скрипты SDK лежат в
`Assets/ThirdParty/UltimateXR/Runtime/Scripts/` — читай их напрямую через `Read`, а для
поиска класса используй `Grep` с `path="Assets/ThirdParty/UltimateXR"`.

Ограничивай поиск этой папкой явно: в ней 1383 `.cs`, и без скоупа она забивает выдачу
по всему остальному проекту.

## Документация SDK в `Docs/UltimateXR/`

| Файл | Когда нужен |
|---|---|
| `known-issues.md` | ⚠️ Первым при расследовании любого бага — неочевидные поведения SDK |
| `sdk-patches.md` | Все наши правки в исходниках SDK. Обязателен к обновлению при новой правке |
| `architecture.md` | Обзор модулей и связей |
| `interactions.md`, `locomotion.md`, `ui.md`, `network-sync.md`, `scripting.md` | Разборы по темам |
| `avatar-guide.md`, `avatar_and_manager.md` | Аватары |
| `guides/` | Официальные гайды VRMADA |

## Правки исходников SDK

Любое изменение внутри `Assets/ThirdParty/UltimateXR/` обязано попасть в `sdk-patches.md` —
иначе оно потеряется при обновлении SDK. Хук `.claude/hooks/sdk-patch-guard.ps1` напомнит
об этом автоматически.
