# Runtime B: текущий вооружённый кандидат

2026-10-10. Только подготовка по source inspection на HEAD `1001415bed29124af821a9a641d0bae2a7224a5b` и собственному WIP. Final canonical packet — inbox3172; exact checkpoint/manifest и A result фиксирует principal. Этот документ не подтверждает Unity execution и не выдаёт lease.

## Предусловия и ограничение fixture

Прочитаны `armed-unity-package-2026-10-10.md` и `prepared-probe-audit-2026-10-10.md`. B запускается после успешного **A3**: 3 generated models,6 sockets,27 calibrated motions, authored уникальные sourceGuid, durable identity readback, compiled BotClipBody NetworkBehaviour и reviewed canonical weapon composition. A3 GUID generation ещё pending на момент подготовки; до этого discovery/Begin не должны разрешать explicit source. Отдельная собственная RUNNING lease/guard; никакого human Play/dirty scene/чужого запуска. Если accepted BotCombatStand scene не проходит PlayLaunch Plan или ValidateFixture, вернуть InvalidFixture и очистить свой запуск; не перестраивать fixture или менять map/registry под B.

Используются Production `T01` (движение/обход), `T03` (cover/combat), seed101,30s each. Четыре core pair-runs и две ordinary MEF пары для запрошенного покрытия всех3 моделей; если principal уже имеет эквивалентное current normal-factory evidence ordinary MEF, эти две можно исключить с ссылкой на evidence. Каждый ≤90s startup +60s cases +30s stop, верхний budget18min для6 пар (12min дляcore4). Таймаут — собственный StopOwnRun, дождаться завершения stop, release; не повторять Play/Prepare для получения вывода. Если пара не дала backward movement + actual shot, её SDK73 backward scope остаётся NotObserved, не PASS. Это server-only первый просмотр; remote/latejoin/Quest вне B.

## 1. До Play: canonical GUID catalog и launch admission

Следующий C# блок выполняется через существующий guarded execute entry principal после A3. Он читает assets/план. Сохранить result в `tasks/bots-fix/reports/armed-runtime-20261010-models3/discovery.json`; folder должен отсутствовать до пакета, иначе principal выбирает новый уникальный suffix до request.

```csharp
var wanted = new[] {
    "310c90ae0d962f344a1eddbe48a429c9", // MEF Black
    "b8146035f3f436a4799882b3a0d1dd45", // ordinary MEF
    "8e0e06323e4754f479a9f19788554652"  // Cyborg
};
var rows = new System.Collections.Generic.List<object>();
var errors = new System.Collections.Generic.List<string>();
var ids = new System.Collections.Generic.HashSet<uint>();
var library = UnityEngine.Resources.Load<VrBattlegrounds.Bots.BotCombatAssets>(VrBattlegrounds.Bots.BotCombatAssets.ResourcePath);
if (library == null || library.clipBodies == null || library.clipBodies.Length != 3)
    errors.Add("Нужен A3 catalog ровно3 sources.");
foreach (var guid in wanted) {
    UnityEngine.GameObject source = null;
    if (library == null || !library.TryGetCanonicalSource(guid, out source)) {
        errors.Add("Unknown/duplicate GUID или source mapping: " + guid); continue;
    }
    var path = UnityEditor.AssetDatabase.GetAssetPath(source);
    var actualGuid = UnityEditor.AssetDatabase.AssetPathToGUID(path);
    var variant = library.GetClipBody(source);
    var declaredGuid = System.Array.Find(library.clipBodies, binding => binding != null && binding.source == source)?.sourceGuid;
    var identity = variant != null ? variant.GetComponent<Mirror.NetworkIdentity>() : null;
    var sourceIdentity = source.GetComponent<Mirror.NetworkIdentity>();
    bool valid = actualGuid == guid && variant != source && identity != null && sourceIdentity != null &&
        identity.assetId != 0 && identity.assetId != sourceIdentity.assetId && identity.sceneId == 0 &&
        variant.GetComponentsInChildren<Mirror.NetworkIdentity>(true).Length == 1 &&
        variant.GetComponent<VrBattlegrounds.Bots.BotClipBody>() != null && ids.Add(identity.assetId) &&
        VrBattlegrounds.Player.Avatars.AvatarManager.ValidateBotAvatarSource(source, out _);
    if (!valid) errors.Add("Невалидный canonical source/variant: " + guid);
    rows.Add(new { guid, actualGuid, path, declaredGuid,
        variantPath = UnityEditor.AssetDatabase.GetAssetPath(variant),
        variantAssetId = identity != null ? (uint?)identity.assetId : null,
        sourceAssetId = sourceIdentity != null ? (uint?)sourceIdentity.assetId : null, valid });
}
var configuration = new VrBattlegrounds.DevTools.PlayLaunchConfiguration {
    Role = "server", SceneSource = "scene", ScenePath = "Assets/Scenes/Debug/BotCombatStand.unity",
    ModeId = "respawn", AutoGoLive = false, BotCount = 0, ClientCount = 0,
    PauseOnFocusLoss = false, HostIsAdmin = false, Readiness = "map-playable"
};
return new { catalogPassed = errors.Count == 0, errors, avatars = rows,
    plan = VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Plan(UnityEngine.JsonUtility.ToJson(configuration)) };
```

