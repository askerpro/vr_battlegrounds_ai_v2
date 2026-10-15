# Прекратить повторную перезапись _assetId при редакторном сохранении префабов

2026-10-15. Владелец network, worktree F:/CodexWorktrees/network/Vr_Battlegrounds_ai.
Статус: **принят пользователем; код и регрессии проверены, выпускаются одним пакетом в origin/dev**.
Проверенная база018e63d4fcc8c97355165474f0e080abd34aa79a; публикационная база98eed6c1c меняет только AGENTS.md.
Прежний AvatarRoot-пакет уже в origin/dev1e945a996.
Зарезервирован Mirror patch5. Пользовательская приёмка получена 2026-10-15 после пакета748.

Исходная находка: OnValidate → NetworkIdentity.SetupIDs в preview-сцене LoadPrefabContents может присвоить
_assetId=0; SaveAsPrefabAsset сохраняет его, обычный импорт/OnValidate затем снова выставляет канон.
В origin/dev уже есть NetworkAssetIdNormalizer (9757c9195), но это нормализация диска, а не устранение этой причины.
Связанные owner-сообщения: avatar-grip6384/6429, weapon-system6378; прежнее обещание network6589/6647.

Узкое решение в NetworkIdentity.SetupIDs: после persistent prefab и PrefabStage выделить preview scene.
У transient contents сохранять загруженный _assetId, sceneId держать0; GUID transient-объекта не выводить
из source варианта или scene.path. Канонический ID по-прежнему назначают persistent prefab и конкретный PrefabStage.
Нормализатор и общий spawn/postprocess путь не меняются. Подробности — [Details.md](Details.md).

Unity6000.4.1f1, одна и та же task-local probe вне Assets/Prefabs: два цикла load→validate→save→reimport,
raw SerializedObject, YAML только читается, обычный prefab и variant с собственным каноническим GUID.
Настоящие scene objects/instances проверены в собственной диагностической сцене; весь fixture удалён.

- Исходный SDK, ticket746: **RED20**, 41 строка отчёта, bodyCompleted=true, cleanupPassed=true.
  Regular ID обнуляется, variant получает ID базы; в обоих contents появляется ложный sceneId.
- Mirror patch5, ticket748: **GREEN**, 0 нарушений, 41 строка отчёта, оба цикла и cleanup прошли.
  Contents сохраняют собственный assetId и sceneId0; настоящие scene instances сохраняют sceneId!=0.
- AndroidCompileGate: **PASS, 0 ошибок**. Существующий NetworkAssetIdOnDiskTests: **1/1 PASS**.
- Finish748 завершён; result e369d6ef09c15f7692c0de017ac65735a0a2c919, paths[], unexpected[].
  Fixed technical input e3353fc4441b413552aaf620bd3cdd071c443c2b. Это не принятый коммит.

Полные отчёты: reports/baseline-746.json, fixed-748.json, android-fixed-748.json, editmode-fixed-748.json
(ignored, вне Git). Подробности, SHA и ранние отказы диагностического пакета — в Details.md.

После приёмки добавлены две регрессии regular/variant и обновлён исторический комментарий disk test.
Unity750: **3/3 PASS, failed0/skipped0**; SDK-код тот же, что в748. Cleanup включает проверку dirty исходной сцены.
Finish750/receive: result428f366f7f01c0c4d0b80a25a3c1401555a45c5e, paths[], unexpected[].
Полный reports/editmode-regression-750.json. Код, тесты и этот документ входят в один принятый коммит;
публикация — штатный fast-forward и request-base по FIFO. PrefabStage автоматически не открывался,
пользователь принял заявленный объём Unity-доказательств.
Граница: уже испорченный raw0/ID базы сам preview guard не ремонтирует; Play/spawn/network/Quest не проверялись.
