# Патч UltimateXR59: grip preview учитывает масштаб SMR один раз

Запись перенесена из незакоммиченной правки общего SDK-журнала по правилам миграции.
Номер59 зарезервирован в хабе: SDK `UltimateXR`, ключ `static-analyzer-sdk-preview-scale`.
Исторические проверки ниже относятся к старым checkpoint SHA; после rebase нужна новая проверка.

`Assets/ThirdParty/UltimateXR/Editor/Manipulation/HandPoses/UxrPreviewHandGripMesh.cs`,
`ComputePose`: `BakeMesh(true)` компенсирует масштаб renderer перед полным `localToWorld`
и переводом в unit-scale рамку grabber. Прежнее `BakeMesh(false)` повторно учитывало масштаб.
Это уточнение патча45; применение позы, mapping, веса, blend shapes и runtime SDK не меняются.

RED подтверждён штатным Unity TestRunner в worker168: два scaled-rig parity случая
uniform1,7 и nonuniform(0,7;1,4;1,2) с blend shape67% дали max2,0772 м/0,8761 м
против компенсированного runtime BakeMesh. Новые независимые LBS/capture-regressions
scale0,5/1,5 при этом прошли; остальные75/77 и Android PASS.
GREEN worker176: штатный Unity TestRunner77/77, sameThread=true, Android PASS,
finish/receive завершены. Допуски прежних parity-проверок не ослаблены:
вершины0,01 мм, нормали0,0001 векторной нормы. Общий capture Hand Pose Fit и анализатор
HandRigQuality используют компенсированное запекание.

## Внешний симптом и область исправления

В сравнении кистей меш был сдвинут относительно собственных костей;
SDK grip preview менялся при scale. В Unity6000.4.1f1 исправлено сочетание BakeMesh
и полной матрицы в обоих Editor-потребителях. Координаты сверены с независимым LBS,
uniform/nonuniform parity и89 диагностическими тестами на прежней базе.
Изображения Hand Rig Quality0.1/0.2 не годятся для визуальной диагностики.
Каркасы0.5 совмещены по запястью без добавочного сдвига, точки костей рисуются поверх сетки.
Игровые префабы и runtime SDK не изменялись.

[Обзор задачи](../Readme.md), [инструкция](../tool.md),
[история проверок анализатора](2026-10-07-static-analyzer.md).
