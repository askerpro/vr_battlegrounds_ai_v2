# Боты: player-flow и Blaze AI

Обновлено: 2026-10-16, Antigravity. Владелец `bots`, задача `bots-fix`, worktree
`F:/CodexWorktrees/bots/Vr_Battlegrounds_ai`, ветка `bots-fix`.
[План](plan.json), [архитектура](player-flow-plan.md), [cc-1](cc1-plan.md), [детали](Details.md).
[Прежний подробный Readme](changelog/2026-10-14-readme-before-worker-ready.md).

## Статус

- **751 DONE/receive**: input=`748d8e8493bf48d8155d6a0b374bf88c75794034`, B=`018e63d4f`, epoch636;
  R=`ef429ee1b99c2c29bbca079b89076091ac03041f`, paths0/unexpected0/binary_invalid0. Своей аренды нет.
  220 broker artifacts /52 991 399 байт; копия `reports/received751/`, SHA256 mismatch0, оба полных launch logs сохранены.
  82 собственных пути, borrowed SDK0, отдельные index/ref, HEAD/index сохранены.
  Выполнены последовательно visual G и M2 с полной cleanup между фазами и штатным finish после пакета.
  M2: scored1…4 (ничьи включены), один adminStop→Warmup→GoLive, epoch2→3→4,
  те же session/Mind, Round1 Buy/contextReset второго матча, затем Round2.
  Probe final/completed=true, incomplete=false; runtime/console errors/drops0. Новые игровые тесты не создавались.
  Офлайн:7 сборок и visual5/M2 6 C#6 payloads errors0. Review исправил wins/completed-round gate,
  exact naturalEnd branch, callback type и преждевременный completedObservation до stopped.
  G: две exact-frame пары old1487/new2520, обе руки, captures2/detached/pending0, без ошибок съёмки.
  Source/pose metadata неполны (runtimePose и weaponSource GUID null); fit analysis_incomplete, ownerDecision UNKNOWN.
  B7: R08 в536 Combat samples без WeaponSystem, при SDKready/loaded и SeesTarget=true444; shots0.
  Обязательный sameGO host gate BotGunner подтверждён; weapon-system получил defect_review8563.
  По8583/8587/8591/8758: R08 переведён на F5 `MagazineOnly` (SDK82 + unified ledger + scoped R08 writer в коде r65,
  согласовано с network8755/grip8732), но `R08.prefab` ещё не авторизован в Unity (stalled delayCall, infra8750);
  ответ8832 отправлен, гейты BotGunner не ослабляются, следующий B7 читает public OutcomeCount без правок игры.
  B0/B4 ответили timeout, но native history подтвердил SUCCESS54,59/63,15с; replay/recovery не было.
  Infra8478/8483/8489: exact tool input/reply/history в `reports/751-visual-b0-infra.json`.
  Canonical performance8491 требует seed-before-AddBots от launch/test-stand; запрос8548 доставлен.
  Native performance8367 ранее отклонён auto-review; ответ получен canonical, этот маршрут продолжению не мешает.
  Grip8565 получил кадры и identity-ограничения. Binding r3: weapon ACK8581, затем owner contract-agree
  (publication80e62dd6); state=agreed, active не объявлен до принятого вливания.
  По8657 (ответ network на8363): S8P (`Role.Player` с `TeamIndex=0`) блокирует `GameMode.AllPlayersHaveModeTeam()` в Elimination
  и создаёт авторитетный аватар; ответ8834 отправлен network, запрос8836 направлен `owner:gameplay` по режиму наблюдателя S8.
  External-mode finger pose boundary передан avatar-ik8576/grip8578. [Итоги751](changelog/2026-10-15-visual-lifecycle751.md).
