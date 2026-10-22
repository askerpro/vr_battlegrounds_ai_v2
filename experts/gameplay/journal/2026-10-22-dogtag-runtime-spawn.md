# dogtag-runtime-spawn: жетон готовности — сетевой предмет стены, 2026-10-22

Влито `1909c211`. Жетон больше не часть `DogTagPanel`: панель держит крючок `_tagAnchor` и префаб `_tagPrefab`
(`Assets/Prefabs/Arsenal/DogTag/DogTag.prefab`, метка `DogTagItem`), а экземпляр выдаёт сервер
`ArsenalWallController.ServerUpdateDogTag` тем же путём, что оружие слотов: `MapRunAdmission.CreateMapItem` →
`DogTagController.BindTag` (крючок, `SetNetworkAnchor`, до спавна) → `NetworkUxrIdentity.SpawnServerObject`.
Клиенту привязку несут SyncVar `_dogTagNetId` и `_dogTagOnRack`; на крючок клиент вешает жетон, только если он там
у сервера (иначе `AdoptTag`).

Инварианты: жетон выдаётся, только когда режим использует его (`ArsenalRules.UsesReadinessTag`); в режимах без жетона
прячется крючок, сам жетон не выключается (см. журнал network). Пропавший жетон заменяется через
`_lostWeaponReplaceDelay`, и замена не сбрасывает «один раз за закупку» (`EnableGrab`, а не `ResetTag`). Брошенный
жетон автор мира возвращает на скрытый крючок (`ReturnLooseTag`). Без сервера (офлайн) жетона нет — как оружия стены.
Тесты — `DogTagSetupTests`, `GameModeRulesTests` (видимость жетона), E2E `scripted-combat-round`/`-next-round`.
