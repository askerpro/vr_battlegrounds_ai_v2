# Закреплённые исправления Unity MCP 10.2.0

Каноника package vendor patches — этот каталог. Установленный package в worker и
server cache вручную не редактируются. Подготовку и установку выполняет единственный
оператор через штатный допуск; managed проверки ниже Unity не вызывают.

## TestJobLifecycle: callback receiver после domain reload

UI ticket672 вернул сохранённый `job_id=d29d2ebb455c4b55910e8741478fa04b` в 01:34:16
МСК 2026-10-15. Сервер подтвердил receipt в 01:34:16.410; plugin disconnect последовал
в 01:34:16.567. Editor.log содержит reload и регистрацию новой session; затем native
`TestRunnerNoThrottle.TestCallbacks.RunStarted` (979163), `RunFinished` (979185), запись
TestResults.xml (979198) и cleanup. После этого MCP `GetJob` объявил init timeout
120000 ms (979220), completed=0/total=null/result=null. Saved terminal timestamp —
01:36:41.795. Native callbacks выполнялись через EditorApplication.update; сохранённые
guard `delayCall fired=0`, stalled 14.7→135.1 s не доказывают отсутствие запуска тестов.

Avatar ticket677 повторил класс симптома: сохранённый
`job_id=8c540cf8a16c4c4e8ded80aba0e707ac`, start 01:52:15.520, terminal init timeout
01:57:25.011, completed=0/total=null/result=null. Native warning найден в Editor.log.
Сохранённые материалы владельца — tasks/hand-rig-quality/reports/native-tests-*-677*.json.
Точное сопоставление каждого соседнего callback/reload с этим job не установлено:
несколько запусков и reload используют общий журнал. Изменяемый AppData TestResults.xml
не является durable receipt отдельного ticket; его содержимое нельзя присваивать
672/677 по одному имени файла. Ни одного игрового результата этим диагнозом не принято.

Причина на уровне исходников: SessionState восстанавливает TestJobManager, а
MCPServiceLocator.Tests создаёт TestRunnerService лениво. Его ctor регистрирует callbacks;
GetTestJob обращается только к manager и после reload receiver не создаёт. Независимый
NoThrottle receiver регистрируется через InitializeOnLoad, поэтому native RunStarted и
RunFinished могут пройти, а MCP progress/result остаться пустыми. Для UI672 source+runtime
цепь подтверждает потерю MCP callback-записи; физический список receiver в той session
не был снят. Этот отказ отличается от потери первоначального ответа без job_id.

Cumulative patch добавляет InitializeOnLoad к TestJobManager. После восстановления
SessionState только для HasRunningJob создаётся единственный lazy Tests receiver,
синхронно до первого editor update. Это регистрация, а не Execute/новый запуск.
Нельзя gating регистрации по IsRunActive: framework ResumeRunningJobs ещё может не
восстановить nonserialized runner registry. Constructor/RegisterCallbacks не вызывают
manager повторно: registration framework лишь добавляет receiver в list. Ошибка
регистрации логируется, manager не отравляется TypeInitializationException; наличие
лога либо инициализации не заменяет live проверку callbacks и полного результата.

## Полный terminal tree после mid-run reload

Одной регистрации receiver недостаточно: TestRunnerService хранит `_leafResults` в
памяти текущего domain. Если первый тест завершился до reload, новый receiver увидит
только последние leaf callbacks. Native summary содержит весь запуск, а прежний
`TestRunResult.Create(result, _leafResults)` сериализует лишь хвост и создаёт ложное
впечатление полного результата.

Канонический TestRunnerService patch для non-null terminal root рекурсивно собирает
leaf results из `ITestResultAdaptor.Children`. Пустая suite (`Test.IsSuite=true`) не
становится phantom testcase. Названия, состояние, message/stack/output каждого leaf
сериализует прежний production converter; summary и классификация исходов не меняются.
При null root остаётся прежний Unknown fallback. Engine objects в JSON не сохраняются.

