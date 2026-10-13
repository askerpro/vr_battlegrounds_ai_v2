# Этап shot-pipeline-budget: стоимость выстрела на Quest

Проект 2026-10-13 (субагент Plan, только чтение). Метод — performance, хаб 6156 (обязателен); план — на их ревью
до прогона. Маркеры — этап `shot-cost-probe` (SDK-патч 79, `changelog/2026-10-13-sdk-79.md`).

## Цель

Снять на Quest 3 стоимость выстрела по маркерам `Uxr.Shot.*`, `Uxr.Source.*`, `Uxr.Wm.*`, `WS.*` у автора,
наблюдателя и сервера; прогрев (выстрелы 1–3) отдельно от установившегося режима. Бюджет — после цифр (ориентир
performance: кадр с выстрелом ≤ 13,9 мс при игроке + 9 удалённых; установившийся выстрел без аллокаций; пик
первого выстрела убран прогревом).

## Ограничения, найденные при проектировании

- Имя E2E-сценария `shot-pipeline-budget` занято (`ShotPipelineBudgetScenario.cs`, T-24, loopback). Новый
  сценарий — `shot-cost`; этап в хабе остаётся `shot-pipeline-budget`.
- Клиент-шлем не может стрелять автоматически: `WeaponSystem.RequestAutomationPreparation` требует
  `IsWorldAuthority`; программный хват без grip отпускается в том же кадре (`ArsenalItemGrabScenario.cs:465-470`);
  `xr-input` launch-infra без срока. Для (а)/(б) стреляет человек по инструкции, сценарий пишет кадры (автор —
  локальный игрок, замер честный).

**Решение пользователя 2026-10-14: сначала только бот.** Первая очередь этапа — сценарии (в)/(г): бот стреляет
на ПК-сервере, шлем — наблюдатель (сервер даёт стоимость выстрела автора-бота, шлем — стоимость чужого выстрела).
Сценарии (а)/(б) со стрелком-человеком — после `xr-input` launch-infra; формат записи общий, сценарий их уже
предусматривает параметром `mode=shooter`.

## Метод (6156)

- Сборка: Test-пара (Development, IL2CPP Release) через `E2EPlayerBuilder.StartPairBuild` на worker из
  `claude/weapon-system` с патчем 79; Prod не нужен.
- Топология: шлем-клиент + выделенный сервер на ПК (`Tools/e2e/Run-QuestE2E.ps1`, контракт quest-device-run r5).

## Сценарии (`-Scenario shot-cost -Map Lobby`, `-Arg`)

| Код | Кто стреляет | `-Arg` | Поза |
|---|---|---|---|
| а | человек в шлеме, AK105: 3 очереди по 30 в стену | `mode=shooter,weapon=ak105,bursts=3,burst=30` | трекинг |
| б | человек, PPK: 30 одиночных, пауза ≥ 0,5 с | `mode=shooter,weapon=ppk,shots=30` | трекинг |
| в | бот на сервере, AK105, 90 выстрелов в стену, бот в кадре | `mode=observer,weapon=ak105,shots=90,view=bot` | sim 1,6 + views |
| г | то же, взгляд отвёрнут | `...,view=away` | sim 1,6 + views |

Клиент (а/б) ждёт ствол в руке локального аватара и пишет каждый кадр до N выстрелов; сервер в (а/б) —
наблюдатель (replay + снаряд). Сервер в (в/г) — автор: бот у стены, путь `RequestAutomationPreparation` +
`TryToShootRound(0)` (механизм — вопрос к bots). Прогрев: выстрелы 1–3 каждого оружия на процесс — `warm`, с 4-го —
`steady`.

## Метрики

ProfilerRecorder как `PerfFrameRecorder` (значение кадра N−1 в начале N; буферы заранее; CSV после замера).
- Кадр с выстрелом: `Count(Uxr.Source.Projectile) > 0`; у автора сверка с `Uxr.Shot.EndSync`; сумма Count = число
  выстрелов. Кадры попадания (`Uxr.Wm.Impact.*`) — отдельно.
