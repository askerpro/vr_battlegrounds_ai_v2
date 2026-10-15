# Проверка preflight загруженной карты

## Возобновление 2026-10-14

Приоритет пользователя сохранён: `preview-loaded-scene`, затем тесты принятой разборки арсенала;
декор арсенала отложен. Inbox gameplay разобран: 6559 подтверждает передачу этапа Codex.
Устаревшие native-маршруты Claude очищены; этот клиент не предоставляет адресацию peer-сессий.
Ветка `claude/preview-loaded-scene` перенесена на `origin/dev` d3c62ca5e;
код после rebase — 02d367fe7. Конфликт Readme разрешён сохранением актуальных сведений передачи.

## Статическое ревью

Отдельный read-only агент проверил исходное изменение e219e2341 и потребителей.
Новых статических блокеров не найдено. Загруженная чистая карта проходит существующий
`ValidateSceneContent`, dirty возвращает `Scene.Dirty:<map>`; незагруженная использует preview
с закрытием в `finally`. Mirror обнуляет повторяющийся sceneId и при сборке бросает исключение.
Это объясняет отказ прежнего пути с открытой Lobby, но не заменяет Unity-прогон.

Ограничения прежнего кода: `RebakeCatalog` сохраняет каталог до Validate; dirty-отказ
не гарантирует отсутствие записи самого каталога. `MapCatalogIntegrityTests` открывает preview
безусловно; ожидание этого теста требует отдельной проверки после приёмки изменения.
До приёмки тесты этапа не создаются и не переписываются.

## Подготовленный пакет Unity

1. Открытая сохранённая Lobby: rebake passed; scene setup, activeScene, dirty и sceneId неизменны.
2. Lobby помечена dirty только в собственной аренде: rebake отказал с `Scene.Dirty:Lobby`;
   открытая сцена осталась dirty, дисковые байты не изменены; затем закрыть без сохранения.
3. Ни одна карта каталога не открыта: rebake passed, пустая сцена и setup не меняются.
4. Реальный `E2EPlayerBuilder.Run` через одноразовый `EditorApplication.update` с чистой Lobby:
   фактический результат сборки, сообщения sceneId, setup/dirty/sceneId до и после.
5. Дождаться terminal-состояния, убрать только временную сцену своего пакета, немедленно finish.
   Изменения каталога и `PC.asset` не принимаются автоматически; отчёты — reports/, вне Git.

## Приёмка

Нужны фактические результаты Unity, затем подтверждение пользователя. Историческая офлайн-компиляция
и статическое ревью не считаются текущим Unity PASS. Вливание и `accepted-tests` — после приёмки.

## Блокер Unity, 2026-10-14

Checkpoint 68380ccb532f4eac3f7e96145c99dadadf468198, заявка 625, epoch 531, LEASED.
Begin не выполнялся, сцены не изменялись. Обёртка клиента попыталась разобрать пустой stdout
ещё работающей claim-команды и потеряла её session_id. Сохранённое состояние подтвердило claim;
повторной команды не было. Получение capability из внутренней SQLite-базы отвергнуто auto-review
как credential probing; helper удалён, обход не применяется.

Сопровождающему worker/agent-infra отправлен infra_issue 6704 в agent-coordination-protocol:
штатно освободить не начатую аренду или восстановить выдачу capability. Локальные доказательства —
[reports/lease-625-blocker.md](reports/lease-625-blocker.md), вне Git. Общая инфраструктура не изменялась.
Подготовленные одноразовые пробы находятся в tools/. Их наличие не означает выполнение.

Блокер снят без SQLite: сопровождающий нашёл исходный штатный claim stdout в журнале этого чата
(`CommandExecution exec-24ad14c6-c4b2-4037-85c6-216dd810412e`). Capability восстановлена из него приватно.
Begin и finish выполнены штатно; сопровождающему сообщено событием 6760, токены не передавались.

## Фактические результаты аренды 625

Unity 6000.4.1f1, worker PID 41776, база d3c62ca5e, вход 68380ccb532f4eac3f7e96145c99dadadf468198.
Три проверки preflight прошли: loaded-clean, loaded-dirty, no-loaded-map. Снимки setup/activeScene/dirty/sceneId
совпали до/после каждой проверки; байты Lobby при чистой и dirty-проверках неизменны. Dirty дал именно
`Scene.Dirty:Lobby` и остался dirty до явного закрытия без сохранения.

`E2EPlayerBuilder.Run` с открытой Lobby: `Passed=true`, 966.8374891 с, ревизия 68380ccb532f, ошибок 0.
Сцена до/после имеет один sceneId 701398374, тот же setup и dirty=false. Составной сырой report имеет
`passed=false`: излишне строгое сравнение включало GetInstanceID (-278854 → 462170), который изменился
при пересоздании объекта. Это наблюдаемое изменение transient identity, не потеря сетевого sceneId.
Критерии этапа (реальная сборка без sceneId-ошибки и сохранённая карта) подтверждены данными;
сырой false не переписан. Будущая build-проба сравнивает sceneId, не InstanceID.

