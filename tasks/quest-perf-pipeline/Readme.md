# Автоматический прогон Quest и каркас E2E

Обновлено: 2026-10-15. Владелец: launch-infra; worktree `F:/CodexWorktrees/launch-infra/Vr_Battlegrounds_ai`,
ветка `codex/vr-test-stand`. Пользователь принял проверенный объём 2026-10-14;
fast-forward влит в `origin/dev`: `be120d280522321f166484f29d3b3fc81a00e4a5`.
Проверенная сборка: input `87643327108f`, база `d3c62ca5e`; локальные правки сохранены.

## Цель и границы

Одна команда: сборка → установка APK → запуск клиента Quest и выделенного ПК-сервера → закрытие →
логи/артефакты → вердикт. Launch-infra владеет дирижёрами, сборками и каркасом E2E.
Содержание сценариев — эксперты-потребители; метрики — performance, сценарий серии — gameplay.
Контракт `quest-device-run` r5; регистрация сценария в E2ERunner проходит ревью launch-infra.

## Статус

| Этап | Состояние |
|---|---|
| design, device-runner, quest-build, e2e-views, e2e-log-verdict, quest-vr-manifest, e2e-head-pose | merged; e2e-log-verdict принят 2026-10-13 |
| e2e-smoke-kit | принят пользователем и влит 2026-10-14, be120d280; worker **637 DONE**, input 87643327108f |
| build-checkouts | развёртывание выполнено93d7dfbc3; этап проверки running, новые инструкции готовятся; первые/повторные сборки не проверены |

В текущем diff исправлены lock перед любым `-Ref` checkout/hydrate, отказ при ошибке Git LFS и
поля путей PairStatus. Эти исправления приняты вместе с каркасом по fixture 9/9. Сборочные worktree чистые, полностью LFS,
на 93d7dfbc3; Library/Build отсутствуют — кеши не прогреты. Свежий audit 2026-10-15:
4/4 clean,8828/8828 LFS present,missing0/pointer0. Antigravity не требуется повторно создавать checkout.
Пользователь подтвердил следующий объём: реальные сборки и инструменты на этих checkout,
инструкции всех агентов с готовыми командами. Общая инструкция направляется сопровождающему;
до назначения общей пары разрешён dry-run без -Ref, batch не запускается.

## Свежие проверки

- ProtocolHarness: **67 passed / 0 failed**; EditorBinding: **0 ошибок / 0 предупреждений**.
- Launcher fixture: до правок 3/9, после **9/9**, повтор principal 9/9. Независимое ревью без блокирующих замечаний.
- Сухая маршрутизация Test, Prod и Quest E2E — exit 0; Unity/adb не запускались.
- AndroidCompileGate в Unity worker: **PASS, errors=[]**. Windows E2E-плеер собран за 71,773 с.
- `e2e-kit-selftest`: **19/19, enforce GREEN**, сервер + два клиента; один hello на соединение,
  ready повторно отправлен после reload, Unknown message id = 0, потерь/дублей журнала нет.
- `map-run-startup-route`: **14/14, enforce GREEN** на базе с исправлением `ec0887cf5`.
- `dedicated-server-arsenal`: **24/24 функционально**, но **enforce RED, exit 1**:
  четыре серверные ошибки `teleport-state-unknown-target`. Передано network: 6850.
- Добавлен счётчик `logVerdict.defects` в summary; исправленный `anchored-ignore-collision-prefab`
  снят из списка по чистому прогону карты. Неисправленный teleport остаётся дефектом.
- Проверки log-verdict: **15/15**; повторная обработка полных результатов текущим JSON —
  GREEN / GREEN / RED (4 дефекта). Summary на реальном RED показывает defects=4.
- После чистого rebase на 39afa05bd собственные source blobs не изменились: новый SHA
  be120d280 повторно проверен ProtocolHarness 67/67, EditorBinding 0/0, launcher 9/9 и log-verdict 15/15.

Unity-компиляция и Windows runtime подтверждены; физический Quest и четыре выделенные сборки
не проверены. Полные логи и результаты — `reports/2026-10-14-status/` вне Git.

## Следующий шаг

Дождаться исправления network и повторить `dedicated-server-arsenal` на принятой ревизии.
Ответ 6866: фикс пока только в candidate `01e8b7748d`, их worker 643 ждёт проверки;
принятого SHA нет, старый прогон не является проверкой этого исправления.
Каркас и правки дирижёров влиты; gameplay пришлёт id задачи серии,
после чего снять старый series-smoke-e2e из map-runtime-bootstrap. Отдельно проверить настоящие
Test/Prod × Windows/Android сборки и сохранение кешей; физический Quest — когда шлем свободен.

637 завершена штатно сразу после сборки; плеер сохранён в `Build/e2e-worker-637/`.
Все процессы трёх прогонов остановлены. Единственный побочный `PC.asset` в результате брокера
не принят (`accepted=false`), receive не выполнялся; это не отменяет PASS сборки.

## Ждёт пользователя

Проверенный Windows-каркас и инфраструктурные правки приняты ответом
«Принять проверенный объём (рекомендуется)». Полный арсенальный сценарий остаётся RED
до исправления network; физический Quest-прогон ещё не выполнен, четыре сборочных кеша не проверены.

## Решения пользователя

- 2026-10-11: автоматический конвейер; Quest — клиент выделенного ПК-сервера.
- 2026-10-13: ошибки исправлять, не фильтровать; сценарий серии пишет gameplay; e2e-log-verdict принят.
- 2026-10-14: четыре сборочных worktree Test/Prod × Windows/Android для сохранения кешей;
  TargetQuest3 пока не включать.

## Риски

Встроенная MCP-сессия Codex завершена; отдельный штатный клиент работоспособен. Сопровождающий
исследует EDITOR_DELAYCALL_STALLED. Owner inbox частично view-only из-за старого route другой задачи;
infra_issue 6653. При отсутствии выделенного пути возможен fallback в текущий checkout; сейчас не сработал.
629 отменена до claim; 632 CANCELLED после истечения OFFERED, claim отказал, Unity-действий не было.
637 завершена с диагностическими hello send/receive; результат network передан в событии 6829.
Хвосты PlayLaunch/-e2eArg/позднего клиента — бэклог 0h.

Подробности: [Details.md](Details.md), [план](plan.json),
[changelog](changelog/2026-10-14-resume-and-launcher-preflight.md). В scope e2e-smoke-kit добавлен
known-noise.json для согласованного снятия исправленного дефекта. Из-за общих GameBuilder/Docs
build-checkouts упорядочен после e2e-smoke-kit; в scope каркаса внесены принятые исправления
двух дирижёров. Чужие этапы/области не изменены.
