> **Для Copilot:** это точка входа в документацию **игры** VR Battlegrounds AI.
> Документация UltimateXR SDK находится отдельно: `Assets/ThirdParty/UltimateXR/Docs/_context/`

---

## Структура папки `Assets/Docs/`

| Файл | Содержание | Статус |
|---|---|---|
| `README.md` | Этот файл — точка входа | ? Готов |
| `gameplay.md` | Игровые механики, структура матча, режимы, менеджеры | ? Готов |

---

## Игровые ассеты проекта

Все ассеты игры хранятся в `Assets/` (исключая `Assets/ThirdParty/` — сторонние библиотеки).

| Путь | Содержание |
|---|---|
| `Assets/Scripts/` | Игровые скрипты (создать по мере разработки) |
| `Assets/Scenes/` | Сцены игры |
| `Assets/Prefabs/` | Префабы игровых объектов |
| `Assets/Prefabs/Player/` | Префабы игроков |
| `Assets/Docs/` | Документация игры (этот файл) |

---

## Ключевые префабы

| Префаб | Путь | Описание |
|---|---|---|
| Игрок | `Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab` | Сетевой аватар игрока. Вариант `CyborgAvatar_URP`. Содержит `UxrAvatar`, `UxrMirrorAvatar`, `NetworkIdentity`. **Требует `PlayerController`** (добавить вручную). |

> **Для Copilot (MCP):** все изменения компонентов игрока вносить в префаб `Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab`, а не на объекты сцены напрямую.

---

## Быстрая навигация

- **Карты: структура, добавление, быстрый запуск** ? [`gameplay.md#карты`](gameplay.md#карты)
- **Инструменты отладки (DebugOrchestrator, DebugBootstrapConfig)** ? [`gameplay.md#инструменты-отладки`](gameplay.md#инструменты-отладки)
- **Геймплей, режимы, матч, менеджеры** ? [`gameplay.md`](gameplay.md)
- **UltimateXR SDK (аватар, захват, оружие, UI)** ? [`Assets/ThirdParty/UltimateXR/Docs/_context/README.md`](../ThirdParty/UltimateXR/Docs/_context/README.md)
- **Mirror (сетевой фреймворк)** ? [`Assets/ThirdParty/UltimateXR/Docs/_context/architecture.md`](../ThirdParty/UltimateXR/Docs/_context/architecture.md) ? раздел Networking
