# Пакет текущей проверки сетевых исправлений

2026-10-15, network / Codex. Совместная проверка `network-sync-hardening` и `precache-server-guard`.
Код не принят; этот документ готовит действия и освобождение аренды, но не выдаёт допуск.

## Входное состояние и доказательства

- Принятая база после fetch: `d3c62ca5e12d9beeceb57954c8ad751e9690d60d`; HEAD до актуализации документов:
  `edcfca552d01dcc6a46e4f56f66deb51bfe5bf93`, 0 behind / 7 ahead.
- Текущие исправления: precache `59262d43b`, SDK 80 `ce7b348eb`, Mirror 4 `6d66c53e2`.
- Старые SHA до rebase: соответственно `f645d72f9`, `859b06777`, `ccfaf5991`.
- Ограниченный статический обзор: `reports/2026-10-14-resume-audit.md`. Нового подтверждённого дефекта не найдено.
- Свежие проверки: `git diff --check origin/dev..HEAD` — exit 0;
  `check_text_eol.py --repo . --range origin/dev..HEAD` — `ok=true`, `issue_count=0`, `commit_checked=22`,
  два пути пропущены как не подпадающие под LF-policy. Это не компиляция.
- Library, Temp и корневые csproj в этом checkout отсутствуют. Старые офлайн-компиляции и CoreCLR-сравнение 351
  случаев из предыдущей сессии не выдаются за результат текущего дерева, Mono или IL2CPP.
- Объявление 6805 сняло СТОП 6539 и разрешило штатные заявки/ожидание FIFO. Тикет 610 ранее отменён.
  DelayCall-предупреждение само не запрещает admission; текущую готовность проверяют _ready/begin.
- Read-only broker status текущей сессии: Editor PID 96696 жив (heartbeat 0.9 с), active/offered=null,
  blocked/recovery_required=false, modal=false. Но `EDITOR_DELAYCALL_STALLED`: два чтения дали 93.1 и 146.4 с,
  последнее — probes=55/fired=7. Готовность к MCP не подтверждена. Отчёт — `reports/2026-10-14-broker-status.json`;
  проблема передана сопровождающему (6675). Worker самостоятельно не запускался и не восстанавливался.

## Подготовленные действия после объявления готовности

1. Обработать inbox, проверить актуальный mode и условия сопровождающего. Fetch origin/dev; при отставании
   сохранить локальные изменения и выполнить штатный rebase. После rebase снова проверить SHA и собственные LF.
2. Создать технический checkpoint текущего дерева. Через актуальный CLI и новый idempotency key оформить одну
   заявку для двух задач; старый `precache-server-guard-guard-1` не переиспользовать. Далее watch-ticket → claim →
   begin → guard. Проверить worker root, точный SHA, RUNNING lease и маршрутизацию своего MCP-прокси.
3. Дождаться завершения Unity import/компиляции и Mirror weaver; сохранить фактические ошибки и текущий SHA.
4. Выполнить `VrBattlegrounds.EditorTools.AndroidCompileGate.Run()` через execute_code. Вход найден в
   `Assets/Editor/VR_Battlegrounds/Debug/AndroidCompileGate.cs:80`. Сохранить Result.Passed/Errors и маркер
   `[AndroidCompileGate]`; наличие маркера или success инструмента не означает PASS. `RunBatch` не применять
   к worker: этот вход завершает Editor.
5. Запустить существующие EditMode проверки assembly `VrBattlegrounds.Tests.EditMode`: namespace-фильтры
   `VrBattlegrounds.Tests.Network` и `VrBattlegrounds.Tests.Weapons`. Свежую schema run_tests прочитать перед
   вызовом; ждать завершения get_test_job, сохранить totals, failed, ignored и конкретные исключения.
6. Не менять тесты, сцены и игровые настройки ради зелёного результата. Полные отчёты сохранять в reports/;
   в контекст возвращать passed, счётчики, до 10 примеров и пути. Использовать штатный compact-result.
7. Дождаться завершения всех активных операций, убрать только свои временные изменения и сразу finish → receive,
   включая случай проваленной проверки. Диагностика, следующая правка и ожидание человека — после finish.

Подготовленный пакет заканчивается компиляцией, Android gate и существующими EditMode тестами. Play/runtime и
физическая приёмка выполняются отдельно после анализа результата, с новым подготовленным пакетом и арендами.

## Существующее покрытие и приёмка

