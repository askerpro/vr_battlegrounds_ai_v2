# lobby-reflections: свет шатра Lobby — фон кругами, акценты прожекторами, гражданские лампы удалены, 2026-10-22

Свет шатра по решению пользователя (приёмка в редакторе worker и в шлеме): фон — 20 светящихся кругов под кровлей
(`LobbyTentLightFixtures`, Unlit `LobbyTentDiscGlow` тёплый белый, источник 1,06 × уровень 0,585), акценты — длинные
плафоны SM_Lamp_long только над точками стрельбы у торцов, свет — прожектор вниз 80° (9 × 0,585). На 1,2 м: фон 0,16,
пятно на точке стрельбы 0,44. Уровень всего света шатра — множитель `TentLightLevel` в `LobbyNightLighting`.

Убраны плафоны CampLighting (`LobbyDecorationAuthoring.BuildLighting`), BayLamp (`LobbyCanopyBaysBuilder`) и подвесные
SM_TopLight (Pendant_NW/SW); сборщики их больше не создают, `LobbyTentLightFixtures` убирает остатки в сцене (миграция).
Плафон над арсеналом убран: акцент арсенала — подсветка слотов, задача `arsenal-slot-lighting` (запечённый прожектор на
слот, проба в центре слота, светящаяся подложка-индикатор вместо realtime-ламп `FireArmSlotPrefab`).

Гражданские лампы удалены из базы ассетов (решение пользователя): RuralTown SM_TopLight/SM_Lamp/SM_LampPost/SM_Lamp_02,
PostApocalypticTown SM_Lamp/SM_Light1/SM_Light3, AbandonedFactory SM_Lamp_01a/SM_CoverLight_01 с 39 зависимостями, на
которые больше никто не ссылался (`tasks/lobby-reflections/tools/orphan_set.py`); таблица «Источники моделей»
`tasks/lobby-decoration/Details.md` обновлена. PortableWorkLamp (SM_Light2) оставлен.

Пробы света: у стены арсенала и полос точек стрельбы — шаг 1 м на высотах рук; проба отражений шатра на весь пол арены.
После правок геометрии (задача чистки арены) свет перезапекать через «Ночь: подготовить запекание» + Generate Lighting.
