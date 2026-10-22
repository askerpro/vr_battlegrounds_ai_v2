using System.Collections.Generic;
using System.Linq;
using TMPro;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>Геометрическая видимость, независимо от экономики, owner и включения текста в EditMode.</summary>
    public static class ArsenalVisibilityGeometryAudit
    {
        private const float SurfaceTolerance = .002f;

        public static List<string> Audit(Scene scene)
        {
            var errors = new List<string>();
            foreach (var station in scene.GetRootGameObjects()
                         .SelectMany(r => r.GetComponentsInChildren<ArsenalWallController>(true)))
            {
                var wallet = station.GetComponentInChildren<ArsenalWalletDisplay>(true);
                var tag = station.GetComponentInChildren<DogTagController>(true);
                var display = wallet == null ? null : new SerializedObject(wallet)
                    .FindProperty("_display").objectReferenceValue as TextMeshPro;
                if (display == null) errors.Add($"{scene.path}/{station.name}: не назначено табло.");
                else Check(scene, station.transform, display.transform, "табло", DisplayPoints(display), errors);
                if (tag == null || tag.TagAnchor == null || tag.TagPrefab == null)
                    errors.Add($"{scene.path}/{station.name}: у панели не назначен крючок или префаб жетона.");
                else if (!TryTagCenter(tag, out Vector3 center))
                    errors.Add($"{scene.path}/{station.name}: у жетона нет видимого меша.");
                else Check(scene, station.transform, tag.TagAnchor.transform, "жетон", new[] { center }, errors);
            }
            return errors;
        }

        /// <summary>
        /// Центр видимых мешей жетона, повешенного на крючок. Жетона в сцене нет — его выдаёт стена в рантайме
        /// (<c>DogTagController.BindTag</c>: поза крючка, масштаб префаба), поэтому меши берутся из префаба.
        /// </summary>
        private static bool TryTagCenter(DogTagController tag, out Vector3 center)
        {
            center = default;
            Transform root = tag.TagPrefab.transform;
            Matrix4x4 rack = tag.TagAnchor.transform.localToWorldMatrix * Matrix4x4.Scale(root.localScale);
            bool any = false;
            Bounds bounds = default;
            foreach (var renderer in tag.TagPrefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!renderer.enabled || !renderer.gameObject.activeSelf || filter == null || filter.sharedMesh == null) continue;
                Matrix4x4 toWorld = rack * (root.worldToLocalMatrix * renderer.transform.localToWorldMatrix);
                Bounds local = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 world = toWorld.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(world, Vector3.zero); any = true; }
                    else bounds.Encapsulate(world);
                }
            }
            if (any) center = bounds.center;
            return any;
        }

        private static Vector3[] DisplayPoints(TextMeshPro display)
        {
            var rect = display.rectTransform.rect;
            return new[]
            {
                display.transform.TransformPoint(rect.center),
                display.transform.TransformPoint(rect.center + new Vector2(-rect.width, -rect.height) * .35f),
                display.transform.TransformPoint(rect.center + new Vector2(rect.width, -rect.height) * .35f),
                display.transform.TransformPoint(rect.center + new Vector2(-rect.width, rect.height) * .35f),
                display.transform.TransformPoint(rect.center + new Vector2(rect.width, rect.height) * .35f)
            };
        }

        private static void Check(Scene scene, Transform station, Transform target, string label,
            IEnumerable<Vector3> points, List<string> errors)
        {
            // Стоим перед собственной станцией: рост и небольшое смещение тела не должны скрывать цель.
            foreach (float height in new[] { 1.45f, 1.7f, 1.95f })
            foreach (float lateral in new[] { -.25f, 0f, .25f })
            foreach (var point in points)
            {
                Vector3 eye = station.TransformPoint(new Vector3(lateral, height, -1f));
                string blocker = FirstBlocker(scene, eye, point, target);
                if (blocker == null) continue;
                errors.Add($"{scene.path}/{Path(station)}: {label} скрыто {blocker}; высота={height:F2}, смещение={lateral:F2}м.");
                return;
            }
        }

        /// <summary>Луч по треугольникам видимых мешей: работает также для стены без коллайдера.</summary>
        public static string FirstBlocker(Scene scene, Vector3 eye, Vector3 target, Transform excludedTarget = null)
        {
            Vector3 delta = target - eye;
            float nearest = delta.magnitude - SurfaceTolerance;
            if (nearest <= 0f) return null;
            var ray = new Ray(eye, delta.normalized);
            string blocker = null;
            foreach (var renderer in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshRenderer>(true)))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    (excludedTarget != null && renderer.transform.IsChildOf(excludedTarget)) ||
                    renderer.GetComponentInParent<UxrGrabbableObject>(true) != null ||
                    renderer.GetComponentInParent<Rigidbody>(true) != null ||
                    renderer.GetComponentInParent<Animator>(true) != null) continue;
                if (!renderer.bounds.IntersectRay(ray, out float boundsDistance) || boundsDistance > nearest) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var mesh = filter.sharedMesh;
                if (!mesh.isReadable)
                {
                    // Нечитаемая геометрия не должна давать ложный зелёный результат.
                    throw new System.InvalidOperationException($"{scene.path}/{Path(renderer.transform)}: меш {mesh.name} недоступен для проверки видимости.");
                }
                Vector3 origin = renderer.transform.InverseTransformPoint(eye);
                Vector3 direction = renderer.transform.InverseTransformVector(ray.direction);
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    float distance = TriangleDistance(origin, direction, vertices[triangles[i]],
                        vertices[triangles[i + 1]], vertices[triangles[i + 2]]);
                    if (distance <= SurfaceTolerance || distance >= nearest) continue;
                    nearest = distance;
                    blocker = Path(renderer.transform);
                }
            }
            return blocker;
        }

        private static float TriangleDistance(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a;
            Vector3 cross = Vector3.Cross(direction, ac);
            float determinant = Vector3.Dot(ab, cross);
            if (Mathf.Abs(determinant) < 1e-8f) return float.PositiveInfinity;
            float inverse = 1f / determinant;
            Vector3 offset = origin - a;
            float u = Vector3.Dot(offset, cross) * inverse;
            if (u < 0f || u > 1f) return float.PositiveInfinity;
            Vector3 q = Vector3.Cross(offset, ab);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0f || u + v > 1f) return float.PositiveInfinity;
            return Vector3.Dot(ac, q) * inverse;
        }

        private static string Path(Transform transform)
        {
            return transform.parent == null ? transform.name : Path(transform.parent) + "/" + transform.name;
        }
    }
}
