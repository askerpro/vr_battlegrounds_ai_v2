# Подготовка переноса шаблонов карманов

Задача player-prefab-layout, решение9661. Папка Pockets переносится в Equipment/Pockets с прежним folder GUID и тремя существующими prefab/meta. Подготовлены только Editor folder constants и Workbench scope paths; механика хвата/карманов и serialized references не меняются. Export/generators не запускались.

Native перенос ещё не исполнен: MCP startup timeout tools/list30s, issue9678. Runtime/UserQA proof нового layout нет, tests не переписывались. Manifest/точные outputs/spec — tasks/player-prefab-layout/contracts/.

Итог: Pockets целиком native перенесён в Equipment/Pockets с folderGUIDa695ea4e1ff53e44ea88cd7856c029aa; три prefab/meta byte-identical.823 DONE/IMPORTED R32b72bd3, all72referenceassets/87 740свойств прежние. Механика и registry/grips не менялись, exporters/generators/Play не запускались. CompilationEditor0CS,selectedchecks78/81 только3прежнихFloor; UserQA/Quest не заявляются, tests не переписаны. Worker released, acceptedcommit/merge/push нет.