- **747 DONE/receive**: функциональный ServerOnly M2, input=`74a81dca5`, B=`018e63d4f`, epoch632,
  R=`2db52022dd1184e308604d7b0c545b95c2cf6867`, paths0/unexpected0. Своей аренды нет.
  Прогон достиг Round6; watcher завершён incomplete: первый матч закончился/сменил mode до admin Stop.
  Stop/GoLive не вызывались, второй матч и sameMind не проверены. Runtime errors/console errors/drops0.
  Probe final=true, directorEquip.completed=false/incomplete=true; 34 events и418 inventory samples.
  CleanupPassed=true, Playfalse/launchIdle, собственный request удалён; штатные finish/receive выполнены.
  Broker сохранил14 reports, копия `reports/received747/cc2-lifecycle-round1/`: 13 815 199 байт, SHA256 mismatch0.
  Причина — ошибочное условие watcher roundWon>=4 вместо четырёх завершённых раундов; исход не переименован в PASS.
  Raw подтверждает natural end Round6 (счёт1:3), 25 shots/16 hits/4 kills; деньги обеих сессий сходятся с half-reset.
  Восемь оплаченных Buy имеют по одному accepted Offer, четыре noPurchase не оплачены. [Итоги747](changelog/2026-10-15-lifecycle747.md).
  После rebase источник=`4d66f9a8c`, stash`3c7f980d3` сохранён. Офлайн7 assemblies+5 payload errors0;
  scoped review исправил проверку durable final. 75 input paths, borrowed SDK/foreign0, HEAD/index сохранены.
  Владелец хвата8388 запросил отдельный visual обеих рук с avatar/weapon/side/grabpoint/pose/sourceID;
  acceptance по8355 не выдан. Network S8 fallback8363 и perf seed8367 ожидают ответов владельцев.
- **738 DONE/receive**, A=`0a8a1b4de`, B=`6669db54f`, epoch625,
  R=`de33426c3fe8e68b39218dcd1cede44feb2f5881`, paths0/unexpected0; своей аренды нет.
  B0/B1 PASS, fresh discovery49/T1 **49/49 PASS** с полным coverage, missing0/extra0.
  Console1 — известная EditMode ошибка UxrActor.Destroy. Пять p3 cases NeedsReview, InvalidFixture/runtime errors/CameraFade0.
  Broker закрепил57 reports; локальная копия `reports/received738/cc1-round9/`: 41 527 253 байта, SHA256 mismatch0.
  Новый G:30/30 DamageReceived согласованы с историей тел, old12/new18/ghost0, foreign/null/destroyed/dropped/errors0.
  Старый734 foreign1 не воспроизведён и остаётся UNKNOWN/FAIL; G не включал условный shot-tail.
  DirectorEquip:1 Buy/1 accepted Offer/1 эффективный Arm;133 inventory samples показывают1 новый Mk14 net204,
  без наблюдаемых дублей. Это sampled inventory, не полный spawn/despawn provenance.
  AB:1598 совместных кадров root/Feet/camera; движение тела подтверждено, proxy↔root <0,7мм.
  Bone feet/точная latency/визуальная приёмка открыты. [Итоги738](changelog/2026-10-15-round9.md).
  Источник738 HEAD=`450d1e41d`, rebased accepted6669; stash`63e8db0a4` и резервная ссылка сохранены.
  Новые code changes существуют как непринятый рабочий diff и technical input, в origin/dev не опубликованы.
- **734 DONE/receive**, A=`932d9cda5`, B=`47c010277`, epoch621, R=`42f37db64992b6dfb3b9a8733bf7c8ec3e80f916`:
  paths0/unexpected0. Аренда завершена. Без borrowed SDK81. Собственный HEAD=`3274e0b7b`, accepted base=`05506131`.
  B0/B1 PASS; discovery 8 fixtures/49 Runnable; штатный T1 **49/49 PASS**, missing0/extra0.
  Console содержит одну известную EditMode ошибку `UxrActor.Destroy`; runtime errors0.
  PlayLaunch p1/p2: 9 завершённых cases **NeedsReview**, InvalidFixture0; ServerOnly подтверждён,
  UsePrecaching=false, container count0, роста CameraFade нет.
  Raw reports: `reports/received734/cc1-round8/`; 100 файлов/53 538 204 байта скопированы после finish,
  SHA256 mismatch0 (`reports/received734/copy-sha256.json`). Broker artifacts[]; это собственная копия, не broker archive.
