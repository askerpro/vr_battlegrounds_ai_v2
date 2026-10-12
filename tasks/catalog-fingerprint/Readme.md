# Отпечатки каталога карт не зависят от окружения

2026-10-12. Владелец — эксперт `gameplay`. Задача выбрана пользователем 2026-10-12 («бейкер утилиту исправь»).

## Проблема

`MapRunPreflight.AssetHash` был `AssetDatabase.GetAssetDependencyHash` — хеш сырых байтов файла. После EOL PhaseB
YAML в Git хранится в LF, а в старых рабочих копиях лежит в CRLF, пока Unity не пересохранит файл. Один коммит
давал разные отпечатки:
- ложные `Catalog.Content.Stale` / `Catalog.Arsenal.Stale` в preflight (weapon-system 4757: rebake в worker
  поменял отпечатки всех 7 карт, хотя менялся только пресет лобби);
- каталог пересобирается при сборке (`MapCatalogBuildStep` → `RebakeCatalog`), а клиент сверяет свой
  `ContentFingerprint` с серверным (`MapBootstrap.cs:690`) — сборки одного коммита с разных машин могли
  отказать друг другу («разные сборки»).

## Решение

`AssetHash` = SHA256 содержимого файла и его `.meta`; текстовый YAML Unity (`%YAML…` / `fileFormatVersion:`)
хешируется с CRLF → LF (как хранит Git), бинарный ассет — как есть. Версии формата: `asset-content-v2`,
`map-content-v2`, `arsenal-content-v2` — старый каталог явно устаревает, нужен `RebakeCatalog`.

## Этапы

1. `env-independent` — `MapRunPreflight.cs` + пересборка `MapRuntimeCatalog.asset` в Unity (`RebakeCatalog`),
   preflight 7/7, EditMode Maps (`MapCatalogIntegrityTests`, `MapRunContractTests`), Play лобби до Ready.
2. `accepted-tests` — после приёмки: EditMode-тест нормализации (CRLF и LF дают один отпечаток).

## Состояние

- 2026-10-12: код `AssetHash` написан. Офлайн-оракул (Python, тот же алгоритм) на 9 входах каталога + `.meta`:
  хеш CRLF-рабочей копии == хеш LF-blob Git == хеш синтетической CRLF-версии — ALL OK. Unity не запускался.
- Связи: weapon-system `sight-arsenal-dedupe` вписывает одну строку отпечатка лобби (разрешение 4912) —
  после их вливания rebase и штатный rebake перезапишут все отпечатки форматом v2. `lobby-decoration`
  (level-design) тоже пишет каталог — после вливания этого этапа их rebake даст стабильные значения.
