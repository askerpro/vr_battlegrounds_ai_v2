# arsenal-combat-lineup: боевой состав карт и Lobby, 2026-10-23

- `CurrentGameplayArsenal` (все игровые `MapData`): TR15, MKR9, Herrington, SRM12, SniperRifleAuto (AX-50 Auto), Viper,
  Revolver. Убраны Nova (ShotgunReal), Mk14, Gun, Uzi — остаются в `FullDemoArsenal` Lobby.
- `FullDemoArsenal`: SniperRifleAuto на месте болтового SniperRifle; SniperRifle остаётся в `WeaponRegistry` (бот-стенд).
- `ArsenalCompositionCatalog` пересобран по реестру (18 стволов, + SniperRifleAuto).
- Баланс в `WeaponInfo`: TR15 32, Revolver 180/1250, SniperRifleAuto 55/240/3700. Тест `CombatLineupTests`.
