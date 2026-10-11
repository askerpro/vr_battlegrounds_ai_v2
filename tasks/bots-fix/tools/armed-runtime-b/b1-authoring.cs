// B1 MUTATION (Edit mode): пересборка NPC ClipBody с StampAssetIdentity + saved readback.
// НЕ ПОВТОРЯТЬ после таймаута: сначала проверить durable state (result.json/composition.json под Root,
// mtime трёх ClipBody). При существующем result.json/composition.json payload сам отказывает и пишет blocked-*.json.
// Основа — tasks/bots-fix/tools/armed-authoring-package.cs (ticket402), изменены только Root и identity-a.json.
const string Root = "tasks/bots-fix/reports/armed-runtime-b";
var resultPath = Root + "/result.json";
var stage = "preflight";
var errors = new List<string>();
var unknown = new List<string>();
var mismatches = new List<string>();
var variants = new List<object>();
var controllers = new List<object>();
var motions = new List<object>();
var definitions = new List<object>();
var identity = new List<Dictionary<string, object>>();
Dictionary<string, object> composition = null, calibration = null;
string setup = null;
bool authoringCompleted = false;
bool? calibrationPassed = null, calibrationBuilt = null;
int previewBefore = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
string activeBefore = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
int previewAfter = previewBefore;
string activeAfter = activeBefore;
int? modelCount = null, socketCount = null, motionCount = null;
var bodies = new[] {
    "Assets/Resources/Bots/ClipBodies/Optimized_MEF_Player_Black_ClipBody.prefab",
    "Assets/Resources/Bots/ClipBodies/Optimized_MEF_Player_ClipBody.prefab",
    "Assets/Resources/Bots/ClipBodies/PlayerControllersCyborgAvatar_ClipBody.prefab" };
