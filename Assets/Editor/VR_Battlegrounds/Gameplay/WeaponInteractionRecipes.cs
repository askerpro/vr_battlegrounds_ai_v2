using System;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>Явные семантические объёмы хвата; точки ладони служат предложением центра на корпусе.</summary>
    public static class WeaponInteractionRecipes
    {
        public static WeaponInteractionRecipe For(GameObject root, HandsPackWeaponRecipe source = null)
        {
            var grab = root.GetComponent<UxrGrabbableObject>();
            var avatar = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("b6fe59db941fa944696ece5e1aabc032")).GetComponent<UxrAvatar>();
            Transform body = root.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && !f.name.StartsWith("GrabHighlight") &&
                            f.GetComponentInParent<UxrGrabbableObject>() == grab)
                .OrderByDescending(f => f.sharedMesh.vertexCount).First().transform;
            bool pistol = grab.Tag == "Gun";
            WeaponVisualRegion Region(string name, Transform part, Vector3 point, Vector3 size) => new()
            {
                Name = name, Source = part,
                Bounds = new Bounds(NearestSurface(root.transform, part, point), size)
            };
            Vector3 Grip(int index) => root.transform.InverseTransformPoint(grab.GetGrabPoint(index)
                .GetGripPoseInfo(avatar).GripAlignTransformHandRight.position);
            var recipe = new WeaponInteractionRecipe
            {
                Name = root.name.Replace("(Clone)", ""),
                AssetFolder = $"Assets/Art/Weapons/Interaction/{root.name.Replace("(Clone)", "")}",
                Primary = Region("Primary", body, Grip(0), new Vector3(0.075f, 0.115f, 0.065f))
            };
            if (grab.GrabPointCount > 1)
                recipe.Support = pistol
                    ? new WeaponVisualRegion { Name = "Support", Source = body, Bounds = new Bounds(recipe.Primary.Bounds.center + new Vector3(0f, -0.015f, 0.02f), new Vector3(0.075f, 0.085f, 0.065f)) }
                    : Region("Support", body, Grip(1), new Vector3(0.085f, 0.075f, 0.13f));
            UxrGrabbableObject action = root.GetComponentsInChildren<UxrGrabbableObject>(true)
                .FirstOrDefault(g => g != grab && g.GetComponentsInParent<UxrGrabbableObjectAnchor>(true).Length == 0);
            if (action != null)
            {
                string partName = source?.ActionGripPart ?? source?.ActionPart;
                Transform part = action.GetComponentsInChildren<MeshFilter>(true)
                    .Where(f => f.sharedMesh != null && !f.name.StartsWith("GrabHighlight"))
                    .First(f => partName == null || f.name == partName.Replace('.', '_')).transform;
                recipe.ActionGripPart = part;
                recipe.ActionContact = source?.ActionGripContact;
                Vector3 point = root.transform.InverseTransformPoint(part.TransformPoint(
                    recipe.ActionContact ?? part.GetComponent<MeshFilter>().sharedMesh.bounds.center));
                recipe.Action = Region("Action", part, point,
                    action.name == "Pump" ? new Vector3(0.085f, 0.08f, 0.15f) : new Vector3(0.06f, 0.055f, 0.06f));
            }
            var anchor = root.GetComponentInChildren<UxrGrabbableObjectAnchor>(true);
            if (anchor != null)
                recipe.Insertion = Region("Insertion", body, root.transform.InverseTransformPoint(anchor.transform.position), new Vector3(0.085f, 0.055f, 0.085f));
            return recipe;
        }

        private static Vector3 NearestSurface(Transform root, Transform part, Vector3 point)
        {
            Mesh mesh = part.GetComponent<MeshFilter>().sharedMesh;
            Vector3 local = part.InverseTransformPoint(root.TransformPoint(point));
            Vector3 nearest = mesh.vertices.OrderBy(v => (v - local).sqrMagnitude).First();
            Vector3 center = root.InverseTransformPoint(part.TransformPoint(nearest));
            // Ось симметрии оружия: объём охватывает обе стороны рукояти, а не только правую щёчку.
            center.x = root.InverseTransformPoint(part.TransformPoint(mesh.bounds.center)).x;
            return center;
        }
    }
}
