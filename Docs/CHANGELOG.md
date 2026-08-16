# Changelog (Журнал изменений)

Все важные изменения проекта будут фиксироваться в этом файле.

## [2026-08-16]
### Исправлено
- **Сборка под Android (Quest)**: Editor-скрипты лежали в семи папках `Assets/Scripts/**/Editor/`, накрытых `VrBattlegrounds.asmdef` с пустым `includePlatforms`. Своей editor-сборки они не образовывали (в `Library/ScriptAssemblies/` был только `VrBattlegrounds.dll`), поэтому `using UnityEditor` попадал в рантайм-сборку для всех платформ. Пять файлов при этом были без `#if UNITY_EDITOR`. Все скрипты перенесены в `Assets/Editor/VR_Battlegrounds/` (`Arsenal/`, `Gameplay/`), опустевшие папки удалены. Это то самое нарушение, от которого предостерегает `.agents/rules/editor_scripts.md`.
- **Хук `post-commit` (Plastic SCM)**: не работал с 2026-03-28. Пофайловый вариант через `git diff-tree ... | cm ci -a -c "$MSG" -` был построен на допущении, что `cm ci` читает пути из stdin — такого режима у Plastic CLI нет, и `-` уходил ему как ещё один путь. Ошибки `cm add` / `cm rm` при этом глушились в `/dev/null`, поэтому поломка была не видна. Хук переписан на один чекин всего воркспейса (`cm ci . -a --applychanged --private`), ошибки теперь выводятся. Добавлен фолбэк на путь к `cm.exe`, если его нет в `PATH`.
- **`.gitattributes`**: правило `* text=auto eol=crlf` при свежем clone превращало `.githooks/post-commit` в CRLF, ломая shebang. Добавлено исключение `.githooks/** text eol=lf` и `*.sh text eol=lf`.
- **Unity MCP не видел редактор**: все инструменты падали с `No Unity Editor instances found` при живом бридже. Рассинхрон транспортов: Unity 10.1.2 подключается к HTTP-хабу, который поднимает сам (`ws://127.0.0.1:8080/hub/plugin`, pid в `Library/MCPForUnity/RunState/`), а `.mcp.json` со `stdio` порождал второй серверный процесс, искавший редактор сканом старых TCP-портов. `.mcp.json` переведён на `{"type": "http", "url": "http://127.0.0.1:8080/mcp"}` — сессия цепляется к уже запущенному хабу, попутно включились project-scoped tools (`execute_custom_tool`). Разбор и диагностика — в `.agents/rules/unity_mcp.md`.

### Добавлено
- **Инфраструктура для ИИ-агентов**: `CLAUDE.md` как единый источник правды (автозагрузка), `.claude/settings.json` с allowlist прав и `GIT_PAGER=cat`, слэш-команды `/commit`, `/debug`, `/feature`, `/unity-check`, `/docs-sync`, хук `.claude/hooks/sdk-patch-guard.ps1` (требует фиксировать правки исходников UltimateXR в `sdk-patches.md`).

### Изменено
- **Правила агентов схлопнуты**: четыре параллельных набора (`.agentrules`, `.cursorrules` на 26 КБ, `.clinerules`, `.github/copilot-instructions.md`) разошлись между собой — в них были устаревшие версии пакетов (URP 17.0.3 против 17.4.0, xr.oculus 4.4.0 против 4.5.4, inputsystem 1.11.2 против 1.19.0) и ссылка на несуществующий путь `_agent/workflows/`. Все четыре превращены в указатели на `CLAUDE.md`. Из `.agents/rules/` удалены шесть файлов, поглощённых `CLAUDE.md` и слэш-командами.
- **Обязательное чтение отменено**: «Шаг 0» требовал 840 строк документации перед любой задачей. Заменён таблицей маршрутизации «задача → что читать» в `CLAUDE.md`.
- **`.mcp.json`**: был в формате VS Code (ключ `servers`), из-за чего Claude Code его не читал. Переведён на `mcpServers` со stdio-транспортом `mcpforunityserver`.
- **`Docs/README.md`**: в индекс добавлены пропущенные `session-architecture.md`, `level-design.md`, `Roadmap.md`, `Arsenal/`, `LegsAnimator_UI_Reference_RU.md`. Файл `Docs/AI_Navigation.md` удалён — правила поиска переехали в `CLAUDE.md`.
- **`.gitignore` — `*.slnx` и `.vscode/`**: Unity 6 генерирует solution в новом XML-формате `.slnx` рядом со старым `.sln`, а тот игнорировался с самого начала. Файл перечисляет 118 путей к `*.csproj`, которые тоже в игноре, порядок проектов в нём не отсортирован — в репозитории он давал бы шумные диффы и ссылки в никуда. `*.slnx` добавлен рядом с `*.sln`. Для `.vscode/` заведён белый список (`settings.json`, `tasks.json`, `launch.json`, `extensions.json`): конфиг «Attach to Unity» и YAML-ассоциации для `.unity`/`.prefab`/`.meta` полезны и переносимы, а локальный мусор расширений в репозиторий не попадёт.