Проверенная source цепь UTF package cache `com.unity.test-framework@76560ee600cb`
(Unity 6000.4.1f1, worker Library/PackageCache):

- UnityEditor.TestRunner/TestRun/Tasks/Events/RunFinishedInvocationEvent.cs:18–24:
  передаёт TestJobData.TestResults, fallback — editModeRunner.m_Runner.Result.
- UnityEditor.TestRunner/Api/CallbacksDelegator.cs:52–55: Factory.Create всего root.
- UnityEditor.TestRunner/Api/TestAdaptorFactory.cs:45–54: рекурсивно Create children.
- UnityEditor.TestRunner/Api/TestResultAdaptor.cs:15–35 сохраняет children и тот же
  NUnit result; ToXml():86–94 вызывает его ToXml(true).
- UnityEditor.TestRunner/Api/ResultsWriter.cs:97–100 записывает тот же ToXml.

Это обоснование использования native terminal tree, не доказательство конкретного
старого XML-файла. Дерево callback root и XML относятся к одному native результату;
текущий изменяемый файл AppData всё равно не является квитанцией произвольного ticket.

## Hash guards и upgrade

SHA256 TestJobManager после нормализации LF/CRLF:

- Original upstream: `80be4984f63398ba8d3484a514e131f131b9a562757e713b07d0ac73fc164476`.
- Прежний MCP2: `395272a8507666a4d88d3b72e69023a03f25499712f1cb895877dfb2ccff4a01`.
- Current: `cbc4952435ab4ef0a710726038eb4d4edd254e30592916d9b4c3d92e1bb0eaca`.

Дополнительные normalized SHA256 TestRunnerService:

- Original: `b5f25bffbcee394d746f4a67b7cfaba1bf3e24279f581d79fec7c20098d6ee23`.
- Current terminal tree: `291801256c293ef07f51bf53668fec4a5a64e5cede7c89f755614d75f5b545f9`.

Install-Upstream.ps1 применяет full `test-job-lifecycle-10.2.0.patch` к pinned upstream
и проверяет оба current hash. Apply-TestJobLifecycle.ps1 принимает только known пары:
original manager + original service, MCP2 manager + original service, callback-only
manager + original service либо current manager + current service. Смешанный/чужой
source pair отклоняется. Original получает cumulative patch, MCP2 —
`test-job-lifecycle-mcp2-to-current.patch`, callback-only —
`test-job-terminal-results-10.2.0.patch`; current pair — idempotent no-write.

Оба source и их существующие .meta сохраняются как raw backup в ignored stage.
Повторный guard и raw/normalized/meta CAS выполняются до первой записи, source/meta
проверяются перед каждым replace. Каждый файл заменяется атомарным File.Replace;
общей atomic filesystem transaction для двух файлов нет. При отказе второго replace
installer возвращает только свои candidate bytes к исходным raw bytes и сохраняет
transaction.json. Чужие source/meta после CAS не перезаписываются; такой rollback
помечается blocked и требует решения оператора по backup/evidence. .meta не записываются.
Fresh и оба upgrade пути проверены на побайтовое равенство результатов. Windows sharing
lock второго source подтвердил реальный rollback после первой замены. PowerShell scripts сохраняют прежний UTF-8 BOM для 5.1.

## Воспроизводимые managed проверки

Pinned ZIP CoplayDev unity-mcp commit `30d22075093d1d35dfb0091c1c7550e9ad948577`.
Точный локальный архив диагностики:
`F:/UnityProjects/Vr_Battlegrounds_ai/tmp/editor-reload-audit/upstream-10.2.0.zip`.
Наличие старого отчёта не заменяет проверку пути перед запуском.

```powershell
python Tools/UnityMcp/Tests/test-job-reload.test.py <archive.zip> --previous
python Tools/UnityMcp/Tests/test-job-reload.test.py <archive.zip> --callback-only
python Tools/UnityMcp/Tests/test-job-reload.test.py <archive.zip>
python Tools/UnityMcp/Tests/test-job-lifecycle.test.py <archive.zip>
python Tools/UnityMcp/Tests/test-job-installer.test.py <archive.zip>
python Tools/UnityMcp/Tests/output-install-fresh.test.py <archive.zip> <pwsh7.exe>
```

