# VR Battlegrounds AI

VR-шутер для Oculus Quest 2/3. Unity **6000.4.1f1**, URP. Над проектом работают только ИИ-агенты.

Этот файл — **единственный источник правды** для правил агента. `.agentrules`, `.cursorrules`,
`.clinerules`, `.github/copilot-instructions.md` — указатели сюда, содержимого не несут.

## Стек

| | |
|---|---|
| Рендер | URP `17.4.0` |
| XR | `com.unity.xr.oculus` `4.5.4`, `com.unity.xr.management` `4.5.4` |
| Ввод | `com.unity.inputsystem` `1.19.0` |
| VR-фреймворк | UltimateXR (VRMADA) — вендорится в `Assets/ThirdParty/UltimateXR/` |
| Сеть | Mirror — вендорится в `Assets/ThirdParty/Mirror/` |
| Сборка кода игры | `Assets/Scripts/VrBattlegrounds.asmdef` |

Версии брать из `Packages/manifest.json` — он источник правды, не эта таблица.

## Жёсткие правила

Нарушение ломает билд или игру. Проверяй до того, как писать код.

- **Логи** — только `GameLog.*` с категорией из `GameSettings.Instance`. `Debug.Log` в игровых
  скриптах запрещён. Уровни: `Verbose` поток, `Info` события, `Warning` проблемы, `Error` сбои.
- **Смена сцены** — только `MapManager.Instance.LoadMap(sceneName)`. Прямые
  `SceneManager.LoadScene(...)` и `NetworkManager.singleton.ServerChangeScene(...)` запрещены:
  Mirror не даёт звать смену сцены из своих колбэков, клиенты рассинхронизируются.
- **Editor-скрипты** — только в `Assets/Editor/VR_Battlegrounds/<категория>/`. Папка `Editor`
  внутри `Assets/Scripts/` затянет `UnityEditor` в Android-билд Quest → фатальная ошибка
  компиляции. Категории: `Avatars/`, `UI/`, `Gameplay/`, `Debug/`, `VersionControl/`.
- **Имя в меню** — `VR Battlegrounds` с пробелом: `[MenuItem("Tools/VR Battlegrounds/...")]`,
  `[MenuItem("GameObject/VR Battlegrounds/...")]`, `[CreateAssetMenu(menuName = "VR Battlegrounds/...")]`.
  Слитное `VrBattlegrounds` плодит дубли корневых пунктов.
- **Single Responsibility** — не превращать синглтоны в God Object. Чужеродную логику выносить
  в отдельный класс/стратегию/менеджер, а не дописывать в существующий.
- **Коммит** — никогда сразу после написания кода. Сначала пользователь проверяет в Unity.
  Исключение: правки только документации. Подробности — `/commit`.
- **Правки UltimateXR SDK** — любое изменение в `Assets/ThirdParty/UltimateXR/` обязано попасть
  в `Docs/UltimateXR/sdk-patches.md`, иначе потеряется при обновлении SDK.
- Комментарии и документация — на русском.

## Поиск: всегда ограничивай область

Своего кода 119 `.cs`, в `Assets/ThirdParty/` — 1383. Поиск без скоупа даёт ~10× мусора.

```
Grep  pattern="ClassName"  path="Assets/Scripts"    # код игры
Grep  pattern="ClassName"  path="Assets/Editor"     # редакторные утилиты
```

В `Assets/ThirdParty/`, `Packages/`, `Library/` заходить только когда задача про сам SDK.
Корневые `*.csproj` (128 шт., 11 МБ), `Library/`, `Temp/`, `obj/` отсекаются `.gitignore`,
и Grep их не видит — но `Read` по ним всё равно возможен, не делай этого без нужды.

Не читай `.unity` и `.prefab` целиком — это YAML на десятки тысяч строк. Нужен конкретный
компонент — `Grep` по имени класса или GUID внутри файла.

## Что читать под задачу

Не читай документацию впрок — только по адресу задачи.

