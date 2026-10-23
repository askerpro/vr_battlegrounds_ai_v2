# arsenal-slot-lighting: подсветка слотов арсенала без realtime-ламп, 2026-10-23

Для зоны:
- Слот: realtime-лампа `LEDs` заменена боковой LED-лентой `ArsenalSlotGlow` (бортики `StripLip_L/R`, точки
  `LampPoints` для запечённых ламп). Цвет «доступно» — янтарный (1; 0,6; 0,25).
- Недоступный слот (дорого, стена ничья, закрыт): лента выключена, ствол и запасной магазин берут пробы в 2 м перед
  слотом (`RefreshDimming`, `Renderer.probeAnchor`). Затемнение основы ×0,3 (T-45) и поле `_unaffordableBrightness`
  убраны — металл выходил чёрным. Новый инвариант: писатель `probeAnchor` предметов слота — только контроллер слота;
  `ArsenalMagazineOffer` лишь зовёт `RefreshDimming`.
- Раскладки 9 стволов стены по образцу Mk14 (`<Ствол>_Pegboard.asset`), зазор рядов Lobby 0,095/0,075.
- Тесты: `ArsenalSlotLightingTests`. Документы: `Docs/Arsenal/Arsenal_Code_Architecture_RU.md` «Подсветка слотов»,
  `Docs/Arsenal/ArsenalWall_Design_RU.md` §5.
