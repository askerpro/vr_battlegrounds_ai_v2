# Низкий периметр нового шатра

09.10.2026. Отдельный зарезервированный helper `LobbyPerimeterDecorationBuilder.Build(parent, arena, tent)`.
Исходники старого ExteriorBuilder, scene, Unity, source cache и shared MilitaryCamp не изменялись.
Fresh guard epoch 1 успешен. Только новый C#/meta и own task artifacts.

## API и границы ответственности

Namespace `VrBattlegrounds.EditorTools`, public static Build. Bounds в общей локальной системе
авторинга; родитель общего draft должен иметь согласованные с этой системой координаты.
Helper создаёт identity child `PerimeterDecoration`. Предполагается +Z shooting direction.
Проверяет widthX шатра=арены, ровно 2 м от каждого торца шатра до physical arena bounds.
Ground level=arena.max.y; это floor-thickness bounds, не суммарный объём всей сцены.
Шатёр, шторы, колонны, зеркало, станции, мишени, основание и реальные lights не создаёт.

Перед объектными изменениями проверяются bounds, отсутствие собственной дублирующей группы
и наличие всех семи source GameObject по GUID. При отсутствии источников пишет own
`asset-dependencies.json` и бросает конкретное исключение до создания scene objects.
Root material directory `Assets/Materials/Lobby/Exterior` должен быть подготовлен общим
авторингом; helper создаёт только `Exterior/Perimeter/BoundaryCanvas.mat`.

## Расстановка

- Передняя непрерывная низкая линия: z=tent.max.z+.34, y=floor, один слой bag height≤.18 м.
  Центры по ширине tent.size.x−.6, bag overlap .015 м, запас у side edges≈.29 м.
  При ширине15.5 м — 36 мешков; depth≤.62 м. Это визуальная граница, без физики.
- Задняя линия: z=tent.min.z−.36; основной слой≤.18 м, дополнительный слой только
  у флангов≤.34 м. Центральная часть имеет только низкий первый слой.
- В передней 2 м полосе две маленькие closed AmmoCase, только x=arena.center.x±(extents.x−1.1);
  z=lerp(tent.max.z,arena.max.z,.71)±.06. Height≤.23, footprint≤.65×.52 м.
- В задней 2 м полосе два closed MilitaryCrate, x=arena.center.x±(extents.x−1.15);
  z=lerp(tent.min.z,arena.min.z,.70)±.08. Height≤.31, footprint≤.75×.55 м.
- Все четыре strip cases проверяются renderer bounds: margin≥.15 м от физической границы,
  north height≤.25/south≤.35. Центр полос свободен от кейсов, заборов и высокого реквизита.
- За задней физической границей две разные supply groups: x=center−4.8/z=arena.min.z−4.2/yaw14,
  x=center+5.3/z=arena.min.z−6.3/yaw−19. Каждая: две paid crates, маленький ammo case
  на вычисленном верхе base crate, один paid barrel. Yaw/relative placement отличаются.
- Один paid короткий fence segment: x=center−7.1/z=arena.min.z−6.8/yaw74/height≤1.1.
  Проверяется, что его maxZ≤arena.min.z−1. За физическим краем helper не создаёт пола.

Числа высоты/footprint — ограничения builder на основе native renderer bounds во время
выполнения, не заявленные результаты Unity-preview. Bounds corners учитывают source LOD
геометрию; общий авторинг обязан удалить draft при исключении и проверить готовый результат.

## Источники

| Role | Source GUID |
|---|---|
| Sandbag | 001dd3cdc49e4775ab15cb63cc6aa80f |
| AmmoA | abc00000000012017236203265661070 |
| AmmoB | abc00000000003273235488023318933 |
| CrateA | abc00000000015158636773066772218 |
| CrateB | abc00000000006183764951078403713 |
| Barrel | abc00000000011043161318511504812 |
| Fence | abc00000000004669335183269123642 |

Sandbag — уже имеющийся `Assets/Art/Lobby/Generated/SingleSandbag.obj`, CC0 derivative12tris,
геометрически очень упрощённый; это не подтверждённый paid MilitaryCamp sandbag и не camo.
Название scene copies SourceSandbag обозначает роль. Качество source bags требует art review;
перенос иных источников этим исполнителем не делался. Остальные — настоящие paid MilitaryCamp
URP prefab, общие binary dependencies копирует единственный ведущий.
Сохраняются все source LODGroup levels; четырёхуровневый rebuild не применяется.
GameObjects source не модифицируются. Scene copies очищаются до visual components;
MonoBehaviour companions удаляются раньше Light, затем physics/Animation/Animator/Audio.
Renderer shadows/probes Off, motion vectors ForceNoMotion. Tags не назначаются.
Source materials сохраняются, заменяется только bag canvas собственным матовым материалом.

## Фактические проверки

Roslyn против Unity6000.4.1f1: exit0 без diagnostics.
Команда: `powershell -NoProfile -ExecutionPolicy Bypass -File tasks/lobby-decoration/implementation/exterior-perimeter/offline_compile.ps1`.
Лог: `reports/implementation/exterior-perimeter/offline-compile.txt`.

Offline GUID-readiness после сообщения владельца о shared copy:7 dependencies,
main_present7, readiness_errors=[]; дубли выбранных GUID не обнаружены. Детали/selected-cache paths:
`reports/implementation/exterior-perimeter/asset-dependencies-offline.json`.
Присутствие GUID не доказывает Unity AssetDatabase/material/mesh import.
Игровые tests до user acceptance не создавались. Native checks/Unity/Android/Quest не запускались.

## Остаток для общего интегратора

После shared source copy повторить GUID audit, вызвать Build с actual arena/tent bounds,
проверить фактическую высоту/непрерывность мешков, source material errors/LOD и отсутствие
Collider/Light/Audio/Animator/игровых behaviours. Обязательно проверить +Z hands/aim sweep
с обеих станций и центра, крайние мишени, свободную центральную часть полос и виды из шатра
назад на разные supply groups. Вертикальная геометрия внутри передней полосы не добавляется.
Низкие мешки остаются визуальным prototype до art/user acceptance; физически переступать
их или выходить за арену пользователю не предлагается. Task integration не считается принятой
до native результатов и пользовательской проверки.