Func<UnityEngine.Object, string> assetPath = value => value == null ? null : AssetDatabase.GetAssetPath(value);
Func<UnityEngine.Object, string> assetGuid = value => value == null ? null : AssetDatabase.AssetPathToGUID(assetPath(value));
Func<float, bool> finite = value => !float.IsNaN(value) && !float.IsInfinity(value);
Func<byte[], string> sha = bytes => {
    using (var h = System.Security.Cryptography.SHA256.Create())
        return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
};
Func<Mirror.NetworkIdentity, uint?> savedAssetId = id => {
    if (id == null) return null;
    // Public getter может лениво назначить assetId. Читаем сохранённое поле без такого побочного действия.
    var property = new SerializedObject(id).FindProperty("_assetId");
    if (property == null) { unknown.Add("NetworkIdentity serialized _assetId unavailable."); return null; }
    return (uint)property.longValue;
};
// --- Общий read-only снимок идентичности (тот же код в B2/B7) ---
Func<string, Dictionary<string, object>> identitySnapshot = path => {
    var row = new Dictionary<string, object> { { "path", path } };
    if (!System.IO.File.Exists(path)) { row["exists"] = false; return row; }
    var bytes = System.IO.File.ReadAllBytes(path);
    row["exists"] = true; row["sha256"] = sha(bytes); row["bytes"] = bytes.Length;
    var text = System.Text.Encoding.UTF8.GetString(bytes);
    row["crlf"] = text.Contains("\r\n");
    var lines = text.Replace("\r\n", "\n").Split('\n');
    var uxrIds = new List<string>();
    var assetIdValues = new List<string>();
    int prefabGuidOverrides = 0, isInPrefabOverrides = 0;
    string header = null, target = null;
    for (int i = 0; i < lines.Length; i++) {
        string line = lines[i], trimmed = lines[i].Trim();
        if (line.StartsWith("--- ")) header = trimmed;
        if (trimmed.StartsWith("- target:")) target = trimmed;
        if (trimmed == "propertyPath: __prefabGuid") prefabGuidOverrides++;
        if (trimmed == "propertyPath: __isInPrefab") isInPrefabOverrides++;
        string next = i + 1 < lines.Length ? lines[i + 1].Trim() : "";
        if (trimmed == "propertyPath: _assetId") assetIdValues.Add(target + " | " + next);
        else if (trimmed.StartsWith("_assetId:")) assetIdValues.Add(header + " | " + trimmed);
        if (!line.Contains("_uxrUniqueId")) continue;
        uxrIds.Add(trimmed.StartsWith("propertyPath:") ? target + " | " + trimmed + " | " + next : header + " | " + trimmed);
    }
    row["uxrUniqueIdLineCount"] = uxrIds.Count;
    row["uxrUniqueIdLinesSha256"] = sha(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", uxrIds.ToArray())));
    row["uxrUniqueIdLines"] = uxrIds;
    row["prefabGuidOverrides"] = prefabGuidOverrides; row["isInPrefabOverrides"] = isInPrefabOverrides;
    row["assetIdFileValues"] = assetIdValues;
    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    if (go == null) { row["loaded"] = false; return row; }
    row["loaded"] = true;
    var onDisk = VrBattlegrounds.EditorTools.VersionControl.UxrUniqueIdPersister.ReadIdsOnDisk(path);
    var loadedIds = new List<string>();
    int flagMismatches = 0, unpersisted = 0, emptyIds = 0, dirty = EditorUtility.IsDirty(go) ? 1 : 0;
    foreach (var component in go.GetComponentsInChildren<UltimateXR.Core.Components.UxrComponent>(true)) {
        var id = component.UniqueId;
        loadedIds.Add(AnimationUtility.CalculateTransformPath(component.transform, go.transform) + ":" + component.GetType().Name + "=" + id.ToString());
        bool inPrefab; string prefabGuid;
        if (VrBattlegrounds.EditorTools.VersionControl.UxrUniqueIdPersister.HasMismatch(component, out inPrefab, out prefabGuid)) flagMismatches++;
        if (id == Guid.Empty) emptyIds++; else if (!onDisk.Contains(id.ToString())) unpersisted++;
        if (EditorUtility.IsDirty(component)) dirty++;
    }
    loadedIds.Sort(StringComparer.Ordinal);
    var networkIdentity = go.GetComponent<Mirror.NetworkIdentity>();
    uint? serializedAssetId = null;
    if (networkIdentity != null) {
        var property = new SerializedObject(networkIdentity).FindProperty("_assetId");
        if (property != null) serializedAssetId = property.uintValue;
        if (EditorUtility.IsDirty(networkIdentity)) dirty++;
    }
    uint expectedAssetId = Mirror.NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(path)));
    row["loadedUxrComponents"] = loadedIds.Count;
    row["loadedIdsSha256"] = sha(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", loadedIds.ToArray())));
    row["uxrFlagMismatches"] = flagMismatches; row["unpersistedIds"] = unpersisted; row["emptyIds"] = emptyIds;
    row["dirtyObjects"] = dirty;
    row["serializedAssetId"] = serializedAssetId; row["expectedAssetId"] = expectedAssetId;
    row["assetIdCanonical"] = serializedAssetId.HasValue && serializedAssetId.Value == expectedAssetId;
    return row;
};
try {
    if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        throw new InvalidOperationException("Authoring требует idle EditMode.");
    if (System.IO.File.Exists(resultPath) || System.IO.File.Exists(Root + "/composition.json")) {
        // Сохраняем только новый отказ, не затирая прежние durable result/composition/calibration.
        resultPath = Root + "/blocked-authoring-" + DateTime.UtcNow.Ticks + ".json";
        throw new InvalidOperationException("A уже выполнялся в этом output root; payload не повторять.");
    }
    System.IO.Directory.CreateDirectory(Root);
    stage = "setup";
    setup = VrBattlegrounds.EditorTools.BotCombatSetup.Build();
    stage = "composition";
    composition = VrBattlegrounds.EditorTools.BotClipComposition.Build(Root);
    if (!composition.ContainsKey("passed") || !(bool)composition["passed"])
        mismatches.Add("Composition not passed; calibration/readback not executed.");
    else {
        stage = "calibration";
        calibration = VrBattlegrounds.EditorTools.BotClipComposition.BuildCalibration(Root);
        if (calibration.ContainsKey("passed")) calibrationPassed = Convert.ToBoolean(calibration["passed"]);
        if (calibration.ContainsKey("candidateBuilt")) calibrationBuilt = Convert.ToBoolean(calibration["candidateBuilt"]);
        authoringCompleted = true;
        stage = "saved-readback";
        var library = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Bots.BotCombatAssets>("Assets/Resources/Bots/BotCombatAssets.asset");
        if (library == null) throw new InvalidOperationException("Saved BotCombatAssets отсутствует.");
        modelCount = library.clipBodies == null ? 0 : library.clipBodies.Length;
        socketCount = library.sockets == null ? 0 : library.sockets.Length;
        motionCount = library.motions == null ? 0 : library.motions.Length;
        if (modelCount != 3 || socketCount != 6 || motionCount != 27)
            mismatches.Add("Expected models3/sockets6/motions27; actual " + modelCount + "/" + socketCount + "/" + motionCount);
        var expectedSources = new Dictionary<string, string>(StringComparer.Ordinal) {
            { "310c90ae0d962f344a1eddbe48a429c9", "Assets/Prefabs/Player/Optimized_MEF_Player_Black.prefab" },
            { "b8146035f3f436a4799882b3a0d1dd45", "Assets/Prefabs/Player/Optimized_MEF_Player.prefab" },
            { "8e0e06323e4754f479a9f19788554652", "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab" }
        };
        var sourceGuids = new HashSet<string>(StringComparer.Ordinal);
        var modelSources = new HashSet<GameObject>();
        var generatedAssetIds = new HashSet<uint>();
        foreach (var model in library.clipBodies ?? new VrBattlegrounds.Bots.BotCombatAssets.ClipBodyBinding[0]) {
            if (model == null || model.source == null || model.prefab == null) { mismatches.Add("Incomplete clip body binding."); continue; }
            if (!modelSources.Add(model.source)) mismatches.Add("Duplicate source: " + assetPath(model.source));
            string actualSourceGuid = assetGuid(model.source);
            string expectedSourcePath;
            if (string.IsNullOrEmpty(actualSourceGuid) || !sourceGuids.Add(actualSourceGuid) ||
                !expectedSources.TryGetValue(actualSourceGuid, out expectedSourcePath) || expectedSourcePath != assetPath(model.source))
                mismatches.Add("Unexpected/duplicate exact source mapping: " + actualSourceGuid + "/" + assetPath(model.source));
            if (model.sourceGuid != actualSourceGuid)
                mismatches.Add("Declared sourceGuid differs from saved source identity: " + assetPath(model.source));
            var bodyPath = assetPath(model.prefab);
            if (!bodies.Contains(bodyPath)) mismatches.Add("Unexpected generated body path: " + bodyPath);
            var sourceIdentity = model.source.GetComponent<Mirror.NetworkIdentity>();
            var sourceId = savedAssetId(sourceIdentity);
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(bodyPath);
            var savedIdentity = saved == null ? null : saved.GetComponent<Mirror.NetworkIdentity>();
            var identityId = savedAssetId(savedIdentity);
            if (identityId.HasValue && !generatedAssetIds.Add(identityId.Value)) mismatches.Add("Generated variants share assetId: " + identityId.Value);
            bool? separateAsset = identityId.HasValue && sourceId.HasValue ? (bool?)(identityId.Value != 0 && identityId.Value != sourceId.Value) : null;
            int savedIdentities = saved == null ? 0 : saved.GetComponentsInChildren<Mirror.NetworkIdentity>(true).Length;
            var savedClip = saved == null ? null : saved.GetComponent<VrBattlegrounds.Bots.BotClipBody>();
            int rootBehaviours = saved == null ? 0 : saved.GetComponents<Mirror.NetworkBehaviour>().Length;
            bool networkClip = savedClip != null;
            if (savedIdentity == null || savedIdentity.sceneId != 0 || savedIdentities != 1 || !networkClip || separateAsset == false)
                mismatches.Add("Saved identity/clip metadata mismatch: " + bodyPath);
            if (!separateAsset.HasValue) unknown.Add("Separate saved assetId comparison unknown: " + bodyPath);
            GameObject reopened = null;
            try {
                // Isolation scene выгружается без SaveAsPrefabAsset: проверяются сохранённые generated данные.
                reopened = PrefabUtility.LoadPrefabContents(bodyPath);
                var reopenedIdentity = reopened.GetComponent<Mirror.NetworkIdentity>();
                var clip = reopened.GetComponent<VrBattlegrounds.Bots.BotClipBody>();
                var human = reopened.GetComponentsInChildren<Animator>(true).Where(a => a.enabled && a.avatar != null && a.avatar.isHuman).ToArray();
                int sdkControllers = reopened.GetComponentsInChildren<UltimateXR.Avatar.Controllers.UxrStandardAvatarController>(true).Count(c => c.enabled);
                int ikSolvers = reopened.GetComponentsInChildren<UltimateXR.Animation.IK.UxrIKSolver>(true).Count(c => c.enabled);
                int tracking = reopened.GetComponentsInChildren<UltimateXR.Devices.UxrTrackingDevice>(true).Count(c => c.enabled);
                int input = reopened.GetComponentsInChildren<UltimateXR.Devices.UxrControllerInput>(true).Count(c => c.enabled);
                int grabbers = reopened.GetComponentsInChildren<UltimateXR.Manipulation.UxrGrabber>(true).Count(c => c.enabled);
                var transport = reopened.GetComponentsInChildren<Mirror.NetworkTransformBase>(true).Select(t => new {
                    name = t.name, enabled = t.enabled, direction = t.syncDirection.ToString(),
                    rootTarget = (t.target != null ? t.target : t.transform) == reopened.transform
                }).ToArray();
                if (reopenedIdentity == null || clip == null || human.Length != 1 || sdkControllers != 0 || ikSolvers != 0 || tracking != 0 || input != 0 || grabbers != 0 ||
                    (human.Length == 1 && human[0].applyRootMotion) ||
                    transport.Any(t => t.enabled && (!t.rootTarget || t.direction != Mirror.SyncDirection.ServerToClient.ToString())))
                    mismatches.Add("Reopened pose/root writer metadata mismatch: " + bodyPath);
                variants.Add(new {
                    sourcePath = assetPath(model.source), sourceGuid = actualSourceGuid, declaredSourceGuid = model.sourceGuid, sourceAssetId = sourceId,
                    generatedPath = bodyPath, generatedGuid = assetGuid(saved), savedAssetId = identityId, separateAsset,
                    savedSceneId = savedIdentity == null ? (ulong?)null : savedIdentity.sceneId, savedIdentities,
                    savedRootNetworkBehaviours = rootBehaviours, savedNetworkClip = networkClip,
                    reopenedRootNetworkBehaviours = reopened.GetComponents<Mirror.NetworkBehaviour>().Select(b => b.GetType().FullName).ToArray(),
                    enabledHumanAnimators = human.Length, enabledSdkControllers = sdkControllers, enabledIkSolvers = ikSolvers,
                    enabledTracking = tracking, enabledInput = input, enabledGrabbers = grabbers,
                    rootMotion = human.Length == 1 ? (bool?)human[0].applyRootMotion : null, transport
                });
            } finally { if (reopened != null) PrefabUtility.UnloadPrefabContents(reopened); }
        }
        if (!sourceGuids.SetEquals(expectedSources.Keys)) mismatches.Add("Exact approved source GUID set is incomplete.");
        var expectedControllers = new[] { library.pistol, library.rifle, library.unarmed };
        for (int i = 0; i < expectedControllers.Length; i++) {
            var controller = expectedControllers[i] as UnityEditor.Animations.AnimatorController;
            var expectedStates = i == 2 ? new[] { "Idle", "Move" } : new[] { "Idle", "Move", "Back", "Left", "Right", "Shoot" };
            if (controller == null || controller.layers.Length != 1) { mismatches.Add("Expected native single-layer controller " + i); continue; }
            var states = controller.layers[0].stateMachine.states;
            var rate = controller.parameters.FirstOrDefault(p => p.name == VrBattlegrounds.Bots.BotCombatAssets.MotionRateParameter);
            if (controller.layers[0].iKPass || states.Length != expectedStates.Length || expectedStates.Any(s => states.Count(c => c.state.name == s) != 1) ||
                rate == null || rate.type != AnimatorControllerParameterType.Float)
                mismatches.Add("Controller required states/rate/IK metadata mismatch: " + assetPath(controller));
            foreach (var child in states) {
                bool moving = child.state.name != "Idle" && child.state.name != "Shoot";
                if (child.state.motion == null || child.state.iKOnFeet || child.state.speedParameterActive != moving ||
                    (moving && child.state.speedParameter != VrBattlegrounds.Bots.BotCombatAssets.MotionRateParameter))
                    mismatches.Add("State motion/rate metadata mismatch: " + controller.name + "/" + child.state.name);
            }
            // states[].motion используется offline-анализом B как карта clip -> state.
            controllers.Add(new { path = assetPath(controller), guid = assetGuid(controller), ikPass = controller.layers[0].iKPass,
                states = states.Select(c => new { name = c.state.name, motion = assetPath(c.state.motion), motionName = c.state.motion != null ? c.state.motion.name : null,
                    c.state.speedParameterActive, c.state.speedParameter, c.state.iKOnFeet }).ToArray() });
        }
        var motionKeys = new HashSet<string>();
        foreach (var motion in library.motions ?? new VrBattlegrounds.Bots.BotCombatAssets.ClipMotionBinding[0]) {
            if (motion == null) { mismatches.Add("Null motion binding."); continue; }
            string key = assetGuid(motion.source) + "/" + assetGuid(motion.controller) + "/" + motion.state;
            if (!motionKeys.Add(key) || !finite(motion.metersPerSecond) || motion.metersPerSecond <= .01f)
                mismatches.Add("Duplicate/nonpositive/nonfinite native motion: " + key);
            motions.Add(new { source = assetPath(motion.source), controller = assetPath(motion.controller), motion.state, motion.metersPerSecond });
        }
        foreach (var model in library.clipBodies ?? new VrBattlegrounds.Bots.BotCombatAssets.ClipBodyBinding[0]) {
            if (model == null || model.source == null) continue;
            for (int i = 0; i < expectedControllers.Length; i++) foreach (var state in i == 2 ? new[] { "Move" } : new[] { "Move", "Back", "Left", "Right" }) {
                float speed = library.GetMotionSpeed(model.source, expectedControllers[i], state);
                if (!finite(speed) || speed <= .01f) mismatches.Add("Required native motion missing: " + assetPath(model.source) + "/" + i + "/" + state);
            }
        }
        stage = "canonical-definition-readback";
        foreach (var id in new[] { "PPK", "AK105" }) {
            var path = "Assets/Data/Weapons/" + id + "_Weapon.asset";
            var info = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Arsenal.WeaponInfo>(path);
            string reason;
            bool admitted = VrBattlegrounds.Weapons.WeaponAutomationCapability.ValidateDefinition(info, out reason);
            var prefab = info == null ? null : info.WeaponPrefab;
            var binding = prefab == null ? null : prefab.GetComponent<VrBattlegrounds.Weapons.WeaponEquipmentBinding>();
            var readiness = prefab == null ? null : prefab.GetComponent<VrBattlegrounds.Weapons.WeaponReadinessController>();
            if (!admitted || binding == null || readiness == null) mismatches.Add("Canonical automation definition incomplete: " + id + "/" + reason);
            definitions.Add(new { weapon = id, path, guid = assetGuid(info), prefabPath = assetPath(prefab), prefabGuid = assetGuid(prefab),
                capabilityValid = admitted, reason, hasEquipmentBinding = binding != null,
                readinessOwnerType = readiness == null ? null : readiness.GetType().FullName });
        }
        stage = "identity-snapshot";
        // Только чтение: базовая линия для B2/B7. Не входит в mismatches A; гейт идентичности — B2.
        foreach (var body in bodies) identity.Add(identitySnapshot(body));
        stage = "completed";
    }
} catch (Exception error) { errors.Add(stage + ": " + error); }
finally {
    previewAfter = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
    activeAfter = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
    if (previewAfter != previewBefore || activeAfter != activeBefore)
        mismatches.Add("Cleanup changed preview count or active scene baseline.");
    System.IO.Directory.CreateDirectory(Root);
    if (identity.Count > 0)
        System.IO.File.WriteAllText(Root + "/identity-a.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
            phase = "after-authoring", utc = DateTime.UtcNow.ToString("o"), rows = identity }, Newtonsoft.Json.Formatting.Indented));
    System.IO.File.WriteAllText(resultPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
        executionCompleted = true, passed = authoringCompleted && errors.Count == 0 && mismatches.Count == 0 && unknown.Count == 0,
        stage, setup, composition, calibration, authoringCompleted, calibrationPassed, calibrationBuilt,
        modelCount, socketCount, motionCount, variants, controllers, motions, definitions, mismatches, unknown, errors,
        identityBaseline = identity.Count > 0 ? Root + "/identity-a.json" : null,
        cleanup = new { previewBefore, previewAfter, activeBefore, activeAfter,
            isPlaying = EditorApplication.isPlayingOrWillChangePlaymode }, gameplayAccepted = false,
        scope = "Native authoring and read-only saved prefab/definition metadata; no runtime/remote/Quest/gameplay acceptance."
    }, Newtonsoft.Json.Formatting.Indented));
}
var identitySummary = identity.Select(r => new {
    path = System.IO.Path.GetFileName((string)r["path"]),
    flagMismatches = r.ContainsKey("uxrFlagMismatches") ? r["uxrFlagMismatches"] : null,
    unpersisted = r.ContainsKey("unpersistedIds") ? r["unpersistedIds"] : null,
    assetIdCanonical = r.ContainsKey("assetIdCanonical") ? r["assetIdCanonical"] : null,
    dirty = r.ContainsKey("dirtyObjects") ? r["dirtyObjects"] : null }).ToArray();
return new { executionCompleted = true, reportPath = resultPath,
    passed = authoringCompleted && errors.Count == 0 && mismatches.Count == 0 && unknown.Count == 0,
    authoringCompleted, modelCount, socketCount, motionCount, calibrationBuilt,
    readbackVariants = variants.Count, definitions = definitions.Count, mismatches = mismatches.Count,
    unknown = unknown.Count, errors = errors.Count, firstError = errors.Count > 0 ? errors[0].Substring(0, Math.Min(400, errors[0].Length)) : null,
    identity = identitySummary, previewBefore, previewAfter, gameplayAccepted = false };
