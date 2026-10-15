# Боты: player-flow и Blaze AI

Обновлено: 2026-10-15, Codex. Владелец `bots`, задача `bots-fix`, worktree
`F:/CodexWorktrees/bots/Vr_Battlegrounds_ai`, ветка `bots-fix`.
[План](plan.json), [архитектура](player-flow-plan.md), [cc-1](cc1-plan.md), [детали](Details.md).
[Прежний подробный Readme](changelog/2026-10-14-readme-before-worker-ready.md).

## Статус

- 2026-10-15 пользователь поручил полностью завершить clip-cutover (cc-1…cc-5).
  Подготовка раннего вливания остановлена; candidate a2576cab не опубликован, активной Unity-аренды нет.
  Выполняются readiness/shot-tail/public Blaze trace и cc-2 закупки, затем новые проверки/матч/демо.
  Точные scope/решения: changelog/2026-10-15-cutover-completion.md. Старые FAIL сохраняются.
- Хаб revision52: `clip-cutover` RUNNING epoch3 (свежий permit; ACK weapon-use-context r2);
  `binding-foundation`, `pf-prototype`, `pf-equipment`, `pf-network` MERGED.
  `armed-development` ACCEPTED; `pilot-split`, `script-brain`, `pf-quest` PLANNED.
- HEAD `e812a2dfa` содержит принятую B `bb2804607`; при последнем fetch/rebase — 0 позади / 13 впереди.
  Свои правки восстановлены из сохранённого stash `a7921d04c`, rebase без конфликтов.
  По7614 общий worker позднее перешёл на B `5435b3488`; собственный687 проверял exact Bbb, это разные снимки.
  Registration base_sha остаётся498f284fb; worker669 использовал exactB546/grant4.
- cc-1: BotKit, удаление гибрида, минимальный Blaze-прокси и сторож авторства реализованы локально.
  Контроллер прокси создан Unity и получен в worktree. Вход `2d652723f`, заявка **644 DONE**,
  результат `512fcb59e`: 3 ожидаемых пути, unexpected0, finish/receive выполнены.
- Продолжение **656 DONE/receive**, input `f24eb91e1`, result `aa814d5ea`: paths0/unexpected0.
  p1 остановлен по условию CameraFade; Play выключен, launch Idle, stand owned=false.
- **664 DONE после recovery**, input `fe95dfead`, exact B39af, immutable grant3 прошёл admission.
  Исходный Refresh terminal после действия сопровождающего; canonical7178 передал bots единственный recover.
  Команда завершилась exit0, аварийный R `b624a8bcc` сохранён (paths0/unexpected0/immutable_changed0, accepted=false).
  Worker HEAD39af чист, broker blocked=false/recovery_required=false/active=null; restore ready=true,
  compile_errors=[], Play/compile/import/tests=false, Lobby восстановлена. B0..B8 на664 не выполнялись.
  Receive требует ревью аварийного результата; принудительного принятия не было. Отчёт7193/7195 доставлен сопровождающему.
- **666 DONE/receive**, input `52790c32`, result `3c344e1a7`: paths0/unexpected0/immutable_changed0.
  B0/B1 PASS; T1 job78b1247567f341a683acf172d55267ce получил wrapper initialization timeout,
  на этом снимке completed0/total=null/Result=null. По7237 native IPrebuildSetup оставался активным,
  затем пришёл поздний RunStarted: фактический итог assertions UNKNOWN. Повторного запуска не было.
  B6/B5/guard на момент finish сообщали idle; это не доказывает отсутствие отложенного native старта;
  worker вернулся наB54673ba4/Git clean, broker active=null/blocked=false/recovery_required=false.
  p1/p2 не запускались. Infra7229 вхаб и native;7237 подтвердил причину wrapper timeout.
- По7257 vendor unity-mcp2 установлен и проверен сопровождающим: живойprebuild24,9с, полныйFailedResult2
  (инфраструктурные1PASS/1намеренныйFAIL). Это не игровые тесты;666 outcome остаётся UNKNOWN.
  **669 DONE/receive**, A94f63a5e/treeffc475c8/B546/grant4, R08865a569, paths0/unexpected0/immutable_changed0.
  Runtime VERSIONeb4ab1d981654f3bcd64cd4eb546cbcc950370d6; strict admission/guard и B0/B1 PASS.
  Штатный run_tests один раз потерял plugin session11381b03-fcb4-4b04-b8aa-beef4987caef доjob_id: outcomeUNKNOWN.
  Native TestRunnerApi.IsRunActive=false до/после; SessionState currentempty/jobCount10 без нового record,
  это не доказательство незапуска. B6/B5/guard cleanup, finishexit0/receive=true, своя аренда освобождена.
  p1/p2 не запускались. Infra7305/7316 доставлены canonical+native; ждём отдельного решения поlaunch-receipt/domainreload.
