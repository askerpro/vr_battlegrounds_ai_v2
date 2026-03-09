# Changelog (Журнал изменений)

Все важные изменения проекта будут фиксироваться в этом файле.

## [2026-03-09]
### Добавлено
- **Готовность к раунду**: Новое состояние `RoundState.WaitingForPlayers` в `RoundManager`. Раунд не начнется, пока все живые игроки не зайдут в свои `TeamSpawnZone`.
- **Архитектура GameMode**: Метод `CanStartGameplay()` для настройки условий старта (например, наличие игроков в обеих командах для `EliminationMode`).
- **EliminationModeEditor**: Кастомный инспектор для режима "Ликвидация" с отображением таймеров и счета в реальном времени.
- **Стабильность триггеров**: Автоматическое добавление `Rigidbody` в `TeamSpawnZone` для надежной детекции VR-аватаров.

### Изменено
- **Рефакторинг менеджеров**: `MatchManager` переименован в `GameplayManager` для подготовки к PvE режимам.
- **HUD**: Обновлен `HUDWidget_GameNotification` для отображения статуса ожидания игроков.
- **Ограничение оружия (Refactored)**: Введен глобальный флаг `WeaponSystemEnabled` и хук `GlobalUsageCheck` в `UxrWeaponManager`. Теперь `GameplayManager` централизованно управляет всей системой боя, отключая расчёты пуль и использование оружия вне активных фаз раунда.
- **События смерти**: Добавлено событие `Died` в `UxrActor` и `PlayerDied` в `PlayerController` для уведомления систем игры о гибели игрока в контексте UltimateXR.
- **Документация**: Обновлены `README.md`, `gameplay.md`, `game-manager.md`, обновлен `walkthrough.md`.

---

## [2026-03-09] - Предшествующие изменения
- `12de155` feat: implement pre-round waiting state and game start readiness logic
- `3a406b4` Refactor: Implement Pure Semantic Events for HUD notifications

## [2026-03-08]
- `c323b19` chore: update scenes, prefabs and settings after manager rename refactoring
- `ec2680b` refactor: rename GameManager -> SessionManager, MatchManager -> GameplayManager
- `d2a82af` feat: add Player HUD system with widgets and prefab generation tools
- `6c34595` refactor: add TeamRuntimeData and support N teams in EliminationMode
- `2e6d29f` fix: use MaterialPropertyBlock in TeamSpawnZone to avoid prefab material errors
- `0a30497` chore: track missing folder meta files
- `a731cc3` feat: add PlayModeStartFromOffline editor tool
- `0e1b8dd` docs: add summary for UxrAvatar and UxrManager modules
- `d6d180e` docs: update agent rules to require user validation before commit
- `25f375b` feat: teleport player to team spawn zone on connect
- `20ba947` feat: setup spawn zones on map and create SpawnZone editor tool
- `66c9d26` feat: TeamSpawnZone inherits color from TeamData
- `4ae57f8` docs: document TeamSpawnZone and generalize AI instructions
- `cb7dc09` feat: add TeamSpawnZone component for team area tracking

## [2026-03-07]
- `5633c68` refactor: move EliminationMode scripts to dedicated folder
- `84f4b6e` docs: add unified .agentrules
- `4f1425f` GameModes + Orchestrator
- `7acb4d2` chore: обновление copilot-instructions и UltimateXR context
- `ea665c3` docs: реструктуризация документации игры
- `729ec50` feat: реализация команд и менеджеров матча
- `59bd59c` chore(editorconfig): add dotnet style rules
- `8b26c3c` feat(maps): add TestMap2 scene and data
- `250334b` refactor(network,debug): integrate GameLog logging
- `afd2791` feat(managers): add MapManager
- `8d690c1` feat(core): add logging system
- `b8ddc63` refactor(copilot): consolidate encoding rules
- `828a2c5` fix(docs): replace remaining garbled '?' symbols
- `b287f07` docs(copilot): add VS UTF-8 setup instructions
