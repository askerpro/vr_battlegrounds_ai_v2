using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Editor.Avatars;
using VrBattlegrounds.Editor.Gameplay;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>Читает входы полного рецепта до первой записи; не экспортирует меши, motion, звук или позы.</summary>
    internal static class ArsenalBuildPreflight
    {
        public const string InsertionMaterial = "Assets/Art/Weapons/Interaction/InsertionHighlight.mat";
        private const string Mef = "Assets/Prefabs/Player/MEF_Base_Avatar.prefab";
        private const string MagazineGrip = "Assets/Prefabs/Weapons/BrowningHiPower/BrowningHiPower_Magazine.prefab";
        private static readonly string[] Effects = { "Tracer_Default", "Impact_Concrete", "ImpactDecal_Concrete", "Muzzle_Default" };

        /// <summary>Convert ограничивает Android-импорт только исходных BC/Normal текстур выбранного оружия.</summary>
        public static string[] MaterialSettingsPaths(KinemationWeaponRecipe kin)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(KinemationWeapon.Pack + "Prefabs/Weapons/" + kin.PackPrefab + ".prefab");
            if (source == null) return Array.Empty<string>();
            var attachments = new HashSet<string>(kin.Attachments ?? Array.Empty<string>());
            return source.GetComponentsInChildren<Renderer>(true)
                .Where(r => r is SkinnedMeshRenderer || attachments.Contains(r.name)).SelectMany(r => r.sharedMaterials)
                .Where(m => m != null).SelectMany(m => new[] { "_BC", "_Normal" }.Where(m.HasProperty).Select(m.GetTexture))
                .Where(t => t != null).Select(AssetDatabase.GetAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && AssetImporter.GetAtPath(p) is TextureImporter).Select(p => p + ".meta").Distinct().ToArray();
        }

        public static string[] Inputs(WeaponInfo weapon)
        {
            var kin = ArsenalEditorActions.Kinemation(weapon);
            var recipe = kin?.Weapon ?? ArsenalEditorActions.Hands(weapon);
            if (recipe == null) return Array.Empty<string>();
            var paths = new List<string> { AssetDatabase.GetAssetPath(weapon), recipe.GripDonor, recipe.FirearmDonor, recipe.MagazineBase,
                recipe.MagazineGripDonor ?? MagazineGrip, HandsPackWeapon.PackModels + recipe.GripDonorFolder,
                AvatarHandBases.NonSdkHands, Mef, WeaponGrabHighlight.MaterialPath, "Assets/Audio/SFX/Impacts" };
            paths.AddRange(Effects.Select(n => "Assets/Prefabs/Weapons/Effects/" + n + ".prefab"));
            if (kin != null)
            {
                paths.Add(KinemationWeapon.Pack + "Prefabs/Weapons/" + kin.PackPrefab + ".prefab");
                paths.Add(KinemationWeapon.Pack + "Animations/" + kin.AnimFolder);
                paths.Add(KinemationPoseExtractor.Character);
                paths.AddRange(Sounds(kin));
                if (kin.Weapon.Name == "TR15") paths.AddRange(Tr15OpticBuilder.InputPaths());
            }
            else
            {
                paths.Add(HandsPackWeapon.PackModels + recipe.Folder);
                // Существующий material resolver ищет подходящий renderer во всех prefab этого пака.
                paths.Add(HandsPackWeapon.PackPrefabs);
                paths.AddRange(RecipeSounds(recipe));
                paths.AddRange(HandsPackPoseImporter.Recipes.Where(p => p.Weapon == AssetDatabase.GetAssetPath(weapon.WeaponPrefab))
                    .Select(p => HandsPackWeapon.PackModels + p.Folder));
            }
            return paths.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();
        }

        public static string Validate(IEnumerable<WeaponInfo> weapons)
        {
            var selection = weapons.ToArray();
            if (selection.Length == 0) throw new InvalidOperationException("Выберите хотя бы одно оружие.");
            foreach (var weapon in selection)
            {
                var kin = ArsenalEditorActions.Kinemation(weapon);
                var hands = ArsenalEditorActions.Hands(weapon);
                if (kin != null) Validate(kin);
                else if (hands != null) Validate(hands);
                else throw new InvalidOperationException("Нет полного рецепта: " + (weapon != null ? weapon.DisplayName : "пустая запись"));
            }
            return "Входы полной сборки проверены без записи: " + selection.Length + " рецептов.";
        }

        private static IEnumerable<string> Sounds(KinemationWeaponRecipe kin)
        {
            yield return kin.ShotSound;
            if (kin.MagOutSound != null && kin.MagInSound != null) { yield return kin.MagOutSound; yield return kin.MagInSound; }
            else yield return kin.ReloadSound;
            if (kin.Weapon.Action != HandsPackWeaponRecipe.ActionKind.None)
                yield return kin.BoltSound ?? kin.BoltSoundSource ?? kin.ReloadSound;
        }

        private static IEnumerable<string> RecipeSounds(HandsPackWeaponRecipe r) =>
            new[] { r.ShotAudio, r.LoadAudio, r.TakeOutAudio, r.SlideBackAudio, r.SlideForwardAudio }.Where(p => !string.IsNullOrEmpty(p));

        private static IEnumerable<string> Parts(HandsPackWeaponRecipe r) =>
            new[] { r.BodyPart, r.TriggerPart, r.MagazinePart, r.MuzzlePart, r.LoadingPart, r.ActionGripPart,
                r.Action != HandsPackWeaponRecipe.ActionKind.None ? r.ActionPart : null }
            .Concat(r.StaticParts ?? Array.Empty<string>()).Concat(r.ActionExtraParts ?? Array.Empty<string>())
            .Concat(r.MagazineExtraParts ?? Array.Empty<string>()).Where(p => !string.IsNullOrEmpty(p)).Distinct();

        private static void Validate(KinemationWeaponRecipe kin)
        {
            var prefab = ArsenalEditorActions.SourcePrefab(kin);
            KinemationWeapon.RequireReadable(prefab, kin.Attachments);
            ValidateDonors(kin.Weapon);
            foreach (var path in Sounds(kin)) Required<AudioClip>(path);
            using var model = new KinemationWeapon(kin.PackPrefab, kin.AnimFolder, kin.PoseClip, kin.Weapon.Name,
                kin.Weapon.BodyPart, kin.RestClip, kin.Attachments);
            var required = Parts(kin.Weapon).Concat(model.Parts.Where(p => !(kin.Excluded ?? Array.Empty<string>())
                .Any(e => e.EndsWith("*") ? p.StartsWith(e.TrimEnd('*'), StringComparison.Ordinal) : p == e))).Distinct().ToArray();
            foreach (string part in required) ValidateGeometry(model, prefab, part);
            model.Clip(kin.Weapon.ActionClip);
            if (!string.IsNullOrEmpty(kin.RestClip)) model.Clip(kin.RestClip);
            if (kin.MagOutSound == null || kin.MagInSound == null) model.Clip(kin.ReloadClip);
            if (kin.Weapon.Action != HandsPackWeaponRecipe.ActionKind.None && kin.BoltSound == null) model.Clip(kin.BoltClip ?? kin.ReloadClip);
            // Эти три дополнительные канала читает существующий KinemationAnimationExporter.Export.
            string empty = kin.Weapon.Name switch { "Viper" => "A_W_WK-11_Viper_Fire_Empty", "TR15" => "A_W_TR15_Fire_Out", "Herrington" => "A_W_Herrington_11-87_Fire_Out", _ => null };
            if (empty != null) model.Clip(empty);
            model.PoseClip();
            model.GripSample(UxrHandSide.Right);
            model.GripSample(UxrHandSide.Left);
            if (kin.Weapon.Name == "TR15") Tr15OpticBuilder.Preflight();
        }

        private static void ValidateGeometry(KinemationWeapon model, GameObject prefab, string part)
        {
            var identity = model.PartIdentity(part);
            Transform bone = string.IsNullOrEmpty(identity.path) ? prefab.transform : prefab.transform.Find(identity.path);
            if (identity.boneIndex < 0)
            {
                var mesh = bone != null ? bone.GetComponent<MeshFilter>()?.sharedMesh : null;
                RequireMesh(mesh, part);
                return;
            }
            var renderer = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(r => identity.boneIndex < r.bones.Length && r.bones[identity.boneIndex] == bone && r.sharedMesh != null);
            var source = renderer != null ? renderer.sharedMesh : null;
            RequireMesh(source, part);
            var weights = source.boneWeights;
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                var triangles = source.GetTriangles(sub);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = weights[triangles[i]].boneIndex0, b = weights[triangles[i + 1]].boneIndex0, c = weights[triangles[i + 2]].boneIndex0;
                    int majority = a == b || a == c ? a : b == c ? b : a;
                    if (majority == identity.boneIndex) return;
                }
            }
            throw new InvalidOperationException("У обязательной детали нет треугольников: " + part);
        }

        private static void Validate(HandsPackWeaponRecipe recipe)
        {
            ValidateDonors(recipe);
            foreach (var audioPath in RecipeSounds(recipe)) Required<AudioClip>(audioPath);
            using var source = new HandsPackWeapon(recipe.Folder, recipe.BodyPart);
            foreach (var part in Parts(recipe)) RequireMesh(source.MeshOf(part), part);
            source.Clip(recipe.PoseClip); source.Clip(recipe.ActionClip);
            source.AimAxes(recipe.PoseClip);
            var body = HandsPackWeaponBuilder.BodyPlacement(recipe, source);
            if (recipe.Action != HandsPackWeaponRecipe.ActionKind.None) HandsPackWeaponBuilder.ResolveActionTravel(recipe, source, body, out _);
            HandsPackWeaponBuilder.ResolveTriggerRotation(recipe, source.Measure(recipe.TriggerPart, recipe.ActionClip, recipe.RestClip));
            string path = $"Assets/Prefabs/Weapons/{recipe.PrefabFolder}/{recipe.Name}.prefab";
            var poses = HandsPackPoseImporter.Recipes.Where(p => p.Weapon == path).ToArray();
            if (poses.Length == 0) throw new InvalidOperationException("Нет рецептов поз полной сборки: " + recipe.Name);
            foreach (var pose in poses) ValidatePose(pose);
        }

        public static void ValidatePose(HandsPackPoseRecipe pose)
        {
            string folder = HandsPackWeapon.PackModels + pose.Folder;
            var model = Required<GameObject>(folder + "/Mesh/" + pose.Mesh + ".FBX");
            var clip = AssetDatabase.FindAssets("t:AnimationClip", new[] { folder + "/Animations" })
                .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
                .OfType<AnimationClip>().FirstOrDefault(c => c.name == pose.Clip)
                ?? throw new InvalidOperationException("Нет клипа позы: " + pose.Clip + " в " + folder);
            HandsPackPoseExtractor.Extract(model, clip, pose.Time, pose.PackSide, pose.BodyMesh, pose.BodyBone);
        }

        private static void ValidateDonors(HandsPackWeaponRecipe r)
        {
            var mef = Component<UxrAvatar>(Required<GameObject>(Mef));
            Component<UxrAvatar>(Required<GameObject>(AvatarHandBases.NonSdkHands));
            var grip = Required<GameObject>(r.GripDonor);
            Component<Rigidbody>(grip);
            var grab = Component<UxrGrabbableObject>(grip);
            RequireGrip(grab, 0, mef); RequireGrip(grab, 1, mef);
            if (grip.transform.Find(r.GripDonorBodyPath)?.GetComponent<MeshFilter>()?.sharedMesh == null)
                throw new InvalidOperationException("Нет корпуса донора хвата: " + r.GripDonor + "/" + r.GripDonorBodyPath);
            var anchor = grip.GetComponentInChildren<UxrGrabbableObjectAnchor>(true);
            if (anchor == null || anchor.GetComponent<AudioSource>() == null || grip.GetComponentInChildren<AnchorSound>(true) == null)
                throw new InvalidOperationException("Нет якоря/звука донора: " + r.GripDonor);
            using (var donor = new HandsPackWeapon(r.GripDonorFolder, r.GripDonorBody)) { donor.Clip(r.GripDonorPoseClip); donor.AimAxes(r.GripDonorPoseClip); }
            if (r.Action != HandsPackWeaponRecipe.ActionKind.None && r.Action != HandsPackWeaponRecipe.ActionKind.Pump)
            {
                var action = grip.GetComponentsInChildren<UxrGrabbableObject>(true)
                    .FirstOrDefault(g => g != grab && g.GetComponentsInParent<UxrGrabbableObjectAnchor>(true).Length == 0);
                if (action == null) throw new InvalidOperationException("Нет ручной детали донора: " + r.GripDonor);
                // Сборщик переносит null HandPose донора затвора; ему нужны обе точки выравнивания.
                RequireGrip(action, 0, mef, false);
                if (!action.GetComponentsInChildren<MeshFilter>(true).Any(f => f.sharedMesh != null && f.name != "GrabHighlight"))
                    throw new InvalidOperationException("Нет меша ручной детали донора: " + r.GripDonor);
            }
            var fire = Required<GameObject>(r.FirearmDonor);
            var firearm = Component<UxrFirearmWeapon>(fire);
            var projectile = Component<UxrProjectileSource>(fire);
            if (new SerializedObject(firearm).FindProperty("_triggers").arraySize == 0 ||
                new SerializedObject(projectile).FindProperty("_shotTypes").arraySize < (r.PreserveDonorGameplay && (r.Pellets || r.Action == HandsPackWeaponRecipe.ActionKind.Pump) ? 2 : 1))
                throw new InvalidOperationException("Донор не содержит спуска/типов выстрела: " + r.FirearmDonor);
            var magazine = Required<GameObject>(r.MagazineBase);
            Component<Mirror.NetworkIdentity>(magazine); Component<Rigidbody>(magazine);
            Component<UxrGrabbableObject>(magazine); Component<UxrFirearmMag>(magazine);
            var magGrip = Required<GameObject>(r.MagazineGripDonor ?? MagazineGrip);
            RequireGrip(Component<UxrGrabbableObject>(magGrip), 0, mef);
            if (!magGrip.GetComponentsInChildren<MeshFilter>(true).Any(f => f.sharedMesh != null))
                throw new InvalidOperationException("Нет меша донора хвата магазина: " + magGrip.name);
            if (!r.PreserveDonorGameplay) foreach (var effect in Effects) Required<GameObject>("Assets/Prefabs/Weapons/Effects/" + effect + ".prefab");
            Required<Material>(WeaponGrabHighlight.MaterialPath);
        }

        private static void RequireGrip(UxrGrabbableObject grab, int index, UxrAvatar avatar, bool requirePose = true)
        {
            if (grab.GrabPointCount <= index) throw new InvalidOperationException("Нет точки хвата " + index + " у " + grab.name);
            var pose = grab.GetGrabPoint(index).GetGripPoseInfo(avatar);
            if (pose == null || (requirePose && pose.HandPose == null) || pose.GripAlignTransformHandLeft == null || pose.GripAlignTransformHandRight == null)
                throw new InvalidOperationException("Неполный хват MEF у " + grab.name + ", точка " + index);
        }

        private static void RequireMesh(Mesh mesh, string part)
        {
            if (mesh == null || mesh.vertexCount == 0 || !Enumerable.Range(0, mesh.subMeshCount).Any(i => mesh.GetIndexCount(i) > 0))
                throw new InvalidOperationException("Нет геометрии обязательной детали: " + part);
        }

        private static T Required<T>(string path) where T : UnityEngine.Object =>
            !string.IsNullOrEmpty(path) ? AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Нет входного ассета: " + path) :
            throw new InvalidOperationException("Не задан обязательный входной ассет " + typeof(T).Name);
        private static T Component<T>(GameObject root) where T : UnityEngine.Component =>
            root.GetComponent<T>() ?? throw new InvalidOperationException("Нет " + typeof(T).Name + " у донора " + root.name);
    }
}
