# Единственный согласованный correcting pass: связные полосы леса

09.10.2026. Root поручил заменить12 far islands связными полосами без увеличения104 деревьев.
Изменены только far layout calls и FarForestIsland→EndForestBand/SideForestBand в exclusive
LobbyExteriorDecorationBuilder.cs. Near38, Factory4 indices3/13/22/31, API и768-triangle
closed horizon не менялись. Approved `Assets/Materials/Lobby/Exterior/RuralFirNeedlesGreen.mat`
path сохранён; source/materials/scene не писались. C# frozen для native Build ведущего.

## Source pose recipe

66 прежних far M_01 распределены: North24, South22, East10, West10. Все heights10–14 м,
разные deterministic yaw; источник и4LOD policy не изменены. Две end полосы используют
два смещённых по глубине/поперечнику ряда, sine изгиб и jitter. Боковые полосы соединяют
концы end полос, имеют переменную глубину/spacing/height, не являются ровным fence.

`forest_band_poses.ps1` воспроизводит recipe с seeded System.Random и single casts,
без Unity.66 рассчитанных center/height/yaw записаны в
`reports/implementation/exterior-paid/forest-band-source-poses.json`.
Это offline source poses с возможной микропогрешностью sin/casts, не native scene measurement.

| Band | Count/seed | X диапазон center, м | Z диапазон center, м |
|---|---|---|---|
| NorthBehindTargets |24/311|−42.39..46.44|48.09..69.07|
| SouthBehindCamp |22/419|−43.84..46.88|−68.91..−46.85|
| EastForestLayer |10/521|42.10..51.87|−48.59..52.08|
| WestForestLayer |10/631|−51.95..−42.25|−52.73..52.88|

North z имеет защитный clamp46.2..73, оставаясь внутри root-requested45–75 band.
Текущий sampled ближайший center48.09, на9.70 м позади максимального target Z38.394.
Canopy bounds не равны center: отдельная native проверка сохраняется, но дерево не создаётся
в полосе самих мишеней. Side trunks |X|≥42, перед арсеналом/physical arena новых props нет.
Южная полоса находится позади distributed rear camp; high source props внутри арены не добавлены.

## Проверки и границы

Свежий guard epoch1 успешен. Roslyn helper+read-only source resolver против Unity6000.4.1f1:
exit0 без diagnostics; `reports/implementation/exterior-paid/offline-compile.txt`.
Source objects104=38near+66far; четыре Factory входят в38near, остальные100 — M_01.
Full source mesh topology/LOD arrays/count не увеличены. При меньшей дистанции native LOD
выбор может поднять visible triangle cost: неизменность source budget не означает неизменный
frame GPU budget. Никаких ForceLOD/новых shader/material правок для сокрытия этого риска нет.
Главный owner измеряет visible levels/tris/overdraw и проверяет Quest/aim rays/новые captures.

Frozen helper SHA `8864D094FED758C87042ECE16CBB171914A083AB79711DB34AE33D1C859CDF91`.
Главный owner выполняет один native Build; самостоятельного нового арт-ответвления после
передачи не ведётся. Paid SandBarrier/perimeter остаются другой отдельно назначенной задачей.
