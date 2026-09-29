# VR Battlegrounds AI

VR-шутер для Quest 2/3 на Unity 6 (URP). Над проектом работают только ИИ-агенты. Этот файл —
единственный источник правил агента (`.agentrules`, `.cursorrules` и т.п. — только указатели сюда).

UltimateXR и Mirror вендорятся в `Assets/ThirdParty/`, код игры — сборка `Assets/Scripts/VrBattlegrounds.asmdef`.
Версии пакетов — `Packages/manifest.json`. Комментарии и документация — на русском.

## Жёсткие правила

Нарушение ломает билд или игру.

- **Логи** — только `GameLog.<Канал>.<Уровень>(...)`, например `GameLog.Match.Info("...")`,
  `GameLog.Player.Verbose("...", this)`. Каналы: `Network`, `Player`, `Match`, `Debug`, `WeaponSystem`,
  `UI`, `PhysicalSpace`, `Arsenal`, `Perf`; вне категории — `GameLog.Error(...)`. `Debug.Log` запрещён.
- **Смена карты на живом сервере** — только `MapLoader.Instance.LoadMap(sceneName)`. Прямые
  `SceneManager.LoadScene` / `ServerChangeScene` рассинхронизируют клиентов. Исключение —
  `offlineScene`/`onlineScene` у `GameNetworkManager`: их ведёт сам Mirror, не трогать (NET-21).
- **Editor-скрипты** — только в `Assets/Editor/VR_Battlegrounds/<категория>/`. Папка `Editor` внутри
  `Assets/Scripts/` ломает Android-билд. Меню — `VR Battlegrounds` с пробелом (`Tools/VR Battlegrounds/...`).
- **Коммит** — никогда сразу после кода, сначала проверка пользователем в Unity. Исключение — только
  документация. Подробности — `/commit`.
- **Теги** — только из `GameTags`, руками не ставятся: правило в `GameTagRules`, расстановка —
  `Tools/VR Battlegrounds/Gameplay/Apply Game Tags`, проверка — `GameTagsTests`. Не удалять теги из
  TagManager в открытом редакторе — индексы сдвигаются у всех загруженных объектов.
- **Префабы оружия и магазинов** (`Assets/Prefabs/Weapons/`): не-trigger коллайдер на `Rigidbody`,
  `Collision Detection` не `Discrete`, `OutOfWorldGuard`; у оружия с якорем магазина —
  `AnchoredItemCollisionIgnore`; у каждой вложенной детали `UxrGrabbableObject` — `GrabOnlyWhenParentHeld`.
  Размер — масштабом корня, эталон в `WeaponScaleTests`. Проверка: `WeaponDropPhysicsTests`,
  `OutOfWorldGuardTests`, `WeaponPartGrabTests`. Новое оружие — `/add-weapon`.
- **Аватары и их тесты меняются вместе.** Любая правка аватара (компонент, карман, якорь, поза, хват,
  слой, запись в `AvatarRegistry`) в той же задаче отражается в `AvatarLoadoutTests` и
  `PrefabCompositionTests`; убранное требование — с комментарием почему. Новый аватар — `/setup-avatar`.
- **Сетевые действия — один автор.** UltimateXR пересчитывает действия чужого игрока на каждой машине.
  Код из Update, таймера, физики, RPC или хука, вызывающий синхронизируемый метод (`Shoot`, `Reload`,
  `ReleaseObject`, `IsGrabbable`…), обязан проверить `StateEventAuthority.IsAuthorOfItem` /
  `IsWorldAuthority`. Иначе — двойной выстрел и урон (Issue 23).
- **Геометрия карты** — после правки: `Tools/VR Battlegrounds/Gameplay/Bake Occlusion (all maps)`.
  Подвижное на карте — только с `Animator`/`Rigidbody`/`NetworkIdentity`, иначе станет окклюдером.
- **Правки SDK**: `Assets/ThirdParty/UltimateXR/` → запись в `Docs/UltimateXR/sdk-patches.md`;
  `Assets/ThirdParty/Mirror/` → пометка `VR Battlegrounds patch` в коде и запись в `Docs/Mirror/mirror-patches.md`.
- **Меню планшета** — экран только вариант `Screen_Base` в `Assets/Prefabs/UI/Menu/Screens/`, содержимое только
  через `MenuKit`, цвета и размеры только из `MenuTheme`, «Назад» и разделы — только каркас. Проверка:
  `MenuDesignRulesTests`, `MenuContainmentTests`. Новый экран — `/add-menu-screen`, дизайн — `Docs/ui-design-system.md`.
- **Single Responsibility** — чужеродную логику в синглтоны не дописывать, выносить в отдельный класс.

## Поиск

Своего кода ~10× меньше, чем в `Assets/ThirdParty/`. Grep — всегда с `path="Assets/Scripts"` или
`"Assets/Editor"`; в `ThirdParty/`, `Packages/` — только если задача про SDK. Для API UltimateXR
исходники `Assets/ThirdParty/UltimateXR/Runtime/Scripts/` точнее доков.
`.unity` и `.prefab` целиком не читать — Grep по имени класса или GUID.

