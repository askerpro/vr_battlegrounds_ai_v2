# Явные ссылки зон — подготовка интегратора, 10.10.2026

Только предложение для решения root; Scene пока не сгруппирована и не имеет bindings. Native snapshot: `reports/implementation/zone-alignment/current-role-references.json` (41 ветка, dirty=false). Не использовать одноимённую неактивную SDK-ветку `Scene/Rooms/Restricted/ShootingRange`: рабочая группа — именно `Gameplay/ShootingRange`.

Ссылки ниже относительны `Environment/LobbyDecoration`.

| Роль | Явно выбранные части |
|---|---|
| ArmoryRangeZone | OpenTent; InteriorDecoration; PerimeterDecoration; ShootingRangeDressing; BaseBackdrop/SharedFiveLaneRockBackstop; BaseBackdrop/LayeredPaidRockMasses/NorthRangeCliffLayers; 4 SM_Lamp_long и4 InteriorWarmLight из CampLighting |
| BarracksZone | BaseBuildings/Казарма A; BaseBuildings/Казарма B; BarracksDressing; последующий frozen фасадный helper; боковой source fixture иExteriorWarmLight при X−17.75/Z−16 |
| WarehouseServiceZone | BaseBuildings/Склад и мастерская; ExteriorDecoration/VehicleMaintenance; FuelDepot; PaidArmoredVehicle; source fixture иExteriorWarmLight при X19.7/Z−21 |
| ShippingDepotZone | BaseBackdrop/OrganizedContainerStores |
| CheckpointZone | CheckpointZone целиком, включая временную опору; ExteriorWarmLight при X21/Z−29.84. Existing source fixture уже внутри GuardHousing, его не дублировать |
| RestCanopyZone | RestCanopyZone целиком; CanopyWarmLight |
| ConstructionZone | ConstructionZone целиком |
| PerimeterZone | Все forest branches иLowLayeredHorizon из ExteriorDecoration; BaseBackdrop/DecorativeBaseChainMesh; отдельные оставшиеся side/south children LayeredPaidRockMasses, НЕ родитель с вложенной NorthRangeCliffLayers; DistantGroundBackdrop |

CampDaylight иAmbientRoutes предлагаются как глобальная косметика вне функциональных перемещаемых зон. Root принимает конкретный состав. При группировке света повторяющиеся имена SM_Lamp_long/ExteriorWarmLight разрешать явными Transform ссылками и проверенной world position, не произвольным Find первого совпадения.

Gameplay followers уже находятся в правильном корне: `Gameplay/ShootingRange` (5 actors,2 positions,2 monitors иcapture), `Gameplay/SpawnZones` (2 станции/anchors). `MirrorDisplayClean` и `LobbyRange/Tables` пока standalone; текущий binder отказывает для roots вне Gameplay/Environment. Root должен разрешить сценовый перенос целых существующих веток с сохранением IDs, ссылок и world matrices. Mirror subtree содержит UxrPlanarReflectionUrp, без calibrationAnchor; Tables — BoxCollider/SpawnZoneBoundaryBlock, не PhysicalArenaDefinition/Shape. `LobbyRange/OuterGround` с Collider ивесь PhysicalArenaLayout сохраняются.

Builder группирует только собственные декоративные descendants, сохраняет мировые матрицы сдопуском0.0001 иscale1 pivots. Для authored custom poses обязательна полная capture→normalize existing source/followers→generate canonical→Bind canonical instance→restore poses→ValidateScene последовательность из `implementation/zone-alignment/Handoff.md`. Не вызывать capture после restore и не сбрасывать пользовательскую позу молча.

Зависимые hard paths primary authoring: common BuildLighting требует CheckpointZone/GuardHousing иRestCanopyZone/RidgeFixture; выполняется ДО группировки. ConnectAudio на установленном instance пока ищет AmbientRoutes непосредственно; предложение оставить эту ветку глобальной не меняет аудиоконтракт.

Проверки scheduler9EE525BC: automatic event following, ordinary иdrag/release split sourceUndo, immediate Save/Redo/reopen прошли вsynthetic additive Scene. ExplicitCapture — две отдельные авторские операции: первыйUndo блокирует intermediate mismatch, второй восстанавливает follower. Actual Play стартовал, штатный Lobby startup удалил synthetic additive Scene; это НЕ Play pass. На реальных Lobby bindings нужен новый actual Play. DefaultScenario stop error1 оставлен открытым. Synthetic scratch Scene/meta ивсе probe subscriptions удалены; main dirty=false/Play=false/bindings0.
