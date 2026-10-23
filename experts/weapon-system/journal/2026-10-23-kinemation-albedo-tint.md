# kinemation-albedo-tint: тон альбедо и _Color задаёт конвертер KINEMATION, 2026-10-23

Хвост weapon-metal-materials (ревью weapon-system): множитель альбедо жил только в `.mat`, а `Convert` при
пересоздании материала ставил `_BaseColor` из `_TintColor` пака и не писал `_Color`.

Для зоны:
- Тон теперь задаёт `KinemationMaterials.Convert`, а не ручная правка `.mat`: цвет = `_TintColor` ×
  `AlbedoBoost(MeanLinear(_BC))` — средняя линейная яркость карты цвета поднимается до `AlbedoTarget` 0,055, не больше
  `AlbedoMaxBoost` ×1,6, округление 0,01; одинаково в `_BaseColor` и легаси `_Color` (расхождение URP
  `LitShader.ValidateMaterial` выравнивает при загрузке сцены и пачкает материал — так падала публикация базы 1113).
- `IronKitMaterials.Ensure`: `_Color` = `_BaseColor`.
- Прежние множители (weapon-metal-materials) считались по копии карты 256×256 и разошлись с расчётом `Convert` на
  0,01–0,05; решение пользователя — «привести материалы к правилу»: 8 материалов пересчитаны
  (`tasks/kinemation-albedo-tint/tools/apply_rule.cs`, сверка `boost_check.cs` mismatch=0, worker 1123). Значения в `.mat`
  — производные правила; менять их вручную не нужно, правило правится в `Convert`.
- На будущее: `MeanLinear` читает через 8-битную `ARGB32 Linear` RT (у альбедо ~0,04 шаг ~10 %) и до `LimitForAndroid`;
  смена на `RGBAHalf` сдвинет множители — тогда пересчитать материалы `apply_rule.cs`.
- Тест `KinemationMaterialsTests.AlbedoBoost_FollowsAcceptedRule` — формула на опорных средних.

Проверка: `tasks/kinemation-albedo-tint/Readme.md`, «Итоги».
