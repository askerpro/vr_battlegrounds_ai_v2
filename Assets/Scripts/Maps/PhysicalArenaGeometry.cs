using UnityEngine;

namespace VrBattlegrounds.LevelDesign
{
    /// <summary>Габариты чертежа из параметров формы: не зависят от активности GO и Collider.</summary>
    public static class PhysicalArenaGeometry
    {
        public static bool TryBox(Transform transform, Vector3 center, Vector3 size, out Bounds bounds)
        {
            bounds = default;
            if (transform == null || size.x <= 0 || size.y <= 0 || size.z <= 0) return false;
            bounds = new Bounds(transform.TransformPoint(center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        bounds.Encapsulate(transform.TransformPoint(center + Vector3.Scale(size * .5f, new Vector3(x, y, z))));
            return Valid(bounds);
        }

        public static bool TryBounds(Collider collider, out Bounds bounds)
        {
            bounds = default;
            if (collider == null) return false;
            Bounds local;
            if (collider is BoxCollider box) local = new Bounds(box.center, box.size);
            else if (collider is MeshCollider mesh && mesh.sharedMesh != null) local = mesh.sharedMesh.bounds;
            else if (collider is SphereCollider sphere)
            {
                var scale = collider.transform.lossyScale;
                float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                bounds = new Bounds(collider.transform.TransformPoint(sphere.center), Vector3.one * radius * 2);
                return Valid(bounds);
            }
            else if (collider is CapsuleCollider capsule)
            {
                var scale = collider.transform.lossyScale;
                var absolute = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                int axis = capsule.direction;
                float radius = capsule.radius * Mathf.Max(absolute[(axis + 1) % 3], absolute[(axis + 2) % 3]);
                float halfSegment = Mathf.Max(0, capsule.height * absolute[axis] * .5f - radius);
                var direction = collider.transform.rotation * (axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
                var center = collider.transform.TransformPoint(capsule.center);
                bounds = new Bounds(center, Vector3.one * radius * 2);
                bounds.Encapsulate(new Bounds(center + direction * halfSegment, Vector3.one * radius * 2));
                bounds.Encapsulate(new Bounds(center - direction * halfSegment, Vector3.one * radius * 2));
                return Valid(bounds);
            }
            else return false;
            bounds = new Bounds(collider.transform.TransformPoint(local.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        bounds.Encapsulate(collider.transform.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z))));
            return Valid(bounds);
        }

        private static bool Valid(Bounds bounds) => PhysicalArenaDefinition.Finite(bounds.center.x)
            && PhysicalArenaDefinition.Finite(bounds.center.y) && PhysicalArenaDefinition.Finite(bounds.center.z)
            && PhysicalArenaDefinition.Finite(bounds.size.x) && PhysicalArenaDefinition.Finite(bounds.size.y)
            && PhysicalArenaDefinition.Finite(bounds.size.z) && bounds.size.x > 0 && bounds.size.z > 0 && bounds.size.y >= 0;
    }
}
