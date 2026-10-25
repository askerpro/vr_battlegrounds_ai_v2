# Version Control

Git — единственная система контроля версий проекта. Unity Version Control
(Plastic SCM) удалён 2026-10-06: воркспейс отвязан, облачные репозитории удалены,
хук переноса коммитов и `ignore.conf` убраны.

## Почему убран Plastic

Plastic держался ради подсветки изменённых полей в инспекторе. Оказалось, что
жирные поля с полоской слева — это prefab overrides самого Unity (отличия
экземпляра или варианта от исходного префаба), к VCS они не относятся. При этом
Plastic стоил дорого: каждый коммит ждал `cm ci` по минуте-две (обход `tmp` на
15 ГБ), воркспейс разошёлся с Git на тысячи файлов, а `cm switch`/`update`
откатывали рабочие файлы Unity к старым ревизиям из облака.

Изменения ассета префаба относительно коммита смотреть через `git diff`.

## Хуки

`Assets/Editor/VR_Battlegrounds/VersionControl/GitHooksInitializer.cs` при
загрузке редактора выполняет `git config core.hooksPath .githooks`. В `.githooks` —
хуки Git LFS (`post-checkout`, `post-merge`, `post-rewrite`, `pre-push`; `pre-push` ещё проверяет EOL)
и `pre-commit`: каталог карт `Assets/Data/Maps/MapRuntimeCatalog.asset` не должен устаревать.
Коммит, который меняет сцену, MapData, реестр режимов или пресет арсенала карты из каталога и не
обновляет её отпечатки, отклоняется (иначе сборка пересчитает их и оставит diff в build-checkout).
Исправление без Unity — `python Tools/LevelDesign/map_catalog_fingerprints.py --fix`, затем
`git add Assets/Data/Maps/MapRuntimeCatalog.asset`. Алгоритм — копия `MapRunPreflight.ContentFingerprint`.

## Пакет collab-proxy

Пакет `com.unity.collab-proxy` (встроенная интеграция UVCS) ещё в
`Packages/manifest.json`, но выключен штатным переключателем с 2026-10-03: он
добавлял 10 секунд к каждой перезагрузке сборок
([аудит](audit/editor-reload-audit-2026-10-03.md), `Tools/UnityMcp/Tests/DisableUvcs.cs.txt`).
Пакет можно удалить из манифеста после проверки в Unity.
