using System.Linq;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>Узкое обновление контактов и визуалов без пересборки оружия из донора.</summary>
    public static class WeaponInteractionInstaller
    {
        public static void Apply(GameObject root, WeaponInteractionRecipe recipe)
        {
            var main = root.GetComponent<UxrGrabbableObject>();
            GameObject Visual(WeaponVisualRegion region) => WeaponGrabHighlight.Create(region.Source,
                WeaponHighlightRegionBaker.Bake(root.transform, region, recipe.AssetFolder), "GrabHighlight" + region.Name);
            main.GetGrabPoint(0).EnableOnHandNear = Visual(recipe.Primary);
            if (recipe.Support != null) main.GetGrabPoint(1).EnableOnHandNear = Visual(recipe.Support);
            if (recipe.Action != null)
            {
                UxrGrabbableObject action = recipe.ActionGripPart.GetComponentInParent<UxrGrabbableObject>();
                var point = action.GetGrabPoint(0);
                point.EnableOnHandNear = Visual(recipe.Action);
                Vector3 contact = recipe.ActionGripPart.TransformPoint(recipe.ActionContact ?? recipe.ActionGripPart.GetComponent<MeshFilter>().sharedMesh.bounds.center);
                // Позиция переносится, поза пальцев и ориентация каждого аватара сохраняются.
                var so = new SerializedObject(action);
                SerializedProperty iterator = so.GetIterator();
                while (iterator.Next(true))
                {
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference || !iterator.propertyPath.StartsWith("_grabPoint.") ||
                        !(iterator.name == "_gripAlignTransformHandLeft" || iterator.name == "_gripAlignTransformHandRight")) continue;
                    Transform align = iterator.objectReferenceValue as Transform;
                    if (align != null) { align.position = contact; align.SetParent(recipe.ActionGripPart, true); }
                }
                EditorUtility.SetDirty(action);
            }
            if (recipe.Insertion != null)
            {
                var anchor = root.GetComponentInChildren<UxrGrabbableObjectAnchor>(true);
                GameObject visual = Visual(recipe.Insertion);
                // Приёмная область имеет свой материал: один визуал — один писатель, без SDK activation-поля.
                const string materialPath = "Assets/Art/Weapons/Interaction/InsertionHighlight.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    KinemationWeapon.EnsureFolder("Assets/Art/Weapons/Interaction");
                    material = new Material(AssetDatabase.LoadAssetAtPath<Material>(WeaponGrabHighlight.MaterialPath));
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                visual.GetComponentInChildren<MeshRenderer>(true).sharedMaterial = material;
                if (!anchor.TryGetComponent(out WeaponMagazineAnchorHighlight owner)) owner = anchor.gameObject.AddComponent<WeaponMagazineAnchorHighlight>();
                var so = new SerializedObject(owner);
                so.FindProperty("_anchor").objectReferenceValue = anchor;
                so.FindProperty("_visual").objectReferenceValue = visual;
                so.ApplyModifiedPropertiesWithoutUndo();
                if (anchor.ActivateOnCompatibleNear != null) anchor.ActivateOnCompatibleNear.SetActive(false);
                anchor.ActivateOnCompatibleNear = null;
            }
            foreach (var mag in root.GetComponentsInChildren<UxrGrabbableObject>(true).Where(g => g.GetComponentsInParent<UxrGrabbableObjectAnchor>(true).Length > 0))
            {
                MeshFilter mesh = mag.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null && !f.name.StartsWith("GrabHighlight"))
                    .OrderByDescending(f => f.sharedMesh.vertexCount).FirstOrDefault();
                if (mesh == null) continue;
                for (int i = 0; i < mag.GrabPointCount; i++)
                    if (mag.GetGrabPoint(i).EnableOnHandNear == null) mag.GetGrabPoint(i).EnableOnHandNear = WeaponGrabHighlight.Create(mesh.transform);
                EditorUtility.SetDirty(mag);
            }
            foreach (Transform old in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "GrabHighlight").ToArray())
            {
                bool used = root.GetComponentsInChildren<UxrGrabbableObject>(true).Any(g => Enumerable.Range(0, g.GrabPointCount).Any(i =>
                    g.GetGrabPoint(i).EnableOnHandNear != null && old.IsChildOf(g.GetGrabPoint(i).EnableOnHandNear.transform))) ||
                    root.GetComponentsInChildren<WeaponMagazineAnchorHighlight>(true).Any(owner => owner.Visual != null && old.IsChildOf(owner.Visual.transform));
                if (!used) Object.DestroyImmediate(old.gameObject);
            }
            EditorUtility.SetDirty(main);
        }
    }
}
