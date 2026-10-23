# arsenal-combat-lineup: оптика с кратностью, AX-50 Auto, баланс TR15/Revolver, 2026-10-23

Решения пользователя 2026-10-22/23 — `Docs/weapons/decisions.md` (Арсенал и патроны, Прицелы). Принято в шлеме (worker).

Для зоны:
- Кратность: `WeaponScopeCamera` (Sights) — моно-камера RT 384², MSAA 4×, только у глаза локального игрока (глаз ≤ 15 см за
  окуляром и ≤ 2 см от оси, стерео-матрицы). `WeaponOpticView` остаётся единственным писателем MPB линзы: точка сведения
  (`TryGetZero`) и стекло (`TryGetLens`, submesh) считаются только в нём. Шейдер `FiniteFocusReticle`: картинка и сетка в
  координатах стекла, зрачок выхода (допуск — `WeaponSightCalibrationSettings._scopeEyeTolerance`, 5 мм), стиль сетки
  «перекрестие», своя точка, флаг `_ScopeEquipped`. Убраны аллокации `CanWrite` на каждой камере.
- Комплекты: `SightKitBuilder.Optics.ScopeMagnification` → `SightKit_Sight08` (призма пака Hands ×2), для стенда — `SightKit_Sight03`, `SightKit_Sight09` (×2, на стволы не ставятся; `PackOpticBuilder.Spec.SkipParts` убирает подсветку Sight_03); писатель сбрасывает
  камеру к значениям кода. `PackOpticBuilder` (бывший LowPolyOpticBuilder) запекает модель прицела пака в оси комплекта.
- Сборщик: третий источник `LowPolyWeapon` (IWeaponModel, без рецептов — Barrett снят), `IPalmSource` в `PalmPoints`.
  `WeaponRebuildFinish` нормализует `_assetId` перед `MigrateWeapons` — первая сборка нового ствола больше не откатывается.
- `SniperRifleAuto` (рецепт `HandsPackWeaponBuilder.SniperRifleAuto`): модель SniperRifle, `FullyAutomatic`, строка
  `WeaponSystemAuthoring` как у Mk14; выстрел обрезан (`MakeSniperRifleAutoShot`) — файл снайперского набора пака несёт
  цикл болтовки.
- Баланс: TR15 32, Revolver 180/мин и 1250, SniperRifleAuto 55/0,98/240/10/3700 (`WeaponBalanceTests`, `HitsToKillTests`).
- Тесты: `ScopeOpticTests`, `CombatLineupTests`. Открыто: замер GPU камеры прицела на Quest (отдельная задача).
