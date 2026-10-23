# weapon-metal-materials: металл KINEMATION как в шейдере пака, без «хрома», 2026-10-23

Материалы `M_*` (`Assets/Art/Weapons/Kinemation/Materials`) выглядели «хромом» и серой вуалью рядом с `Hands_*`.

Для зоны:
- `KinemationMaterials` (конвертер Shader Graph пака → URP Lit): гладкость = lerp(x, y, 1 − шероховатость), как в
  `Shader_TacticalShooterWeapon` (было 1 − lerp(x, y, шероховатость)); карты пака читаются линеаризованными (импорт sRGB).
  Расчёт пикселя — `PackPixel`; меню «KINEMATION: перепаковать металличность и гладкость» (`RepackAll`) пересчитывает
  `*_MetallicSmoothness.png` существующих материалов, GUID те же.
- Тёмное альбедо пака поднято серым `_BaseColor` до ~0,055 (×1,06–1,6, 13 материалов) — решение пользователя; живёт
  только в `.mat` (`Convert` при пересоздании материала берёт `_TintColor` пака), воспроизведение —
  `tasks/weapon-metal-materials/tools/apply_base_tint.cs`.
- Та же формула гладкости исправлена в `Weapons/Sights/Core/IronKitMaterials.cs` (MX68; карты там уже читались
  линейно). У `M_MX68_03` диапазоны (0; 1), карта прицела от этого не меняется — перегенерация не нужна.
- Недоступное оружие в арсенале чёрное: `ArsenalSlotController.SetDimmed` заменяет `_BaseColor` на 0,3 — передано
  arsenal-slot-lighting.
- Тест `KinemationMaterialsTests` (формула, линеаризация).

Проверка: `tasks/weapon-metal-materials/changelog/2026-10-23-worker-check.md`.
