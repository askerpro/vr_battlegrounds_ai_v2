using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace VrBattlegrounds.Tests.DevTools
{
    /// <summary>
    /// Алгоритм box-UV (BoxUvProjector, Assembly-CSharp-Editor) на синтетических мешах: UV0 в метрах по главной
    /// оси нормали, исходный UV0 переносится в UV2 при его отсутствии, чужой сабмеш не меняется, атрибуты дубликатов
    /// сохраняются. Принято пользователем 2026-10-20 (Lobby, scene-detail): визуально и замером на Quest 3.
    /// </summary>
    public class BoxUvProjectorTests
    {
        // Тестовая asmdef не может ссылаться на predefined Assembly-CSharp-Editor.
        private static object Project(Mesh mesh, params int[] submeshes)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VrBattlegrounds.Editor.Performance.BoxUv.BoxUvProjector"))
                .FirstOrDefault(t => t != null);
            Assert.IsNotNull(type, "Editor assembly с BoxUvProjector не загружена.");
            MethodInfo method = type.GetMethod("Project", BindingFlags.Public | BindingFlags.Static);
            return method.Invoke(null, new object[] { mesh, (IReadOnlyCollection<int>)submeshes });
        }

        private static T Field<T>(object result, string name) =>
            (T)result.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance).GetValue(result);

        // Куб 2×2×2 с 8 общими вершинами: каждая вершина принадлежит трём граням с разными осями.
        private static Mesh SharedCube(bool withUv2)
        {
            var v = new List<Vector3>();
            for (int i = 0; i < 8; i++) v.Add(new Vector3((i & 1) * 2f, ((i >> 1) & 1) * 2f, ((i >> 2) & 1) * 2f));
            int[] tris =
            {
                0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, // -Z, +Z
                0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, // -Y, +Y
                0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5, // -X, +X
            };
            var mesh = new Mesh();
            mesh.SetVertices(v);
            mesh.SetUVs(0, v.Select(p => new Vector2(p.x * 0.1f + 0.3f, p.y * 0.1f)).ToList());
            if (withUv2) mesh.SetUVs(1, v.Select(p => new Vector2(p.y * 0.25f, p.z * 0.25f)).ToList());
            mesh.SetColors(v.Select((p, i) => new Color32((byte)(i * 30), 7, 9, 255)).ToList());
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        private static int Axis(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            float x = Mathf.Abs(n.x), y = Mathf.Abs(n.y), z = Mathf.Abs(n.z);
            return x >= y && x >= z ? 0 : y >= z ? 1 : 2;
        }

        private static Vector2 Expected(Vector3 p, int axis) =>
            axis == 0 ? new Vector2(p.z, p.y) : axis == 1 ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);

        [Test]
        public void ProjectsEveryTriangleOntoItsDominantAxisInMetres()
        {
            Mesh mesh = SharedCube(withUv2: true);
            object result = Project(mesh, 0);
            Assert.IsNull(Field<string>(result, "Skipped"));
            Assert.AreEqual(12, Field<int>(result, "ProjectedTriangles"));
            Vector3[] v = mesh.vertices;
            Vector2[] uv = mesh.uv;
            int[] t = mesh.GetTriangles(0);
            double area3 = 0, area2 = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                int axis = Axis(v[t[i]], v[t[i + 1]], v[t[i + 2]]);
                for (int c = 0; c < 3; c++)
                    Assert.That(Vector2.Distance(uv[t[i + c]], Expected(v[t[i + c]], axis)), Is.LessThan(1e-5f));
                area3 += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude * 0.5;
                Vector2 p = uv[t[i + 1]] - uv[t[i]], q = uv[t[i + 2]] - uv[t[i]];
                area2 += Math.Abs(p.x * q.y - p.y * q.x) * 0.5;
            }
            Assert.That(Math.Sqrt(area3 / area2), Is.EqualTo(1.0).Within(1e-4), "1 м мира на единицу UV");
        }

        [Test]
        public void SplitsSharedVerticesAndKeepsTheirAttributes()
        {
            Mesh mesh = SharedCube(withUv2: true);
            Color32[] colorsBefore = mesh.colors32;
            Vector3[] positionsBefore = mesh.vertices;
            object result = Project(mesh, 0);
            // 8 вершин, по 3 оси на каждую: 16 дубликатов.
            Assert.AreEqual(16, Field<int>(result, "AddedVertices"));
            Assert.AreEqual(24, mesh.vertexCount);
            Assert.IsFalse(Field<bool>(result, "CopiedUv0ToUv2"));
            Vector3[] positions = mesh.vertices;
            Color32[] colors = mesh.colors32;
            for (int i = 8; i < 24; i++)
            {
                int source = Array.IndexOf(positionsBefore, positions[i]);
                Assert.GreaterOrEqual(source, 0, "дубликат — копия исходной вершины");
                Assert.AreEqual(colorsBefore[source], colors[i], "vertex colour дубликата сохранён");
            }
        }

        [Test]
        public void CopiesOriginalUv0IntoMissingUv2ForLightmaps()
        {
            Mesh mesh = SharedCube(withUv2: false);
            Vector2[] uv0Before = mesh.uv;
            Vector3[] positionsBefore = mesh.vertices;
            object result = Project(mesh, 0);
            Assert.IsTrue(Field<bool>(result, "CopiedUv0ToUv2"));
            Vector2[] uv2 = mesh.uv2;
            Vector3[] positions = mesh.vertices;
            Assert.AreEqual(mesh.vertexCount, uv2.Length);
            for (int i = 0; i < positions.Length; i++)
                Assert.AreEqual(uv0Before[Array.IndexOf(positionsBefore, positions[i])], uv2[i],
                    "UV2 = исходная UV0: лайтмапа без UV2 бралась из UV0");
        }

        [Test]
        public void LeavesForeignSubmeshUvUntouched()
        {
            Mesh mesh = SharedCube(withUv2: true);
            int[] all = mesh.GetTriangles(0);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(all.Take(12).ToArray(), 0); // ±Z — цель
            mesh.SetTriangles(all.Skip(12).ToArray(), 1); // ±Y, ±X — чужой материал
            int[] foreignBefore = mesh.GetTriangles(1);
            Vector2[] uvBefore = mesh.uv;
            Project(mesh, 0);
            int[] foreignAfter = mesh.GetTriangles(1);
            Vector2[] uv = mesh.uv;
            CollectionAssert.AreEqual(foreignBefore, foreignAfter, "индексы чужого сабмеша не меняются");
            foreach (int index in foreignAfter)
                Assert.AreEqual(uvBefore[index], uv[index], "UV вершин чужого сабмеша не меняется");
        }
    }
}
