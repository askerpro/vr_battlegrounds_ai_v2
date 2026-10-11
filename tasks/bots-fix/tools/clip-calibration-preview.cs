// Диагностический offline-визуал cached socket; игровая приёмка этим не выполняется.
var folder = "tasks/bots-fix/reports/clip-calibration-preview";
var pairs = new System.Collections.Generic.List<object>();
var failures = new System.Collections.Generic.List<string>();
var files = new System.Collections.Generic.List<string>();
var scene = default(UnityEngine.SceneManagement.Scene);
var temporary = new System.Collections.Generic.List<UnityEngine.GameObject>();
System.Func<UnityEngine.Vector3, float[]> vector = v => new[] { v.x, v.y, v.z };
System.Func<UnityEngine.Quaternion, float[]> quaternion = q => new[] { q.x, q.y, q.z, q.w };
try {
    if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Preview только вне Play.");
    System.IO.Directory.CreateDirectory(folder);
    var library = UnityEngine.Resources.Load<VrBattlegrounds.Bots.BotCombatAssets>(VrBattlegrounds.Bots.BotCombatAssets.ResourcePath);
    if (library == null) throw new System.InvalidOperationException("Нет BotCombatAssets.");
    scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
    var lightObject = new UnityEngine.GameObject("ClipCalibrationPreviewLight"); temporary.Add(lightObject);
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject, scene);
    var light = lightObject.AddComponent<UnityEngine.Light>(); light.type = UnityEngine.LightType.Directional;
    light.intensity = 1.4f; light.shadows = UnityEngine.LightShadows.None;
    light.transform.rotation = UnityEngine.Quaternion.Euler(40f, -25f, 0f);
    var cameraObject = new UnityEngine.GameObject("ClipCalibrationPreviewCamera"); temporary.Add(cameraObject);
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
    var camera = cameraObject.AddComponent<UnityEngine.Camera>(); camera.enabled = false; camera.scene = scene;
    camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
    camera.backgroundColor = new UnityEngine.Color(.52f, .56f, .62f);
    camera.nearClipPlane = .01f; camera.farClipPlane = 80f; camera.useOcclusionCulling = false;
    int uiLayer = UnityEngine.LayerMask.NameToLayer("UI"); camera.cullingMask = uiLayer < 0 ? -1 : ~(1 << uiLayer);
    System.Func<string, UnityEngine.Bounds, UnityEngine.Vector3, object> shot = (label, bounds, offset) => {
        float fov = 40f;
        float radius = UnityEngine.Mathf.Max(.1f, bounds.extents.magnitude);
        var focus = bounds.center;
        var position = focus + offset.normalized * (radius / UnityEngine.Mathf.Sin(fov * UnityEngine.Mathf.Deg2Rad * .5f) * 1.12f);
        camera.transform.SetPositionAndRotation(position, UnityEngine.Quaternion.LookRotation(focus - position)); camera.fieldOfView = fov;
        var previous = UnityEngine.RenderTexture.active;
        var target = new UnityEngine.RenderTexture(960, 960, 24);
        var image = new UnityEngine.Texture2D(960, 960, UnityEngine.TextureFormat.RGB24, false);
        try {
            camera.targetTexture = target; camera.Render(); UnityEngine.RenderTexture.active = target;
            image.ReadPixels(new UnityEngine.Rect(0, 0, 960, 960), 0, 0); image.Apply();
            var pixels = image.GetPixels32(); var first = pixels[0];
            int diverse = pixels.Count(p => System.Math.Abs(p.r - first.r) > 2 || System.Math.Abs(p.g - first.g) > 2 || System.Math.Abs(p.b - first.b) > 2);
            if (diverse < 100) throw new System.InvalidOperationException("Пустой/одноцветный ракурс: " + label);
            var file = System.IO.Path.Combine(folder, label + ".png"); System.IO.File.WriteAllBytes(file, UnityEngine.ImageConversion.EncodeToPNG(image)); files.Add(file);
            return new { file, cameraPosition = vector(position), cameraRotation = quaternion(camera.transform.rotation),
                focus = vector(focus), fov, width = 960, height = 960, diversePixels = diverse };
        } finally {
            camera.targetTexture = null; UnityEngine.RenderTexture.active = previous;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
        }
    };
    foreach (var model in library.clipBodies.Where(m => m != null && m.source != null &&
        (m.source.name == "Optimized_MEF_Player_Black" || m.source.name == "PlayerControllersCyborgAvatar")))
    foreach (var id in new[] { "PPK", "AK105" }) {
        UnityEngine.GameObject body = null, item = null;
        try {
            body = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(model.prefab, scene); temporary.Add(body);
            body.transform.SetPositionAndRotation(UnityEngine.Vector3.zero, UnityEngine.Quaternion.identity); body.SetActive(true);
            foreach (var solver in body.GetComponentsInChildren<UltimateXR.Animation.IK.UxrIKSolver>(true)) solver.enabled = false;
            foreach (var writer in body.GetComponentsInChildren<UltimateXR.Avatar.Controllers.UxrStandardAvatarController>(true)) writer.enabled = false;
            foreach (var writer in body.GetComponentsInChildren<UltimateXR.Devices.UxrTrackingDevice>(true)) writer.enabled = false;
            foreach (var writer in body.GetComponentsInChildren<UltimateXR.Devices.UxrControllerInput>(true)) writer.enabled = false;
            foreach (var writer in body.GetComponentsInChildren<UltimateXR.Manipulation.UxrGrabber>(true)) writer.enabled = false;
            foreach (var childCamera in body.GetComponentsInChildren<UnityEngine.Camera>(true)) childCamera.enabled = false;
            var animator = body.GetComponentsInChildren<UnityEngine.Animator>(true).Single(a => a.enabled && a.avatar != null && a.avatar.isHuman);
            animator.runtimeAnimatorController = id == "PPK" ? library.pistol : library.rifle;
            animator.applyRootMotion = false; animator.cullingMode = UnityEngine.AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind(); animator.SetFloat(VrBattlegrounds.Bots.BotCombatAssets.MotionRateParameter, 1f);
            animator.Play("Idle", 0, 0f); animator.Update(0f);
            var right = animator.GetBoneTransform(UnityEngine.HumanBodyBones.RightHand);
            var left = animator.GetBoneTransform(UnityEngine.HumanBodyBones.LeftHand);
            if (right == null || left == null) throw new System.InvalidOperationException("Нет видимых Humanoid wrists.");
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Arsenal.WeaponInfo>("Assets/Data/Weapons/" + id + "_Weapon.asset");
            UnityEngine.Pose socket;
            if (data == null || !library.TryGetSocket(model.source, data, out socket)) throw new System.InvalidOperationException("Нет cached socket: " + id);
            item = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(data.WeaponPrefab, scene); temporary.Add(item); item.SetActive(true);
            item.transform.SetParent(right, false); item.transform.SetLocalPositionAndRotation(socket.position, socket.rotation);
            var firearm = item.GetComponentInChildren<UltimateXR.Mechanics.Weapons.UxrFirearmWeapon>(true);
            var projectile = firearm == null ? null : firearm.GetComponent<UltimateXR.Mechanics.Weapons.UxrProjectileSource>();
            var index = firearm == null ? -1 : firearm.GetTriggerShotIndex(0);
            var muzzle = projectile != null && index >= 0 && index < projectile.ShotTypes.Count ? projectile.ShotTypes[index].ShotSource : null;
            object muzzleData = muzzle == null ? null : new { path = UnityEditor.AnimationUtility.CalculateTransformPath(muzzle, item.transform),
                worldPosition = vector(muzzle.position), forward = vector(muzzle.forward) };
            var weaponRenderers = item.GetComponentsInChildren<UnityEngine.Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy &&
                r.gameObject.layer != uiLayer && !(r is UnityEngine.ParticleSystemRenderer)).ToArray();
            var visibleRenderers = body.GetComponentsInChildren<UnityEngine.Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy &&
                r.gameObject.layer != uiLayer && !(r is UnityEngine.ParticleSystemRenderer)).ToArray();
            if (weaponRenderers.Length == 0 || visibleRenderers.Length == 0) throw new System.InvalidOperationException("Нет видимой геометрии.");
            var full = visibleRenderers[0].bounds; foreach (var renderer in visibleRenderers.Skip(1)) full.Encapsulate(renderer.bounds);
            var hands = new UnityEngine.Bounds(right.position, UnityEngine.Vector3.one * .18f); hands.Encapsulate(left.position);
            foreach (var renderer in weaponRenderers) hands.Encapsulate(renderer.bounds);
            if (muzzle != null) hands.Encapsulate(muzzle.position);
            var label = model.source.name + "-" + id;
            var captures = new[] { shot(label + "-full-front-quarter", full, new UnityEngine.Vector3(.8f, .2f, 1f)),
                shot(label + "-hands-right-side", hands, new UnityEngine.Vector3(1f, .2f, .35f)) };
            pairs.Add(new { model = model.source.name, weapon = id, avatar = animator.avatar.name,
                rightWrist = UnityEditor.AnimationUtility.CalculateTransformPath(right, body.transform),
                leftWrist = UnityEditor.AnimationUtility.CalculateTransformPath(left, body.transform),
                socketPosition = vector(socket.position), socketRotation = quaternion(socket.rotation),
                weaponWorldPosition = vector(item.transform.position), weaponWorldRotation = quaternion(item.transform.rotation),
                weaponScale = vector(item.transform.lossyScale), muzzle = muzzleData, captures });
        } catch (System.Exception e) { failures.Add(model.source.name + "/" + id + ": " + e); }
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
    System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "manifest.json"), Newtonsoft.Json.JsonConvert.SerializeObject(
        new { pairs, failures, files, expectedImages = 8, gameplayAccepted = false }, Newtonsoft.Json.Formatting.Indented));
}
return new { executionCompleted = true, pairs = pairs.Count, images = files.Count, failures = failures.Count,
    manifest = System.IO.Path.Combine(folder, "manifest.json"), gameplayAccepted = false };
