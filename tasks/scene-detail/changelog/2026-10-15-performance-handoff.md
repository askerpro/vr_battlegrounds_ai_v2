# Передача инструментов GPU-анализа эксперту performance

2026-10-15. Передаёт level-design / scene-detail; получатель performance / perf-stress-scenario.
Основание — прямое решение пользователя в текущем чате. Автор составил сцену; дальнейшие
оптимизации выполняются по измерениям после готовности инструментов performance.

## Ответственность и рабочий цикл

- **Performance:** инструменты GPU-профилирования, захват кадра/счётчиков, привязка GPU-событий
  к данным сцены, воспроизводимый A/B-отчёт, экспорт сырых данных, проверка достоверности метрик.
  Техническое сопровождение переданных SceneDetail-инструментов согласуется и регистрируется
  в собственной задаче performance; код MenuDebug остаётся за UI.
- **Автор сцены / level-design:** разбор отчёта применительно к сцене, выбор правок и визуальных
  компромиссов, сцена/профиль/ассеты, подготовка точных вариантов A/B и повторных прогонов.
  Автор должен уметь самостоятельно собирать Test в выделенных build checkout по маршруту launch-infra.
- **Launch-infra:** штатная инфраструктура сборки/запуска, сборочные checkout, параметры/блокировки,
  происхождение APK/EXE и артефактов. Запрошен конкретный рецепт событием8290; build пока не запускается.
- **UI:** размещение и представление панели, MenuDebug/MenuKit. Согласовано «Перф-анализ → GPU»,
  один SceneDetailDebugSection.Build/Snapshot вместо прежнего LobbyDetailTuner-блока.

Цикл: инструменты perf → измерение A → автор изучает отчёт → меняет один фактор сцены →
точная Test-сборка B → тот же прогон → разница GPU и деталей → следующий осмысленный шаг.
До появления отчётов дальнейшие оптимизации LOD/текстур/шейдеров сцены не начинаются автоматически.

## Быстрая предварительная проверка на Windows — уточнение пользователя

До дорогой Android-сборки нужен быстрый цикл Editor/Windows Test-клиент: находить лишние
проходы/отрисовки, неверные LOD/отсечение, overdraw, дорогие шейдеры и геометрию, сравнивать
варианты A/B на ПК. Предпочтителен отдельный Windows Player для GPU-таймингов, Editor — для
самой быстрой структурной диагностики. Нужен клиент с работающим рендерингом, не headless/server-only.
Конкретный пригодный Test-профиль/сборочный маршрут уточняет launch-infra.

Пользователь напомнил о существующих E2E-клиентах Windows. По исходникам подтверждено:
`E2EPlayerBuilder` создаёт обычный Development Windows Player `Build/e2e/VrBattlegrounds.exe`;
тот же exe запускается сервером с `-batchmode -nographics`, а клиентом — обычным окном.
`Run-E2E.ps1` запускает клиент без `-nographics`, но сейчас задаёт640×480. Для GPU-анализа
переиспользовать этот клиент; обеспечить контролируемые разрешение/камеру/качество и capture.
E2E-сборка отключает XR на старте: её монорендер не подтверждает стоимость Quest stereo/multiview.

Затем контрольные замеры Quest подтверждают мобильную стоимость и итоговый бюджет. Windows
и Quest нельзя считать одинаковыми по относительным GPU-долям: tiled GPU, bandwidth/кэши,
mobile shader variants/graphics API, XR stereo/multiview/FFR и платформенные настройки меняют
соотношения. Даже знак/масштаб выигрыша отдельной оптимизации нужно подтверждать на устройстве.

Perf должен определить, какие признаки/счётчики сопоставимы между платформами, обеспечить
одинаковые scene/profile/view/settings там, где это возможно, и явно показать расхождения.
Критерий отправки на Quest — пройдены согласованные Windows-проверки и подготовлен кандидат;
«на мощной видеокарте нет просадки FPS» само по себе не означает готовность под мобильный GPU.
Нужен периодический опорный Quest capture, чтобы Windows-цикл опирался на реальные ограничения шлема.

## Что требуется от анализа

Нужно максимально точно выяснять, **куда и сколько уходит GPU-времени кадра**, с возможностью
дойти от дорогого прохода/отрисовки до конкретного меша, освещения, текстур и шейдера сцены.
Не ограничиваться общим FPS или числом треугольников.

Минимальный полезный результат:

1. Реальное время приложения GPU в мс, отдельно compositor; распределение по одинаковым
   точкам обзора/нагрузкам и сырые выборки, а не одно случайное число.
2. Проходы/поверхности/стадии рендера: основной рендер, тени и освещение, depth/opaque/transparent,
   постобработка/копирование/загрузка-сохранение буферов/compute — где реально присутствуют.
