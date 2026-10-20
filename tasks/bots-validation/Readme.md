# Проверки ботов и запуск внешних клиентов

2026-10-19. Owner `bots-validation`, worktree `F:/CodexWorktrees/bots-validation`,
ветка `task/bots-validation`, ведущая сессия `01a15069-c158-7da0-ab1d-8ee6b32c4dd6`.
HEAD задачи `48d38b9b2885b771f9cc961b4b640eaa7f74ca9e`; свежая `origin/dev`
уже `a62039a26f1d2d454b69e134aeb6eea38670042a`, rebase с сохранением dirty впереди.
Исходные проверки818/824/829 относятся к базе2e1b6a2fc и указанным input.
Для продолжения в новом чате сначала прочитать [Handoff.md](Handoff.md):
это единый самодостаточный документ передачи: решения, состояние, полный остаток
плана, архитектурные ограничения, evidence и порядок смены ведущей сессии.

## Цель

Завершить оставшиеся плановые проверки принятого поведения ботов и установить причину неудачного запуска с двумя внешними клиентами, устранив игровые блокеры в рамках этой задачи.

## Этапы

Этапы и области записи — в [plan.json](plan.json), последовательность и факты —
в [Details.md](Details.md). Агент задачи делает нужные изменения во всех затронутых
игровых зонах; эксперты подключаются для ревью. Инфраструктурные сервисы обслуживает
назначенный сопровождающий.

- [ ] Диагностика двух внешних клиентов: historical808 и новый input0/8ботов.
- [ ] Функциональные внешние Player/Spectator, latejoin/reconnect/holster.
- [ ] Lifecycle/директор/оружейные пары/контакт и применимые существующие проверки.
- [ ] Валидный H/C performance-контроль и H8/C8/H10/C10/RH/S8.
- [ ] Android и физический Quest-client0/1/9 + dedicated9.
- [ ] После соответствующей приёмки регрессии, ревью и публикация.

## Место в эпике ботов (2026-10-20)

Задача — этап 1 эпика [bots-epic](../bots-epic/Readme.md) «Боты по правилам сетевых игроков». По синхронизации с
e2e-regression (хаб 10212/10216) остаток сокращён: внешние Spectator/Player с ботами; бой, урон с автором,
смерть/призрак/возрождение, реестр SDK; QA пола/helper. Покупку, готовность и выбор команды старым бот-путём не
проверяем — их заменит паритет (этапы bot-input и rules-parity эпика). Perf и Quest — этапы 5–6 эпика.

## 2026-10-19: рестарт матча, план отложенного

Ведущая сессия Claude [4a1890], route epoch4. Checkout FF до `a62039a26`, dirty сохранён
(`refs/backup/bots-validation/pre-ff-a620-20261019`).

Отчёт пользователя: после «Играть заново» его бот-союзник стоит в центре карты,
покупка не списывает деньги. Разбор Editor.log worker (TestMap1, 1 человек + 3 бота):
- Матч 1 закончился в 4-м раунде, уже после смены сторон. Бот 3 погиб внутри зоны своей базы:
  тело уничтожено, `OnTriggerExit` не пришёл, у `PlayerSession.SpawnZoneTeamIndex` остался индекс
  «Повстанцы». Призрак появился у центра, бот считал себя на базе (`IsInSpawnZone`) и стоял.
  После сброса сторон в матче 2 он «закупился и стоит на базе» в центре, а `EliminationMode`
  каждый раунд ждал его возвращения. Правка: `TeamSpawnZone` помнит сессию и команду
  отчёта «внутри» и снимает запись за уничтоженное тело, если новое тело той же сессии не внутри.
- Покупки со стены: «Item taken» без списания и без отмены, в обоих матчах.
  `ArsenalWallController.InstallSlots` ставит слоты после `OnEnable` и не подписывал их события
  (пункт `Docs/tasks/arsenal-generator-plan.md:115`), касса не получала захват. Правка: подписка
  в `InstallSlots`. Боты покупают в обход стены (`BotShopper` → `MatchEconomy`), поэтому дефект не ловили.
