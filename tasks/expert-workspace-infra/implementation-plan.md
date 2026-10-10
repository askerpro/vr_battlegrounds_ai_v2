# План реализации рабочих зон экспертов v1

> Для исполнителей: использовать `superpowers:executing-plans` при исполнении основной сессией либо `superpowers:subagent-driven-development` при выбранном исполнении субагентами. Отмечать завершение только по фактическим проверкам.

Дата: 2026-10-10. Статус: план для рассмотрения; проект v1 одобрен пользователем.

**Цель:** детерминированный read-only CLI, шаблоны и пилот, позволяющие восстановить контекст без роста стартового пакета.

**Архитектура:** переносимый candidate использует обязательный JSON manifest,
один каталог маленьких карточек и указатель первичных источников. Общий сборщик
получает ограниченный DTO координации; отдельный project adapter читает хаб.
Предметные данные находятся в `experts/vr-test-stand/`, исходники/evidence остаются на месте.

**Стек:** Python 3.10+, стандартная библиотека, unittest, UTF-8/LF; без pip/LLM/Unity.

**Проект:** [design.md](design.md).

## Общие ограничения

- Жёсткий максимум stdout — 16 384 байта UTF-8, включая оформление и LF.
- Default и максимальный limit списка — 10; максимум раскрытых direct dependency cards в awake — 3.
- Размер JSON-карточки — не более8192bytes; неизвестные поля/тип/версия схемы отклоняются.
- Все обязательные границы/инварианты и mandatory selected context выводятся полностью либо явный отказ.
- Нет file/cache/log/index writes из CLI, ACK/leases/work execution, импорта shared runtime или CLI inbox/events/watch-inbox.
- Python `-B`; entry point отключает bytecode до локальных imports.
- Хаб — источник actual coordination; режим off не даёт ownership. Неизвестность не становится разрешением.
- Legacy paths/task-id/contracts/evidence, gameplay/профили экспертов не меняются.
- Кандидат остаётся в согласованном worktree; deployment в agent-infra отдельно сопровождающим.
- Отчёты инфраструктурной проверки в ignored reports/, без force-add и секретов.

## Фокус проверки

1. Длинная обязательная часть/многобайтовая кириллица: controlled error вместо обрезки инвариантов.
2. Явный backlog выбор при более свежем completed: selected ID сохраняется, работа не начинается.
3. Missing/cycle/external dependency и изменение каталога между страницами: ограничения явно видны.
4. Missing/locked/WAL hub или неизвестная схема: локальный core остаётся; writable fallback запрещён.
5. Revoked historical result и отсутствующий evidence: старый PASS не становится новой приёмкой.

## Файлы и интерфейсы

Корень реализации ниже: `tasks/expert-workspace-infra/tools/candidate/`.

| Файл | Ответственность |
|---|---|
| `awake.py` | Тонкий entry point, no bytecode, stdout UTF-8 |
| `expert_workspace/model.py` | Схема, загрузка/проверка JSON, безопасные типизированные ссылки, DTO |
| `expert_workspace/catalog.py` | Единственный каталог, фильтрация/поиск/сортировка/cursor |
| `expert_workspace/context.py` | Отбор контекста, критическая часть, бюджет, omissions, rendering |
| `expert_workspace/cli.py` | argparse/dispatch/errors/next commands, запрет автоматического действия |
| `adapters/coordination.py` | Изолированная проекция project hub, без runtime imports и domain names |
| `schemas/*.schema.json` | Документированные workspace/card/source/coordination-view схемы |
| `templates/*` | Минимальная зона/карточка, lifecycle регламент, короткие adapters Codex/Claude |
| `tests/test_*.py`, `tests/fixtures/audio-assets/` | Контрактные инфраструктурные проверки и другое направление |
| `experts/vr-test-stand/*` | Предметное наполнение пилота и ссылки |
| `tasks/expert-workspace-infra/migration.md`, `usage.md` | Миграция и краткая инструкция использования |

Общие публичные функции, используемые соседними модулями:

