using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>Обрезает треугольники шестью плоскостями объёма. Длинная грань не подсвечивает весь корпус.</summary>
    public static class WeaponHighlightRegionBaker
    {
        public static Mesh Bake(Transform root, WeaponVisualRegion region, string folder)
        {
            Mesh source = region.Source.GetComponent<MeshFilter>().sharedMesh;
            Matrix4x4 toRoot = root.worldToLocalMatrix * region.Source.localToWorldMatrix;
            Matrix4x4 fromRoot = toRoot.inverse;
            Vector3[] sourceVertices = source.vertices;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            int[] src = source.triangles;
            for (int i = 0; i < src.Length; i += 3)
            {
                var polygon = new List<Vector3> { toRoot.MultiplyPoint3x4(sourceVertices[src[i]]), toRoot.MultiplyPoint3x4(sourceVertices[src[i + 1]]), toRoot.MultiplyPoint3x4(sourceVertices[src[i + 2]]) };
                for (int axis = 0; axis < 3 && polygon.Count > 0; axis++)
                {
                    polygon = Clip(polygon, axis, region.Bounds.min[axis], true);
                    polygon = Clip(polygon, axis, region.Bounds.max[axis], false);
                }
                for (int v = 1; v + 1 < polygon.Count; v++)
                {
                    // 0,3 мм от поверхности исключают мерцание совпадающих полигонов подсветки.
                    Vector3 normal = Vector3.Cross(polygon[v] - polygon[0], polygon[v + 1] - polygon[0]).normalized * 0.0003f;
                    int first = vertices.Count;
                    vertices.Add(fromRoot.MultiplyPoint3x4(polygon[0] + normal));
                    vertices.Add(fromRoot.MultiplyPoint3x4(polygon[v] + normal));
                    vertices.Add(fromRoot.MultiplyPoint3x4(polygon[v + 1] + normal));
                    triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                }
            }
            if (triangles.Count == 0) throw new InvalidOperationException($"{root.name}/{region.Name}: пустая область подсветки");
            var mesh = new Mesh { name = $"{root.name}_{region.Name}Region", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            KinemationWeapon.EnsureFolder(folder);
            string path = $"{folder}/{region.Name}Region.asset";
            Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
            else
            {
                // CopySerialized обновляет CPU-данные Mesh, но может оставить старый GPU-буфер.
                // Mesh API инвалидирует буфер; объект ассета и GUID остаются теми же.
                saved.Clear(); saved.indexFormat = mesh.indexFormat;
                saved.vertices = mesh.vertices; saved.triangles = mesh.triangles;
                saved.normals = mesh.normals; saved.bounds = mesh.bounds; saved.name = mesh.name;
                UnityEngine.Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved);
            }
            return saved;
        }

        private static List<Vector3> Clip(List<Vector3> polygon, int axis, float value, bool above)
        {
            var result = new List<Vector3>();
            if (polygon.Count == 0) return result;
            Vector3 previous = polygon[polygon.Count - 1];
            bool previousInside = above ? previous[axis] >= value : previous[axis] <= value;
            foreach (Vector3 current in polygon)
            {
                bool inside = above ? current[axis] >= value : current[axis] <= value;
                if (inside != previousInside)
                    result.Add(Vector3.LerpUnclamped(previous, current, (value - previous[axis]) / (current[axis] - previous[axis])));
                if (inside) result.Add(current);
                previous = current; previousInside = inside;
            }
            return result;
        }
    }
}
