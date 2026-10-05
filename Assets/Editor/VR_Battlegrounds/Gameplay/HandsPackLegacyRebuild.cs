using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Пересборка трёх старых Hands-префабов из FBX без регистрации новых предметов и правки аватаров.
    /// Доноры сохраняются однократно вне игрового каталога; повторный запуск читает тот же baseline.
    /// </summary>
    public static class HandsPackLegacyRebuild
    {
        private const string Donors = "Assets/Art/Weapons/HandsPack/LegacyBuildDonors";
        private const string MotionRoot = "Assets/Art/Weapons/HandsPack";
        private const string Gun = "Assets/Prefabs/Weapons/BrowningHiPower/BrowningHiPower.prefab";
        private const string GunMag = "Assets/Prefabs/Weapons/BrowningHiPower/BrowningHiPower_Magazine.prefab";
        private const string Rifle = "Assets/Prefabs/Weapons/AR15/AR15.prefab";
        private const string RifleMag = "Assets/Prefabs/Weapons/AR15/AR15_Magazine.prefab";
        private const string Pump = "Assets/Prefabs/Weapons/FabarmSDASS/FabarmSDASS.prefab";
        private const string PumpMag = "Assets/Prefabs/Weapons/FabarmSDASS/FabarmSDASS_Ammo.prefab";
        private static readonly string[] Targets = { Gun, GunMag, Rifle, RifleMag, Pump, PumpMag };

        /// <summary>Создаёт только Art baseline/motion assets и возвращает preflight. Игровые префабы ещё не меняются.</summary>
        public static string PrepareBaseline()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Подготовка Hands запрещена в Play Mode");
            var ids = Targets.ToDictionary(p => p, ReadAssetId);
            CaptureDonors();
            string report = Preflight(Recipes(ids));
            AssetDatabase.SaveAssets();
            return report;
        }

        /// <summary>Единственная entry point для writer под Unity lock. Сначала все доноры и preflight, затем три сборки.</summary>
        private static void OpenWorkbench() => VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.OpenMaintenance(VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.Tab.Build);

        public static void RebuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Пересборка Hands запрещена в Play Mode");
            foreach (string path in Targets)
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    throw new InvalidOperationException($"Отсутствует baseline: {path}");
            var guids = Targets.ToDictionary(p => p, AssetDatabase.AssetPathToGUID);
            var ids = Targets.ToDictionary(p => p, ReadAssetId);
            var rootIds = Targets.ToDictionary(p => p, ReadRootLocalId);
            CaptureDonors();
            HandsPackWeaponRecipe[] recipes = Recipes(ids);

            // Ошибка структуры источника должна обнаружиться до перезаписи первого игрового префаба.
            GameLog.WeaponSystem.Info(Preflight(recipes));

            foreach (var recipe in recipes)
            {
                using var source = new HandsPackWeapon(recipe.Folder, recipe.BodyPart);
                HandsPackWeaponBuilder.Build(recipe, source);
                string path = $"Assets/Prefabs/Weapons/{recipe.PrefabFolder}/{recipe.Name}.prefab";
                string magPath = $"Assets/Prefabs/Weapons/{recipe.PrefabFolder}/{recipe.MagazineName}.prefab";
                ApplyMagazine(magPath);
                InstallMotion(path, recipe, source);
                foreach (string saved in new[] { path, magPath })
                    if (AssetDatabase.AssetPathToGUID(saved) != guids[saved] || ReadAssetId(saved) != ids[saved] || ReadRootLocalId(saved) != rootIds[saved])
                        throw new InvalidOperationException($"Изменён GUID/root localFileID/NetworkIdentity assetId: {saved}");
                ValidateReferences(path, magPath);
                GameLog.WeaponSystem.Info($"[HandsPackLegacyRebuild] {recipe.Name}: FBX, baseline config и generic motion сохранены");
            }
            AssetDatabase.SaveAssets();
        }

        private static string Preflight(HandsPackWeaponRecipe[] recipes)
        {
            var report = new List<string>();
            // Все три источника проверяются до экспорта первого motion asset.
            foreach (var recipe in recipes)
            {
                using var source = new HandsPackWeapon(recipe.Folder, recipe.BodyPart);
                ValidateSourceParts(recipe, source);
                source.Clip(recipe.PoseClip);
                source.Clip(recipe.ActionClip);
                if (recipe.Name == "BrowningHiPower") source.Clip("Shot_end");
                Matrix4x4 placement = HandsPackWeaponBuilder.BodyPlacement(recipe, source);
                HandsPackWeaponBuilder.ResolveActionTravel(recipe, source, placement, out _);
                HandsPackWeaponBuilder.ResolveTriggerRotation(recipe, source.Measure(recipe.TriggerPart, recipe.ActionClip, recipe.RestClip));
            }
            foreach (var recipe in recipes)
            {
                using var source = new HandsPackWeapon(recipe.Folder, recipe.BodyPart);
                PartMotion action = source.Measure(recipe.ActionPart, recipe.ActionClip, recipe.RestClip);
                Vector3 actionTravel = HandsPackWeaponBuilder.ResolveActionTravel(recipe, source,
                    HandsPackWeaponBuilder.BodyPlacement(recipe, source), out bool donorTravel);
                var trigger = HandsPackWeaponBuilder.ResolveTriggerRotation(recipe, source.Measure(recipe.TriggerPart, recipe.ActionClip, recipe.RestClip));
                var motion = ExportMotion(recipe, source);
                report.Add($"{recipe.Name}: {source.ModelPath}; body={recipe.BodyPart}; action={recipe.ActionPart}; trigger={recipe.TriggerPart}; " +
                    $"static=[{string.Join(",", recipe.StaticParts)}]; " +
                    $"excluded=[{string.Join(",", recipe.ExcludedSourceParts ?? Array.Empty<string>())}]; " +
                    $"Fire={motion.Fire.SourceGuid}/{motion.Fire.SourceLocalId}; Empty={(motion.Empty == null ? "нет" : motion.Empty.SourceGuid)}; " +
                    $"Manual={(motion.Manual == null ? "нет" : motion.Manual.SourceGuid)}; sourceTravel={action.Far:F5}; sourceActionRotation={action.MaxAngle:F5}; " +
                    $"actionTravel={actionTravel:F5}/{actionTravel.magnitude:F5}; actionOrigin={(donorTravel ? "immutable donor manual fallback (no source translation): " + recipe.GripDonor : "source clip")}; " +
                    $"trigger={trigger.Axis}/{trigger.Degrees:F5}; triggerOrigin={(trigger.UsesDonor ? "immutable donor (no source rotation): " + recipe.FirearmDonor : "source clip")}; baselineLength={recipe.TargetLength:F5}; finalRootScale={recipe.FinalRootScale:F5}; " +
                    $"mag={recipe.MagazineName}/{recipe.MagazineCapacity}; network={recipe.NetworkAssetId}/{recipe.MagazineAssetId}");
            }
            return string.Join("\n", report);
        }

        private static uint ReadAssetId(string path)
        {
            var identity = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<NetworkIdentity>();
            if (identity == null) throw new InvalidOperationException($"Нет NetworkIdentity: {path}");
            return (uint)new SerializedObject(identity).FindProperty("_assetId").longValue;
        }

        private static long ReadRootLocalId(string path)
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(root, out string _, out long localId) || localId == 0)
                throw new InvalidOperationException($"Нет persistent root localFileID: {path}");
            return localId;
        }

        private static string Donor(string original)
        {
            // Snapshot identity не зависит от нового публичного имени. Immutable baseline не переснимается при rename.
            string filename = original switch
            {
                Gun => "Gun_real.prefab",
                GunMag => "Gun_real_mag.prefab",
                Rifle => "M16_Rifle_prefab.prefab",
                RifleMag => "M16_Magazine.prefab",
                Pump => "Shotgun_real.prefab",
                PumpMag => "Shotgun_real_mag.prefab",
                _ => throw new InvalidOperationException("Нет immutable donor mapping: " + original)
            };
            return $"{Donors}/{filename}";
        }

        private static void CaptureDonors()
        {
            KinemationWeapon.EnsureFolder(Donors);
            // Смешанный baseline недопустим: после частичного ручного удаления snapshots пересборка должна остановиться.
            int existing = Targets.Count(p => AssetDatabase.LoadAssetAtPath<GameObject>(Donor(p)) != null);
            if (existing == Targets.Length) return;
            if (existing != 0) throw new InvalidOperationException("Неполный immutable baseline Hands: восстановите все шесть donor assets");
            foreach (string original in Targets)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(original);
                try
                {
                    // У snapshot нет зависимости от магазина, который эта же операция позднее пересобирает.
                    if (PrefabUtility.IsAnyPrefabInstanceRoot(root))
                        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                        if (child != root.transform && PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject))
                            PrefabUtility.UnpackPrefabInstance(child.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    PrefabUtility.SaveAsPrefabAsset(root, Donor(original));
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
        }

        private static HandsPackWeaponRecipe[] Recipes(Dictionary<string, uint> ids)
        {
            var gun = HandsPackWeaponBuilder.PistolDonor(new HandsPackWeaponRecipe
            {
                Name = "BrowningHiPower", PrefabFolder = "BrowningHiPower", Folder = "Hands_Gun", BodyPart = "Base_mesh",
                PoseClip = "Aiming_Idle", ActionClip = "Shot", TriggerPart = "Trigger_mesh", ActionPart = "Gate_mesh",
                SupportGrip = true,
                MagazinePart = "Magazine_mesh"
            });
            var rifle = HandsPackWeaponBuilder.RifleDonor(new HandsPackWeaponRecipe
            {
                Name = "AR15", PrefabFolder = "AR15", Folder = "Hands_Automatic_Rifle03", BodyPart = "Rifle_Body_Mesh",
                BodyObjectName = "Rifle_Body_Mesh", PoseClip = "Aim_Idle", ActionClip = "Shot", SupportGrip = true,
                MagazinePart = "Rifle_Magazine_Mesh"
            });
            GameObject rifleBaseline = AssetDatabase.LoadAssetAtPath<GameObject>(Donor(Rifle));
            var rifleFire = new SerializedObject(rifleBaseline.GetComponent<UxrFirearmWeapon>());
            Transform trigger = (Transform)rifleFire.FindProperty("_triggers").GetArrayElementAtIndex(0)
                .FindPropertyRelative("_triggerTransform").objectReferenceValue;
            rifle.TriggerPart = trigger.GetComponent<MeshFilter>().sharedMesh.name;
            var main = rifleBaseline.GetComponent<UxrGrabbableObject>();
            var action = rifleBaseline.GetComponentsInChildren<UxrGrabbableObject>(true)
                .First(g => g != main && g.GetComponentsInParent<UxrGrabbableObjectAnchor>(true).Length == 0);
            rifle.ActionPart = action.GetComponentsInChildren<MeshFilter>(true)
                .First(f => f.sharedMesh != null && AssetDatabase.GetAssetPath(f.sharedMesh).EndsWith("Hands_Automatic_Rifle03.FBX", StringComparison.OrdinalIgnoreCase)).sharedMesh.name;
            var pump = HandsPackWeaponBuilder.ShotgunReal;
            pump.ShotAudio = null; pump.LoadAudio = null; // исходная игровая конфигурация звука из immutable baseline
            // Второй патрон — prop FPS-руки в Recharge, а не часть корпуса и не второй gameplay magazine.
            // Live probe: в bind/Shot он выше корпуса; Recharge переносит его из руки к окну заряжания.
            pump.ExcludedSourceParts = new[] { "Shogun_Patron_mesh001" };

            Configure(gun, Gun, GunMag, ids);
            Configure(rifle, Rifle, RifleMag, ids);
            Configure(pump, Pump, PumpMag, ids);
            return new[] { gun, rifle, pump };
        }

        private static void Configure(HandsPackWeaponRecipe recipe, string weapon, string magazine, Dictionary<string, uint> ids)
        {
            GameObject baseline = AssetDatabase.LoadAssetAtPath<GameObject>(Donor(weapon));
            var grab = baseline.GetComponent<UxrGrabbableObject>();
            recipe.UxrTag = grab.Tag;
            recipe.MagazineName = Path.GetFileNameWithoutExtension(magazine);
            recipe.MagazineBase = Donor(magazine);
            recipe.MagazineGripDonor = Donor(GunMag);
            recipe.MagazineTag = AssetDatabase.LoadAssetAtPath<GameObject>(Donor(magazine)).GetComponent<UxrGrabbableObject>().Tag;
            recipe.MagazineCapacity = AssetDatabase.LoadAssetAtPath<GameObject>(Donor(magazine)).GetComponent<UxrFirearmMag>().Capacity;
            recipe.GripDonor = Donor(recipe.GripDonor);
            recipe.FirearmDonor = Donor(weapon);
            recipe.PreserveDonorGameplay = true;
            recipe.NetworkAssetId = ids[weapon]; recipe.MagazineAssetId = ids[magazine];
            Transform body = baseline.transform.Find("MeshContainer/" + recipe.BodyObjectName);
            if (body == null) throw new InvalidOperationException($"Нет корпуса в baseline: {weapon}");
            Vector3 scale = body.lossyScale;
            if (Mathf.Abs(scale.x - scale.y) > 0.0001f || Mathf.Abs(scale.y - scale.z) > 0.0001f)
                throw new InvalidOperationException($"{weapon}: baseline корпуса имеет неравномерный масштаб");
            using var source = new HandsPackWeapon(recipe.Folder, recipe.BodyPart);
            // Имена костей/mesh subassets из FBX не являются списком импортированных renderer parts.
            // Остаток вычисляется по фактическому IWeaponModel: ни догадок, ни пропущенных деталей.
            recipe.StaticParts = source.Parts.Except(new[] { recipe.BodyPart, recipe.TriggerPart, recipe.ActionPart, recipe.MagazinePart }
                .Concat(recipe.ActionExtraParts ?? Array.Empty<string>()).Concat(recipe.MagazineExtraParts ?? Array.Empty<string>())
                .Concat(recipe.ExcludedSourceParts ?? Array.Empty<string>()))
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();
            ValidateSourceParts(recipe, source);
            Vector3 bounds = source.MeshOf(recipe.BodyPart).bounds.size;
            recipe.TargetLength = Mathf.Max(bounds.x, bounds.y, bounds.z) * scale.x;
            // Геометрия/хваты рассчитываются в immutable donor frame, затем меняются только два корня.
            recipe.FinalRootScale = recipe.Name == "AR15" ? Ar15ScaleCalibration.RootScale : 0f;
        }

        private static IEnumerable<string> Parts(HandsPackWeaponRecipe r) => new[] { r.BodyPart, r.TriggerPart, r.ActionPart, r.MagazinePart }
            .Concat(r.StaticParts ?? Array.Empty<string>()).Concat(r.ActionExtraParts ?? Array.Empty<string>())
            .Concat(r.MagazineExtraParts ?? Array.Empty<string>());

        private static void ValidateSourceParts(HandsPackWeaponRecipe recipe, HandsPackWeapon source)
        {
            string[] available = source.Parts.OrderBy(p => p, StringComparer.Ordinal).ToArray();
            string[] required = Parts(recipe).Concat(new[] { recipe.LoadingPart, recipe.MuzzlePart, recipe.ActionGripPart })
                .Concat(recipe.ExcludedSourceParts ?? Array.Empty<string>()).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();
            string[] missing = required.Except(available).ToArray();
            if (missing.Length != 0)
                throw new InvalidOperationException($"{recipe.Name}: source {source.ModelPath}; missing=[{string.Join(",", missing)}]; " +
                    $"available renderer parts=[{string.Join(",", available)}]");
            string[] overlap = Parts(recipe).Intersect(recipe.ExcludedSourceParts ?? Array.Empty<string>()).ToArray();
            if (overlap.Length != 0)
                throw new InvalidOperationException($"{recipe.Name}: source parts одновременно assembled/excluded=[{string.Join(",", overlap)}]");
            string[] unclaimed = available.Except(Parts(recipe).Concat(recipe.ExcludedSourceParts ?? Array.Empty<string>())).ToArray();
            if (unclaimed.Length != 0)
                throw new InvalidOperationException($"{recipe.Name}: source {source.ModelPath}; unclaimed renderer parts=[{string.Join(",", unclaimed)}]");
            foreach (string part in required) source.MeshOf(part);
        }

        private static WeaponMechanismMotion ExportMotion(HandsPackWeaponRecipe r, HandsPackWeapon source)
        {
            string[] parts = Parts(r).Except(new[] { r.BodyPart, r.MagazinePart }).Distinct().ToArray();
            var result = ScriptableObject.CreateInstance<WeaponMechanismMotion>();
            try
            {
                var fire = KinemationAnimationExporter.Sample(source, source.Clip(r.ActionClip), parts, source.PartIdentity, false, true);
                if (r.Action == HandsPackWeaponRecipe.ActionKind.Pump)
                {
                    result.Manual = fire;
                    // Pump-cycle из FPS-пака не досылает патрон за игрока. Его Gate/цевьё сохраняются в Manual.
                    result.Fire = KinemationAnimationExporter.Sample(source, source.Clip(r.ActionClip), new[] { r.TriggerPart }, source.PartIdentity, false, true);
                }
                else result.Fire = fire;
                if (r.Name == "BrowningHiPower")
                    result.Empty = KinemationAnimationExporter.Sample(source, source.Clip("Shot_end"), parts, source.PartIdentity, true, true);
                string folder = $"{MotionRoot}/{r.Name}";
                KinemationWeapon.EnsureFolder(folder);
                string path = $"{folder}/MechanismMotion.asset";
                var saved = AssetDatabase.LoadAssetAtPath<WeaponMechanismMotion>(path);
                if (saved == null) { AssetDatabase.CreateAsset(result, path); return result; }
                EditorUtility.CopySerialized(result, saved); EditorUtility.SetDirty(saved);
                return saved;
            }
            finally
            {
                source.Sample(null);
                if (!AssetDatabase.Contains(result)) Object.DestroyImmediate(result);
            }
        }

        private static void ApplyMagazine(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                WeaponInteractionInstaller.ApplyMagazine(root);
                GameTagsTool.ApplyToHierarchy(root, path, new GameTagsTool.Result());
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void InstallMotion(string path, HandsPackWeaponRecipe r, HandsPackWeapon source)
        {
            WeaponMechanismMotion motion = AssetDatabase.LoadAssetAtPath<WeaponMechanismMotion>($"{MotionRoot}/{r.Name}/MechanismMotion.asset");
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var interaction = WeaponInteractionRecipes.For(root, r);
                WeaponInteractionInstaller.Apply(root, interaction);
                Transform body = root.transform.Find("MeshContainer/" + r.BodyObjectName);
                var action = root.transform.Find(r.Action == HandsPackWeaponRecipe.ActionKind.Pump ? "Pump" : "Slide").GetComponent<UxrGrabbableObject>();
                if (!AutomaticWeaponSlideFeedback.TryGetSlideTravel(action, out _, out float originalLength) ||
                    float.IsNaN(originalLength) || float.IsInfinity(originalLength))
                    throw new InvalidOperationException($"{r.Name}: rebuilt action не содержит корректного ручного хода");
                var anchor = root.GetComponentInChildren<UxrGrabbableObjectAnchor>(true);
                var tracks = (motion.Fire?.Tracks ?? Array.Empty<WeaponMechanismMotion.Track>())
                    .Concat(motion.Empty?.Tracks ?? Array.Empty<WeaponMechanismMotion.Track>()).GroupBy(t => t.Part).Select(g => g.First()).ToArray();
                var bindings = new List<WeaponMechanismVisuals.Binding>();
                foreach (var track in tracks)
                {
                    string name = track.Part == r.TriggerPart ? "Trigger" : HandsPackWeaponBuilder.Clean(track.Part);
                    Transform target = root.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == name).transform;
                    if (target.GetComponent<Collider>() != null)
                    {
                        var visual = new GameObject("MechanismVisual").transform;
                        visual.SetParent(target, false);
                        visual.gameObject.AddComponent<MeshFilter>().sharedMesh = target.GetComponent<MeshFilter>().sharedMesh;
                        visual.gameObject.AddComponent<MeshRenderer>().sharedMaterials = target.GetComponent<MeshRenderer>().sharedMaterials;
                        target.GetComponent<MeshRenderer>().enabled = false;
                        target = visual;
                    }
                    bindings.Add(new WeaponMechanismVisuals.Binding { Part = track.Part, Target = target, RestPosition = target.localPosition,
                        RestRotation = target.localRotation, Rotate = track.AnimateRotation });
                }
                if (motion.Empty != null && AutomaticWeaponSlideFeedback.TryGetSlideTravel(action, out Vector3 direction, out _))
                {
                    float length = originalLength;
                    source.Sample(null);
                    foreach (var track in motion.Empty.Tracks)
                    {
                        var binding = bindings.Single(b => b.Part == track.Part);
                        if (!binding.Target.IsChildOf(action.transform)) continue;
                        Vector3 rest = source.PartInBody(track.Part).GetColumn(3);
                        foreach (var key in track.Keys)
                            length = Mathf.Max(length, Vector3.Dot(root.transform.InverseTransformVector(body.TransformVector(key.Position - rest)), direction));
                    }
                    Vector3 travel = direction * (length + 0.004f);
                    action.TranslationLimitsMin = Vector3.Min(Vector3.zero, travel);
                    action.TranslationLimitsMax = Vector3.Max(Vector3.zero, travel);
                }
                var so = new SerializedObject(root.AddComponent<WeaponMechanismVisuals>());
                so.FindProperty("_motion").objectReferenceValue = motion;
                so.FindProperty("_body").objectReferenceValue = body;
                so.FindProperty("_slide").objectReferenceValue = action;
                so.FindProperty("_contactPart").objectReferenceValue = interaction.ActionGripPart;
                so.FindProperty("_magazineAnchor").objectReferenceValue = anchor;
                so.FindProperty("_originalSlideLength").floatValue = originalLength;
                var items = so.FindProperty("_bindings"); items.arraySize = bindings.Count;
                for (int i = 0; i < bindings.Count; i++)
                {
                    var binding = bindings[i]; var item = items.GetArrayElementAtIndex(i);
                    item.FindPropertyRelative("Part").stringValue = binding.Part;
                    item.FindPropertyRelative("Target").objectReferenceValue = binding.Target;
                    item.FindPropertyRelative("RestPosition").vector3Value = binding.RestPosition;
                    item.FindPropertyRelative("RestRotation").quaternionValue = binding.RestRotation;
                    item.FindPropertyRelative("Rotate").boolValue = binding.Rotate;
                    item.FindPropertyRelative("InMagazine").boolValue = false;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                GameTagsTool.ApplyToHierarchy(root, path, new GameTagsTool.Result());
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ValidateReferences(string weapon, string magazine)
        {
            var definition = AssetDatabase.FindAssets("t:WeaponInfo", new[] { "Assets/Data/Weapons" })
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<WeaponInfo>)
                .Single(info => info.WeaponPrefab != null && AssetDatabase.GetAssetPath(info.WeaponPrefab) == weapon);
            if (definition.MagazinePrefab == null || AssetDatabase.GetAssetPath(definition.MagazinePrefab) != magazine)
                throw new InvalidOperationException($"WeaponInfo потерял ссылку на магазин: {weapon}");
            GameObject root = PrefabUtility.LoadPrefabContents(weapon);
            try
            {
                if (root.GetComponentsInChildren<Animator>(true).Length != 0 || root.GetComponentsInChildren<Animation>(true).Length != 0)
                    throw new InvalidOperationException($"Осталась legacy animation: {weapon}");
                if (root.GetComponentsInChildren<NetworkIdentity>(true).Length != 1)
                    throw new InvalidOperationException($"Вложенный NetworkIdentity: {weapon}");
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root) != 0)
                    throw new InvalidOperationException($"Missing script: {weapon}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
