# weapon-batch-legacy-recipe: известные красные EditMode после пересборки арсенала, 2026-10-24

`Tools/e2e/editmode-known-red.json` (рабочая копия задачи, ещё не подтверждена прогоном): сняты записи, которые должна
закрыть задача —
- `GrabPoseCoverageTests…(FabarmSDASS, CyborgHandsBase)` и `…(Herrington, CyborgHandsBase)`: патрон в гнезде теперь
  вложенный экземпляр префаба патрона и наследует его хват (`ManualLoadingAuthoring`);
- `WeaponShotSoundTests.Стволы_реестра_не_делят_звук_выстрела`: у каждого ствола реестра свой выстрел из набора пака.

Подтверждение — heavy `--profile editor --suite default --editmode also` перед merge-request; путь к `verdict.json` — в
review-request задачи. Новые красные против списка не добавлять без разбора.
