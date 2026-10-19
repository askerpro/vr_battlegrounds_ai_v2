# Подготовка layout Player

Задача player-prefab-layout, решение9661. Подготовлен перенос30проектных поз папкой в HandPoses/CyborgHands с прежним folder GUID; vendor-позы не затрагиваются. GripTargetProfile обоих Cyborg/MEF профилей получает один общий CyborgPoseFolder, остальные параметры записи/рига/хвата прежние. AvatarHandBases и производственные paths обновлены, генераторы не запускались.

Native moves ещё не исполнялись: startup unityMCP tools/list timeout30s, issue9678. Layout compile/fit/runtime/Quest proof нет, tests до UserQA не переписывались. Manifest/outputs/execution spec и obsolete literal tests — tasks/player-prefab-layout/contracts/. Heavy предыдущего этапа сохранён.

Итог возобновления: native MCP восстановлен, layout823 DONE/IMPORTED R32b72bd3, worker освобождён.30поз перенесены с прежними GUID/байтами, общий folderGUID071565fdb4dfc0a4e851f3fe5153d09d сохранён. Native87 740свойств в72reference assets прежние; source/body/pose/meta SHA и GUID/fileID сохранены. CompilationEditor0CS; unchangedchecks78/81 (3прежнихFloor). Fit/rig/runtime/Quest и UserQA не заявляются, tests не переписывались. Полные receipts в tasks/player-prefab-layout/reports/layout/.
