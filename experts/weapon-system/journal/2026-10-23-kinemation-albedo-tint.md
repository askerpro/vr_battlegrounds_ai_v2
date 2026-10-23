# kinemation-albedo-tint: принятый тон альбедо и _Color в конвертере KINEMATION, 2026-10-23

Хвост weapon-metal-materials (ревью weapon-system): множитель альбедо жил только в `.mat`, а `Convert` при
пересоздании материала ставил `_BaseColor` из `_TintColor` пака и не писал `_Color`.

Для зоны:
- `KinemationMaterials.Convert`: цвет = `_TintColor` × `AlbedoBoost(MeanLinear(_BC))` — средняя линейная яркость карты
  цвета поднимается до `AlbedoTarget` 0,055, не больше `AlbedoMaxBoost` ×1,6, округление 0,01; одинаково в
  `_BaseColor` и легаси `_Color` (расхождение URP `LitShader.ValidateMaterial` выравнивает при загрузке сцены и пачкает
  материал — так падала публикация базы worker 1113).
- `IronKitMaterials.Ensure`: `_Color` = `_BaseColor`.
- Существующие 20 материалов `Assets/Art/Weapons/Kinemation/Materials` не менялись; расчёт сверен с ними в worker.
- Тест `KinemationMaterialsTests.AlbedoBoost_ReproducesAcceptedTint`.

Проверка: `tasks/kinemation-albedo-tint/Readme.md`.
