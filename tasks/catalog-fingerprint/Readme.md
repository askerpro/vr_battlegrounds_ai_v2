# catalog-fingerprint — отпечатки каталога карт и preflight

Владелец — эксперт `gameplay`. Обновлено 2026-10-14 (передача Claude → Codex). Машинный план — [plan.json](plan.json).

## Статус

| Этап | Состояние | Где |
|---|---|---|
| `env-independent` | влит, принят пользователем 2026-10-12 | origin/dev e330b6162 |
| `preview-loaded-scene` | код готов, **Unity не проверен**, этап в хабе `planned` (остановлен при передаче) | ветка `claude/preview-loaded-scene`, HEAD 3eb3aff7f (код e219e2341) |
| `accepted-tests` | не начат | после `preview-loaded-scene ≥ merged` |

## Что сделано

- `env-independent`: `MapRunPreflight.AssetHash` = SHA256 содержимого файла + `.meta`, текстовый YAML Unity с CRLF→LF
  (как в Git), формат v2. Причина: `GetAssetDependencyHash` зависел от EOL рабочей копии → ложные `Catalog.*.Stale` и
  риск «разные сборки» клиент/сервер (`MapCatalogBuildStep` пересобирает каталог при сборке, `MapBootstrap.cs:690`
  сверяет отпечаток). Проверено в аренде 503 (CRLF-копия worker): RebakeCatalog 7/7, 14 отпечатков = расчёт по
  LF-blob Git, EditMode Maps 253 (6 известных FAIL level-design вне этапа), Play лобби Ready.
- `preview-loaded-scene` (находка launch-infra 5478, дизайн согласован 5483): если сцена карты уже загружена в Editor,
  `Validate` проверяет её саму (общий `ValidateSceneContent`), preview не открывает; dirty — отказ `Scene.Dirty:<map>`
  с подсказкой сохранить; иначе прежний путь `OpenPreviewScene`. Офлайн-компиляция Editor-сборки — 0 ошибок.

## Следующий шаг

1. `git switch claude/preview-loaded-scene`, rebase на origin/dev (там материалы лобби URP 77ef97d15), `begin` этапа
   `preview-loaded-scene`, checkpoint → request (`--task catalog-fingerprint`) → claim → begin → guard.
2. В аренде: (а) RebakeCatalog с открытой сохранённой Lobby — passed, сцена не закрыта/не сохранена; (б) Lobby dirty
   (`EditorSceneManager.MarkSceneDirty`) → отказ `Scene.Dirty:Lobby`, сцена осталась dirty; (в) сборка e2e-плеера с
   открытой Lobby (`E2EPlayerBuilder.Run` через одноразовый `EditorApplication.update`, не delayCall) — нет ошибки
   sceneId; (г) RebakeCatalog без открытых карт — как раньше. Результат аренды со сборкой меняет
   `Assets/URPDefaultResources/PC.asset` (известный побочный эффект сборщика launch-infra) — не принимать (`receive`).
3. Показать результат пользователю → приёмка → вливание → сообщить launch-infra (inbox quest-perf-pipeline: обход
   «открыть пустую сцену перед сборкой» в Docs/testing.md больше не нужен) и level-design (lobby-decoration).
4. `accepted-tests`: `Assets/Tests/EditMode/Maps/MapRunPreflightTests.cs` (+.meta) — CRLF и LF дают один отпечаток;
   загруженная чистая / dirty сцена.

## Ждёт пользователя

- Приёмка `preview-loaded-scene` после Unity-прогона.

## Решения пользователя

- 2026-10-12: «бейкер утилиту исправь» — `env-independent`; weapon-system на время исправления разрешён вариант А
  (одна строка отпечатка лобби вручную, 4912). «принимаю» — `env-independent`.
- 2026-10-13: «идем по твоим рекомендациям — arsenal пока откладываем» — `preview-loaded-scene` взят в работу.

## Риски и ловушки

- Каталог пересобирать только `MapBootstrapMigration.RebakeCatalog`, YAML руками не править.
- Отпечатки v2 зависят только от содержимого; любая правка сцены/MapData/реестра режимов/пресета требует rebake.
- Не проверено: две реальные сборки (сервер/клиент) из разных рабочих копий.

## Коммуникация (для Codex)

Inbox: `python Tools/agents/coordination.py inbox --owner gameplay --unread --as "<свой адрес>"`; ожидание —
`wait-inbox --owner gameplay --as "<свой адрес>"` фоновой командой. Вопросы пользователю — явным сообщением с пометкой
«ЖДЁТ ОТВЕТА» и дублем в «Ждёт пользователя».
