# Стресс-тест на шлеме без рук: сценарий perf-stress, арена, обход точек обзора

Обновлено: 2026-10-15 (продолжение в Codex). Владелец: эксперт `performance`. Worktree:
`F:/CodexWorktrees/performance/Vr_Battlegrounds_ai`, ветка `claude/performance` (HEAD — последний коммит ветки;
работа этапа `view-tour` — 8cde6946a после rebase на origin/dev bb28046079). Начата по решению пользователя («начинай свою часть»).

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
| `view-tour` | обход точек обзора `views=<файл>`, метрики по точкам | **planned, два отказа DevTools у launch-infra**: runtime принят; AndroidCompileGate PASS0; полный Result688 — 139/141 PASS, все 17 новых PASS; 2 FAIL переданы 7591/7592; worker освобождён |
| `gpu-tool-phases` | `quest_gpu_breakdown.py --phases <имена>` — профайлер в произвольных фазах (обещано weapon-system) | planned |
| `docs` | раздел в `Docs/perf-stress-test.md` (обход, арена, без рук, бюджет сцен); файл точек → `Tools/Perf/views/Lobby.json` | planned |

## Следующий шаг (конкретно)

1. `view-tour`: дождаться диагноза владельцев по 7591/7592: два отказа
   `DebugBootstrapEditorOnlyTests` (editor-only BotCombatStand в build-сценах и BotCount expected7/actual0).
   Их причина/baseline пока не установлены; собственный diff этих тестов/сцены пуст.
   Чужие тесты/ассеты не менять, ожидания ради GREEN не ослаблять.
   Пакет688 получил полный Result139PASS/2FAIL/0skip; все17новыхPASS, discovery141=terminal141.
   Worker DONE/received, R30814e4c39669a9c83310bf5e3377b8f54d54a41 paths0/unexpected0.
   После исправления/согласованного решения повторить ограниченную проверку новым пакетом.
   Текущий DevTools RED; прежние634/654/670 остаются UNKNOWN, не повторяются автоматически.
   Подготовленный CLI-spec — `contracts/view-tour-test-spec-v1.json`; опубликованного CLI executor ещё нет.
   Ожидаемое подтверждение перед merge: весь DevTools зелёный, включая17новых случаев.
   Затем зафиксировать проверку и прежнюю приёмку → штатное fast-forward в origin/dev;
   в mode off — legacy pipeline, в enforced — verify/accept/merge-request/merge-execute.
2. `gpu-tool-phases`: `--phases` в `Tools/Perf/quest_gpu_breakdown.py` (сейчас слушает только «база»/«куклы…»);
   README рядом. Нужен weapon-system для замера стоимости выстрела (метод 6156).
   Уточнение8113: основной after-profile SDK80 не ждёт before8101; пакет cache+player log clamp
   не reflection-only. Метод6203/log conditions и исходные SHA внесены в contracts/gpu-tool-phases-notes.md.
3. `docs`: порядок правки `Docs/perf-stress-test.md` согласовать с avatar-ik (legs-ik/`quest-perf` пишет тот же файл).

## Ждёт пользователя

- Ничего открытого по этой задаче. Уточнения и решения — через доступный инструмент вопросов пользователю.

## Возобновление 2026-10-14

- 2026-10-15 пакет688/input11d9d62cc/basebb28046079/epoch583: явный refresh/compile завершён,
  AndroidCompileGate PASS0. Полный native discovery141DevTools/3237EditMode, все17новых.
  run_tests вернул job4d0be5f6a53a47e4965acbbb6c30b96a; terminal Failed(Child), total141,
  passed139/failed2/skipped0, детали всех141 сохранены до cleanup/finish. Множество terminal leaves
  точно равно discovery; все17новыхPASS. Cleanup только своего discovery key; finish/receive DONE,
  R30814e4c3966 paths0/unexpected0. Отказы DebugBootstrapEditorOnlyTests направлены launch-infra7591
  и bots7592. Полные отчёты — reports/validation-20261015-688/; новых Unity-заявок нет, merge не выполнен.
- 2026-10-15 диагностика7492→7502: один штатный debug_request_context PASS,
  затем resources/templates/list PASS6. Фактически server10.2.0+vr2012.de5035a57e7f;
  ordinary automaticreconnect восстановил доступ, без изменения proxy/config/worker.
  Полные ответы — reports/preflight-20261015-7469/diagnostic-7492.json.
  Новый пакет готовится на текущей базе, без повторения старыхUNKNOWN. Отчёты MCP сохраняются
  в ignored reports напрямую; полного native XML этот MCP API не обещает, CLI-spec остаётся подготовкой.
- 2026-10-15 после7457: свой route epoch11; fetch/rebase на origin/dev bb28046079 без конфликтов.
  Новая заявка не подавалась: MCP resource preflight вернул Session terminated.
  Infra_issue7469 и PUBLICATION_BUSY автоматической публикации переданы сопровождающему
  через canonical inbox+Codex; recovery/reconnect/ремонт не делали. Readonly runtime VERSION
  f6387f9dd19ff4547e7f98c53e6e4718d5735933. Полный scope — reports/preflight-20261015-7469/.
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
