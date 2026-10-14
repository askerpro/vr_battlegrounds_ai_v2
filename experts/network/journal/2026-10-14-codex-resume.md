# Network: компиляция и 429 тестов прошли, E2E RED/GREEN, ServerOnly+8 ботов проверен

2026-10-14, network. Пользователь поручил актуализировать статус задач и продолжить работу.

- Awake и startup выполнены в назначенном linked worktree. Mode off подтверждён конфигом; ownership не менялся.
- Fetch: origin/dev `d3c62ca5e`, HEAD до правок документов `edcfca552`, 0 behind / 7 ahead. Rebase не требовался.
- Актуальные SHA: precache `59262d43b`, SDK 80 `ce7b348eb`, Mirror 4 `6d66c53e2`; Readme и планы обновлены.
- Устаревшие Claude routes очищены только у своих задач. Running guard/statesync-cost сняты штатным stop
  (6640/6644), код сохранён; этапы planned до текущей проверки и приёмки.
- Inbox обработан. Решение 6589: preview Mirror и помощник сохранения префаба после network-sync-hardening;
  ответ 6647 записан. Его auto-publication вернул PUBLICATION_BUSY, durable message сохранился;
  инфраструктурная находка адресована сопровождающему (6656), recovery не выполнялся.
- СТОП 6539 ещё не снят входящим объявлением. Запрос готовности worker адресован сопровождающему (6643);
  новых requests/аренд и фонового ожидания нет. Инфраструктура не ремонтировалась.
- Свежий read-only broker status: Editor PID 96696 жив, heartbeat 0.9 с, active/offered=null,
  blocked/recovery_required=false, modal=false; `EDITOR_DELAYCALL_STALLED` вырос с 93.1 до 146.4 с.
  Отчёт — `tasks/network-sync-hardening/reports/2026-10-14-broker-status.json`. Симптом передан сопровождающему (6675);
  это блокер подтверждения текущей готовности MCP, а не отсутствие Editor или подтверждённый product-дефект.
- Подагент сделал ограниченный статический обзор; отчёт `tasks/network-sync-hardening/reports/2026-10-14-resume-audit.md`.
  Подтверждённого нового дефекта не установлено. Условный переход server-only → host/client с тем же UxrManager
  добавлен в приёмку; существующие проверки оставляют пробелы server-only, кеша, лог-политики и client Destroy.
- Свежие whitespace/LF checks собственных коммитов и физических 8 путей документов прошли;
  компиляция, Unity, Android и Quest не выполнялись.
  Library/Temp/csproj отсутствуют; исторические офлайн-результаты не перенесены на текущий SHA.
- Общий пакет Unity проверок и условия освобождения подготовлены в `tasks/network-sync-hardening/Details.md`.
  Планы зарегистрированы в хабе: network-sync-hardening rev 5, precache-server-guard rev 2, client codex.
  Новые regression tests — после пользовательской приёмки. Принятых коммитов и вливания в этом продолжении нет.

## После объявления готовности 6805

- СТОП снят; read-only route обновлён проверкой своего чата, прежние Claude bindings остаются очищенными.
- Документационный коммит сохранён перед rebase. База `97a320c88`, HEAD `2e0255d75`, код precache `ecfbfdee7`,
  SDK 80 `74727da7a`, Mirror 4 `4224310c4`. Новые продуктовые правки и тесты не делались.
- Worker 643: input `01e8b7748dfb`, compiled metadata PASS и четыре SHA256 совпали, Android scripts PASS/0 ошибок,
  существующие Network/Weapons EditMode 429/429 PASS, 0 failed/skipped. Finish/receive DONE, paths=[]unexpected=[],
  result `29583b14aa46c38e0410a831e32b07c17b70e742`. Аренда освобождена до анализа; сводка `reports/worker-643-summary.json`
  в задаче network-sync-hardening. Сопровождающему передано 6934.
- Встроенный resources/list Codex unityMCP вернул Session terminated до аренды. Подготовлен локальный одноразовый
  клиент `tasks/network-sync-hardening/tools/mcp_call.py` через штатный launcher своего worktree: raw response
  сохраняется до сводки, неизвестный исход не повторяется. Source/helper LF проверен; общая инфраструктура не менялась.
- Условие cleanup 6527 снято source-ревью 6854 на `b00b03f33`, оговорка avatar-disappearance передана владельцу;
  они исправили её отдельно в 6906. Windows smoke-kit результат 6829 получен, арсенал 6850 имел ошибки, но не
  содержал Mirror4 (проверено Git blob `87643327108f`). Ответ 6866 durable, auto-publication дала PUBLICATION_BUSY.