3. Дорогие draw calls и используемые меши/LOD, материалы, шейдеры/варианты, текстуры;
   привязка к пути объекта сцены и устойчивым идентификаторам/GUID, когда это возможно.
4. Признаки стоимости: обработка вершин, фрагменты/overdraw, текстурные выборки и простои,
   память/bandwidth, освещение и тени. Отмечать реальные динамические затраты и запечённые данные.
5. Для каждой атрибуции — метод, единицы, область действия и точность: прямое измерение,
   аппаратный счётчик, корреляция либо предельная стоимость из A/B. Не выдавать оценку за
   прямое измерение и не складывать неаддитивные GPU-стоимости в ложный точный итог.
6. В A/B-отчёте — до/после и разница GPU в мс, относительное изменение, разброс результатов,
   изменившиеся проходы/draw calls/объекты, точный перечень отличий и ссылки на captures/logs.

Обязательный пример: автор меняет LOD-правило на конкретной дистанции. Должны быть видны
исходные и новые границы/LOD, затронутые объекты и меши, фактическая геометрия/отрисовки,
а также изменение реального GPU-времени при том же обзоре и условиях. Аналогично — текстуры,
шейдеры, свет/тени и проходы, если именно они меняются.

Для воспроизводимости фиксировать source/build/scene/profile SHA или hashes, устройства/OS/GPU,
graphics API, разрешение/render scale, MSAA, FFR, частоты/уровни CPU-GPU и тепловое состояние,
точку/позу камеры, число аватаров/нагрузку, настройки качества и параметры прогона. Факторы,
которые удержать не удалось, явно включать в отчёт. Финальные абсолютные сравнения — без
накладных расходов detailed profiling; его traces используются с явной маркировкой overhead.

## Переданные наработки и точные источники

Принятый core: **74bd42f5760b5e3f04d2e886a5685a2f52ff3d1d**, origin/dev, ledger core MERGED.
UI-интеграция: **47c0102778d00b6a06f6c3eceb96aff2e425493d**, origin/dev, Build/Snapshot в GPU.
В своём checkout получить принятые коммиты обычным fetch/rebase с сохранением своих изменений.
Основной checkout не копировать целиком: его HEAD — старый WIP, чужие worktree сохраняются.

- `Assets/Scripts/Maps/Detail/SceneDetailProfile.cs`: границы планов, правила LOD/mip/cull, исключения.
- `Assets/Scripts/Maps/Detail/SceneDetailApplier.cs`: один runtime-писатель, Live-копия, Apply/Restore;
  полосы DetailBand0..8, сохранение исходных настроек, запрет конкурирующего владельца.
- `Assets/Scripts/Maps/Detail/SceneDetailDebugSection.cs`: Build/Snapshot, живая настройка и JSON-save.
- `Assets/Editor/VR_Battlegrounds/Maps/Detail/SceneDetailSetup.cs`: сбор записей, материал-слоты,
  перенос JSON через adb с валидацией. Editor-списки при импорте правил сохраняются.
- `Assets/Scenes/Lobby.unity`, `Assets/Scenes/Lobby/LobbyDetailProfile.asset`, QualitySettings
  и140 texture-meta — принятый образец конфигурации; значения сцены остаются зоной автора.
- `Tools/Perf/quest_gpu_breakdown.py`: существующая perf-заготовка realtime/detailed по фазам
  perf-stress. Получить полный raw вывод, адаптировать/проверить CLI на реальном Quest.
- `tasks/scene-detail/tools/`: подготовка immutable grant, protected texture proof, технических
  checkpoint и scoped accepted candidate. Это вспомогательные операции задачи, не замена
  брокеру/lease и не разрешение чинить инфраструктуру.
- `tasks/scene-detail/Details.md`, `Readme.md`, `plan.json`, весь `changelog/`: контракт, решения,
  этапы, результаты и история передачи. Приватные временные Git-индексы не нужны получателю.

## Данные и доказательства (на этой машине, reports вне Git)

Корень: `F:/UnityProjects/Vr_Battlegrounds_ai/tasks/scene-detail/reports/`.

- `accepted-release/`: publication.json, candidate.json, Android compile,8 существующих tests,
  core-ledger begin/verify/merged.154 product paths и13 task files приняты в74bd, соседние старые
  task docs в выпуск не попадали. Основной HEAD/index сохранены.
- `core-runtime/startup.json`, `cleanup.json`: PC-host Lobby через PlayLaunch, Current/Live,
 690LODGroup+361renderer, непустой Snapshot; Idle/CleanupPassed/ProfileRestored, Lobby clean.
