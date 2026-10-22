# lobby-material-fixes: розовые HDRP Lit фиксером, уточнение material.emission-off, 2026-10-22

`BakedLitMaterialFixer` (правило `shader.non-mobile`, QPM-SHADER-001/004) получил источник «HDRP Lit без шейдера»:
InternalErrorShader + сохранённая `_BaseColorMap` → Baked Lit по сохранённым HDRP-свойствам (`_BaseColorMap` с тайлингом,
`_BaseColor`, alpha cutoff, двусторонность; `_EmissiveColor` не чёрный → Simple Lit). Такой материал переводится и без
рендереров в открытых сценах (хуже розового не станет); откат пропускается — исходного шейдера в проекте нет, его GUID в
userData `missingShaderGuid`. Правило отдаёт розовые фиксеру (`WithFixTarget`, группа `shader:(ошибка)`).

Переведены 7 материалов паков RuralTown/PostApocalypticTown (MI_Emissive, MI_TopLight, MI_ElectricPole, MI_Lamp,
MI_LampPost, MI_Lamp_02, No Name). В Lobby они не рисуются: назначены только в FBX (externalObjects), префабы ставят свои
URP-материалы; аудит сцены видит их через `GetDependencies` FBX — инвентарь сцены шире, чем реально рисуется.

`material.emission-off` (QPM-SHADER-006): `EmissiveIsBlack` с белым цветом без `_EmissionMap` больше не находка — остаток
конвертации HDRP→URP (широкий вариант «без карты» снимал бы WarmLampDiffuser и OwnerIndicator с подобранным цветом). Повод — M_Glass_Inst (стекло лампы «Light Off»): белый `_EmissionColor` остался от конвертации
HDRP→URP, в HDRP-источнике эмиссия чёрная; EmissionOffFixer сделал бы стекло светящимся (кадры до/после показаны
пользователем, решение — стекло не трогать). Дверная лампа с картой эмиссии ловится по-прежнему.
