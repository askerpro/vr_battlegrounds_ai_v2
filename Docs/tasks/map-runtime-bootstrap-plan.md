# MapRunConfig и MapBootstrap: поэтапный план реализации

> Для исполнителя: прочитать design и актуальный AGENTS.md. Пользователь разрешил начать реализацию 2026-10-04. Использовать superpowers:executing-plans для inline работы либо superpowers:subagent-driven-development только при разрешённом делегировании. Зависимости, Unity lease и human acceptance gates сохраняются.

**Цель:** зарегистрированная карта с одним MapRoot и authoring bindings получает полный проверенный runtime состав без ручного размещения служебных managers.

**Архитектура:** local MapBootstrap строит и проверяет одну карту; MapRunAuthority на SessionContext единолично commits canonical snapshot. Сервер спавнит служебные NetworkBehaviours из registered prefabs, клиенты только разрешают опубликованный run; gameplay owners сохраняются.

**Технологии:** Unity 6/URP, vendored Mirror/UltimateXR, C#, AndroidCompileGate, временные temporal probes и существующий Unity test harness.

**Spec:** [map-runtime-bootstrap-design.md](map-runtime-bootstrap-design.md).

**Статус 2026-10-05:** контрактный срез задачи 2 и authoring foundation задачи 3 реализованы. Contract18/18, teardown2/2, root14/14, adversarial6/6 и Android прошли в сохранённых прогонах (локальные отчёты); существующие группы20/21 с отдельным LegsAnimator failure. Bounded lifecycle baseline задачи1 воспроизведён, live SDK inventory/full startup ещё впереди. Gameplay остаётся legacy. [Handoff и следующие действия](map-runtime-bootstrap-handoff.md).

## Общие ограничения

- Смена карты в живой сессии только `MapLoader.Instance.LoadMap(sceneName)`. Mirror onlineScene/offlineScene — прежнее исключение.
- Логи только GameLog. Документация и комментарии на русском.
- Scene/prefab/import/compile/tests/PlayMode/bake — под собственной Unity lease; чужой PlayMode и dirty assets не трогать.
- Environment авторский; PhysicalArenaLayout диагностический без Collider, Geometry скрыта, CalibrationAnchors активны; Gameplay содержит stations/zones/targets.
- ArsenalStationAnchor.Zone — единственная явная связь места станции с зоной; не parent под scaled TeamSpawnZone.
- Один canonical writer snapshot. Не хранить mutable копию state/config в MapReferee, SessionManager, Binding, coordinator или каждом consumer.
- Raw local Instantiate сетевого manager на client запрещён. Dynamic network prefabs имеют registered assetIds, scene NI сохраняют sceneIds, UXR IDs проверяются отдельно.
- Generator и SDK readiness parallel work чужие; здесь только adapters и accepted lifecycle seam. Не править их files без handoff.
- Canonical StationKey сериализуется только ArsenalStationCompositionBinding; MapRoot хранит refs binding. Generator API — [его design](arsenal-generator-design.md)/[plan](arsenal-generator-plan.md); геометрию и semantic UID algorithm bootstrap не присваивает.
- CompositionReady не playable Ready. Задача 3 завершает только primitives под закрытыми gates; server Ready после policy gates задачи 4, remote LocalPlayable после current Relay snapshot admission задачи 5/7.
- Actual Relay немедленно запрашивает/применяет initial SDK snapshot и отбрасывает preinitial incoming increments, без incoming queue. Новая ordering/generation гарантия — будущий proof, не исходное поведение.
- Сейчас временные RED/GREEN probes + compile + существующие checks; не писать/переписывать permanent gameplay tests до human acceptance.

## Review focus

1. `OnStartServer` scene stations раньше `OnServerSceneChanged`: никакой refill или gameplay activity по одному CompositionReady, mode lifecycle gates обязательны.
2. Late join/object order, stale mode epochs и SDK snapshot/increments: current registrations/reference closure до fresh request, old-run response не применяется.
3. Same-scene reload/cancel/stop server: каждый callback привязан к RunKey, старый scope не оживает.
4. Future Selection во время Warmup/Live/Paused: accepted Series mode не меняется, следующая Series принимает новый выбор, Resume сохраняет mode снимка.
5. Dedicated/headless и direct Editor start без Offline: нет ожидания local avatar, duplicate process root или SDK auto-create default вместо нужного configured instance.

