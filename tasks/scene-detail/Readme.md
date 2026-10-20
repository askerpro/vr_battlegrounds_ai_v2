# Детальная GPU-диагностика сцены

Обновлено: **19.10.2026**. Текущее продолжение — задача `scene-detail` в
`F:/CodexWorktrees/71af/Vr_Battlegrounds_ai`, ветка `codex/scene-detail-perf-20261018`.

**Текущий статус:** выполняется разрешённый пользователем
[единый план завершения](gpu-diagnostics-completion-plan.md). GPU-исходники перенесены
выборочно из immutable1d85; начат offline анализ реального исторического scene.json.
Основной выход — машиночитаемый JSON/JSONL/CSV для агента; HTML пользователю не нужен.
Текущая база обновлена до dev48d38b9b2, HEAD9daadf810 после rebase; WIP сохранён
checkpointb6139021 и stashffc3386e, затем восстановлен без конфликтов.

**Продолжение19Oct:** Windows Testd5db59ea: Unity/wrapper exit0,0errors/8warnings.
Identity исправлен и подтверждён в клиенте:14802refs/51unresolved; scene SHA совпал
с structural index. Парные RenderDoc A/B GREEN из той же сборки/вида, native-r11
export exit0,195из660 GPU ресурсов имеют asset candidates. Через replay-query агент
читает timing→pass ancestry→bindings→asset candidate→места сцены. Prefab build-copy IDs
сопоставляются отдельно как уникальные fold candidates; коллизии не угадываются.
compare-replay подтвердил farStart25→20 как единственную primary quality разницу:
7geometry groups изменились, submitted indexed elements−38358. Это command metadata,
не доказанный выигрыш GPU frame time. Актуальные joins: reports/windows-identity-joins-A-r4-20261019
и B-r4; сравнение: reports/windows-identity-replay-ab-r3-20261019/comparison.json.
Read-only аудит выявил неверный нулевой множитель у draw без instancing; исправлено,
фактические суммы743694→705336, дельта прежняя. Использованные bytes проверяются по SHA.
Android Testd5db59ea: Unity/wrapper exit0,0errors/134warnings; APK установлен на Quest3.
Первый discovery остановился до workload: неполный dirty evidence и /sdcard alias.
Host-исправления готовы. Пользователь разрешил закрыть Link и продолжить Quest-замеры.
Link закрыт, discoveryA workload/focus/logs GREEN; итог INCONCLUSIVE из-за Windows ADB
MAX_PATH на harvest.11raw files восстановлены в reports/qraw-051853-flat; lifecycle/hash
проверка без замечаний; runner исправлен. Quest Vulkan72Hz, FFR0.75/CPU4/GPU2.

**Quest3 результаты19Oct (detailed OFF, сборка d5db59ea):** серия center-north
A/A + 3 пары A/B с чередованием — все8 GREEN; AppGPUTime p50 ≈9,93мс, A/A Δ0,003мс,
A/B Δ≈0,003мс; последний B 10,82мс — позднее смещение, повторилось и в следующем
full-A center-north, причина не установлена. Полный обход8видов A/B GREEN:
p50 A 4,55…10,93мс, Δ(B−A) от −0,05 до +0,44мс без систематического выигрыша
(reports/q3-full-observation-summary-20261019.json, q3-normal-observation-summary-20261019.json).
Scene identity на Quest совпала с Windows:14802refs, тот же хеш сцены.
**Stage-трассы A/B** (instrumented, ovrgpuprofiler -t, collector включал/вернул detailed сам;
сейчас OFF): основной стерео-проход 2016×1760 MSAA4 multiview — работа без Preempt
A10,353/B10,363мс, Render ≈8,4мс, Binning ≈1,3–1,5мс; Preempt (компоновщик) A0,9/B2,3мс.
Вывод: кадр Lobby фрагментно-ограничен, LOD farStart25→20 не меняет GPU работу; путь к7мс —
фрагментная стоимость (MSAA/шейдеры/overdraw/разрешение) и вспомогательные проходы
(2×768² поверхности ≈0,6мс/кадр + compute ≈0,2мс — гипотеза: планарное отражение UXR).
Данные: reports/q3-stage-compare-20261019/ (tools/compare_stage_traces.py), raw A/B
q3-stage-raw-{A,B}-20261019. Это замеры одного вида и гипотезы, не принятые оптимизации.
Per-draw: ovrgpuprofiler -x на этом runtime данных не даёт. Новый адаптер
Tools/Perf/gpu_diagnostics/capture_adapters/meta_renderdoc_quest.py (Meta RenderDoc Fork68.18,
снимок/откат шлема, ревью approve; изменения шлема разрешены пользователем). Пробы r1–r7:
инъекция, захват кадра и PIL-сессия на шлеме работают, откат каждый раз verified; кадр стартового
экрана без E2E пустой (0 draw) — следующий шаг: захват Lobby через Run-QuestE2E (adb-launch
--intent-args). С19Oct задачу ведёт Claude-сессия (route epoch15). Новых tests нет;
quality/review/приёмка пользователем pending.

