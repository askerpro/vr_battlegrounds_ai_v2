# cc3: common perf probe

Текущий автоматический `analyze.py` исключён из маршрута приёмки: launch-review
e2b1fe50fa7c4d06 выявил, что `passedGates` не требует полный успешный lifecycle,
точную identity и отчёты всех ожидаемых участников; отсутствующие console counters
считаются нулями. Его PASS не подтверждает эти условия. До проверки качества пользователем
expectations не переписываются. Автор читает raw artifacts вручную: собственные
RunId/owner/runTag и полный roster; явные console counters каждого процесса;
watch.completed; Idle/Play=false/CleanupPassed и отсутствие cleanupWarning;
удаление только своей неизменённой заявки; полные SHA256-verified архивы всех процессов.
Missing/unknown остаётся открытым, а не считается нулём. После пользовательской приёмки
проверялка и её тесты исправляются в этой же задаче перед завершением.

Обновлено 2026-10-18. Source pin/hashes в `common-probe-source-manifest.json` — историческая офлайн-подготовка, не принятый объединённый input и не запущенный Unity пакет.

Текущее состояние: база `50c27ae71`, seed backend уже принят в `192eb34f8`; B0/B3 задают одинаковый `UseRandomSeed=true`/`RandomSeed=20261018`. B5 показывает actual server receipt, B4 сохраняет его до cleanup. Для допуска сравнения вручную проверить applied=true, beforeBots=true, exact seed/scene на H и C; текущие test expectations не переписаны. SDK81, SDK82/R08/Revolver и обе стороны Spectator перенесены в свою задачу из точных checkpoints/WIP; их совместная runtime-приёмка впереди. Старые внешние handoff/grants не описывают текущую own composition. `AllowSpectatorCell=false` остаётся до функционального подтверждения Spectator.

Технически упакованная композиция common36: H — accepted Hybrid gameplay из B плюс общие собственные technical
dependencies SDK81/82/84/86/87/88, Mirror6/7, оружейный авторинг, Spectator и companion stand;
C* `5489395093f9f36c89b79df95e1b3c2de561583b`, source tree `1d0d59e88472c0aeb2d89b0f84451a32375c47ad`;
accepted=false, целое дерево H пока не принято. C — тот же common36/probe2/tools плюс cutover48.
Legacy frozen77 дополнен десятью общими M:
HeadlessPrecacheGuard, UxrGrabbableObject и UxrGrabbableObjectAnchor (SDK87, Replay-only derived proxy),
Mirror/Core/NetworkManager.cs (Mirror6), Mirror/Core/NetworkIdentity.cs (Mirror7),
UxrComponent.cs, UxrUniqueIdImplementer_1.cs и UxrMirrorAvatar.cs (SDK88), а также
Editor/VR_Battlegrounds/Testing/StandProtocol.cs и PlayModeStandParticipant.cs
(typed request-team-change через существующий Cmd, без прямых writers); итого frozen87 Assets paths.
Cutover48/probe2/excluded test1 прежние. Авторские файлы берутся из одного frozen C*,
probe — побайтно из2fa11b27; исходный worktree и его index/HEAD не меняются.
Точные inventory и H↔C operation diff сохраняет build-inputs.py в ignored reports.
Обе композиции требуют свежей semantic compilation и actual runtime receipts.
Common36 H `4d9f8d4e229788de43f6d9fe9817c761faa756f9` (tree `834fe10e1cf69a4ac08c7dd9a3f0447924d992fa`)
и C `1152421fcd958d15036c669b284b7e3db0e5f9ae` (tree `7754fe7c6a4d0d1b212089af4cfde706aab0d2c6`)
зафиксированы в `tasks/bots-fix/reports/cc3-perf-inputs/common36-attempt-1.json`, SHA256
`c84e3033b2ca20db7abe70b198dcbaa37ebd8fa2f5045076b3642e5cca101c21`.
Это только упаковка: Assets87 A18/M55/D14, common36/cutover48/probe2/excludedTest1/tools12,
LF84/CR0, exact общие StandProtocol/Participant M из54893950, H↔C=cutover48, Tests=B, HEAD/index неизменны.
Компиляция/runtime ещё не выполнялись; ссылки дописаны после frozen tool/docs blobs C*54893950.
Расширение общего companion stand не меняет perf expectations.
Историческая упаковка common34 из C* `53e3a85fd6c2f88d7593ffe2a9aa94bd5e9eb1a2`, source tree
`ac321d34695ffe76c7ae269bae994670d4adec22`, дала следующие snapshots, не являющиеся current common36 input:
H `1a20c093aca8075b0ff772685510f46ccf119883` (tree `a6991b2ff5b38b00649ccd85afc7d9fd9d166dc3`)
и C `1b8497e673cfc4397a6c03412067e436f9a7e0bd` (tree `403351049f3c1ff5766fe543779420106ee39f49`)
зафиксированы packaging-only inventory `tasks/bots-fix/reports/cc3-perf-inputs/sdk88-mirror7-attempt-1.json`,
SHA256 `8e7cadd4ed425d3b97a30c64d632d48a6bb8ccdd71ad71d6caffded970b13afc`.
Фактические packaging guards: A18/M53/D14, common34/cutover48/probe2/excludedTest1/tools12,
LF82/CR0, точный H↔C cutover48, Tests=B, HEAD/index неизменны. Это не игровые test expectations.
Предыдущие common30 H `d5933476e` / C `e1d95e8c2` имеют только офлайн-компиляцию, без runtime;
их результаты не переносятся на эти common36 inputs.
Ниже старые SHA и compilation оставлены только как история.

