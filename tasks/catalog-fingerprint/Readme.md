# catalog-fingerprint — отпечатки каталога карт и preflight

Владелец — эксперт `gameplay`. Обновлено 2026-10-15. Машинный план — [plan.json](plan.json),
подробности и условия проверки — [Details.md](Details.md).

## Статус

| Этап | Состояние | Где |
|---|---|---|
| `env-independent` | влит, принят пользователем 2026-10-12 | origin/dev e330b6162 |
| `preview-loaded-scene` | принят и влит 2026-10-14; Unity 3/3 preflight PASS, Windows E2E build PASS | origin/dev 97a320c88; stage_merged 6800; база опубликована 639 DONE / 6808 |
| `accepted-tests` | принят и FF-влит 2026-10-15; Unity694:7/7 PASS, failed/skipped0 | origin/dev 7fbacc825; stage_merged7825; finish694 DONE |

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

## Завершение

1. `accepted-tests`: приёмка получена 2026-10-15 («да - вливаем, считай принял»); коммит семи тестов,
   Unity-generated meta и документации этапа7fbacc825 FF-влит, удалённый SHA подтверждён ls-remote.
   Unity694 после явных import/readiness/discovery7: 7/7 PASS,39.266314s; полный terminal Result сохранён
   доcleanup/finish. Finish DONE d76df7621be92d6bf95a1e05c609cdac154376e4, paths[]/unexpected[].
   Fixture сохраняет исходную безымянную сцену и root IDs, Lobby открывает Additive; assertions прошли.
   Managers/live/previews0 и setup[] до/после; изменения clean unnamed scene оболочкой NUnit раскрыты в Details.
   Старый676 остаётся UNKNOWN, не повторялся. Compile-only284refs PASS, ревью текущего source без блокеров.
2. Результат аренды 84bc9c0de949 содержит только побочный `PC.asset`; `receive` не выполнялся.
3. Вливание выполнено fast-forward; launch-infra и level-design уведомлены через hub.
4. По infra_status7375 подготовлена [спецификация владельца v2](contracts/accepted-tests-spec.v2.json):
   текущие source/meta SHA256,7/7 без пропусков, XML/log outputs и cleanup; immutable checkpoint
   c7ee905d058091a02360c42f7092073d189b340f. Production CLI executor ещё не опубликован; обычный MCP runner
   восстановлен сопровождающим (7457). UNKNOWN676 не повторяется.

## Ждёт пользователя

- Ничего: все этапы приняты и влиты. Задача закрыта; межкопийная server/client проверка остаётся вне её scope.

## Решения пользователя

- 2026-10-12: «бейкер утилиту исправь» — `env-independent`; weapon-system на время исправления разрешён вариант А
  (одна строка отпечатка лобби вручную, 4912). «принимаю» — `env-independent`.
- 2026-10-13: «идем по твоим рекомендациям — arsenal пока откладываем» — `preview-loaded-scene` взят в работу.
- 2026-10-14: «Принимаю, влить (рекомендуется)» в диалоге приёмки текущего прогона — `preview-loaded-scene`.
- 2026-10-15: «да - вливаем, считай принял» — `accepted-tests`,7тестов, meta и документация этапа.

## Риски и ловушки

- Каталог пересобирать только `MapBootstrapMigration.RebakeCatalog`, YAML руками не править.
- Отпечатки v2 зависят только от содержимого; любая правка сцены/MapData/реестра режимов/пресета требует rebake.
- Не проверено: две реальные сборки (сервер/клиент) из разных рабочих копий.

## Коммуникация (для Codex)

Inbox разобран 2026-10-14; передача подтверждена событием 6559. Устаревший маршрут Claude очищен.
Вопросы и приёмка пользователя — через доступный инструмент запроса ввода; решения также записывать в задаче.