Проверка: аренда 838 (input 50f3e1b4) — компиляция без ошибок, PlayLaunch Host + 3 бота;
пользователь подтвердил в worker: деньги со стены списываются и возвращаются, после
«Играть заново» боты идут на базу. Аренда 839 (input fa86553a) — тесты +5 (узкий набор 38/38),
полный EditMode 2813/2857, 35 упавших к изменённому коду не обращаются. Коммиты
`67445c70c`, `fddf7b4ed`, журнал `5a066921c`; этап `restart-match-fix` (plan rev8): ревью
gameplay `d789b3ad` и level-design `f59c7f4e` approve (patch-id 79e5e8bd), merge-execute
`396d81b0` — origin/dev = `5a066921c` (ls-remote). request-base 840 поставлен.
Changelog: `changelog/2026-10-19-restart-match.md`.
Хвост ревью (не блокер): второй цикл `TeamSpawnZone.OnDisable` без проверки `NetworkServer.active`.

Отложено/запланировано:
- Медленный вход в Play на worker: после перезапуска worker 5-6 с. Аренда 842 (2026-10-19) поймала
  причину: main и оба клона MPPM висят на `[Subsystems] Loading plugin OculusXRPlugin for subsystem
  oculus display...` (main ~9 мин, клоны дольше), до сцены и сети. Это инициализация XR-рантайма Meta
  в Editor; перезапуск worker его сбрасывает. Кандидат исправления (launch-infra): не поднимать XR у
  участников PlayLaunch без шлема (server, PC Spectator/Player). Логи: `reports/startup842/`.

Startup S1+S2 (дизайн `contracts/startup-lifecycle-design.md`, согласован): аренда 842, input e07c2c9b —
компиляция без ошибок; сервер discovery8 опубликовался после готовности (боты 17:48:30.2, карта
30.9, порт и объявление 32.8). Клиенты не проверены: клоны зависли на OpenXR. Cancel → Idle,
CleanupPassed=false (клоны), finish отклонён из-за безымянной сцены → собственный `recover --force`, DONE.
После перезапуска worker пользователем — аренда 844 (тот же input e07c2c9b), все прогоны Ready,
Cancel с CleanupPassed=true, finish/receive paths0/unexpected0 (`reports/startup844/`):
| Случай | Результат |
|---|---|
| discovery, 8 ботов, server + 2 PC Spectator | Ready: 10 сессий, оба клиента epoch1; в trace829 падал |
| localhost, 8 ботов | Ready; клиенты стучались в 18:34:16.4 до публикации 17.5, SocketException, повтор через 1 с, подключились со 2-й попытки |
| localhost, 0 ботов | Ready, 2 клиента epoch1 |
| Host VR + 3 бота | Ready, 4 сессии, аватар; публикация через 10 с |
Пользователь проверил сервер + 2 клиента с 8 ботами (аренда 847): «оба клиента подключились, видят карту,
ботов и матч». Тесты `StartupConnectLifecycleTests` (+13) в аренде 849: узкий 43/43, полный EditMode
2826/2870, 35 упавших — тот же набор, что до изменения. Коммиты `59baff564` (opt-in диагностика),
`1aadb3dc3` (S1+S2, тесты, журналы network/launch-infra); этап `startup-lifecycle` (plan rev9);
ревью network `d301cd17` и launch-infra `a369f20c` approve (patch-id 902ae8cd), AndroidCompileGate PASS
(аренда 851), merge-execute `8bf5c9ef` — origin/dev = `1aadb3dc3` (ls-remote); request-base 852.
Дизайн дополнен фактической реализацией и хвостами ревью (docs `e96c9b82e`).
Хвосты ревью (этап `startup-review-tails`, plan rev10): адресное подключение закрывает начальные попытки,
отключение между ними не ошибка; PlayLaunch ready=Published, фазы старта в Snapshot, startup JSONL в
архивах; `TeamSpawnZone.OnDisable` пишет сессии только на сервере. Аренда 854 (приёмка пользователем по
прогонам: localhost8, адресный disconnect/reconnect эпоха 1→2, Host3), аренда 858 (+2 теста, узкий 34/34,
полный 2838/2883; новое падение UxrUniqueIdOnDiskTests — из чужого 43a771bcf xr-input, сообщено
bots-control-modes, event 10166), Android PASS (860). Ревью network/launch-infra/gameplay approve,
merge `53235c29` — origin/dev = `2a5931352`; request-base 861.
- Подключение клиентов при любой длительности старта сервера — блок A Handoff §7-8, дизайн не согласован.
- Требование пользователя: боты подчиняются тем же правилам, что клиенты; два режима — Blaze и
  скрипт-кукловод для сценариев и сквозных E2E по настоящей сети. План уже есть:
  `tasks/bots-fix/control-modes-plan.md` (решение 2026-10-12; B-1 pilot-split, B-2 script-brain,
  LI-1 xr-input, B-3 device-executor, B-4 match-smoke). Ни один этап не начат.