Каждый пункт имеет probe у своего owning среза ниже. Любой новый unresolved architecture conflict выносится root до dependent edits; независимая работа продолжается.

## Карта файлов и ответственности

Новые runtime файлы предлагаются в `Assets/Scripts/Maps/Runtime/`:

| Файл | Ответственность |
|---|---|
| MapRunConfig.cs | Immutable IDs/resolved inputs и RunKey, без состояния механик |
| MapRunSnapshot.cs | Serializable целый canonical descriptor и BootstrapStatus/ModeEpoch/Revision |
| MapRunResolver.cs | Pure validation/resolution по catalogs и captured Series mode |
| MapRunAuthority.cs | Единственная server commit точка на SessionContext, read-only subscription API |
| MapRoot.cs | Scene authoring refs, включая canonical ArsenalStationCompositionBinding; key не дублируется |
| MapRuntimeCatalog.cs | Центральные service prefab refs/capabilities; не per-map roster |
| MapBootstrap.cs | Typed stage machine composition/activation/teardown |
| MapRunScope.cs | Keyed cancellation, owned subscriptions/objects/disposal |
| MapArsenalCompositionAdapter.cs | Existing station prepare + accepted external generator seam |
| MapRunAdmission.cs | Composition/server gameplay/local SDK admission results текущего run; не второй mutable state owner |

Editor tools только `Assets/Editor/VR_Battlegrounds/Maps/`: MapRunPreflight.cs, MapBootstrapMigration.cs, DirectMapPlayBootstrap.cs. Конкретные temporary probes — `Tools/Probes/MapRuntimeBootstrap/`, artifacts — `Docs/tasks/report/map-runtime-bootstrap/`. Native probe runner под той же Editor Maps категорией; не permanent тестовая сборка.

Изменяемые existing owners: `Managers/{MapLoader,MapReferee,SessionManager,Series,ManagerOrder}.cs`; `Network/{GameNetworkManager,NetworkStateRelay,HeadlessPrecacheGuard}.cs`; NetworkUxrIdentity registration/observable-ref seam только по generator handoff; `GameModes/{GameMode,ModeStartCleanup}.cs`; `Arsenal/{ArsenalWallController,ArsenalStationPresetBinding,ArsenalBoundaryWall}.cs`; допуск `PlayersManager`/`AvatarManager` по фактической общей spawn точке. Сценовые миграции отдельным срезом. Не добавлять bootstrap responsibilities в PersistentRoot или SessionManager.

## Задача 1. Baseline и временный lifecycle harness

**Зависимости:** root принял scope и доступна Unity lease; runtime code ещё не изменять.

**Файлы:** create временный probe runner и artifacts выше; read owners, native prefabs/scenes/registry. Editor tools не должны сохранять рабочую сцену или вызывать global SaveAssets.

**Результат:** детерминированный baseline event trace с фактическими failures; каждый следующий срез имеет тот же observable assertion.

- [x] Зафиксировать source SHAs/diffs, serialized refs/registry IDs и world transforms шести карт. Actual live SDK registration остаётся отдельным proof.
- [x] Добавить bounded source probes partial Ready/stale mode и четыре lifecycle scenario. Полный configured process startup и stock creation этим fixture не проверяются.
- [x] Выполнить именованный baseline RED, сохранить trace и очистить own fixture. ClientRpc/harness noise не считается отказом logic.
- [x] Прогнать baseline и checkpoint AndroidCompileGate; оба PASS в своих сохранённых прогонах.
- [x] Выполнить review bounded baseline/probes. Remote delivery по этим traces не заявляется.

## Задача 2. Immutable config и единственная публикация

**Файлы:** create MapRunConfig.cs, MapRunSnapshot.cs, MapRunResolver.cs, MapRunAuthority.cs, MapRunScope.cs; modify SessionContext.prefab и GameNetworkManager session startup в адресном lease.

**Интерфейсы (предлагаемый стабильный bootstrap API):**

