# Стресс-тест на шлеме без рук: сценарий perf-stress, арена, обход точек обзора

Обновлено: 2026-10-14 (передача Codex). Владелец: эксперт `performance`. Worktree:
`F:/CodexWorktrees/performance/Vr_Battlegrounds_ai`, ветка `claude/performance` (HEAD — последний коммит ветки;
работа этапа `view-tour` — 909fb967a). Начата по решению пользователя («начинай свою часть»).

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
| `view-tour` | обход точек обзора `views=<файл>`, метрики по точкам | **running**: шлем GREEN, принят пользователем 2026-10-14; тесты написаны (909fb967a), НЕ прогнаны — worker был выключен |
| `gpu-tool-phases` | `quest_gpu_breakdown.py --phases <имена>` — профайлер в произвольных фазах (обещано weapon-system) | planned |
| `docs` | раздел в `Docs/perf-stress-test.md` (обход, арена, без рук, бюджет сцен); файл точек → `Tools/Perf/views/Lobby.json` | planned |

## Следующий шаг (конкретно)

1. `view-tour`: rebase на `origin/dev`; заявка worker (checkpoint → request → claim → begin → guard) — только
   компиляция + EditMode `VrBattlegrounds.Tests.DevTools` (StressTest*, PerfStressScenarioArgsTests, PerfLogicTests),
   без сборки (тесты рантайм не меняют). Ожидается: все зелёные, новых 17 (8 в `StressTestPlanTests` «Обход точек
   обзора», 9 случаев в `PerfStressScenarioArgsTests` «views»). Красное — чинить тесты/код в своей ветке, повтор.
   Затем finish аренды → `verify` → `accept` → `merge-request`/`merge-execute` (fast-forward в origin/dev).
2. `gpu-tool-phases`: `--phases` в `Tools/Perf/quest_gpu_breakdown.py` (сейчас слушает только «база»/«куклы…»);
   README рядом. Нужен weapon-system для замера стоимости выстрела (метод 6156).
3. `docs`: порядок правки `Docs/perf-stress-test.md` согласовать с avatar-ik (legs-ik/`quest-perf` пишет тот же файл).

## Ждёт пользователя

- Ничего открытого по этой задаче. (В Codex вопросы — явным сообщением с пометкой «ЖДЁТ ОТВЕТА» + строка здесь.)

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
