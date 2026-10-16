# R2 Apply698 остановлен до записи

Input55025784/base1812dc5bf. Refresh/guard PASS; helper выявил dirty shaderassets до общего SaveAssets.
Known runtime exception до GripExtraction.Run, новых R2 assetwrites нет.
CleanupPASS; DONE/received R07d5ef0d52ea12554b861f89991d30c9095e0ce9,paths/unexpected=[].
[Полная квитанция](../reports/native-r2-apply-698.json), [finish](../reports/broker-finish-698.json).

Корень — два глобальных AssetDatabase.SaveAssets в GripExtraction.Write. Предложен scoped-save:
сохранять только текущие UxrHandPoseAsset,weapon и registry сохраняются существующими writers.
Нельзя очищать чужие dirtyflags или пересохранять loaded weaponprefab (риск UXR IDs).
До решения по boundeddesign productкод не изменён. R2 planned,Measure695 verified иsave merged сохраняются.