---

## [2026-03-28]
### Добавлено
- **Документация Архитектуры Сессий**: Добавлен файл [`Docs/session-architecture.md`](session-architecture.md), подробно описывающий паттерн разделения логики `PlayerSession` и визуальных кукол аватаров.
- **Инструментарий `Avatar Maintenance` (`Assets/Editor/`)**: Создан набор редакторских утилит (`CheckSkeletons`, `CreateBaseAvatars`, `FixAvatarRenderers`, `FixHandTrackingCache`) для массовой настройки, экстракции и поправки скелетов базовых `UltimateXR` аватаров прямо в инспекторе. 
- **Модель сессий и аватаров (`Avatar Subsystem`)**: Внедрены реестры скинов (`AvatarRegistry`, `SkinRegistry`), профили девайсов клиентов (`ClientDeviceType`), логика стратегий спавна аватаров (`AvatarSpawnStrategy`, `TeamAvatarStrategy`) и обработчик потери сессий (`SessionRecoveryManager`). Сетевая логика теперь отделяет "сессию" (счет, команда) от "тела" игрока.
- **Поддержка Spectator Connect**: Реализованы раздельные сообщения при подключении к серверу для игроков (`GamePlayerConnectMessage`) и наблюдателей (`SpectatorConnectMessage`) вместо единого `PlayerJoinMessage`.

### Изменено
- **UI и HUD Интеграция**: Виджеты счета, таймеры команд, меню и `PlayerHUDManager` адаптированы к новой системе сессий. Так как `PlayerSession` теперь является локальным игроком (`isLocalPlayer=true`), HUD-составляющие куклы завязаны на событие `OnStartAuthority` от Mirror.
- **Игровые Режимы (Elimination / Respawn)**: Менеджеры раундов и игровые циклы (`GameplayManager`, `GameMode`, `RoundManager`) переписаны для взаимодействия со стойкими `PlayerSession`. Очки и статистика сохраняются при уничтожении куклы скина.
- **Plastic SCM Хуки (`.githooks/post-commit`)**: Хук для дублирования коммитов в `cm ci` переведен с монолитного `cm ci -a` на пофайловое сопоставление дерева через `git diff-tree`. Это позволяет изолированно коммитить и пушить в Plastic только выбранные stage файлы (Interactive commits).

---

## [2026-03-23]
### Добавлено
- **Модульный Level Design**: Внедрена система блокаутов `LD_Alphabet`, содержащая 10 стандартных префабов (укрытия, змейки, доритос, цилиндры, инкапсуляторы столбов).
- **Материал для Greyboxing**: Создан PBR-совместимый материал `PrototypeGrid_URP` с UV-разметкой для четкого понимания масштабов при тестировании карт.
- **Утилиты генерации**: Написаны Unity Editor скрипты `CreateDoritoMesh.cs` (процедурная генерация пирамид) и `BuildVegasMap.cs` (для автоматической сборки асимметричных турнирных схем NXL в Unity).

### Изменено
- **Иерархия Сцен (Map Hierarchy)**: Карты реструктурированы для четкого разделения физических характеристик зала и игровой геометрии. Внедрена связка шаблона `Arena_Core` (где собраны пол, спавн-зоны и колонны) и префабов `Map_Layout`.
- **Документация**: В `gameplay.md` прописаны обязательные правила инкапсуляции бетонных колонн 15x18 арены и концепт Art Pass для естественной интеграции киберспортивных макетов в реалистичное окружение.

---