Узкий перечень из статического аудита — 25 случаев по исходникам, не результат Test Runner:
`HeadlessPrecacheGuardTests`, `DespawnedObjectEventFilterTests`, `AvatarStateEventGateTests`,
`NetworkStateRelayTests`, `AuthorityRequestForDestroyedTests` (namespace Network), `LedgerSpawnReadyTests`
(namespace Weapons). Общий запуск Network дополнительно включает `PlayerCalibrationOwnerTests` и guards
`RpcCarriesNoStateTests`, `StateSnapshotCoverageTests`, `RemoteCallHashTests`.

Приёмка precache: сервер с графикой + 8 ботов — нет роста копий прогрева и памяти; host/client сохраняют прогрев.
Если один UxrManager переживает смену server-only → host/client, проверить возврат штатного прогрева: текущий
guard пишет false и не восстанавливает true. Такой жизненный цикл менеджера пока не подтверждён; это условный
риск, не установленный дефект.

Приёмка Mirror: dedicated-server-arsenal и смена тела/скина host+client, server+client — 0 ошибок
`UpdateTeleportState`/`UxrComponentNotFoundException`. Проверить local/remote avatar, scene-object disable/reset
и custom unspawn/pooling, где используются: callbacks остановки ещё видят spawned, callbacks уничтожения уже нет.

Приёмка SDK 80: Development/Release плеер без DebugMode — нет per-event логов UXR; DebugMode или
`-uxr-log-verbose` возвращает исходные уровни. Выстрел, хват, перезарядка и поздний вход проходят на Quest.
Editor-проверка не доказывает установку политики в плеере или IL2CPP-поведение кеша.

## Тесты после пользовательской проверки

Существующие assert двухаргументного ShouldPrecache остаются корректными для rendering-only helper, но описание
`HeadlessPrecacheGuardTests` устарело и нет server-only/sceneLoaded/composition покрытия.
`DespawnedObjectEventFilterTests` моделирует server spawned.Remove вручную; не проверяет реальный client Destroy.
Добавить матрицу холодного/тёплого кеша, перегрузок/null/наследования и ошибок рефлексии; лог-политику 10 категорий;
StateSyncFrameStack, NetworkTransformKeyframe, UniqueIdStaleRegistry — в согласованном regression-tests этапе.
До приёмки пользователя новые тесты и изменения ожиданий не выполняются.

## Пакет Windows E2E после проверки 643

Проверка 643 завершена: input `01e8b7748dfb`, compiled metadata и четыре SHA256 совпали, Android PASS/0 ошибок,
EditMode Network/Weapons 429/429 PASS, 0 failed/skipped. Finish/receive: DONE, paths=[], unexpected=[],
result `29583b14aa46c38e0410a831e32b07c17b70e742`. Продуктовый код и тесты между проверкой и сборкой не меняются.

Отдельный подготовленный пакет: capture activeBuildTarget/group → `E2EPlayerBuilder.Run()` → полный result
(Passed/Path/Revision/RevisionWarning/Message/Summary) → экспорт **всего** worker `Build/e2e/` в новую папку
своего `Build/network-sync-hardening-<input>/` → восстановление исходного build target/group при изменении →
ожидание окончания импорта/компиляции → немедленно finish/receive, включая отказ сборки. До заявки — fetch и
rebase при отставании, checkpoint и проверка совпадения Assets-tree с input 643. Код smoke-kit не импортируется.
Рабочий каталог и input должны совпасть с собственным guard. Новые scene/assets/settings руками не правятся.

Builder уже есть в принятой базе `97a320c88`, фиксированный output `Build/e2e/VrBattlegrounds.exe`.
Не подменять BuildRevision аргументом; проверить фактическую Revision кандидата, исходник Mirror и сборку.
Из worker только читать сгенерированный build и копировать в свой checkout; ignored binaries в Git не добавлять.
При неизвестном исходе вызова не повторять Run: сначала durable execute output, Editor.log и готовность/файлы.

### Фактический результат 651 и E2E

Worker 651 завершён и освобождён: input `8432b0ce35e6`, тот же Assets-tree, Windows BuildReport Succeeded,
0 ошибок / 105 предупреждений. Ответ Run оборвался; повторного запуска не было. Успех подтверждён native
BuildReport и durable execute history с revision кандидата. Времена BuildSummary после ToUniversalTime не
используются как доказательство свежести: DateTime Unity может иметь Kind=Unspecified. Экспортированы все 386
файлов в `Build/network-sync-hardening-8432b0ce-651`; exe и три основные DLL сверены по SHA256.

