# Пакет Unity: вооружённое движение и настоящая стрельба

Актуализация допуска 2026-10-10: inbox3172 разрешает неизменённые 21 пути
`d942dce3db9caeecfbe5e023f20f0117bb8bb0fd` (SDK/context/writer + canonical PPK/AK105)
в собственном DEVELOPMENT input. Принятая база HEAD/origin/dev/published B — `1001415`.
Общий coordination mode off установлен сопровождающим по поручению пользователя;
старое требование stage permit ниже историческое. FIFO/RUNNING lease/guard обязательны.
SDK71/72 и prefab-пакет больше не ожидаются; собственный checkpoint собирается заново.
Synthetic equipment probes исключены по `prepared-probe-audit-2026-10-10.md`.

Дата: 2026-10-10. Подготовка без исполнения. Проверенный HEAD checkout: `8e3c513f5627de786ea0df0c1e6db5d81c78e09c`. Linked worktree; главный checkout только для чтения. Этап root: `armed-development`, epoch 5/revision 15. Билет, checkpoint и аренда в этой подготовке не создавались.

## Допуск до request

- Root получает формальное согласование совместного DEVELOPMENT-кандидата и права Unity composition от weapon-system. Нужны точные manifest/SHA SDK71, исправление actor attribution и canonical binding prefabs; согласованные контракты и действующий stage permit. Старый текст `contracts/armed-development-composition.md` ещё описывает SDK70 и не служит разрешением SDK71.
- Root проверяет узкие owner diff и joint compilation, затем делает свежие fetch/rebase `origin/dev` с сохранением tracked/staged/untracked. База B — актуальная принятая база broker и `origin/dev`, а не исторический SHA из этого документа. При maintenance/несовпадении базы/допуска — остановить заявку, уведомить владельца; не чинить infrastructure и не повторять request вслепую.
- До checkpoint проверить LF только у собственных изменённых managed source/docs/tools и staged blobs. Чужие файлы не нормализовать. Снимок A включает все зависимости совместного кандидата, payload и canonical prefabs. `checkpoint --path` повторяется, parent снимка — текущий HEAD, не предыдущий технический input.
- Проверить существование `Assets/Scenes/Debug/BotCombatStand.unity`, её runtime MapCatalog/Play Launch admission, валидные WeaponInfo `PPK_Weapon`/`AK105_Weapon`, team avatar indices для точных MEF/Cyborg source GUID. Номер аватара нельзя предполагать по старому preview; подтверждать фактический `SourcePrefab`.
- Подготовить до lease durable output/report manifest, точные имена артефактов, timeout и очистку. Первое предъявление пользователю — движение с Pistol/Rifle и реальные выстрелы обеих моделей. Безоружный preview не заменяет результат.

## Пакет A: authoring вне Play

Существующий entry point `tasks/bots-fix/tools/calibration-build.cs` выполняет последовательность ниже. Указать новый уникальный report folder до checkpoint; неизменённый файл сейчас пишет в `reports/calibration-rebuild`.

```csharp
var folder = "tasks/bots-fix/reports/armed-unity-<ticket>/authoring";
VrBattlegrounds.EditorTools.BotCombatSetup.Build();
var composition = VrBattlegrounds.EditorTools.BotClipComposition.Build(folder);
if (!(bool)composition["passed"])
    return new { executionCompleted = true, composition, calibrationExecuted = false };
var calibration = VrBattlegrounds.EditorTools.BotClipComposition.BuildCalibration(folder);
return new { executionCompleted = true, composition, calibration };
```

Фактические gates: 2/2 composition models, native hips/render-rest и saved prefab rest; 4 sockets; **18 motions** (2 модели × [Pistol 4 + Rifle 4 + Unarmed Move 1]). Положительная finite native speed у всех 18, state/controllers соответствуют модели. Unarmed Move — калибровочная зависимость, не отдельная первая демонстрация. Полные composition/calibration/status сохранить, число и максимумы rest/saved/round-trip читать только после release. `candidateBuilt` не означает gameplay acceptance.

