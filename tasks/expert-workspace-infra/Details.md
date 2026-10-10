# Проект и результаты

Дата: 2026-10-10. Подготовлен [проект v1](design.md), ожидается рассмотрение.

Проверено: checkout является linked worktree; исходный HEAD 8fbf1cad460e507de5cd9a0a717c2c238b741126, tracked/untracked изменений нет. Hook требует допуска даже для чтения навыка и fetch; первичные документы созданы штатным исключением.

## База и координация

Register в песочнице вернул `attempt to write a readonly database`; штатный escalated register прошёл, revision1. Этап workspace admitted. Fetch/rebase origin/dev выполнены штатно с доступом к Git metadata вне writable root, без конфликтов; HEAD `1001415bed29124af821a9a641d0bae2a7224a5b`. После обновления прочитан pilot-handoff/Readme.md.

На старте режим хаба enforced. Inbox проверен: адресованных pending на момент старта0; широковещательные события есть. Native peer-session addressing и штатного Monitor у клиента нет; собственный route cleared epoch1. Не создавать unattended watcher. Обязательные свежие проверки выполняются вручную перед зависимыми действиями. Infra issue передан `agent-coordination-protocol`, event3119; ручная доставка, native route сопровождающего не подтверждён.

Во время анализа доставлен native сигнал3138, отдельно прочитан canonical event и status. Сопровождающий сообщил об отключении admission по поручению пользователя; config подтверждает mode off. Сам режим не менял, configure не повторял. Ownership/Unity leases/песочница сохранены. Свежий snapshot подтверждает4merged этапа пилота, включая handoff1001415b; snapshot.json пилота содержит старый running — учтено в проекте.

## Исследование

- Прочитан текущий AGENTS.md, указанные профили Codex/Claude, hand-rig-quality/expert, pilot-handoff и адресные §§13–14 Details.md пилота. Профили и исходные документы не изменены.
- Read-only explorer expert_analysis: структура существующих KB; avatar entry+analysis 386строк/33258символов, weapon обязательные KB380строк/27779символов. В hand-rig-quality/expert/sources.md:61 обнаружены3неразрешимые ссылки ../../Readme.md, ../../Details.md, ../../tool.md. Это находки исследования, не исправления в чужой области.
- Read-only explorer pilot_analysis: карта завершённых этапов/383/negative377/ограничений. Principal перечитал ключевые §14 и snapshot. Полные raw logs не переаудированы. Reports отсутствуют в нашем checkout; исходные ссылки сохранены.
- Read-only explorer hub_readonly_design: source/deployed cli.py,store.py,inbox.py совпадают по digest; status имеет readonly handler, inbox/events/watch-inbox конструируют writable store. Новый awake не должен вызывать их автоматически. Общую SQLite БД explorer не открывал.
- Проверена официальная документация SQLite WAL: mode=ro сам по себе не гарантирует отсутствие создания sidecars; поэтому проект требует read-only защиты файлов для live adapter, иначе unavailable/очищенный DTO.

## Состояние

Реализация и инфраструктурные тесты ещё не запускались. Пилот не мигрирован, чистая сессия acceptance pending. Применён brainstorming architectural gate: сначала рассмотрение письменного проекта, затем план и выбор исполнения. Документы хранятся в tasks/ согласно AGENTS, не в общем индексе.

Формальные проверки документации: plan.json разобран ConvertFrom-Json; git diff --check без ошибок (документы ещё untracked на момент проверки). Штатный check_text_eol пропустил4пути как non_lf_policy, это не LF PASS. Дополнительное чтение физических bytes всех4файлов: CR0, BOMfalse. Игровые/Unity проверки не выполнялись.

Пользователь одобрил проект v1. Подготовлен implementation-plan.md (5этапов, интерфейсы и приёмочные проверки), рекомендуется основной агент с финальным независимым ревью. До review плана реализация не начинается. Event3138 отдельно прочитан principal и ACK-нут, никакого автоматического ACK через awake нет.

Event3219 прочитан после отдельного native сигнала: сопровождающий выпустил off-audit, mode off сохранён. Shared runtime не менялся нами. Данные хаба потенциально обновляются независимо от базы исследования; проектный reader обязан показывать дату и unknown/schema mismatch.

Git attributes корня задают task-docs eol=crlf, хотя новые физические файлы LF. Добавлена локальная .gitattributes только для собственной папки с LF для новых managed extensions; чужие legacy файлы не нормализуются. Это документационная настройка, без принятия candidate-кода.