- `readonly struct MapRunKey { Guid SessionEpoch; ulong LoadSequence; }` — value equality; IDs serializable Mirror-supported representation в wire DTO.
- `sealed class MapRunConfig` — readonly properties `Key`, `MapScene`, `ContentFingerprint`, `CompositionVersion`, `WarmupModeId`, `MatchIntent`, `ArsenalPresetId`, `ArsenalFingerprint`, readonly `Stations` (protected copy). `MatchIntent` только `NoMatch` или `Resolved(modeId)` по captured active Series intention; неполный intent не публикуется.
- `MapRunSnapshot` — wire value: key, revision, status, MapState, ModeEpoch, ActiveModeId/NetId, RefereeNetId, CoordinatorNetId, full config wire description либо manifest ID/hash, FailureCode.
- `MapRunResolver.Resolve(MapRunRequest request, MapRuntimeCatalog catalog, MapRootBindings bindings) -> MapRunResolution` (success config либо exact errors/fallback reasons), без world mutation.
- `MapRunAuthority.Current` read-only; `Subscribe(Action<MapRunSnapshot> handler)` immediately отдаёт current; `Unsubscribe` симметричен.
- Внутренние server-only `BeginRun(validatedRequest)`, `CommitPrepared(snapshot, expectedKey, expectedRevision)`, `Fail(scope, code)`, `Retire(scope)`; все canonical writes идут одним private commit, revision CAS для stale requests. CommitPrepared не public arbitrary setter для consumers.
- `MapRunScope` owns Key, cancellation, subscriptions и disposal в обратном порядке; повторное Dispose безопасно.

- [ ] До изменения создать/прогнать RED probes `ConfigCollectionsDoNotAlias`, `RevisionRejectsOldScope`, `FullSnapshotSerializes`, `UnknownContentDoesNotResolve`. При отсутствии типов baseline RED должен быть именованным отсутствующим контрактом, не compiler-noise всего проекта.
- [x] Реализовать immutable copy/IDs, resolution и одного writer; не копировать score/round state. Source asset references оставить в local resolved view, wire отправляет только stable IDs/hash. Native asset adapters будут в задаче 3; сейчас pure frozen catalog/bindings.
- [x] Add MapRunAuthority на existing SessionContext, one instance проверен native readback; установленный catalog before map run остаётся задачей 3; dedicated применяет commit напрямую, не ждёт SyncVar hook.
- [x] Настроить actual Mirror serializer full snapshot и actual late-client deserialize; revision+config/state применяются целым value. Проверить unsupported field types до интеграции. Проверено отдельными NetworkIdentity SerializeServer/DeserializeClient, не только host shared instance.
- [x] GREEN probes для collection alias, unknown ID/hash, stale revision/session epoch, subscription before/after current. AndroidCompileGate. Документировать новые контракты адресно в game-manager/session-architecture/README/CHANGELOG.

## Задача 3. MapRoot preflight и composition primitives под закрытым gate

**Состояние 2026-10-05:** authoring foundation реализован и проверен: MapRoot/native catalog/preflight, root14/0 и adversarial6/0 после фактического RED, Android PASS. Runtime integration (bootstrap/admission и actor hooks) не применена: автоматическая проверка отклонила общий пакет как широкий production lifecycle risk. Его [конкретные границы и приёмка](map-runtime-bootstrap-runtime-integration.md) предъявлены для разрешения; task3 целиком не завершён. Binding schema генератора теперь существует; source stage1 принят root, registration/composer ещё впереди.

**Зависимости:** tasks1/2; generator owner передал минимальную schema/API ArsenalStationCompositionBinding с единственным StationKey и exclusive authored/generated mode. Это lightweight handoff до runtime generation GREEN; bootstrap не создаёт конкурирующий key компонент. Stage3 probe использует explicit captured-mode request; production Series capture замыкается task4.

**Файлы:** create MapRoot.cs, MapRuntimeCatalog.cs, MapBootstrap.cs, MapRunAdmission.cs, MapRunPreflight.cs; create coordinator prefab; modify MapData.cs (canonical MapKind), registered spawnPrefabs, MapReferee initialization, ArsenalWallController/Binding lifecycle gates, GameNetworkManager lifecycle notifications.

**Интерфейсы:**

- `MapRoot.Bindings` immutable resolved scene view: MapData, EnvironmentRoot, GameplayRoot, PhysicalArenaLayout, authored zones и refs ArsenalStationCompositionBinding; canonical StationKey читается из binding, map kind из MapData. `MapRoot.ValidateBindings() -> MapBindingValidation`.
- `MapBootstrap.TryAdvance()` keyed typed prerequisites; `Cancel(string reason)`; `CompositionReady` подтверждает primitives/refs/registrations. `Ready` требует task4 lifecycle proof и не публикуется этим срезом; Failed не Ready.
- `MapReferee.InitializeRun(MapRunScope scope, MapRunConfig config)` до Spawn; `OnStartServer` регистрирует сеть, а не автоматически ServerStartWarmup без gates.
- `MapRunAdmission.CanCreateAvatar(scope)` и `CanActivateMapGameplay(scope)` до task4 server Ready всегда false. Один queued request per session/key draining после server Ready; server admission не ждёт remote snapshot/local avatar. `LocalPlayable` отдельно ждёт task5 Relay initialization, queue не выдаёт authority клиенту.
- `ArsenalStationPresetBinding.Prepare(preset)` сохраняется public visual API; bootstrap adapter предоставляет canonical preset и readiness; auto TryPrepareFromScene остаётся только explicit legacy migration mode, не параллельный resolver current run.

