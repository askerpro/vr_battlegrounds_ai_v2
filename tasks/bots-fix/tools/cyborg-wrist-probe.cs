// Диагностика только transient объектов; assets и настройки Animator не сохраняются.
var reportPath = "tasks/bots-fix/reports/wrist-probe/result.json";
var reports = new System.Collections.Generic.List<object>();
var metrics = new System.Collections.Generic.List<object>();
var failures = new System.Collections.Generic.List<string>();
var scene = default(UnityEngine.SceneManagement.Scene);
var temporary = new System.Collections.Generic.List<UnityEngine.GameObject>();
System.Func<UnityEngine.Vector3, float[]> vector = v => new[] { v.x, v.y, v.z };
System.Func<UnityEngine.Quaternion, float[]> quaternion = q => new[] { q.x, q.y, q.z, q.w };
System.Func<UnityEngine.Matrix4x4, float[]> matrix = m => System.Linq.Enumerable.Range(0, 16).Select(i => m[i]).ToArray();
System.Func<UnityEngine.Transform, UnityEngine.Transform, string> path = (t, root) => t == null ? null :
    t == root ? "" : UnityEditor.AnimationUtility.CalculateTransformPath(t, root);
System.Func<UnityEngine.Object, object> identity = obj => {
    string guid = null; long localId = 0;
    bool persistent = obj != null && UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out guid, out localId);
    if (!persistent) { guid = null; localId = 0; }
    return new { name = obj == null ? null : obj.name, guid, localId, persistent };
};
System.Func<UnityEngine.GameObject, string, object> snapshot = (body, stage) => {
    var avatar = body.GetComponent<UltimateXR.Avatar.UxrAvatar>();
    var animator = body.GetComponentsInChildren<UnityEngine.Animator>(true).FirstOrDefault(a => a.avatar != null && a.avatar.isHuman);
    var wrist = avatar == null ? null : avatar.GetHandBone(UltimateXR.Core.UxrHandSide.Right);
    UnityEngine.Transform animated = null;
    string animatorLookupError = null;
    // Prefab asset может не иметь инициализированного Animator; это отдельный факт отчёта.
    if (animator != null) try { animated = animator.GetBoneTransform(UnityEngine.HumanBodyBones.RightHand); }
        catch (System.Exception e) { animatorLookupError = e.Message; }
    var bindposes = new System.Collections.Generic.List<object>();
    foreach (var renderer in body.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true)) {
        var bones = renderer.bones;
        for (int i = 0; i < bones.Length; i++) if (wrist != null && bones[i] == wrist) {
            var mesh = renderer.sharedMesh;
            bindposes.Add(new { renderer = path(renderer.transform, body.transform), rootBone = path(renderer.rootBone, body.transform),
                mesh = identity(mesh), boneIndex = i, bindpose = mesh != null && i < mesh.bindposes.Length ? matrix(mesh.bindposes[i]) : null });
        }
    }
    object anatomy = null;
    if (avatar != null) {
        // Читаем сериализованные axes, не вызывая getter RigInfo с возможным пересчётом.
        var serialized = new UnityEditor.SerializedObject(avatar);
        var axes = serialized.FindProperty("_rigInfo._rightArmInfo._handUniversalLocalAxes");
        if (axes != null) {
            var targetProperty = axes.FindPropertyRelative("_transform");
            var rotationProperty = axes.FindPropertyRelative("_universalToActualAxesRotation");
            var target = targetProperty == null ? null : targetProperty.objectReferenceValue as UnityEngine.Transform;
            var axesRotation = rotationProperty == null ? UnityEngine.Quaternion.identity : rotationProperty.quaternionValue;
            var universal = target == null ? UnityEngine.Quaternion.identity : target.rotation * UnityEngine.Quaternion.Inverse(axesRotation);
            anatomy = new { target = path(target, body.transform), hasRotation = rotationProperty != null,
                universalToActual = quaternion(axesRotation), worldRotation = quaternion(universal),
                wristLocalRotation = quaternion(wrist == null ? UnityEngine.Quaternion.identity : UnityEngine.Quaternion.Inverse(wrist.rotation) * universal) };
        }
    }
    var state = animator == null ? default(UnityEngine.AnimatorStateInfo) : animator.GetCurrentAnimatorStateInfo(0);
    return new { stage, body = body.name, rootPosition = vector(body.transform.position), rootRotation = quaternion(body.transform.rotation),
        rootMatrix = matrix(body.transform.localToWorldMatrix), wristPath = path(wrist, body.transform),
        animatedWristPath = path(animated, body.transform), wristIdentityMatches = wrist != null && wrist == animated,
        animatorLookupError, animatorPath = animator == null ? null : path(animator.transform, body.transform),
        animatorEnabled = animator != null && animator.enabled, avatarAsset = identity(animator == null ? null : animator.avatar),
        controllerAsset = identity(animator == null ? null : animator.runtimeAnimatorController), stateHash = state.fullPathHash,
        normalizedTime = state.normalizedTime, wristInstanceId = wrist == null ? 0 : wrist.GetInstanceID(),
        wristLocalPosition = vector(wrist == null ? UnityEngine.Vector3.zero : wrist.localPosition),
        wristLocalRotation = quaternion(wrist == null ? UnityEngine.Quaternion.identity : wrist.localRotation),
        wristLocalScale = vector(wrist == null ? UnityEngine.Vector3.zero : wrist.localScale),
        wristWorld = wrist == null ? null : matrix(wrist.localToWorldMatrix),
        wristRootRelative = wrist == null ? null : matrix(body.transform.worldToLocalMatrix * wrist.localToWorldMatrix),
        wristRootPosition = vector(wrist == null ? UnityEngine.Vector3.zero : body.transform.InverseTransformPoint(wrist.position)),
        wristRootRotation = quaternion(wrist == null ? UnityEngine.Quaternion.identity : UnityEngine.Quaternion.Inverse(body.transform.rotation) * wrist.rotation),
        anatomy, bindposes };
};
try {
    if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Wrist probe только вне Play.");
    var library = UnityEngine.Resources.Load<VrBattlegrounds.Bots.BotCombatAssets>(VrBattlegrounds.Bots.BotCombatAssets.ResourcePath);
    if (library == null) throw new System.InvalidOperationException("Нет BotCombatAssets.");
    scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
    foreach (var model in library.clipBodies.Where(m => m != null && m.source != null &&
        (m.source.name == "Optimized_MEF_Player_Black" || m.source.name == "PlayerControllersCyborgAvatar"))) {
        try {
            var prefabs = new[] { model.source, model.prefab };
            var live = new UnityEngine.GameObject[2];
            for (int i = 0; i < prefabs.Length; i++) {
                if (prefabs[i] == null) throw new System.InvalidOperationException("Нет clip body.");
                var prefab = prefabs[i];
                reports.Add(new { model = model.source.name, kind = i == 0 ? "source" : "npc", snapshot = snapshot(prefab, "asset-before-instantiation") });
                // Все исходные local TRS читаются до создания transient экземпляра.
                var rest = prefab.GetComponentsInChildren<UnityEngine.Transform>(true).ToDictionary(t => path(t, prefab.transform),
                    t => new UnityEngine.Pose(t.localPosition, t.localRotation));
                var scales = prefab.GetComponentsInChildren<UnityEngine.Transform>(true).ToDictionary(t => path(t, prefab.transform), t => t.localScale);
                reports.Add(new { model = model.source.name, kind = i == 0 ? "source" : "npc", serializedTransforms =
                    rest.Select(p => new { path = p.Key, position = vector(p.Value.position), rotation = quaternion(p.Value.rotation), scale = vector(scales[p.Key]) }).ToArray() });
                var body = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, scene);
                temporary.Add(body); live[i] = body;
                System.Action<string> record = stage => reports.Add(new { model = model.source.name, kind = i == 0 ? "source" : "npc", snapshot = snapshot(body, stage) });
                record("instantiated");
                foreach (var writer in body.GetComponentsInChildren<UltimateXR.Animation.IK.UxrIKSolver>(true)) writer.enabled = false;
                foreach (var writer in body.GetComponentsInChildren<UltimateXR.Avatar.Controllers.UxrStandardAvatarController>(true)) writer.enabled = false;
                foreach (var writer in body.GetComponentsInChildren<UltimateXR.Devices.UxrTrackingDevice>(true)) writer.enabled = false;
                foreach (var writer in body.GetComponentsInChildren<UltimateXR.Devices.UxrControllerInput>(true)) writer.enabled = false;
                foreach (var writer in body.GetComponentsInChildren<UltimateXR.Manipulation.UxrGrabber>(true)) writer.enabled = false;
                foreach (var writer in body.GetComponentsInChildren<UnityEngine.Animator>(true)) writer.enabled = false;
                record("disabled");
                foreach (var t in body.GetComponentsInChildren<UnityEngine.Transform>(true)) {
                    var key = path(t, body.transform);
                    if (!rest.ContainsKey(key)) throw new System.InvalidOperationException("Нет serialized transform path: " + key);
                    t.SetLocalPositionAndRotation(rest[key].position, rest[key].rotation); t.localScale = scales[key];
                }
                record("serialized-rest-restored");
                var animator = body.GetComponentsInChildren<UnityEngine.Animator>(true).First(a => a.avatar != null && a.avatar.isHuman);
                // Animator включается только на transient объекте; SDK/IK/VR остаются выключены.
                animator.enabled = true; animator.Rebind(); record("rebound");
                animator.Update(0f); record("updated-zero");
            }
            var sourceWrist = live[0].GetComponent<UltimateXR.Avatar.UxrAvatar>().GetHandBone(UltimateXR.Core.UxrHandSide.Right);
            var npcWrist = live[1].GetComponent<UltimateXR.Avatar.UxrAvatar>().GetHandBone(UltimateXR.Core.UxrHandSide.Right);
            var finalMetric = new { model = model.source.name, stage = "final-pair", restMm = UnityEngine.Vector3.Distance(
                live[0].transform.InverseTransformPoint(sourceWrist.position), live[1].transform.InverseTransformPoint(npcWrist.position)) * 1000f,
                restAngle = UnityEngine.Quaternion.Angle(UnityEngine.Quaternion.Inverse(live[0].transform.rotation) * sourceWrist.rotation,
                    UnityEngine.Quaternion.Inverse(live[1].transform.rotation) * npcWrist.rotation) };
            reports.Add(finalMetric); metrics.Add(finalMetric);
        } catch (System.Exception e) { failures.Add(model.source.name + ": " + e); }
    }
} catch (System.Exception e) { failures.Add(e.ToString()); }
finally {
    foreach (var body in temporary) if (body != null) try { UnityEngine.Object.DestroyImmediate(body); }
        catch (System.Exception e) { failures.Add("cleanup object: " + e); }
    if (scene.IsValid()) try { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        catch (System.Exception e) { failures.Add("cleanup scene: " + e); }
    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(reportPath));
    System.IO.File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(new { reports, failures },
        Newtonsoft.Json.Formatting.Indented));
}
return new { executionCompleted = true, reportPath, snapshots = reports.Count, failures = failures.Count, metrics,
    scope = "Transient source/NPC rest and initialization; no assets saved, no gameplay acceptance." };
