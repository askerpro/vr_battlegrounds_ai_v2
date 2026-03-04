# VR Battlegrounds AI — Copilot Instructions

## Проект
VR-шутер для Oculus Quest 2/3, разрабатываемый на Unity.
- Репозиторий: https://github.com/askerpro/vr_battlegrounds_ai (ветка: `dev`)
- Разработчик — новичок в Unity, C# и VR SDK, активно изучает технологии итерационно.

## Стек и зависимости
| Параметр | Значение |
|---|---|
| Рендер | URP (Universal Render Pipeline) v17.0.3 |
| XR SDK | `com.unity.xr.oculus` v4.4.0 |
| Ввод | `com.unity.inputsystem` v1.11.2 |
| XR Management | `com.unity.xr.management` v4.5.4 |
| VR фреймворк | UltimateXR (VRMADA), локально в `Assets/ThirdParty/ultimate-xr/` |
| Сеть | Mirror, локально в `Assets/ThirdParty/Mirror/` |

## UltimateXR — ключевые факты
- Все классы библиотеки имеют префикс `Uxr` (namespace: `UltimateXR.*`)
- Базовые классы: `UxrComponent<T>`, `UxrAvatarComponent<T>`, `UxrSingleton<T>`
- Главный менеджер: `UxrManager.Instance` — управляет аватарами, событиями, состоянием
- Аватар управляется через `UxrStandardAvatarController`, не напрямую через `UxrAvatar`
- Захват объектов: `UxrGrabbableObject` + `UxrGrabber` ? обрабатывает `UxrGrabManager`
- Телепортация: `UxrTeleportLocomotion`
- Оружие: `UxrFirearmWeapon` (наследует `UxrGrabbableObject`)
- Скрипты рантайма: `Assets/ThirdParty/ultimate-xr/Runtime/Scripts/`
- Документация UltimateXR: `Assets/ThirdParty/ultimate-xr/Docs/guides/`

## Контекст проекта (детально)
Документация разделена на две части:

**Документация UltimateXR SDK** — `Assets/ThirdParty/ultimate-xr/Docs/_context/`:
- `README.md` — точка входа: платформа, зависимости, список файлов контекста
- `architecture.md` — все модули UltimateXR, ключевые классы, диаграмма зависимостей

**Документация игры** — `Assets/Docs/`:
- `README.md` — точка входа в документацию игры
- `gameplay.md` — геймплей, структура матча, режимы, менеджеры, физическая арена

## Стиль кода
- Язык комментариев и документации: русский
- Следовать соглашениям UltimateXR для компонентов, связанных с VR

---

## Инструкция по обновлению документации

> Эти файлы — живая документация. Они должны обновляться по мере изучения проекта.

### Когда обновлять
- После изучения нового модуля UltimateXR — обновить или создать соответствующий файл в `Assets/ThirdParty/ultimate-xr/Docs/_context/`
- После добавления новой игровой механики — обновить `Assets/Docs/gameplay.md`
- После решения нетривиальной задачи — зафиксировать в `progress.md`
- После изменения архитектуры проекта — обновить `architecture.md`

### Какой файл обновлять
| Что изучил / сделал | Файл |
|---|---|
| Новый модуль UltimateXR (Avatar, Grabbing, UI…) | `Assets/ThirdParty/ultimate-xr/Docs/_context/<module>.md` (создать если нет) |
| Общая архитектура, новые зависимости | `Assets/ThirdParty/ultimate-xr/Docs/_context/architecture.md` |
| Прогресс, заметки, решённые проблемы | `Assets/ThirdParty/ultimate-xr/Docs/_context/progress.md` (создать если нет) |
| Изменился стек или платформа | Этот файл (`copilot-instructions.md`) |
| Новая игровая механика, режим, изменение архитектуры матча | `Assets/Docs/gameplay.md` |

### Формат обновления
При изучении нового модуля попроси Copilot:
> "Обнови `_context/architecture.md` — добавь раздел по [модуль], который мы только что разобрали"

---

## Правила работы Copilot с инструментами

### Чтение файлов
- **Всегда** использовать `get_file` с параметром `includeLineNumbers: true`
- Это единственный способ получить корректное содержимое без проблем с кодировкой

### Команды терминала
- **Никогда** не использовать многострочные команды в одном вызове `run_command_in_terminal`
- PowerShell переходит в режим ожидания `>>` при многострочном вводе — команда зависает
- Каждый вызов `run_command_in_terminal` должен содержать **ровно одну команду**
- Если нужно выполнить несколько команд — делать отдельный вызов для каждой
