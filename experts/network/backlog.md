Обновлено 2026-10-15. Кандидаты вне хаба; порядок — приоритет.

## Следующая задача по прямому выбору пользователя

2026-10-15: «после текущей запланируй исправление перезаписи _assetId».
После исправления текущей AvatarRoot гонки — [mirror-preview-scene-objects](../../tasks/mirror-preview-scene-objects/Readme.md):
прекратить повторную перезапись _assetId в OnValidate/SetupIDs при LoadPrefabContents/сохранении prefab/variant.
Сейчас только очередь/план, SDK-патч не начат и номер не зарезервирован; прежний нормализатор не считается root fix.

## Обещания и ожидания (2026-10-14, передача Claude → Codex)

Активные задачи: `precache-server-guard` (сторож прогрева, `ecfbfdee7`), `network-sync-hardening` (Mirror patch 4
`4224310c4`, SDK-патч 80 `74727da7a`). SHA после rebase на `97a320c88`; Unity-проверка — собственный тикет 643.
Обе: Unity/Android/EditMode Network+Weapons проверены в 643 (429/429, 0 failed/skipped), код не принят/не влит.
Устаревшие running сняты штатным stop; СТОП 6539 снят объявлением 6805. Windows build 651 PASS, аренда освобождена;
побочный PC.asset не импортирован. E2E functional 24/24, LogVerdict RED: поздние события GhostAvatar, teleport error=0.
Повтор того же E2E GREEN/enforce 24/24; первый RED не снят. Server-only+8 ботов с графикой 658 завершён:
Ready, precache=false, childCount/liveCopies=0 в трёх снимках за 92,36 с. Физическая Quest-приёмка pending.
Поздние ghost events — подтверждённое окно без server-side retirement admission; gameplay 7027 подтвердил
отсутствие общего retirement-сигнала. Пользователь выбрал проект исправления в этой задаче:
`contracts/avatar-root-event-admission-v1.md` — AvatarRoot netId+UniqueId, один owner Mirror, штатные17–21 Б аргументов,
без нового decode/SDK patch. Код написан, source review без production blockers;713 DONE/Android PASS,
EditMode428/429 (один старый Cmd-membership), Windows E2E24/24 GREEN. User/Quest и отрицательная ветка pending; вливания нет.

Обещано другим (кому — что — событие):
- bots (`bots-fix`) и performance (`perf-stress-scenario`): сообщить о вливании `precache-server-guard` — они делают
  регрессию (матч 8 ботов / perf-stress на сервере с графикой). 6324→6330, 6326→6332.
- weapon-system (+ копия performance): после вливания `statesync-cost` — замер выстрела до/после (этап shot-cost), 5871.
- avatar-ik (`uxr-precache-once`, UltimateXR patch 81, 91470fff6): ревью 6527 — ACK с условием обнулять
  `_precacheCoroutine` при обрыве корутины. Условие OnDisable/exception снято source-ревью 6854 на `b00b03f33`;
  исчезновение аватара без scene change отдельно передано владельцу. Ждём runtime-прерывание и замер
  bots клиента позднего входа (childCount `UxrPrecachedInstances` до/после).
- launch-infra (`quest-perf-pipeline`, e2e-smoke-kit): сетевое ACK дано (6425, 6437; условия выполнены 6441/6445).
  Результат 6829: Windows input `87643327108f`, selftest 19/19 по сообщению владельца, hello=1/соединение,
  ready повторяется после reload, Unknown message id=0. Это не Quest/reconnect. Ответ о получении — 6866.
  Прогон арсенала 6850 имеет 4 teleport errors, но в его input нет нашего Mirror4. Собственный run с патчем:
  teleport error=0, общий RED по другим событиям; владельцу передано 6998, приёмки/вливания нет.
- avatar-grip (`hand-rig-quality`), 6429: Mirror-патч preview-сцены (`_assetId`/sceneId, пункт `mirror-preview-scene-objects`
  ниже) и общий помощник сохранения префаба — взяты в зону network; решение 6589: запуск после `network-sync-hardening`;
  ответ прочитанному решению — 6647; при старте
  сообщить им номер патча и этап. До того они нормализуют `_assetId` сами.
- manipulation (`grab-authority`, карман): закрыто — ACK 6343, условие снято 6352. gameplay 6342/6373 — без возражений.
- Сопровождающий: infra_issue 6447 (хук sdk-patch-guard) принят в работу (6474) — ждать выпуска, ничего не делать.

## Кандидаты

