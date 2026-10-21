# scene-detail-followups: фиксеры материалов аудитора, правило эмиссии, хвосты ревью, 2026-10-22

Один писатель перевода материалов окружения (QPM-SHADER-007): `AssetAudit/Fixers/BakedLitMaterialFixer` (правило
`shader.non-mobile`) заменил `LobbyGpuMaterialTier` и `BoxUv/BakedLitMaterialConverter`. Аудит-путь переводит только .mat
в `EnvironmentRoots`, не у другого писателя (`OwnedElsewhere`), рисуемые в открытых сценах только статичными объектами и не
используемые другими сценами/префабами; box-UV зовёт `PlanWorldAligned` (тайлинг = метры на UV / _MainTile). Данные отката —
прежний формат (маркер `lobby-gpu-tier-v1`). В ядре `AuditFinding.FixTargets`: все ассеты группы без лимита 25 примеров.
Меню: `Asset Audit/Fix Mobile Shaders in Active Scene`, `Revert Mobile Shader Fix in Selected Folders`.

Правило `material.emission-off` (QPM-SHADER-006) и `EmissionOffFixer`: дверная лампа Lobby (EmissiveIsBlack → URP снял
_EMISSION → ярус принял за неэмиссивную → Baked Lit) снова Simple Lit без бликов, RealtimeEmissive; кадры до/после приняты
пользователем. Порядок пользователя 2026-10-22 записан в манифест: гипотеза → проверка → правило ловит → исправление фиксером.

Хвосты ревью: PerfPlanarFloor вне Resources (в Development-сборку — `PerfDiagnosticShaderBuildProcessor`, в Prod нет;
сборкой не проверено), Tools/Perf/README (ссылки, раздел Meta RenderDoc), заметка о буфере PerfDiagnosticRecorder
(~7 МиБ по коду, не замер), меню Lobby GPU под Tools. Держатель шейдера — в каждой сцене Development-сборки (Shader.Find
после выгрузки Offline); эмиссия после фиксеров — RealtimeEmissive (BakedEmissive дал бы вклад в лайтмапы).

Попутно: `material.emission-off` ловит ещё M_Glass_Inst (Lobby), OwnerIndicator, DustPuffParticle_NoSoft и сэмплы UXR;
в Lobby 3 розовых материала (MI_Emissive, MI_TopLight, «No Name»).