Отдельно снять durable identity readback generated variants после save/reopen: отличные
от canonical source и друг от друга ненулевые Mirror assetId, sceneId=0, один root identity,
BotClipBody NetworkBehaviour в обеих моделях. Текущие YAML не содержат отдельного _assetId
override; Mirror OnValidate назначает правильный ID по prefab GUID в памяти, поэтому
только in-memory проверка Build не доказывает сохранённый ID. Не исправлять ID вручную
вместо Mirror. Если сохранение/чтение не подтверждено, вернуть конкретный authoring blocker;
отдельный Client spawn/late join и weaving остаются самостоятельными runtime-проверками.

Разрешённые записи A: `Assets/Resources/Bots` и новые ignored reports. Исходные модели `Assets/Prefabs/Player`, оружие/магазины/HandsPack, SDK, ProjectSettings и registry этим пакетом не переписываются. Canonical owner prefab patch уже должен быть в проверенном input A; генераторы не дописывают binding в чужой weapon prefab.

Existing metadata check: для каждого `WeaponInfo` загрузить путь `Assets/Data/Weapons/PPK_Weapon.asset` / `AK105_Weapon.asset`, вызвать `VrBattlegrounds.Weapons.WeaponAutomationCapability.ValidateDefinition(info, out error)` и сохранить реальный результат/error. Runtime equivalent: `ValidateHeld(firearm, out error)`. Это capability evidence, не доказательство готового chamber/actor/выстрела. `ArsenalCompositionMetadataCompiler.Compile(...)` — другой, геометрический каталог; здесь не требуется.

Existing `tools/controller-repeatability.cs` делает **два дополнительных Build** и пишет собственный result: запускать лишь если этот отдельный authoring шаг заранее включён в пакет. Он не является read-only metadata check.

Existing equipment fixture `tools/equipment-runtime.cs` исключён из автоматического пакета:
[аудит](prepared-probe-audit-2026-10-10.md) выявил synthetic EquipmentProbe вместо canonical
WeaponInfo, ручной Animator/private snapshot и неверный assembly lookup Editor probe.
Он не доказывает новый normal factory flow, BotClipBody NetworkBehaviour или remote animation.
Исторические 64/64 на новую базу не переносить; устаревшие ожидания сохранить отдельно,
не переписывать gameplay-тесты до пользовательской приёмки. `tools/equipment-build.cs`
**не запускать** как обход canonical owner migration. Пакет A проверяет actual authoring/
metadata, B — существующий Production BotCombatStand; отсутствие remote-проверки остаётся
явным пределом первого вооружённого просмотра, не объявляется PASS.

После A: закрыть только собственные preview scenes/transient objects, сохранить status с baseline и after (preview count, Play, active scene), немедленно finish/receive. Ошибка composition/calibration также завершает пакет; не повторять mutation ради вывода и не анализировать под арендой. Частичные generated candidate assets сохраняет result checkpoint, чужие diff не чистить.

## Пакет B: existing BotCombatStand, штатный normal flow

Новый lease после просмотра результата A и подготовки всех runtime observers. Существующий Editor API находится в `Assets/Editor/VR_Battlegrounds/Bots/BotCombatStandEditor.cs`; runtime — `Assets/Scripts/Debug/BotCombatStand/`.

```csharp
return VrBattlegrounds.EditorTools.BotCombatStandEditor.Prepare(
    new VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions {
        OutputDirectory = "tasks/bots-fix/reports/armed-unity-<ticket>/mef-ppk",
        Profile = VrBattlegrounds.DevTools.BotCombatStand.BotStandProfile.Production,
        Seeds = new[] { 101 }, CaseSeconds = 30f, Capture = true, HandFit = false,
        Rifle = false, WeaponName = "PPK_Weapon", AvatarIndex = <validatedIndex>,
        Cases = new[] {
            VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T01,
            VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T06,
            VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T07,
            VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T08
        }
    }, false);
```

