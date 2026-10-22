# weapon-rebuild-sight-guarantee: тег Weapon при пересборке ствола, 2026-10-22

`GameTagsTool.ApplyToPrefab(path, result)` выделен из `ApplyToPrefabs`. Пересборка (`WeaponRebuildFinish.Complete`)
вызывает его для магазина и оружия. Тег `Weapon` больше не теряется. Из `GameNetworkManager.spawnPrefabs` убраны три
удалённые базы магазинов SDK. Проверка: `GameTagsTests`, `NetworkSpawnableRegistrationTests`.