- Inbox7202 подтвердил независимую проверку recovery664 и снятие паузы FIFO. Publication665 DONE/B54673ba4.
  Пустой аварийный R664 можно оставить AVAILABLE/accepted=false с сохранённым ref; force receive/ACK не требуется.
  Native route refreshed epoch29 после возобновления:
  `codex-app`, `01a13af7-2278-70a0-8070-8bbc42ec5300` (default open_in_codex + свежий list_threads).
  Предыдущие MCP session/ping и database-lock ошибки требуют свежей проверки, не считаются устранёнными по уведомлению.
  Пользователь подтвердил ручного адресата «Ответить, кто ты» (agent-infra); native уведомление7142/7154/7157 доставлено.
  Policy7160 и прямое поручение пользователя в чате сопровождающего проверены: infra_issue + native уведомление
  теперь отправляются без отдельного вопроса. Чат переименован в «сопровождающий», thread-id прежний.
  По7178 bots выполнил единственный штатный recover664; аренды666/669 завершены. Своей активной аренды нет.
  Policy7257: инфраструктуру самостоятельно не ремонтировать и не обходить; сразу canonical infra_issue + native,
  сticket/job/request, VERSION и redacted evidence. Ремонт только явно назначенным сопровождающим.
- Новое указание пользователя «попробуй еще раз»: grant5 наB8eb получен7348/7349, свойpermit продлён
  безсменыr52/epoch3. Fresh exactA8f3ae332/tree66b8787c:55current/36excluded/5proofs/2pins,56delta.
  Requestkey bots-cc1-round5-user-retry-20261015 отказал доticket: отсутствовал providerReadme.
  Infra7352/provider7354 доставленыобоимканалам; owner7357 восстановилсвоиReadme/plan, grant5unchanged.
  Read-only all677ticket snapshot:0matches ключа; maintainer7377 подтвердилno-created-ticket/право admission.
  **679 DONE/receive**: повторный request тем же ключом прошёл strict admission после восстановления provider.
  Результат `80b16a72a3a6cbbcdeabd42df0bf32f6c8bd6461`: paths0/unexpected0/immutable_changed0.
  B0/B1 PASS, native TestRunner до Play неактивен. Обе фазы PlayLaunch завершены: 9/9 случаев,
  InvalidFixture0, ошибок/исключений/предупреждений анимации0, CameraFade0. Cleanup: Play=false,
  launch=Idle, stand owned=false, bots0; finish и receive завершились exit0. Своей активной аренды нет.
  Анализатор: AK105 80/80; PPK 67/68 — FAIL точного hitRate=1. Кобура, отсутствие оружия, покупка,
  смерть/призрак/возрождение — pf-equipment PASS. Все случаи сохраняют NeedsReview.
  Не пройдены: камера/сдвиг цели T07 (0–1,65 м / 3,06 м), покрытие первого Move, все Move ≤0,2 с,
  рост normalizedTime >95%. Разбор target trace и proxy отделяет дефект игры от дефекта измерения;
  критерии не ослаблены. Полные отчёты: `reports/received679/cc1-round5/`, сводка `table.md`.
  В 11 B8-снимках containerChildCount0, экспоненциального роста памяти нет, но IsPrecaching=true
  и dynamicDictionaryPresent=false во всех снимках. Это не подтверждает завершение precache;
  evidence7436 передан владельцу SDK81 canonical+native. SDK/worker не менялись.
  Ответ владельца7439: такая комбинация согласуется с ожиданием LocalAvatar до выделения копий
  (WAIT_LOCAL); сам LocalAvatar не измерялся. В этом пакете повторные копии не наблюдались,
  полноценный прогрев/abort/latejoin остаются отдельной приёмкой владельца.
  Точный разбор target: 5602 строки, 6 начальных неверных поз, 5596 стабильных, без пропусков.
  Каждый скачок только между первой и второй строкой тела; все312 Fired имеют стабильную цель.
  Это локализует проблему окна готовности fixture и не объясняет PPK67/68; FAIL сохранён.
  Во время679 standard run_tests race был в ownerrepair;7375/7377 разрешили независимый runtime-onlyp1/p2,
  T1 иoldUNKNOWN остаются непрошедшейприёмкой. Никакогообхода/ремонтаинфраструктуры.
