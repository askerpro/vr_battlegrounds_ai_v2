# Подготовка переноса часов Player

Задача player-prefab-layout, решение9661. WristWatch и WristWatch_HUD предназначены для Equipment/Watches с сохранением имён/GUID/байтов. Подготовлены только производственные Editor path updates WristWatchInstaller и Workbench scope paths. Runtime WristDisplay не содержит точного пути, его поведение/код не менялись; генераторы не запускались.

Native moves ещё не исполнялись из-за startup unityMCP blocker9678; компиляции/UserQA новой структуры нет. UI/WristDisplayTests и RegisteredAvatarBodyTests со старым literal path оставлены как obsolete до UserQA. Подробности — tasks/player-prefab-layout/.

Итог: часы native перенесены в Equipment/Watches в823, GUID/байты/source и serialized references прежние. Layout14moves/46assets DONE/IMPORTED R32b72bd3,72referenceassets без дельт, CompilationEditor0CS. WristDisplay/runtime не менялись, генераторы/Play не запускались. UserQA UI/новой структуры впереди; obsolete literal tests не переписаны, acceptedcommit/merge/push нет. Worker released.
