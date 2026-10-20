# asset-audit: аудитор ассетов под Quest, 2026-10-20

Новый Editor-инструмент `Assets/Editor/VR_Battlegrounds/Performance/AssetAudit/` (меню
`Tools/VR Battlegrounds/Performance/Asset Audit/…`, MCP `AssetAuditRunner.Run*`, batchmode
`AssetAuditMenu.RunFromCommandLine`). Только читает ассеты пака, выборки или сцены и даёт
JSON + окно, серьёзные находки сверху. Правило = класс `AuditRule` в `Rules/` с `WhyBadOnQuest`,
`Evidence` (замер с условиями), подсказкой и порогами `[AuditThreshold]`; ядро не правится.

Первые 13 правил — по per-draw замеру Lobby на Quest 3 (scene-detail, 2026-10-20). Аудитор сам
нашёл все 8 узких мест замера (S_VPWorldAligned 24 выборки, S_BasicTextured 14 + Branch ×8,
LOD-цепочка башни 142k→17,8k→8,9k, пол на пробах без UV2, зеркало 768²/16 м, ели alpha-test,
Skybox/Procedural, URP Lit) и новые: несжатые ORM на Android, розовые материалы, LOD бронемашины.

Инварианты: пороги — ориентиры, не бюджеты; прирост доказывает только A/B на шлеме. Новый замер →
константа в `AuditEvidence` + порог правила. Загрузка части материалов помечает их несохранёнными:
раннер выгружает такие ассеты без записи, иначе `finish` брокера отказывает. Исправления —
отдельными «фиксерами» (ASTC, Baked Lit, LOD), не в аудиторе.
