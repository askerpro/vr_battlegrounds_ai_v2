# dogtag-runtime-spawn: сетевой жетон готовности, 2026-10-22

Влито `1909c211`. Жетон стены — сетевой префаб `Assets/Prefabs/Arsenal/DogTag/DogTag.prefab` (`NetworkIdentity`, без
`NetworkTransform`, как оружие), зарегистрирован в `spawnPrefabs` (`--- MANAGERS ---`, тест
`NetworkSpawnableRegistrationTests.Жетоны_панелей_арсенала_зарегистрированы_в_spawnPrefabs`). UniqueId выравнивает
`NetworkUxrIdentity` по netId; привязку к крючку несут SyncVar стены `_dogTagNetId`/`_dogTagOnRack`, а не ClientRpc (NET-23).

Находка для зоны: `NetworkServer.SpawnObserversForConnection` спавнит подключившемуся клиенту только объекты с
`activeSelf=true`. Сетевой предмет, который нужно спрятать, выключать нельзя — поздний клиент его не получит; жетон
прячется выключением родителя-крючка. Проверено: heavy windows (`dedicated-server-arsenal`, `map-run-startup-route` с
поздним клиентом) и heavy editor — код 0, UxrComponentNotFound 0.
