# Сборные корпуса базы — 2026-10-10

Замороженный runnable helper `LobbyBaseBuildingsBuilder.Build(Transform parent, Bounds arena, Bounds tent)` создаёт новую группу BaseBuildings. Пол/Y — actual arena.max.y, XZ относительно arena.center. Исходные MilitaryCamp assets только читаются через общий Sources.Prefab.

| Корпус | Footprint X | Footprint Z | Состав |
|---|---|---|---|
| Казарма A | −44..−40 | −21..−9 | 31 модуль, кровля01 |
| Казарма B | −37..−33 | −21..−9 | 31 модуль, кровля02, смещённое чередование окон |
| Склад и мастерская | 34..42 | −24..−12 | 37 модулей, кровля02, дополнительное фасадное окно4м |

Согласованные с Exterior reserved envelopes: westX[-45,-32]Z[-22,-8],eastX[31,45]Z[-25,-11]. Состояние ограды ±X55/+Z65/−Z60 и источники входного/строительного зон — владение ведущего/Exterior. Здания не пересекают центральное направление мишеней +Z и эти зоны. Дополнительный отдельный workshop не добавлен: существующий восточный корпус уже имеет роль склад-мастерская, reserved width не расширяем.

## Модульный контракт

Native preview просмотрен для окна2м, входа, крыши и одиночной двери: бетонные стены, настоящий оконный переплёт, плоская плита, вход шириной3м. Размеры подтверждены `reports/implementation/paid-preview/base-buildings/native-bounds.json`.

Крыша состоит из родных плит4×4м. Высота стен3.002м, крыша +.15м. Полные задние стены — wall_4x4m_A в родном размере. Длинные стены — чередование window_2m_A/plain wall_2m_A, сверху непрерывное глухое заполнение высотой1.002м. Южный фасад — entrance_2m_A actual3м ширина/2.50023м высота, над ним cap .50177м и рядом глухая1м секция; у8м корпуса дополнительное window_4m_A +верхняя секция.

Оконные, входные, кровельные и полные стеновые модули имеют scale1. Только глухие заполнения используют исходную wall_2m_A с явно записанным nonuniform scaleX/scaleY; thickness/source materials/UV остаются исходными. Стретч рисунка на этих глухих cap/filler — известный компромисс и предмет native art review. Переплёты/дверной проём не растягиваются. Вход остаётся открытым, Door_single шириной1.1м не подменяет трёхметровый вход. Hangar36.4м не используется.

Все sourceLOD2/3 сохраняются, no ForceLOD/перестроения source arrays. На новых инстансах остаются только Transform, MeshFilter, MeshRenderer, LODGroup; игровые скрипты/Collider/Light/Animator удалены. Нет BatchingStatic под LODGroup. Нет изменений shared prefab/material/texture, новых SDK, Scene/MCP или gameplay.

## Проверки

Fresh epoch1 guard успешен перед записью. New .meta GUID c32949bd8c4b4148a7af7b89325294c1 проверен по Assets, совпадений нет. Offline compile helper+Sources против Unity6000.4.1f1 API/VrBattlegrounds.dll: exit0; scoped diff-check: exit0. Новые untracked файлы отдельно проверены rg на trailing whitespace: совпадений0. Лог `reports/implementation/base-buildings/offline-compile.txt`. Код SHA256 `4D9044A26246ECC5CBB62321F8A4CD83AE6A2C2241AD366A9583D5B8E9A7B949`.

Source prefab GUID: wall4m `abc00000000016267615287660173025`; wall2m `abc00000000009277847641397237419`; window2m `abc00000000000795625392220992488`; window4m `abc00000000002814992928678820954`; entrance `abc00000000009639275756007365438`; roof01 `abc00000000003420785275937382796`; roof02 `abc00000000000628014937827482183`.

Native Build этим исполнителем не выполнялся. Build запишет actual bounds каждого модуля по всем LOD, sourceGUID/scale, суммарные bounds корпуса и placed/missing/invalid. Целевой native результат: placed3, invalid0, missing0, modules99. Не помещённый в reserved envelope/пересекающий другой корпус уничтожается только внутри собственной новой группы и отмечается invalid.

Для приемки ведущий проверяет стыки стен/cap/крыши без дыр, ориентацию фасадов, читаемость двух казарм и склада-мастерской, окна/проёмы, отсутствие источников за оградой/в кронах, дальний LOD и стоимость99 модульных instances. Root art review остаётся обязательным; offline compile не означает визуальную приемку.
