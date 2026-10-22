using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace VrBattlegrounds.Editor.Gameplay.Tests
{
    /// <summary>
    /// Области подсветки хвата на синтетических мешах: затравка — ближайшая точка поверхности, а не вершины; рукоять
    /// отдельной оболочкой берётся островом целиком, без ствольной коробки над ней; слишком большой остров — откат на
    /// обрезку коробкой. Тесты лежат рядом с кодом: <c>Gameplay/</c> — в <c>Assembly-CSharp-Editor</c>, на который asmdef
    /// тестов не ссылается. Оси корня: x вправо, y вверх, z вперёд; точка ладони — как у AK105 (в 13 мм от щёчки).
    /// </summary>
    public class WeaponHighlightRegionTests
    {
        // Рукоять 30×110×80 мм под ствольной коробкой; коробка — отдельная оболочка, касается рукояти снизу своей гранью.
        private static readonly Vector3 GripMin = new(-0.015f, -0.12f, -0.09f), GripMax = new(0.015f, -0.01f, -0.01f);
        private static readonly Vector3 ReceiverMin = new(-0.02f, -0.01f, -0.15f), ReceiverMax = new(0.02f, 0.03f, 0.3f);
        private static readonly Vector3 Palm = new(0.028f, -0.064f, -0.064f);

        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        /// <summary>Коробка из шести граней по 4 своих вершины (раздельные нормали — как у импортированных мешей).</summary>
        private static void AddBox(List<Vector3> vertices, List<int> triangles, Vector3 min, Vector3 max)
        {
            Vector3 P(int x, int y, int z) => new(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
            int[][] faces =
            {
                new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 },
                new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 }
            };
            foreach (int[] face in faces)
            {
                int first = vertices.Count;
                foreach (int corner in face) vertices.Add(P(corner >> 2 & 1, corner >> 1 & 1, corner & 1));
                triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }
        }

        private (Transform root, Transform body) Weapon(params (Vector3 min, Vector3 max)[] boxes)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (var (min, max) in boxes) AddBox(vertices, triangles, min, max);
            var mesh = new Mesh { name = "SyntheticBody" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            var root = new GameObject("SyntheticWeapon");
            var body = new GameObject("Base");
            body.transform.SetParent(root.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            _created.Add(root);
            _created.Add(mesh);
            return (root.transform, body.transform);
        }

        private WeaponVisualRegion Primary(Transform root, Transform body) =>
            WeaponInteractionRecipes.SurfaceRegion(PartGeometry.Of(root, body), "Primary", body, Palm, WeaponInteractionRecipes.PrimarySize, true);

        private Mesh Bake(Transform root, WeaponVisualRegion region, out WeaponHighlightRegionBaker.RegionStats stats)
        {
            Mesh mesh = WeaponHighlightRegionBaker.Build(root, region, out stats);
            _created.Add(mesh);
            return mesh;
        }

        [Test]
        public void SeparateGripShell_IsTakenWhole_WithoutReceiver()
        {
            var (root, body) = Weapon((GripMin, GripMax), (ReceiverMin, ReceiverMax));
            WeaponVisualRegion region = Primary(root, body);

            Assert.IsNull(region.IslandRejection);
            Assert.IsNotNull(region.Triangles, "рукоять — остров размера коробки");
            Assert.AreEqual(12, region.Triangles.Length, "только треугольники рукояти");
            Assert.That(region.Triangles.All(t => t < 12), "первая коробка меша — рукоять");

            Bounds baked = Bake(root, region, out var stats).bounds;
            Assert.AreEqual(GripMin.y, baked.min.y, 0.001f, "низ рукояти покрыт");
            Assert.AreEqual(GripMax.y, baked.max.y, 0.001f, "ствольная коробка над рукоятью не захвачена");
            Assert.AreEqual(GripMin.z, baked.min.z, 0.001f);
            Assert.AreEqual(GripMax.z, baked.max.z, 0.001f, "передняя сторона рукояти покрыта целиком");
            Assert.AreEqual(stats.Area, stats.OnSeedIsland, 1e-7f);
            Assert.AreEqual(region.SeedIsland.Area, stats.Area, 1e-6f);
        }

        [Test]
        public void Seed_IsClosestSurfacePoint_NotNearestVertex()
        {
            // У грани рукояти вершины только в углах: ближайшая вершина ушла бы на 5 см вверх или вниз.
            var (root, body) = Weapon((GripMin, GripMax), (ReceiverMin, ReceiverMax));
            WeaponVisualRegion region = Primary(root, body);

            Assert.AreEqual(GripMax.x, region.Seed.x, 1e-5f);
            Assert.AreEqual(Palm.y, region.Seed.y, 1e-5f);
            Assert.AreEqual(Palm.z, region.Seed.z, 1e-5f);
            Assert.AreEqual(Palm.y, region.Bounds.center.y, 1e-5f, "центр коробки — на высоте затравки");
            Assert.AreEqual(0f, region.Bounds.center.x, 1e-5f, "центр коробки — на оси симметрии меша");
        }

        [Test]
        public void OversizedIsland_FallsBackToBoxClip()
        {
            // Рукоять слита с рамкой в одну оболочку (пистолет, револьвер, шейка ложи): остров длиннее коробки.
            var (root, body) = Weapon((new Vector3(-0.015f, -0.12f, -0.25f), new Vector3(0.015f, 0.03f, 0.3f)));
            WeaponVisualRegion region = Primary(root, body);

            Assert.IsNull(region.Triangles);
            StringAssert.Contains("больше коробки", region.IslandRejection);

            Bounds baked = Bake(root, region, out var stats).bounds;
            Bounds box = region.Bounds;
            box.Expand(0.002f); // сдвиг 0,3 мм по нормали
            Assert.That(box.Contains(baked.min) && box.Contains(baked.max), "обрезка коробкой");
            Assert.AreEqual(stats.Area, stats.OnSeedIsland, 1e-7f);
            Assert.Less(stats.Area, region.SeedIsland.Area);
        }

        [Test]
        public void DistantOrTinyIsland_IsRejected()
        {
            Vector3 size = WeaponInteractionRecipes.PrimarySize;
            var island = new MeshIslands.Island { Bounds = new Bounds(new Vector3(0f, 0f, 0.1f), new Vector3(0.03f, 0.1f, 0.05f)) };
            StringAssert.Contains("центр острова", WeaponInteractionRecipes.IslandRejection(island, Vector3.zero, size));
            Assert.IsNull(WeaponInteractionRecipes.IslandRejection(island, new Vector3(0f, 0f, 0.07f), size));

            // Винт или накладка рукояти (MP5K: остров 2×5×8 мм) — не рукоять.
            var tiny = new MeshIslands.Island { Bounds = new Bounds(Vector3.zero, new Vector3(0.002f, 0.005f, 0.008f)) };
            StringAssert.Contains("мельче детали", WeaponInteractionRecipes.IslandRejection(tiny, Vector3.zero, size));
        }

        [Test]
        public void Islands_WeldSplitVertices_AndSeparateShells()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            AddBox(vertices, triangles, GripMin, GripMax);
            AddBox(vertices, triangles, ReceiverMin, ReceiverMax);

            int[] labels = MeshIslands.Labels(vertices, triangles.ToArray());

            Assert.AreEqual(1, labels.Take(12).Distinct().Count(), "грани коробки с раздельными вершинами — один остров");
            Assert.AreEqual(1, labels.Skip(12).Distinct().Count());
            Assert.AreNotEqual(labels[0], labels[12], "оболочки без общих вершин — разные острова");
        }

        [Test]
        public void ClosestPointOnTriangle_CoversRegions()
        {
            Vector3 a = Vector3.zero, b = Vector3.right, c = Vector3.up;

            void Near(Vector3 expected, Vector3 p) =>
                Assert.Less((WeaponInteractionRecipes.ClosestPointOnTriangle(p, a, b, c) - expected).magnitude, 1e-6f, $"точка {p}");

            Near(new Vector3(0.25f, 0.25f, 0f), new Vector3(0.25f, 0.25f, 1f)); // внутренность
            Near(a, new Vector3(-1f, -1f, 0f));                                 // вершина
            Near(new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, -1f, 0f));        // ребро ab
            Near(new Vector3(0.5f, 0.5f, 0f), new Vector3(1f, 1f, 0f));         // ребро bc
        }
    }
}
