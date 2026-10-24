# weapon-batch-legacy-recipe: AR15/FabarmSDASS на полном рецепте без доноров, звуки из паков, паспорт снаряда, 2026-10-23

Решения пользователя 2026-10-23/24 — `Docs/weapons/decisions.md` (Отклик, Захват, Арсенал и патроны). Принято в шлеме
2026-10-24; тесты на принятую логику, heavy и ревью — после приёмки.

Для зоны:
- Доноров нет: AR15 и FabarmSDASS — `HandsPackWeaponBuilder.Full` (поля рецепта `RootScale`, `StaticActionTravel`,
  `StaticTriggerDegrees`, `ManualCycle`, `MechanismMotion`, `PackSounds`, `InsertAudio`); Browning — `HandsPackLegacyRebuild.Browning`.
  Общие значения — `HandsPackBuildDefaults`; снимки `LegacyBuildDonors`, `Ar15ScaleCalibration`, `BuildShotgunReal` удалены.
- `WeaponInfo`: скорость и дальность снаряда (`WeaponProjectileMigration.CaptureFromPrefabs` — до первой пересборки),
  режим огня из баланса, ручной цикл — рецепт; `RequireCycleMatchesBalance` отказывает при противоречии.
- Звуки: нарезка `HandsPackAudio`/`KinemationAudio` (тишина до удара срезается), дефолты категорий
  `WeaponFeedbackDefaults` (`_magazineInsert`, `_magazineTakeOut`, `_ejectaImpacts`, `_dropImpacts`) — `Docs/sound-library.md`.
- Патрон в гнезде Herrington/FabarmSDASS — вложенный экземпляр (`ManualLoadingAuthoring`); кнопки-наследие сняты.
- Болтовой `SniperRifle` убран из реестра и каталога композиции (файлы остаются); `SniperRifleAuto` — категория Rifle.
- Открыто: ручные хваты затворов MKR9/TR15; покупка пака звуков с нарезкой ([I](../../../tasks/weapon-batch-legacy-recipe/research/I-weapon-sound-packs.md)).
  Исследования: [B](../../../tasks/weapon-batch-legacy-recipe/research/B-ar15-fabarm-recipe.md),
  [E](../../../tasks/weapon-batch-legacy-recipe/research/E-donor-fields.md),
  [F](../../../tasks/weapon-batch-legacy-recipe/research/F-stage3-impl.md),
  [H](../../../tasks/weapon-batch-legacy-recipe/research/H-sound-defaults-assets.md),
  [K](../../../tasks/weapon-batch-legacy-recipe/research/K-leading-silence.md),
  [L](../../../tasks/weapon-batch-legacy-recipe/research/L-legacy-sound-files.md).
