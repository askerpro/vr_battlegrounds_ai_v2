using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>Узкое обновление восьми KINEMATION из T-39 без сборки из донора и без изменения реестра.</summary>
    public static class KinemationFixReview
    {
        public static void Apply(string name)
        {
            KinemationWeaponRecipe recipe = KinemationWeaponBuilder.All.Single(k => k.Weapon.Name == name);
            ApplyMagazine(recipe);
            using var source = new KinemationWeapon(recipe.PackPrefab, recipe.AnimFolder, recipe.PoseClip, name, recipe.Weapon.BodyPart, recipe.RestClip, recipe.Attachments);
            WeaponMechanismMotion motion = KinemationAnimationExporter.Export(recipe, source);
            string path = KinemationWeaponBuilder.PrefabPath(recipe);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                HandsPackWeaponBuilder.AlignMuzzle(root, recipe.Weapon);
                var interaction = WeaponInteractionRecipes.For(root, recipe.Weapon);
                interaction.AssetFolder = $"{KinemationWeapon.ArtRoot}/{name}/Interaction";
                WeaponInteractionInstaller.Apply(root, interaction);
                var body = root.transform.Find("MeshContainer/Base");
                Transform slideTransform = root.transform.Find("Slide");
                var slide = slideTransform != null ? slideTransform.GetComponent<UxrGrabbableObject>() : null;
                var anchor = root.GetComponentInChildren<UxrGrabbableObjectAnchor>(true);
                var bindings = new List<WeaponMechanismVisuals.Binding>();
                Vector3 originalTravel = slide != null ? root.transform.InverseTransformVector(body.TransformVector(source.Measure(recipe.Weapon.ActionPart, recipe.Weapon.ActionClip).Far)) : Vector3.zero;
                float originalLength = originalTravel.magnitude;
                if (slide != null && motion.Empty == null)
                {
                    slide.TranslationLimitsMin = Vector3.Min(originalTravel, Vector3.zero);
                    slide.TranslationLimitsMax = Vector3.Max(originalTravel, Vector3.zero);
                }
                var cycle = motion.Fire ?? motion.Manual;
                foreach (var track in cycle.Tracks)
                {
                    bool magazine = track.Part == recipe.Weapon.MagazinePart || (recipe.Weapon.MagazineExtraParts ?? Array.Empty<string>()).Contains(track.Part);
                    Transform target = magazine ? anchor.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == (track.Part == recipe.Weapon.MagazinePart ? "Mesh" : track.Part)).transform :
                        root.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == track.Part.Replace('.', '_')).transform;
                    if (target.GetComponent<Collider>() != null)
                    {
                        Transform child = target.Find("MechanismVisual");
                        if (child == null)
                        {
                            child = new GameObject("MechanismVisual").transform; child.SetParent(target, false);
                            child.gameObject.AddComponent<MeshFilter>().sharedMesh = target.GetComponent<MeshFilter>().sharedMesh;
                            child.gameObject.AddComponent<MeshRenderer>().sharedMaterials = target.GetComponent<MeshRenderer>().sharedMaterials;
                            // Существующий коллайдер остаётся на месте. Копия отвечает только за изображение барабана.
                            target.GetComponent<MeshRenderer>().enabled = false;
                        }
                        target = child;
                    }
                    bindings.Add(new WeaponMechanismVisuals.Binding { Part = track.Part, Target = target, RestPosition = target.localPosition,
                        RestRotation = target.localRotation, Rotate = track.AnimateRotation, InMagazine = magazine });
                }
                if (slide != null && motion.Empty != null && AutomaticWeaponSlideFeedback.TryGetSlideTravel(slide, out Vector3 dir, out float length))
                {
                    source.Sample(null);
                    float far = originalLength;
                    foreach (var track in motion.Empty.Tracks)
                    {
                        var binding = bindings.Single(b => b.Part == track.Part);
                        if (!binding.Target.IsChildOf(slide.transform)) continue;
                        Vector3 rest = source.PartInBody(track.Part).GetColumn(3);
                        foreach (var key in track.Keys) far = Mathf.Max(far, Vector3.Dot(root.transform.InverseTransformVector(body.TransformVector(key.Position - rest)), dir));
                    }
                    // Диапазон должен содержать измеренную открытую позу и 4 мм дополнительной ручной тяги.
                    Vector3 travel = dir * (far + 0.004f);
                    slide.TranslationLimitsMin = Vector3.Min(travel, Vector3.zero);
                    slide.TranslationLimitsMax = Vector3.Max(travel, Vector3.zero);
                }
                if (!root.TryGetComponent(out WeaponMechanismVisuals visuals)) visuals = root.AddComponent<WeaponMechanismVisuals>();
                var so = new SerializedObject(visuals);
                so.FindProperty("_motion").objectReferenceValue = motion;
                so.FindProperty("_body").objectReferenceValue = body;
                so.FindProperty("_slide").objectReferenceValue = slide;
                so.FindProperty("_contactPart").objectReferenceValue = interaction.ActionGripPart;
                so.FindProperty("_magazineAnchor").objectReferenceValue = anchor;
                so.FindProperty("_originalSlideLength").floatValue = originalLength;
                var serializedBindings = so.FindProperty("_bindings"); serializedBindings.arraySize = bindings.Count;
                for (int i = 0; i < bindings.Count; i++)
                {
                    var b = bindings[i]; var item = serializedBindings.GetArrayElementAtIndex(i);
                    item.FindPropertyRelative("Part").stringValue = b.Part;
                    item.FindPropertyRelative("Target").objectReferenceValue = b.Target;
                    item.FindPropertyRelative("RestPosition").vector3Value = b.RestPosition;
                    item.FindPropertyRelative("RestRotation").quaternionValue = b.RestRotation;
                    item.FindPropertyRelative("Rotate").boolValue = b.Rotate;
                    item.FindPropertyRelative("InMagazine").boolValue = b.InMagazine;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }

        private static void ApplyMagazine(KinemationWeaponRecipe recipe)
        {
            var weapon = AssetDatabase.LoadAssetAtPath<GameObject>(KinemationWeaponBuilder.PrefabPath(recipe));
            var anchor = weapon.GetComponentInChildren<UxrGrabbableObjectAnchor>(true);
            var nested = anchor.GetComponentInChildren<UxrGrabbableObject>(true);
            string path = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(nested));
            if (string.IsNullOrEmpty(path) || !path.StartsWith($"Assets/Prefabs/Weapons/{recipe.Weapon.PrefabFolder}/"))
                throw new InvalidOperationException($"{recipe.Weapon.Name}: не найден собственный префаб магазина");
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var grab = root.GetComponent<UxrGrabbableObject>();
                Transform mesh = root.transform.Find("Mesh");
                if (recipe.Weapon.Name == "R08")
                {
                    Transform visual = mesh.Find("MechanismVisual");
                    if (visual == null)
                    {
                        visual = new GameObject("MechanismVisual").transform; visual.SetParent(mesh, false);
                        visual.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh.GetComponent<MeshFilter>().sharedMesh;
                        visual.gameObject.AddComponent<MeshRenderer>().sharedMaterials = mesh.GetComponent<MeshRenderer>().sharedMaterials;
                        mesh.GetComponent<MeshRenderer>().enabled = false;
                    }
                    foreach (string dependent in recipe.Weapon.MagazineExtraParts)
                    {
                        Transform part = visual.Find(dependent.Replace('.', '_'));
                        if (part != null) part.SetParent(root.transform, true);
                    }
                    mesh = visual;
                }
                for (int i = 0; i < grab.GrabPointCount; i++)
                    if (grab.GetGrabPoint(i).EnableOnHandNear == null) grab.GetGrabPoint(i).EnableOnHandNear = WeaponGrabHighlight.Create(mesh);
                EditorUtility.SetDirty(grab);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>Совместимый read-only API: результат в логе, без исторического export и без source reimport.</summary>
        public static void Report()
        {
            VrBattlegrounds.Core.GameLog.Arsenal.Info(VrBattlegrounds.Editor.Arsenal.ArsenalWeaponDiagnostics.Capture(KinemationWeaponBuilder.All).Summary);
        }
    }
}
