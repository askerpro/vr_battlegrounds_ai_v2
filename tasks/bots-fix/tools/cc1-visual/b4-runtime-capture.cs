// Новый G-кандидат: максимум 2 exact-frame пары. Не восстанавливает snapshot738.
const string Root = "tasks/bots-fix/reports/cc1-visual-consolidated-20261018-r1";
const string Marker = "bots-fix.cc1-visual-consolidated-20261018-r1";
if (AppDomain.CurrentDomain.GetData(Marker) != null || System.IO.File.Exists(Root + "/visual-state.json") || System.IO.File.Exists(Root + "/visual-start-intent.json"))
    return new { executionCompleted = true, refused = true, reason = "Попытка существует: только B5, не replay." };
if (!EditorApplication.isPlaying || !Mirror.NetworkServer.active) throw new InvalidOperationException("Нужен свой Play стенда.");
var warmup = VrBattlegrounds.EditorTools.BotCombatStandEditor.Status();
var receipt = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(Root + "/launch-identity.json"));
string launchRunId = (string)receipt["launchRunId"];
var launch = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
if (string.IsNullOrEmpty(launchRunId) || (string)receipt["root"] != Root || (string)receipt["owner"] != "BotCombatStand" ||
    launch.RunId != launchRunId || launch.Owner != "BotCombatStand" || (string)warmup["launchId"] != launchRunId)
    throw new InvalidOperationException("Точный собственный launch не подтверждён.");
if (!(bool)warmup["owned"] || !(bool)warmup["finished"] || Convert.ToInt32(warmup["invalidFixture"] ?? 0) > 0)
    throw new InvalidOperationException("Own warmup не завершён/невалиден.");
if (!string.Equals(System.IO.Path.GetFullPath((string)warmup["reportPath"]), System.IO.Path.GetFullPath(Root + "/warmup/summary.json"), StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Warmup report другого Root.");
if (VrBattlegrounds.Bots.BotSenses.Stage() != VrBattlegrounds.Bots.BotStage.Combat) throw new InvalidOperationException("Нужен Combat.");
if (VrBattlegrounds.Editor.HandPoseReview.HandPoseFitRuntimeCapture.Busy) throw new InvalidOperationException("Есть чужой capture.");
var manifest = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(Root + "/preflight.json"));
foreach (var input in ((Newtonsoft.Json.Linq.JObject)manifest["hashes"]).Properties()) {
    string actual;
    using (var stream = System.IO.File.OpenRead(input.Name)) using (var sha = System.Security.Cryptography.SHA256.Create())
        actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    if (actual != (string)input.Value) throw new InvalidOperationException("Source drift после B0: " + input.Name);
}
string manifestHash;
using (var sha = System.Security.Cryptography.SHA256.Create())
    manifestHash = BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(Root + "/preflight.json"))).Replace("-", "").ToLowerInvariant();
var records = new List<Dictionary<string, object>>();
var jobs = new Queue<Dictionary<string, object>>();
var seen = new HashSet<int>();
// Сохраняем фактическую пару создания до RunCases; runtime-клон не обязан иметь Editor prefab source.
var weaponSources = new Dictionary<int, Tuple<GameObject, GameObject>>();
Action<GameObject, GameObject> onInstanceCreated = (prefab, instance) => {
    if (instance != null) weaponSources[instance.GetInstanceID()] = Tuple.Create(prefab, instance);
};
var state = new Dictionary<string, object> { { "state", "starting" }, { "root", Root }, { "ownerDecision", "UNKNOWN" },
    { "sourceManifestSha256", manifestHash }, { "captures", records }, { "runId", null }, { "detached", false },
    { "launchRunId", launchRunId }, { "warmupRunId", warmup["runId"] },
    { "distancePolicy", "priority-only/not-equivalent-to-owner-reference-points" } };
