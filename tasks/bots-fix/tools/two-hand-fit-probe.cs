// Кандидат одного постоянного fit. Контакты — anatomical palm proxies, не доказательство mesh contact.
var folder = "tasks/bots-fix/reports/two-hand-fit";
var reportPath = System.IO.Path.Combine(folder, "result.json");
var pairs = new System.Collections.Generic.List<object>();
var failures = new System.Collections.Generic.List<string>();
var scene = default(UnityEngine.SceneManagement.Scene);
var temporary = new System.Collections.Generic.List<UnityEngine.GameObject>();
System.Func<float, bool> finite = x => !float.IsNaN(x) && !float.IsInfinity(x);
System.Func<UnityEngine.Vector3, float[]> vector = v => new[] { v.x, v.y, v.z };
System.Func<UnityEngine.Quaternion, float[]> quaternion = q => new[] { q.x, q.y, q.z, q.w };
System.Func<UnityEngine.Transform, float> scale = frame => {
    if (frame == null) throw new System.InvalidOperationException("Нет scale frame.");
    var s = frame.lossyScale; var m = frame.localToWorldMatrix;
    var x = ((UnityEngine.Vector3)m.GetColumn(0)).normalized;
    var y = ((UnityEngine.Vector3)m.GetColumn(1)).normalized;
    var z = ((UnityEngine.Vector3)m.GetColumn(2)).normalized;
    if (!finite(s.x) || !finite(s.y) || !finite(s.z) || s.x <= .00001f ||
        UnityEngine.Mathf.Abs(s.x - s.y) > .00001f || UnityEngine.Mathf.Abs(s.x - s.z) > .00001f ||
        !finite(m.determinant) || m.determinant <= 0f || UnityEngine.Mathf.Abs(UnityEngine.Vector3.Dot(x, y)) > .0001f ||
        UnityEngine.Mathf.Abs(UnityEngine.Vector3.Dot(x, z)) > .0001f || UnityEngine.Mathf.Abs(UnityEngine.Vector3.Dot(y, z)) > .0001f)
        throw new System.InvalidOperationException("Нужен positive uniform TRS: " + frame.name);
    return s.x;
};
System.Func<UnityEngine.Transform, UnityEngine.Transform, UnityEngine.Transform, UnityEngine.Transform, UnityEngine.Vector3> palm = (wrist, index, middle, ring) => {
    if (wrist == null || index == null || middle == null || ring == null) throw new System.InvalidOperationException("Нет bones для palm proxy.");
    var value = wrist.position + .5f * ((index.position + middle.position + ring.position) / 3f - wrist.position);
    if (!finite(value.x) || !finite(value.y) || !finite(value.z)) throw new System.InvalidOperationException("Невалидный palm proxy.");
    return value;
};
System.Action<UnityEngine.GameObject> disableWriters = body => {
    foreach (var writer in body.GetComponentsInChildren<UltimateXR.Animation.IK.UxrIKSolver>(true)) writer.enabled = false;
    foreach (var writer in body.GetComponentsInChildren<UltimateXR.Avatar.Controllers.UxrStandardAvatarController>(true)) writer.enabled = false;
    foreach (var writer in body.GetComponentsInChildren<UltimateXR.Devices.UxrTrackingDevice>(true)) writer.enabled = false;
    foreach (var writer in body.GetComponentsInChildren<UltimateXR.Devices.UxrControllerInput>(true)) writer.enabled = false;
    foreach (var writer in body.GetComponentsInChildren<UltimateXR.Manipulation.UxrGrabber>(true)) writer.enabled = false;
    foreach (var writer in body.GetComponentsInChildren<UnityEngine.Animator>(true)) writer.enabled = false;
};
try {
    if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Two-hand probe только вне Play.");
    var library = UnityEngine.Resources.Load<VrBattlegrounds.Bots.BotCombatAssets>(VrBattlegrounds.Bots.BotCombatAssets.ResourcePath);
    if (library == null) throw new System.InvalidOperationException("Нет BotCombatAssets.");
    const string referencePath = "Assets/Prefabs/Player/MEF_Base_Avatar.prefab";
    const string referenceGuid = "b6fe59db941fa944696ece5e1aabc032";
    if (UnityEditor.AssetDatabase.AssetPathToGUID(referencePath) != referenceGuid) throw new System.InvalidOperationException("Изменена identity MEF template.");
    scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
    var reference = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(
        UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(referencePath), scene);
    temporary.Add(reference); disableWriters(reference);
    var referenceAvatar = reference.GetComponent<UltimateXR.Avatar.UxrAvatar>();
    if (referenceAvatar == null) throw new System.InvalidOperationException("Нет source UxrAvatar.");
    var rightHand = referenceAvatar.AvatarRig.RightArm.Hand;
    var leftHand = referenceAvatar.AvatarRig.LeftArm.Hand;
    var sourceCR = palm(rightHand.Wrist, rightHand.Index.Proximal, rightHand.Middle.Proximal, rightHand.Ring.Proximal);
    var sourceCL = palm(leftHand.Wrist, leftHand.Index.Proximal, leftHand.Middle.Proximal, leftHand.Ring.Proximal);
    var sourceGR = reference.GetComponentsInChildren<UltimateXR.Manipulation.UxrGrabber>(true).Single(g => g.Side == UltimateXR.Core.UxrHandSide.Right).transform;
    var sourceGL = reference.GetComponentsInChildren<UltimateXR.Manipulation.UxrGrabber>(true).Single(g => g.Side == UltimateXR.Core.UxrHandSide.Left).transform;
    scale(rightHand.Wrist); scale(leftHand.Wrist); scale(sourceGR); scale(sourceGL);
    var states = new[] { "Idle", "Move", "Back", "Left", "Right", "Shoot" };
    foreach (var model in library.clipBodies.Where(m => m != null && m.source != null &&
        (m.source.name == "Optimized_MEF_Player_Black" || m.source.name == "PlayerControllersCyborgAvatar")))
    foreach (var id in new[] { "PPK", "AK105" }) {
        UnityEngine.GameObject body = null, item = null;
        var samples = new System.Collections.Generic.List<object>();
        var summaries = new System.Collections.Generic.List<object>();
        var pair = new System.Collections.Generic.Dictionary<string, object> { { "model", model.source.name }, { "weapon", id },
            { "samples", samples }, { "summaries", summaries }, { "candidateOnly", true }, { "meshContactProven", false },
            { "sourceTemplate", referencePath }, { "sourceTemplateGuid", referenceGuid },
            { "cyborgAuthoredGripEntryUsed", false }, { "fingerOverlay", false } };
        pairs.Add(pair);
        try {
            body = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(model.prefab, scene); temporary.Add(body);
            var candidates = body.GetComponentsInChildren<UnityEngine.Animator>(true).Where(a => a.enabled && a.avatar != null && a.avatar.isHuman).ToArray();
            if (candidates.Length != 1) throw new System.InvalidOperationException("Нет единственного видимого Humanoid Animator.");
            var animator = candidates[0]; disableWriters(body); animator.enabled = true;
            body.transform.SetPositionAndRotation(UnityEngine.Vector3.zero, UnityEngine.Quaternion.identity);
            animator.runtimeAnimatorController = id == "PPK" ? library.pistol : library.rifle;
            animator.applyRootMotion = false; animator.cullingMode = UnityEngine.AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind(); animator.SetFloat(VrBattlegrounds.Bots.BotCombatAssets.MotionRateParameter, 1f);
            animator.Play("Idle", 0, 0f); animator.Update(0f);
            var wrist = animator.GetBoneTransform(UnityEngine.HumanBodyBones.RightHand);
            var leftWrist = animator.GetBoneTransform(UnityEngine.HumanBodyBones.LeftHand);
            float wristScale = scale(wrist); scale(leftWrist); scale(body.transform);
            System.Func<bool, UnityEngine.Vector3> targetPalm = isRight => palm(isRight ? wrist : leftWrist,
                animator.GetBoneTransform(isRight ? UnityEngine.HumanBodyBones.RightIndexProximal : UnityEngine.HumanBodyBones.LeftIndexProximal),
                animator.GetBoneTransform(isRight ? UnityEngine.HumanBodyBones.RightMiddleProximal : UnityEngine.HumanBodyBones.LeftMiddleProximal),
                animator.GetBoneTransform(isRight ? UnityEngine.HumanBodyBones.RightRingProximal : UnityEngine.HumanBodyBones.LeftRingProximal));
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Arsenal.WeaponInfo>("Assets/Data/Weapons/" + id + "_Weapon.asset");
            UnityEngine.Pose originalSocket;
            if (data == null || !library.TryGetSocket(model.source, data, out originalSocket)) throw new System.InvalidOperationException("Нет cached socket.");
            item = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(data.WeaponPrefab, scene); temporary.Add(item);
            float weaponWorldScale = scale(item.transform);
            var firearm = item.GetComponentInChildren<UltimateXR.Mechanics.Weapons.UxrFirearmWeapon>(true);
            VrBattlegrounds.Weapons.WeaponGripBinding binding;
            if (!VrBattlegrounds.Weapons.WeaponGripBinding.TryResolve(firearm, out binding) || binding.Support < 0)
                throw new System.InvalidOperationException("Нет explicit primary/support роли.");
            var primaryPoint = binding.Grip.GetGrabPoint(binding.Primary);
            var supportPoint = binding.Grip.GetGrabPoint(binding.Support);
            var primaryAlign = primaryPoint.GetGripPoseInfo(referenceGuid)?.GripAlignTransformHandRight;
            var supportAlign = supportPoint.GetGripPoseInfo(referenceGuid)?.GripAlignTransformHandLeft;
            if (primaryAlign == null || supportAlign == null || !primaryAlign.IsChildOf(item.transform) || !supportAlign.IsChildOf(item.transform) ||
                primaryPoint.SnapReference != UltimateXR.Manipulation.UxrSnapReference.UseOtherTransform ||
                supportPoint.SnapReference != UltimateXR.Manipulation.UxrSnapReference.UseOtherTransform ||
                primaryPoint.SnapMode != UltimateXR.Manipulation.UxrSnapToHandMode.PositionAndRotation ||
                supportPoint.SnapMode != UltimateXR.Manipulation.UxrSnapToHandMode.PositionAndRotation)
                throw new System.InvalidOperationException("Нет exact MEF physical bridge; fallback запрещён.");
            scale(primaryAlign); scale(supportAlign);
            System.Func<UnityEngine.Transform, UnityEngine.Transform, UnityEngine.Vector3, UnityEngine.Vector3> physicalPoint = (align, grabber, center) =>
                item.transform.InverseTransformPoint(align.position) + UnityEngine.Quaternion.Inverse(item.transform.rotation) * align.rotation *
                    UnityEngine.Quaternion.Inverse(grabber.rotation) * (center - grabber.position) / weaponWorldScale;
            var pR = physicalPoint(primaryAlign, sourceGR, sourceCR);
            var pL = physicalPoint(supportAlign, sourceGL, sourceCL);
            var weaponLine = pL - pR;
            if (weaponLine.sqrMagnitude < .000001f) throw new System.InvalidOperationException("Вырожденная physical weapon span.");
            var projectile = firearm.GetComponent<UltimateXR.Mechanics.Weapons.UxrProjectileSource>();
            int shotIndex = firearm.GetTriggerShotIndex(0);
            var muzzle = projectile != null && shotIndex >= 0 && shotIndex < projectile.ShotTypes.Count ? projectile.ShotTypes[shotIndex].ShotSource : null;
            if (muzzle == null) throw new System.InvalidOperationException("Нет actual ShotSource для roll/aim измерения.");
            var muzzleLocalForward = UnityEngine.Quaternion.Inverse(item.transform.rotation) * muzzle.forward;
            var aR = wrist.InverseTransformPoint(targetPalm(true));
            var aL = wrist.InverseTransformPoint(targetPalm(false));
            var idleLine = aL - aR;
            if (idleLine.sqrMagnitude < .000001f) throw new System.InvalidOperationException("Вырожденная target palm span.");
            var q1 = UnityEngine.Quaternion.FromToRotation(originalSocket.rotation * weaponLine, idleLine) * originalSocket.rotation;
            var axis = idleLine.normalized;
            var f = q1 * muzzleLocalForward;
            var desired = wrist.InverseTransformDirection(body.transform.forward);
            var fProjected = UnityEngine.Vector3.ProjectOnPlane(f, axis);
            var desiredProjected = UnityEngine.Vector3.ProjectOnPlane(desired, axis);
            bool rollApplied = fProjected.sqrMagnitude > .000001f && desiredProjected.sqrMagnitude > .000001f;
            float rollDegrees = rollApplied ? UnityEngine.Vector3.SignedAngle(fProjected, desiredProjected, axis) : 0f;
            var qFit = UnityEngine.Quaternion.AngleAxis(rollDegrees, axis) * q1;
            float localWeaponScale = weaponWorldScale / wristScale;
            var posFit = aR - qFit * pR * localWeaponScale;
            if (!finite(qFit.x) || !finite(qFit.y) || !finite(qFit.z) || !finite(qFit.w) ||
                !finite(posFit.x) || !finite(posFit.y) || !finite(posFit.z)) throw new System.InvalidOperationException("Fit не finite.");
            item.transform.SetParent(wrist, true); item.transform.SetLocalPositionAndRotation(posFit, qFit);
            if (UnityEngine.Mathf.Abs(scale(item.transform) - weaponWorldScale) > .00001f) throw new System.InvalidOperationException("Parent изменил world weapon scale.");
            pair["primary"] = binding.Primary; pair["support"] = binding.Support;
            pair["physicalPointRight"] = vector(pR); pair["physicalPointLeft"] = vector(pL);
            pair["rawPrimaryAlignPath"] = UnityEditor.AnimationUtility.CalculateTransformPath(primaryAlign, item.transform);
            pair["rawSupportAlignPath"] = UnityEditor.AnimationUtility.CalculateTransformPath(supportAlign, item.transform);
            pair["sourcePalmRight"] = vector(sourceCR); pair["sourcePalmLeft"] = vector(sourceCL);
            pair["sourceGrabberRightPosition"] = vector(sourceGR.position); pair["sourceGrabberLeftPosition"] = vector(sourceGL.position);
            pair["originalSocketPosition"] = vector(originalSocket.position); pair["originalSocketRotation"] = quaternion(originalSocket.rotation);
            pair["fitPosition"] = vector(posFit); pair["fitRotation"] = quaternion(qFit); pair["rollDegrees"] = rollDegrees; pair["rollApplied"] = rollApplied;
            pair["weaponWorldScale"] = weaponWorldScale; pair["localWeaponScale"] = localWeaponScale;
            pair["weaponSpanMm"] = weaponLine.magnitude * weaponWorldScale * 1000f;
            pair["idlePalmSpanMm"] = idleLine.magnitude * wristScale * 1000f;
            pair["muzzlePath"] = UnityEditor.AnimationUtility.CalculateTransformPath(muzzle, item.transform);
            System.Action<string, int, float, System.Collections.Generic.List<float[]>> measure = (group, n, phase, values) => {
                if (UnityEngine.Mathf.Abs(scale(wrist) - wristScale) > .00001f || UnityEngine.Mathf.Abs(scale(item.transform) - weaponWorldScale) > .00001f)
                    throw new System.InvalidOperationException("Scale меняется по клипу; постоянный fit не поддержан.");
                var cR = targetPalm(true); var cL = targetPalm(false); var d = cL - cR;
                if (d.sqrMagnitude < .000001f) throw new System.InvalidOperationException("Вырожденный sampled palm span.");
                var rightResidual = item.transform.TransformPoint(pR) - cR;
                var leftResidual = item.transform.TransformPoint(pL) - cL;
                var line = d.normalized;
                float along = UnityEngine.Vector3.Dot(leftResidual, line) * 1000f;
                float perpendicular = UnityEngine.Vector3.ProjectOnPlane(leftResidual, line).magnitude * 1000f;
                float primaryMm = rightResidual.magnitude * 1000f, supportMm = leftResidual.magnitude * 1000f;
                float spanMm = d.magnitude * 1000f;
                var dLocal = wrist.InverseTransformVector(d);
                float aimError = UnityEngine.Vector3.Angle(muzzle.forward, body.transform.forward);
                var localForward = body.transform.InverseTransformDirection(muzzle.forward);
                float elevation = UnityEngine.Mathf.Atan2(localForward.y, UnityEngine.Mathf.Sqrt(localForward.x * localForward.x + localForward.z * localForward.z)) * UnityEngine.Mathf.Rad2Deg;
                var row = new[] { primaryMm, supportMm, along, perpendicular, spanMm, aimError, elevation };
                if (row.Any(v => !finite(v))) throw new System.InvalidOperationException("Невалидная sampled metric.");
                values.Add(row);
                samples.Add(new { group, index = n, phase, isInTransition = animator.IsInTransition(0),
                    currentStateHash = animator.GetCurrentAnimatorStateInfo(0).fullPathHash,
                    nextStateHash = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0).fullPathHash : 0,
                    transitionNormalizedTime = animator.IsInTransition(0) ? animator.GetAnimatorTransitionInfo(0).normalizedTime : 0f,
                    primaryResidualMm = primaryMm, supportResidualMm = supportMm,
                    supportAlongMm = along, supportPerpendicularMm = perpendicular, palmSpanMm = spanMm,
                    dRightWristFrame = vector(dLocal), dDeltaFromIdleMm = (dLocal - idleLine).magnitude * wristScale * 1000f,
                    primaryResidualWorld = vector(rightResidual), supportResidualWorld = vector(leftResidual),
                    actualMuzzleForward = vector(muzzle.forward), aimErrorDegrees = aimError, elevationDegrees = elevation });
            };
            System.Action<string, System.Collections.Generic.List<float[]>> summarize = (group, values) => {
                summaries.Add(new { group, samples = values.Count,
                    primaryMaxMm = values.Max(v => v[0]), primaryRmsMm = UnityEngine.Mathf.Sqrt(values.Average(v => v[0] * v[0])),
                    supportMaxMm = values.Max(v => v[1]), supportRmsMm = UnityEngine.Mathf.Sqrt(values.Average(v => v[1] * v[1])),
                    supportAlongMinMm = values.Min(v => v[2]), supportAlongMaxMm = values.Max(v => v[2]),
                    supportPerpendicularMaxMm = values.Max(v => v[3]), palmSpanMinMm = values.Min(v => v[4]), palmSpanMaxMm = values.Max(v => v[4]),
                    palmSpanSpreadMm = values.Max(v => v[4]) - values.Min(v => v[4]),
                    aimErrorMaxDegrees = values.Max(v => v[5]), elevationMinDegrees = values.Min(v => v[6]), elevationMaxDegrees = values.Max(v => v[6]) });
            };
            foreach (var state in states) {
                animator.Play(state, 0, 0f); animator.Update(0f);
                if (!animator.GetCurrentAnimatorStateInfo(0).IsName(state)) throw new System.InvalidOperationException("Не выполнен state: " + state);
                var values = new System.Collections.Generic.List<float[]>();
                for (int n = 0; n <= 120; n++) { animator.Play(state, 0, n / 120f); animator.Update(0f); measure(state, n, n / 120f, values); }
                summarize(state, values);
            }
            foreach (var from in states) foreach (var to in states) if (from != to) {
                animator.Play(from, 0, .4f); animator.Update(0f);
                animator.CrossFadeInFixedTime(to, .1f, 0, 0f);
                var group = from + "->" + to; var values = new System.Collections.Generic.List<float[]>();
                for (int n = 1; n <= 20; n++) { animator.Update(.005f); measure(group, n, n / 20f, values); }
                summarize(group, values);
            }
            pair["completed"] = true; pair["toleranceAcceptance"] = "No physical/gameplay tolerance decision; inspect measured span, residual and independent ShotSource gates.";
        } catch (System.Exception e) { pair["error"] = e.ToString(); failures.Add(model.source.name + "/" + id + ": " + e); }
        finally {
            if (item != null) UnityEngine.Object.DestroyImmediate(item);
            if (body != null) UnityEngine.Object.DestroyImmediate(body);
        }
    }
} catch (System.Exception e) { failures.Add(e.ToString()); }
finally {
    foreach (var obj in temporary) if (obj != null) try { UnityEngine.Object.DestroyImmediate(obj); }
        catch (System.Exception e) { failures.Add("cleanup object: " + e); }
    if (scene.IsValid()) try { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        catch (System.Exception e) { failures.Add("cleanup scene: " + e); }
    System.IO.Directory.CreateDirectory(folder);
    System.IO.File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(new { pairs, failures,
        contactProxy = "W + .5*(average Index/Middle/Ring proximal world positions - W)",
        fitScope = "One Idle K reused across six states and all 30 directed transitions; no stretch, no wrist writes, no finger overlay, no runtime IK.",
        gameplayAccepted = false }, Newtonsoft.Json.Formatting.Indented));
}
return new { executionCompleted = true, pairs = pairs.Count, failures = failures.Count, reportPath, gameplayAccepted = false };
