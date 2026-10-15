# Общая инструкция сборок: candidate сопровождающему

2026-10-15. Пользователь поручил обновить инструкции всех агентов и обеспечить команды сборки
в уже развёрнутых выделенных checkout. Launch-infra владеет продуктовыми дирижёрами и Docs/release.md;
общая инструкция AGENTS/hooks выпускается её сопровождающим. Этот файл — предложение, не допуск.

Предлагаемый абзац для AGENTS.md «Checkout, Unity and coordination»:

> Обычные сборки плееров агенты выполняют штатными Tools/release/Build-Game.ps1 и
> Tools/e2e/Run-QuestE2E.ps1 из своего task-worktree в назначенной паре выделенных
> build-{test,prod}-{win64,android} с LFS-ролью build/full. Перед -Ref или запуском Unity
> получить явный допуск batch и назначение общей пары от сопровождающего;
> закрытый Editor не резервирует checkout. До решения7288 только dry-run безRef/Unity.
> Обоим checkout задаётся один полный immutable SHA. Agent-worktree, основной Editor и
> интерактивный unity-editor-worker не служат fallback для обычной сборки. Команды, dry-run,
> логи и артефакты — Docs/release.md, «Агенту: сборка в выделенном checkout».

Нужно решение владельца инфраструктуры: существующая общая формулировка требует RUNNING
worker lease для Unity-мутаций linked worktree, а Docs/release.md описывает отдельный batch
без очереди интерактивного worker. Указать штатный допуск/назначение build-пары и согласовать
область исключения для назначенной batch-сборки. Launch-infra не добавляет свой lease/mutex,
не чинит/не обходит broker и не запускает batch до решения владельца.

Проверка развёртывания: четыре clean worktree на93d7dfbc3, role=build,8828 LFS present,
missing0/pointer0. Library/Build отсутствуют; первые/повторные сборки ещё не доказаны.
Отчёт: reports/2026-10-15-build-checkouts-antigravity/audit.md.

Ответ владельца7514 (2026-10-15): исходное решение пользователя о dedicated batch без FIFO/lease
интерактивного worker подтверждено по agent-infra/docs/maintainer-handoff.md:79. Проблема конкретна:
Build-Game не делает pair reservation до Ref; UnityLockfile — только моментальный preflight.
Worker lease guard ограничен config.editor_root и не выдаёт capability четырём build roots.
Root принял отдельный адресный хвост: явное назначение пар + batch admission/reservation,
затем общий пункт AGENTS отдельным candidate. До этого существующее ограничение noRef/noUnity
сохраняется. 7457/7492 относятся к GUI worker/MCP, не к этому назначению.
