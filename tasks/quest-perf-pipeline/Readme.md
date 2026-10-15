# Автоматический прогон Quest и каркас E2E

Обновлено: 2026-10-15. Владелец: launch-infra; worktree
`F:/CodexWorktrees/launch-infra/Vr_Battlegrounds_ai`, ветка `codex/vr-test-stand`.
План хаба rev30. Режим coordination off; интерактивный Unity требует своей RUNNING lease.

## Цель и границы

Одна команда: сборка → APK → Quest-клиент и выделенный ПК-сервер → логи/артефакты → вердикт.
Launch-infra владеет сборщиками, запуском и каркасом E2E. Содержание сценариев — их эксперты,
метрики и GPU-инструменты — performance, анализ сцены — её автор. Контракт quest-device-run r5.

## Статус

| Этап | Состояние |
|---|---|
| design, device-runner, quest-build, e2e-views, e2e-log-verdict, quest-vr-manifest, e2e-head-pose | merged |
| e2e-smoke-kit | принят14.10 и влит be120d280; Windows selftest19/19, map-startup14/14 GREEN |
| build-checkouts | принят15.10 и FF влит e83af8413; reader fix,9новых проверок и отчётность, матрица8/8builder PASS |
| devtools-contract-tests | merged6669db54f, принят15.10,129/129PASS; публикация737DONE; lifecycle закрыт штатно |

## Четыре checkout и фактическая матрица

Развёртывание93d7dfbc3 уже было выполнено; повторно checkout не создавались. Начальный аудит:
4/4clean,8828LFS present,missing0/pointer0. Выделены постоянные пары
`F:/CodexWorktrees/build-{test,prod}-{win64,android}/Vr_Battlegrounds_ai`.
Library создавались первым проходом и сохранялись между проходами.

Все8build на одном runtime SHA `74bd42f5760b5e3f04d2e886a5685a2f52ff3d1d`.

| Проход | Android / Win64 builder | Pipeline exit | Время пары |
|---|---|---|---|
| Test cold | PASS / PASS | 1 — исходный дефект UXR reader | 90мин22с |
| Test repeat | PASS / PASS | 0 | 14мин11с |
| Prod cold | PASS / PASS | 0 | 85мин28с |
| Prod repeat | PASS / PASS | 0 | 17мин45с |

Последний terminal16:33:33MSK. UXR во всех8build одинаковый: `748f8ac1c2e18298`,
9681id/359files. Время — wall-clock сборок, не Quest FPS или performance gate.

Первый дирижёр ожидал заголовок `# sha256:`, producer пишет raw16hex первой строкой.
Минимально исправлен только Read-UxrIdHash; writer/manifest не менялись. Регрессии:
RED15/18 → GREEN18/18, principal18/18; независимое review clean, realmanifest9/9 cases.
Три последующих реальных pipeline exit0 используют caller SHA256
`AB74B435E3AA879AC447AA76CCD8EE34FE740FC164EDFD9A95B60378BA49C1BD`.
Reader принят пользователем15.10 отдельным пакетом и FF опубликован
`e83af8413de0ebd726df27a7321f23131c9cf073`; remote SHA проверен, localdev mirror обновлён.
После rebase все8путей равны принятому05b1b5b33; итоговые fixture18/18, EOLrange issues0.
В DevTools commit reader не входил. Повторная fixture:
[accepted-summary.json](reports/uxr-reader-accepted/accepted-summary.json).

Полный итог: [matrix-result.json](reports/build-checkouts-validation/matrix-result.json),
[final-checkout-state.json](reports/build-checkouts-validation/final-checkout-state.json).
Полные8native logs, commands/exit, fingerprints, artifact hashes и generated diff сохранены.
Архивы Prod cold230files, Test repeat234files, Prod repeat230files проверены source/copy SHA256:
[artifacts](reports/build-checkouts-validation/artifacts/). Test cold binaries отдельно до repeat
не копировались; для них сохранены полные логи/manifest/metadata hashes.

После terminal и fresh no-process/no-lock восстановлены только собственные заархивированные
catalog/UPM-lock/Win64URP generated diff; чужие изменения не затронуты. Все4checkout clean
на74bd, Library/Build сохранены. Test Library creation times и platform-state SHA256 совпали
до и после обоих Prod проходов; кеши Test не пересоздавались и платформа не менялась.
Все четыре повторных native log содержат положительные Local cache hits, включая compiled0variants:
[cache-reuse-evidence.json](reports/build-checkouts-validation/cache-reuse-evidence.json).
Heartbeat checkout штатно остановлен (PAUSED) после сохранения результатов.
Gameplay8213 подтвердил границы cleanup; source callback/catalog — их отдельный кандидат
catalog-build-derived-state, ручной правки YAML нет.