- H = accepted `origin/dev` (`1b2f88612f563172db6c57249feae2f9d46d0491`) с неизменным default Hybrid + pinned own probe/tools; C = тот же B + отдельный exact own cutover manifest (`4210b8ed644a2ecbdddad9af38f4b1ee2298c403`) + **побайтно та же** probe (`2fa11b2767f23d4c709f739deb629fd378307c97:Assets/Scripts/Debug/BotCombatStand/BotStandMatchProbe.cs`, SHA256 `5a0611c9aa311f9ae9b4eed7258041defac5bf43a7193cd2435b4df5a3e130cc`). SDK/guards из B одинаковы; целое historical дерево, old SDK и чужие pending компоненты не копировать.
- При материализации — одна definition probe/Request/Gate и прежний GUID (`5d236333c33745b59741cdb31bebf141`), без параллельного класса. Raw source хранится только в собственном TEMP до разрешённого immutable overlay. Офлайн-компиляция проверена для обеих точных сборок: H (`17` sources, `errors=0`, `warnings=8`, `warningsInPinnedProbe=0`) и C (`18` sources, `errors=0`, `warnings=29 CS0618`, `warningsInPinnedProbe=0`).
- Старый Request имеет `RunTag`/`OutputDirectory`/`ExpiresUtc`/`WriteSeconds` (общий период `10` с). `FinishActive` принимает **один `string`**, без `completed`-bool; поля `DirectorEquip` нет. Подготовлен собственный guarded `cc3-perf` пакет (`package.json`, `b0-preflight.cs`, `b3-runtime-launch.cs`, `b4-runtime-watch.cs`, `b5-runtime-status.cs`, `b6-runtime-stop.cs`, `compile-check.ps1`, `analyze.py`), скомпилированный в C# 6 (`errors=0`, `payloads=5`). Hybrid gameplay API не дописывается ради `cc2`.
- Дополнительные trace collectors выключены: `B7`, Blaze/public/shot-tail subscribers, `NetObserver`/`NetReceiveTrace` не устанавливаются. Стандартные match timeline/frame/network metrics common probe остаются включены. Runtime trace-off пока не доказан; private reflection/придуманный public toggle запрещены.
- Одинаковые B/SDK/platform/Unity/hardware/scene/NavMesh/Quality/URP/Host topology/controlled seed/прогрев/окна H/C. Existing asmdef имеет `UNITY_EDITOR`: built player/Quest запуск не подтверждён. По `vr-test-stand` `8857` (ответ на `8548`) в `PlayLaunchConfiguration` / `DebugBootstrapSettings` сейчас нет `UseRandomSeed`/`RandomSeed`, а спавн ботов при `BotCount > 0` происходит в `DebugOrchestrator.OnServerSceneChanged` до `Ready == true`; по требованию `performance` `8491` у `vr-test-stand` запрошен **Вариант B** (`UseRandomSeed`/`RandomSeed` в `PlayLaunchConfiguration` с вызовом `Random.InitState(RandomSeed)` в `OnServerSceneChanged` строго до `EnsureCount(botCount)` и диагностическим receipt в `PlayLaunch.Status()`).
- `N8`: Combat `p95(C8) <= fresh H8` по approved scope; `H10`/`C10` — scaling/GC без нового tolerance. GC: `bytes/frame`, `bytes/s`, `duration/frames` и `gen0 collections`/`rate`; нет pause duration/all generations (`UNKNOWN`). Invalid recorder/marker — `UNKNOWN`. Стандартная observer overhead одинакова; workload/длительности показываются `performance`.
- Сетевой контроль: по `network` `8657`/`8834` и `gameplay`+`network` `8843`/`8847`/`8849` прогон `S8P` (`Role.Player`, `TeamIndex=0`) отклонён и исключён. Канонический контракт пассивного наблюдателя для `S8` — `GameRole.Spectator` (`PlayerSession` с `Role=Spectator`, `TeamIndex=0`, `ActiveAvatar=null`); ячейка `S8` остаётся заблокированной (`AllowSpectatorCell=false`) до внесения владельцами `gameplay` и `network` согласованной серверной цепочки (`PlayersManager.HandleSpectatorConnect`, `GameNetworkManager.OnSpectatorConnect`/`OnServerReady`, `Role == GameRole.Player`-гейты в `EliminationMode`/`DebugOrchestrator`/`RespawnMode`/`UnregisterSession`/`TeamChangeRequests`). В `cc3-perf` ячейка `RH` (`1 human + 8 bots`, `Role=server`, `ClientCount=1`, `GameRole=Player`) измеряет контрольный `remote-human` с исключением собственного локального аватара/сессии (`ownAvatarNetId`/`ownSessionNetId`).
- Обязательны actual `final=true`/own `runTag`, нулевые console `errors`/`exceptions`/`asserts`/`relayErrors` и `timelineDropped`, recorder validity, четыре завершённых раунда (`roundScored` 1..4) либо подтверждённый `naturalEnd` с actual rounds. `FinishActive` return не доказывает файл: `Write` ловит IO до `Warning`. Чтение actual `final` после единственного вызова; mutation не replay.
- Guarded own `owner`/`run`/`request`, `StateEventAuthority.IsWorldAuthority` для admin actions, durable intents до mutation. Затем own `Cancel`, `Idle`/`Play=false`/`CleanupPassed`, удаление только unchanged own request, полные SHA256-verified `PlayLaunch` logs до немедленного `finish`/`receive`. Common probe этим lifecycle не управляет.

Исторический scope 2026-10-16: офлайн semantic build H (`1b2f88612`) и C (`4210b8ed6`) с pinned probe и 5 C#6 payload-ами `cc3-perf` завершился без ошибок. Это не компиляция и не runtime нового объединённого input; текущие остатки перечислены в `../../completion-plan.md`.