- [ ] Повторить baseline RED `MissingRequiredDoesNotReady`, `SceneRefillBeforeCallback`, `InitialStartWithoutSceneChange`; проверить ни одного stock/avatar/Begin при missing binding.
- [ ] Implement complete preflight по всему registry: exact refs/map kind/zone teams/active anchors/hierarchy/content IDs/assets и duplicates; compatible mode fallback выводить как resolved reason, не silent asset repair.
- [ ] Gate authored station OnStartServer, Update initial-refill и refill requests, client local interaction до соответствующего server Ready/LocalPlayable, а не CompositionReady; все callbacks допуска используют scope. Не выключать весь объект удерживаемого предмета ради gate.
- [ ] Server creates inert MapReferee/coordinator из catalog; move to scene before Spawn, config coordinator authored deployment list до spawn, no nested NI. Client binds received instance по key; host reuse instance. AssetId registry preflight.
- [ ] Добавить session-start/root-registration/scene-loaded notifications: advance и при initial start без SceneChanged, и после обычной смены; MapLoadCompleted не используется как gameplay ready.
- [ ] Один admission owner ставит map-created avatar requests из OnServerReady и первого подключения в queue до server Ready; PlayerSession admission/connection handshakes сохраняются. Composition stage не ждёт будущий avatar или client SDK snapshot.
- [ ] GREEN missing/duplicate/corrupt inputs, callback order, host один service spawn, dedicated zero players, initial no-onlineScene и late root/session order; `CompositionReadyDoesNotActivatePolicies` подтверждает ноль stock/avatar/Begin/policy side effects. AndroidCompileGate. Новая карта пока probe-created, production scenes не мигрировать автоматически; playable claim отсутствует.

## Задача 4. Mode transition ownership и выбор режима

**Зависимости:** tasks2/3 verified composition; пользователь уже выбрал captured Series mode и разрешил реализацию. Задача замыкает shared lifecycle gates и впервые разрешает server Ready/activation. Client LocalPlayable до task5 Relay gate закрыт.

**Файлы:** modify MapReferee.cs, GameMode.cs, ModeStartCleanup.cs, GameModes/WarmupMagazineSupply.cs, GameModes/EliminationMode/{RoundCleanup,RoundMagazineRefill}.cs и Economy/{MatchEconomy,ArsenalCheckout,ArsenalOwnershipPolicy,StartingSidearmPolicy}.cs (только committed lifecycle gates, без переписывания механик); AdminMapCommands.cs, Debug/Bootstrap/DebugOrchestrator.cs, Series.cs/SessionManager.cs. Остальной состав mode prefabs инвентаризировать по refs перед срезом, в том числе LooseItemSweeper.

**Интерфейсы:**

- `MapReferee.CurrentState` читает `MapRunAuthority.Current`; `_currentState` SyncVar больше нет.
- `GameMode.InitializeRun(MapRunKey key, ulong modeEpoch, GameModeData data)` до Spawn; `ActivateCommittedRun()` единожды начинает cleanup/teams/refill/CanBegin pipeline после server commit, а remote local policies — после matching descriptor+mode и LocalPlayable задачи 5. ModeStartCleanup активируется этим lifecycle signal, не OnEnable; descriptor alone не обходит Relay gate.
- `GameMode.IsCommitted` read-only и lifecycle signals `RunActivated`/`RunDeactivated`; policies connect static subscriptions/Update/teardown только activated scope. Неактивировавшийся candidate disposal не снимает owners живых stations. Phase hooks/Initialize/Restore не поднимают global gameplay events до matching commit на server и client.
- `RegisterActiveGameMode`/`UnregisterActiveGameMode` проверяют key/epoch/netId по snapshot и держат pending candidate до descriptor; event только canonical local change.
- `Series.ServerBegin(IReadOnlyList<string> maps, string modeId)` snapshot-ит active series intention; SessionManager.StartSession передаёт текущий selection один раз; `GoLive()` использует config.Resolved mode. Future selection меняет только следующую Series; config текущей карты не заменяется mode-командой.

