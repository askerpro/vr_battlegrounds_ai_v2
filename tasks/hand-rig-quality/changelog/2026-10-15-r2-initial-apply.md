# R2 initial Apply+Fit 704 выполнен, снимок требует исправления

Input `16ca6506fff6a5794ed858444505c4ee4a4deadc`, base `d449f1983`.
До Run все 2400 Measure-входов существовали и совпали по SHA256.
GripExtraction status=ok: 17 точек, 34 руки, 34 Fit reports/review sheets.
2522 disk-state paths проверены, 501 исходный dirty object сохранён по instance/path/flags.
Фактически изменены только 9 разрешённых weapon prefabs, байты 17 поз прежние.
unexpected/GUID/identity/foreignDirtyChanges пусты, все 9 NetworkIdentity._assetId каноничны.

Исходный validator `passed=false`: единственный отказ — 97 nonGripChanges.
Обнаружены две ошибки проверяющего снимка: он исключал `Grabs/<source>/...`
вместо фактического `<source>/...` и читал поля мимо оболочки `MonoBehaviour`.
Отдельный corrected proof подтвердил все97/97, unexplainedCount=0; principal повторил analyze.py.
9 root snapshots: только51 разрешённая ссылка;85 новых target entries;3 default scaffolds Browning.
Parsed YAML9:142 новых документа (54GO/54Transform/34Snap),25 изменённых
(9targetgrabbable+16append-onlychildlists), removed/unexplained=0.
Исходный JSON с passed=false не переписан. Correctedreview SHA256
`17c31b58c6696ea13e1c1a721cb4591aaf9d5718da6027db56d98eda38f37fc7`.

Fit: 19 gross, 7 notable, 8 ok; palm 32 ok/2 notable. Все 34 analysis_incomplete
из-за приблизительности/недоступных частей static capture, при Error=null и готовых hints.
Это не contact/runtime/Quest acceptance. Для второго прохода отобраны14рук/24curlв7позах:
толькоRecommended,finite,converged,!DepthLimited и без ухудшения predicted errors/depth.
12рук manual/preview,8unchanged; normalmapempty (обе-21,7мм за пределом15мм).
9листов проверены экспертом, Uzi/SniperSupport также principal; коррекции пока не применены.

Cleanup/nativeidle PASS, preview leftovers=0. 704 DONE/received:
R`13ef52db5fa48c6bc58525cbd6332c38e4f37c0c`, broker unexpected пуст.
8 полных JSON заархивированы; validation SHA256
`cc2963b7f526922b33928e13c29ea5ec2eabbf3b185447afab6bccd955ad7a01`.
MCP timeout30s сохранён; C# продолжил и завершился, повторного Apply не было.

Доказательства: `reports/extraction-run/r2-apply-20261015T061148-e7c8adc2b02f4779bcf6c52224d43808/`,
`reports/broker-finish-704.json`, `reports/native-cleanup-704.json`.
