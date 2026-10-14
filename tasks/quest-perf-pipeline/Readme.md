# Автоматический перф-прогон на шлеме Quest и каркас E2E

Обновлено: 2026-10-14 (передача сессии Codex, событие хаба 6545). Владелец: эксперт `launch-infra`.
Worktree: `F:/CodexWorktrees/launch-infra/Vr_Battlegrounds_ai`, ветка `codex/vr-test-stand`.
Последний WIP-коммит: `049080ed5` (каркас `e2e-smoke-kit`, НЕ принят, не влит). План хаба rev 22.

## Цель

Одна команда без участия человека: собрать Android-билд → установить на шлем через adb → запустить сценарий →
закрыть приложение → забрать логи и результаты → машиночитаемый итог. Плюс общий E2E-каркас для сценариев экспертов
(вердикт по логам, мультипроцессные сценарии) и маршрут сборок.

## Границы

- launch-infra: дирижёры (`Tools/e2e/Run-E2E.ps1`, `Run-QuestE2E.ps1`), сборки, E2E-каркас (`Assets/Scripts/Debug/E2E/*.cs`
  верхний уровень), `Tools/e2e/known-noise.json`, вердикт.
- Содержание сценариев, ожидания и шум сценария — эксперт-потребитель (контракт `quest-device-run` r5). Регистрация
  сценария — одна строка в `E2ERunner` через ревью launch-infra. `Scenarios/**` — не в owns launch-infra.
- performance: метрики и бюджеты. gameplay: сценарий серии матчей. network: ревью сетевой части каркаса.

## Статус этапов

| Этап | Состояние |
|---|---|
| `design`, `device-runner`, `quest-build`, `e2e-views`, `quest-vr-manifest`, `e2e-head-pose` | merged (ef477dc2 … a311cdb1) |
| `e2e-log-verdict` | merged `0cd489fd` (принят 2026-10-13) |
| `e2e-smoke-kit` | **running**, WIP `049080ed5`, не проверен в Unity |
| `build-checkouts` | реализован в worktree + развёрнуты 4 сборочных worktree (`build-{test,prod}-{win64,android}`) |

## Следующий шаг (конкретно)

1. Дождаться, пока сопровождающий опубликует базу worker (тикет 607 FAILED из-за переимпорта 29 материалов лобби;
   материалы уже в origin/dev `77ef97d15`; request-base самим НЕ подавать, ждать объявления 6535/6539).
2. `git fetch` + rebase ветки на origin/dev (сохранить WIP). Проверить, что `e2e-smoke-kit` всё ещё `running`
   (`coordination.py status`); если допуск истёк — `stop` → `begin` (нужен `contract-ack quest-device-run 5`).
3. Офлайн: `dotnet run --project Tools/TestStand/ProtocolHarness.csproj` (ожидается 67/67).
4. Worker (штатно: checkpoint → request → claim → begin → guard): компиляция без ошибок, AndroidCompileGate, сборка e2e-плеера
   через одноразовый `EditorApplication.update` (delayCall в worker не срабатывает) — образец
   `tasks/vr-test-stand/tools/e2e-isolation-build.cs.txt`; finish (побочные `PC.asset` — ожидаемо, не принимать).
5. Скопировать плеер в `Build/e2e` worktree и выполнить
   `Run-E2E.ps1 -Scenario e2e-kit-selftest -Map TestMap1 -Clients 2 -Timeout 300`. Итог network (просили в 6425/6437):
   число рукопожатий на соединение, повторы после смены карты, 0 «Unknown message id» в логах клиентов. Плюс прогнать
   существующие `map-run-startup-route` и `dedicated-server-arsenal` (раннер теперь сам крутит вложенные ожидания).
6. Отчёт пользователю → приёмка → коммит/rebase/verify/accept/merge-request/merge-execute → request-base.
7. После вливания: сообщить gameplay (они заведут задачу series-smoke и пришлют id) → снять
   `map-runtime-bootstrap/series-smoke-e2e` с плана той задачи.

## Ждёт пользователя

- Приёмка `e2e-smoke-kit` после проверки на worker (пункт 5).
- Прогон `Run-QuestE2E` на шлеме с вердиктом по логам (пока проверен только на фикстурах) — когда шлем свободен.

## Решения пользователя

- 2026-10-11: автоматический конвейер на шлеме; шлем — настоящий клиент выделенного ПК-сервера.
- 2026-10-13: «ошибки исправлять, не фильтровать» — в `known-noise.json` только доказанно безвредный шум; известные
  дефекты `kind=defect` (owner/ticket) оставляют прогон red.
- 2026-10-13: «смок тесты серии матчей… может подготовить им инфру чтобы они могли сами написать тесты?» — сценарий
  серии пишет gameplay, launch-infra — каркас (`e2e-smoke-kit`).
- 2026-10-13: принят `e2e-log-verdict` (без прогона на шлеме).
- 2026-10-14: «нам точно нужен отдельный воркер для прод сборок, чтобы кеш не сбрасывался» + «для тест сборок я бы тоже
  создал по отдельному ворктри на платформу» (AskUserQuestion) → 4 выделенных сборочных worktree с полным LFS
  (`build-test-win64`, `build-test-android`, `build-prod-win64`, `build-prod-android`), этап `build-checkouts`.
- 2026-10-14: Quest 3 (TargetQuest3) пока не включаем — предупреждение валидатора остаётся.

## Риски и ловушки

- Сборка e2e-плеера в worker меняет `Assets/URPDefaultResources/PC.asset` → finish `unexpected`, это ожидаемо.
- Каркас: процесс без `-e2eScenario` к e2e-серверу не подключать (hello-рукопожатие это закрывает, но проверено только
  чтением кода). `VRBG_E2E_CORE_ONLY` — define только харнесса, в Unity-профили не попадает.
- Хвосты вне writes этапа (бэклог 0h): probe `managed_e2e_worker_probe.py` ждёт роль `client`; PlayLaunch не передаёт
  `-e2eArg`/позднего клиента.
- Коммуникация: `inbox --unread --as "<свой адрес>"`; ожидание — `coordination.py wait-inbox --owner launch-infra --as
  "<адрес>"` фоновой командой; в Codex вопросы пользователю — явным сообщением «ЖДЁТ ОТВЕТА» + этот раздел.

Подробности: [Details.md](Details.md) (проект каркаса и условия network/gameplay). План: [plan.json](plan.json).
Отчёты — `reports/` (вне Git; diff ревью — `reports/e2e-smoke-kit/`).
