# arsenal-combat-lineup: хваты AX-50 Auto, 2026-10-23

- `SniperRifleAuto` и `SniperRifleAuto_mag` — та же модель, что SniperRifle: записи хвата MEF/Cyborg и трансформы
  выравнивания перенесены с SniperRifle/SniperRifle_mag через `WeaponGripCarryOver.Capture` → смена пути → `Restore`
  (одноразово; дальше пересборка переносит их как обычно). Позы общие с SniperRifle.
- Экспорт `_orig` для нового рецепта: `HandsPack_SniperRifleAuto_Magazine_orig`, `_orig_sources.json`.
