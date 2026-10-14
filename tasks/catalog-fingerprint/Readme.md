# catalog-fingerprint — отпечатки каталога карт и preflight

Владелец — эксперт `gameplay`. Обновлено 2026-10-14 (Codex возобновил работу). Машинный план — [plan.json](plan.json),
подробности и условия проверки — [Details.md](Details.md).

## Статус

| Этап | Состояние | Где |
|---|---|---|
| `env-independent` | влит, принят пользователем 2026-10-12 | origin/dev e330b6162 |
| `preview-loaded-scene` | принят пользователем 2026-10-14; Unity 3/3 preflight PASS, Windows E2E build PASS; вливание | ветка `claude/preview-loaded-scene`, код 02d367fe7, база d3c62ca5e; аренда 625 освобождена |
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
- 2026-10-14, аренда 625, вход 68380ccb532f: чистая Lobby / dirty Lobby / ни одной открытой карты — 3/3 PASS.
  Windows E2E-плеер собран за 967 с с открытой Lobby, ошибок 0; setup, dirty=false и sceneId 701398374 сохранены.
  Сырой составной отчёт `passed=false` сравнивал ещё transient InstanceID, изменившийся при сборке;
  это не критерий preflight. Исходный отчёт сохранён; подробная расшифровка — Details.md.

## Следующий шаг

1. Приёмка получена 2026-10-14: «Принимаю, влить». Выполнить fast-forward публикацию принятого этапа;
   тесты арсенала выполняются независимо.
2. Результат аренды 84bc9c0de949 содержит только побочный `PC.asset`; `receive` не выполнялся.
3. После приёмки: вливание → сообщить launch-infra (inbox quest-perf-pipeline: обход
   «открыть пустую сцену перед сборкой» в Docs/testing.md больше не нужен) и level-design (lobby-decoration).
4. `accepted-tests`: `Assets/Tests/EditMode/Maps/MapRunPreflightTests.cs` (+.meta) — CRLF и LF дают один отпечаток;
   загруженная чистая / dirty сцена.

## Ждёт пользователя

- Ничего для `preview-loaded-scene`: принят пользователем. `accepted-tests` — отдельная следующая приёмка.

## Решения пользователя

- 2026-10-12: «бейкер утилиту исправь» — `env-independent`; weapon-system на время исправления разрешён вариант А
  (одна строка отпечатка лобби вручную, 4912). «принимаю» — `env-independent`.
- 2026-10-13: «идем по твоим рекомендациям — arsenal пока откладываем» — `preview-loaded-scene` взят в работу.
- 2026-10-14: «Принимаю, влить (рекомендуется)» в диалоге приёмки текущего прогона — `preview-loaded-scene`.

## Риски и ловушки

- Каталог пересобирать только `MapBootstrapMigration.RebakeCatalog`, YAML руками не править.
- Отпечатки v2 зависят только от содержимого; любая правка сцены/MapData/реестра режимов/пресета требует rebake.
- Не проверено: две реальные сборки (сервер/клиент) из разных рабочих копий.

## Коммуникация (для Codex)

Inbox разобран 2026-10-14; передача подтверждена событием 6559. Устаревший маршрут Claude очищен.
Вопросы и приёмка пользователя — через доступный инструмент запроса ввода; решения также записывать в задаче.
