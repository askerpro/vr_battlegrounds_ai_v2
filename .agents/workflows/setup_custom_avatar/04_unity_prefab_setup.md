---
description: How to setup Unity UXR Avatar Prefab and apply configuration script
---

# Unity Prefab Setup

Этот скрипт описывает шаги по интеграции подготовленного (в Blender) FBX-аватара в Unity и применению скриптов UltimateXR.

## Шаг 1: Создание объекта в Unity
1. Вызови `mcp_unityMCP_refresh_unity` чтобы обновить AssetDatabase.
2. С помощью инструмента `manage_gameobject` (`action: create`) создай на активной сцене GameObject из этого обновленного FBX префаба.
3. Установи имя только что созданного GameObject'а в строгое значение **"AutoSetupAvatarTarget"**. Это важно, так как именно по этому маркерному имени наш перманентный скрипт поймёт, какой объект настраивать, если он был вызван в автономном режиме ИИ-агентом.

## Шаг 2: Выполнение сборочных скриптов
Вместо одного монолитного скрипта, настройка разбита на 4 атомарных подшага для гарантии отказоустойчивости.
Отправьте команды `mcp_unityMCP_execute_menu_item`, по порядку вызывая:

1. `menu_path: "Tools/VR Battlegrounds/Avatars/UXR Setup Wizard/1. Core Setup"`
   - *Распаковывает префаб и вешает UxrAvatarRig.*
2. `menu_path: "Tools/VR Battlegrounds/Avatars/UXR Setup Wizard/2. Hands Integration"`
   - *Линкует BigHandsIntegration и подключает рендереры рук.*
3. `menu_path: "Tools/VR Battlegrounds/Avatars/UXR Setup Wizard/3. Controller & Camera"`
   - *Вычисляет Eye-bounds, ставит слои телепортации и создает камеру.*
4. `menu_path: "Tools/VR Battlegrounds/Avatars/UXR Setup Wizard/4. Finalize Rig Mapping"`
   - *Маппит UxrHandIntegration на кастомные IK пальцы и переименовывает объект в `VR_Avatar_Ready`.*
5. `menu_path: "Tools/VR Battlegrounds/Avatars/UXR Setup Wizard/5. Save as Prefab"`
   - *Создает финальный префаб по пути `Assets/Prefabs/Player/HeavySoldierAvatar_Ready.prefab`.*

После удачного выполнения сохрани сцену (или проверь результат).
