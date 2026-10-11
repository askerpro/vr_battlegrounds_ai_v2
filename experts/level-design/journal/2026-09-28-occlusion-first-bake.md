# Первый bake окклюзии: Lobby, TestMap1, TestMap2

Коммит `0c00007d2d335fe234f4627760e4da9e85401505` (2026-09-28, «perf: запечён occlusion culling для Lobby,
TestMap1, TestMap2»). Перед этим `826d8d9acb38183b5a2fae071f8fe3d754ca6606` запёк свет TestMap2 —
единственный `LightingData.asset` в сценах. Что проверено при bake и бюджеты кадра — unknown (в источниках нет).
Правила разметки окклюдеров сейчас — в `OcclusionBakeTool` (только Occluder/OccludeeStatic, мелкое и
вырезанное по альфе — только Occludee, подвижное исключено).

Источники: git log origin/dev; `Assets/Editor/VR_Battlegrounds/Gameplay/OcclusionBakeTool.cs` (summary).
