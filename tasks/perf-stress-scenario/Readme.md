# Стресс-тест на шлеме без рук: сценарий perf-stress, арена, обход точек обзора

Обновлено: 2026-10-15 (продолжение в Codex). Владелец: эксперт `performance`. Worktree:
`F:/CodexWorktrees/performance/Vr_Battlegrounds_ai`, ветка `claude/performance` (HEAD — последний коммит ветки;
работа этапа `view-tour` — 832d7f50f после rebase на origin/dev 54673ba496). Начата по решению пользователя («начинай свою часть»).

## Цель и границы

Перф-прогон на шлеме одной командой `Tools/e2e/Run-QuestE2E.ps1 -Scenario perf-stress` — без человека в шлеме,
шлем-клиент и выделенный сервер на ПК (контракт `quest-device-run` r5, ACK; задача `quest-perf-pipeline`, launch-infra).
Сценарий только наблюдает и запускает стресс-тест; FFR, уровни CPU/GPU не трогает; порогов не ставит (вердикт —
«прогон состоялся», оценка чисел — performance). Проект и решения — [Details.md](Details.md).

## Статус

| Этап | Суть | Состояние |
|---|---|---|
| `setup` | документы задачи | merged `ec711f2df` |
| `scenario` | сценарий `perf-stress` без рук, обе роли, `-e2eArg`, артефакты | merged `fa83395aa` |
| `controller-free-launch` | Test-APK стартует без пробуждения контроллеров (руки в манифесте, Test only) | merged `3cabcc1ab` |
| `arena-layout` | куклы по полу арены, корни не следуют за игроком; `Tools/Perf/quest_gpu_breakdown.py` | merged `2a5ed8f07` |
| `view-tour` | обход точек обзора `views=<файл>`, метрики по точкам | **planned, блокер EditMode MCP**: runtime принят пользователем; свежий AndroidCompileGate PASS0 (670); новый запуск670 после SDKunity-mcp2 потерял соединение до `job_id`/итога (7318); worker освобождён |
| `gpu-tool-phases` | `quest_gpu_breakdown.py --phases <имена>` — профайлер в произвольных фазах (обещано weapon-system) | planned |
| `docs` | раздел в `Docs/perf-stress-test.md` (обход, арена, без рук, бюджет сцен); файл точек → `Tools/Perf/views/Lobby.json` | planned |

## Следующий шаг (конкретно)

1. `view-tour`: дождаться диагноза сопровождающего по infra_issue7318 (продолжение7008),
   затем готовить следующий ограниченный пакет только по подтверждённому маршруту.
   По infra_status7375 подготовлена `contracts/view-tour-test-spec-v1.json`: неизменяемая
   спецификация фильтров/17 новых случаев, XML/log/receipt/correlation и cleanup. Это подготовка,
   не запуск и не утверждение совместимости с пока не выпущенной схемой CLI executor.
   После worker_ready7257/SDKunity-mcp2 проведён новый независимый пакет670: свежая
   AndroidCompileGate PASS0; run_tests EditMode/DevTools потерял plugin session до job_id.
   Исходы634/654/670 UNKNOWN; инфраструктурные smoke-проверки не заменяют результаты DevTools.
   Никаких retry/replay/clear_stuck/Refresh/саморемонта. Отчёты670 сохранены в ignored reports/;
   finish без --artifact завершён, R a66b06b000ed, paths0/unexpected0, worker освобождён.
   Ожидаемое подтверждение: весь DevTools зелёный, включая 17 новых случаев (8 Plan +9 Args).
   Затем зафиксировать проверку и прежнюю приёмку → штатное fast-forward в origin/dev;
   в mode off — legacy pipeline, в enforced — verify/accept/merge-request/merge-execute.
2. `gpu-tool-phases`: `--phases` в `Tools/Perf/quest_gpu_breakdown.py` (сейчас слушает только «база»/«куклы…»);
   README рядом. Нужен weapon-system для замера стоимости выстрела (метод 6156).
3. `docs`: порядок правки `Docs/perf-stress-test.md` согласовать с avatar-ik (legs-ik/`quest-perf` пишет тот же файл).

## Ждёт пользователя

- Ничего открытого по этой задаче. Уточнения и решения — через доступный инструмент вопросов пользователю.

## Возобновление 2026-10-14

- 2026-10-15 после worker_ready7257: свой route epoch9; fetch/rebase origin/dev54673ba496
  без конфликтов, runtime/test diff bd7dd0127→832d7f50f пуст, карта owns/watch без новых изменений.
  Пакет670/input28afff074/epoch566: AndroidCompileGate PASS0, run_tests один раз вернул
  `plugin session12cf785b-e214-4a7b-9471-b6ef4bd5070e disconnected while awaiting command_result`,
  job_id/data отсутствует. Native guard после idle/responsive; finish запущен сразу,
  R a66b06b000ed paths0/unexpected0. Runtime VERSION eb4ab1d981654f3bcd64cd4eb546cbcc950370d6.
  Infra_issue7318 отправлен в canonical inbox и подтверждённый Codex-чат сопровождающего.
  Подробности: `reports/validation-20261015-670/`; новых Unity-заявок не оставлено.
