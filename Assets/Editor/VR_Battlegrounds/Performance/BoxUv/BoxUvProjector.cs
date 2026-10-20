using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.Editor.Performance.BoxUv
{
    /// <summary>
    /// Кубическая (box) развёртка UV0 выбранных сабмешей: каждый треугольник проецируется на плоскость
    /// главной оси своей нормали, UV — координаты вершин в метрах пространства меша. Это одна выборка
    /// стандартного шейдера вместо трипланарной проекции по миру (S_VPWorldAligned пака MilitaryCamp):
    /// повтор текстуры задаёт тайлинг материала (1 / метры на повтор).
    ///
    /// <para>
    /// Вершина, которую делят треугольники с разными осями или другой сабмеш, дублируется со всеми
    /// атрибутами — UV остальных сабмешей не меняется. Если UV2 нет, исходный UV0 сначала копируется
    /// в UV2: лайтмаппер без UV2 берёт UV0, и запечённые лайтмапы остаются верными.
    /// </para>
    /// </summary>
    public static class BoxUvProjector
    {
        public sealed class Result
        {
            public int ProjectedTriangles;
            public int AddedVertices;
            public bool CopiedUv0ToUv2;
            public string Skipped;
        }

        /// <summary>Переразвернуть сабмеши <paramref name="submeshes"/> меша на месте.</summary>
        public static Result Project(Mesh mesh, IReadOnlyCollection<int> submeshes)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            var result = new Result();
            if (mesh.blendShapeCount > 0 || mesh.HasVertexAttribute(VertexAttribute.BlendWeight))
            {
                result.Skipped = "skinned/blendshape";
                return result;
            }
            var positions = new List<Vector3>();
            mesh.GetVertices(positions);
            int originalCount = positions.Count;
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var colors = new List<Color32>();
            mesh.GetNormals(normals);
            mesh.GetTangents(tangents);
            mesh.GetColors(colors);
            var uvs = new List<Vector4>[8];
            for (int channel = 0; channel < 8; channel++)
            {
                if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord0 + channel)) continue;
                uvs[channel] = new List<Vector4>();
                mesh.GetUVs(channel, uvs[channel]);
            }
            if (uvs[0] == null)
            {
                result.Skipped = "no uv0";
                return result;
            }
            if (uvs[1] == null)
            {
                uvs[1] = new List<Vector4>(uvs[0]);
                result.CopiedUv0ToUv2 = true;
            }

            // Вершины, которые используют сабмеши вне цели: их UV трогать нельзя.
            var foreign = new bool[originalCount];
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (Contains(submeshes, sub)) continue;
                foreach (int index in mesh.GetIndices(sub)) foreign[index] = true;
            }

            // Ось, уже закреплённая за исходной вершиной (-1 — ещё не использована целью).
            var assignedAxis = new int[originalCount];
            for (int i = 0; i < originalCount; i++) assignedAxis[i] = -1;
            var duplicates = new Dictionary<long, int>();
            var newIndices = new List<int>[mesh.subMeshCount];

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (!Contains(submeshes, sub)) continue;
                if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                {
                    result.Skipped = "submesh " + sub + " is not triangles";
                    return result;
                }
                int[] indices = mesh.GetIndices(sub);
                var rebuilt = new List<int>(indices.Length);
                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    int axis = DominantAxis(positions[indices[t]], positions[indices[t + 1]], positions[indices[t + 2]]);
                    for (int c = 0; c < 3; c++)
                    {
                        int source = indices[t + c];
                        rebuilt.Add(Resolve(source, axis));
                    }
                    result.ProjectedTriangles++;
                }
                newIndices[sub] = rebuilt;
            }

            int Resolve(int source, int axis)
            {
                if (!foreign[source] && (assignedAxis[source] == -1 || assignedAxis[source] == axis))
                {
                    assignedAxis[source] = axis;
                    uvs[0][source] = Planar(positions[source], axis, uvs[0][source]);
                    return source;
                }
                long key = ((long)source << 2) | (uint)axis;
                if (duplicates.TryGetValue(key, out int existing)) return existing;
                int copy = positions.Count;
                positions.Add(positions[source]);
                if (normals.Count == originalCount) normals.Add(normals[source]);
                if (tangents.Count == originalCount) tangents.Add(tangents[source]);
                if (colors.Count == originalCount) colors.Add(colors[source]);
                for (int channel = 0; channel < 8; channel++)
                    if (uvs[channel] != null) uvs[channel].Add(uvs[channel][source]);
                uvs[0][copy] = Planar(positions[source], axis, uvs[0][source]);
                duplicates.Add(key, copy);
                result.AddedVertices++;
                return copy;
            }

            Bounds bounds = mesh.bounds;
            if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            if (normals.Count == positions.Count) mesh.SetNormals(normals);
            if (tangents.Count == positions.Count) mesh.SetTangents(tangents);
            if (colors.Count == positions.Count) mesh.SetColors(colors);
            for (int channel = 0; channel < 8; channel++)
                if (uvs[channel] != null) mesh.SetUVs(channel, uvs[channel]);
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                if (newIndices[sub] != null) mesh.SetIndices(newIndices[sub], MeshTopology.Triangles, sub, false);
            mesh.bounds = bounds;
            return result;
        }

        private static bool Contains(IReadOnlyCollection<int> set, int value)
        {
            foreach (int item in set) if (item == value) return true;
            return false;
        }

        private static int DominantAxis(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            float x = Mathf.Abs(n.x), y = Mathf.Abs(n.y), z = Mathf.Abs(n.z);
            if (x >= y && x >= z) return 0;
            return y >= z ? 1 : 2;
        }

        private static Vector4 Planar(Vector3 p, int axis, Vector4 original)
        {
            // Как SubG_WorldAlignedTexture: X-грань — (z, y), Y-грань — (x, z), Z-грань — (x, y).
            switch (axis)
            {
                case 0: return new Vector4(p.z, p.y, original.z, original.w);
                case 1: return new Vector4(p.x, p.z, original.z, original.w);
                default: return new Vector4(p.x, p.y, original.z, original.w);
            }
        }
    }
}