## Диагностические checkpoints18–19Oct (история)

AndroidCompileGate checkpoint9cc517a: PASS,0ошибок, ticket827 закрыт и receive выполнен.
Windows E2E Player7e07a43 собран Unity:0ошибок/108warnings. Wrapper завершился exit1
после сборки из-за отсутствующего cross-profile uxr-ids.txt; исправление подготовлено.
Windows discoveryA/B и pilotA/B GREEN19Oct; local ready windows приняты, restore выполнен.
GPU observation p50 A1.015808ms/B1.002496ms; одна пара без noise/repeats не доказывает
выигрыш и не является Quest budget. Полные8views A/B пройдены, но оба прогона RED
из-за потери фокуса; raw сохранён и не принимается как полноценная A/B серия.
Формат: [scene-index-v1](contracts/scene-index-v1.md). Новых тестов и Quest-прогонов нет.

Продолжение18Oct: runtime adapter/conditions, Windows applicability и управляемая
LOD-пара с restore реализованы и скомпилированы на input9cc517a,
ticket827 (826 заменён до аренды). Добавлен import-runtime с честными observations
и отдельными observation deltas. Build-Game умеет e2e Test через прежний builder,
dry-run exit0. Shared build dirty сохранён сопровождающим (9760), roots освобождены.
Opt-in build identity скомпилирован на223da422: AndroidCompileGate PASS0/ticket831.
Finish831 сначала не подтвердил возврат B; сопровождающий восстановил очередь9872,
сохранив исходный результат7b54c096. Ровно2meta получены штатным receive, без чужого
префаба. Instrumented mode скомпилирован в Windows Testf8bae4d9; новый discovery GREEN.
Ticket828 не начинался и закрыт
сопровождающим (event9780), blocked/recovery_required=false. Build7e07 имеет явный
dirty packages-lock, созданный UPM; его точные bytes/hash сохранены в RunSpec bundle.
Runtime7e07 и importer подтверждены Windows pilot; native source GPU frame/interval
остаются unknown. Отчёт reports/windows-pilot-ab-20261019/comparison.json — limited.
Для новой сборки dirty dedicated root сохранён и освобождён сопровождающим9832
(WIP44addf1db). Windows Testf8bae4d9: Unity и wrapper exit0. Opt-in identity processor
падал на Unity fake-null MeshFilter; минимальная правка и fail-build подготовлены,
новая сборка pending. Следующее сохранение generated build diff запрошено9911.
RenderDoc1.46: реальный instrumented E2E GREEN, capture206341966bytes,
export native-r10 exit0:446events/364work/660resources,368timed events,0missing refs.
Данные: reports/windows-renderdoc-export-A-r3-20261019, сохранён replay.log.
Это replay timing; scene joins и точная привязка source GPU frame остаются unresolved.
[Подробный статус и evidence](status-2026-10-18.md).

