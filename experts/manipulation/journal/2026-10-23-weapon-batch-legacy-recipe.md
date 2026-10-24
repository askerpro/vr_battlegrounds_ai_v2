# weapon-batch-legacy-recipe: звуки якорей и падения — дефолты в ассетах, 2026-10-23

Решение пользователя 2026-10-23: звуковые дефолты — ассеты с подстановкой в момент проигрывания, не копии в префабах.
Принято в шлеме 2026-10-24. Таблица клипов — `Docs/sound-library.md`, «Звуковые дефолты».

Для зоны:
- `AnchorSound`: `AnchorSoundOverride` предмета → свой клип якоря → у гнезда магазина и окна приёма патрона категория
  ствола (`WeaponFeedbackDefaults`) → роль якоря (`MagazineAnchorSoundDefaults`: World, Primary, Secondary, Magazine,
  WeaponMagazine; роль — `AnchorRole.Get`) → общий звук вставки. Играет только приёмник.
- `ImpactSound`: свои клипы → `_dropImpacts` категории ствола → `ImpactSoundDefaults` (Resources, `Weapon`/`Magazine`);
  удар гильзы или патрона (`WeaponEjecta`) не будит звук падения лежащего предмета.
- `AnchorSoundInstaller` клипов не пишет; старую пару карманов снимает `AnchorSoundRoleDefaultsCleanup`, копии
  дефолтов — `SoundDefaultsCleanup`; `ImpactSoundInstaller` снимает копию набора по умолчанию.
- `Universal/Weapon_Select.flac` импортируется как AudioClip (0,50 с, проверено на worker 2026-10-24), остаётся в
  `AudioSource` карманов `Anchor_Back`, `MagazinePocket`. `AmmoInsertSoundTests`: общий звук гнёзд заменён проверкой
  «своя нарезка или дефолт категории».
  Исследования: [G](../../../tasks/weapon-batch-legacy-recipe/research/G-interaction-sounds.md),
  [H](../../../tasks/weapon-batch-legacy-recipe/research/H-sound-defaults-assets.md),
  [L](../../../tasks/weapon-batch-legacy-recipe/research/L-legacy-sound-files.md).
