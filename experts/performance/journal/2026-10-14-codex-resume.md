# Продолжение в Codex: карта актуализирована, view-tour ждёт текущий EditMode

Хаб: `performance-onboarding/onboarding` merged; в `perf-stress-scenario` четыре первых этапа merged,
`view-tour` planned с блокером MCP-тестов, `gpu-tool-phases` и `docs` planned. Принятие обхода пользователем сохранено;
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

После уведомления 6805 о восстановлении worker выполнен fetch/rebase на origin/dev 97a320c88;
собственные owns/watch исходники до/после совпадают, карта reviewed_sha обновлена до 5cfc55248.
Заявка 645 подготовлена только для чтения сохранённого исхода потерянного EditMode (TestJobsV1),
сразу release после пакета. Общий worker ready не означает, что тесты 634 завершились или прошли.

Запрос level-design 6813 обработан независимым bounded source-review, ответ в хабе 6875.
Quality 9 names/18 zero, 140 meta только group rename; стабильный snapshot cc485d9bd + diff895a39b7…,
155 файлов drift0. Кодовых блокеров нет; независимое disjointness140/112 требует export protected GUID.
Apply выделяет коллекции на событиях; UI/restore/Quest gates открыты. Полный отчёт:
tasks/performance-onboarding/reports/scene-detail-6813/source-review.md.
