# UltimateXR — Контекст проекта (точка входа)

> **Для Copilot:** этот файл — точка входа. При работе с UltimateXR всегда начинай отсюда.
> Детали по каждому модулю — в соответствующих файлах этой папки.

---

## Проект

**Название:** VR Battlegrounds AI  
**Репозиторий:** https://github.com/askerpro/vr_battlegrounds_ai (ветка: `dev`)  
**Разработчик:** новичок в Unity/C#/VR, изучает итерационно

| Параметр | Значение |
|---|---|
| Движок | Unity (URP v17.0.3) |
| Платформа | Android — Oculus Quest 2 / Quest 3 |
| XR SDK | `com.unity.xr.oculus` v4.4.0 |
| Ввод | `com.unity.inputsystem` v1.11.2 |
| XR Management | `com.unity.xr.management` v4.5.4 |
| Сеть | Mirror (`Assets/ThirdParty/Mirror/`) |

---

## Библиотека UltimateXR

- **Путь:** `Assets/ThirdParty/UltimateXR/`
- **Namespace:** `UltimateXR.*`
- **Скрипты рантайма:** `Assets/ThirdParty/UltimateXR/Runtime/Scripts/`
- **Документация (оригинал):** `Assets/ThirdParty/UltimateXR/Docs/guides/`
- **Контекст для ИИ:** `Assets/ThirdParty/UltimateXR/Docs/_context/` ← текущая папка

> Ссылки вида `/guides/<name>` в документации = `Assets/ThirdParty/UltimateXR/Docs/guides/<name>.md`

---

## Файлы контекста

| Файл | Содержание | Статус |
|---|---|---|
| `README.md` | Этот файл — точка входа | ? Готов |
| `architecture.md` | Все модули, ключевые классы, диаграмма | ? Готов |
| `avatar-guide.md` | Аватары, rig, HandsIntegration | ? Готов |
| `interactions.md` | Grabbing, UxrGrabbableObject, события | ? Готов |
| `locomotion.md` | Телепортация, smooth movement | ? Готов |
| `ui.md` | VR UI, лазерные указки, fingertip | ? Готов |
| `scripting.md` | Паттерны кода, примеры API, input | ? Готов |
| `progress.md` | Прогресс изучения, заметки, TODO | ? Создать по мере работы |

> **Документация игры** (геймплей, механики, архитектура матча) находится отдельно: `Assets/Docs/`

---

## Игровые механики

> Документация игры находится в `Assets/Docs/`. Ниже — только механики, связанные с UltimateXR SDK.

| Механика | Статус | Файл контекста |
|---|---|---|
| Захват оружия | ? Не реализовано | `interactions.md` |
| Стрельба | ? Не реализовано | `architecture.md` ? Weapons |
| VR UI (меню администратора) | ? Не реализовано | `ui.md` |
| Mirror + UxrNetworkImplementation | ? Не реализовано | `architecture.md` ? Networking |