VrBattlegrounds.DevTools.BotCombatStand.BotStandSession session = null;
VrBattlegrounds.DevTools.BotCombatStand.BotStandCaseResult caseResult = null;
float caseStart = 0f;
int firstBody = 0;
bool caseFinished = false;
double startedRealtime = EditorApplication.timeSinceStartup;
Action write = () => {
    state["updatedUtc"] = DateTime.UtcNow.ToString("o"); state["pendingAnalysis"] = jobs.Count;
    System.IO.File.WriteAllText(Root + "/visual-state.json", Newtonsoft.Json.JsonConvert.SerializeObject(state, Newtonsoft.Json.Formatting.Indented));
};
Func<UnityEngine.Object, object> identity = asset => {
    string path = asset != null ? AssetDatabase.GetAssetPath(asset) : null;
    return new { path, guid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path),
        dependencyHash = string.IsNullOrEmpty(path) ? null : AssetDatabase.GetAssetDependencyHash(path).ToString() };
};
Func<VrBattlegrounds.Player.PlayerController, VrBattlegrounds.Bots.BotGunner, string> fingerprint = (player, gunner) => {
    var roots = new[] { player.transform, gunner.Firearm.transform };
    var data = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Distinct().Select(t => new {
        id = t.GetInstanceID(), p = new[] { t.localPosition.x, t.localPosition.y, t.localPosition.z },
        q = new[] { t.localRotation.x, t.localRotation.y, t.localRotation.z, t.localRotation.w },
        s = new[] { t.localScale.x, t.localScale.y, t.localScale.z } }).ToArray();
    var renderers = roots.SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).Distinct().Select(r => new {
        id = r.GetInstanceID(), r.enabled, materials = r.sharedMaterials.Select(m => m != null ? m.GetInstanceID() : 0).ToArray() }).ToArray();
    using (var sha = System.Security.Cryptography.SHA256.Create())
        return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(new { data, renderers })))).Replace("-", "");
};
Action sample = () => {
  try {
    if (session == null || caseResult == null || records.Count >= 2) return;
    var player = session.Subject != null ? session.Subject.ActiveAvatar : null;
    if (player == null || !player.IsAlive || seen.Contains(player.GetInstanceID())) return;
    var avatar = player.GetComponent<UltimateXR.Avatar.UxrAvatar>();
    var gunner = player.GetComponent<VrBattlegrounds.Bots.BotGunner>();
    if (avatar == null || gunner == null || gunner.Firearm == null || gunner.ShotsFired < 1) return;
    if (!UltimateXR.Manipulation.UxrGrabManager.HasInstance) return;
    var right = avatar.GetGrabber(UltimateXR.Core.UxrHandSide.Right);
    var left = avatar.GetGrabber(UltimateXR.Core.UxrHandSide.Left);
    UltimateXR.Manipulation.UxrGrabbableObject grip; int primary; int support;
    if (!VrBattlegrounds.Bots.BotHands.TryResolveGrips(gunner.Firearm, out grip, out primary, out support) || support < 0) return;
    if (right == null || left == null || right.GrabbedObject != grip || left.GrabbedObject != grip) return;
    var manager = UltimateXR.Manipulation.UxrGrabManager.Instance;
    if (manager.GetGrabbedPoint(right) != primary || manager.GetGrabbedPoint(left) != support) return;
    seen.Add(player.GetInstanceID());
    int frame = Time.frameCount;
    string epoch = player.GetInstanceID() == firstBody ? "old" : "new";
    string folder = Root + "/pairs/" + epoch + "-frame-" + frame;
    System.IO.Directory.CreateDirectory(folder);
    Tuple<GameObject, GameObject> creation;
    bool observedCreation = weaponSources.TryGetValue(gunner.Firearm.gameObject.GetInstanceID(), out creation) &&
        object.ReferenceEquals(creation.Item2, gunner.Firearm.gameObject);
    var weaponComponent = gunner.Firearm.GetComponent<VrBattlegrounds.Arsenal.WeaponComponent>();
    var weaponInfo = weaponComponent != null ? weaponComponent.WeaponData : null;
    var driver = player.GetComponent<VrBattlegrounds.Bots.BotAvatarDriver>();
    var entry = new Dictionary<string, object> { { "epoch", epoch }, { "frame", frame }, { "gameTime", Time.time },
        { "t", Time.time - caseStart }, { "bodyId", player.GetInstanceID() }, { "bodyNetId", player.netId },
        { "sessionNetId", player.SessionNetId }, { "avatarSource", identity(player.SourcePrefab) }, { "avatarPrefabGuid", avatar.PrefabGuid },
        { "weaponId", gunner.Firearm.GetInstanceID() }, { "weaponUniqueId", gunner.Firearm.UniqueId.ToString() },
        { "weaponSource", identity(observedCreation ? creation.Item1 : null) },
        { "weaponSourceEvidence", observedCreation ? "NetworkUxrIdentity.InstanceCreated/exact-instance" : "UNKNOWN: creation not observed" },
        { "weaponSourceInstanceId", observedCreation && creation.Item1 != null ? (int?)creation.Item1.GetInstanceID() : null },
        { "weaponPrefabId", ((UltimateXR.Core.Unique.IUxrUniqueId)gunner.Firearm).UnityPrefabId },
        { "weaponCard", identity(weaponInfo) },
        { "weaponRegistrySource", identity(weaponInfo != null ? weaponInfo.WeaponPrefab : null) },
        { "weaponSourceMatchesRegistry", observedCreation && weaponInfo != null ?
            (bool?)object.ReferenceEquals(creation.Item1, weaponInfo.WeaponPrefab) : null },
        { "handPoseSelection", driver != null ? (object)new { driver.HandPoseSelectionFrame,
            driver.LeftHandPoseSelected, driver.RightHandPoseSelected } : null },
        { "grabbableId", grip.GetInstanceID() }, { "grabbableUniqueId", grip.UniqueId.ToString() },
        { "sourceManifestSha256", manifestHash }, { "hands", new List<object>() }, { "ownerDecision", "UNKNOWN" }, { "folder", folder },
        { "distancePolicy", "priority-only/not-equivalent-to-owner-reference-points" } };
    records.Add(entry);
    string before = fingerprint(player, gunner);
    entry["beforeFingerprint"] = before;
    try {
        // Синхронно в одном AvatarsUpdated: живые позы только читаются, никакого повторного pose/snap/IK.
        foreach (var grabber in new[] { right, left }) {
            string side = grabber.Side.ToString();
            var pose = avatar.GetCurrentRuntimeHandPose(grabber.Side);
            var poseAsset = pose != null ? avatar.GetHandPose(pose.PoseName) : null;
            var request = new VrBattlegrounds.Editor.HandPoseReview.HandPoseFitRequest {
                OutputDirectory = folder + "/" + side, Side = grabber.Side, SampleCount = 1000, PadSamplesPerFinger = 128,
                ImageSize = 512, RenderImages = true, IncludeOtherHand = true, CheckHandSelfIntersections = false,
                ApplyGripRules = false, ComputeCorrectionHints = false, RenderReviewSheet = false,
                ProximityMm = 2f, ContactWindowMm = 2f, StateName = epoch, SeriesName = "G-visual-candidate",
                FrameToHandBounds = true, Seed = 101 };
            var report = new VrBattlegrounds.Editor.HandPoseReview.HandPoseFitReport {
                CreatedUtc = DateTime.UtcNow.ToString("o"), UnityVersion = Application.unityVersion, Settings = request };
            var frozen = new VrBattlegrounds.Editor.HandPoseReview.HandPoseFitSnapshot(request, report, grabber, gunner.Firearm.gameObject);
            var hand = new Dictionary<string, object> { { "side", side }, { "grabPoint", manager.GetGrabbedPoint(grabber) },
                { "grabberId", grabber.GetInstanceID() }, { "runtimePose", pose != null ? pose.PoseName : null },
                { "poseAsset", identity(poseAsset) }, { "blend", avatar.GetCurrentHandPoseBlendValue(grabber.Side) },
                { "captureFrame", report.CaptureFrame }, { "sourceManifestSha256", manifestHash }, { "analysis", "pending" } };
            ((List<object>)entry["hands"]).Add(hand);
            jobs.Enqueue(new Dictionary<string, object> { { "snapshot", frozen }, { "report", report }, { "hand", hand }, { "entry", entry } });
        }
        using (var capture = new VrBattlegrounds.EditorTools.BotStandVisualCapture()) {
            var pngs = capture.Capture(player, folder, "frame-" + frame);
            entry["liveImages"] = pngs; caseResult.Images.AddRange(pngs);
        }
    } catch (Exception error) { entry["captureError"] = error.ToString(); }
    finally {
        entry["afterFrame"] = Time.frameCount;
        string after = fingerprint(player, gunner);
        entry["afterFingerprint"] = after;
        entry["captureGuardUnchanged"] = before == after && frame == Time.frameCount;
        System.IO.File.WriteAllText(folder + "/metadata.json", Newtonsoft.Json.JsonConvert.SerializeObject(entry, Newtonsoft.Json.Formatting.Indented));
        write();
    }
  } catch (Exception error) { state["sampleError"] = error.ToString(); }
};
Action<VrBattlegrounds.DevTools.BotCombatStand.BotStandCaseResult, VrBattlegrounds.DevTools.BotCombatStand.BotStandSession> onStarted = (result, current) => {
    caseResult = result; session = current; caseStart = Time.time;
    firstBody = current.Subject.ActiveAvatar.GetInstanceID(); state["caseStartFrame"] = Time.frameCount;
};
Action<VrBattlegrounds.DevTools.BotCombatStand.BotStandCaseResult> onFinished = result => {
    if (result != caseResult) return;
    caseFinished = true; session = null;
    result.Metrics["visualPairEvidence"] = new { captures = records, sourceManifestSha256 = manifestHash, ownerDecision = "UNKNOWN" };
};
EditorApplication.CallbackFunction tick = null;
Action detach = null;
AssemblyReloadEvents.AssemblyReloadCallback onReload = null;
Action<PlayModeStateChange> onPlay = null;
detach = () => {
    VrBattlegrounds.Network.NetworkUxrIdentity.InstanceCreated -= onInstanceCreated;
    weaponSources.Clear();
    UltimateXR.Core.UxrManager.AvatarsUpdated -= sample;
    VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseStarted -= onStarted;
    VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseFinished -= onFinished;
    EditorApplication.update -= tick; AssemblyReloadEvents.beforeAssemblyReload -= onReload;
    EditorApplication.playModeStateChanged -= onPlay;
    while (jobs.Count > 0) {
        var job = jobs.Dequeue(); ((Dictionary<string, object>)job["hand"])["analysis"] = "aborted-before-analysis";
        ((IDisposable)job["snapshot"]).Dispose();
    }
    state["detached"] = true; state["pendingAnalysis"] = 0;
};
onReload = () => { state["state"] = "aborted-reload"; detach(); write(); };
onPlay = mode => { if (mode == PlayModeStateChange.ExitingPlayMode) { state["state"] = "aborted-play-exit"; detach(); write(); } };
tick = () => {
    try {
        if (jobs.Count > 0) {
            var job = jobs.Dequeue();
            var frozen = (VrBattlegrounds.Editor.HandPoseReview.HandPoseFitSnapshot)job["snapshot"];
            var report = (VrBattlegrounds.Editor.HandPoseReview.HandPoseFitReport)job["report"];
            var hand = (Dictionary<string, object>)job["hand"];
            var entry = (Dictionary<string, object>)job["entry"];
            try {
                VrBattlegrounds.Editor.HandPoseReview.HandPoseFitAnalyzer.AnalyzeCaptured(frozen, report);
                hand["analysis"] = report.Status; hand["report"] = report.JsonPath; hand["error"] = report.Error;
                string review = report.Settings.OutputDirectory + "/zone-review.json";
                System.IO.File.WriteAllText(review, Newtonsoft.Json.JsonConvert.SerializeObject(new {
                    schema = "cc1-visual-zone-review@1", owner = "grip8388", ownerDecision = "UNKNOWN", priorityAbsSignedMm = 2,
                    distancePolicy = "priority-only/not-equivalent-to-owner-reference-points",
                    exactFrame = report.CaptureFrame, hand, pair = entry, sourceManifestSha256 = manifestHash,
                    report.CaptureSource, report.Avatar, report.Object, report.Pose,
                    report.Zones, report.Segments, report.ContactMetrics, report.Reliability, report.Limitations,
                    note = "±2мм — лишь review priority. Fixed-reference phalanges владельца здесь не восстановлены." },
                    VrBattlegrounds.Editor.HandPoseReview.FitUnityJsonConverter.Settings));
                hand["zoneReview"] = review;
            } finally { frozen.Dispose(); }
            System.IO.File.WriteAllText((string)entry["folder"] + "/metadata.json", Newtonsoft.Json.JsonConvert.SerializeObject(entry, Newtonsoft.Json.Formatting.Indented));
            write();
        }
        string runId = state["runId"] as string;
        var status = string.IsNullOrEmpty(runId) ? null : VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.GetStatus(runId);
        if (status != null && status.IsFinished && caseFinished && jobs.Count == 0) {
            state["state"] = "observed"; state["captureCount"] = records.Count; state["standReport"] = status.ReportPath;
            detach(); write(); return;
        }
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - startedRealtime > 180) {
            if (status != null && !status.IsFinished) {
                string cancelPath = Root + "/stand-cancel-intent.json";
                if (!System.IO.File.Exists(cancelPath)) {
                    var cancel = System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(new {
                        runId, launchRunId, phase = "G", reason = "visual watchdog", utc = DateTime.UtcNow.ToString("o") }));
                    using (var file = new System.IO.FileStream(cancelPath, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.Read)) {
                        file.Write(cancel, 0, cancel.Length); file.Flush(true);
                    }
                    VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.Cancel(runId);
                }
            }
            state["state"] = "aborted-timeout-or-play"; detach(); write();
        }
    } catch (Exception error) { state["state"] = "observer-error"; state["error"] = error.ToString(); detach(); write(); }
};
var startIntent = System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(new { root = Root, launchRunId, manifestHash, utc = DateTime.UtcNow.ToString("o") }));
using (var file = new System.IO.FileStream(Root + "/visual-start-intent.json", System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.Read)) {
    file.Write(startIntent, 0, startIntent.Length); file.Flush(true);
}
AppDomain.CurrentDomain.SetData(Marker, state);
Action cleanup = () => {
    if ((string)state["state"] != "observed") state["state"] = "aborted-cleanup";
    detach(); write();
};
AppDomain.CurrentDomain.SetData(Marker + ".detach", cleanup);
VrBattlegrounds.Network.NetworkUxrIdentity.InstanceCreated += onInstanceCreated;
UltimateXR.Core.UxrManager.AvatarsUpdated += sample;
VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseStarted += onStarted;
VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseFinished += onFinished;
EditorApplication.update += tick; AssemblyReloadEvents.beforeAssemblyReload += onReload; EditorApplication.playModeStateChanged += onPlay;
state["state"] = "running"; write();
try {
    var options = new VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions {
        OutputDirectory = Root + "/runtime/G-ghost-respawn-ak105", Profile = VrBattlegrounds.DevTools.BotCombatStand.BotStandProfile.Production,
        Seeds = new[] { 101 }, CaseSeconds = 40f, Capture = true, Rifle = true, HandFit = false, WeaponName = "AK105_Weapon", AvatarIndex = -1,
        ZeroAiAimSpread = false, NoRecoil = false, ObserveShotTail = false, KillAtSeconds = 8f, RespawnAtSeconds = 20f,
        Cases = new[] { VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T07 } };
    var handle = VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.RunCases(options, options.Cases);
    state["runId"] = handle.RunId; write();
} catch (Exception error) { state["state"] = "start-error"; state["error"] = error.ToString(); detach(); write(); throw; }
return new { executionCompleted = true, queued = true, ownerDecision = "UNKNOWN", output = Root, maxPairs = 2,
    next = "B5 до observed+detached и stand finished; затем B6. При таймауте inspect, не replay." };
