# Build-checkout уже созданы; проверка инструментов и общий маршрут агентов

Пользователь15.10 уточнил: существующие4buildcheckout и реализация93d7dfbc3 не делаются повторно;
следующий объём — реальные сборки/кеши и инструкции всех агентов с готовыми инструментами.
Свежий audit4/4 clean/build/full,8828LFS; Library/Build отсутствуют. Dry-run Test/Prod/QuestE2E exit0.
Own Docs/release.md и AGENT.md дополнены точными командами существующих BuildGame/RunQuest.
Новые сборщики/общие писатели не добавлялись. Evidence tasks/quest-perf-pipeline/reports/2026-10-15-build-checkouts-antigravity/.

Общие AGENTS и назначение пары — у сопровождающего: infra_issue7288+Codexnative.
Требуется согласовать RUNNING workerlease из общих правил с dedicatedbatch из Docs;
до этого только dry-run безRef/Unity. Candidate tasks/quest-perf-pipeline/contracts/build-checkouts-agent-guidance.md.
Свою инфраструктуру для обхода не создаём. Первые/повторные сборки и Quest ещё открыты.

Сопровождающий7514 подтвердил: исходный dedicated batch не требует аренды интерактивного
worker, но назначения/owner-bound резервирования пар сейчас нет. Это отдельный адресный
infra-хвост сопровождающего; восстановление GUI worker7457/7492 не выдаёт batch-capability.
Собственные матрица/критерии подготовлены в contracts/build-checkouts-validation.md;
до выпуска batch admission/reservation сохранён запрет Ref/Unity. База инструкций673 DONE.
У своей задачи штатно обновлён справочный client=codex (rev27), native route epoch7;
owner/worktree/стадии сохранены. Чужие маршруты и ACK не менялись.

Пока batch capability ожидается, по запросу performance7591 разобраны два DevTools688
отказа: legacy EditorPrefs assert вместо JSON и все Build Settings вместо release resolver input.
Result139/141 остаётся RED; production code/tests/Unity не менялись. Отдельный built-fixture
контракт BotCombatStand направлен bots. Подробности — task changelog2026-10-15-devtools-contract-diagnosis.md
и ignored reports/2026-10-15-devtools-688/. Публикация checkpoint5435b3488 через689 DONE проверена.

Прямое решение пользователя15.10 заменило запрет noRef/noUnity выше: сборки в dedicated roots
разрешены без брокера и резервирования. Перед стартом проверять процессы builder/Unity,
логи и UnityLockfile; при занятости повторять preflight по таймеру20–30мин. Оценку дешёвого
build-брокера запрашиваем у сопровождающего, нового механизма сейчас не создаём.
Передано8007+Codex; own Docs/release.md/AGENT/candidate актуализированы.
Fresh preflight4/4 clean/free; Test cold SHA74bd42f57 запущен, Android импортирует ассеты,
Win64 следует в той же команде. Это запуск, не PASS. Обычные worker leases сохраняются.
DevTools пользователь согласовал; snapshot RED1dcf2bdeeaf поверх base04fc, request721 QUEUED.
Расширение running stage корректно отвергнуто; выделен новый planned devtools-contract-tests.
Register нового stage получил database is locked, durable status остаётся rev27; issue направлен maintainer.

Обычный safe-CAS retry после8043/nativego успешен: hub rev28, новый stage planned,
публикация1f1343197f; terminal результат8049 передан canonical/native. Begin отдельно
корректно отклонён правилом одного running stage на worktree; build-checkouts не прерывался.
Docs/release.md, own AGENT и exact common candidate опубликованы docs-only934605e1f,
remote SHA проверен; сопровождающий8037 подтвердил humanрешение и готовит отдельный AGENTS.
Штатный heartbeat checkout каждые25мин проверяет сборки, молчит при неизменной занятости.
Исполнение721/RED и последующая минимальная правка guard вне lease переданы
devtools_scene_guard; leading route у root, второй claim root не делает. Полный GREEN — следующий пакет.
Независимое review тестов clean; физические own tests восстановлены LF после git materialization.
Cold-import CS0246 ShaderGraph оказались промежуточным API Updater: повторная компиляция
Tundra success/Domain Reload. Это не PASS всей сборки и не основание её перезапуска.