- По worker_ready7457 / publication7492 принят выпуск runtime `f6387f9` и MCP
  `10.2.0+vr2012.de5035a57e7f`; собственный rebase на bb280460 завершён без конфликтов,
  stash `a7921d04c` сохранён. Grant6 на точную новую B получен7494/7497; consumer r52/epoch3 прежний.
  Свежий аудит: 55 current / 37 excluded / 5 recorded proofs / 2 pins, own_verified0, delta56.
  Technical A `40c4c0a2c5db9c3e1a62b4c527f724f63451c187`, tree85950aeb, реальный index/HEAD/SDK сохранены.
  **687 DONE/receive** — новая независимая T1-проверка, без Play и replay644/666/669.
  B0/B1 PASS; явный refresh/compile завершён reload, ошибок компиляции0, native runner=false.
  Actual discovery: все8 классов / 49 Runnable, missing0. Один standard run_tests вернул
  job `1c504f58a64f40b5b32dbda47eb3efb0`, полный terminal Result: **49/49 PASS**, failed0/skipped0.
  Все49 details.state=Passed, exact coverage с discovery без пропусков/посторонних тестов.
  Full Result сохранён до finish и архивирован брокером: SHA256 `70856d216bfaa599ccef35155f298ecb8afd8ae041a01048533edad9c03d28d8`.
  R `0f4ecfed2defbb09d75eb09652f67bf63d6e8f42`, paths0/unexpected0/immutable_changed0;
  cleanup native runner/Play/compile/import=false, guard responsive; finish/receive=true. Своей аренды нет.
  В T1 Console1 Error: SDK UxrActor.Destroy в EditMode-тесте урона; SilenceMirrorNoise включает
  LogAssert.ignoreFailingMessages. Это не Console-clean PASS и не новый дефект Play, тесты/SDK не переписывались.
  Полные отчёты: reports/received687/ и reports/cc1-round6/native687-*.json. Игровые FAIL679 остаются открытыми.
  Resource-list32600 сообщил infra7495; по7492 один обычный native read восстановил templates/list,
  полный ответ в reports/cc1-round6-input/debug-request-context.json; результат7501 передан сопровождающему.
- Разбор679: после последнего PPK Fired до снятия probe — 1 кадр/34,7748мс, у AK105 — 14 кадров/402,1682мс.
  SDK пропускает первый projectile update; поздний hit мог не попасть в окно, но identity недостающего shot UNKNOWN.
  PPK67/68 и FAIL сохранены. Готовой устойчивой stop-fire команды не найдено; contract request7648 отправлен
  weapon-system для штатного прекращения новых fire и наблюдения завершения. Отчёт reports/cc1-round6-input/ppk-tail679.md.
- По7592 разобран независимый DevTools688:139/141, два FAIL. Editor-only компонент BotCombatStand в enabled scene
  присутствует в B/A688/current; production resolver исключает debug scene, automation включает явно.
  Нужен общий build/test/attachment contract: request7645 в vr-test-stand, ответ performance7647.
  Отчёт reports/cc1-round6-input/stand-boundary7592.md. Scene/asmdef/tests не менялись,68749/49 не отменяет688.
  Штатные native routes адресатов cleared/channel_mismatch; сообщения доставлены canonical, это не ACK.
  На вопрос launch-infra7656 подтверждена существующая граница: текущий BotCombatStand только Editor/MPPM
  (Docs/tasks/bots-validation-stand-plan.md:59–65,180). Built route должен явно отказать;
  runtime fixture сейчас не требуется, новая сборка/тесты не запускались. Ответ направлен quest-perf-pipeline.

## Проверено

