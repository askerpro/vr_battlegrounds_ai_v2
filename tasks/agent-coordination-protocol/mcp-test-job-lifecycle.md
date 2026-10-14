# Жизненный цикл MCP-тестов — 2026-10-15

## Подтверждённый отказ

Заявка bots-fix #666: job `78b1247567f341a683acf172d55267ce` начат
2026-10-15 00:27:46 MSK. Через 15,702 с GetJob объявил Failed с нулём
завершённых тестов, total/result=null и очистил current job. Editor.log показывает
IPrebuildSetup Unity.PerformanceTesting до этого отказа и RunStarted после него.
GetJob считает отсутствие total признаком отсутствия запуска, хотя нативный
Test Runner уже выполняет подготовку. Очистка current лишает поздние callbacks
связи с заданием. Заявка штатно завершена и получена; её игровые проверки не приняты.

Второй подтверждённый дефект: ToSerializable скрывает сохранённый Result у Failed.
Отказ тестов должен возвращать результаты так же, как успешный прогон.

## Ограниченное исправление

- SDK-патч unity-mcp № 2, резерв `mcp-test-job-lifecycle-20261015`.
- Канонический vendor patch MCP 10.2.0 и его установщики в Tools/UnityMcp.
- Истечение времени инициализации допускается только при подтверждённо
  неактивном нативном Test Runner. Активный prebuild сохраняет Running/current.
  Недоступная проверка состояния не является доказательством завершения.
- Reflection использует существующий internal TestRunnerApi.IsRunActive;
  публичного IsTestRunActive в установленном Unity Test Framework 1.6.0 нет.
- Непустой Result возвращается для завершённых Succeeded и Failed.
- Domain reload persistence и потерянная квитанция run_tests остаются отдельными
  дефектами. Старые операции с неизвестным исходом не повторяются.

## Проверка и выпуск

1. Управляемый стенд связывает настоящий TestJobManager.cs с заглушками Unity:
   RED исходной версии, GREEN патча; активная подготовка, поздние callbacks,
   настоящий timeout, недоступная нативная проверка, результат Failed.
2. Установщик проверяет исходный hash, готовит staging, повторяет guard/hash,
   сохраняет backup и отказывает при чужом diff. Проверить повторную установку,
   отказ без capability и запрет записи в main на отдельном fixture.
3. Независимое ревью и проверка diff основным агентом до установки.
4. Технический checkpoint candidate. Живой worker меняет только основной
   сопровождающий в своей аренде; backup и существующие .meta сохраняются.
5. Компиляция, клиентский initialize и отдельный адресный тестовый прогон.
   Затем штатный инфраструктурный выпуск и проверка состояния очереди.

## Состояние

Реализация делегирована runner_diagnosis, независимое ревью — test_job_review.
Основной агент проверил diff и installer fixtures. Независимый production harness:
RED 5 PASS/3 FAIL, GREEN 8 PASS и по 1 PASS для missing/wrong-type API.
Guarded installer PS5.1 PASS; fresh installer фактически доступным pwsh7 PASS.
PS5.1 fresh fixture на длинном пути упирается в MAX_PATH до применения патчей.

Технический source checkpoint: `679a8783006991be7340defb901e4a2924ec2b56`.
Билет #667, единственный оператор — сопровождающий. Guard подтвердил собственную
RUNNING-аренду, верный worker root и отсутствие Play/compile/import/tests.
Apply сохранил raw backup и установил проверенный hash. После полного импорта
подтверждена загрузка private NativeRunActiveOrUnknown и тестовой сборки.

Живой job `936c016152614e20bfcec27a1a3e397d`: в настоящем IPrebuildSetup через
24 942 мс native_active=true, total=null, status=Running, current ID сохранён.
Затем completed=2/total=2, Result содержит 1 PASS и 1 намеренный FAIL с marker
AGENT_INFRA_EXPECTED_FAILURE_RESULT_PROBE, сообщением и стеком. Это ожидаемый
результат инфраструктурного стенда, не отказ исправления и не игровая приёмка.
Квитанции: reports/mcp-test-job-lifecycle-20261015/.

Finish #667 завершён exit0/DONE. R `2a7d7216b7553528b8bf9ea6e38601db1f39671c`,
ref `refs/heads/codex/tmp/result-667-7365c67bc89c406d96f4d57b0f77fc25` сохраняет
пять собственных файлов стенда и .meta, unexpected=[]; технический результат
AVAILABLE сохраняется архивом, временный стенд не входит в игровой выпуск.
Worker восстановлен на B `54673ba496688efbb5c0dd1aaba6287cbe8feb4a`, Git clean,
active=null, blocked=false, recovery_required=false. MCP initialize точной
командой клиента PASS, resources=19. Runtime VERSION не менялся: исправлен
embedded MCP-пакет, а не код broker runtime.

Cleanup park завершился за 833 мс; последующее восстановление диска/refresh заняло
несколько минут. DelayCall-warning после reload остаётся отдельной диагностикой:
он не блокировал штатный park/restore. Старые UNKNOWN и сохранение полного Result
через domain reload этим ограниченным патчем не исправлены.

По решению пользователя 2026-10-15 в AGENTS.md и правилах координации/MCP закреплены
немедленная эскалация без вопроса пользователю и запрет самостоятельного ремонта
или обходов инфраструктуры владельцами задач. Уведомление worker_ready #7257
записано в общий inbox и отправлено в 11 проверенных текущих Codex-чатов; отправка
не означает чтение. Штатный выпуск candidate ещё выполняется.
