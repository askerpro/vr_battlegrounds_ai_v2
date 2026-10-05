using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Maps;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Снимок геометрии в инспекторе. Обе временные сцены закрываются до возвращения текстуры.</summary>
    public static class MapGrowthCandidatePreview
    {
        public static Texture2D Render(MapGrowthSceneCapture capture, MapGrowthCandidateResult result, bool isometric = false)
        {
            MapGrowthSnapshotBuilder.RequireMainThread();
            if (capture == null || !capture.IsCurrent() || result == null || result.InputVersion != capture.InputVersion)
                throw new InvalidOperationException("Новый геометрический предпросмотр требует актуального входа. Сохранённые изображение и отчёт доступны после изменения карты.");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Для предпросмотра отсутствует URP/Lit.");
            var renderer = new PreviewRenderUtility();
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var temporaryMeshes = new List<Mesh>();
            Texture2D texture = null; bool opened = false, rendered = false;
            try
            {
                using (var preview = capture.CreateFixedPreview())
                {
                    foreach (var recipe in result.Recipes)
                        BlockoutRegistryFactory.CreatePreview(BlockoutRegistryFactory.Current.Definitions.Single(d => d != null && d.shapeId == recipe.ShapeId), preview.Scene, recipe, capture.AuthorCell);
                    var colliders = preview.Scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider>(false)).Where(BlockoutSupportSurfaces.IsActiveSolid).ToArray();
                    if (colliders.Length == 0) throw new InvalidOperationException("Вариант не содержит геометрии.");
                    var bounds = colliders[0].bounds; foreach (var c in colliders.Skip(1)) bounds.Encapsulate(c.bounds);
                    var camera = renderer.camera; camera.orthographic = true; camera.aspect = 1;
                    camera.allowHDR = false; camera.useOcclusionCulling = false; camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.14f, .16f, .18f); camera.transform.rotation = Quaternion.Euler(isometric ? 55 : 90, isometric ? -30 : 0, 0);
                    float radius = Mathf.Max(.1f, bounds.extents.magnitude), span = 0;
                    foreach (var corner in Corners(bounds))
                    { var delta = corner - bounds.center; span = Mathf.Max(span, Mathf.Abs(Vector3.Dot(delta, camera.transform.right)), Mathf.Abs(Vector3.Dot(delta, camera.transform.up))); }
                    camera.orthographicSize = Mathf.Max(.1f, span) * 1.08f;
                    camera.transform.position = bounds.center - camera.transform.forward * (radius * 4 + 1);
                    camera.nearClipPlane = .01f; camera.farClipPlane = radius * 8 + 2;
                    renderer.ambientColor = Color.gray; renderer.lights[0].intensity = 1.2f; renderer.lights[0].transform.rotation = Quaternion.Euler(45, -30, 0);
                    renderer.lights[1].intensity = .6f;
                    renderer.BeginStaticPreview(new Rect(0, 0, 512, 512)); opened = true;
                    foreach (var collider in colliders)
                    {
                        Mesh mesh; Matrix4x4 matrix;
                        if (collider is MeshCollider mc) { mesh = mc.sharedMesh; matrix = mc.transform.localToWorldMatrix; }
                        else if (collider is BoxCollider box)
                        { mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx"); matrix = box.transform.localToWorldMatrix * Matrix4x4.TRS(box.center, Quaternion.identity, box.size); }
                        else if (collider is SphereCollider sphere)
                        {
                            mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx"); var scale = sphere.transform.lossyScale;
                            float radiusWorld = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                            matrix = Matrix4x4.TRS(sphere.transform.TransformPoint(sphere.center), Quaternion.identity, Vector3.one * radiusWorld * 2);
                        }
                        else if (collider is CapsuleCollider capsule)
                        {
                            var scale = capsule.transform.lossyScale; int a = capsule.direction, b = (a + 1) % 3, d = (a + 2) % 3;
                            float radiusWorld = capsule.radius * Mathf.Max(Mathf.Abs(scale[b]), Mathf.Abs(scale[d]));
                            mesh = Capsule(radiusWorld, Mathf.Max(radiusWorld * 2, capsule.height * Mathf.Abs(scale[a]))); temporaryMeshes.Add(mesh);
                            Quaternion axis = a == 0 ? Quaternion.Euler(0, 0, -90) : a == 2 ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
                            matrix = Matrix4x4.TRS(capsule.transform.TransformPoint(capsule.center), capsule.transform.rotation * axis, Vector3.one);
                        }
                        else throw new InvalidOperationException("Не поддержан геометрический предпросмотр " + collider.GetType().Name);
                        if (mesh == null) throw new InvalidOperationException("Отсутствует меш предпросмотра.");
                        var color = collider.gameObject.layer == LayerMask.NameToLayer("Ground") ? new Color(.27f, .29f, .3f)
                            : CoverSurface.Of(collider)?.Class == CoverClass.Soft ? new Color(.49f, .68f, .75f) : new Color(.15f, .38f, .5f);
                        var properties = new MaterialPropertyBlock(); properties.SetColor("_BaseColor", color);
                        for (int sub = 0; sub < mesh.subMeshCount; sub++) renderer.DrawMesh(mesh, matrix, material, sub, properties);
                    }
                    renderer.Render(true, false); rendered = true;
                    texture = renderer.EndStaticPreview(); opened = false;
                }
            }
            finally
            {
                try { if (opened) { var unfinished = renderer.EndStaticPreview(); if (unfinished != null) Object.DestroyImmediate(unfinished); } }
                finally
                { renderer.Cleanup(); foreach (var mesh in temporaryMeshes) Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); if (!rendered && texture != null) Object.DestroyImmediate(texture); }
            }
            texture.hideFlags = HideFlags.HideAndDontSave; texture.name = "Вариант " + result.CandidateId; return texture;
        }
        private static IEnumerable<Vector3> Corners(Bounds b)
        { for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++) yield return new Vector3(x == 0 ? b.min.x : b.max.x, y == 0 ? b.min.y : b.max.y, z == 0 ? b.min.z : b.max.z); }
        private static Mesh Capsule(float radius, float height)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            float half = Mathf.Max(0, height * .5f - radius);
            const int slices = 24, rings = 17;
            for (int ring = 0; ring <= rings; ring++)
            {
                bool upper = ring <= 8;
                float angle = upper ? Mathf.PI * .5f * ring / 8 : Mathf.PI * .5f + Mathf.PI * .5f * (ring - 9) / 8;
                float y = Mathf.Cos(angle) * radius + (upper ? half : -half), r = Mathf.Sin(angle) * radius;
                for (int s = 0; s <= slices; s++) vertices.Add(new Vector3(r * Mathf.Cos(2 * Mathf.PI * s / slices), y, r * Mathf.Sin(2 * Mathf.PI * s / slices)));
            }
            for (int ring = 0; ring < rings; ring++) for (int s = 0; s < slices; s++)
            { int a = ring * (slices + 1) + s, b = a + slices + 1; triangles.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b }); }
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); return mesh;
        }
    }
}
