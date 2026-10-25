# Pre-commit: каталог карт не устаревает

2026-10-25. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/map-catalog-precommit` (схема «задача = агент»).

## Цель

Коммит, меняющий сцену/MapData/реестр режимов/пресет арсенала карты из MapRuntimeCatalog без обновления отпечатков, отклоняется pre-commit хуком с командой исправления; есть --fix без Unity.

## Этапы

Этапы и области записи — в [plan.json](plan.json). Заготовленный этап `work` пишет только в `tasks/map-catalog-precommit/**`: перед `begin` объявите нужные пути (`writes`) и перерегистрируйте план (`register`).

## Итог (2026-10-25)

- `Tools/LevelDesign/map_catalog_fingerprints.py` — пересчёт `ContentFingerprint`/`ArsenalFingerprint`
  каталога по алгоритму `MapRunPreflight` (SHA-256 файла и .meta, CR LF → LF у Unity YAML) из индекса
  или рабочей копии; `--fix` переписывает устаревшие отпечатки без Unity.
- `.githooks/pre-commit` — `--check --source index --staged-only`: коммит без входов каталога — 0,4 с;
  устаревший каталог — отказ с командой исправления (~1,3 с); сбой самой проверки коммит не блокирует.
- Проверено: на `8f985a849` найден устаревший Lobby, `--fix` побайтно равен выводу генератора сборки;
  остальные карты совпали. Тест — `Tools/Tests/test_map_catalog_fingerprints.py` (7 сценариев, зелёный).
- Приёмка пользователем 2026-10-25 («вливай»).