- [ ] RED `CandidateModeEnableCleanup`, `CandidatePolicyTakesOrClearsOwners`, `StagedPhaseRefillsLivePlayers`, `LateOldModeOverridesCurrent`, `PauseDescriptorWarmup`, `FutureSelectionAffectsGoLive`: active mode fixed at Series begin, следующий Series принимает новый выбор. Проверить snapshot pause принадлежит тому же RunKey.
- [ ] Перевести transition на prepare inert candidate → validate → existing destructive boundary → sole commit → activate; Fail после boundary закрывает admission, не сообщает ложный rollback.
- [ ] Внедрить captured active Series mode, удалить старый implicit Selection read из GoLive; несовместимый captured mode разрешает MapModeRules с explicit fallback reason один раз до map load, corrupt prefab/teams — отказ до teardown. Warmup/no-match lobby остаётся валидным.
- [ ] Pause создаёт snapshot исходного mode; published active Warmup + Paused. Resume restore mode снимка, не текущий Selection. Stop/Finished snapshots clear; Series получает результат один раз.
- [ ] DebugOrchestrator использует verified MapBootstrap readiness, сохраняет player-count/AutoGoLive intent и direct startup settings; MapReferee.SubscribeToInstance больше не достаточен для начала.
- [ ] GREEN transition/order/candidate-side-effect assertions, host/dedicated server Ready и ровно один initial refill/activation, late join descriptor Live/Paused, stale epoch/unregister/reload. Remote LocalPlayable остаётся закрыт до Relay proof task5. AndroidCompileGate + существующие flows без переписывания changed expectations. Зафиксировать список ожидаемых outdated tests.

## Задача 5. Load transaction, cancel, локальный SDK и Relay snapshot admission

**Файлы:** modify MapLoader.cs, Series.cs (изъятие перед accepted load), GameNetworkManager.cs, NetworkStateRelay.cs, HeadlessPrecacheGuard.cs и MapRunAdmission.cs; observable refs из NetworkUxrIdentity по handoff (без собственного UID algorithm); create DirectMapPlayBootstrap.cs; configured local SDK root prefab после settings comparison; hooks scope/disposal.

**Интерфейсы:** MapLoader.LoadMap(sceneName) остаётся единственной public live scene entry; accepted load получает generation token. IsLoading держится до actual completion/cancel/failure. MapLoadStarted сохраняет existing capture timing; new verified map ready имеет отдельное имя, не silently расширенную старую семантику. Process startup provider `EnsureConfiguredProcessServices()` создаёт ровно один existing persistent prefab и configured local SDK root при Editor direct Play. Bootstrap/network readiness result для Relay содержит current RunKey/local epoch, matched local registered manifests и observable referenced object/SDK IDs; RequestInitialState вызывается только после barrier. Exact wire correlation/queue API выбирается по probes, дополнительный ACK заранее не требуется.

