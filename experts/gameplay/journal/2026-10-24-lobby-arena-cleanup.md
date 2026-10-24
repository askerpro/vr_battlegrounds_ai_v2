# lobby-arena-cleanup: урон на стрельбище по зонам, 2026-10-24

Для зоны:
- Монитор попаданий (планшет, «Обзор») считает урон как в бою: спад SDK по дистанции от дула
  (`ShootingRangePhotoCapture.DamageAt`, та же интерполяция, что `UxrWeaponManager`) × `HitZoneDamage.Multiplier`
  зоны щита. Зоны размечены на мишени `ShootingTargetZone` (`Pivot/HeadZone` — голова ×3; вне разметки — торс ×1).
  Правило множителей одно — `HitZoneDamage`; урон мишени ни к чему не применяется, только показывается.
- Снимается только свой выстрел (`StateEventAuthority.IsAuthorOfItem` по источнику снаряда); сетевых действий нет.
- Описание — `Docs/gameplay.md`, «Мишень» и «Граница стрельбища».