- Rebase завершён на dev2e1b6a2fc, HEAD67ea4b3d3. До обновления документов дерево
  чистое, production paths равны dev. Исходная история и несвязанный WIP сохранены.
- SceneDetail74bd/UI47c и launch provider e70e9a5e1 уже в принятой базе.
- GPU tools/recorder/exporter — неслитый source1d85f2305; реальный edit-time экспорт
  r2 найден:8643objects/1376resources/75lights, compilePASS. Это ещё не runtime/GPU A/B.
- Ведущий этого диалога реализует всё необходимое в своём checkout; эксперты проверяют
  готовый результат. Ниже сохранена **история15Oct**: назначения «инструменты только perf»,
  ожидания zone-ACK/передачи и старые checkout не являются текущим порядком работы.
- Исторические9evidence файлов скопированы с SHA256 в
  `reports/status-recovery-20261018/`. Новые Windows/Quest результаты не объявлялись.
- Hub task штатно передан в71af сопровождающим (rev24/event9672), route epoch8 опубликован.
  Guard не обходился. Подробности исполнения — в changelog/2026-10-18-execution.md.

## История до восстановления 18Oct

Обновлено: 2026-10-15 (продолжение Codex). Задача `scene-detail`, владелец level-design, участие performance.
Checkout: `F:/UnityProjects/Vr_Battlegrounds_ai` (основной), ветка `scene-detail`, содержит origin/dev `d3c62ca5e`.
Проект и этапы — [Details.md](Details.md).

## Текущее решение пользователя: передача performance

Сцена составлена; core74bd42f57 и UI47c010277 опубликованы. Дальнейшие инструменты измерения
и детального GPU/A/B-анализа ведёт **performance**, разбор результатов и правки сцены — её **автор**.
Здесь дальнейшие инструменты/оптимизации не запускаются: ждём измерительный пакет perf.
Полная передача кода, данных, доказательств и требований —
[handoff](changelog/2026-10-15-performance-handoff.md), комплект — `reports/performance-handoff/`.

Автор должен самостоятельно уметь готовить Test в сборочных checkout: маршрут запрошен у
launch-infra8290. Новый приоритет пользователя — быстрый Editor/Windows-client цикл до Android,
после него проверка на Quest; переносимость долей GPU между ПК и мобильным устройством не предполагается.
Дальнейшие пункты старого плана ниже — история; владение инструментами передано perf.
Perf подтвердил получение и границы8320, готовит свой scope/план. ZIP8311:154product+53taskfiles,
SHA25656a1e311a07255d9c9fcf89097d3fc0b21289bdeddc7a5e429d3ede217417be2,207payload проверены.
Дополнение8323: существующий Windows E2E Player пригоден для окна клиента (обычный Server — headless),
runner задаёт640×480/XRoff. Рецепт автора получен8313; его ограничения и открытые views/Map стыки
сохранены в handoff. Новые сборки здесь не запускались; дальнейшая диагностика инструментов — perf/launch.

Ревью спецификации perf по запросу8505: **подходит с условиями**; конкретизированы
переход к месту правки, ранжирование, эффективные LOD/свет и охват ракурсов.
[Consumer-review](changelog/2026-10-15-gpu-diagnostics-consumer-review.md) содержит
четыре сценария приёмки и статический кандидат первого LOD A/B. Это ревью дизайна;
реализация/замеры не принимались, новых прогонов и изменений сцены нет.

Для запроса perf8625 подготовлены [границы API](contracts/gpu-scene-detail-boundary-v1.md)
и [контрольная пара LOD](contracts/lod-ab-control-v1.json): только farStart25→20,
textureBias/cullSize0 в обоих планах/вариантах, полный список40 групп из690.
Accepted source5/5 сверены с74bd; на момент ответа8625 экспортного runtime API не было. Весь инструментальный
код остаётся performance; здесь контракт/данные, без реализации или запусков.

Контракт perf `gpu-scene-evidence` r1 и точный stage плана rev11 прочитаны;
formal contract-ack выполнен, публикация15b3ee9e65bcaf533c239be7bedc2ca252c9fee1.
[Ревью r1](changelog/2026-10-15-gpu-scene-evidence-r1-review.md): реализация у perf,
writer/lifecycle остаются прежними. Это согласование API, не приёмка реализации.