На HEAD9ee56ea37 до policy rebase: 7 офлайн-сборок и 9 C#6 payload (включая B8 precache/container/memory) — errors0,
TEMP bots-cc1-round3-compile. Analyzer fixture и24 rejection повторно PASS; это не Unity evidence.
Borrowed SDK81 скомпилирован в Unity666/679, B0/B1 PASS. Runtime-only679 выполнен; SDK acceptance,
полная игровая приёмка остаются открыты. Новый штатный T1 на A40c4c0a2/Bbb/grant6 в687:49/49 PASS,
full terminal Result и coverage подтверждены; это не переоценка старых UNKNOWN.
Проверялка cc1-round1: полная синтетическая fixture проходит, 24 ошибочные отвергнуты;
неизвестный источник первого Move — «не измерен». Это проверка обработки отчётов, не Unity/runtime-приёмка.
Unity644: B0/B1 PASS, T1 потерял ответ; в SessionState656 сопоставимой записи нет, исход644 неизвестен.
Unity656: B0/B1 PASS. Явные фильтры через installed RunTests handler: **49/49 PASS**, failed0/skipped0;
полный Result подтверждает все 8 выбранных классов, посторонних тестов нет. SDK не изменялся.
p1: E/PE завершились, оба NeedsReview; H прерван. Счётчик CameraFade вырос **2→108→256**,
сработало условие остановки. p2 не запускался, полного cc-1 PASS нет. Console errors0.
E: попадания10/22, PE25/28; target root однажды сместился3,06м, camera height0–1,65м.
Причина требует следующего разрешённого замера; B4 теперь сохраняет ограниченный target trace
(C#6 compile PASS; свежий target trace получен в679 и разбирается). Оружейная диагностика передана владельцу:7046.
Контроллер и .meta имеют LF. [Доказательства и события](changelog/2026-10-14-codex-resume.md);
полные отчёты вне Git: `reports/received656/` и `reports/cc1-round1/native656-*.json`.
Новые полные отчёты666: `reports/received666/cc1-round3/` и `reports/cc1-round3/native666-*.json`.
Отчёты669: `reports/received669/cc1-round4/` и `reports/cc1-round4/native669-*.json`, intent сохранён доAPI.

## Решения пользователя

- 2026-10-12: player-flow — единственная архитектура; клиповый гибрид удаляется.
- 2026-10-13: «можно мержить» pf-network; «да начинай clip-cutover».
- 2026-10-14: «мое решение поправить в cc-1» — исключение для BotAuthorityTests.
- 2026-10-14: «если текущий этап готов - вливай» — условная интеграция; этап ещё не готов.
- Боты входят в minPlayersToStart; матч минимум с двумя командами. Следующие замеры — 8 и 10 ботов.

## Дальше

1. Диагностика полученных E/PE отчётов; рост CameraFade передан владельцу uxr-precache-once.
   Продолжение p1/p2 — после accepted guard либо exact immutable grant с проверенными own proofs.
   Новые runtime-попытки используют отдельный output root; intent/phase маркеры656 не удалять.
   Grant3 наr52/epoch3/B39af подтверждён7125/7126. Exact audit:55 mutation paths,33 исключения,
   5 recorded own proofs проверены, own_verified entries0. Technical checkpoint — отдельный index,
   56 delta paths с immutable SDK81; HEAD/real index/working SDK сохранены.
   Grant4 наB54673ba4 получен; exact audit55current/34excluded/5proofs/2pins, A52790c32 построен отдельным index.
   666 завершён после wrapper T1 timeout, native позже стартовал; assertions outcome UNKNOWN.
   669 завершён после потери штатной launch квитанции. Дождаться owner решения поinfra7305/7316;
   старые UNKNOWN не повторять. Runtime-only679 завершён на B8eb/grant5; уточнить готовность fixture
   до начала case, не скрывая начальные строки; отдельно записывать actual Idle, request и transition/reset.
   Два отменённых Move без смещения в T06/T08 (0,242/0,282 с) требуют decision/path/gate trace;
   необходимость запасного 1-с клипа не доказана, A/B остаётся контролируемой проверкой по cc1-plan.
   PPK67/68 требует отдельной shot-pipeline диагностики.
   Запрос7648 ожидает owner contract для хвоста projectile; без него не вводить второй writer огня.
   Независимый scene/build FAIL688 согласуется с launch-infra через7645; не ослаблять тест или UNITY_EDITOR.
   Исправлять только установленную причину в своей зоне. После выпуска7457 свежий T1 получен в687
   (49/49 PASS); для следующей проверки изменённого gameplay нужен новый точный вход и runtime.
   Селекторы/приёмка будущего CLI подготовлены в contracts/cc1-t1-cli-selectors.json, backend ещё не выпущен.
   Weapon-system7282 выбрал partial observer attempt → commit/emission → returned outcome вявной синхронной scope.
   Actual projectile/firstcollision UNAVAILABLE publicAPI; SDKpatch не разрешён, implementation пока не выполнялась.
2. Зональное ревью и демо пользователю. Затем покупки cc-2, документация cc-4, принятые тесты cc-5.
3. Матч M8/S8 и late-client замеры — после защиты precache; независимый patch81 пока не принят.
   Обещания другим владельцам и события — [backlog](../../experts/bots/backlog.md).

## Ждёт пользователя и риски

Приёмка cc-1 после Unity-раунда; предложение отдельного dead-code-sweep остаётся без ответа.
«Резиновые руки» при покупке — backlog. До защиты прогрева не запускать ≥8 ботов;
не вмешиваться в чужой Play/lease. Старые пакеты tools кроме cc1-* и cc0-match используют удалённый PlayerFlow.
Спавны TestMap1 и сценарий S8 со Spectator требуют отдельных решений владельцев.
Сбой native T1 передан сопровождающему (infra6970), delayCall warning — infra6994;
shared SDK и worker не ремонтировались. Подготовленный прямой T1 отработал фактически в656.
