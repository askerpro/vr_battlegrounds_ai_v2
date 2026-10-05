# План реализации контроллера Unity

> Для исполнителей: superpowers:subagent-driven-development; пользователь уже разрешил выполнение.

**Цель:** изоляция worktree, FIFO и двусторонние checkpoint с проверенным возвратом базы.
**Архитектура:** внешний Python-контроллер, SQLite, файловый Unity-мост с отдельной Editor asmdef.
**Стек:** Python 3.10+, SQLite, Git, Unity 6000.4.1f1. Без новых внешних зависимостей.
**Контракт:** [editor-broker-spec.md](editor-broker-spec.md).

## Общие ограничения

- Все изменения только в worktree editor-worktree-protocol; основной checkout не трогать.
- Русские комментарии/документация. Нативный apply_patch. Не коммитить принятую реализацию.
- Технические snapshot используют alternate index + commit-tree, не меняют HEAD/index и не вызывают hooks.
- Разрешены тесты инфраструктуры: запрет тестирования непроверенных игровых механик не применим.
- Общий Editor не переключать; проверка моста — отдельный минимальный проект.

## Разделение файлов и интерфейсы

### 1. Очередь (субагент queue)

Файлы: Tools/agents/editor_broker/queue.py; tests/test_editor_broker_queue.py.
`BrokerStore(state_dir, clock=time.time, offer_seconds=60, lease_seconds=900)`.
`enqueue(owner, base_sha, input_sha, agent_root, output_roots, request_key) -> dict`;
`claim(ticket_id, owner) -> dict` (token/epoch); `validate(ticket_id, token) -> dict`;
`renew(ticket_id, token) -> dict`; `transition(ticket_id, token, phase, **details) -> dict`;
`complete(ticket_id, token, result_sha=None, restored=True) -> dict`;
`cancel(ticket_id, owner)`, `status()`, `get(ticket_id)`, `events(owner, after=0)`,
`wait(ticket_id, owner, timeout=60)`, `ack_result(ticket_id, owner)`.
Контроллер задаёт baseline/editor-root в config.json вне SQLite; очередь хранит неизменяемые поля заявки.

- [x] Красные тесты FIFO/offer timeout/active expiry/idempotency/fencing/нескольких процессов.
- [x] Реализация и зелёный прогон; отдельное ревью.

### 2. Git/checkpoint (субагент git)

Файлы: Tools/agents/editor_broker/git_state.py; tests/test_editor_broker_git.py.
`GitState(root)`; `head()`, `common_dir()`, `is_clean()`, `is_ancestor(base, sha)`,
`checkpoint(state_dir, label, paths=None, parent=None) -> dict` (sha/ref/paths/tree),
`switch_detached(sha)`, `capture_result(state_dir, input_sha, output_roots, label) -> dict`,
`restore_captured(result_sha, baseline_sha)`; `receive_result(input_sha, result_sha) -> dict`.
checkpoint с paths=None фиксирует весь tracked/untracked исходный diff; опасные пути/кэши запрещены.
capture_result сохраняет полный diff и возвращает unexpected список; output_roots могут включать
Assets и исходники, но не Library/Temp/.git/локальный мост. Receive применяет только A..R,
отказывает без мутации при пересечении и сохраняет HEAD/index пользователя.

- [x] Красные тесты на настоящем Git, hook sentinel, index preservation, бинарники/.meta/конфликт.
- [x] Реализация и зелёный прогон; отдельное ревью.

### 3. Unity-мост (субагент unity)

Файлы: Tools/agents/editor_broker/unity.py; Tools/agents/unity_bridge/*;
tests/test_editor_broker_unity.py; независимый C# compile/smoke harness.
`FileUnityAdapter(editor_root, state_dir, timeout=120)`;
`inspect() -> dict`; `park() -> dict`; `refresh() -> dict`;
`save_outputs(output_roots) -> dict`; `restore(setup) -> dict`.
Все результаты JSON snake_case: project_root, ready, is_playing, is_compiling, is_updating,
is_test_running, dirty_scenes, dirty_assets, prefab_stage, process_id, session_id.
park возвращает setup (scenes/prefab). При небезопасном состоянии/таймауте — исключение.
`install_bridge(editor_root, state_dir) -> dict` безопасно ставит только известную версию,
проверяет ignored путь, не перезаписывает неизвестные исходники. Имена mailbox совпадают
между adapter и C#; договорить внутри этой задачи, соседние части используют только API.

- [x] Красные тесты запросов/timeout/idempotent response, compile harness.
- [x] Реализация моста, тесты и изолированный Unity smoke; отдельное ревью.

### 4. Интеграция (основной агент)

Файлы: editor_broker/service.py, cli.py, __init__.py; Tools/agents/editor-broker.py;
tests/test_editor_broker_service.py; Docs/agents/editor-broker.md;
AGENTS.md, .agents/rules/unity_sharing.md, Docs/README.md, Docs/CHANGELOG.md, .gitignore.
CLI: init/checkpoint/request/wait/watch-ticket/claim/begin/guard/renew/finish/recover/receive/ack/status/events/cancel/publish-base.
OS/file mutex охватывает весь Unity/Git переход; фазы и setup фиксируются до изменений.
Очередь — единственный источник аренды. Старый инструмент захвата удалён по решению пользователя.
Постоянное состояние и runtime находятся в .agent-state/editor-broker, исходники — в Tools/agents.

- [x] Красный end-to-end fake-adapter тест: A -> generated .meta/binary -> R -> B -> receive.
- [x] CLI и lifecycle; тесты сбоя/восстановления/чужих изменений, event wait.
- [x] Документация активации и пределы гарантии; независимое итоговое ревью.
- [x] Полный зелёный прогон инфраструктуры и компиляция Unity-моста.

## Проверенный результат

2026-10-05: полный инфраструктурный прогон — 96 тестов за 102.384s, один OS symlink fixture
пропущен из-за прав Windows. Проверки старого инструмента удалены вместе с ним при внедрении.
Git mode 120000 проверен отдельным тестом без требования создавать OS symlink.
Нативная компиляция Unity 6000.4.1f1 и 11 smoke-сценариев в отдельном минимальном проекте
прошли, включая настоящий цикл A -> generated asset/.meta/binary -> R -> B -> receive.
Очередь, Git и интеграция прошли отдельные ревью после исправлений аварийных границ.
Активация в общем Editor и приём игровых изменений в эту реализацию не входят.

## Фокус ревью

Потеря результата при сбое; два владельца после истечения аренды; stale base; пропущенные новые
.meta; изменение hooks/config/index общего репозитория; случайное удаление ignored кэшей;
MCP/bridge timeout посреди компиляции; dirty-сцена из прежнего состояния; чужой .git worktree.