Owner-review perf8785 immutable checkpoint016fd8 завершён: статических блокеров
в accessor/DTO/exporter не найдено. [Результат](changelog/2026-10-15-gpu-scene-code-owner-review.md)
фиксирует три blob, точность original/effective и границы пригодности индекса.
Фактический Unity export/Quest/captured mesh ещё не подтверждены этим ревью;
новые tests/probes и запуски здесь не выполнялись.

## Цель

Регулируемая детализация среднего и дальнего планов для любой сцены: профиль сцены, применение во
всех сборках, подстройка с планшета в отладке, позже — правила на префаб и предмет. Первое
применение — Lobby: бюджет базы ovr_gpu p50 ≤ 7 мс по худшей из 8 точек обхода (сейчас 10,83 мс,
событие 6467: center-north 10,83, west-station 10,53, center-east 10,35, center-south 10,11).

## Статус

- `contract` — согласован performance (6499): профиль в Prod ДА; группы mipmap limit — часть
  инструмента (limitBias 0 на всех уровнях, globalTextureMipmapLimit не трогать, итоговые лимиты
  дальнего плана — в импорт Android, рантайм-смещение — только по правилу плана с замером tex_mb);
  бюджеты сцен — документ performance. Zone-ack на core: performance (6508), level-design (6509).
- `core` — принят пользователем15Oct «вливай как есть» и опубликован в origin/dev
  `74bd42f5760b5e3f04d2e886a5685a2f52ff3d1d` (FF поверх04fc, ls-remote подтверждён).
  Размещение/порядок UI подтверждены7983, MenuDebug не менялся. Основной Unity открыт;
  итоговая компиляция Editor и AndroidCompileGate — PASS, Android ошибок 0:
  `Assets/Scripts/Maps/Detail/` — `SceneDetailProfile` (ScriptableObject), `SceneDetailApplier`
  (единственный писатель; OnEnable/OnDisable; LOD обрезкой списка с сохранением порога отсечения;
  мелочь ≤ 0,6 м; группы `DetailBand0..8`; на сервере без графики не работает),
  `SceneDetailDebugSection` (секция планшета, только Development, «Сохранить» → JSON в
  persistentDataPath/detail/<сцена>.json); `Assets/Editor/VR_Battlegrounds/Maps/Detail/SceneDetailSetup.cs`
  (меню Tools/VR Battlegrounds/Detail: «Собрать записи сцены», «Проверка слотов материалов (обрезать
  лишние)», «Забрать профиль со шлема»; отчёты в Library/SceneDetail/).
- Исправления Codex: безопасный импорт JSON через adb, возврат исходных mipmap-настроек,
  имя сцены в сохранении, дочерние исключения LODGroup, сбор от нулевой дистанции, запрет
  второго активного писателя DetailBand. Подробности — `changelog/2026-10-14-codex-continuation.md`.
- Lobby: профиль создан; 690 LODGroup, 361 мелкий рендерер, 252 текстуры (112 исключены, 140
  texture-meta переведены в DetailBand). Environment/DetailTuner удалён; 9 LobbyBand удалены.
  Сцена сохранена. Editor authoring-check — PASS: исключения, записи, 9 групп, bias 0 на двух
  Quality-уровнях, globalTextureMipmapLimit 0/0; console ошибок 0. Геометрия не изменена.
- Отчёты вне Git: `reports/android-compile.json`, `reports/lobby-authoring-check.json`,
  `reports/lobby-collect.json`, `reports/lobby-migration.json`, `reports/main-editor.log`.
  Это компиляция и authoring-проверка; Play/Quest, UI и бюджет 7 мс ещё не приняты.
- Прямые инструменты MCP работают с основным Editor; resource API вернул Session terminated.
  Worker не использовался. Bootstrap подключения отклонён auto-review и не создан; он не понадобился.