- Следующий подготовленный пакет — Windows builder 651, input `8432b0ce35e6`, Assets-tree идентичен 643.
  После build/export/restore target сразу finish/receive, затем локальный E2E dedicated-server-arsenal.
  Код не принят/не влит; Quest и server+8 ботов ещё не проверены. Новые regression tests остаются после приёмки.

## Windows build и строгий E2E

- 651 завершён: native BuildReport Succeeded, errors=0/warnings=105; MCP reply оборвался, Run не повторялся.
  Durable history содержит успешный исход и revision8432b0ce35e6. Полный export386 файлов, exe/3DLL hashes совпали;
  исходная платформа возвращена, аренда освобождена. R `b11d19aa57df1c0a85f08577fc7f7b23e83b3399`
  accepted=false из-за трёх shader prefilter полей PC.asset; не получен/не ACK-нут. Shared URP не исправлялся.
  Инфраструктурный отчёт отправлен сопровождающему; receive сам запрещает accepted=false (source проверен).
- E2E `20261014-211134-dedicated-server-arsenal`: functional24/24, strictRED, четыре отклонённых события /
  шесть Error-логов, dropped0/native0, UpdateTeleportState missing-target0. Три роли имеют appVersion
  0.1.0+8432b0ce35e6 и один buildGuid. Процессы завершены, UDP51831/51832 свободны. Владельцу E2E передано6998.
- Разбор существующих raw logs связал missing dummy/root IDs с GhostAvatar224 штатной формулой CombineUniqueId.
  Сервер уже заменил ghost при возрождении, клиент отправляет RenderMode/HandPose из Start/Update.
  Это authored dummy, историческая причина NET24 не подходит; отказ до reflection cache, причинная регрессия
  новых патчей не установлена. Отчёт `reports/2026-10-14-e2e-error-diagnosis.md` задачи network-sync-hardening.
- Отдельный подготовленный graphics ServerOnly+8 bots пакет658, input `5c5329ec17ba`, тот же Assets-tree.
  Только существующий PlayLaunch и read-only наблюдения, без нового теста/recorder. FIFO; физическая Quest-приёмка pending.
- Повтор E2E неизменного плеера `20261014-212454-dedicated-server-arsenal`: GREEN/enforce, functional24/24,
  unexpected0/knownDefects0/native0/dropped0. Первый RED сохранён как нестабильное окно поздних ghost events.
  Серверного retired-avatar admission до decoder нет (отдельный source-аудит); архитектурное исправление не начато.
  Плеер/порты своего повторного прогона очищены; сопоставимый baseline запрошен у launch-infra.
- Performance 7025: owner-disposition discard только generated R-delta PC.asset; в canonical source не переносить,
  runtime light/shadow settings unchanged, поля editor-only производные URP prefiltering. Штатное закрытие R —
  через сопровождающего, не ручной receive/ack или запись worker. Own PC.asset без diff.
- Gameplay 7027: общего retirement-сигнала/flag или принятого этапа его введения нет; release перед Destroy
  в ReplaceBody и disconnect, смена карты отдельным Mirror lifecycle, release мёртвого тела не удаляет его.
  Это owner source-факты, не доказательство причины различия RED/GREEN.

## ServerOnly с графикой: 658 выполнен

- Input `5c5329ec17ba`, тот же Assets-tree, SDK81 не импортирован. Direct3D11/batch=false, TestMap1,
  ServerOnly Ready, 8 Bots/8 ActiveAvatar. За 92,36 с три снимка: precache/coroutine=false, childCount/copies=0.
  OS private memory за 54,35 с 10472,22→10472,09 MiB, WS4213,50→4233,00 MiB; не long-duration perf PASS.
  Unity Mono Process memory дал 0, эти значения не использованы. Guard-log подтверждён.
- Cancel→Idle/CleanupPassed=true/Playing=false/RequestActive=false. Profile/prefs/settings/native config match,
  edit scene Untitled→Offline: полное baseline equality=false, сопровождающему передано 7048, worker не исправлялся.
  Finish/receive: R `fa8135f83e01a89f553cb6298ad3285229572c01`, paths=[]unexpected=[], аренда освобождена.
- После finish исправлен task-local MCP helper по двум review P1: selection denial не допускает target,
  disconnected reply отражает unknown outcome. Подставной транспорт: RED до исправления, 3/3 PASS после;
  повторный source-review без findings. Это не игровые тесты и не повторная Unity-проверка helper.
- Полные сводки: `reports/e2e-651-summary.json`, `reports/worker-658-summary.json` задачи network-sync-hardening.
  Код не принят и не влит; Quest и решение по поздним ghost events остаются открытыми.
