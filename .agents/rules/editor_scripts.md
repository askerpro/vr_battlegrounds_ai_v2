# Правила: Написание и организация Editor скриптов

Это правило предназначено для ИИ-помощника, чтобы стандартизировать создание утилит и расширений редактора Unity.

## 1. Расположение файлов (Архитектура)
**СТРОГИЙ ЗАПРЕТ**: Никогда не создавайте папки `Editor` внутри `Assets/Scripts/` или любых других директорий, которые покрыты файлом ассембли (ASMDEF) `VrBattlegrounds.asmdef`. Это приведет к попаданию неймспейса `UnityEditor` в Android-билд для Oculus Quest и фатальной ошибке компиляции.

**ВСЕ Editor скрипты должны лежать строго в `Assets/Editor/VR_Battlegrounds/`**.
При создании нового скрипта выберите подходящую подпапку:
- `Assets/Editor/VR_Battlegrounds/Avatars/` — автоматизация для UltimateXR, настройка скелетов, рук, материалов.
- `Assets/Editor/VR_Battlegrounds/UI/` — генераторы префабов интерфейса, инструменты для HUD.
- `Assets/Editor/VR_Battlegrounds/Gameplay/` — настройка спавнов, контроллеров игрока, сцен.
- `Assets/Editor/VR_Battlegrounds/Debug/` — отладочные утилиты (старт с оффлайн сцены, меню оркестрации и др.).
- `Assets/Editor/VR_Battlegrounds/VersionControl/` — хуки и парсеры (например, GitHooksInitializer).

## 2. Организация меню (MenuItem)
Главная цель: избежать беспорядка в верхнем меню Unity и собирать все наши утилиты в один аккуратный список.

**НЕ ИСПОЛЬЗУЙТЕ** независимые корневые разделы вроде `[MenuItem("VrBattlegrounds/...")]`.

✅ **Для глобальных утилит (Верхняя панель Unity):**
Используйте префикс `Tools/VR Battlegrounds/` и далее категорию.
`[MenuItem("Tools/VR Battlegrounds/UI/Generate Elimination HUD")]`
`[MenuItem("Tools/VR Battlegrounds/Avatars/Check Skeletons")]`
`[MenuItem("Tools/VR Battlegrounds/Debug/Start from Offline Scene")]`

✅ **Для контекстного меню создания объектов (Иерархия):**
Используйте префикс `GameObject/VR Battlegrounds/`.
`[MenuItem("GameObject/VR Battlegrounds/Team Spawn Zone", false, 10)]`