Finish вернул R `b11d19aa57df1c0a85f08577fc7f7b23e83b3399`, accepted=false: неожиданный
`Assets/URPDefaultResources/PC.asset` (три поля shader prefilter). Этот ассет не импортирован, receive/ack не
вызывались; результат сохранён для решения сопровождающего и владельца performance. Исходная платформа возвращена.
Performance 7025 после source review URP: discard только эти три derived editor prefilter поля, не переносить
R-delta в canonical source; authored light/shadow settings не менялись. Штатное закрытие R передано сопровождающему.

`Tools/e2e/results/20261014-211134-dedicated-server-arsenal`: все 24 функциональные проверки прошли,
строгий LogVerdict RED: четыре группы / шесть ошибок на сервере, SetAvatarRenderMode и SetCurrentHandPose.
UpdateTeleportState error=0, dropped=0, native crashes=0. Все три роли: appVersion `0.1.0+8432b0ce35e6`,
buildGuid `139612a59cb641c091b884182b2373ef`. После прогона собственные процессы отсутствуют, UDP 51831/51832 свободны.
Это Windows ServerOnly+2 clients, не Quest и не полная приёмка. Отчёты `worker-651-*.json` и исходные E2E JSON.

Повтор неизменного плеера `20261014-212454-dedicated-server-arsenal`: GREEN/enforce, functional24/24,
unexpected0/knownDefects0/native0/dropped0, тот же appVersion/buildGuid. Собственные процессы завершены,
UDP57031/57032 освобождены. Первый RED не объявляется закрытым. Source-аудит не нашёл серверной защиты retired
avatar до decoder; локальный spawned filter защищает только исходящую сторону. Решение затрагивает lifetime
envelope/admission либо scoped decoder/tombstones, требует отдельного архитектурного выбора без подавления логов.
Отчёт `reports/retired-avatar-event-architecture.md`. Baseline-повтор запрошен владельцу существующего плеера.
Gameplay 7027 подтвердил отсутствие общего retirement-сигнала или принятого этапа; ReplaceBody/disconnect
освобождают перед Destroy, смена карты идёт через Mirror, dead-body release тело не удаляет. Новое решение не начато.

## Следующий отдельный пакет: сервер с графикой и восемью ботами

Только существующий PlayLaunch API, без новых тестов/компонентов/recorder и без импорта незавершённого SDK81.
После свежего fetch/checkpoint/request/claim/begin/guard проверить root/input, idle, clean scenes, prefab stage,
отсутствие чужого launch, batch=false, graphics!=Null. Если worker без графики, завершить пакет как неподходящий.
Сохранить baseline Configuration/startScene/scene setup, точные profile bytes, EditorPrefs presence/value,
SHA256 EditorSettings и EditorBuildSettings. Разовый JSON: Server, TestMap1, elimination, ClientCount=0,
BotCount=8, AutoGoLive=true, NetworkPortPolicy=auto; Plan → один Play с PlanHash → Status до Ready/Failed.

Снять раздельные read-only наблюдения при готовности и через ограниченный интервал: Server=true/Client=false,
ровно восемь Bots с восемью ActiveAvatar; UsePrecaching=false, IsPrecaching=false, childCount и живые элементы
существующего _dynamicInstances не растут. WorkingSet/PrivateMemory — отдельные наблюдения стандартного API,
не пороговый perf-тест и не доказательство стабильности памяти на длинном интервале. При runaway/ошибке
сохранить исход и остановить свой run. Затем Cancel только своего RunId/owner, ждать Idle/Playing=false/
CleanupPassed=true/RequestActive=false, сравнить baseline, дождаться завершения операций и сразу finish.
Receive выполнять только для результата в разрешённых собственных путях; побочные foreign assets не принимать.

### Фактический результат 658

Пакет выполнен на input `5c5329ec17ba33fafe409c0273b135fc6a81f287`, Assets-tree тот же 643/651.
Direct3D11, batch=false, ServerOnly, TestMap1; Ready с valid MapRunKey, Bots=8/ActiveAvatar=8.
Три отдельных снимка за 92,36 с: UsePrecaching=false, IsPrecaching=false, childCount=0, known/live copies=0;
поле _dynamicInstances найдено, словарь не создавался. Guard-log: «сервер без своего игрока — прогрев UltimateXR
выключен», зарегистрировано 14 выключенных компонентов с id. SDK81 отсутствует в input.

OS Get-Process PID41776: за 54,35 с working set 4213,50→4233,00 MiB, private 10472,22→10472,09 MiB.
Unity Mono Process memory API вернул 0; эти значения не использованы. Нет порога/долговременного perf PASS,
это не проверка NPC-моделей/оружия и не пользовательская/Quest-приёмка.