- Финальный Android-вызов вернул timeout, но отчёт и лог подтвердили последующее завершение PASS;
  операцию не повторяли. Прежний database is locked при свежей проверке снят; inbox 6805 обработан.
  Canonical inbox: UI-запрос 6807, performance-ревью 6813, infra-отчёт 6817 доставлены.
  UI ответил 6821: обычная интеграция после приёмки/merge core; временная композиция требует
  immutable owner grant и recorded dependency proof. Так как core acceptance включает планшет,
  порядок образует зависимость по кругу; маршрут технической композиции уточняется у UI.
  Квитанции — `reports/coordination-pending.json`. Core scope для миграции — 140 точных путей meta,
  поскольку хаб поддерживает точные пути и завершающий /**, но не маску **/*.meta.
- UI 6857/6964 согласовал отдельную техническую композицию: consumer debug-menu-refactor rev5,
  implementation RUNNING epoch1; design proof verified epoch3 `63dbf45f56b67a396f6f05a30dc992e3fb2795d7`.
  Требуется свежий точный provider checkpoint/grant runtime, companions, профиля, Lobby, Quality и meta.
  Bindings/B/TTL перепроверяются при публикации; принятия/release этот маршрут не даёт.
- UI 7589: по уточнению пользователя детализация размещается в «Перф-анализ → GPU», отдельная
  вкладка «Сцена» убрана. Build/Snapshot неизменны; consumer теперь rev8, все старые bindings
  недействительны. Новый source/grant передаётся после свежей проверки B/permit/epoch/TTL.
- Performance 6875: статических блокеров нет, Quality/meta подтверждены; Apply имеет burst allocations,
  zero-GC не заявляется. Экспорт 252/112 и proof GUID disjointness готовы:
  `reports/protected-textures/working-snapshot-proof.json` (112 protected, 140 changed, пересечение 0;
  все protected.meta byte-identical HEAD). Это physical snapshot, ещё не source-bound grant.
- Performance 7220 независимо подтвердил proof: 252 unique paths/GUID, 112 protected + 140 changed,
  пересечение 0, drift 259 physical SHA = 0. Evidence-gap 6875 закрыт для этого static snapshot;
  это review acknowledgment, без приёмки runtime/UI/Quest, zero-GC или immutable grant.
- Blocker checkpoint 7183 снят: сопровождающий выпустил исправление 7657, runtime
  `1432984ac464614e7956aaba9f2eec78602e3f77`. Один штатный повтор с прежними selected paths — PASS.
  Source `30901528d984fe18b5ad01f03ddb06a827f70c21`, parent/B
  `1812dc5bf22860e8302e858b1637e92b1df636e9`; HEAD/index основного checkout и Git-состояние
  чужого вложенного checkout сохранены. Worker request/lease не использованы. Полная квитанция:
  `reports/ui-provider/checkpoint-retry.json`; результат сопровождающему — event 7673 и native.
- Проверен source-bound пакет: 151 primary entry, 112 protected texture-meta побайтно сохранены,
  140 changed texture-meta, пересечение GUID = 0. Manifest `reports/ui-provider/grant.json`,
  SHA256 `6fecb5b6125948b5568e0a0b2da13d56461243e8252efce5479a94ea1feb0692`;
  proof `reports/ui-provider/protected-texture-proof.json`. Для неизменной Lobby.unity.meta
  подтверждены одинаковые B/source blob и GUID; физический CRLF отличается только EOL.
  Changed/new primary paths проверены строго по сырым байтам Git. Grant rev1 опубликован,
  consumer UI rev10 получил event7683; performance — source proof7686. После смены B и
  consumer revision старый grant не переиспользовать.
- По canonical UI7800 готовится grant rev2 для consumer12 / implementation epoch3.
  Новый checkpoint `cd2f0c632ee65b124d4abac41de45f0d5eb72ab5` имеет parent/B
  `d449f19832b1c2b0f6bb9c9ada3b9db4a4a2b299`; HEAD/index/foreign checkout сохранены.
  Квитанции и точный результат grant — `reports/ui-provider-rev2/`. Регистрация этого статуса
  выполняется перед публикацией, затем binding повторно сверяется по хабу. API не изменён.