- `load_workspace(path: Path) -> Workspace`: manifest, resolved roots, sources, diagnostics; не читает большие документы.
- `load_catalog(workspace: Workspace) -> Catalog`: карточки/index/digest/errors, один каталог.
- `select_page(catalog: Catalog, query: Query) -> Page`: filter/search/sort/paging, next cursor и точные counts.
- `get_card(catalog: Catalog, task_id: str) -> dict`: точный выбор, unknown ID вызывает типизированную ошибку.
- `read_view(workspace: Workspace, selected: dict | None, as_of: datetime, mode: str, view_path: Path | None) -> dict`: validated DTO; off не вызывает adapter.
- `build_awake(workspace: Workspace, catalog: Catalog, selected: dict | None, view: dict, query: Query, as_of: date) -> dict`: полный core/selected, bounded dependencies/options/navigation.
- `render_bounded(package: dict, format: str) -> bytes`: UTF-8/LF <=16384bytes или типизированная ошибка mandatory_context_over_budget.
- `main(argv: list[str] | None = None) -> int`: только чтение, controlled exit codes0/2.

Простые dataclasses Workspace/Catalog/Query/Page определяются в model.py; mutable
payload не создаёт второй каталог на диске. Query содержит status tuple, tag,
query string, sort (`priority` default), limit10, cursor. Cursor — URL-safe JSON
с v1, catalog digest, параметрами запроса и offset; digest mismatch не продолжает страницу.

## 1. Схемы и каталог

**Файлы:** model.py, catalog.py, schemas/, test_catalog.py, другая предметная fixture.
**Выход:** load_workspace/load_catalog/get_card/select_page; фиксированные входы всех следующих этапов.

- [ ] Создать failing unittest cases: valid fixture, schema mismatch, duplicate ID, invalid card, ссылка за catalog root/symlink, missing dependency, Unicode search/completed/status/tag, priority/updated/id ties, paging без пропусков, stale cursor.
- [ ] Запустить `python -B -X utf8 -m unittest discover -s tasks/expert-workspace-infra/tools/candidate/tests -p test_catalog.py -v`; подтвердить RED из отсутствующего модуля/функциональности.
- [ ] Реализовать модели/валидатор/схемы по design. Не читать report bodies; optional external evidence проверяется stat и явным unavailable.
- [ ] Создать минимальную audio-assets fixture: две сущности и соседний владелец, минимум одна completed и одна backlog карточка, принятый historical decision и evidence unavailable. В общем коде не должно быть этого названия или task-id пилота.
- [ ] Запустить те же тесты, подтвердить GREEN и read-only API отсутствия side effects при загрузке.

## 2. Бюджет и read-only CLI

**Файлы:** context.py, cli.py, awake.py, test_context.py, test_cli.py.
**Вход:** интерфейсы этапа1. **Выход:** awake/tasks/search/show/check и text/json stdout.

- [ ] Создать failing cases: explicit backlog wins, no selection when task omitted, unknown ID не выбирает другую задачу, deterministic repeated runs, 100/1000records <=16384bytes, карточки<=10, hidden counts, full mandatory invariants, oversized core/selected explicit failure, direct deps3 + hidden pointers, cycle termination, кириллица UTF-8.
- [ ] Запустить unittest для test_context.py/test_cli.py, подтвердить RED.
- [ ] Реализовать critical-first сборку и serialized byte budget; optional records исключаются целиком, footer/next commands обязательны. show отдаёт полную малую карточку и навигацию, check — счётчики/ошибки в том же бюджете.
- [ ] Добавить CLI параметры точно как design; upper limit10, filters повторяемые. stdout и stderr не выводят необработанные exceptions/credential values.
- [ ] Добавить subprocess checks всех команд с snapshots файлов/метаданных fixture; запрещённые write/ACK/lease вызовы должны приводить к failure теста. Отсутствующий hub не создаётся, imports не создают pycache.
- [ ] Запустить tests, проверить deterministic JSON/text bytes и команды next-page на path with spaces.

## 3. Координационный DTO и adapter

**Файлы:** adapters/coordination.py, coordination-view.schema.json, test_coordination.py.
**Вход:** Workspace + явно выбранная карточка + источники зависимостей, never implicit ownership.
**Выход:** read_view с availability/live|snapshot/stale/unavailable, sanitized projections.