Finish завершён, результат 84bc9c0de949d20426639e7cf0a35553a291fd29: единственный diff —
`Assets/URPDefaultResources/PC.asset`, unexpected=[]; receive не выполнялся. Изменений Lobby в результате нет.
Полные локальные отчёты скопированы из durable artifacts, SHA256 проверен:
- [reports/preflight-loaded-scene.json](reports/preflight-loaded-scene.json): 7147e7873ee549450ed6de81c3b31e4b008fa649c1cc8c331a4b6e22c0086f3c.
- [reports/build-loaded-lobby.json](reports/build-loaded-lobby.json): 596b8e01a88bb8e8c6efd62798034387a0cd63a90cee5579bf3334f37e0c004b.

Пользователь принял этот результат 2026-10-14: «Принимаю, влить (рекомендуется)» в диалоге приёмки.
Разрешено влить именно preview-loaded-scene. Тесты арсенала и будущие accepted-tests каталога
этой приёмкой не принимаются.

## Публикация и тесты после приёмки

Принятый этап опубликован fast-forward в `origin/dev` 97a320c88469cb2a74d89adb0a6b6edd23536753;
хаб: verify → accept → record-merged, событие 6800. Worker-база опубликована заявкой 639 DONE,
событие 6808. Исходник preflight в принятом кандидате совпадает с проверенным входом 625.
launch-infra и level-design уведомлены через canonical inbox; побочный `PC.asset` не получался.

После пользовательской приёмки подготовлен `MapRunPreflightTests.cs`: 7 случаев, Roslyn compile-only
exit0; статическое ревью без подтверждённых блокеров. Прямые тестовые записи (временные файлы и прежний
preflight report) восстанавливаются в finally; исходные assets только сравниваются, их rollback нет.
Текущий Validate не пишет assets. Фактический NUnit-прогон ещё нужен, этап не завершён.

## Попытка accepted-tests650

Request/watch заранее разрешённым task-local helper получил OFFERED, но immediate claim явно отказал:
«Другой процесс завершает Unity/Git переход». Повторный native claim дал тот же отказ; третья проверка
уже видела чужую RUNNING network (номер в ответе не указан). Capability не выдавалась, Unity/NUnit действия не начинались.
Штатный watch-ticket подтвердил650 CANCELLED, result_sha=null. Infra_issue6980 передан сопровождающему;
shared infra не менялась. [reports/attempt-650.json](reports/attempt-650.json) — публичная сводка, вне Git.
Новый запрос не создаётся до установления штатной готовности; compiled7 не считаются NUnit PASS.

## Подтверждённый RED671 и исправление fixture, 2026-10-15

Canonical worker_ready7257 подтвердил восстановление runner сопровождающим. Новая аренда671
на базе54673ba4, вход1cdc516a2af0e8c8ba789571429660ba86de3ea7, broker epoch567, worker41776.
Job76ce49ac6ca54138838b96b5a692c321 завершён failed: total7/passed5/failed2/skipped0, duration33.7522616s.
Оба Lobby случая упали в finally WithLoadedLobby: RestoreSceneManagerSetup(original) получил setup[]
и выбросил Invalid SceneManagerSetup (нет загруженной/активной сцены). Это ошибка собственного теста,
а не подтверждённый дефект preflight или новый инфраструктурный отказ.

До/после managers/live/previews0, setup[]. Finish671 DONE, result00bee58eb1d70dd3d400f14d929cafeb2384427b,
paths только Assets/Tests/EditMode/Maps/MapRunPreflightTests.cs.meta, unexpected[]. Receive выполнен штатно,
Unity-generated GUID33a70f46be69a0e4d8d9441943cd0502 сохранён. Полный scoped MCP отчёт:
[reports/attempt-671.json](reports/attempt-671.json), SHA25604cc1f96c1b6eb9eac6e55789f2c376ab54d22de21c681d890d77f970eedf7ee.

После finish в собственном тесте добавлена ветка original.Length==0: закрыть только открытую fixture Lobby,
не вызывать RestoreSceneManagerSetup с пустым массивом. Ветки сохранённых/безымянных исходных сцен
и все assertions сохранены. Узкое read-only ревью blockers не выявило; компиляция против284ссылок exit0.
Новый source SHA2565c6faa42f219ad31fc98ddf42913a58de44d6f5d2a443c5a7711c1f7194d5108.

После fetch/rebase --autostash до origin/dev8eb8779 создан новый input9976444b181abb66617a4418b48338d328025648
с полученным GUID; новая заявка675 QUEUED. Критерий —7passed/0failed/0skipped и сохранённый cleanup.
Продуктовый код, каталог и сцены не редактировались; прежний RED сохранён.