- После уведомления worker_ready_resume 6805 обновлён собственный маршрут (epoch 6), inbox обработан.
  Восстановление общей очереди не объявляет исправленным частный MCP-сбой тестов 6790.
  Диагностическая заявка 645: прочитать сохранённые TestJobsV1/CurrentTestJobIdV1 в собственной аренде,
  сохранить полный отчёт и сразу finish/receive. Вход 4b76171687, база 97a320c88.
  Диагностика645 DONE: чтение PASS, currentJob пуст, ни один из четырёх jobs не совпадает со своим634.
  Старый исход не установлен. Raw сохранён, hash04fda9a47d…; full reports/validation-20261014-645/.
  finish --artifact пометил report-only путь unexpected; receive отказал, R9dd929ea6 не интегрируется.
  Worker освобождён. Infra_issue6940. После fresh-state проверки подготовлен независимый запуск654;
  будущий PASS относится только к его job/input и не меняет неизвестный исход634.
- Проверка654 завершена без результата тестов: `run_tests` один раз; plugin55158773-2ac5-4c91-93a0-ed0ec8df956c
  disconnected до job_id. После вызова guard/native state idle, currentJob=null/lastRun=null.
  Finish/receive DONE, R3efdf8f04, paths0/unexpected0; admission возвращён в planned с BLOCKED reason.
  Infra_issue7008; полные ответы reports/validation-20261014-654/. Retry/replay/clear_stuck не применялись.
- 2026-10-15: пользовательская policy7160 подтверждена в canonical inbox и прямом user turn
  сопровождающего: infra issue сразу hub + проверенный Codex channel, без вопроса пользователю.
  Открытый7008 дополнительно уведомлён через native channel, собственный маршрут обновлён(epoch7).
  Recovery7202 завершён сопровождающим/owner; 7216 отдельно сохраняет test-result blocker:
  неизвестные старые исходы, lost-start receipt и потеря Result после reload не исправлены recovery.
  Новых Unity-заявок/мутаций нет. Следующий шаг инфраструктуры — durable command_id/job_id/start receipt
  и полный terminal result, затем независимая проверка DevTools. Gameplay приёмка не меняется.
- Хаб сверён: четыре первых этапа merged; `view-tour` возвращён в planned после неуспешного пакета
  проверки (в причине stop — MCP-блокер); `gpu-tool-phases` и `docs` planned. Это не отменяет приёмку runtime.
- Inbox performance обработан: новых адресных сообщений нет. Прежние маршруты Claude очищены;
  `perf-stress-scenario` переведён на справочный client Codex (rev 9), опубликован собственный маршрут
  `codex-desktop/01a13af4-33f9-7740-8274-1f3226b0275e`. Чужие маршруты не менялись.
- Рабочая копия на старте чистая. Основной checkout только для чтения; Unity — исключительно через собственную аренду.
- Подготовленный пакет: компиляция и существующие EditMode-тесты DevTools, без Play и без сборки пары APK/сервера.
- Worker: публикации 620/621 DONE, база d3c62ca5e. Сопровождающий возобновил FIFO (уведомление 6695).
  Вход проверки 38af759779eeef71dba8dd17f9fd7047533e109e; билет 622 CANCELLED (`offer_expired`),
  повторный 626 DONE/received: первый `execute_code` оборвался `Session terminated`, результата
  AndroidCompileGate нет, EditMode не запускался. После idle guard аренда сразу закрыта; R=5ee4591c2,
  изменённых файлов 0. Read-only MCP затем восстановился.
  Повторная аренда 634 DONE/received: AndroidCompileGate PASS, 0 ошибок; `run_tests` EditMode потерял
  Unity plugin session bfec0fe2-d4ec-41d5-b1c1-709496fb8591 до ответа `job_id`/итога.
  Guard после вызова — idle, тесты не активны. Аренда сразу закрыта: R=9a23d26f8, изменённых файлов 0.
  Общую инфраструктуру из этой сессии не меняли; `EDITOR_DELAYCALL_STALLED` сопровождающий исследует отдельно.
  Собственных активных/ожидающих аренды нет. Подтверждённого результата EditMode нет;
  отсутствие активного теста после разрыва не является ни PASS, ни доказательством, что запуск не состоялся.
  Infra_issue отправлен сопровождающему: `agent-coordination-protocol`, событие 6678.
  Отдельный отчёт сбоя MCP — событие 6768, исходный ответ в `reports/validation-20261014-626/`.
  Блокер запуска EditMode — событие 6790; полные ответы в `reports/validation-20261014-634/`.
  `route-resolve` получателя вернул `manual / recipient_cleared`; native-уведомление недоступно.