Выполнить для MEF/PPK, MEF/AK105, Cyborg/PPK, Cyborg/AK105; для AK105 `Rifle=true`, `WeaponName="AK105_Weapon"`. До запуска подтвердить actual WeaponInfo.name; options фильтрует именно name, не WeaponId. Каждый Prepare требует вне Play и отсутствия другого owned run. Он использует общий `PlayLaunch` server/respawn/map-playable, затем `MapReferee.GoLive()` и `BotCombatStand.RunCases`; сервер/роль/карту вручную не подменять. Во время Play варианты можно выполнить через existing `BotCombatStand.RunCases(options, ids)` только после terminal предыдущего case run; после Stop новый Prepare.

Ожидание existing API:

```csharp
return VrBattlegrounds.EditorTools.BotCombatStandEditor.Status();
// В Play, дополнительные runtime counts по известному runId:
return VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.GetStatus(runId);
```

Prepare имеет 90s deadline готовности. CaseSeconds ограничен 30s. `finished`/`IsFinished` ждать до terminal; прочитать actual completed/invalidFixture/error/reportPath. `passed=null`/NeedsReview не называть PASS. Capability profile меняет fixture поведение и не заменяет Production normal flow. V01 и L01 в текущем runtime возвращают Unsupported, поэтому не входят в этот первый пакет.

`BotStandSession.Begin` создаёт обычного `BotDirector.AddBot`, выбирает source через AvatarManager, вызывает `BotGunner.Arm`; target получает большой Life=100000, но настоящий урон остаётся. `BotGunner` сам вызывает единственный readiness lifecycle `RequestAutomationPreparation`, затем штатный `TryToShootRound(0)`. Запрещены refill/SetAmmo/ForceReload/chamber warming/fake SDK hands/direct shoot из проверки, второй transform writer и ручное присваивание wrist/IK. HandFit=false сохраняет первый scope движения/стрельбы.

SDK73 требует двух наблюдений в этом же runtime-прогоне: старт из покоя при BotMotionRate=0
должен завершить fixed-time переход и перейти к фактическому движению; при отходе назад
с emission должен сохраняться Back-клип, а native-speed rate соответствовать скорости
корня. Сравнить NavMeshAgent.nextPosition до/после Move с итоговым proxy/root displacement:
статическая семантика API не подтверждает момент обновления simulation position.
Если существующие сценарии не покрыли backward attack, это остаётся непроверенным,
а не подразумеваемым PASS. Не заставлять bot стрелять или двигаться прямыми SDK-командами.

### Недостающие обязательные доказательства перед запуском B

`BotStandWeaponObserver` добавляет bounded timeline публичного SDK: ledger revision/ShotSequence/M/C, emission и фактические DamageReceiving/DamageReceived с ActorSource и Life. `BotGunner.PreparationObserved` вызывается до RequestAutomationPreparation: первая baseline сохраняется отдельно для weapon/epoch/owner, последующие запросы её не заменяют. Uninitialized или отсутствующие значения остаются Unknown; отсутствие патрона в патроннике само по себе не доказывает cold M=0/C=0. Receiving не считается доказательством применённого урона. Lethal SDK-path пропускает DamageReceived: отдельное read-only Died-наблюдение сохраняет фактические LifeAfter/IsDead, а контекст receiving того же кадра помечает Unverified; deferred/direct Die без такого контекста остаётся Unknown attribution. Это не payload применённого урона и не автоматический PASS. Новых NUnit не писать. Перед B проверить observer в текущем совместном source-пакете; ShotsFired и успешный вызов SDK сами по себе не закрывают реальную стрельбу.

