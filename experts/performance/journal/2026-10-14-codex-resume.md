# Продолжение в Codex: карта актуализирована, view-tour ждёт текущий EditMode

Хаб: `performance-onboarding/onboarding` merged; в `perf-stress-scenario` четыре первых этапа merged,
`view-tour` running, `gpu-tool-phases` и `docs` planned. Принятие обхода пользователем сохранено;
Android-компиляция на текущем worker PASS, итог EditMode ещё не получен.
`view-tour` возвращён в planned после инфраструктурного сбоя тестов; пользовательская приёмка runtime сохранена.
Ветка `claude/performance` сверена с origin/dev d3c62ca5e.

Карта исходников сверена по всем owns/watch_paths между 716743336f и ce8a365b4. Новое: observer
`StressTestRunRecord`, раскладка с закреплёнными корнями, view-tour через helper launch-infra с клиентским
`PerfViewReport`, GPU-утилита в Tools/Perf. Watch-пути GameSettings/DebugPerfReadout/occlusion/
RemoteAvatar/ShotPipelineBudget в этом диапазоне не менялись. QualitySettings содержит принятые группы
LobbyBand0..8 с нулевыми bias; обещанная замена на DetailBand остаётся работой level-design.
`reviewed_sha` относится к карте, не к результату тестов.

Ревью нашло ограничение `view.ok`: не сравнивает отклонения позиции/курса с допуском. Конкретный
исторический Quest-отчёт проверен: 16/16 поз, XZ ≤ 2,39e-7 м, yaw/floor = 0. Согласовать точность
до изменения автоматического вердикта. Отчёт: tasks/perf-stress-scenario/reports/view-tour-review-20261014-codex.md.

FIFO удерживал служебный тикет 620 при остановленном publication-runner. Infra_issue 6678 отправлен
сопровождающему; тот возобновил очередь (6695). Предложение 622 истекло, повторная заявка 626 ждёт
предшественника gameplay 625. Аренда 626 была получена, но execute_code потерял MCP-сессию;
finish/receive выполнены без изменений файлов. В повторной аренде 634 AndroidCompileGate PASS (0 ошибок),
а запуск EditMode потерял plugin session до `job_id`/итога. После idle guard — finish/receive,
R=9a23d26f8, paths=0. Admission освобождён; infra_issue 6790 отправлен сопровождающему.
Полные отчёты: tasks/perf-stress-scenario/reports/validation-20261014-634/.
Общую инфраструктуру performance не изменял; новых заявок не оставлено. Это блокер проверки, не провал тестов.
Подготовка GPU-этапа: tasks/perf-stress-scenario/contracts/gpu-tool-phases-notes.md. Код этого этапа не начат.
