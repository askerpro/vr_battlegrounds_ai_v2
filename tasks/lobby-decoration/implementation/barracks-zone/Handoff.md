# Оформление казарм — 2026-10-10

Срез готов к нативной сборке ведущим; художественная приёмка ещё не выполнена. Главный checkout, ветка `lobby-decoration`, HEAD `d274fa9361463a0804b255b7e581831415e6a978`. Свежий guard текущего implementation epoch1 успешен перед записью. Общие builders, Scene/MCP, игровой код, исходные assets, тесты, commit/push не изменялись.

## Замороженный контракт

`LobbyBarracksDressingBuilder.Build(Transform parent, Bounds arena, Bounds tent)` требует `parent/BaseBuildings/Казарма A` и `Казарма B`, создаёт только новый `parent/BarracksDressing`. При ошибке удаляет собственный новый root. Дубликат root запрещён. XZ относительно фактического arena.center, пол относительно arena.max.y. Резерв X[-31,-16]/Z[-24,-8]. Никаких новых assets/materials; исходные материалы только читаются.

Фактические native shells проверены по `reports/implementation/barracks-zone/native-shell-snapshot.json` и лично просмотренным кадрам `visual-review/siteplan-final/barracks-south-front.png`, `barracks-west-window.png`, `barracks-south-edge.png`. A X[-29.000256,-25.000202], B X[-22.000256,-17.998356], обе Z≈[-22,-10]. Входы действительно южные, без двери, внешние bounds 3×2.500229×.3017 м. Узкая Door_single 1.101 м не подходит. Существующие корпуса/склад/окна/крыша/основной свет сохраняются.

Состав: один общий короткий подход WornSoil X[-29,-17.5]/Z[-23.95,-22.25], две низкие бетонные ступени 2.8×.6×.08 м, по одному исходному стулу рядом с каждым входом, два исходных дверных светильника, две входные и две восточные plaque «КАЗАРМА A/B». Боковые таблички выше оконной полосы и обращены к игроку в шатре. Текст равномерно вписывается по фактическим TextMesh bounds в plaque 1.55×.29 м. Нет дополнительных столов, оружейных ящиков, боеприпасов, бочек и неподдержанных кружек.

Источники `Assets/Art/Lobby/Imported/MilitaryCamp/Art/Prefabs/SM_Chair.prefab` и `SM_Door_lamp_a.prefab`, `RequiredSources` публикует оба stem. Исходные LOD/material сохраняются, масштаб source1. Человекоподобный размер chair, опора нижнего LOD0 на фактический пол; весь LOD envelope отдельно проверяется. Mono/URP зависимости удаляются до Light, затем остаются только static mesh/LOD. Вывески имеют TextMesh. Fail-fast counters Collider/Light/MonoBehaviour/AudioSource/Animator ожидаются 0.

Socket paths: `BarracksDressing/BarracksA/LightingSocket__BarracksA__CommonLightingOwner` и аналогичный B. В helper нет Light; общий владелец решает световой бюджет и создаёт Light. Существующий боковой свет казарм не изменяется этим срезом.

## Проверки и следующий gate

Offline Roslyn helper+read-only MilitaryCampSources против установленной Unity6000.4.1f1: **exit0**, лог `reports/implementation/barracks-zone/offline-compile.txt`; это не Unity import/project/Android/Quest проверка. Meta GUID `621d00af552f4d5abfe9b83dd4bbadc7`, поиск совпадений по Assets нашёл только собственный meta. Scoped diff-check exit0, trailing whitespace отсутствует.

Замороженный код: git blob SHA `c5112324f2edfa0d3277a433eb0df05ee3704961`, SHA256 `01A3D256E72F68A59A802019DC7296A896D18407172C4710DAD9E04CF07D80C5`.

Ведущему: вызвать Build после BaseBuildings до common lighting; сохранить `LastReport` (helper также пишет `latest-build.json`). Снять южный фасад крупно, общий подход с юга/сверху, вид из west tent window и south edge. Проверить TextMesh ориентацию/читаемость, контакт ступеней и ножек, крепление ламп/наличие socket, свободные дверные corridor 1.8 м, отсутствие оконных пересечений и clutter. Общий exterior floodlighting принадлежит следующему отдельному решению root. После снимков root выполняет art gate. Bake, Unity/Quest пользовательская приёмка и финальные gameplay/runtime проверки остаются pending.