## [2026-03-22]
### Добавлено
- **Сохранение позиции между сценами**: Реализован перенос абсолютных координат аватара при загрузке новых сцен. `PhysicalSpaceSyncManager` сохраняет координаты по событию `UxrAvatar.GlobalAvatarMoved` и передает их на сервер через расширенное сообщение `PlayerJoinMessage` (бывшее `RoleJoinMessage`), чтобы сервер сразу спавнил аватара в нужной точке.
- **Калибровка роста в VR**: Создана система подгонки виртуального роста `PhysicalSpaceSyncManager` и соответствующие экраны настраиваемого UI (MenuPhysicalSpaceSync, HUD). Наложен патч на ядро трекеров `UxrControllerTracking`, позволяющий глобально сдвигать руки по высоте (`GlobalHeightOffset`), чтобы телепортация не сбрасывала выставленное смещение.
- **MVC Архитектура Меню**: Внедрен переработанный UI-стек. Созданы `MenuController` (глобальный контроллер), `LocalMenuManager` (управление инстансами по контексту сцены) и `MenuScreen` (базовый класс для экранов).
- **Иерархический Реестр Префабов Меню**: `MenuPrefabRegistry` теперь использует древовидную структуру (Role -> Scene Context -> Game Mode), избавляя от жесткого кодирования имен сцен.
- **Компонент Session Setup**: Добавлен `MenuSessionSetup` с поддержкой красивой сетки (Grid) и визуальных вкладок (Tabs) для выбора игровых режимов и карт.
- **Инструментарий Editor**: Добавлены скрипты для генерации/запекания красивых меню-префабов.

### Изменено
- **Чистка старого UI**: Удалены устаревшие `AdminMenuController` и `PlayerMenuController`.
- **Логика вращения планшета**: Физика `TabletMenuBase` стабилизирована: удалён проблемный скрипт `SpringOnRelease`, добавлен Kinematic Rigidbody, а иерархия префаба оптимизирована для ровного спавна перед игроком.

---

## [2026-03-21]
### Добавлено
- **Архитектура Ролей (Role Architecture)**: Логика идентификации платформы и прав в сети выделена в модуль ядра `AppRoleManager`. Введены роли устройства (`DeviceRole` — VR, PC, Server) и сетевые роли (`NetworkRole` — Host, Client, DedicatedServer).
- **Поддержка режимов по картам**: В `MapData` добавлено поле `supportedModes`, позволяющее привязывать конкретные игровые режимы к картам.
- **Интеграция UI библиотек**: Импортированы `SlimUI` и `TextMeshPro`. Добавлена новая категория `LogLevelUI` в настройки логирования `GameSettings`.
- **Magazine Pocket**: Внедрена механика карманов для магазинов. Добавлен `ColliderVisualizer` для удобной настройки коллайдеров. Оформлен `Docs/Roadmap.md`.

### Изменено
- **Глобальные Менеджеры**: Введен `PersistentRoot` для управления глобальными объектами. `SessionManager` (бывший `GameManager`) отделён от сетевого транспорта и переведён в формат спавнящегося префаба для упрощения загрузки оффлайн сцен и тестирования.

---

## [2026-03-19]
### Изменено
- **Документация и ИИ**: Внедрена система M.A.P. (быстрая навигация для ИИ-агентов). Создан файл `AI_Navigation.md`. Обновлены `README.md` и `.agentrules` для устранения устаревших PowerShell скриптов, предотвращения раздувания базы знаний и уточнения политик. Устаревшие правила чтения перенесены в `.github/copilot-instructions.md`.

---

## [2026-03-15]
### Добавлено
- **Авто-рюкзак (Back Slot)**: Внедрена MVP-система невидимого инвентарного слота на спине (`Anchor_Back`) для ношения крупного оружия.
- **Grab Proxy (Перехват хвата)**: Нативная поддержка редиректа в UltimateXR. При попытке схватить невидимую зону за плечом, игрок моментально достает оружие из слота на спине.
- **Нативный Swap (Быстрая замена)**: При попытке вставить предмет в занятый слот инвентаря, старый предмет автоматически выталкивается, освобождая место новому.
- Утилита `RefineBackPocket` для настройки позиционирования слотов через Inspector.

### Исправлено
- **UxrReturnGrabbableObject**: Безопасная отвязка компонента возврата (`ClearLastAnchor`) при срабатывании механизма Swap.
- **Грип-позы (Grip Poses)**: Обновлены и исправлены позы хвата для аватара CyborgAvatar и добавлены/перемещены Demo-хваты.
- Отключен спам (verbose logs) в `UxrGlobalSettings` и `GameSettings`.

---

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