- [ ] RED `ReloadSameSceneOldCallback`, `SecondLoadDuringAsync`, `ServerStopDuringComposition`, `EditorDirectMissingProcessRoot`; Relay probes `InitialRequestBeforeRegisteredAnchor`, `OldRunSnapshotAfterReloadOrCancel`, `SnapshotIncrementPermutations`, `SnapshotReferencedObjectArrivesLate`. Current authored-anchor failure измеряется на baseline; deliberately delayed future generated anchor — corrupt-order assertion будущего harness, не baseline доказательство несуществующего generator.
- [ ] Bind accepted load token к scope; ignored duplicate LoadMap не increment-ит key и не retire-ит current. Сохранять ConnectionsSettled/no local-player dependency и позиционный capture до unload.
- [ ] Перенести Series.Load EquipmentStrip за единый accepted-load preflight; это один owner destructive map boundary, не два strip вызова. Probe invalid/ignored duplicate load подтверждает неизменные held items и current run.
- [ ] Retire closes gates, cancels stages, unsubscribes before destruction; server-owned network objects удаляются через NetworkServer.Destroy/UnSpawn по actual lifetime; client просто следует lifecycle. Persistent objects не уничтожать scene disposal.
- [ ] Migrate один configured local SDK prefab в process startup. Compare current serialized UXR scene settings; don't assume automatic default equivalent. Client/host input setup и dedicated headless behavior отдельные dependencies.
- [ ] Direct Editor запускается до сети, использует existing debug settings, возвращает изменённые personal settings после probes; no extra process root после Offline entry/scene reload/domain-reload-disabled Play.
- [ ] Headless identity registration/readiness работает без graphics precache на authored path; prepared generated manifest extension добавляется в task7 через generator-owned readback, physics/state sync остаются.
- [ ] Перенести existing fresh RequestInitialState за current local registrations/reference barrier; не задерживать уже полученный snapshot, отбрасывая последующие increments. Network owner наблюдаемо устанавливает reference closure сериализуемого SDK snapshot (persistent SDK + уже существующие avatars/items/anchors), actual spawn-before-state order и server capture cut. Если inventory отсутствует, это unresolved gate, не таймер или требование всех будущих avatars.
- [ ] Reject stale request/response/cancel completion по RunKey/epoch; подтвердить reliable ordered Target snapshot→post-capture increments. Request после readiness сам достаточен как уведомление, второй ACK не добавлять. Wire correlation token либо bounded queue/fresh resync допускаются только при конкретном failing probe и принятом узком network scope; нельзя потерять post-capture events из-за deferred application.
- [ ] Probe `AvatarAdmissionHasNoSnapshotCycle`: server Ready после composition+policy gates допускает создание avatars/stock независимо от remote snapshot; local barrier ждёт только фактические snapshot refs, не yet-unspawned avatar. После свежего apply открывается remote LocalPlayable; host не запрашивает второй snapshot, dedicated не ждёт client hook.
- [ ] GREEN all cancel/reload/direct/headless и Relay permutations/old response/late object traces, отсутствие missing SDK targets и lost increments, duplicate IDs/subscriptions/owned objects; AndroidCompileGate. Изолированный two-process network probe по existing harness, если available; без его прогонов delivery не заявлять. Generated manifests ждут task7 extension, authored map admission уже имеет Relay gate.

## Задача 6. Адресная миграция authored карт

**Зависимости:** задачи 2–5 GREEN, native Editor чужая PhysicalArenaLayout миграция завершена; human не Play; immutable authored station stage accepted.

**Файлы:** create MapBootstrapMigration.cs; адресно Lobby.unity, Maps/ReferenceMap04, ServiceYard, TestMap1/2/3; MapData/catalog/prefabs необходимые к этой миграции. Документация current state, не история.

- [ ] Migration dry run показывает exact removals/adds, refs и backed-up IDs/world transforms. Не менять geometry, authored station scene NI и UXR IDs. StationKey назначить один раз в generator-owned ArsenalStationCompositionBinding по согласованному handoff, MapRoot сохранит только refs; второй key writer не создавать.
- [ ] Установить один MapRoot в каждую сцену, configure explicit groups/layout/zones/stations/map kind. Удалить legacy authored MapReferee и coordinator только после verified runtime counterparts; удалить лишние SDK scene copies после settings comparison.
- [ ] Validate complete registry и новая probe map без ручных managers; два missing-input probes должны fail до writer. Scene services не получают nested NI и не становятся Environment.
- [ ] Read back saved assets selective YAML GUID/serialized refs; MapGameplayHierarchy.ValidateAll(), PhysicalArenaLayoutMigration.ValidateAll() и пре-flight без global SaveAssets.
- [ ] AndroidCompileGate, existing structural checks (scene IDs/station refs/weapon rules), временные startup/late-join/cancel probes. Bake Occlusion (all maps) если миграция меняет геометрию/flags; не запускать без lease.
- [ ] Human Unity/шлем check: карта входит в warmup, 1–2 клиента видят одинаковые config/mode/arsenal/deployment, match/pause/resume/stop/next работают, direct map debug одинаков; shared references/позиции и физическая калибровка сохранены.

## Задача 7. Внешний generator adapter и generated snapshot admission

**Gate:** generator design/plan задают owner API; implementation GREEN manifest/registration/composer и visual Binding handoff ещё обязательны. Task5 Relay ordering/reference-closure proof выполнен на authored path. Без этого среза tasks 1–6 могут закончить map bootstrap на authored stations.

