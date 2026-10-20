# asset-audit — подробности

Обновлено: 2026-10-20.

## Как добавить правило
1. Класс в `Assets/Editor/VR_Battlegrounds/Performance/AssetAudit/Rules/`, наследник `AuditRule`, конструктор без параметров.
2. Реализовать нужные интерфейсы: `IMaterialAuditCheck`, `ITextureAuditCheck`, `IShaderAuditCheck`,
   `IGameObjectAuditCheck`, `ISceneAuditCheck`, `IAuditFinishCheck` (итог по накопленному).
3. Заполнить `Id` (`область.суть`), `Title`, `DefaultSeverity`, `WhyBadOnQuest`, `Evidence` (замер с условиями;
   общие замеры — константы `AuditEvidence`), `FixHint`; пороги — публичные поля с `[AuditThreshold]`.
4. Находка — `context.Report(this, asset, message, severity)`, дальше `WithItem/WithGroup/WithMetric/WithFix`.
   Одинаковые находки сливаются по `WithGroup` (по умолчанию ассет + сообщение).
Ядро не правится: правила находит `TypeCache`. Переопределение порогов — `AuditRequest.ThresholdOverrides["id.Поле"]`
или `-auditThreshold id.Поле=значение` в batchmode.

## Правила (первый набор, по находкам Lobby 2026-10-20)
| id | что ловит | серьёзность |
|---|---|---|
| `shader.non-mobile` | материал не на URP Simple Lit/Baked Lit/Unlit (группа — шейдер); нет шейдера — критично | W / C |
| `shadergraph.heavy-sampling` | разбор .shadergraph: выборки с сабграфами, трипланар/world-aligned, Branch по свойству | W / C |
| `material.hdrp-legacy` | `_DISABLE_SSR_TRANSPARENT`, `RayTracingPrepass` и др.; потеря HDRP alpha cutoff | I / W |
| `material.texture-count` | > 3 текстур (критично > 6), только свойства текущего шейдера | W / C |
| `texture.no-mipmaps` | текстура мира без mip | W |
| `texture.android-format` | не ASTC на Android; несжатая > 256 px — критично | W / C |
| `texture.size` | ≥ 2048 px в сборке Android — инфо, ≥ 4096 — предупреждение | I / W |
| `lod.far-not-reduced` | последний LOD > 1000 треуг. (критично > 5000); доля > 20 % — инфо | I / W / C |
| `mesh.no-lod` | MeshRenderer вне LODGroup > 5000 треуг. (критично > 20000) | W / C |
| `lighting.large-without-lightmap` | крупная поверхность (грань AABB ≥ 24 м²) без лайтмапы; причины: нет UV2, не ContributeGI, Receive GI = пробы; alpha-test и LOD>0 пропускаются | I / W |
| `reflection.planar` | UxrPlanarReflectionUrp: текстура > 512 (≥ 1024 — критично), дальность 0/> 10 м, все слои | W / C |
| `foliage.alpha-test` | сцена: alpha-test рендереры, треугольники от последних LOD до LOD0; пак: число материалов | I / W / C |
| `scene.procedural-skybox` | скайбокс на Skybox/Procedural (из RenderSettings файла сцены) | W |

## Сверка с находками scene-detail (прогон 1, worker, ticket 902)
Пак MilitaryCamp: 708 ассетов, 2,7 с; Lobby.unity: 2366 ассетов, 8,6 с; ошибок правил 0.
- S_VPWorldAligned — критично, трипланар, ≈24 выборки (замер: пол 11,8 %) — совпало.
- S_BasicTextured — критично, ≈14 выборок, Branch ×8 по свойству «UseWorldAligned?» — объясняет столл текстур скал.
- SM_WatchTower_01 — цепочка 142,4k / 71,2k / 35,6k / 17,8k / 8,9k: LOD3 = 17,8k как в замере. В прогоне 1 —
  только «предупреждение» (порог 10k на последний LOD4) → пороги снижены до 1000/5000.
- Пол `nalchik_tolst.fbx#floor` 294 м²: нет UV2, Receive GI = Light Probes — совпало (проба далеко от лампы).
- Зеркало MirrorGlass: 2 × 768², дальность 16 м, все слои — совпало (≈0,64 мс).
- Skybox/Procedural (`LobbyNightSky.mat`) — совпало (8,3 %).
- Ели SM_SilverFir_M_01 ×110 — alpha-test. Шум в `lighting.large-without-lightmap` (ели, LOD1–4) → правило
  пропускает alpha-test и LOD>0.
- Новое, не из замера: ORM-текстуры MilitaryCamp на Android несжаты (RGB24/RGBA32, 1024 px);
  3 материала без шейдера (розовые) в каталоге PostApocalypticTown/RuralTown; 2 декали потеряли HDRP alpha cutoff.

## Побочный эффект открытия сцены (2026-10-20)
После аудита Lobby в worker остался несохранённый `HauntedFarmHouse/.../MI_MiscProps_01.mat` (модель кружки
FieldCamp; на диске blob одинаковый во всех базах). При begin грязных ассетов не было — значит, пометило открытие
сцены (скрипты [ExecuteAlways]/OnValidate) или загрузка. `finish` отказал («чужой dirty-объект»), аренда ушла в
RECOVERY_REQUIRED, закрыта штатным `recover --force` (исход известен, результат на worker не нужен; .meta не получены).
Решение: раннер снимает список грязных ассетов до прогона, после фаз сообщает новые в `errors` и выгружает их без записи
(`AuditRequest.RevertNewlyDirtied`).

Прогон 2 (ticket 911, вход ba3f8c1b, finish штатный, .meta получены, result 1fadd7e5): грязными становятся 8 материалов
уже в фазе «проверки ассетов» (MI_MiscProps_01, два `No Name.mat`, встроенные материалы FBX `nalchik_tolst`,
`Hands_Automatic_Rifle01/03`) — значит, их помечает загрузка/чтение материала (вероятно, исправление keywords при
загрузке), а не скрипты сцены. Защита выгрузила их, finish прошёл. Промах: под выгрузку попал внутренний объект
редактора `SceneVisibilityState` (persistent, но не ассет) — фильтр исправлен на `AssetDatabase.Contains`
(после прогона 2, в Unity ещё не проверено). Worker после finish возвращён к базе брокером.

Итог прогона 2 (пороги после калибровки): MilitaryCamp — critical 25, warning 37, info 69; Lobby — critical 15,
warning 140, info 346. Шум: `texture.size` (271 инфо в Lobby), `lod.far-not-reduced` инфо по доле (63) — фильтруются
в окне по серьёзности.
