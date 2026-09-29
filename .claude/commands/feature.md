---
description: Добавить новую фичу по архитектуре проекта
---

Реализуй фичу: $ARGUMENTS

**Не пиши код, пока не понял, куда он встаёт.**

## 1. Проверь, что этого ещё нет

- `Docs/README.md` — таблицы классов. Не дублируй существующее.
- `Docs/UltimateXR/architecture.md` — нет ли готового компонента в SDK. UltimateXR закрывает
  захват, локомоцию, оружие, урон, аватары — проверь до того, как писать своё.
- `Docs/gameplay.md` — как механика вписывается в матч.

## 2. Определи слой

Сеть / Менеджеры / Игрок / UI / GameMode / UltimateXR. Нужна ли сетевая синхронизация Mirror.

## 3. Используй существующие паттерны

| Тип фичи | Паттерн |
|---|---|
| Новый игровой режим | Наследник `GameMode` + `GameModeData` + префаб + запись в `GameModeRegistry` |
| Новый менеджер | Синглтон, `DontDestroyOnLoad`, подписка на события |
| VR-взаимодействие | `UxrGrabbableObject` + `UxrGrabber` |
| Оружие | Наследник `UxrFirearmWeapon` / `UxrGrenadeWeapon` |
| Сеть | `NetworkBehaviour`, `[SyncVar]`, `[Command]`, `[ClientRpc]` |
| Здоровье и смерть | Подписка на `UxrActor.Death` / `UxrActor.DamageReceived` |
| Смена сцены | Только `MapLoader.Instance.LoadMap()` |
| Логирование | Только канал категории: `GameLog.Match.Info(...)`, `GameLog.UI.Warning(...)` |
| Editor-утилита | Только `Assets/Editor/VR_Battlegrounds/<категория>/` |

Жёсткие правила и почему они жёсткие — в `CLAUDE.md`.

## 4. Соблюдай Single Responsibility

Не дописывай чужеродную логику в существующий менеджер, чтобы «было быстрее». Новая
ответственность — новый класс.

## 5. Обнови документацию

| Что добавил | Куда |
|---|---|
| Класс, скрипт | `Docs/README.md` |
| Игровая механика | `Docs/gameplay.md` |
| Менеджер, поток | `Docs/game-manager.md` |
| Зависимость от UltimateXR | `Docs/UltimateXR/architecture.md` |

## 6. Отдай на проверку

Проверь компиляцию (`/unity-check`), затем передай пользователю для проверки в Unity.
Перечисли, что именно смотреть. Коммит — потом, через `/commit`.