- Свежие эталоны: AK105 shots/hits49/49, PPK73/73; источник emission/Gunner согласован,
  readiness3 валидных UXR-цикла и условный shot-tail завершены. Начальные невалидные строки сохранены.
  Полная идентичность/завершение каждого projectile не доказаны. Исторический679 PPK67/68 остаётся FAIL.
  G death/respawn: foreignDamage1 без event/source journal; причина UNKNOWN, отказ сохранён.
  Blaze trace показывает реальные шаги proxy у ряда прежних «отменённых» Move; запасной клип не обоснован.
  Контролируемый explicit Idle→Move и совпадение proxy/body/feet ещё требуют отдельной проверки.
- Hub **r53**, clip-cutover RUNNING epoch3. TASK_LAYOUT8260 снят по8265: неизменный RUNNING scope,
  отдельные planned `product-docs` и `binding-player-flow-contract`; legacy Docs/tasks восстановлены к accepted05506131,
  актуальные документы перенесены в [product-docs](product-docs/T-48-bots-match.md).
  Binding r3 **PROPOSED** (publication32508b3d), замечания weapon-system8267 учтены; свой ACK записан
  (publicationd19800ec) после штатного решения lock8288/8294. ACK weapon-system ожидается; active r2 до workflow.
  [Разбор734](changelog/2026-10-15-round8.md). Ни gameplay, ни cc-1…cc-5 ещё не приняты.
- 2026-10-15: разрешённая B4-правка применена: Vector3 → массивы координат, потеря listener/coverage
  делает trace incomplete. После rebase на принятую `47c010277` офлайн: 7 сборок и 9 C#6 payload errors0;
  analyzer: полная synthetic fixture PASS, 24 ошибочные отвергнуты. Это не Unity/runtime PASS.
  Round7 `A=aec747179` без borrowed SDK: **732 DONE/receive**, `R=932d9cda5`, только два ожидаемых `.meta`.
  B0 оборвался `Session terminated`; исходная ошибка сохранена. Сопровождающий8239 независимо доказал
  **NOT_DELIVERED** по HTTP404 до Unity dispatch. B0/T1/Play не пройдены. Свежий idle guard → renew
  только для capture/restore → штатный finish/receive; своей аренды больше нет.
  Следующий вход сохраняет полученные GUID; новая проверка — в конец FIFO и с read-only MCP probe.
  Детали: changelog/2026-10-15-round7.md; evidence reports/cc1-round7-input/infra732.json.
- 2026-10-15, canonical8064: `precache-server-guard` принят и влит в `origin/dev`
  `1e945a99673b9726773242f690fcea2032cae2c6`; удалённый SHA проверен через `ls-remote`.
  Зависимость от отсутствующего accepted guard снята. До собственного Unity-запуска нужны
  fetch/rebase и подтверждённый DONE/base заявки723. Чужие Android713 PASS и Windows713 E2E24/24
  не заменяют наши M8/M10/S8, perf и Quest. SDK81 остаётся отдельной непринятой зависимостью.
  Canonical8081 уточняет ServerOnly: фактическая роль `NetworkServer.active && !NetworkClient.active`,
  scene-load guard до спавна и `UsePrecaching=false`; клиентские realLocal/warmup/abort/latejoin
  проверяются отдельно. Новый grant SDK81 не выдан, свежие exact B/A и own proofs обязательны.
  Canonical8089: perf-методика7933 и свежая hybrid baseline обязательны; сетевой overhead
  `16 + VarUIntSize(netId)` = 17–21 байт (unscoped17) плюс prefix/fanout/transport.
  Canonical8100: worker716 восстановлен DONE, FIFO продолжилась; production CLI ещё не выпущен,
  MCP timeout fix f344798 не deployed, delayCall остаётся отдельным открытым симптомом.
  Текущий локальный HEAD `cb3c61ecc` — технический checkpoint; свежего round7 input и аренды нет.
  Canonical7961 подтвердил внешний blocker auto-review7947: прежние разрешения человека действуют,
  повторы отклонённых правок и обход другим инструментом/исполнителем остановлены.
