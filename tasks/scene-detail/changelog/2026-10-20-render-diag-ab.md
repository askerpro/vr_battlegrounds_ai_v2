# 2026-10-20 — normal A/B render_diag на Quest3 (Lobby center-north)

Сборка Test da78253b (checkpoint поверх базы 43332888; e2e + quest -GpuSceneIdentity), RunSpec из
reports/q3rd-inputs-b-20261020 (manifests профилей + SHA256SUMS, APK a639caee…). Fingerprint c7571afb… (= прежний).
Wi-Fi adb 192.168.1.234:5555, оркестратор tools/run_quest_series.py: reconnect+WAKEUP перед прогоном,
Run-QuestE2E -KeepAwake внутри серии, KEYCODE_SLEEP в конце; по решению пользователя
debug.oculus.guardian_pause=1 на серию (до: "", после: "" — restored, reports/q3rd-series-20261020/guardian-pause.json).
14/14 прогонов GREEN, readyCount 1 каждый, отказов нет, render_diag restored в каждом.
Подмена затрагивает все 3 материала на S_VPWorldAligned = 662 слота материалов рендереров (не один пол).

AppGPUTime p50 (normal, detailed OFF, фаза 20с), пары с чередованием порядка:
| пара | вариант | A, мс | B, мс | Δ, мс |
| aa1/aa2 | none (A/A) | 10.787 | 10.798 | +0.011 |
| n3/fb1, n4/fb2 | floor-basic (S_BasicTextured) | 10.805 / 10.792 | 10.086 / 10.093 | −0.718 / −0.698 |
| n5/fu1, n6/fu2 | floor-unlit (URP Unlit) | 10.807 / 10.801 | 8.937 / 8.950 | −1.870 / −1.851 |
| n7/sk1, n8/sk2 | skybox-color | 10.811 / 10.808 | 10.762 / 10.764 | −0.049 / −0.044 |
Сводка: reports/q3rd-series-20261020/summary.json (tools/summarize_render_diag_series.py), compare/*.
Вывод: шейдер S_VPWorldAligned — ~1,86 мс кадра (≈17%); простая замена S_BasicTextured даёт ~0,7 мс,
почти бесплатный Unlit — ~1,86 мс. Procedural-скайбокс в реальном кадре ~0,05 мс (8% replay-тактов per-draw
было артефактом изолированного replay). Baseline этой сборки 10,8 мс (выше 9,93 серии 19Oct; новая база dev и
позднее смещение — причина не установлена). Цель 7 мс: после пола остаётся ~1,9 мс.
Ограничения: API observations; floor-unlit — раскладка по UV меша, визуально не проверено; skybox-color меняет и
отражение зеркала. Это диагностика; правка ассетов — после визуальной проверки пользователем.

## Визуальная проверка вариантов пола

Quest (реальный трекинг, -HeadPose off): floor-basic первой версии — пол чёрный (дефолт графа S_BasicTextured
_Metallic=1; исправлено: _Metallic=0, ORMH=white); floor-unlit (URP Unlit по UV меша) — размытое пятно низкого
разрешения: UV пола рассчитана на мировую проекцию → визуально непригоден (замер −1,86 мс остаётся оценкой
нижней границы). Обрывы adb во время показа совпали со сборками соседней задачи (adb-сервер перезапускался).
Добавлен диагностический шейдер Resources/PerfDiagnostics/PerfPlanarFloor (одна выборка MainBC, uv = worldXZ/_MainTile,
как SubG_WorldAlignedTexture без тройной проекции; _PLANAR_LIT — + лайтмапа/SH) и варианты floor-planar-unlit/-lit.
Checkpoint 33406814 (parent 137517ec), Windows e2e Build-Game exit0, шейдер скомпилирован и в сборке.
Windows visual (discovery, tools/run_windows_visual.ps1 + capture_client_window.ps1): скриншоты
reports/win-visual-20261020/{01-none,02-floor-basic,03-floor-planar-unlit,04-floor-planar-lit}.png. Вердикты RED
(отказ сценария/закрытие окна) — для визуального показа не значимы; Windows frame time на скриншотах — не замер.
Наблюдение по скриншотам: basic и planar-lit близки к исходному; planar-unlit заметно светлее (нет запечённого света).
Ждём выбора пользователя; затем — APK и normal A/B выбранного варианта на Quest.

## Освещение пола (read-only разбор level-design-эксперта, статически)

- `Environment/ArmoryRangeZone/MapShell/floor` (WornGround.mat, плоскость nalchik_tolst.fbx 948×775) — m_ReceiveGI: 2
  (light probes, Blend), без UV2 (generateSecondaryUV: 0) → не в лайтмапе; SH берётся в центре bounds (~начало координат),
  ≈8,1 м от InteriorWarmLight (радиус 6,5) → пол получает почти только ночной ambient. Плюс _MainTint 0.6/0.6/0.55,
  saturation 0.7, _MainRoughnessIntensity 0.9 (это smoothness).
- 662 подменённых слота — в основном M_Concrete_01 (стены/сваи/балки MilitaryCamp, в лайтмапе) + WornGround + M_Tiles_ObjectSpace:
  эффект A/B относится к шейдеру S_VPWorldAligned на всей сцене, не только к полу.
- PerfPlanarFloor _PLANAR_LIT для пола без лайтмапы берёт ту же SH-пробу (тёмный); для бетона — лайтмапу.
- Проверки в Unity (не выполнялись): Anchor Override пола на пробу в палатке / Light Probe Usage Off; тинт 1.
  Фиксы: Anchor Override + тинт ~0.85–1 (дешевле) или отдельный меш пола палатки с UV2 и ReceiveGI=Lightmaps (ребейк).

## Серия B: normal A/B исправленных вариантов (Quest3, сборка 33406814)

APK 33406814 (первая сборка упала: Gradle «Java heap space» в compressDebugAssets; остатки упавшей сборки сохранены
в build-root-save/test-android-20261020-c 0f3cf827e; повтор exit0). 14/14 GREEN, readyCount 1, restored, Guardian pause
"" → 1 → "" (restored). AppGPUTime p50 (normal, фаза 20с), reports/q3rd-series-b-20261020/summary.json:
| пара | вариант | A, мс | B, мс | Δ, мс |
| aa1/aa2 | none | 10.807 | 10.801 | −0.006 |
| n3/fb1, n4/fb2 | floor-basic (исправлен, metallic 0) | 10.808 / 10.804 | 10.093 / 10.091 | −0.715 / −0.713 |
| n5/pu1, n6/pu2 | floor-planar-unlit | 10.821 / 10.840 | 8.944 / 8.917 | −1.878 / −1.924 |
| n7/pl1, n8/pl2 | floor-planar-lit | 10.803 / 10.803 | 8.934 / 8.937 | −1.868 / −1.866 |
Вывод: одна выборка с проекцией по миру + запечённый свет стоит столько же, сколько unlit: ~−1,87 мс (−17%) кадра
при замене S_VPWorldAligned (пол WornGround + бетон M_Concrete_01 + M_Tiles_ObjectSpace) в виде center-north.
basic −0,71 мс (метал не влиял на стоимость, как ожидалось). Визуально (Windows-скриншоты) planar-lit близок к
исходному; пол исходно недосвечен (light probe, тинт 0.6) — отдельная правка освещения.

## Аудит UV под стандартный Baked Lit (ticket 888, только чтение)

Lease 888 на базе ade7d06e: открыта Lobby без сохранения, отчёт reports/uv-audit-20261020/uv-audit.txt, finish
accepted, paths пусто, receive ок. Метрика m_per_uv = sqrt(площадь мира / площадь UV0).
- floor (WornGround): UV0 [0.13..0.88]x[0..1], без UV2, m_per_uv ≈ 39.6 — стандартный Baked Lit пригоден с повтором
  ≈ 39.6/_MainTile (первый floor-unlit был размыт из-за повтора = _MainTile, ошибка диагностики).
- M_Concrete_01 / M_Tiles_ObjectSpace (~40 мешей MilitaryCamp, сотни экземпляров): m_per_uv от 0.49 до 15, у многих
  UV0 — островок атласа (wall_4x4: [0.38..0.62]x[0..0.25]); пак рассчитан на мировую проекцию → единый повтор
  стандартного шейдера даст несогласованный масштаб. Нужен свой дешёвый шейдер с проекцией по основной оси нормали
  (1 выборка + лайтмапа) или пере-развёртка мешей.

## Box-UV + URP Baked Lit вместо S_VPWorldAligned (решение пользователя: скрипт, без своих шейдеров)

Пользователь выбрал AssetPostprocessor (разовый переимпорт всех моделей в каждой Library) вместо сгенерированных
мешей. Код: Assets/Editor/VR_Battlegrounds/Performance/BoxUv/ (Projector, ModelPostprocessor с custom dependency
`VrbBoxUv/<модель>`, TargetScanner, BakedLitMaterialConverter, конфиг BoxUvTargets.json — пишет только сканер).
Ticket 899 (input 01a92150, база 911f93b3): компиляция чистая; Scan — 53 сабмеша в 23 моделях, пропущен только
встроенный Cube (UpperWindowWall__ProceduralFiller), unverifiedScenes пусто; Apply — 3 материала на Baked Lit
(M_Concrete_01 тайлинг 0,286, тинт 1,5; M_Tiles_ObjectSpace 1; WornGround 19,795, тинт 0,6/0,6/0,55);
проверка плотности: 53/53 = 1,0 м на единицу UV. finish accepted 162ab935, receive ок. Кадры редактора —
reports/boxuv-apply-20261020/*.png (временная камера, не вид center-north). Ошибки консоли NaN localRotation
у Link/Cartridge — от загрузки префабов ленты при сканировании, к BoxUv не относятся (не разбирались).
Коммит 3cc74cf59 (технический). Дальше: Windows e2e для показа, затем APK и normal A/B на Quest.
Слои R/G по vertex paint, нормали, краевой шум и насыщенность не переносятся (это и есть экономия).

## Манифест производительности Quest (задача quest-perf-manifest, влита 7d0b86b15)

`Docs/quest-performance-manifest.md` в origin/dev ссылается на находки этой задачи. При вливании scene-detail
в том же изменении: дописать цены финального A/B (box-UV + Baked Lit, 91 материал, свет шатра) в QPM-SHADER-001/
LIGHT-002/REFL-001, перевести BoxUv, LobbyTentFloorLightmap, render_diag и meta_renderdoc_quest.py в «Статус
инструментов» как влитые, поправить источник чисел LOD башни в QPM-LOD-001 (аудит в Unity субагента, тикеты
896/905, а не аудит UV 888). Ревью performance и level-design.

## Итоговый normal A/B: база a0432b240 против финала 8500240ab (Quest 3, 2026-10-20)

Визуальная проверка финального APK в шлеме (реальный трекинг, Lobby) — пользователь: «да, стало гораздо лучше».
Серия tools/run_ab_two_builds.py: блоки A×3 | B×4 | A×3 с подменой Build/e2e и Build/Quest по sha (сервер и APK каждой
ревизии собраны Build-Game -GpuSceneIdentity), Guardian pause "" → 1 → "" (restored), 10/10 GREEN.
Условия: Quest 3 (совместимость Quest 2), Test, FFR 0,75, CPU 4 / GPU 2, center-north, фаза 20 с, AppGPUTime p50.
| сборка | прогоны | p50, мс |
| A a0432b240 | 6 | 10,814 10,801 10,817 / 10,796 10,801 10,815 → медиана 10,808 |
| B 8500240ab | 4 | 4,235 4,245 4,267 4,670 → медиана 4,256 |
Δ ≈ −6,55 мс (−61%) в виде center-north (до правок — худший вид тура 2026-10-14). Бюджет Lobby задан по худшему
из 8 видов: выполнен ли он, покажет тур по 8 точкам (не сделан). Дрейфа A до/после B нет.
Измеренные сборки — ревизии до rebase (SHA меняются при каждом rebase); в истории им соответствуют
коммиты «WIP(scene-detail): GPU-диагностика Quest — per-draw, render_diag A/B…» и «fix(scene-detail): LobbyGpuMaterialTier — Simple Lit без бликов…». После замера
в коде изменились BoxUvProjector (атрибуты дубликатов, версия постпроцессора 2) и эмиссия LobbyNightLampLong
(RealtimeEmissive) — по сути без цены GPU, но финальный SHA не тот, что мерили.
Условия A и B совпадают, кроме qualityHash: отличаются только пороги LOD групп 688/689 (вышки на LOD4 — наша правка).
Между базами dev (911f93b37..646170c47) Lobby, ProjectSettings, URP и Packages не менялись → разница — правки задачи:
box-UV + Baked Lit (пол, бетон, плитка), 91 материал декора на Baked Lit, вышки LOD4, зеркало 512²/8 м, свет шатра
(перезапекание, пол в лайтмапе, Unlit/Simple Lit плафоны). Вклад по отдельности не мерился.
Сводка: reports/q3final-ab-20261020/summary.json (tools/summarize_ab_two_builds.py).
Попутно: сборка 8500240ab пересчитала ContentFingerprint Lobby в MapRuntimeCatalog — закоммичен в 91b9f1046.

## Финализация 21.10: тесты и heavy

Тесты (ticket 973, чистая смена ветки на worker, код 2986a88c5): BoxUvProjectorTests, LobbyQuestRenderStateTests,
MapRunPreflightTests, MapCatalogIntegrityTests — 21/21. До этого тесты нашли и закрыли три дефекта: атрибуты
дубликатов box-UV (cfa…), устаревшие артефакты моделей из кэша (DependsOnSourceAsset + BoxUvModelPostprocessor v2),
плафон без _EMISSION (URP включает его только при AnyEmissive → RealtimeEmissive).
Heavy editor/default --editmode also (тот же ticket): код 1. scripted-input-smoke и scripted-wall-purchase GREEN;
scripted-combat-hit FAIL «нёс оружие не ровно: ровность 0,488 < 0,9»; EditMode 2885, красных 19 (новых 2:
GameTagsTests.Prefabs_TagsMatchRules, HandsPackWeaponTests(Scar)). Те же три провала — в heavy задачи e2e-combat на
той же dev (её reports/heavy-all-1, 21.10 02:25); два новых красных e2e-combat относит к пересборке стволов в dev
(200236fe7, 331048228). Ветка scene-detail префабы оружия и e2e-сценарии боя не меняет → регрессия dev, не этой
задачи. Отчёт: reports/heavy-20261021/verdict.json.