## Принятый DevTools

Commit `6669db54ff2629c716c2678f67f2c23841c60c93` FF опубликован в origin/dev по прямому
решению пользователя15.10. Remote SHA проверен, localdev mirror обновлён без переключения
пользовательской scene-detail. Commit содержит4source/meta и2taskdocs.

Проверены JSON roundtrip в изолированном профиле, реальный release resolver input и отказ
явно запрошенной automation-сцене с компонентом из Editor-only assembly. BotCombatStand остаётся
Editor/MPPM, его scene/asmdef не менялись. Точный RED727:5/4PASS/1FAIL; затем guard.
GREEN731/input16e8089:129/129PASS,0fail/skip, job18b567b0dff84e33b368a83985eec52a.
Cleanup/finish успешны, R945281d39131b6e6455d2c2b5c11a4a19dc40944; full terminal в
[reports/2026-10-15-devtools-approved](reports/2026-10-15-devtools-approved/).
Ignored reports в R не captured. После rebase полные Assets/Packages/ProjectSettings trees
равны проверенному input; EOL issues0. Публикация базы737DONE/published6669db54f по8336.
Новый namespace129 не объявляется эквивалентом исторических141/139/2.

Историческая721 завершена сопровождающим после compile refusal; её не повторять. Подробности
в [devtools-validation.md](contracts/devtools-validation.md). Пока build-stage был running,
DevTools begin не допускался. План30 задаёт фактический порядок публикации: DevTools merged,
затем build-checkouts. Штатные verify/accept/record-merged DevTools выполнены без обходных флагов;
build-checkouts первоначально verified0bef1d608; приёмка15.10 и публикация e83af8413 выполнены. Receipts:
[lifecycle-reconcile](reports/lifecycle-reconcile/13-final-own-status.json).
Итоговый build-checkouts record-merged8480 успешен: verified-content, content_drift0,
dependency_gaps0; [accepted-record-merged.json](reports/build-checkouts-validation/accepted-record-merged.json).

## Инструкции и межзадачные ответы

Решение пользователя15.10: ordinary dedicated builds без брокера/резервирования. Перед Ref/Unity
fresh process/log/UnityLockfile preflight обоих root; busy/unknown → native timer20–30мин,
без второго процесса, изменения SHA или удаления locks. Library сохраняются. Интерактивные
worker/MCP leases остаются обязательными. Новый механизм сборочного брокера не внедрялся.

Own Docs/release и launch-infra instructions опубликованы8eb877915/934605e1f; dryrun3/3.
Сопровождающему переданы8007/8039: общий AGENTS candidate и оценка дешёвого build-брокера;
подтверждения общего выпуска/нового решения пользователя пока нет.

Scene-detail получил8313/8325: существующий E2E Windows rendered-client route, команды
E2EPlayerBuilder.RunBatch → Run-E2E и пределы PC/Quest; полный static recipe в
[scene-author-recipe.md](reports/build-checkouts-validation/scene-author-recipe.md).
Новых профилей/запусков GPU не было. По ответу8394 performance уже опубликовал view-tour
3b0f41b62: его combined741 AndroidCompileGate PASS,146/146PASS, включая17view-tour.
Старый static recipe описывает прежний input; launcher Map/views options и загрузка context.Map
остаются отдельными стыками. Headless GameBuilder.Server не подменяет rendered PC-клиент.
Performance получил DevTools результаты8230 и принятое вливание8309.
Завершение матрицы и освобождение checkout переданы performance8453, scene-detail8455,
сопровождающему8457; оценка build-брокера и общий AGENTS candidate остаются у сопровождающего.

## Осталось

Проверить FIFO-публикацию принятой базы перед следующей worker-заявкой.
Далее отдельный собственный dedicated-server-arsenal на принятом network1e945a996:
исторический наш24/24functional/enforceRED4teleport не заменяет новый прогон, чужой713GREEN24/24
не является нашим подтверждением. Frozen74bd старше network-исправления.
Gameplay должен прислать id новой series-smoke задачи; старую map-runtime-bootstrap здесь не менять.
Физический Quest, GPU/FPS и ввод контроллеров этой batch-матрицей не проверялись.
TargetQuest3 по решению14.10 остаётся false.

Подробности: [Details.md](Details.md), [plan.json](plan.json),
[build validation](contracts/build-checkouts-validation.md),
[DevTools accepted](changelog/2026-10-15-devtools-accepted.md).
