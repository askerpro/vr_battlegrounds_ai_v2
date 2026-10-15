# Подготовленный пакет проверки DevTools

2026-10-15. Пользователь согласовал изолированную проверку JSON, release resolver input
и отказ для player-несовместимых явно запрошенных automation fixtures. Bots7663 подтвердил:
BotCombatStand только Editor/MPPM; scene/asmdef не меняются.

Изменяемые файлы: BuildSceneResolver.cs; DebugBootstrapEditorOnlyTests.cs;
BuildSceneResolverTests.cs и его meta. Root добавил отдельный этап devtools-contract-tests;
исполняемый build-checkouts сохраняет свой scope и продолжает сборку.
Это продуктовый admission сборщика; broker/proxy/config не меняются.

1. RED snapshot: исправленные устаревшие тесты и новые resolver regressions, прежний
   production resolver. В собственной RUNNING worker lease refresh/compile, затем
   EditMode VrBattlegrounds.Tests.DevTools.BuildSceneResolverTests с полными details.
   Ожидается отказ именно из-за отсутствующего исключения для BotCombatStand; остальные
   проверки должны пройти. Несвязанный compile/test failure не считается подтверждением RED.
2. Сохранить полный terminal Result в own task-root. В этой же подготовленной lease
   штатным native MCP сохранить JSON receipt результата в worker-каталог
   tasks/quest-perf-pipeline/reports/2026-10-15-devtools-approved (буквальный output root),
   чтобы отчёт вошёл в R. Затем cleanup и немедленный finish, даже при RED.
   Если исход неизвестен, сначала durable inspection; не replay/clear_stuck/recover.
3. После подтверждения RED вне аренды добавить минимальный generic guard по фактическим
   Editor/PlayerWithoutTestAssemblies. Ошибка содержит сцену/assembly/script, без whitelist.
4. Новый checkpoint и собственная lease: refresh/compile; весь namespace
   VrBattlegrounds.Tests.DevTools, include_details=true. Сохранить terminal summary и все leaves,
   перечислить failures/skips. Профиль/SessionState/каталог должны быть восстановлены тестами.
5. После сохранения результата cleanup и немедленный finish; никакого ожидания человека в lease.

Полные результаты — reports/2026-10-15-devtools-approved/, вне Git. Исторические141/139/2
не заменяются новым статусом до фактического прогона. Исправление будет представлено
пользователю с реальным результатом до принятого code commit.

## Первая попытка721: compile failure, не RED

Input1dcf2bdeeaf, claim epoch610, begin отказал:4CS0246 на неподдерживаемом
NonParallelizableAttribute в двух тестовых файлах. Tests не запускались; guard ещё не менялся.
Refresh aa12adbd712a4ae0927dea25d8d9d5df подтверждён terminal, operations/tests не активны;
phase RECOVERY_REQUIRED. Один штатный finish после terminal отказал exit2, recovery назначен
только сопровождающему (infra_issue8116, recovery_handoff8121, private capability передана
путём без вывода значения). Own атрибуты уже удалены в taskcheckout, immutable721 не менялся.

После адресного release: свежие fetch/rebase с сохранением own WIP, LF-проверка owned paths,
новый checkpoint/request/key. Output root — каталог reports/2026-10-15-devtools-approved,
без суффикса /** и без glob. Старый input721/запуск не повторяется. Ни compile failure,
ни снятие атрибута статически не доказывают TDD RED или GREEN; нужен полный новый Result.

Release8131 подтверждён:721 DONE/R25d8215225e5715b6261dd0567cfe3ebb507dab1 accepted=false.
После fetch/rebase и LF checks новый RED2 input5bfc0b6b05d47bfbca1f727eaf4020aa50327ebb,
base1e945a99673b9726773242f690fcea2032cae2c6. Worker727 QUEUED; единственный исполнитель
devtools_scene_guard, leading route/inbox остаётся root. Новый output literal directory сверён.
727 завершена штатно: ожидаемый RED5total/4PASS/1FAIL/0skip, job4c983604bf244007a36ab24db0a2d4bd,
finish/Rabd17d7f2a6314459493c0912859caf2515683ed. Guard добавлен только после release.
Полный JSON есть в own reports; ignored worker receipt не captured в R (paths/artifacts[]).
Не считать запись receipt доказательством его включения в R.

Полный GREEN поставлен731: input16e8089f935d977058c387688ec62bbaaa74cbd5,
base1e945a996, own HEAD47c0102778d после safe fetch/rebase. Только devtools_scene_guard
watch/claim/begin/execute/finish; leading session сохраняет inbox/route и не дублирует lease.
Фактический731 GREEN: job18b567b0dff84e33b368a83985eec52a,129/129PASS,0failed/skipped,
42.3580458с; exact namespace ^VrBattlegrounds\.Tests\.DevTools\.,129unique leaves/outside0.
Resolver5/5 и DebugBootstrap4/4 PASS. Principal самостоятельно проверил полный terminal JSON,
SHA256B6D51C609DF1C61E09D8D78839234564A799656F685613329949F2B6640D2047.
Cleanup/finish успешны, R945281d39131b6e6455d2c2b5c11a4a19dc40944,unexpected/binaryinvalid[].
paths/artifacts[]: ignored reports не captured в R, полный terminal сохранён в own reports.
Это EditMode-проверка конкретного input, не runtime/Quest и не эквивалент исторической
выборки141. Пользователь принял проверенный DevTools-объём15.10 сообщением
«если девтулзы исправлены - вливаем?». Публикуются только resolver, два тестовых source
и meta с документацией этой проверки; сборочный reader и matrix WIP в этот объём не входят.