После8100 заявка721 claim epoch610, но begin отказал по own test compile defect:
NonParallelizable не поддерживается Unity NUnit,4CS0246. Tests не запускались, guard не написан.
Durable refreshaa12adbd... terminal ready/безактивныхопераций; один штатный finish после terminal
отклонил RECOVERY_REQUIRED. Назначенному сопровождающему переданы canonical/native IDs,
подробный результат и ошибочный literal output root /**. Source исправляется после release;
повторять старый input/recover самостоятельно нельзя. Обычный cold build продолжает работать.

Release8131:721 DONE, полныйR25d821...acceptedfalse, blocked/recoveryfalse. Исправленные own
тесты без NonParallelizable перенесены через fresh rebase на1e945a996, LFCR0; checkpoint5bfc0b6b05d,
новая заявка727 QUEUED с literal output каталогом безglob. Её исполнение передано одному
devtools_scene_guard; root не claim. После terminal предусмотрен native JSON receipt в worker
output и немедленный finish. Heartbeat уточнён: только batch-матрица, без вмешательства в DevTools leases.
На heartbeat13:16 Test Android PID188960 ещё работал, lock активен; новые build не запускались.
В13:23 лог подтверждает BuildPipeline/обработку shader variants, terminal результата пока нет.

Heartbeat13:40 подтвердил завершённый Android GameBuilder PASS/sha74bd42f5760b,
UXR748f8ac1c2e18298. APK802784194байта, SHA256d0bd5d0469c0e301c9fe32315ec8706be174c5de737c8869668f78aa74c2028b.
Полный native log/metadata/diff сохранены reports/build-checkouts-validation/test-cold-android-*.
Win64 стартовал13:34, PID215384/lock активны; pairverdict/общийexit ещё не получены.
Android sideeffects: Lobby fingerprint rebake (старыйcommitted2380eef1.. ужеstale по frozeninput,
новыйa7553c0b.. воспроизведёнstreamhashGitblobs) иUPMembeddedMCP->git вpackages-lock.
Ни snapshot/restore обоихфайлов, ниповторприdirtyдирижёр непредусматривает; gameplayуведомлён,
активныеbuildrootsнеменяются. Это неотменаPASSAPK и неготовностьповторнойпары.

13:57MSK Test cold terminal: обе платформы GameBuilder PASS, UXR совпал748f8ac1c2e18298,
дирижёр exit1 из-за consumer, ожидавшего несуществующий заголовок вместо raw firstline hash.
Исправлен только Read-UxrIdHash; реальный manifest/fixture совпали побайтно, RED15/18 →
GREEN18/18, principal18/18. Полный pair-result, два native лога, commandexit и sideeffectdiff
сохранены reports/build-checkouts-validation. После terminal/free/no-lock восстановлены
только собственные catalog/lock/Win64PC.asset из frozen74bd; Library сохранены.
Test repeat запущен14:01, exec12203; SHA неизменен, hash caller записан отдельно.
Gameplay получил canonical8197 о stale derived catalog; sourceкаталог нами не правился.

DevTools727: точный RED5/4PASS/1FAIL, fullterminalownJSON, finishsuccess/Rabd17d7f2a63.
Ignored worker receipt в R не captured (paths/artifacts[]); результат не потерян в own reports.
Generic guard добавлен после release; независимое staticreviewclean. Safe fetch/rebase на
47c0102778d сохранил ownWIP; новый input16e8089f935d и request731 полного DevToolsGREEN.
Единственный executor devtools_scene_guard; root/heartbeat второй claim не делают.

14:31 heartbeat подтвердил Test repeat terminal14:15: оба builderPASS, pipelineexit0,
UXR748f8ac1c2e18298 совпал,851.417с. Полные logs/metadata/diff сохранены; repeat diff
побайтно совпали с cold, scoped restore после free/no-lock вернул Testclean без удаления
Library/Build. Текущие Test binaries — repeat; cold binaries отдельно до перезаписи не
архивировались, cold logs/hashes сохранены. Prod cold запущен14:34/exec57313, logs143441,
runtime74bd, оба Prod root clean/free/no-lock/без Library. Никаких cache delete/брокера.
Перед Prod repeat сохранить cold Build outputs отдельно; после Prod сравнить Test cache
state hashes из test-repeat-pair-result.json. DevTools731 уже129/129PASS, finish завершён.

Пользователь15.10 поручил вливание DevTools после проверенного129/129. Отдельный commit
6669db54ff2629c716c2678f67f2c23841c60c93 FF опубликован в origin/dev; remoteSHA подтверждён,
localdev mirror обновлён без переключения human scene-detail. Полные Assets/Packages/
ProjectSettings trees после rebase идентичны tested input731; EOLrange issues0. Commit4source/
meta+2taskdocs; reader и batchWIP сохранены отдельно. Worker-base737QUEUED, DONE ещё не доказан.
Hub record-merged ожидаемо отказал для plannedstage; после batch нужно штатно оформить
DevTools verify/accept/record-merged, не прерывая активную сборку ради lifecycle.
Performance получил canonical8309. Scene-detail8290/8303 просит самостоятельный Test и
Windows-first rendered GPU маршрут; recipe в own reports подготовлен, Windows дополнение
делегировано read-only build_launcher_fixes. Новый runtime/build/инфрамеханизм не запускался.

16:11 heartbeat: Prod cold terminal15:59:38, обе платформыPASS/exit0,5128.277с, UXR совпал.
230Quest/Server outputfiles полностью архивированы в taskreports/artifacts/prod-cold с проверкой
каждого source/copy SHA256. Полные logs/exit/manifests/diff сохранены. Generated diff совпали
с Test cold; после terminal/free/no-lock выполнен scopedrestore к74bd, Library сохранены.
Последний Prod repeat запущен16:15:48/exec1330/logs161619, SHA и callerAB74B435 неизменны.
После него — fullresult, проверка сохранения Test platform caches, штатная остановка heartbeat.
ПубликацияDevTools737DONE/published6669db54f поcanonical8336; freshstatus blocked/recoveryfalse.

16:36heartbeat подтвердил финальный Prod repeat terminal16:33:33: оба builderPASS/pipelineexit0,
1064.570с. Все8builderPASS, исходный Testcoldpipelineexit1 остаётся RED;3последующихpipelineGREEN.
Матрица,8fullnative log hashes,artifact manifests и finalcheckoutstate сохранены. Prod cold230
и Test/Prod repeat464files архивированы со сверкой каждогоSHA256. Все4root после scopedowncleanup
clean/free/no-lock на74bd, Library/Build сохранены. Test platformhash/Librarycreation до/послеProd
совпали. Readerfinalreviewclean; physicalQuest/GPU/runtime не проверялись.
Buildstage штатно stop после terminal; plan29 упорядочил DevTools после buildverified для
нормального оформления уже принятого6669. Readerfix/sourcecommit ещё ждёт пользовательской приёмки.

Итоговое уточнение: plan30 исправляет порядок публикации на DevTools merged → build-checkouts.
DevTools штатно verified/accepted/record-merged6669db54f, event8442; build-checkouts
verified0bef1d608, accepted/merged отсутствуют. Receipts reports/lifecycle-reconcile/.
Все четыре repeat native log подтверждают Local cache hits (cache-reuse-evidence.json).
Heartbeat checkout остановлен штатно, statusPAUSED; повторных сборок не требуется.

Пользователь15.10 ответил «Принять и влить» на пакет readerfix,9новых fixture cases и отчётность
матрицы. Разрешены принятый коммит и штатное FF-вливание; физический Quest/GPU остаются вне приёмки.