- 2026-10-15 пользователь поручил полностью завершить clip-cutover (cc-1…cc-5).
  Подготовка раннего вливания остановлена; candidate a2576cab не опубликован, активной Unity-аренды нет.
  Выполняются readiness/shot-tail/public Blaze trace и cc-2 закупки, затем новые проверки/матч/демо.
  Точные scope/решения: changelog/2026-10-15-cutover-completion.md. Старые FAIL сохраняются.
- Хаб revision53: `clip-cutover` RUNNING epoch3 (ACK weapon-use-context r2; admission не заменяет Unity lease);
  `binding-foundation`, `pf-prototype`, `pf-equipment`, `pf-network` MERGED.
  `armed-development` ACCEPTED; `pilot-split`, `script-brain`, `pf-quest` PLANNED.
- Исторический HEAD `e812a2dfa` содержал принятую B `bb2804607`; тогда — 0 позади / 13 впереди.
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

Текущий порядок после751:

1. Журналы G и DirectorEquip внедрены и проверены в738; старый foreign1 и неупражнённые отрицательные ветви не закрыты.
2. M2 исправлен и выполнен в751: четыре раунда с ничьими, adminStop→Warmup→GoLive,
   новый Round1 Buy/contextReset на прежних Mind, затем Round2. Natural full match-end отдельно в747.
3. До принятия gameplay закрыть runtime-авторинг `R08.prefab` (F5 `MagazineOnly`, SDK82, 8758) с `weapon-system`
   и finger-pose/source identity с `avatar-ik`/`grip`; не ослаблять gates, не выдавать authored binding за applied pose.
   Отказ OfferPurchase, map/reload, pause/resume и Respawn требуют отдельных реально наблюдаемых переходов.
4. После seed/setup контракта8548, решения `gameplay`/`network` по режиму наблюдателя S8 (8657/8836; S8P отклонён)
   и fresh SDK81/82 grants — новый exact input/FIFO для Host hybrid/candidate×N8/N10, S8, remote-human (командный клиент
   с исключением local avatar netId) и позднего входа по perf-методике7933.
   Common probe C semantic PASS, H exact compile ещё pending; исторический M3 не baseline.
5. Зональное ревью и демонстрация пользователю. После Unity/headset-приёмки — актуализация тестов cc-5,
   финальный scoped candidate, проверка и fast-forward публикация origin/dev. До этого не вливать.

Предыдущая последовательность и обращения ниже — исторический журнал до734; текущий порядок указан выше.

1. Диагностика полученных E/PE отчётов; рост CameraFade передан владельцу uxr-precache-once.
   Accepted guard опубликован (canonical8064, `origin/dev` `1e945a996`); для продолжения p1/p2
   нужны собственный fetch/rebase, подтверждение worker DONE/base723 и точный новый input.
   Borrowed SDK81 требует отдельного exact immutable grant с проверенными own proofs.
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
3. Матч M8/M10/S8 и late-client замеры — после получения worker с принятой защитой precache,
   выполнения условий SDK-владельца и согласованной perf-методики7933; patch81 пока не принят.
   Обещания другим владельцам и события — [backlog](../../experts/bots/backlog.md).

## Ждёт пользователя и риски

Приёмка cc-1 после Unity-раунда; предложение отдельного dead-code-sweep остаётся без ответа.
«Резиновые руки» при покупке — backlog. До защиты прогрева не запускать ≥8 ботов;
не вмешиваться в чужой Play/lease. Старые пакеты tools кроме cc1-* и cc0-match используют удалённый PlayerFlow.
Спавны TestMap1 и сценарий S8 со Spectator требуют отдельных решений владельцев.
Сбой native T1 передан сопровождающему (infra6970), delayCall warning — infra6994;
shared SDK и worker не ремонтировались. Подготовленный прямой T1 отработал фактически в656.