TeamRegistry membership и одинаковый AvatarIndex **не являются предусловием**. Team assignment остаётся штатным и отдельным от immutable canonical source. `BotStandSession.Begin` валидирует subject GUID и control Black GUID до первой сессии; PlayersManager фиксирует source до Spawn/RegisterBot; AvatarManager остаётся единственной factory. Actual SourcePrefab subject/T02/target должен совпасть с selected canonical source, ghost имеет штатный приоритет. У target явный constant Black source, а не default Team2 skin. В discovery требуется catalogPassed=true плюс PlayLaunch Plan.Passed=true; это не повторяет и не заменяет A3 durable metadata readback.

## 2. Prepare одновременно запускает общий Play Launch

Отдельного вызова `PlayLaunch.Play` после Prepare не делать: Prepare сам Plan/Play, затем ждёт map-playable, вызывает штатный GoLive и RunCases. Выполнить блок для каждой выбранной пары после завершения/stop/restore предыдущей, заменив только `sourceGuid`, `rifle`, `weaponName`, `pair` из таблицы. AvatarSourceGuid сериализуется existing Newtonsoft DTO/Prepare pipeline; namespace ниже соответствует current API.

| pair | sourceGuid | rifle | weaponName |
|---|---|---|---|
| mef-black-ppk | 310c90ae0d962f344a1eddbe48a429c9 | false | PPK_Weapon |
| mef-black-ak105 | 310c90ae0d962f344a1eddbe48a429c9 | true | AK105_Weapon |
| cyborg-ppk | 8e0e06323e4754f479a9f19788554652 | false | PPK_Weapon |
| cyborg-ak105 | 8e0e06323e4754f479a9f19788554652 | true | AK105_Weapon |
| mef-ordinary-ppk | b8146035f3f436a4799882b3a0d1dd45 | false | PPK_Weapon |
| mef-ordinary-ak105 | b8146035f3f436a4799882b3a0d1dd45 | true | AK105_Weapon |

```csharp
string sourceGuid = "310c90ae0d962f344a1eddbe48a429c9";
bool rifle = false;
string weaponName = "PPK_Weapon";
string pair = "mef-black-ppk";
return VrBattlegrounds.EditorTools.BotCombatStandEditor.Prepare(
    new VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions {
        OutputDirectory = "tasks/bots-fix/reports/armed-runtime-20261010-models3/" + pair,
        Profile = VrBattlegrounds.DevTools.BotCombatStand.BotStandProfile.Production,
        Seeds = new[] { 101 }, CaseSeconds = 30f, Capture = true, HandFit = false,
        Rifle = rifle, WeaponName = weaponName, AvatarIndex = -1, AvatarSourceGuid = sourceGuid,
        Cases = new[] { VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T01,
                        VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T03 }
    }, false);
```

GUID в блоке exact Black core pair; для других строк заменить его явно. Не запускать старый index-based recipe вслед за этим пакетом и не включать source override после создания сессии. Новый generated catalog до A3 не объявлять готовым.

## 3. Наблюдение без writer

Статус коротким отдельным вызовом, пока finished не true либо error/stage Failed:

```csharp
return new { stand = VrBattlegrounds.EditorTools.BotCombatStandEditor.Status(),
    launch = VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status() };
```

Для bounded SDK73 snapshot во время каждой пары использовать следующий read-only вызов несколько раз: при первом brain, после появления Move и во время Back. Principal сохраняет ответы с frame/time в `pair/runtime-samples.jsonl` через отчётный transport. Sampling API не ставит Animator state, aim, root, equipment, ammo или fire.