## RED675 и потерянная квитанция676

675 завершён терминально: job ebc1b7df845848c7b6f3f54faabd0aa3, passed5/failed1/skipped1.
После снятия маскирующей ошибки finally dirty-тест получил Catalog.Missing; clean-тест пропустился из-за
оставшейся dirty сцены. Native output подтвердил: выгрузка последней loaded Lobby не поддерживается.
Finish675 DONE, result1aea629a9c24796973c3ac92e548d44edbf0b9f4, paths[]/unexpected[], cleanup0/setup[].
Полный RED — [reports/attempt-675.json](reports/attempt-675.json),
SHA256f2cfc3e025455a338aecccedf22ad1127206dafebb3b43b1b0b776634f7de490.

Собственный fixture теперь определяет пустую исходную сцену по реальному active/path/isLoaded,
сохраняет её и открывает Lobby Additive; затем создаёт clone каталога с HideAndDontSave и уничтожает
его в finally. Cleanup keepEmpty имеет приоритет над пустым сериализованным setup. Все исходные assertions
сохранены; неподдерживаемый исходный zero-scene случай явно не считается PASS. Compile-only284refs exit0,
source SHA25604b9bd78a1e0bb1ba4ee02c01e3b18b9ee8642f750d23f827649ef0e53801e03.

Новая676 RUNNING, input60eadffa8db8124b7a13de9a104c57e5fd91a1d7, broker epoch572. Перед тестом
подтверждён native parked-case: sceneCount1, activeValid=true/path пустой, loaded/clean/roots0, setup[].
Run_tests потерял plugin session c6207558-9dba-4f5b-a6d9-d94e69f7d4dc без job_id. Свежий resource
editor/state observed1792017908264 показал idle/no compile/no update/tests.is_running=false/current_job_id=null,
last_domain_reload_after1792017864712. Это UNKNOWN исход, не NUnit PASS и не основание для replay.

Infra_issue7345 немедленно передан сопровождающему через хаб и свежеподтверждённый Codex канал.
Run повторно не вызывался, clear_stuck/recover/самостоятельный ремонт не выполнялись.
Второй свежий resource observed1792018110906 снова показал idle/no tests/currentjob=null/no pending reload.
Read-only status execute_code в EditorUTC22:48:57.231Z подтвердил пустой persisted CurrentTestJobIdV1,
исходную clean пустую сцену1 и managers/live/previews0/setup[]. После подтверждения отсутствия активной
операции сразу выполнен finish676: DONE result e0c459e64e7d74de56aa28064d46a79ebcc2e903, paths[]/unexpected[].
NUnit исход остаётся UNKNOWN; новый запрос не создаётся до поддержанной готовности от сопровождающего.
Полный scoped отчёт — [reports/attempt-676.json](reports/attempt-676.json),
SHA25689fcce639814587bc4f52b355db901046c420fd252fc06eb05355b0ac771faa2.

## Прогон684 и сохранение исходных корней сцены

После worker_ready7457 обычный MCP runner восстановлен сопровождающим: runtime
f6387f9dd19ff4547e7f98c53e6e4718d5735933, server10.2.0+vr2012.de5035a57e7f.
В аренде684 выполнены явный refresh и фактическая discovery7 случаев. Job
2b53a24011f7431e8a09505fe0181c01 вернул terminal Result: total7/passed5/failed0/skipped2.
Статус задания succeeded не означает выполнения всех проверок: оба Lobby случая пропущены
защитой sceneCount/rootCount до вызова Validate. После прогона clean unnamed scene имеет два корня;
их происхождение не установлено. Чужие объекты не удалялись.

Полный [terminal-result-684.json](reports/terminal-result-684.json) сохранён до cleanup и finish,
SHA2561a886b46e1f3670b440ee6d775ac26894a056cedaab4af9c4213556f73f5d3bb.
[attempt-684.json](reports/attempt-684.json), SHA256a018d50db290adfdfda59d6cf00cfdf0b33a488f1451890c9dd7e5e7698526d9,
содержит discovery, состояния и finish684 DONE c5c312ddca070ce6afee32f550f183b05b60d320, paths[]/unexpected[].

В собственном fixture убрано только требование rootCount0 для единственной clean unnamed scene:
Lobby открывается Additive, исходные корни сохраняются. Добавлена проверка точного набора root entity IDs
и сохранения активной сцены; проверки hash, dirty/clean, sceneId и исходных байтов не ослаблены.
Source SHA2569ec0d4a6853f2d34b4696a8c5665a6dd9b02c47b11f5948e013257c08f41211e;
компиляция против284ссылок exit0, diagnostics[]. NUnit для этой версии ещё не выполнен.