Cancel своего run `c7db6db2d1e649d4b07a9454a7a6a9b2` → Idle, Playing=false, CleanupPassed=true,
RequestActive=false. Configuration/startScene/profile bytes/6 EditorPrefs/2 ProjectSettings hashes совпали;
edit scene setup отличается: чистая Untitled → чистая Offline. Полное baseline equality=false; infra 7048,
ручной правки общего worker не было. Finish выполнен до диагностики: R `fa8135f83e01a89f553cb6298ad3285229572c01`,
accepted=true, paths=[]unexpected=[], receive=true, аренда освобождена. Отчёты `worker-658-*.json`,
`worker-658-main.log`; компактная сводка `reports/worker-658-summary.json`.

## Локальный MCP-клиент

В helper `tools/mcp_call.py` после ревью исправлены два реальных отказа: selection.isError теперь останавливает
целевой запрос и сохраняет instance/raw selection; disconnected-awaiting-command-result после мутации отмечен
effect_unknown=true/exit2. completed означает получение MCP-ответа, а не доказательство Unity-эффекта.
Исходные отчёты 651 не переписывались; при том обрыве Run не повторялся.
`python -B -X utf8 tasks/network-sync-hardening/tools/test_mcp_call.py`: до исправления 2 failure/1 error,
после 3/3 PASS на подставном транспорте. Повторный source-review снял оба P1. Это tool-проверки без процессов,
MCP/Unity; исправленная версия helper в live Unity после этого не запускалась. Игровые тесты не изменены.

## Проект исправления поздних событий аватара

После выбора пользователя подготовлен [контракт v1](contracts/avatar-root-event-admission-v1.md).
Предлагается только scope source UxrAvatar с UxrMirrorAvatar: добавить к relay-пакету Mirror netId и уже
существующий SDK UniqueId корня. Mirror.spawned остаётся единственным владельцем доступности тела;
нового флага retirement у gameplay, tombstones и SDK-патча нет. Sender capture/revalidate после gate;
server проверяет actual-target binding перед Execute/forward, client проверяет пару ID до обычного SDK-вызова.
Цена фиксированного формата — +20 байт каждому событию, в том числе unscoped; число decode не увеличивается.

Живой root с удалённой ссылкой в аргументах и дочерние target-компоненты вне этого scope сохраняют прежнюю
строгую обработку. Отсутствие netId называется Unavailable, не доказанным retirement. Проект содержит границы
доверия серверу, map/reconnect/AOI/final-release и наблюдаемую приёмку. Пользователь 2026-10-15 одобрил проект
для подготовки [плана реализации](avatar-root-implementation-plan.md). Product/test изменений, реализации,
SDK reserve или новой Unity-заявки нет. Этап avatar-event-design — только task docs.

Отзывы: gameplay7180 — bounded no objections по release/body-swap/map/disconnect; avatar-ik7223 — без
блокеров при точной проверке CombineIdSource текущего netId (учтено в 7bc8846ba). Performance7191 уточнил
fanout и модель logical delta: 20*(Ncmd+sum actual RPC recipients)/T, SDK lookup не гарантированно O(1)/zeroalloc.
SDK GetGuid использует SHA-256 и временные массивы; план сохраняет expected Guid только на синхронный send,
без постоянного кеша жизни. Это source-review, не runtime/Quest/performance PASS и не formal contract-ack.
Fetch origin/dev 2026-10-15: 8eb87791541fda68f67bcf99d35bdffb08879d9e; в рассмотренной области относительно
97a320c88 изменился только PlayerLoadoutManager. Rebase не выполнялся; исходные runtime inputs не обновлены.

После освобождения аренды локально:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/e2e/Run-E2E.ps1 -Scenario dedicated-server-arsenal -Map TestMap1 -Clients 2 -Timeout 300 -Player <свой-экспорт>/VrBattlegrounds.exe -LogVerdict enforce
```

Порты выбирает штатный runner. Он завершает только свои PID/потомков; проверить cleanup и UDP-пару. Отчёты —
`Tools/e2e/results/<timestamp>-dedicated-server-arsenal/` плюс ссылки/сводка в reports задачи. Нужны три
completed/passed, один buildGuid/ожидаемая appVersion, exit=0, log-verdict green, все captured=true,
unexpected/defects/native/dropped=0 и отдельно 0 UpdateTeleportState/target-not-found. Это Windows runtime,
не физическая Quest-приёмка. Статический маршрут и границы freshness-check — `reports/network-e2e-route.md`.