```csharp
var rows = new System.Collections.Generic.List<object>();
var director = VrBattlegrounds.Bots.BotDirector.Instance;
if (director != null) foreach (var session in director.Bots) {
    var player = session != null ? session.ActiveAvatar : null;
    if (player == null) continue;
    var body = player.GetComponent<VrBattlegrounds.Bots.BotBody>();
    var driver = player.GetComponent<VrBattlegrounds.Bots.BotCombatDriver>();
    var gun = player.GetComponent<VrBattlegrounds.Bots.BotGunner>();
    var animator = body != null ? body.Animator : null;
    var firearm = gun != null ? gun.Firearm : null;
    var brain = driver != null ? driver.Brain : null;
    rows.Add(new { frame = UnityEngine.Time.frameCount, time = UnityEngine.Time.time,
        bodyNetId = player.netId, sourcePath = UnityEditor.AssetDatabase.GetAssetPath(player.SourcePrefab),
        sourceGuid = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(player.SourcePrefab)),
        velocity = body != null ? body.PlanarVelocity.ToString("F5") : null,
        animatorSpeed = animator != null ? (float?)animator.speed : null,
        motionRate = animator != null ? (float?)animator.GetFloat(VrBattlegrounds.Bots.BotCombatAssets.MotionRateParameter) : null,
        stateHash = animator != null ? (int?)animator.GetCurrentAnimatorStateInfo(0).shortNameHash : null,
        transition = animator != null && animator.IsInTransition(0),
        external = brain != null && brain.UsesExternalAnimatorSource,
        movementThisFrame = brain != null && brain.HasExternalMovementThisFrame,
        shots = gun != null ? gun.ShotsFired : 0,
        bindingEpoch = gun != null ? gun.BindingEpoch : 0,
        shotSequence = firearm != null ? firearm.GetReadinessState(0)?.ShotSequence : null,
        preparation = gun != null ? gun.Preparation.ToString() : null,
        capabilityError = gun != null ? gun.CapabilityError : null });
}
return rows;
```

Периодическое MCP sampling не гарантирует захват same-frame backward emission или первого rate0. Existing recorder стартует через1s и отслеживает visual cadence, поэтому отсутствие раннего frame не доказывает SDK73 старт. Не создавать новое управление ради coverage; NotObserved указать явно. Rate0 → finite displacement → Move после завершения fixed transition и отсутствие full-body Shoot при backward emission требуют совпадающих frame evidence; samples/clips/shot timeline сопоставить после release.

## 4. Stop/restore перед следующей парой и немедленный release после последней

Сначала сохранить Status/runId/reportPath; затем:

```csharp
VrBattlegrounds.EditorTools.BotCombatStandEditor.StopOwnRun();
return VrBattlegrounds.EditorTools.BotCombatStandEditor.Status();
```

Дождаться, что `UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode == false`, и завершён PlayLaunch stop. `Restore` не останавливает Play; вызывается только после завершения:

```csharp
return VrBattlegrounds.EditorTools.BotCombatStandEditor.Restore();
```

Если entered-edit callback уже сделал Restore, метод возвращает «Нет своего запуска» безопасно. Не вызывать Cancel для чужого owner/run. После prepared package и cleanup — finish/receive до анализа либо ожидания пользователя.

## Доказательства и scope результата

Для каждой пары существуют `summary.json`, `report.html`, `T01-101/inputs.json`, `visual-frames.json`, `report.html`, изображения и аналогичные `T03-101/` (конкретный PNG список перечислить по фактическим files). Summary включает cases[].WeaponTimeline, PreparationBaselines, ColdBaselineStatus, observation errors и dropped counts. Recorder inputs фиксирует actual SourcePrefab; visual frames содержит действительные clips, preparation, shot results, muzzle ray и projectile counts.

Cold — только первое `preparation-first-pre-request` по firearm/epoch/owner, фактические Initialized/M/C/Revision/ShotSequence. Unknown, missing baseline или dropped/errors остаются видимыми. Настоящий выстрел: typed ledger commit Emitted + source-emitted + рост ShotSequence и соответствующее уменьшение ammo, затем отдельно actual damage-received ActorSource/Target и LifeBefore/After. Смертельное actor-died correlation Unverified не заменяет applied damage proof. Только ShotsFired/Requests/visual effect не достаточно. Пара без damage — emission evidence, не hit/credit PASS.

Не вызывать historical equipment-runtime/human-preview/idle-motion probes: они содержат manual Bind/Animator или synthetic fixtures и не доказывают normal path. Никаких fake equip, direct SDK shoot, M/C копий, refill, новых lifecycle/тестов. Вертикальное наведение не реализовано; clip-only muzzle gate предотвращает off-barrel разрешение, но не означает полного aim coverage. Grip geometry отложена; Heavy исключён.

Статическая опора: BotCombatStandEditor Prepare:36-61, Tick:78-96, Status:64-75, Stop/Restore:106-124; BotCombatStand selection/observer-before-Begin:122-128, loop:133-139, report:242-243; BotStandSession source verification:53-60; BotStandRecorder registration:37-42, inputs:50-57, snapshots:98-110, output:151-155. Этот artifact — единственная запись в ходе подготовки; gameplay/source/tests/assets/Unity не изменялись.