- [ ] Создать failing cases: disabled/off mode provider, missing DB/no mkdir, locked DB timeout, unknown schema, readonly fixture with selected task/contracts/pending ID, stale snapshot, irrelevant tasks omitted, token/body/spec secrets не сериализуются, mode off отражён без допуска.
- [ ] Запустить test_coordination.py, подтвердить RED.
- [ ] Реализовать фиксированные параметризованные проекции current hub schema. Обнаружить Git common-dir без mutation; SQL mode=ro/query_only/short transaction, <=2s timeout; нет SELECT полного data, SQL из manifest, runtime imports/CLI вызовов.
- [ ] Реализовать safe path: live только при защищённом read-only доступе; если гарантия доступа не подтверждена, вернуть unavailable. Для WAL без нужных read-only sidecars нет immutable/writable fallback. Fixture обеспечивает реальную защиту чтения для положительного теста.
- [ ] Проверить untrusted snapshot schema/byte ceiling, observed_at и five-minute staleness. Отсутствующие revisions/counts/sections unknown. DTO не ACK-ает pending.
- [ ] Проверить hashes/metadata DB+sidecars+zone до/после под sandbox/read-only fixture. Прямое чтение live хаба проекта выполнять только при подтверждённой защите; иначе честно сохранить ограничение и проверенный snapshot путь.
- [ ] Запустить весь infrastructure suite. Если READONLY/WAL условия текущего окружения не позволяют live — не ремонтировать общий хаб, recorded unavailable acceptance case обязателен.

## 4. Шаблоны и миграция пилота

**Файлы:** templates/, experts/vr-test-stand/, migration.md, usage.md, test_pilot.py.
**Вход:** работающий CLI/schema и исходный pilot-handoff. **Выход:** рабочая зона и процедура сопровождения.

- [ ] Создать failing pilot checks по заданиям handoff: boundaries/entity ownership, порты accepted/input/base/SHA, revoked377, selected xr-input, play-launch-control/map-startup-route owners, optional logs383, stale local running vs fresh merged.
- [ ] Запустить test_pilot.py, подтвердить RED до создания зоны.
- [ ] Создать10карточек:4completed, revoked audit377 (lifecycle completed, validation revoked),5backlog candidates. Сохранить canonical IDs отдельно от карточек; контрактные ссылки и SHA только из исходных источников.
- [ ] Создать компактное ядро/архитектуру/source index, указать ограничения и evidence availability. Не переносить Details/raw logs; не обновлять существующие чужие task docs или роли.
- [ ] Создать generic template зоны и README entry; lifecycle обновления одного основного факта; Codex TOML и Claude Markdown snippets только с placeholders без выдачи owner.
- [ ] Запустить check/awake/show/search/tasks на пилоте и audio fixture; описать конкретные команды в usage.md.
- [ ] Составить migration.md: source mapping,4этапа и старый handoff running, historical verification distinctions, сохранённые пути, missing evidence, unresolved gameplay/XR/Quest и live hub ограничения.

## 5. Чистая сессия, ревью и итог

**Файлы:** reports/ (ignored), Readme.md/Details.md/plan.json (principal), исправления по результатам проверки.

- [ ] Запустить полный unittest suite и CLI exercised commands; stdout summaries/counts, полные результаты в ignored reports. Проверить сохранение файлов и отсутствие секретов.
- [ ] Поручить свежему read-only subagent (`fork_turns=none`) только путь candidate/зоны и задания pilot-handoff, без ответов или истории. Он восстанавливает контекст через awake + адресную навигацию, фиксирует число обращений/выводы/ограничения в reports; не играет в Unity и не получает ownership.
- [ ] Principal проверяет ответы по original sources и actual CLI outputs. Ошибки восстановления исправляются в каталоге/механизме и повторяется только затронутый gate.
- [ ] Независимое финальное read-only ревью общего кода: domain independence, bounded output/errors, no writes/authority, foreign scopes preserved, schemas/DTO consistent. Principal инспектирует relevant diffs и подтверждает проверки.
- [ ] Проверить физический LF только owned paths, staged blobs при checkpoint; JSON parse/schema, git diff --check. Reports не staged.
- [ ] Обновить компактный task Readme/plan actual state и передать результат: design/tool/template links, test counts/scopes, migration report, короткие CLI команды и pending acceptance/deployment отдельно.
- [ ] Код/candidate не принимать в origin/dev без пользовательской проверки. Документационный checkpoint допустим; общую инфраструктуру публикует её сопровождающий штатным маршрутом.

## Самопроверка плана

Все разделы design покрыты пятью этапами. Общие interfaces определены до зависимых
модулей, source data отделены от backend. Все пять review risks имеют конкретные
checks; тесты затрагивают инфраструктуру, не gameplay. Сам pipeline и tests не
расширяют pilot scope. План ещё не исполнен.

Рекомендованный способ: основная сессия (`executing-plans`) с отдельным финальным
ревью; тесные интерфейсы и один read-only dataflow делают его дешевле параллельной
реализации. Чистая session acceptance всё равно отдельная независимо от способа.