## Текущее состояние

Пол080f137aa и helper48d38b9b2 влиты через штатные merge-execute, origin/dev SHA
проверен отдельно. Публикация — по явному исключению пользователя «Разрешаю
выпуск пола и helper без QA»; headset/helper QA остаётся открытой. Для точных
коммитов получены новые зональные approve/ACK. Обновление worker baseline832
DONE; broker уже наa62039a26, workeridle. Сетевые исходники не публиковались.

Исследование с read-only субагентом продолжено: успешный discovery0 уже имел
main70s reload/29s map, clones~5s/~1.5s. Startup должен выдерживать долгую init
любой роли; role-swap PASS не будет считаться исправлением. Source-backed варианты
общего server publication/client waiting/sync contract —
reports/trace829/startup-architecture-options.md. Реализации нового поведения нет.
Следующие абзацы сохраняют исторические данные подготовки до публикации.

Подготовлен раздельный выпуск до дальнейшего исследования MPPM:
Floor-only0c89300d (3пути,99/99+catalog7/0, bots/gameplay approve исходного diff)
и helper-only73517e32 (2пути, PS5.1/PS7, launch-infra approve исходного diff).
Оба ждут соответствующей userQA; accepted commit/merge ещё не выполнены.
Подробности — tools/release-split.md. Передача исследования следующему агенту
подготовлена в tools/startup-investigation-handoff.md; новый агент ещё не запущен.

Пользователь подтвердил оба следующих шага. Helper исправлен: новый режим
`-ReaggregateInput ... -ReaggregateOutputDirectory ...` выполнился на PS5.1 и PS7,
проверив полный inventory240 файлов без новой сборки Unity. Проверен и собственный
повтор principal: `reports/helper-fix/principal-final/build.json`, success=true,
outputRoot указывает на проверенные архивы. Original failed pair не изменён;
sourceKind=unknown, dirty и неизвестные поля не подменены. Это ещё не userQA.

Helper получил launch-infra approve: request `fc5f8a0419674a40`, candidate31b6c5ae,
ACK записан через дословный вывод независимого ревьюера. Ревью не принимает продукт.

Пассивный сетевой trace реализован, по умолчанию выключен. Согласованные повторы
выполнены в ticket829, input5877d882: AndroidCompileGate Passed/Errors0;
discovery8 и localhost8 не достигли Ready. В каждом случае штатный Cancel,
CleanupPassed=true, Playing=false, Owner пустой; сохранены3 полных process logs
и3 startup JSONL. Finish/receive829 завершены, R5fa499be:1path/unexpected0;
получен Unity-generated meta, worker освобождён.
В localhost оба connect/SocketException произошли примерно за80s до server-listening.
В discovery KCP timeout перекрыт длинным серверным кадром без late/send update.
Подробный разбор — `reports/trace829/timeline-analysis.md`; это диагностика,
не исправление сетевого поведения. Timeout/readiness/retry не менялись.
Чужой828 восстановил сопровождающий; самостоятельного recovery не было.
Старые .NET fixtures не компилируются из-за отсутствующих типов; их не меняли.

Продолжение принято чатом `01a15069-c158-7da0-ab1d-8ee6b32c4dd6`: route epoch2,
inbox обработан, native proxy использует правильный root bots-validation.
Dirty продукт и untracked материалы сохранены checkpoint
`5dcb91e30c19e67ac6bbd4bfb9c1ca8f18c02ba4`; HEAD не менялся.
После fetch HEAD/origin/dev/broker baseline совпали (`2e1b6a2fc`).