**Файлы:** MapArsenalCompositionAdapter.cs, MapRunAdmission.cs, NetworkStateRelay.cs и accepted bootstrap/network-owned readiness types; NetworkUxrIdentity refs/readback по generator handoff, UID algorithm не присваивать. Чужие Preset/Style/Builder/Composer/CompositionBinding source без согласованного write-set не менять.

**Owner interfaces:** `ArsenalStationResolver.ResolveDescription(ArsenalStationBuildInput) -> ArsenalStationDescription`; `ArsenalStationComposer.PrepareComposition(description, ArsenalCompositionScope) -> ArsenalCompositionHandle`; `ValidateReady(handle) -> ArsenalReadyReport`; `Activate(handle, ArsenalAdmissionToken)`; handle.Dispose(). Scope/token адаптируют map RunKey/Epoch/scene/cancellation и description hash. Binding единолично хранит StationKey; Report подтверждает actual registered identity/layout hashes. Геометрия и semantic seed — generator/network owners.

- [ ] RED stale manifest/capacity/reorder/unknown decoration probes по accepted generator harness; baseline external failures не подменять bootstrap guesses.
- [ ] Adapter delegates owner API, records selected decoration/fallback+layout hash в resolved config, prepares inactive scope и verifies actual result. Composer Ready только CompositionReady contribution; Activate получает внешний current admission token, не сам разрешает map gameplay. Same source preset/Style, no second entries/key authoring.
- [ ] Sized → universal → bare cases проверяются functional-equivalent inputs, no artwork deformation; overflow placement invalid отдельно от decorative availability.
- [ ] Runtime branch разрешён только с deterministic slot keys/Uxr IDs/manifest и late-join match before registration. Если external owner выбрал bake, adapter проверяет current source/version hash и загружает derived cache; editor preview не source.
- [ ] Extend task5 Relay barrier actual generated role registrations/readback, не использовать wall Ready как proxy всех SDK refs. Probes `GeneratedAnchorMissingAtInitialRequest`, `GeneratedSnapshotOldRunResponse`, `GeneratedDescriptorSpawnSnapshotPermutations`, `GeneratedLateNetworkItemArrival`: descriptor/NI/stock/snapshot/event все порядки, stale unload/cancel response не открывает канал, taken/thrown magazine не телепортируется поздним binding. Required refs только текущего snapshot; yet-unspawned avatar не requirement.
- [ ] GREEN runtime/server/client/headless manifest match, exact SDK reference resolution и snapshot→increments без потерь, reorder/add/remove around capacity, pending unload cancellation, no held-object rebuild; AndroidCompileGate и human visual/ergonomic acceptance. Если minimal delayed fresh request не доказывает гарантии, network owner предъявляет trace и узкий token/queue/resync proposal root, не заявляет GREEN и не меняет transport автоматически.

## Задача 8. Закрепление принятой логики и выпуск

**Gate:** пользователь принял изменённую механику предыдущих срезов в Unity/шлеме. До этого task открыт, новые permanent expectations не писать.

- [ ] После принятия перенести confirmed probe invariants в scoped permanent tests: config ownership/serialization, readiness/duplicate/cancel, mode transition revision/Paused, identity manifest/admission.
- [ ] Обновить конкретные outdated tests с комментарием снятого требования; no baseline rewrites, mask errors и reflection testing private fields как замены ownership.
- [ ] Запустить `run_tests(mode="EditMode", assembly_names=["VrBattlegrounds.Tests.EditMode"])` → `get_test_job`, AndroidCompileGate и targeted network validation. Fresh raw PASS с пределами обязательны.
- [ ] Проверить whole diff/docs/current lifecycle table и canonical writer; провести root/reviewer review. Зафиксировать какие файлы foreign dirty, не stage их.
- [ ] Scoped commit только разрешённые exact paths после acceptance; Git/Plastic independently verify по Docs/version-control.md. Документы можно commit отдельно по исключению; первый implementation срез пока без коммита.

## Self-review плана

Plan покрывает process/session/map/mode lifetime, captured Series mode vs future Selection, один commit owner, all start paths, preflight, CompositionReady vs server Ready vs remote LocalPlayable, Mirror/UXR identity, external generator API, Relay fresh snapshot/reference closure/order/stale cancellation, отсутствие avatar admission cycle, unload/cancel/direct/headless и migration. Реализация разрешена; первый pure/authority срез GREEN с указанными пределами. Runtime identity/Relay proof ещё отсутствует, SDK/visual/generator shared-file writes не включены без handoff. Human gameplay acceptance отдельна.
