if (UnityEditor.EditorApplication.isPlaying) throw new System.InvalidOperationException("Authoring probe только вне Play.");
var library = UnityEngine.Resources.Load<VrBattlegrounds.Bots.BotCombatAssets>(VrBattlegrounds.Bots.BotCombatAssets.ResourcePath);
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
var results = new System.Collections.Generic.List<object>();
try {
    foreach (var model in library.clipBodies) {
        var body = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(model.prefab, scene);
        try {
            var animator = body.GetComponentsInChildren<UnityEngine.Animator>(true).First(a => a.avatar != null && a.avatar.isHuman);
            var avatar = body.GetComponent<UltimateXR.Avatar.UxrAvatar>();
            var grabber = body.GetComponentsInChildren<UltimateXR.Manipulation.UxrGrabber>(true)
                .First(g => g.Side == UltimateXR.Core.UxrHandSide.Right);
            foreach (var id in new[] { "PPK", "AK105" }) {
                var data = UnityEditor.AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Arsenal.WeaponInfo>("Assets/Data/Weapons/" + id + "_Weapon.asset");
                var item = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(data.WeaponPrefab, scene);
                try {
                    var firearm = item.GetComponentInChildren<UltimateXR.Mechanics.Weapons.UxrFirearmWeapon>(true);
                    VrBattlegrounds.Weapons.WeaponGripBinding grip;
                    if (!VrBattlegrounds.Weapons.WeaponGripBinding.TryResolve(firearm, out grip)) throw new System.InvalidOperationException(id);
                    animator.runtimeAnimatorController = id == "PPK" ? library.pistol : library.rifle;
                    animator.applyRootMotion = false; animator.cullingMode = UnityEngine.AnimatorCullingMode.AlwaysAnimate;
                    animator.Rebind(); animator.Play("Idle", 0, 0); animator.Update(0);
                    var right = animator.GetBoneTransform(UnityEngine.HumanBodyBones.RightHand);
                    var left = animator.GetBoneTransform(UnityEngine.HumanBodyBones.LeftHand);
                    UltimateXR.Avatar.Rig.UxrAvatarRig.SetupRigElementsFromAnimator(avatar.AvatarRig, animator);
                    UnityEngine.Vector3 primary; UnityEngine.Quaternion primaryRotation;
                    if (!grip.Grip.ComputeRequiredGrabberTransform(grabber, grip.Primary, out primary, out primaryRotation, false))
                        throw new System.InvalidOperationException("Нет authored primary: " + id);
                    var primaryLocal = item.transform.InverseTransformPoint(primary);
                    var primaryLocalRotation = UnityEngine.Quaternion.Inverse(item.transform.rotation) * primaryRotation;
                    var sourceHandOffset = right.InverseTransformPoint(grabber.transform.position);
                    var sourceHandRotation = UnityEngine.Quaternion.Inverse(right.rotation) * grabber.transform.rotation;
                    var socketRotation = sourceHandRotation * UnityEngine.Quaternion.Inverse(primaryLocalRotation);
                    var socketPosition = sourceHandOffset - socketRotation * primaryLocal;
                    var states = new[] { "Idle", "Move", "Back", "Left", "Right" };
                    foreach (var state in states) {
                        animator.Play(state, 0, 0); animator.Update(0);
                        var clip = animator.GetCurrentAnimatorClipInfo(0).First().clip;
                        var frames = new System.Collections.Generic.List<object>();
                        var footPositions = new System.Collections.Generic.List<UnityEngine.Vector3[]>();
                        for (int n = 0; n <= 120; n++) {
                            animator.Play(state, 0, n / 120f); animator.Update(0);
                            var weaponRotation = right.rotation * socketRotation;
                            var weaponPosition = right.TransformPoint(socketPosition);
                            UnityEngine.Vector3 support = UnityEngine.Vector3.zero;
                            if (grip.Support >= 0) {
                                var supportGrabber = body.GetComponentsInChildren<UltimateXR.Manipulation.UxrGrabber>(true)
                                    .First(g => g.Side == UltimateXR.Core.UxrHandSide.Left);
                                UnityEngine.Quaternion supportRotation;
                                grip.Grip.ComputeRequiredGrabberTransform(supportGrabber, grip.Support, out support, out supportRotation, false);
                                support = weaponPosition + weaponRotation * item.transform.InverseTransformPoint(support);
                            }
                            var wristToGrabber = right.InverseTransformPoint(grabber.transform.position);
                            var relativeHandRotation = UnityEngine.Quaternion.Inverse(right.rotation) * grabber.transform.rotation;
                            var feet = new[] { animator.GetBoneTransform(UnityEngine.HumanBodyBones.LeftFoot).position,
                                animator.GetBoneTransform(UnityEngine.HumanBodyBones.RightFoot).position };
                            footPositions.Add(feet);
                            if (n % 6 == 0) frames.Add(new { phase = n / 120f,
                                wristToGrabber = new[] { wristToGrabber.x, wristToGrabber.y, wristToGrabber.z },
                                offsetDriftMm = UnityEngine.Vector3.Distance(wristToGrabber, sourceHandOffset) * 1000,
                                offsetAngle = UnityEngine.Quaternion.Angle(relativeHandRotation, sourceHandRotation),
                                handSpanMm = UnityEngine.Vector3.Distance(left.position, right.position) * 1000,
                                supportToWristMm = grip.Support < 0 ? 0 : UnityEngine.Vector3.Distance(support, left.position) * 1000,
                                rightPath = UnityEditor.AnimationUtility.CalculateTransformPath(right, body.transform),
                                grabberPath = UnityEditor.AnimationUtility.CalculateTransformPath(grabber.transform, body.transform) });
                        }
                        var direction = state == "Move" ? UnityEngine.Vector3.forward : state == "Back" ? UnityEngine.Vector3.back :
                            state == "Left" ? UnityEngine.Vector3.left : state == "Right" ? UnityEngine.Vector3.right : UnityEngine.Vector3.zero;
                        var speeds = new System.Collections.Generic.List<float>();
                        for (int side = 0; side < 2; side++) {
                            float low = footPositions.Min(f => f[side].y);
                            float high = footPositions.Max(f => f[side].y);
                            for (int n = 1; n < footPositions.Count - 1; n++) {
                                if (footPositions[n][side].y > low + (high - low) * .25f) continue;
                                float speed = -UnityEngine.Vector3.Dot((footPositions[n + 1][side] - footPositions[n - 1][side]) * (60f / clip.length), direction);
                                if (speed > .05f) speeds.Add(speed);
                            }
                        }
                        speeds.Sort();
                        results.Add(new { model = model.source.name, weapon = id, state = state,
                            grabberIsWristChild = grabber.transform.IsChildOf(right),
                            rootScale = body.transform.lossyScale.ToString("F4"), handScale = right.lossyScale.ToString("F4"),
                            sourceHandOffset = sourceHandOffset.ToString("F4"), clip = clip.name, clipLength = clip.length,
                            importedAverageSpeed = clip.averageSpeed.ToString("F4"), stanceSamples = speeds.Count,
                            stanceSpeed = speeds.Count == 0 ? 0f : speeds[speeds.Count / 2], frames = frames });
                    }
                } finally { UnityEngine.Object.DestroyImmediate(item); }
            }
        } finally { UnityEngine.Object.DestroyImmediate(body); }
    }
} finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
var path = "tasks/bots-fix/reports/clip-authoring/probe.json";
System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
System.IO.File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(results, Newtonsoft.Json.Formatting.Indented));
return new { executed = true, combinations = results.Count, reportPath = path };
