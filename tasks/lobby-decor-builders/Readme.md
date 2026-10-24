# Сцена Lobby — источник правды; инструменты — общие, полезное — в проверки

2026-10-24. Задача создана командой `task-new`, ветка `task/lobby-decor-builders`, worktree `F:/CodexWorktrees/lobby-decor-builders`.
Этапы и области записи — в [plan.json](plan.json).

## Цель

Исходно: устаревшие сборщики LobbyDecoration не откатывают принятый вид арены (lobby-arena-cleanup, 93a5f0b6d);
`ShootingRangePhotoCapture.IdlePhoto` кеширует отказ.

Решения пользователя 2026-10-24 расширили цель:
- сцена правится вручную — сборщики для неё не нужны, удалить;
- всё полезное — в проверки (тесты), инструменты — универсальные для всех карт, не только для Lobby;
- общие правила света сторожить сначала предупреждениями, но так, чтобы они попадали в отчёты агентов;
- процедурное небо: A/B на Quest 3 2026-10-20 — −0,05 мс → небо оставить, правило `scene.procedural-skybox` удалить;
- LOD вышек (.95/.85/.75/.65) — из оверрайдов сцены в префаб `Watchtower_Lit`;
- неиспользуемые материалы `LobbyNightFloodFace`, `LobbyNightWindowGlow` — удалить.

## Что сделано

- Удалено: все сборщики `Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration/` (декор, свет, бюджет Quest, UV2, съёмка),
  `Performance/LobbyGpuSceneTweaks`, `FieldCamp.prefab`, `LobbyZoneLayoutBinding`, `LobbyDetailTuner`, два материала.
  Сгенерированные ассеты, которые использует сцена (меш пола `LobbyTentFloor.asset`, UV2 в импортёрах), остаются.
- Аудитор (`Performance/AssetAudit`): правила `lighting.realtime-light`, `lighting.scene-not-baked`, `lighting.dynamic-in-gi`,
  `mesh.lightmapped-without-uv2`, `lighting.zone-without-probes`; фиксеры `RealtimeLightBakeFixer`, `DynamicObjectGiFixer`,
  `LightmapUv2Fixer` (своя копия меша в `<сцена>/LightmapMeshes/`, QPM-LIGHT-003). Цели — по компонентам, без путей карты.
- heavy (`Tools/e2e/heavy/editmode.py`): Inconclusive с маркером `[Warn:<область>]` — предупреждение в `warnings` и заметках
  `verdict.json`, вердикт не роняет (в UTF 1.6 / NUnit 3.5 нет `Assert.Warn`).
- `Docs/quest-performance-manifest.md`: QPM-LIGHT-005 (сцена — источник правды), -006, -009 (новое), QPM-SKY-001, статус и сопоставление.
- Сопровождающему индексов (inbox, agent-coordination-protocol): `Docs/README.md` ~419 `ShootingRangeDisplay`, ~424 `LobbyRangeLayoutBuilder`.

## Дальше

1. Unity (worker): компиляция, прогон новых правил по картам Build Settings, перенос LOD вышек в `Watchtower_Lit`.
2. Приёмка пользователем (находки правил, вид Lobby, фиксеры) → тесты: аудит света всех карт (предупреждения),
   тесты правил/фиксеров, heavy `warnings`; устаревшее ожидание `scene.procedural-skybox` в `AssetAuditTests.cs:45`.
3. Журналы зон, ревью level-design, performance, launch-infra (+ weapon-system: IdlePhoto), heavy, merge-request.

Инструменты задачи: `tools/unity_call.py` — вызов Unity MCP через прокси этого worktree (в worker своей аренды).
Разбор инструментов света: `reports/lighting-tools-analysis.md` (вне Git).
