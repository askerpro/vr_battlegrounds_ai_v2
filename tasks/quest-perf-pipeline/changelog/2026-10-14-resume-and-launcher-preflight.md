# Возобновление Codex: статус задач и защита сборочных дирижёров

Дата: 2026-10-14. Владелец: launch-infra. Checkout: `codex/vr-test-stand`, HEAD `6a95b322e`.
После fetch `origin/dev = d3c62ca5e`; отставания нет, один локальный WIP-коммит.

## Статус и границы

- `vr-test-stand` rev36: все семь этапов merged, новый этап не выбирался.
- `quest-perf-pipeline` rev25: `e2e-smoke-kit` перед приёмкой, `build-checkouts` planned в хабе.
  Исходники последнего уже в принятой базе (`93d7dfbc3`); это не формальное закрытие его этапа.
- `map-runtime-bootstrap` rev23: generated-stations/startup-route/series-smoke merged;
  series-smoke-e2e planned до передачи gameplay после вливания каркаса. Канонический Readme прочитан
  в зарегистрированном другом checkout; записи туда не было.
- Старые Claude routes двух задач этого checkout очищены штатно. Owner inbox частично view-only
  из-за опубликованного route map-runtime-bootstrap в другом checkout. Обращение сопровождающему: 6653.
- После первого аудита получено объявление 6695: FIFO возобновлена, worker и база `d3c62ca5e`,
  штатные заявки разрешены. 629 штатно отменена до claim; 632 CANCELLED после истечения OFFERED,
  claim отказал, Unity-действий не было. **637 DONE**, input `87643327108f`, включает hello-логи
  и исправленный клиент. Пакет выполнен после штатных claim/begin/guard; worker освобождён.
  Request-base не подавался; broker/worker не ремонтировались.
- Встроенная MCP-сессия Codex завершена (Session terminated); отдельный клиент через штатный
  proxy успешно выполнил initialize и чтение editor/state/instances, 48 инструментов.
  Логи — `reports/2026-10-14-status/mcp/`; чтение не означает успешную Unity-мутацию.

## Исправления и подготовленный пакет

1. `Build-Game.ps1`: вся группа checkout проверяет удерживаемый UnityLockfile до любого переключения
   `-Ref` и LFS hydrate; поздняя проверка сохранена. Ошибка `git lfs ls-files` теперь даёт отказ exit 2,
   вместо ложного отсутствия pointer-файлов.
2. `Run-QuestE2E.ps1`: объединённый отчёт сохраняет существующие поля `serverOutput`/`questOutput`,
   читая исходный `PairStatus.serverPath`/`apkPath`. Пустые/отсутствующие поля берут уже вычисленные
   пути дирижёра; владелец конфигурации сборки не изменён.
3. `tools/e2e-smoke-kit-build.cs.txt`: подготовлен одноразовый update вместо зависающего delayCall.
   Подписка снимается до `Run`, занятость Editor повторно проверяется. Durable статусы
   queued/building/completed и отказ повторно планировать существующий файл защищают от повтора
   после таймаута. В Unity выполнен один раз, сборка прошла.
4. Исправлены устаревшие SHA/revision и описания: отдельного last-build.json builder не пишет,
   `-Ref` поддерживает только Build-Game. Обновлена карта архитектуры эксперта до HEAD WIP;
   эта отметка не означает приёмку кода.
5. В E2EBarrier добавлены GameLog.Debug.Info в точках отправки/получения hello: без них точное
   число hello на соединение из runtime-логов подтвердить было невозможно. Поведение и состояние
   протокола не менялись; входящий message.role в серверный лог не выводится.
6. Подготовлен одноразовый CLI-клиент через штатный proxy для текущей завершённой MCP-сессии Codex.
   Ревью нашло ложный effectUnknown=false при серверном timeout с raw response; после исправления
   любой отказ после отправки консервативно требует inspect. Ретраев мутации нет. Этот клиент
   работает из текущего checkout, версия из worker input для его исполнения не используется.

## Фактические проверки

- ProtocolHarness на HEAD WIP: 67 passed / 0 failed, exit 0.
- EditorBinding: 0 ошибок / 0 предупреждений, exit 0.
- Helper: компиляция точного тела с Unity DLL и заглушкой API builder, 0 ошибок / 0 предупреждений.
  Это compile-only проверка helper; фактическая Unity-сборка описана отдельно ниже.
- До исправления launcher: 3 passed / 6 failed. Реальные удерживаемые lock-файлы на первом и втором
  fixture checkout позволяли checkout/hydrate перед отказом; LFS exit 71 проходил как пустой список;
  builder paths терялись. После исправления: 9/9, exit 0. Principal повторил 9/9 на отдельной fixture.
- Сухие команды Test/Prod Quest+Server и Quest E2E: exit 0, правильные выделенные пары checkout.
  Все четыре worktree чистые, detached `93d7dfbc3`, `vrb.lfs.role=build`, по 2207 LFS-файлов,
  pointerRows=0. Library/Build отсутствуют: кеши не прогреты.
- CLI timeout fixture: прежний клиент проваливал серверный timeout (1/4); текущий — 4/4,
  включая транспортный timeout, initialize failure и успешный ответ. На каждом отправленном
  сценарии ровно один execute_code; это SDK-фикстура, не Unity.

