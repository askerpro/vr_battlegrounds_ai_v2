# Heavy: ссылки сохранены при MoveAsset

Задача `player-prefab-layout`, 2026-10-18. Heavy перенесён в Player/Unused с прежним GUID `fefcf679beb2d0f42b6cce742988357e`; Mirror spawnPrefabs/registry/grips не редактировались. Native receipt фиксирует 10 свойств Heavy в HeavySoldier AvatarData, Managers, AR15_Magazine и трёх LegacyBuildDonors; GUID/fileID прежние. Все шесть файлов byte/blob-identical до/после. Архивирование не исключает prefab из build/spawnables.

FIFO819 завершён и результат получен: `8874a93c`; неожиданных outputs нет. PrefabComposition17/17, AvatarLoadout60/63 (прежние3Floor failures). Это asset/reference evidence, не сетевой runtime/Quest proof. UserQA, ревью и интеграция впереди; accepted commit/merge/push нет. Доказательства: `tasks/player-prefab-layout/reports/heavy-native-receipt.json` и source-hashes reports.

Layout stage9661 подготовил14moves/46assets с прежними GUID и72reference candidates, включая Legacy/vendor consumers; до native исполнения блокер startup MCP9678. Новый NetworkAssetId catalogue check включён в spec только точным методом, весь mutation-regression fixture не запускается. Registry/Managers/spawnables и сетевое поведение не редактировались; новых lease/mutations нет.

Итог:823 DONE/IMPORTED R32b72bd3, все14moves выполнены. Native before821/after823:72reference assets byte-identical,87 740свойств и dependencies без дельт. Все95transfer файлов blob/SHA прежние, GUID/fileID/source chain прежние;3folderGUID и11newfolderGUID сохранены. ExactNetworkAssetId catalogue1/1,Composition17/17,Loadout60/63 (прежние3Floor),0CS. Сетевые runtime/Quest не прогонялись, поведения/transport изменений нет; UserQA и review впереди. Worker released, acceptedcommit/merge/push нет.