- `lobby-collect.json`, `lobby-migration.json`, `lobby-authoring-check.json`: авторинг Lobby.
- `protected-textures/lobby-textures.json`:252 уникальные текстуры/GUID и112 исключённых;
  `working-snapshot-proof.json`:259 physical hashes; performance7220 независимо проверил.
- `ui-provider*/protected-texture-proof.json`, manifests/receipts/checkpoints: source-bound
 112protected/140changed, GUID intersection0. Старые grants/revisions исторические, не переиспользовать.
- `android-compile.json`, `main-editor.log` и остальные task reports: полный контекст прошлых проверок.

Переносимый комплект и manifest/hash inventory подготовлены в `reports/performance-handoff/`.
В него входят154 product paths принятого core и task material/reports, кроме временных .index,
кэшей Python и самого выходного каталога. ZIP — передача исходных данных, не новый runtime gate.

## Границы готовности и известные пробелы

Editor/Android compilation PASS0, Lobby authoring PASS, существующие LobbyLayout/Occlusion8/8,
независимое static review P0-P2none. Core принят пользователем «вливай как есть».
Quest, live-controls, JSON roundtrip и GPU≤7мс не объявлены проверенными. Исторические10,83мс
и бюджет7мс — ориентир прежнего прогона, не свежий замер этой конфигурации.

В `quest_gpu_breakdown.py` жёстко заданы IDs realtime/trace metrics и варианты `-x/-s` команд.
Сверить их с каталогами **конкретного шлема/runtime**, разделив realtime/render-stage/per-draw
catalogs и проверяя exit code/полноту. Современные сведения Meta предупреждают о меняющихся IDs
и overhead/искажении абсолютного per-draw timing. Без такой проверки raw capture не считать
готовой достоверной разбивкой кадра. Автоматической полной привязки GPU→объекты и A/B-отчёта пока нет.

### Ответ launch-infra8313 и открытые стыки

Рецепт автора: `F:/CodexWorktrees/launch-infra/Vr_Battlegrounds_ai/tasks/quest-perf-pipeline/reports/build-checkouts-validation/scene-author-recipe.md`;
копия/хеш — дополнение в `reports/performance-handoff/`. Это инструкции будущего исполнения,
не разрешение занимать текущие build roots: сначала завершение матрицы владельца.

Выявленные по source ограничения, которые perf/launch должны закрыть/учесть:

- `GameBuilder.Server` — Dedicated/headless; E2E Windows Player отличается и пригоден для окна клиента.
- Реальный Test repeat74bd имел PASS851с, но caller использовал WIP-исправление reader UXR manifest.
  На момент ответа исправление reader ещё не принято; accepted build launcher не объявлен GREEN.
- `perf-stress` пока не принимает views, обход views реализован у `device-smoke`; полноценный
  фиксированный A/B GPU-view нельзя считать готовым только по наличию общего аргумента.
- `context.Map`/параметр `-Map` сам по себе не подтверждает загрузку нужной карты. Нужна проверка
  фактических сцены/MapRun/позиции перед capture, с соблюдением MapLoader/владения запуском.
- Путь `Run-QuestE2E` ожидает E2EPlayerBuilder `Build/e2e/VrBattlegrounds.exe`, не обычный Build/Server.

Точный Windows-only рецепт получен8325 и добавлен владельцем в тот же документ; сохранена
отдельная копия `reports/performance-handoff/launch-windows-recipe-received.md`.
Сборка — `E2EPlayerBuilder.RunBatch` в свободном build-test-win64 на проверенном fullSHA,
затем `Run-E2E.ps1 -Player FULL_E2E_EXE -Scenario device-smoke -Clients 1 -Timeout 300 -LogVerdict enforce`.
Это существующий rendered client, новый профиль не нужен. У Run-E2E нет CLI выбора resolution,
host/views/headPose/profiler/backend/API, клиентское окно640×480; внешний provenance и capture
должен обеспечить инструментарий perf/launch. Готовность исходного маршрута подтверждена source/static,
не новым GPU-прогоном. Пока занята build-матрица, новые сборки здесь не запускаются.

Справочные первоисточники:

- https://developers.meta.com/vr/documentation/native/android/ts-ovrgpuprofiler/
- https://developers.meta.com/vr/documentation/unity/ts-renderdoc-for-oculus/
- https://developers.meta.com/vr/documentation/unreal/ts-renderdoc-settings/
- https://developers.meta.com/horizon/documentation/unity/gpu-impaired-algorithms/
- https://docs.unity3d.com/2022.2/Documentation/Manual/profiler-profiling-applications.html

Ожидается ответ performance о получении полного пакета, собственной задаче/области инструментов,
плане получения подробного отчёта и договорённости с launch-infra. Анализ сцены и выбор её правок
остаются у автора; здесь новые инструменты/оптимизации не продолжаются.