## Что читать под задачу

Не читать документацию впрок. Индекс — `Docs/README.md`.

| Задача | Читать |
|---|---|
| **Баг** | **`Docs/troubleshooting.md` (индекс по симптому) первым** → `Docs/UltimateXR/known-issues.md`, затем `/debug` |
| Новая фича | `Docs/README.md` (нет ли дубля) → `Docs/gameplay.md`, затем `/feature` |
| Матч, режимы, раунды | `Docs/gameplay.md`, `Docs/game-manager.md` |
| Сессия, роли, устройства | `Docs/session-architecture.md` |
| UI-меню / шрифты | `Docs/ui-design-system.md`, `Docs/ui-menu-architecture.md` / `Docs/ui-fonts.md` |
| Стена арсенала | `Docs/Arsenal/Arsenal_Code_Architecture_RU.md` |
| Сборка, Git/Plastic, перф | `Docs/release.md`, `Docs/version-control.md`, `Docs/perf-stress-test.md` |
| Unity MCP сломан | `Docs/unity-mcp.md`, `.agents/rules/unity_mcp.md` |

## Unity MCP

Инструменты `mcp__unityMCP__*` — предпочтительный способ видеть консоль и сцену. Если их нет —
не блокироваться: ошибки компиляции — `%LOCALAPPDATA%\Unity\Editor\Editor.log` (`/unity-check`);
правки сцен и объектов — списком для пользователя. `execute_code` падает с MAX_PATH — патч в
`.agents/rules/unity_mcp.md`.

## Самопроверка

**Задача не закрыта, пока агент сам не получил зелёный результат.** «Пользователю нужно проверить» —
незаконченная работа.

- Компиляция под Android: `execute_code` → `VrBattlegrounds.EditorTools.AndroidCompileGate.Run()`.
- Тесты: `run_tests(mode="EditMode", assembly_names=["VrBattlegrounds.Tests.EditMode"])` → `get_test_job`.

1. **Сначала харнесс, потом правка.** Нечем проверить — проверялка входит в задачу.
2. **Тест красный до правки.** Проверка, ни разу не показавшая отказ, ничего не доказывает.
3. **Чтение кода — гипотеза, прогон — факт.** Включая собственные выводы и аудиты.
4. **Шум харнесса ≠ отказ логики.** Mirror пишет `Error` на `[ClientRpc]` вне сервера, `[Server]`-методы
   вне сервера молча глушатся — такое падение теста — дефект теста.

Два клиента и шлем автономно недоступны: такие задачи закрывать, вынося логику под юнит-тест.

## Баги: чинить класс, а не экземпляр

1. **Назвать класс ошибки** — какое допущение нарушено и почему код позволил его нарушить.
2. **Найти все экземпляры** класса, а не только место падения.
3. **Предложить архитектуру, при которой баг невозможен** — единая точка входа, инвариант в одном
   месте, генерация вместо ручной настройки. Образцы: `StateEventAuthority`, `GameTags`+`GameTagRules`,
   `MapLoader.LoadMap`.
4. **Закрепить тестом**, который ловит весь класс.

Точечная правка — только как срочная мера, с записью класса ошибки и предложенного решения.
Большой рефакторинг — сначала предложить пользователю.

## Стиль работы

- **Словарь игры:** серия (`Series`) → карта (`MapReferee`: `Warmup → Live → Paused`) → половина → раунд
  (`RoundPhases`) → фаза (`RoundPhase`). Разминка — состояние карты без матча, не режим каталога.
  «Матч» — только в текстах для игрока («Начать матч»), в именах классов его нет.
- **События — без префикса `On`** (`RoundStartedLocal`, `SessionConnected`); `On…`/`Handle…` —
  обработчики и методы, поднимающие событие.

- На архитектурных развилках — зачем, риски, одна-две альтернативы, но рекомендовать одно.
- Уточнять, только если разные прочтения ведут к разной работе; рутинное решать самому и говорить, что выбрал.

## Терминал

- Bash-инструмент **изолирован от сети** (`curl` к localhost падает) — сетевые проверки через PowerShell.
- Git — всегда `--no-pager`.

## Документация

Новый код без документации не оставлять; тривиальное (геттеры, колбэки) не документировать.

| Что сделал | Куда |
|---|---|
| Новый класс/компонент; новый `.md` | `Docs/README.md` |
| Механика, режим | `Docs/gameplay.md` |
| Менеджер, инициализация | `Docs/game-manager.md` |
| Значимое изменение, фикс | `Docs/CHANGELOG.md` |
| Неочевидное поведение, баг SDK | `Docs/UltimateXR/known-issues.md` |
| Новое правило для агента | этот файл или `.agents/rules/<зона>.md` |

Пришлось лезть в код за тем, что должно быть в доках, — дописать доки той же задачей.