`--previous` закреплён historical MCP2 patch и проверяет точный прежний hash. RED:
2 passed/4 failed — receiver до update отсутствует, поздние failed/succeeded результаты
теряются, повторная initialization не восстанавливает receiver. Второй RED
`--callback-only` связывает manager CBC с pristine service: 7 passed/1 failed — native
summary обещает два теста, full results содержит только последний leaf. Первый failed
leaf и его metadata были сохранены в старом domain до переноса SessionState; для
изоляции volatile-list дефекта fixture вызывает production force-persist на boundary.
Current GREEN: 8/8, включая earlier failed output/stack, вложенный parameterized/skipped
leaf и empty suite без phantom result.
В исходном lifecycle harness дополнительно 8/8 и missing/wrong-type native API 1+1.
Guarded two-source installer PowerShell5.1 и fresh two-source installer PowerShell7 проходят offline fixtures.
Fresh PS5.1 этим изменением повторно не проверен; предыдущая длинная ZIP fixture имела
независимое ограничение MAX_PATH, не относящееся к callback patch.

Reload harness Compile Include связывает настоящие pinned TestJobManager,
TestRunnerService, ITestRunnerService и MCPServiceLocator; все четыре source hash
проверяются. Каждый сценарий загружает assembly в новый collectible AssemblyLoadContext:
production statics и locator сбрасываются, сохранённый SessionState явно переносится.
InitializeOnLoad attributes выполняются через RuntimeHelpers.RunClassConstructor;
native registry при initialization ещё false, затем внешняя framework boundary
передаёт RunStarted/leaf callbacks/RunFinished. Реальный production сериализатор выдаёт
полный Failed результат двух тестов (один намеренно failed) и Succeeded результат.
Повторная initialization не дублирует receiver; без active job receiver не создаётся;
expired inactive job получает обычный init timeout/result=null, без выдуманного успеха.

Подготовленный TestJobLifecycleProbe.cs.txt предназначен только для собственной lease:
Order0 intentional failure, Order1 prebuild receipt PASS, Order2 UnityTest IEnumerator
использует официальный RecompileScripts после однократного RequestScriptCompilation.
SessionState key включает текущий job_id и предотвращает повторный запрос при replay;
новый domain nonce и неизменный current job_id записываются в reload receipt. Paths:
reports/mcp-test-job-lifecycle-20261015/<job_id>/{prebuild,reload}-receipt.json — старый
receipt667 не подходит. Template скомпилирован против фактических Unity6000/UTF/NUnit DLL
без запуска Unity. Ожидаемая live delivery — 3 results (2 PASS + 1 intentional FAIL),
включая earlier failure до reload; физический reload и эти outcomes ещё не проверены.
RecompileScripts.Perform (UTF TestRunner/Messages/RecompileScripts.cs:80–111) даёт
первый yield null для установки editor runner yield-object; native EditModeRunner.cs:
263–269 готовит domain reload. Собственных while/reload-repeat в probe нет.

Это managed evidence, не Unity reload acceptance. Unity API, SessionState storage,
EditorStateCache/TestRunStatus и прочие неиспользуемые сервисы — boundary stubs; реальные
порядок загрузки Editor, сериализация Unity объектов, PlayMode и broker readiness ими
не подтверждены. Обязательны собственная lease, контролируемый live reload, сохранённый
job_id, поздние callbacks и полный terminal result; затем штатный finish/receive.

Патч не сохраняет полный Result в SessionState: upstream намеренно исключает его.
Дополнительный reload после terminal до получения результата всё ещё может потерять
payload. Durable receipt, восстановление старых результатов из XML и native client
reconnect остаются отдельными задачами; неизвестный test start не повторяется автоматически.