Для каждой пары необходимо записать: начальный холодный chamber и ledger до подготовки; actor/session/netId/weapon binding epoch; readiness transition от normal automation; authored TryToShoot result; ровно соответствующие projectile events, увеличение ShotSequence и расход ledger/боеприпаса; ненулевое уменьшение target Life; attribution sourceActor совпадает с вооружённым ботом; свежий actual ShotSource/LOS после attachment projection. Также observed armed движение/strafe/повороты ≥30s без скольжения/инерции weapon. Отсутствующее evidence — UNVALIDATED, не fallback на счётчик бота.

Existing health/ledger APIs брать из точного SDK71 совместного кандидата до checkpoint, не предполагать название ShotSequence/sourceActor по SDK70 в текущем checkout. Authority-negative/no-legal-target можно добавить existing T09/T10 отдельным prepared пакетом; они не заменяют позитивный real-fire результат. Quest/remote/lifecycle/user acceptance имеют отдельный scope.

## Очистка и release B

```csharp
VrBattlegrounds.EditorTools.BotCombatStandEditor.StopOwnRun();
// Дождаться завершения собственного PlayLaunch/выхода из Play, затем:
return VrBattlegrounds.EditorTools.BotCombatStandEditor.Restore();
```

Не заканчивать lease во время активного Unity job/PlayLaunch cleanup. StopOwnRun только для своего owned запуска; чужой Play/dirty scene/prefab stage не трогать. После terminal/cleanup проверить отсутствие своих bots/targets/temporary captures/subscriptions, восстановление Play и scenes baseline, сохранить full status и native reports. Runtime scene/build list/registry/profile не менять: Editor adapter уже принадлежит общему Play Launch. `BotCombatStandBuilder.Build()`/`NormalizeFixture()` сюда не включены: первый может создать map/material/scene, второй переписывает сцену. Если fixture невалидна — завершить пакет и вернуть blocker.

## Точный broker маршрут для root после допуска

```text
python Tools/agents/editor-broker.py checkpoint --label bots-armed-<package>-input --path <owned-root-1> --path <reviewed-joint-root-2> ...
python Tools/agents/editor-broker.py request --owner bots-fix --input <A> --base <fresh-B> --output Assets/Resources/Bots --key bots-armed-authoring-<attempt>
# Runtime пакет B: без --output, если его fixture не пишет assets.
python Tools/agents/editor-broker.py watch-ticket --ticket <T> --owner bots-fix --timeout 1800 --until offered
python Tools/agents/editor-broker.py claim --ticket <T> --owner bots-fix
python Tools/agents/editor-broker.py begin --ticket <T> --token <private>
python Tools/agents/editor-broker.py guard --ticket <T> --token <private>
python Tools/agents/editor-broker.py renew --ticket <T> --token <private>
python Tools/agents/editor-broker.py finish --ticket <T> --token <private> --artifact <worker-relative-file> --artifact <another-file>
python Tools/agents/editor-broker.py receive --ticket <T> --owner bots-fix
```

Request→untiloffered→claim→begin→guard — одна непрерывная foreground orchestration без model sampling между offer и claim. Не создавать второй waiter. Сначала сохранить stdout, затем parse; session_id дождаться. Token только ignored `.agent-state/private/worker-<T>.json`, не inbox/chat. Guard должен подтвердить exact A/worker root/instance, ready и отсутствие чужих jobs. Renew только во время ongoing runtime; не ради анализа/ожидания человека. `finish --artifact` принимает **файлы**, не directory; enumerate report/PNG до release, передать каждый существующий worker-relative path. Отчёты копировать из immutable artifact storage после release; generated assets получать receive. `unexpected=[]` — соответствие output paths, не игровая приёмка.

Полный MCP ответ сохранять durable status, пользователю возвращать компактный результат через `Tools/UnityMcp/compact-result.js` ≤2000 tokens. На timeout inspect durable/native completion, не перезапускать mutation вслепую. Освободить lease сразу после заранее приготовленного пакета, включая failure, до source diagnosis или общения о приёмке. Accepted commit/push и новые gameplay tests — только после пользовательской проверки вооружённого движения/стрельбы.