| Задача | Читать |
|---|---|
| Архитектура, «где что лежит» | `Docs/README.md` |
| Расследование бага | `Docs/UltimateXR/known-issues.md` → `sdk-patches.md`, затем `/debug` |
| Новая фича | `Docs/README.md` (нет ли дубля) → `Docs/gameplay.md`, затем `/feature` |
| API UltimateXR | исходники `Assets/ThirdParty/UltimateXR/Runtime/Scripts/` — точнее, чем `.md` |
| Модули UltimateXR обзорно | `Docs/UltimateXR/architecture.md` |
| Матч, режимы, раунды | `Docs/gameplay.md`, `Docs/game-manager.md` |
| Сессия, роли, устройства | `Docs/session-architecture.md` |
| UI-меню | `Docs/ui-menu-architecture.md` |
| Стена арсенала | `Docs/Arsenal/Arsenal_Code_Architecture_RU.md` |
| Git / Plastic | `Docs/version-control.md` |
| Unity MCP сломан | `Docs/unity-mcp.md`, `.agents/rules/unity_mcp.md` |

Полный индекс документации — `Docs/README.md`.

Углублённые правила (то, что не влезло сюда) — `.agents/rules/`:

| Файл | О чём |
|---|---|
| `editor_scripts.md` | Где размещать Editor-скрипты, соглашения по пунктам меню |
| `unity_mcp.md` | Патч ошибки MAX_PATH в `execute_code` на Windows |
| `ultimate_xr.md` | Ключевые классы UltimateXR, где искать API |
| `terminal.md` | Различия PowerShell и Bash, подводные камни |
| `documentation.md` | Что документировать, а что нет |

## Unity MCP

`mcpforunityserver` подключён через stdio (`.mcp.json`). Это единственный способ увидеть
консоль Unity, иерархию сцены и состояние объектов — когда инструменты `mcp__unityMCP__*`
доступны, предпочитай их терминалу.

Если инструментов в сессии нет — **не блокируйся и не жди**. Прежнее правило «обходные пути
запрещены» отменено: оно оставляло агента без выхода.

- Ошибки компиляции без MCP: `%LOCALAPPDATA%\Unity\Editor\Editor.log` — см. `/unity-check`.
- Сцены и объекты без MCP менять нельзя. Сформулируй списком, что сделать руками в редакторе,
  и передай пользователю.
- `execute_code` падает с ошибкой MAX_PATH на Windows — готовый патч в
  `.agents/rules/unity_mcp.md`.

## Стиль работы

- Ответ по существу задачи. Не пересказывай сделанное шаг за шагом.
- На архитектурных развилках давай **зачем** (почему так), **риски** и одну-две **альтернативы** —
  но рекомендуй что-то одно, а не выкладывай меню.
- Уточняй, только если разные прочтения ведут к разной работе. Рутинные развилки решай сам
  и скажи, что выбрал.
- Не коммить без явного подтверждения пользователя — см. жёсткие правила выше.

## Терминал

Доступны оба: **PowerShell** (основной, Windows PowerShell 5.1) и **Bash** (Git Bash). Синтаксис
у них разный — не смешивай в одном вызове.

- В PowerShell нет `&&` и `||`. Последовательность — `;`, условие — `; if ($?) { ... }`.
- Bash-инструмент **изолирован от сети** (`curl` к localhost падает с кодом 7). Сетевые
  проверки — только через PowerShell.
- Git всегда с `--no-pager`, иначе пейджер зависает в ожидании `q`.
- Многострочные скрипты в PowerShell — через here-string `@'...'@` с `'@` в нулевой колонке.

## Слэш-команды

`/commit` `/debug` `/feature` `/unity-check` `/docs-sync` — лежат в `.claude/commands/`.
Многошаговые процедуры (пайплайн кастомного аватара) — в `.agents/workflows/`.

## Документация — что обновлять

Новый код без документации не оставлять. Тривиальные методы, геттеры и обычные
`MonoBehaviour`-колбэки **не документировать** — это раздувает доки без пользы.

| Что сделал | Куда писать |
|---|---|
| Новый класс, скрипт, компонент | `Docs/README.md` |
| Игровая механика, режим | `Docs/gameplay.md` |
| Менеджер, поток инициализации | `Docs/game-manager.md` |
| Значимое изменение, фикс, рефакторинг | `Docs/CHANGELOG.md` |
| Неочевидное поведение или баг SDK | `Docs/UltimateXR/known-issues.md` |
| Правка исходников UltimateXR | `Docs/UltimateXR/sdk-patches.md` |
| Архитектура, новые зависимости | `Docs/UltimateXR/architecture.md` |
| Новое правило для агента | этот файл или `.agents/rules/<зона>.md` |

Создал новый `.md` — добавь ссылку в `Docs/README.md` и, если по теме, в этот файл.

Если пришлось лезть в код за тем, что должно было быть в доках, — допиши это в доки
той же задачей. Это главный способ удешевить следующие сессии.
