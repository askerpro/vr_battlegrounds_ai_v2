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
| VR фреймворк | UltimateXR (VRMADA), локально в `Assets/ultimate-xr/` |

## UltimateXR — ключевые факты
- Все классы библиотеки имеют префикс `Uxr` (namespace: `UltimateXR.*`)
- Базовые классы: `UxrComponent<T>`, `UxrAvatarComponent<T>`, `UxrSingleton<T>`
- Главный менеджер: `UxrManager.Instance` — управляет аватарами, событиями, состоянием
- Аватар управляется через `UxrStandardAvatarController`, не напрямую через `UxrAvatar`
- Захват объектов: `UxrGrabbableObject` + `UxrGrabber` ? обрабатывает `UxrGrabManager`
- Телепортация: `UxrTeleportLocomotion`
- Оружие: `UxrFirearmWeapon` (наследует `UxrGrabbableObject`)
- Скрипты рантайма: `Assets/ultimate-xr/Runtime/Scripts/`
- Документация UltimateXR: `Assets/ultimate-xr/Docs/guides/`

## Контекст проекта (детально)
Подробная документация для ИИ-помощника находится в `Assets/ultimate-xr/Docs/_context/`:
- `README.md` — точка входа: платформа, зависимости, список файлов контекста
- `architecture.md` — все модули UltimateXR, ключевые классы, диаграмма зависимостей

## Стиль кода
- Язык комментариев и документации: русский
- Следовать соглашениям UltimateXR для компонентов, связанных с VR

---

## Инструкция по обновлению документации

> Эти файлы — живая документация. Они должны обновляться по мере изучения проекта.

### Когда обновлять
- После изучения нового модуля UltimateXR ? обновить или создать соответствующий файл в `Assets/ultimate-xr/Docs/_context/`
- После добавления новой игровой механики ? добавить описание в `README.md` раздел "Механики"
- После решения нетривиальной задачи ? зафиксировать в `progress.md`
- После изменения архитектуры проекта ? обновить `architecture.md`

### Какой файл обновлять
| Что изучил / сделал | Файл |
|---|---|
| Новый модуль UltimateXR (Avatar, Grabbing, UI…) | `_context/<module>.md` (создать если нет) |
| Общая архитектура, новые зависимости | `_context/architecture.md` |
| Прогресс, заметки, решённые проблемы | `_context/progress.md` (создать если нет) |
| Изменился стек или платформа | Этот файл (`copilot-instructions.md`) |

### Формат обновления
При изучении нового модуля попроси Copilot:
> "Обнови `_context/architecture.md` — добавь раздел по [модуль], который мы только что разобрали"