Логи вне Git: `reports/2026-10-14-status/offline/summary.md`, `build-audit/audit.txt`,
`build-fixes/red-summary.json`, `build-fixes/green-summary.json`,
`principal-verification/principal-summary.json`. Первоначальные sandbox-отказы чтения NuGet
сохранены; успешная компиляция выполнена со стандартной эскалацией.
Проверка timeout: `mcp/timeout-regression/summary.json`; дополнительное независимое ревью
подтвердило точки hello и выявило исправленный дефект клиента.

## Остаток и приёмка

Заявка 637: worker `Vr_Battlegrounds_ai@72e96145498eb8a0`, PID41776, own RUNNING lease
и свежий guard. AndroidCompileGate PASS (errors=[]), Windows E2E build PASS за 71,773 с,
revision `87643327108f`. Полный плеер скопирован в новый `Build/e2e-worker-637/`.
Finish выполнен сразу после завершения сборки. Результат `b0fdc75a790d3b1a8eeab3670f0097f6d6656233`
содержал только побочный `Assets/URPDefaultResources/PC.asset`, accepted=false, receive не выполнялся.
Первый begin не исполнился из-за timeout автопроверки разрешения; единственный повтор успешен.

После release проведены Windows-прогоны (сервер + два клиента), все процессы остановлены:

| Сценарий | Функциональные проверки | Строгий вердикт |
|---|---:|---|
| e2e-kit-selftest | 19/19 | GREEN, exit 0 |
| map-run-startup-route | 14/14 | GREEN, exit 0 |
| dedicated-server-arsenal | 24/24 | RED, exit 1; четыре teleport-state-unknown-target |

В selftest один hello на каждое соединение, ready обоим повторён после reload /2→/3;
Unknown message id = 0 во всех трёх полных логах, 25 шагов без дублей/потерь.
Это не проверки reconnect, перезапуска сервера или клиента без E2E. Network уведомлён 6829/6850.
Map-прогон на базе с принятым ec0887cf5 чистый; исправленный anchored-ignore-collision-prefab
снят из known-noise.json по обещанию 6367. Teleport остаётся kind=defect и сохраняет RED.
В summary.logVerdict добавлен defects count: прежний known=0 не отражал четыре ошибки.

Полные доказательства: `reports/2026-10-14-status/worker-637/{android-gate,build-result,finish}.json`,
`runtime/selftest-analysis.json` и три полных каталога runtime `20261014-195458-*`, `195744-*`, `195932-*`.
Независимое финальное ревью последних правок не нашло блокирующих замечаний.
Metadata-проверка: PowerShell parse 0 ошибок, существующий log-verdict-selftest 15/15 exit0;
replay сохранённых трёх прогонов текущим JSON GREEN/GREEN/RED4, summary AST показывает defects=4.
Доказательства — `metadata-verification/verification.json` и `command-output.txt`.
Known-noise.json приведён к LF только в собственном изменённом файле.
Для добавления known-noise scope этап безопасно остановлен, зарегистрирован rev24; begin выявил
UNORDERED_OVERLAP. В rev25 build-checkouts упорядочен после e2e-smoke-kit из-за общих файлов.
Begin успешен, этап running. Gameplay получил 6892, manipulation 6896.
Network ответил 6866: фикс только в candidate01e8b7748d, собственная проверка worker643
ещё ожидается, accepted SHA нет. Существующий RED не выдаётся за проверку нового фикса.
Gameplay сообщил 6907 о принятом preview-loaded-scene в origin/dev97a320c88:
его Unity preflight3/3 и Windows build с открытой Lobby PASS. В own Docs/testing.md
описана штатная проверка загруженной чистой сцены и отказ dirty; обход пустой сценой не требуется.
Это доказательства владельца, не повторный запуск launch-infra; наша сборка87643327108f старше фикса.

Пользователь принял проверенный объём 2026-10-14: «Принять проверенный объём (рекомендуется)»
в диалоге с перечисленными Windows 19/19, map14/14, launcher9/9 и открытыми ограничениями.
Приёмка включает каркас, правки дирижёров, отчётность и снятие исправленного дефекта.
Принятый коммит78ec7e0bb перенесён чистым rebase на39afa05bd в be120d280522321f166484f29d3b3fc81a00e4a5.
В собственных source blobs каркаса/дирижёров изменений нет; после rebase повторно67/67,
EditorBinding0errors0warnings, launcher9/9, log-verdict15/15. Логи — post-rebase/.
LF проверен в34 собственных изменённых путях и Git blobs; после rebase10 физических файлов
вернулись CRLF из текущего filter policy и были точечно нормализованы, содержимое Git blobs не менялось.
Fast-forward push39afa05bd→be120d280 успешен; remote SHA подтверждён ls-remote.
Verify/accept зарегистрированы на точный rebased SHA, затем record-merged по фактическому push.
Network должен исправить дефект,
после чего нужен повтор арсенала. Физический Quest, настоящие четыре выделенные сборки и прогрев
кешей не проверены и этой приёмкой не принимаются. Runtime input87643327108f сохранён как источник доказательства.

Условные риски из аудита: fallback в текущий checkout при отсутствии выделенного пути;
реестр путей повторён в трёх местах. В текущем развёртывании fallback не сработал. Изменение
реестра/маршрутизации не выполнялось. Обрыв helper может оставить queued/building или неполный
файл: перед любым повтором inspect durable/native state, автоматический повтор запрещён.
