# Внешний лагерь: paid props и низкий горизонт

09.10.2026. Ведущий вернул exclusive write ownership существующего
`LobbyExteriorDecorationBuilder.cs`; изменения применены только в нём и own task artifacts.
Perimeter builder, shared resolver/source/materials, scene и Unity этим исполнителем не изменялись.
API `Build(Transform parent, Bounds arena)`; C# frozen перед native Build ведущего.

## Изменения относительно предыдущего helper

- Удалено создание 12 гигантских отдельных boulder silhouettes. Камни ближнего плана остаются.
- Два непрерывных низких гребня: Z190 и Z−210, ширины320/300 м, глубина32/36 м,
  основание6 м ниже floor. Один own continuous mesh:40 сегментов,4 vertex rows,
  240 triangles на объект,480 суммарно. Два спокойных own grey-blue материала формируют
  нижний/дальний слой; общие vendor materials не меняются.
- Аналитический crest profile после масштаба20 и base−6 находится на8.97–13.45 м выше floor.
  Это расчёт формулы, не native capture/bounds. Новое asset имя ContinuousHorizonRidge.
- Вместо равномерного кольца28 fir —12 неравномерных островков по5/6 деревьев,66 far trees.
  Near/mid38 деревьев и боковые камни сохранены:104 дерева суммарно.
- Островки: (−36,86),(−10,96),(18,92),(47,82),(−44,−83),(−18,−97),(13,−94),(43,−82),
  (86,−30),(91,28),(−88,−32),(−90,25). Jitter±3.5 X/±5 Z, height8–13, разные yaw.
  Tree thresholds .5/.3/.15/.012 без ForceLOD/crossfade. Видимые tris/drawcalls пока неизвестны.
- Primitive tower заменена на paid SM_WatchTower_01: (24,−25),yaw12,uniform actual-bounds
  height8 м. Все5 source LOD сохранены. Вертикальный акцент теперь со стороны заднего лагеря.
- CC0 SupplyTruckCandidate заменён на paid SM_ArmoredVehicle: (22,−34),yaw18,scale1.
  Source материалы не заменяются, все5 source LOD сохранены. Старый OBJ остаётся архивным
  unreferenced candidate, текущий Build его не создаёт и не генерирует Truck_* materials.
- Primitive fences заменены на6×paid SM_Fence_long_2m и2×SM_Fence_long_4m.
  Группы (24,−14),yaw90; (−24,15),yaw90; (24,−46),yaw−8. Единичный source scale.
  Третий segment перенесён на−Z для большей плотности заднего лагеря.

PaidModel использует исключительно read-only `LobbyMilitaryCampSources.Prefab(stem)`.
Scene copies очищаются: сначала companion MonoBehaviour, затем Light/physics/animation/audio.
Это учитывает RequireComponent у UniversalAdditionalLightData. Renderer shadows/probes Off;
source LODGroup arrays сохраняются, RecalculateBounds не меняет число levels/thresholds.
Общий Model/ConfigureLods также больше не пересобирает существующие non-tree LODGroup.
Raw fir остаётся специальным четырёхуровневым случаем. Ни ForceLOD0, ни shared material writes
не добавлены. Все высокие лагерные объекты находятся на флангах |X|>11, вне центральной полосы.

## Фактические проверки и границы

Собственная Roslyn компиляция helper + read-only LobbyMilitaryCampSources против
Unity6000.4.1f1: exit0 без diagnostics. Скрипт `offline_compile.ps1`, лог
`reports/implementation/exterior-paid/offline-compile.txt`. Другие implementation files
не включались в эту собственную проверку; общий compile выполняет ведущий.

`verify_paid.py` читает только4 main source prefab/meta и вычисляет ridge profile;
отчёт `reports/implementation/exterior-paid/source-and-horizon-audit.json`.
Это source-readiness/LOD metadata, не Unity import, rendered appearance или actual triangles
source FBX. Shared vendor shader repair и сообщение ведущего о64 материалах без error shaders
принадлежат его проверке, этим исполнителем повторно не заявляются проверенными.

Native Build, human-height views, реальная высота tower/vehicle, отсутствие source colliders,
видимость всех пяти LOD уровней машины/вышки, tree alpha-overdraw, Quest и user acceptance
остаются за ведущим. Особенно проверить машину возле SouthEastForest, tower рядом со складом,
отсутствие широких пустых просветов между far islands и низкий силуэт гребней за лесом.
Главные арт-изменения согласованы root; новых самостоятельно выбранных source не переносилось.