External-cases выполнены в аренде824 через PlayLaunch, штатные finish/receive
завершены: result `e1130da7142c317d9207d820fed71c3bad7f58d1`, paths0/unexpected0.

| Случай | Фактический результат824 |
|---|---|
| discovery, bot0 | Ready: dedicated + два PC Spectator, bodyless/team0. Disconnect/reconnect: epoch1→2, новая session identity; второй клиент остался подключён. |
| discovery, bot8 | Сервер TestMap1 с8 ботами; оба клиента Offline, epochs0/1, Ready не достигнут. У обоих клиентов в логах KCP silence10000ms. |
| localhost, bot0 | LaunchNotReady до Play всех процессов; сетевое подключение не проверено. |
| localhost, bot8 | Сервер TestMap1 с8 ботами; оба клиента Offline/epoch0. Причина раннего разрыва пока не доказана. |

Все4 случая завершены с CleanupPassed=true и Playing=false. Полные статусы и
по3 архивных process logs — `reports/external824/`; main-архив имеет известное
ограничение общего Editor.log. UNKNOWN не считается PASS. Dirty продукт
по-прежнему соответствует checkpoint5dcb91e3. Инфраструктура не менялась.
Диагностика helper PS5.1/PS7 и inventory240 файлов —
`reports/build3d24/provenance-diagnosis.json`; исходный pair остаётся success=false.

Пользователь подтвердил ходьбу, стрельбу и удержание оружия в Host принятой
итерации bots-fix. Новая база содержит последующие принятые изменения иерархии
аватаров/кистей; старые5489/G805/802/АПК53 не считаются fresh proof этой базы.

В historical808 первый discovery-запуск дошёл до TestMap1 у одного клиента и
завершился KCP silence10000ms; второй explicitlocalhost оборвался доepoch1
без записанной KCP error. Единая первопричина не доказана. Штатного retry при
epoch0 нет: контролируемый disconnect удерживает Offline, stand reconnect требуетepoch>=1.

AndroidCompileGate выполнен штатным existing CLI из нового root в аренде817:
Editor.log подтверждает PASS/ExitCode0. MCP reply disconnected; raw ошибка сохранена
отдельно. Guard подтвердил idle, затем finish/receive817: paths0/unexpected0,
worker освобождён. APK/Quest/runtime эта проверка не покрывает.

Native MCP предыдущей сессии был запущен с прежним `--repo F:/CodexWorktrees/bots`;
после перезапуска uv соединение восстановилось, но путь не изменился.
По ответу сопровождающего9664 нужен чат, открытый из проекта `bots-validation`,
продолжающий существующую задачу без повторного task-new.
Этот переход выполнен текущим чатом; результаты external824 приведены выше.

Передача9609/9615/9623 завершена: после terminal/receive818 проверил и перенёс
native Floor BotCombatStand (Default→Ground), builder и пересчитанный catalog.
Три файла, 5 insert/4 delete; raw99/99 и catalog7maps/0errors проверены,
полные артефакты сохранены в reports/floor818. Дополнительный Lobby fingerprint
сверен с исходными байтами; это исправление старого отставания каталога.
Дублирующих Unity-операций, чужих правок, новых tests/expectations нет.
Исправление Floor ждёт userQA телепорта; accepted commit/merge ещё не было.

Сборка Test Quest + Dedicated Server из технического checkpoint
`3d24f53080fe853cbfc8a1de82170c70cedc5b7a` завершена: оба native builder PASS,
UXR ID совпал (`ea5574049cff8620`, 8714 IDs / 356 files). APK и полный сервер
сохранены в `reports/build3d24/`. Общий Build-Game завершился exit1: в PS7.6.5
ru-RU JSON-дата преобразуется в DateTime, затем в строку, которую повторный
TryParse отвергает. Исходный pair manifest остаётся success=false; исправления
helper и подтверждённая повторная агрегация теперь выполнены (см. выше). APK не установлен,
физический Quest не проверен. Подробности и ограничения — в Handoff.md.