- Независимый обзор diff и 17 новых тестов завершён. Ограничение: `view.ok` не проверяет точность позиции/курса;
  исходный принятый Quest-отчёт проверен отдельно: 16/16 поз, yaw/floor deviation 0, XZ ≤ 2,39e-7 м.
  Отчёт и ограничения — `reports/view-tour-review-20261014-codex.md` (вне Git).
- Подготовка следующего GPU-этапа сохранена в `contracts/gpu-tool-phases-notes.md`:
  точные имена, безопасные выходные ключи, временные границы и отсутствующие маркеры оружейного сценария.
  Этапы `gpu-tool-phases` и `docs` не начинались, их зависимости сохранены.
- weapon-system ответил 6747: ShotCostFrameLog/analyzer пока uncommitted и проверены только offline;
  точный кандидат на перф-ревью обещан после драйвера bots и runtime probe. Hardware PASS не заявлен.

## Решения пользователя

- 2026-10-12 «хоста на квесте не будет» — только Quest-клиент + выделенный сервер.
- 2026-10-12 «следующим этапом … можно было без необходимости будить контроллеры» → `controller-free-launch`.
- 2026-10-13 «в любом случае мы должны измерять по тест сборке, если там укладываемся в бюджет значит на прод
  сборке точно уложимся» — бюджеты по Test (Development).
- 2026-10-13 «вторая сессия не нужна quest 3 пока не включаем» — `TargetQuest3` не включать (backlog `quest3-target`).
- 2026-10-13 «если этот скрипт будет помогать … хранить не в tasks а в tools» → `Tools/Perf/`.
- 2026-10-13 `arena-layout`: «я не смотрел в шлеме - но давай примем на основании твоих анализов и вольем».
- 2026-10-14 «запуски сборки на 30 минут при ожидании очереди в 10 это перебор … в дальнейшем так делать ненадо» —
  одна пара на этап и только при изменении рантайм-кода; перед сборкой выровняться с origin/dev.
- 2026-10-14 «нам точно нужен отдельный воркер для прод сборок, чтобы кеш не сбрасывался» → launch-infra этап
  `quest-perf-pipeline/build-checkouts` (6455): Prod и Quest-Test — отдельные checkout со своей Library.
- 2026-10-14 `view-tour` — «Принять и влить» (AskUserQuestion) по прогону GREEN.

## Сделано (факты)

- `view-tour`: 8 точек Lobby × (база, куклы), 33/33 фаз, 16/16 переносов, логи GREEN (без E2ELogCapture),
  `Tools/e2e/results/20261014-022853-quest-perf-stress` (вне Git); база Lobby вне бюджета 7 мс — худшая
  center-north 10,83 мс; таблица `reports/view-tour-20261014.md` (вне Git), журнал
  `experts/performance/journal/2026-10-14-lobby-view-tour.md`; отправлено level-design 6467.
- `arena-layout`: 9/9 кукол, 7 запретных зон, `Tools/e2e/results/20261013-185319-quest-perf-stress`.
- `controller-free-launch`: `Tools/e2e/results/20261013-011525-quest-perf-stress`; Prod APK 355 с против Test 150 с.
- `scenario`: GREEN 14/14, `Tools/e2e/results/20261012-204203-quest-perf-stress`.

## Как запускать прогон

`powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-QuestE2E.ps1 -Scenario perf-stress -Apk Build\Quest\VrBattlegrounds_Quest.apk -ViewsFile tasks\quest-perf-pipeline\tools\lobby-views.json -Timeout 1200 -StopLink`
(Quest 3 по USB, Link выключен, контроллеры не нужны; adb — `C:\Program Files\Unity\Hub\Editor\6000.4.1f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`).
APK и сервер — пара `E2EPlayerBuilder.StartPairBuild("Build/e2e-pair-status.json")` в аренде worker, копировать из
`F:/CodexWorktrees/unity-editor-worker/Vr_Battlegrounds_ai/Build/` в свой `Build/` (в LFS-agent worktree `-Build` нет).

## Риски и ловушки

- Пара на worker сейчас ~36 мин (переключение Windows → Android); до `build-checkouts` не собирать без нужды.
- Пара 605 отмечена ревизией `455e9425472f-dirty` — грязный checkout worker; в условиях замера писать как есть.
- Публикация базы worker после вливания Lobby падала (6535, материалы URP); сопровождающий публикует 77ef97d15+.
  Ошибка заявки «Идёт публикация базы» — ждать; база заявки = текущая база брокера (из текста ошибки).
- Логи прогона без `E2ELogCapture` — «GREEN» по логам неполный (примечание дирижёра).
- Хаб: `inbox --owner performance --unread --as "<свой адрес>"`, ожидание — фоновая `wait-inbox`; `events --limit`
  не больше 100. Bash: `\\` в командах ломается — скрипты писать файлом.

## Зависимости

- `quest-device-run` r5 (ACK). Регистрация сценария — строка в `E2ERunner.cs` (зона launch-infra).
- Хелпер точек `E2EViews` (launch-infra, `e2e-views` merged).