## Найденные средства анализа

`Tools/Perf/quest_gpu_breakdown.py` снимает GPU на Quest по фазам `perf-stress` (зона performance):
realtime — абсолютное время, detailed — только доли. `LobbyQuestDetailBudget.cs` — прежние команды
LOD/дальнего декора/Android-текстур; исторические отчёты —
`tasks/lobby-decoration/reports/implementation/quest-budget/`. SceneDetail заменяет управление
`LobbyDetailTuner`, измерения остаются у performance. Профиль Lobby уже подготовлен.

## Следующий шаг (core)

1. UI: заменить старый блок LobbyDetailTuner на SceneDetailDebugSection.Build/Snapshot в GPU-секции
   «Перф-анализ»; MenuDebug.cs
   остаётся у владельца ui. Для приёмки core до merge согласовать техническую композицию с
   immutable grant/proofs; обычный UI-маршрут 6821 ждёт merge core. Класс LobbyDetailTuner
   пока сохранён из-за старой ссылки MenuDebug.
   После исправления checkpoint: новый private snapshot, `tools/prepare_provider_grant.py --base B
   --source SHA`, свежие provider/consumer binding, публикация grant; передать UI exact source/proof.
2. Performance: ревью diff QualitySettings и условий нового замера до checkpoint.
3. Play Lobby только через PlayLaunch: подстройка LOD без появления отсечённых объектов;
   выключение/выгрузка возвращает исходное состояние, сохранение/перенос JSON корректны.
4. Приёмка пользователем в шлеме; после неё — тесты SceneDetail и вливание. Затем новый обход
   8 точек performance для lobby-apply. Fetch origin/dev выполнен; rebase не требовался.

Дальше этапы `lobby-apply` (профиль Lobby: дальний план худший LOD + мелочь ≤ 0,6 м; «Проверка слотов
материалов» — убирает розовые куски домов; общие материалы стрельбищ ради setpass; замер — обход
8 точек performance), `shader-tier`, `overrides`, `docs`.

## Принятие и оставшаяся проверка

- 2026-10-15 пользователь ответил на запрос проверки: «вливай как есть - согласовав с UI expert
  расположение в меню». Это явное разрешение принятой публикации текущего core без дополнительной
  ручной проверки. Ни Quest-замер, ни JSON/controls этим не объявляются проверенными. UI-запрос7979.
- UI7983 подтвердил Перф-анализ → GPU, единственный Build/Snapshot с полной заменой legacy;
  pending718 отменён до аренды. Сначала scoped accepted core, затем UI-интеграция/свои проверки/release.
- Публикация core выполнена74bd42f57:154product+13taskfiles, соседние старые task docs исключены,
  mainHEAD/index сохранены. Свежие AndroidCompileGatePASS0, существующие LobbyLayout/Occlusion8/8;
  LF physical/staged/rawcommit167/167. Unity-generated trailing spaces в пустых полях новых meta
  и Lobby YAML сохранены как сериализовал Editor; source/docs diff-check чистый.
  Локальный dev обновлёнFF без переключения основной ветки. Worker base719 DONE/event8105:
  broker B74bd42f57 подтверждена по config, blocked/recovery=false. UI уведомлён7993 и результатом719.
  Ledger core также MERGED74bd42f57 (record a782db4e7f6d49dd96f4ca58783e6434): по маршруту8148
  добавлен только порядок lobby implementation.after → scene-detail/core merged, lobby register16;
  core begin/verify/record-merged выполнены штатно. Scope и остальные зависимости Lobby сохранены,
  его implementation остаётся PLANNED, приёмка других этапов не менялась.