1. `direct-mirror-inventory` — реестр прямых путей Mirror мимо UXR (16 NetworkBehaviour, 14 Cmd, 14 ClientRpc, 2 TargetRpc, 9 NetworkMessage, SyncVar): состояние/событие, кто шлёт, опоздавший, reconnect, ServerOnly, хост; сверка с `RpcCarriesNoStateTests`. Состав (grep 2026-10-11): `PlayerSession` 10 Cmd, `PlayerController` 4 Rpc; GameMode, EliminationMode, RespawnMode, MapReferee, Series, SessionManager, TeamNameService, MapRunAuthority (8 Rpc); MatchEconomy 1 Rpc; арсенал 4 класса SyncVar; `CartridgeIntake` 2 Cmd + TargetRpc; сообщения: боты 2, DebugMode 3, StressTest 2, подключение 2 — `Docs/tasks/T-34-late-join-state-snapshot.md` «Все экземпляры».
2. `message-rules` — единые правила NetworkMessage: `RegisterHandler` (GameNetworkManager) против `ReplaceHandler` (BotNetwork, BotMatchNetwork, DebugModeNetwork, StressTestNetwork), проверка роли/авторитета отправителя, регистрация/снятие по жизненному циклу — grep `ReplaceHandler<` в `Assets/Scripts`.
3. `late-join-parity` — ярус C сценария `late-join-state-parity`: T-34 🔧, снимок с жизнью у второго процесса и гонка `Life = 0` раньше снимка не проверены — T-34 «Сделано (2026-09-29)», «Проверки».
4. `client-scene-window` — события клиента-автора в окне смены сцены молча отбрасываются (`NetworkStateRelay.Send`, «клиент ещё не готов»); сейчас в это окно ничего не держат — T-34 «Открытые вопросы».
5. `despawn-filter-live` — `DespawnedObjectEventFilter` после правки не проверен живьём хост + клиент; смена скина двумя процессами — коммит `4068663ef`.
6. `authority-handover-window` — смена автора оружия при фиксации прежнего в пути: ~RTT фантомный выстрел, две фиксации с одной ревизией неразличимы; с weapon-system — `Docs/UltimateXR/known-issues.md` Issue 38 «Остаток».
7. `combat-decisions` — T-23 (компенсация задержки) и T-24 (где симулировать пули) ждут замеров на Quest; с performance и weapon-system — `Docs/combat-networking.md` «Что измерить на Quest — и как», «Условная рекомендация».
8. `patch-markers` — маркеры в `UxrMirrorAvatar.cs` неединообразны (патч 1 «ПРАВКА ПРОЕКТА», патч 9 «VR Battlegrounds (патч 9)»), поиск по `VR Battlegrounds patch` их не находит — через `patch-reserve`/правила SDK.
9. `network-sync-doc` — `Docs/UltimateXR/network-sync.md` (2026-03-21) описывает удалённые T-12 `_serverBroadcaster`/`_initialStateLoaded` и баг `TryGetComponentById`, закрытый патчем 51; docs-этап с владельцем Docs.
10. `spectator-echo` — при T-12 записано «зритель получит собственное событие обратно» (отсечка по netId игрока, у зрителя его нет); статус unknown — `afda2605b`, journal 2026-08-18.
11. `round-winner-late-join` — победитель раунда только в `RpcOnRoundEnded`; если «Обзор» показывает его опоздавшему — SyncVar в `EliminationMode` (решает gameplay) — T-34 «Открытые вопросы».
12. `uxr-unique-id-kb` — раздел базы знаний: реестр компонентов и UniqueId (генерация, `UxrUniqueIdPersister`, выравнивание по netId); перешёл из `uxr-core-kb` manipulation (карта ред. 2) — `Docs/UltimateXR/known-issues.md` Issue 11–12.
13. `e2e-reconnect-port-seam` — `SessionRecoveryOnReconnectScenario.ConnectDirectly` (`:869-894`) в собранном плеере зовёт `NetworkManager.StartClient()` мимо шва портов `PrepareNetworkStart`. В Editor уже идёт через `RequestClientConnect`. Перевести плеер на `GameNetworkDiscovery.RequestClientConnect`, иначе прогон E2E с изолированной парой портов переподключится на 7778. Владельца у сценария в карте нет; передано launch-infra (событие 3685, после вливания `e2e-run-isolation` `7ceb8a08`).
14. `mirror-rigidbody-enable-arg` — `UxrMirrorNetwork.EnableNetworkRigidbody` (`UxrMirrorNetwork.cs:242`) передаёт в `EnableNetworkTransform` поле компонента `enabled` вместо параметра `enable`: NetworkTransform предмета включается и выключается не по запросу SDK. Сейчас у оружия нет NT и NetworkRigidbody, поэтому вреда нет, но первый предмет с NT получит неверное состояние. Патч SDK — номер через `patch-reserve`. Найдено при разборе 4296, 2026-10-12. Там же (manipulation 5191): `UxrNetworkManager.SetNetworkTransformEnabled` (`UxrNetworkManager.cs` ~167) передаёт свой `gameObject` вместо `target`. Оба — одним патчем.
15. `mirror-preview-scene-objects` — в worker-редакторе preview-сцены префабов оружия (LoadPrefabContents без выгрузки) получают sceneId от `NetworkScenePostProcess`; сервер их спавнит, у клиентов «Spawn scene object not found» (bots 5078, разбор launch-infra 5204). Патч Mirror: под `UNITY_EDITOR` исключить объекты `EditorSceneManager.IsPreviewScene(gameObject.scene)` и PrefabStage в `NetworkScenePostProcess` и `Utils.IsSceneObject`. Номер — `patch-reserve`, маркер `VR Battlegrounds patch`. Обход до патча: preflight PlayLaunch (launch-infra) и рестарт worker. Тот же корень (avatar-grip 6384, 2026-10-14): `NetworkIdentity.SetupIDs` (`NetworkIdentity.cs:542`, ветка else ~606) в preview-сцене `LoadPrefabContents` пишет `_assetId = 0`, `SaveAsPrefabAsset` сохраняет 0 — шум в диффах у всех редакторных писателей префабов (Tr15OpticBuilder, GhostAvatar, grip-extraction; блокер weapon-system 6378). Одним патчем: preview-сцена/PrefabStage — не сцена (sceneId) и не повод менять `_assetId`. Плюс общий помощник сохранения содержимого префаба (save + канонический `_assetId` + сверка provenance UltimateXR `__prefabGuid`/`__isInPrefab`) рядом с `NetworkAssetIdNormalizer` (`Assets/Editor/VR_Battlegrounds/VersionControl`), владелец — network. До патча писатели нормализуют сами.
16. `foreign-avatar-start-events` — клиент шлёт Cmd «Отправка состояния» из `UxrAvatar.Start` и `UxrGrabbableObjectAnchor.Start` чужих аватаров (isServer=False; bots net-round1 attempt-2, `mppm4877b184-Editor.log` ~81417). Лог стоит после `StateEventAuthority.ShouldSend`, значит, событие уходит. Выяснить, что за событие и почему фильтр пропускает (гипотеза: netId ещё 0 → `IsAuthor` по умолчанию true), и проверяет ли сервер в `CmdComponentStateChanged` право отправителя. Найдено при разборе 5094, 2026-10-12.
17. `ledger-fork-recovery` — защита на будущее (не замена правки weapon-system по 5094): клиент при обнаруженном форке учёта запрашивает у сервера состояние одного объекта тем же `SerializeState`, что опоздавший. Правило из 5094: компонент, адресуемый после Awake, обязан принимать повтор события сразу, без зависимости от Start — внести в Docs (combat-networking или session-architecture) отдельным docs-этапом.
18. `inactive-spawned-diagnostic` — после 5446: Mirror не спавнит новому клиенту неактивные объекты (`NetworkServer.cs:1302`, `activeSelf`), а снимок их пропускает («N из M адресатов не зарегистрированы»). Моя часть: (а) при сборке снимка на сервере — `GameLog.Network.Error`, если адресат лежит на неактивном заспавненном NetworkIdentity; (б) правило «сетевой объект на сервере не деактивировать, пока заспавнен» — в `Docs/session-architecture.md`; (в) поправить допущение в комментарии `InitialStateBarrier.cs:13-15`. Исправление кармана — manipulation (5458): прятать без SetActive(false).
19. `uxr-statesync-cost` — ВЗЯТО в задачу `network-sync-hardening` (решение пользователя 2026-10-14: всё одной задачей). (перф, находка weapon-system 5871, 2026-10-13) — (а) `Assets/Resources/UxrGlobalSettings.asset`: `_logLevelCore/Avatar/Locomotion/Manipulation/Networking = 5` (Verbose) → `UxrManager.ExecuteStateSyncEvent` (`:672-675`) пишет Debug.Log на каждое принятое событие у клиентов Quest (строка + стек). В Release уровни не выше Warnings (ассет для сборки или рантайм-ограничение вне Development), Verbose — по отладочному переключателю. Ассет без владельца в карте экспертов. (б) `UxrStateSyncImplementer_1.SyncState` (`:102-112`) — `GetMethods()`+LINQ на каждое событие → кеш MethodInfo по (тип, имя, число параметров), патч SDK. Замер выстрела до/после — weapon-system (этап shot-cost), копия performance.
20. `teleport-state-unknown-target` — ВЗЯТО в задачу `network-sync-hardening` (решение пользователя 2026-10-14: всё одной задачей). (дефект, старый, 2026-10-13) — на сервере `UxrComponentNotFoundException` для событий `UpdateTeleportState` с компонентов рук аватара клиента (CC_Base_*_Clavicle/Upperarm/Forearm), 6 Error в e2e dedicated-server-arsenal (launch-infra 6048), видно и в августовских прогонах. Не связано с патчем 77. Гипотезы: событие уходит до выравнивания UniqueId аватара по netId (CombineUniqueId при спавне) или компонент есть только на локальной копии. Артефакты: worktree launch-infra, `Tools/e2e/results/20261013-211416-dedicated-server-arsenal`. Если компонент только локальный — решение с avatar-ik.
