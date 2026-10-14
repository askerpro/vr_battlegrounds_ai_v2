# Стресс-тест на шлеме без рук: сценарий perf-stress, арена, обход точек обзора

Обновлено: 2026-10-14 (продолжение в Codex). Владелец: эксперт `performance`. Worktree:
`F:/CodexWorktrees/performance/Vr_Battlegrounds_ai`, ветка `claude/performance` (HEAD — последний коммит ветки;
работа этапа `view-tour` — bd7dd0127 после rebase на origin/dev 97a320c88). Начата по решению пользователя («начинай свою часть»).

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
| `view-tour` | обход точек обзора `views=<файл>`, метрики по точкам | **planned, блокер EditMode MCP**: runtime принят пользователем; AndroidCompileGate PASS (634), запуск тестов потерял соединение до `job_id`/итога; admission освобождён |
| `gpu-tool-phases` | `quest_gpu_breakdown.py --phases <имена>` — профайлер в произвольных фазах (обещано weapon-system) | planned |
| `docs` | раздел в `Docs/perf-stress-test.md` (обход, арена, без рук, бюджет сцен); файл точек → `Tools/Perf/views/Lobby.json` | planned |

## Следующий шаг (конкретно)

1. `view-tour`: после диагностики MCP-тестов (infra_issue 6790) восстановить исход потерянного запуска:
   ни `job_id`, ни подтверждённого результата не получено; вслепую запуск не повторять.
   Fetch/rebase на `origin/dev` выполнен 2026-10-14 (97a320c88, diff собственного кода после rebase пуст).
   AndroidCompileGate уже PASS на входе 38af7597 (код совпадает с веткой). Затем штатный begin этапа
   и новый подготовленный пакет worker для существующего EditMode `VrBattlegrounds.Tests.DevTools`,
   без сборки. Ожидается: все зелёные, новых 17 (8 в `StressTestPlanTests` «Обход точек
   обзора», 9 случаев в `PerfStressScenarioArgsTests` «views»). Красное — чинить тесты/код в своей ветке, повтор.
   Затем finish аренды → `verify` → `accept` → `merge-request`/`merge-execute` (fast-forward в origin/dev).
2. `gpu-tool-phases`: `--phases` в `Tools/Perf/quest_gpu_breakdown.py` (сейчас слушает только «база»/«куклы…»);
   README рядом. Нужен weapon-system для замера стоимости выстрела (метод 6156).
3. `docs`: порядок правки `Docs/perf-stress-test.md` согласовать с avatar-ik (legs-ik/`quest-perf` пишет тот же файл).

## Ждёт пользователя

- Ничего открытого по этой задаче. Уточнения и решения — через доступный инструмент вопросов пользователю.

## Возобновление 2026-10-14

- После уведомления worker_ready_resume 6805 обновлён собственный маршрут (epoch 6), inbox обработан.
  Восстановление общей очереди не объявляет исправленным частный MCP-сбой тестов 6790.
  Диагностическая заявка 645: прочитать сохранённые TestJobsV1/CurrentTestJobIdV1 в собственной аренде,
  сохранить полный отчёт и сразу finish/receive. Вход 4b76171687, база 97a320c88.
  Новые тесты в этом пакете не запускаются; отсутствие записи не доказывает, что старый запуск не состоялся.
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
