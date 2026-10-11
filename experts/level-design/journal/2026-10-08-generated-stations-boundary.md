# Generated-станции влиты: runtime — gameplay, место станции — level-design

Этап `map-runtime-bootstrap/generated-stations` merged `c427527df9ab7b29d4f578220c6737e526724b37`, принят
пользователем 2026-10-08. Evidence (хаб): worker 212 — компиляция 0 ошибок, AndroidCompileGate PASS, пробы
generated-stations-1..3 32/32, EditMode 658 с 7 падениями только в тестах содержимого сцен вне области этапа;
хост Lobby→TestMap1→reload→Lobby server Ready. Шлем не требовался (нет видимого изменения).

Граница (карта экспертов): `MapRuntimeCatalog`, `MapArsenalCompositionAdapter`, MapBootstrap и корпус/стиль
станции — gameplay; положение станции в сцене, ниша ограды (SpawnZoneBoundaryBlock/Opening), ссылка на зону —
level-design. Ключ места хранит `ArsenalStationCompositionBinding` на станции. Какие 7 тестов сцен падали —
unknown; известный шум — LFS-указатель `TestMap2/LightingData.asset`.

Источники: хаб status (stage evidence/acceptance); `tasks/map-runtime-bootstrap/Readme.md` «Статус»;
`tasks/map-runtime-bootstrap/Details.md` «Известные шумы тестов и харнесса»; `Docs/scene-hierarchy.md` «Группы сцены».
