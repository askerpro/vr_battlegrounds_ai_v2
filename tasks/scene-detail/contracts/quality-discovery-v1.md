# Явное получение actual quality fingerprint

18.10.2026. Обнаружен bootstrap gap RunSpec v1: для ready уже требуется actual fingerprint,
но его впервые можно получить только из работающей сцены. Подставлять0/фиктивный hash
или выдавать requested за actual запрещено. Совместимое дополнение к quality object:

`discoveryOnly=true`, `requestedFingerprint=null`, прежний непустой profileId.
Только mode=live/capture.kind=none. При отсутствии discoveryOnly прежняя валидация SHA256
и readiness неизменны. Discovery собирает observations/conditions/actual snapshot, но
никогда не публикует workload.ready и не используется как принятый GPU/A/B замер.
Следующий отдельный прогон получает фактический SHA256 из quality-canonical.json.

Producer: gpu_analyze.py prepare-windows-run с --discovery либо --fingerprint.
Он только формирует immutable JSON входы/копии manifest/views/phase-plan и launch-args;
запуск остаётся у существующего Run-E2E.ps1. Для author LOD pair используется один build,
по8прежних Lobby views, map-only/puppets0/clutter0, явный scene_detail lod-a/lod-b.

Реализация: producer подготовлен во время ожидания827; C# и PS reader согласованно
расширены после получения meta/проверки frozen9cc517a для нового Windows build input.
Сам9cc517a ещё не принимает discoveryOnly; новый diff проверит сборка. Это не
разрешение снимать прежние guards или считать discovery GPU-измерением.
