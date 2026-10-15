# Приёмка нормализации сетевого assetId

2026-10-15 пользователь поручил: «тогда вливаем и переходим к run extraction?».
Принят узкий этап grip-save-validation: GripRecordWriter после SaveAsPrefabAsset вызывает
штатный NetworkAssetIdNormalizer и проверяет канонический assetId на диске, затем существующий identity guard.

Unity647: compiled witness и Apply принятого Viper PASS,3 asset bytes неизменны.
Свежие EditMode683:56/57; идентификаторы4/4 и цепочки поз13/13 PASS.
FAIL старого Cyborg (пустой LocalDisabledGameObjects) передан avatar-ik,event7525; не относится к изменению сохранения.
Ревью diff origin/dev...HEAD не выявило блокеров. Перенесён ошибочно стоявший summary к методу Place;
функциональный код после runtime-проверки не изменён. Обобщение через ReadOnDisk отложено вместе с общим helper.

Приёмка не принимает будущие R2/R3 ассеты. Следующий этап grip-extraction-run:
сначала текущая матрица и Measure, затем согласованные Apply и пользовательская проверка.
Широкие zone-ack требуют отдельного согласования; узкие Viper ACK не переиспользуются.