- По маркеру: сумма за кадр выстрела, mean/p95/max по классу (steady N ≥ 30).
- GC на выстрел: `GC Allocated In Frame` минус медиана базовых кадров сегмента (ствол в руке, ±3 кадра без выстрела
  и попадания).
- Кадр с выстрелом vs без: dt, cpu_main, scripts, scripts_late (p50/p95/max, Δp50); ovr_gpu — max по f..f+2.
- Сервер: dt, PlayerLoop, маркеры бота (`BotGunner.*`, `BotRuntimeController.*`).
- Условия к каждому числу: устройство, Test, SHA пары, FFR, уровни CPU/GPU, частота, сцена, топология, поза,
  батарея/температура.

## Файлы (writes) и согласование

| Файл | Зона | Согласование |
|---|---|---|
| `Assets/Scripts/Debug/E2E/Scenarios/ShotCostScenario.cs` (+.meta) | launch-infra (`Scenarios/**`) | zone-ack, передача в owns weapon-system |
| `Assets/Scripts/Debug/E2E/Scenarios/ShotCostFrameLog.cs` (+.meta) | launch-infra | zone-ack; ревью performance |
| `Assets/Scripts/Debug/E2E/E2ERunner.cs` (одна строка регистрации) | launch-infra | zone-ack |
| `tasks/weapon-system/tools/shot_cost_report.py` (`--selftest`) | weapon-system | ревью формул performance |
| `tasks/weapon-system/tools/shot-cost-views.json` | weapon-system | — |
| стрельба бота по точке (debug/e2e) | bots | этап и ACK bots |

Артефакты на устройстве: `persistentDataPath/shot-cost/<utc>_<роль>/{frames,shots}.csv, conditions.json`.
Сценарий красный только при «замер не состоялся».

## Шаги прогона

1. ПК (ярус C, `Run-E2E.ps1 -Scenario shot-cost`, режим в): маркеры Valid, Count растёт, CSV пишется, база GC = 0.
2. Сборка пары в аренде worker (`StartPairBuild`, ~7–12 мин), забрать APK и `Build/e2e/`, сдать аренду.
3. Слот шлема у performance (после view-tour); батарея ≥ 50 %, без Link.
4. Прогоны в, г, а, б, повтор а; остывание 2–3 мин.
5. `shot_cost_report.py <run> --out tasks/weapon-system/reports/shot-cost/<run>` → таблицы performance.

Оценка: реализация 1–1,5 дня; сборка ~30 мин; прогоны 1,5–2 ч (человек в шлеме ~30 мин).

## Готовность

GREEN-прогон на каждый сценарий; steady N ≥ 30 на ствол; число выстрелов = счётчик маркера; warm/steady разведены;
условия у каждого числа; `--selftest`; ACK performance по таблицам.

## Риски

Сборка IL2CPP на worker; шлем занят; нестабильный E2E (Discovery/Link); пользовательские маркеры могут не попасть в
`ProfilerRecorderHandle` до первого выстрела (создать `ProfilerMarker` с тем же именем до `StartNew`); отставание
ovr_*; разброс от человека (повтор а); троттлинг; сервер — Windows Mono (только относительные цифры).

## Открытые вопросы

1. ~~Пользователю: (а)/(б) — человек или `xr-input`~~ — решено 2026-10-14: сначала только бот (в/г).
2. bots: debug-приказ «держать AK105, стрелять в точку стены N раз с темпом R» или вызов из сценария
   `RequestAutomationPreparation`/`TryToShootRound` после `AddBot` + `Arm`; держит ли бот ствол в разминке.
3. performance: Lobby или TestMap2; число повторов; сырой frames.csv как артефакт; фазы для
   `quest_gpu_breakdown.py`; замер «+9 удалённых» — отдельно поверх perf-stress?
4. launch-infra: имя `shot-cost`, передача файла сценария в owns weapon-system, строка в `E2ERunner.cs`.
5. Lobby: AK105/PPK на стене и оружие в разминке; точки обзора (`tasks/quest-perf-pipeline/tools/lobby-views.json`).