- Приёмка `core` в Unity/шлеме: подстройка LOD/отсечения мелочи/текстур без появления отсечённых
  объектов, сохранение/импорт JSON, восстановление исходного состояния при выключении/выгрузке.
  UI714 получил7PNG, но detailAvailable=false в EditMode; принятие этих снимков не принимает core.
  Запрос UI7937 на accepted dependency обработан7941: публикация ожидает своей проверки.
  UI7966 снял зависимость своего accepted release: выпускает существующий LobbyDetailTuner,
  provider-вариант сохранён в UI reports/scene-detail-deferred/MenuDebug.inline-with-provider.cs.txt
  (SHA256953a9c06a788d6da8b1945ffb0a4dec263c06bce91ab6c32285513fee44486c6).
  После отдельного accepted core UI заменит старый блок целиком на Build/Snapshot в GPU.
  Технический grant4 для ненужной composition отозван; UI release core не блокирует.
- PC-host smoke выполнен через PlayLaunch97d0962f340e49e19a31fc730c5ff4a3: Ready/Connected,
  Current существует, Live отличается от ассета,690LOD+361renderer, Snapshot непустой, console errors0.
  Cancel завершён: Idle, CleanupPassed=true, ProfileRestored=true, Current=null, Lobby clean.
  Отчёты reports/core-runtime/startup.json и cleanup.json. Это загрузка/уборка, не проверка controls,
  JSON roundtrip, визуального отсечения или Quest. Новые тесты до пользовательской проверки не писались.
- Приёмка `lobby-apply`: визуал дальнего плана + замер 8 точек ≤ 7 мс.

## Решения пользователя

- 2026-10-13: «мы сейчас жертвуем картинкой в пользу производительности»; бюджет базы 7 мс («да давай попробуем 7мс»).
- 2026-10-13: отсечение мелочи «не больше 50-60см»; стёкла прожекторов убрать; динамическое освещение не нужно нигде.
- 2026-10-13: «для дальнего плана можно использовать худшие текстуры и лоды, особенно в темноте»;
  средний план при подборе изменений не дал.
- 2026-10-13: «давай оставим регулируемые настройки, чтобы в дальнейшем можно было каждую сцену так
  настраивать. возможно в будущем ещё добавим параметры + уникальные настройки на предмет или префаб»;
  управление — «в планшете в разделе отладка».
- 2026-10-14: «инструмент детализации нужен будет и в дальнейшем поэтому начинаем с него».
- 2026-10-20: цель — производительность в ущерб картинке; зеркало в лобби **оставить** («люди любят вставать
  у зеркала и дурачиться»), на игровых картах его нет. Удешевлять можно, удалять нельзя. Факт: Lobby
  UxrPlanarReflectionUrp _textureSize 768, _maxReflectionDistance 16, две поверхности 768² (по глазу) ≈0,3мс
  каждая в center-north; patch 78 уже не рисует отражение вне кадра.
- 2026-10-20: всегда оценивать целесообразность — доля GPU объекта против трудоёмкости его оптимизации;
  при дорогой оптимизации предлагать замену почти бесплатной альтернативой (пример — пол), сначала проверять
  стандартные приёмы оптимизации игр. Правило внесено в experts/performance/AGENT.md.

## Договорённости и ловушки

- ui (`debug-menu-refactor`, 6495): MenuDebug.cs правит только ui; секция детализации — вызов
  `SceneDetailDebugSection.Build(content, invalidate)` + `Snapshot()` в «Перф-анализ → GPU» (7589). Если UI
  вольётся раньше core — после влития core прислать ui однострочную замену блока LobbyDetailTuner.
- Текстуры, общие с ближним планом, не ухудшаются (группа — по ближайшему использованию): для
  среднего плана нужны отдельные ассеты текстур, иначе эффекта нет.
- Брокер: бюджет serialized binary в Git 64 MiB — крупные `.asset` (окклюзия, LightingData) — в LFS.
- Каналы: `inbox --unread --as <свой адрес>`; ожидание — `wait-inbox --owner level-design --as <адрес>`
  фоновой командой. В Codex вопросы пользователю — через доступный инструмент запроса ввода;
  статус ожидания дублировать в «Ждёт пользователя».