Новый immutable input c7ee905d058091a02360c42f7092073d189b340f включает source/meta, owner-spec v2
и собственный acquire helper. Заявка694 QUEUED; перед ней база1812dc5bf публикуется штатной заявкой693.
Перед run отдельно снимутся состояния до discovery и после неё, включая имена/IDs корней.
Критерий принятия —7/7 без пропусков и восстановление исходных сцен/реестров. Старый UNKNOWN676 не повторяется.

## GREEN694 — тестовый код готов к отдельной приёмке

База1812dc5bf опубликована693 DONE. Аренда694/epoch589 получила inputc7ee905d0580 и собственный guard;
обычный refresh завершён, external_changes_dirty=false, idle/no compile/no reload/no tests.
Нативная discovery фактически вернула7leaf случаев. До и после discovery: одна clean unnamed scene,
activeHandle-22716556, roots0, managers/live/previews0/setup[].

Обычный run_tests вернул job7eabbb0d60ba4f8fbe939ff7f80213d2. Полный terminal Result:
total7/passed7/failed0/skipped0, duration39.266314s. Все7details сохранены до любых cleanup/finish:
[terminal-result-694.json](reports/terminal-result-694.json),
SHA256b7fdb4a761aaf4c641e46cbabacf9ea699b8c3a5e494280d75508517aaec673d.
Dirty Lobby получила ожидаемый Scene.Dirty:Lobby, чистая прошла; hash/EOL/binary/одиночныйCR — PASS.
Assertions сохранения сцены, active handle, root IDs, sceneId и исходных файлов также прошли.

После terminal очистили только собственный discovery SessionState. Managers/live/previews0/setup[],
одна clean unnamed scene activeHandle-22879218 с двумя корнями Main Camera и Directional Light.
Таким образом, состояние сцены перед всем run_tests и после него не идентично: handle/корни изменились
в интервале работы NUnit. Принадлежность объектов не установлена, они не удалялись; идентичность
исходной сцены и корней проверяется внутри каждого Lobby fixture и прошла. Продуктовые сцены/ассеты не сохранены.

Сразу после scoped cleanup отправлен finish694: DONE resultd76df7621be92d6bf95a1e05c609cdac154376e4,
paths[]/unexpected[], receive не требуется. Составной [attempt-694.json](reports/attempt-694.json),
SHA256df3521b3eece1d063758d31cb8f2bf633f2e735ebafe3705023f0e910c11e107,
содержит readiness/discovery/все состояния, полный Result и finish. Runtime готовности — notice7614
(1432984ac464614e7956aaba9f2eec78602e3f77), MCP server10.2.0+vr2012.de5035a57e7f.

Повторное статическое ревью source9ec0d4a6 не выявило blockers. При исключении check cleanup выполняется,
но assertions после finally уже не исполняются; ошибка теста при этом остаётся видимой. Проверка root IDs
покрывает состав корней, а не все их свойства/потомков. Это границы доказательства, не hardware/network приёмка.
Готовый scope приёмки: MapRunPreflightTests.cs + Unity-generated meta GUID33a70f46be69a0e4d8d9441943cd0502
и документация текущего этапа; инструменты, сгенерированные reports и уже принятый арсенал не включаются.

## Приёмка accepted-tests, 2026-10-15

Пользователь: «да - вливаем, считай принял». Scope: семь MapRunPreflightTests, Unity-generated meta
и документация этапа. Source/meta совпадают с проверенным input694 побайтно (SHA256 указаны выше).
После fetch/rebase --autostash до принятой базыd449f19832b1c2b0f6bb9c9ada3b9db4a4a2b299
код preflight, Maps/GameModes/Arsenal, каталог, данные, Lobby и зависимости в Packages/ProjectSettings не менялись.
Это перенос доказательства694 при неизменных зависимостях, а не новый Unity-прогон.

Owner-spec v2 сохранена как историческая декларация требований до694: её latest_unity_run684 и ожидание
будущего CLI исполнителя описывают момент подготовки. XML/log требования относятся к будущему CLI
контракту; реальный результат694 получен и сохранён через обычный MCP. Текущий статус находится здесь
и в Readme, исходная frozen spec не переписана задним числом.

Коммит7fbacc8259e8bb5ebe45968cdd2676fbd54ca691 опубликован обычным fast-forward
d449f1983→7fbacc825; git ls-remote подтвердил точный удалённый SHA. Только пять файлов: тест, meta,
Readme, Details и frozen owner-spec v2. Физические файлы и blob коммита побайтно совпали, CR отсутствуют;
LF range check без issues. Сводка привязки694 к принятому коммиту —
[accepted-code-verification.json](reports/accepted-code-verification.json). Другие локальные материалы сохранены
и в принятый коммит не включены. Все этапы catalog-fingerprint завершены.
Хаб: stage_merged7825/audit7826, epoch7, content_drift[]/dependency_gaps[].
