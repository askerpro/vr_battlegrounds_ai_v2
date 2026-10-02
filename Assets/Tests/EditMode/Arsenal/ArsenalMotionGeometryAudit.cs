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
                            walls.Add(wall);
                            names.Add(wall, Path(wall.transform));
                            wallBounds.Add(wall, ColliderBounds(wall));
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
                foreach (ArsenalEquipmentPoses source in stations)
                {
                    var host = new GameObject("Motion audit");
                    SceneManager.MoveGameObjectToScene(host, preview);
                    var equipment = host.AddComponent<ArsenalEquipmentPoses>();
                    var poses = new List<ArsenalEquipmentPoses.PoseTarget>();
                    var boxes = new List<BoxCollider>();
                    int steps = 100;
                    foreach (var pose in source.Targets)
                    {
                        if (pose.Target == null || pose.ClosedPose == null || pose.OpenPose == null)
                        {
                            errors.Add($"{scene.path}/{Path(source.transform)}: неполная конфигурация поз.");
                            continue;
                        }
                        Transform target = CopyGeometry(pose.Target, host.transform, boxes, true);
                        Transform closed = Marker(pose.ClosedPose, host.transform);
                        Transform open = Marker(pose.OpenPose, host.transform);
                        poses.Add(new ArsenalEquipmentPoses.PoseTarget { Target = target, ClosedPose = closed, OpenPose = open });
                        // Smoothstep имеет максимальную производную 1,5; учитываем её в числе шагов.
                        steps = Mathf.Max(steps, Mathf.CeilToInt(1.5f * Vector3.Distance(closed.position, open.position) / .002f));
                        steps = Mathf.Max(steps, Mathf.CeilToInt(1.5f * Quaternion.Angle(closed.rotation, open.rotation) / .25f));
                    }
                    if (poses.Count == 0 || boxes.Count == 0)
                        errors.Add($"{scene.path}/{Path(source.transform)}: нет проверяемой геометрии оборудования.");
                    if (steps > 4096) throw new InvalidOperationException($"{source.name}: ход требует {steps} шагов; расширьте бюджет проверялки.");
                    equipment.Configure(poses.ToArray());
                    // Сфера вокруг каждой группы включает промежуточные повороты, не только конечные AABB.
                    var sweep = new Bounds(source.transform.position, Vector3.zero);
                    foreach (var pose in poses)
                    {
                        float radius = 0f;
                        foreach (var box in boxes)
                        {
                            if (!box.transform.IsChildOf(pose.Target)) continue;
                            var bounds = TransformBounds(box.transform, new Bounds(box.center, box.size));
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
                        foreach (BoxCollider box in boxes)
                        {
                            var bounds = TransformBounds(box.transform, new Bounds(box.center, box.size));
                            foreach (Collider wall in nearbyWalls)
                            {
                                string obstacle = names[wall];
                                if (reported.Contains(obstacle) || !bounds.Intersects(wallBounds[wall])) continue;
                                if (Physics.ComputePenetration(box, box.transform.position, box.transform.rotation,
                                    wall, wall.transform.position, wall.transform.rotation, out _, out float depth) && depth > .001f)
                                {
                                    reported.Add(obstacle);
                                    errors.Add($"{scene.path}/{Path(source.transform)}: {box.name} пересекает {obstacle}, progress={progress:F3}, depth={depth:F4}м");
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

        private static Transform CopyGeometry(Transform source, Transform parent, List<BoxCollider> boxes, bool root)
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
                var box = copy.gameObject.AddComponent<BoxCollider>();
                box.center = mesh.sharedMesh.bounds.center;
                box.size = mesh.sharedMesh.bounds.size;
                boxes.Add(box);
            }
            foreach (Transform child in source) CopyGeometry(child, copy, boxes, false);
            return copy;
        }

        private static string Path(Transform transform)
        {
            return transform.parent == null ? transform.name : Path(transform.parent) + "/" + transform.name;
        }
    }
}
