// Один prepared authoring-пакет; readback не меняет source prefab, SDK или игровую логику.
var folder = "tasks/bots-fix/reports/armed-authoring-20261010-models3";
var resultPath = System.IO.Path.Combine(folder, "result.json");
var stage = "preflight";
var errors = new System.Collections.Generic.List<string>();
var unknown = new System.Collections.Generic.List<string>();
var mismatches = new System.Collections.Generic.List<string>();
var variants = new System.Collections.Generic.List<object>();
var controllers = new System.Collections.Generic.List<object>();
var motions = new System.Collections.Generic.List<object>();
var definitions = new System.Collections.Generic.List<object>();
System.Collections.Generic.Dictionary<string, object> composition = null, calibration = null;
string setup = null;
bool authoringCompleted = false;
bool? calibrationPassed = null, calibrationBuilt = null;
int previewBefore = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
string activeBefore = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
int previewAfter = previewBefore;
string activeAfter = activeBefore;
int? modelCount = null, socketCount = null, motionCount = null;
System.Func<UnityEngine.Object, string> assetPath = value => value == null ? null : UnityEditor.AssetDatabase.GetAssetPath(value);
System.Func<UnityEngine.Object, string> assetGuid = value => value == null ? null : UnityEditor.AssetDatabase.AssetPathToGUID(assetPath(value));
System.Func<float, bool> finite = value => !float.IsNaN(value) && !float.IsInfinity(value);
System.Func<Mirror.NetworkIdentity, uint?> savedAssetId = identity => {
    if (identity == null) return null;
    // Public getter может лениво назначить assetId. Читаем сохранённое поле без такого побочного действия.
    var property = new UnityEditor.SerializedObject(identity).FindProperty("_assetId");
    if (property == null) { unknown.Add("NetworkIdentity serialized _assetId unavailable."); return null; }
    return (uint)property.longValue;
};
try {
    if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating)
        throw new System.InvalidOperationException("Authoring требует idle EditMode.");
    // Уникальный каталог не смешивается с прежним результатом или незавершённым повтором.
    if (System.IO.Directory.Exists(folder)) {
        // Сохраняем только новый отказ, не затирая прежние durable result/composition/calibration.
        resultPath = System.IO.Path.Combine(folder, "blocked-" + System.DateTime.UtcNow.Ticks + ".json");
        throw new System.InvalidOperationException("Report folder уже существует; payload не повторять: " + folder);
    }
    System.IO.Directory.CreateDirectory(folder);
    stage = "setup";
    setup = VrBattlegrounds.EditorTools.BotCombatSetup.Build();
    stage = "composition";
    composition = VrBattlegrounds.EditorTools.BotClipComposition.Build(folder);
    if (!composition.ContainsKey("passed") || !(bool)composition["passed"])
        mismatches.Add("Composition not passed; calibration/readback not executed.");
    else {
        stage = "calibration";
        calibration = VrBattlegrounds.EditorTools.BotClipComposition.BuildCalibration(folder);
        if (calibration.ContainsKey("passed")) calibrationPassed = System.Convert.ToBoolean(calibration["passed"]);
        if (calibration.ContainsKey("candidateBuilt")) calibrationBuilt = System.Convert.ToBoolean(calibration["candidateBuilt"]);
        authoringCompleted = true;
        stage = "saved-readback";
        var library = UnityEditor.AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Bots.BotCombatAssets>("Assets/Resources/Bots/BotCombatAssets.asset");
        if (library == null) throw new System.InvalidOperationException("Saved BotCombatAssets отсутствует.");
        modelCount = library.clipBodies == null ? 0 : library.clipBodies.Length;
        socketCount = library.sockets == null ? 0 : library.sockets.Length;
        motionCount = library.motions == null ? 0 : library.motions.Length;
        if (modelCount != 3 || socketCount != 6 || motionCount != 27)
            mismatches.Add("Expected models3/sockets6/motions27; actual " + modelCount + "/" + socketCount + "/" + motionCount);
        var expectedSources = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal) {
            { "310c90ae0d962f344a1eddbe48a429c9", "Assets/Prefabs/Player/Optimized_MEF_Player_Black.prefab" },
            { "b8146035f3f436a4799882b3a0d1dd45", "Assets/Prefabs/Player/Optimized_MEF_Player.prefab" },
            { "8e0e06323e4754f479a9f19788554652", "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab" }
        };
        var sourceGuids = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        var modelSources = new System.Collections.Generic.HashSet<UnityEngine.GameObject>();
        var generatedAssetIds = new System.Collections.Generic.HashSet<uint>();
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
            var sourceIdentity = model.source.GetComponent<Mirror.NetworkIdentity>();
            var sourceId = savedAssetId(sourceIdentity);
            var saved = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(bodyPath);
            var savedIdentity = saved == null ? null : saved.GetComponent<Mirror.NetworkIdentity>();
            var identityId = savedAssetId(savedIdentity);
            if (identityId.HasValue && !generatedAssetIds.Add(identityId.Value)) mismatches.Add("Generated variants share assetId: " + identityId.Value);
            bool? separateAsset = identityId.HasValue && sourceId.HasValue ? (bool?)(identityId.Value != 0 && identityId.Value != sourceId.Value) : null;
            int savedIdentities = saved == null ? 0 : saved.GetComponentsInChildren<Mirror.NetworkIdentity>(true).Length;
            var savedClip = saved == null ? null : saved.GetComponent<VrBattlegrounds.Bots.BotClipBody>();
            int rootBehaviours = saved == null ? 0 : saved.GetComponents<Mirror.NetworkBehaviour>().Length;
            bool networkClip = savedClip != null && savedClip is Mirror.NetworkBehaviour;
            if (savedIdentity == null || savedIdentity.sceneId != 0 || savedIdentities != 1 || !networkClip || separateAsset == false)
                mismatches.Add("Saved identity/clip metadata mismatch: " + bodyPath);
            if (!separateAsset.HasValue) unknown.Add("Separate saved assetId comparison unknown: " + bodyPath);
            UnityEngine.GameObject reopened = null;
            try {
                // Isolation scene выгружается без SaveAsPrefabAsset: проверяются сохранённые generated данные.
                reopened = UnityEditor.PrefabUtility.LoadPrefabContents(bodyPath);
                var identity = reopened.GetComponent<Mirror.NetworkIdentity>();
                var clip = reopened.GetComponent<VrBattlegrounds.Bots.BotClipBody>();
                var human = reopened.GetComponentsInChildren<UnityEngine.Animator>(true).Where(a => a.enabled && a.avatar != null && a.avatar.isHuman).ToArray();
                int sdkControllers = reopened.GetComponentsInChildren<UltimateXR.Avatar.Controllers.UxrStandardAvatarController>(true).Count(c => c.enabled);
                int ikSolvers = reopened.GetComponentsInChildren<UltimateXR.Animation.IK.UxrIKSolver>(true).Count(c => c.enabled);
                int tracking = reopened.GetComponentsInChildren<UltimateXR.Devices.UxrTrackingDevice>(true).Count(c => c.enabled);
                int input = reopened.GetComponentsInChildren<UltimateXR.Devices.UxrControllerInput>(true).Count(c => c.enabled);
                int grabbers = reopened.GetComponentsInChildren<UltimateXR.Manipulation.UxrGrabber>(true).Count(c => c.enabled);
                var transport = reopened.GetComponentsInChildren<Mirror.NetworkTransformBase>(true).Select(t => new {
                    name = t.name, enabled = t.enabled, direction = t.syncDirection.ToString(),
                    rootTarget = (t.target != null ? t.target : t.transform) == reopened.transform
                }).ToArray();
                if (identity == null || clip == null || human.Length != 1 || sdkControllers != 0 || ikSolvers != 0 || tracking != 0 || input != 0 || grabbers != 0 ||
                    (human.Length == 1 && human[0].applyRootMotion) ||
                    transport.Any(t => t.enabled && (!t.rootTarget || t.direction != Mirror.SyncDirection.ServerToClient.ToString())))
                    mismatches.Add("Reopened pose/root writer metadata mismatch: " + bodyPath);
                var layout = identity == null ? null : identity.NetworkBehaviours;
                variants.Add(new {
                    sourcePath = assetPath(model.source), sourceGuid = actualSourceGuid, declaredSourceGuid = model.sourceGuid, sourceAssetId = sourceId,
                    generatedPath = bodyPath, generatedGuid = assetGuid(saved), savedAssetId = identityId, separateAsset,
                    savedSceneId = savedIdentity == null ? (ulong?)null : savedIdentity.sceneId, savedIdentities,
                    savedRootNetworkBehaviours = rootBehaviours, savedNetworkClip = networkClip,
                    reopenedSceneId = identity == null ? (ulong?)null : identity.sceneId,
                    reopenedRootNetworkBehaviours = reopened.GetComponents<Mirror.NetworkBehaviour>().Select(b => b.GetType().FullName).ToArray(),
                    initializedMirrorLayout = layout == null ? null : layout.Select(b => b == null ? null : b.GetType().FullName).ToArray(),
                    mirrorLayoutScope = "Component readback only; serializer weaving/remote snapshot behaviour unvalidated.",
                    enabledHumanAnimators = human.Length, enabledSdkControllers = sdkControllers, enabledIkSolvers = ikSolvers,
                    enabledTracking = tracking, enabledInput = input, enabledGrabbers = grabbers,
                    rootMotion = human.Length == 1 ? (bool?)human[0].applyRootMotion : null, transport
                });
            } finally { if (reopened != null) UnityEditor.PrefabUtility.UnloadPrefabContents(reopened); }
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
                rate == null || rate.type != UnityEngine.AnimatorControllerParameterType.Float)
                mismatches.Add("Controller required states/rate/IK metadata mismatch: " + assetPath(controller));
            foreach (var child in states) {
                bool moving = child.state.name != "Idle" && child.state.name != "Shoot";
                if (child.state.motion == null || child.state.iKOnFeet || child.state.speedParameterActive != moving ||
                    (moving && child.state.speedParameter != VrBattlegrounds.Bots.BotCombatAssets.MotionRateParameter))
                    mismatches.Add("State motion/rate metadata mismatch: " + controller.name + "/" + child.state.name);
            }
            controllers.Add(new { path = assetPath(controller), guid = assetGuid(controller), ikPass = controller.layers[0].iKPass,
                states = states.Select(c => new { name = c.state.name, motion = assetPath(c.state.motion), c.state.speedParameterActive,
                    c.state.speedParameter, c.state.iKOnFeet }).ToArray() });
        }
        var motionKeys = new System.Collections.Generic.HashSet<string>();
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
            var info = UnityEditor.AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Arsenal.WeaponInfo>(path);
            string reason;
            bool admitted = VrBattlegrounds.Weapons.WeaponAutomationCapability.ValidateDefinition(info, out reason);
            var prefab = info == null ? null : info.WeaponPrefab;
            var binding = prefab == null ? null : prefab.GetComponent<VrBattlegrounds.Weapons.WeaponEquipmentBinding>();
            var readiness = prefab == null ? null : prefab.GetComponent<VrBattlegrounds.Weapons.WeaponReadinessController>();
            if (!admitted || binding == null || readiness == null) mismatches.Add("Canonical automation definition incomplete: " + id + "/" + reason);
            definitions.Add(new { weapon = id, path, guid = assetGuid(info), prefabPath = assetPath(prefab), prefabGuid = assetGuid(prefab),
                capabilityValid = admitted, reason, hasEquipmentBinding = binding != null,
                readinessOwnerType = readiness == null ? null : readiness.GetType().FullName,
                scope = "Definition only; held/cold-preparation/ledger/emission/actor attribution/gameplay unvalidated." });
        }
        stage = "completed";
    }
} catch (System.Exception error) { errors.Add(stage + ": " + error); }
finally {
    previewAfter = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
    activeAfter = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
    if (previewAfter != previewBefore || activeAfter != activeBefore)
        mismatches.Add("Cleanup changed preview count or active scene baseline.");
    System.IO.Directory.CreateDirectory(folder);
    System.IO.File.WriteAllText(resultPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
        executionCompleted = true, stage, setup, composition, calibration, authoringCompleted, calibrationPassed, calibrationBuilt,
        modelCount, socketCount, motionCount, variants, controllers, motions, definitions, mismatches, unknown, errors,
        cleanup = new { previewBefore, previewAfter, activeBefore, activeAfter,
            isPlaying = UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode }, gameplayAccepted = false,
        scope = "Native authoring and read-only saved prefab/definition metadata; no runtime/remote/Quest/gameplay acceptance."
    }, Newtonsoft.Json.Formatting.Indented));
}
return new { executionCompleted = true, reportPath = resultPath, authoringCompleted, modelCount, socketCount, motionCount,
    calibrationPassed, calibrationBuilt,
    readbackVariants = variants.Count, definitions = definitions.Count, mismatches = mismatches.Count,
    unknown = unknown.Count, errors = errors.Count, previewBefore, previewAfter, gameplayAccepted = false };
