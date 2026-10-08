using System;
using System.Collections.Generic;
using UltimateXR.Manipulation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>Проверка хода на копии: шаг не более 2 мм или 0,25°; исходные объекты не изменяются.</summary>
    public static class ArsenalMotionGeometryAudit
    {
        public static IEnumerable<string> Audit(Scene scene)
        {
            var errors = new List<string>();
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var walls = new List<Collider>();
                var names = new Dictionary<Collider, string>();
                var wallBounds = new Dictionary<Collider, Bounds>();
                var stations = new List<ArsenalEquipmentPoses>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    stations.AddRange(root.GetComponentsInChildren<ArsenalEquipmentPoses>(true));
                    foreach (Collider wall in root.GetComponentsInChildren<Collider>(true))
                        if (IsStaticObstacle(wall.transform) && wall.enabled && !wall.isTrigger)
                        {
                            // ComputePenetration не сравнивает тела из разных preview physics scenes.
                            // Видимый невыпуклый меш также не заменяет solid collider при полном вложении.
                            Collider obstacle = CopyCollider(wall, preview);
                            walls.Add(obstacle);
                            names.Add(obstacle, Path(wall.transform));
                            wallBounds.Add(obstacle, ColliderBounds(obstacle));
                        }
                    // Отсутствие физического коллайдера не разрешает полке пересекать видимую стену.
                    foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (!renderer.enabled || !IsStaticObstacle(renderer.transform)) continue;
                        var filter = renderer.GetComponent<MeshFilter>();
                        if (filter == null || filter.sharedMesh == null) continue;
                        // Проекция границы — нематериальная поверхность, не постоянная стена.
                        if (renderer.GetComponentInParent<VrBattlegrounds.Maps.SpawnZoneBoundaryVisual>(true) != null) continue;
                        var clone = new GameObject("Static mesh probe");
                        SceneManager.MoveGameObjectToScene(clone, preview);
                        clone.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                        clone.transform.localScale = renderer.transform.lossyScale;
                        var meshCollider = clone.AddComponent<MeshCollider>();
                        meshCollider.sharedMesh = filter.sharedMesh;
                        walls.Add(meshCollider);
                        names.Add(meshCollider, Path(renderer.transform));
                        wallBounds.Add(meshCollider, TransformBounds(clone.transform, filter.sharedMesh.bounds));
                    }
                }
                Physics.SyncTransforms();
                foreach (Collider wall in walls) wallBounds[wall] = ColliderBounds(wall);
                foreach (ArsenalEquipmentPoses source in stations)
                {
                    var host = new GameObject("Motion audit");
                    SceneManager.MoveGameObjectToScene(host, preview);
                    var equipment = host.AddComponent<ArsenalEquipmentPoses>();
                    var poses = new List<ArsenalEquipmentPoses.PoseTarget>();
                    var geometry = new List<MeshCollider>();
                    int steps = 100;
                    foreach (var pose in source.Targets)
                    {
                        if (pose.Target == null || pose.ClosedPose == null || pose.OpenPose == null)
                        {
                            errors.Add($"{scene.path}/{Path(source.transform)}: неполная конфигурация поз.");
                            continue;
                        }
                        Transform target = CopyGeometry(pose.Target, host.transform, geometry, true);
                        Transform closed = Marker(pose.ClosedPose, host.transform);
                        Transform open = Marker(pose.OpenPose, host.transform);
                        poses.Add(new ArsenalEquipmentPoses.PoseTarget { Target = target, ClosedPose = closed, OpenPose = open });
                        // Smoothstep имеет максимальную производную 1,5; учитываем её в числе шагов.
                        steps = Mathf.Max(steps, Mathf.CeilToInt(1.5f * Vector3.Distance(closed.position, open.position) / .002f));
                        steps = Mathf.Max(steps, Mathf.CeilToInt(1.5f * Quaternion.Angle(closed.rotation, open.rotation) / .25f));
                    }
                    if (poses.Count == 0 || geometry.Count == 0)
                        errors.Add($"{scene.path}/{Path(source.transform)}: нет проверяемой геометрии оборудования.");
                    if (steps > 4096) throw new InvalidOperationException($"{source.name}: ход требует {steps} шагов; расширьте бюджет проверялки.");
                    equipment.Configure(poses.ToArray());
                    // Сфера вокруг каждой группы включает промежуточные повороты, не только конечные AABB.
                    var sweep = new Bounds(source.transform.position, Vector3.zero);
                    foreach (var pose in poses)
                    {
                        float radius = 0f;
                        foreach (var shape in geometry)
                        {
                            if (!shape.transform.IsChildOf(pose.Target)) continue;
                            var bounds = ColliderBounds(shape);
                            radius = Mathf.Max(radius, Vector3.Distance(pose.Target.position, bounds.center) + bounds.extents.magnitude);
                        }
                        sweep.Encapsulate(new Bounds(pose.ClosedPose.position, Vector3.one * radius * 2f));
                        sweep.Encapsulate(new Bounds(pose.OpenPose.position, Vector3.one * radius * 2f));
                    }
                    var nearbyWalls = new List<Collider>();
                    foreach (var wall in walls) if (sweep.Intersects(wallBounds[wall])) nearbyWalls.Add(wall);
                    var reported = new HashSet<string>();
                    for (int sample = 0; sample <= steps; sample++)
                    {
                        float progress = sample / (float)steps;
                        equipment.Apply(progress);
                        foreach (MeshCollider shape in geometry)
                        {
                            var bounds = ColliderBounds(shape);
                            foreach (Collider wall in nearbyWalls)
                            {
                                string obstacle = names[wall];
                                if (reported.Contains(obstacle) || !bounds.Intersects(wallBounds[wall])) continue;
                                if (Physics.ComputePenetration(shape, shape.transform.position, shape.transform.rotation,
                                    wall, wall.transform.position, wall.transform.rotation, out _, out float depth) && depth > .001f)
                                {
                                    reported.Add(obstacle);
                                    errors.Add($"{scene.path}/{Path(source.transform)}: {shape.name} пересекает {obstacle}, progress={progress:F3}, depth={depth:F4}м");
                                }
                            }
                        }
                    }
                    Object.DestroyImmediate(host);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            return errors;
        }

        private static bool IsStaticObstacle(Transform transform)
        {
            return transform.gameObject.activeInHierarchy &&
                transform.GetComponentInParent<Rigidbody>(true) == null &&
                transform.GetComponentInParent<UxrGrabbableObject>(true) == null &&
                transform.GetComponentInParent<Animator>(true) == null &&
                transform.GetComponentInParent<ArsenalEquipmentPoses>(true) == null;
        }

        private static Collider CopyCollider(Collider source, Scene preview)
        {
            var clone = new GameObject("Static collider probe");
            SceneManager.MoveGameObjectToScene(clone, preview);
            clone.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            clone.transform.localScale = source.transform.lossyScale;
            switch (source)
            {
                case BoxCollider box:
                    var boxCopy = clone.AddComponent<BoxCollider>();
                    boxCopy.center = box.center;
                    boxCopy.size = box.size;
                    return boxCopy;
                case SphereCollider sphere:
                    var sphereCopy = clone.AddComponent<SphereCollider>();
                    sphereCopy.center = sphere.center;
                    sphereCopy.radius = sphere.radius;
                    return sphereCopy;
                case CapsuleCollider capsule:
                    var capsuleCopy = clone.AddComponent<CapsuleCollider>();
                    capsuleCopy.center = capsule.center;
                    capsuleCopy.radius = capsule.radius;
                    capsuleCopy.height = capsule.height;
                    capsuleCopy.direction = capsule.direction;
                    return capsuleCopy;
                case MeshCollider mesh:
                    var meshCopy = clone.AddComponent<MeshCollider>();
                    meshCopy.cookingOptions = mesh.cookingOptions;
                    meshCopy.sharedMesh = mesh.sharedMesh;
                    meshCopy.convex = mesh.convex;
                    return meshCopy;
                case TerrainCollider terrain:
                    var terrainCopy = clone.AddComponent<TerrainCollider>();
                    terrainCopy.terrainData = terrain.terrainData;
                    return terrainCopy;
                default:
                    throw new InvalidOperationException($"{Path(source.transform)}: непроверяемая форма {source.GetType().Name}.");
            }
        }

        private static Bounds ColliderBounds(Collider collider)
        {
            var box = collider as BoxCollider;
            if (box != null) return TransformBounds(box.transform, new Bounds(box.center, box.size));
            var mesh = collider as MeshCollider;
            if (mesh != null && mesh.sharedMesh != null) return TransformBounds(mesh.transform, mesh.sharedMesh.bounds);
            // Для остальных форм bounds заполняется физикой; нужны актуальные Transform перед запуском.
            return collider.bounds;
        }

        private static Bounds TransformBounds(Transform transform, Bounds local)
        {
            Vector3 x = transform.TransformVector(Vector3.right * local.extents.x);
            Vector3 y = transform.TransformVector(Vector3.up * local.extents.y);
            Vector3 z = transform.TransformVector(Vector3.forward * local.extents.z);
            Vector3 extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(transform.TransformPoint(local.center), extents * 2f);
        }

        private static Transform Marker(Transform source, Transform parent)
        {
            var marker = new GameObject(source.name).transform;
            marker.SetParent(parent, false);
            marker.SetPositionAndRotation(source.position, source.rotation);
            return marker;
        }

        private static Transform CopyGeometry(Transform source, Transform parent, List<MeshCollider> geometry, bool root)
        {
            var copy = new GameObject(source.name).transform;
            copy.SetParent(parent, false);
            if (root)
            {
                copy.SetPositionAndRotation(source.position, source.rotation);
                copy.localScale = source.lossyScale;
            }
            else
            {
                copy.localPosition = source.localPosition;
                copy.localRotation = source.localRotation;
                copy.localScale = source.localScale;
            }
            MeshFilter mesh = source.GetComponent<MeshFilter>();
            Renderer renderer = source.GetComponent<Renderer>();
            if (source.gameObject.activeInHierarchy && mesh != null && mesh.sharedMesh != null &&
                renderer != null && renderer.enabled && source.GetComponentInParent<UxrGrabbableObject>(true) == null)
            {
                // Bounds оставляем для отсечения кандидатов; контакт проверяем выпуклой оболочкой меша.
                // Непустой угол AABB не обязан принадлежать геометрии оборудования.
                var shape = copy.gameObject.AddComponent<MeshCollider>();
                shape.sharedMesh = mesh.sharedMesh;
                shape.convex = true;
                geometry.Add(shape);
            }
            foreach (Transform child in source) CopyGeometry(child, copy, geometry, false);
            return copy;
        }

        private static string Path(Transform transform)
        {
            return transform.parent == null ? transform.name : Path(transform.parent) + "/" + transform.name;
        }
    }
}
