using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Editor.Gameplay.SightSplit;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Tests.SightSplit
{
    /// <summary>
    /// Тесты ИНСТРУМЕНТА разделения вшитых прицелов (<see cref="SightPartSplitter"/>), не механики сведения и не
    /// игрового поведения. Работают на временной копии префаба (содержимое в preview-сцене), ничего не сохраняют:
    /// селекторы находят ровно один остров, треугольники корпуса + деталей = исходные, pivot детали — на точке R/F,
    /// вершины детали не сдвигаются в мире (у Scar — после подъёма рамок).
    /// </summary>
    [Category("SightSplitTool")]
    public sealed class SightPartSplitterTests
    {
        private const float PivotTolerance = 0.05e-3f;   // 0,05 мм в метрах префаба
        private const float VertexTolerance = 0.01e-3f;  // 0,01 мм в метрах префаба

        private static IEnumerable<string> Weapons => SightSplitTable.All.Select(s => s.WeaponId);

        private sealed class TempCopy : IDisposable
        {
            public readonly SightSplitSpec Spec;
            public readonly GameObject Root;
            public SightSplitApplyResult Result;

            public TempCopy(string weaponId, bool apply = true)
            {
                Spec = SightSplitTable.Get(weaponId);
                Root = PrefabUtility.LoadPrefabContents(Spec.PrefabPath);
                if (apply) Result = SightPartSplitter.Apply(Root, Spec);
            }

            public void Dispose()
            {
                Result?.DestroyMeshes();
                PrefabUtility.UnloadPrefabContents(Root);
            }
        }

        [Test]
        public void Table_CoversAgreedWeapons()
        {
            CollectionAssert.AreEquivalent(new[] { "AK105", "Mk14", "Viper", "R08", "MP5K", "Uzi", "Herrington", "Revolver", "Gun", "PPK", "Scar" }, Weapons.ToArray());
            foreach (SightSplitSpec spec in SightSplitTable.All)
                Assert.That(spec.Parts.Count(p => p.Movable), Is.EqualTo(spec.WeaponId == "PPK" ? 0 : 1), spec.WeaponId);
        }

        [TestCaseSource(nameof(Weapons))]
        public void Selectors_FindExactlyOneIslandEach(string weaponId)
        {
            using var copy = new TempCopy(weaponId, apply: false);
            foreach (var group in copy.Spec.Parts.Where(p => p.Selectors.Count > 0).GroupBy(p => p.NodePath))
            {
                Mesh mesh = copy.Root.transform.Find(group.Key).GetComponent<MeshFilter>().sharedMesh;
                SightMeshSnapshot snapshot = SightMeshSnapshot.Read(mesh, weaponId);
                var islands = new MeshIslands(snapshot.Positions, snapshot.SubmeshIndices);
                foreach (SightSplitPartSpec part in group)
                    foreach (SightIslandSelector selector in part.Selectors)
                        Assert.That(islands.Islands.Count(selector.Matches), Is.EqualTo(1), $"{weaponId} {part.Role} {selector}");
            }
        }

        [TestCaseSource(nameof(Weapons))]
        public void TriangleUnion_EqualsSource(string weaponId)
        {
            using var copy = new TempCopy(weaponId);
            Assert.That(copy.Result.Splits.Values.Sum(s => s.Parts.Count), Is.EqualTo(copy.Spec.MovingPart != null ? 1 : 0), "вынесенных деталей");
            foreach (SightMeshSplit split in copy.Result.Splits.Values)
            {
                SightMeshSnapshot source = SightMeshSnapshot.Read(split.Source, weaponId);
                SightMeshSnapshot body = SightMeshSnapshot.Read(split.Body, weaponId);
                Assert.That(body.VertexCount, Is.EqualTo(source.VertexCount), "корпус сохраняет все вершины");
                Assert.That(body.TriangleCount, Is.GreaterThan(split.Parts.Sum(p => p.Triangles)), "выносится деталь, а не узел целиком");
                CollectionAssert.AreEqual(source.Attributes.Select(a => (a.attribute, a.format, a.dimension)).ToArray(),
                                          body.Attributes.Select(a => (a.attribute, a.format, a.dimension)).ToArray(), "атрибуты корпуса (без скин-атрибутов)");
                foreach (var attribute in source.Attributes)
                    CollectionAssert.AreEqual(source.AttributeData[attribute.attribute], body.AttributeData[attribute.attribute], $"{attribute.attribute} корпуса не изменён");
                foreach (SightPartMesh part in split.Parts)
                {
                    SightMeshSnapshot partData = SightMeshSnapshot.Read(part.Mesh, weaponId);
                    foreach (var attribute in source.Attributes.Where(a => a.attribute != UnityEngine.Rendering.VertexAttribute.Position))
                        for (int v = 0; v < part.SourceVertices.Length; v++)
                            CollectionAssert.AreEqual(source.AttributeBytes(attribute.attribute, part.SourceVertices[v]), partData.AttributeBytes(attribute.attribute, v),
                                                      $"{weaponId} {part.Spec.Role}: {attribute.attribute} вершины {v}");
                }
                Assert.That(body.SubmeshIndices.Count, Is.EqualTo(source.SubmeshIndices.Count), "число подмешей корпуса");

                for (int sub = 0; sub < source.SubmeshIndices.Count; sub++)
                {
                    var union = Triangles(body.SubmeshIndices[sub]).ToList();
                    foreach (SightPartMesh part in split.Parts)
                    {
                        SightMeshSnapshot partData = SightMeshSnapshot.Read(part.Mesh, weaponId);
                        int local = Array.IndexOf(part.SourceSubmeshes, sub);
                        if (local >= 0)
                            union.AddRange(Triangles(partData.SubmeshIndices[local]).Select(t => (part.SourceVertices[t.a], part.SourceVertices[t.b], part.SourceVertices[t.c])));
                    }
                    var expected = Triangles(source.SubmeshIndices[sub]).OrderBy(t => t).ToList();
                    CollectionAssert.AreEqual(expected, union.OrderBy(t => t).ToList(), $"{weaponId}: подмеш {sub} — объединение ≠ исходному");
                }
                foreach (SightPartMesh part in split.Parts)
                    Assert.That(part.Triangles, Is.EqualTo(part.Islands.Sum(i => i.Triangles)), $"{weaponId} {part.Spec.Role}: треугольники = острова селекторов");
            }
        }

        [TestCaseSource(nameof(Weapons))]
        public void Pivot_IsOnSpecPoint(string weaponId)
        {
            using var copy = new TempCopy(weaponId);
            Transform root = copy.Root.transform;
            foreach (SightSplitPartSpec part in copy.Spec.Parts)
            {
                SightAdjustablePart component = copy.Result.Parts[part];
                Vector3 pivot = SightSplitPaths.PrefabMetres(root, component.transform.position);
                Vector3 expected = SightSplitPaths.PrefabMetres(root, root.Find(part.NodePath).TransformPoint(part.PointMesh));
                Assert.That((pivot - expected).magnitude, Is.LessThan(PivotTolerance), $"{weaponId} {part.Role}: pivot ≠ точке меша");
                if (part.PointPrefabM.HasValue)
                    Assert.That((pivot - part.PointPrefabM.Value).magnitude, Is.LessThan(PivotTolerance),
                                $"{weaponId} {part.Role}: pivot {pivot * 1e3f} мм ≠ точке разбора {part.PointPrefabM.Value * 1e3f} мм");
            }
        }

        [TestCaseSource(nameof(Weapons))]
        public void PartVertices_KeepWorldPositions(string weaponId)
        {
            using var copy = new TempCopy(weaponId);
            Transform root = copy.Root.transform;
            foreach (var pair in copy.Result.Splits)
            {
                Transform node = pair.Key;
                SightMeshSnapshot source = SightMeshSnapshot.Read(pair.Value.Source, weaponId);
                foreach (SightPartMesh part in pair.Value.Parts)
                {
                    Transform meshObject = copy.Result.PartRenderers[part.Spec].transform;
                    Vector3[] vertices = part.Mesh.vertices;
                    float worst = 0f;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 now = SightSplitPaths.PrefabMetres(root, meshObject.TransformPoint(vertices[i]));
                        Vector3 was = SightSplitPaths.PrefabMetres(root, node.TransformPoint(source.Positions[part.SourceVertices[i]]));
                        worst = Mathf.Max(worst, (now - was).magnitude);
                    }
                    Assert.That(worst, Is.LessThan(VertexTolerance), $"{weaponId} {part.Spec.Role}: сдвиг вершин {worst * 1e3f:0.0000} мм");
                }
            }
        }

        [TestCaseSource(nameof(Weapons))]
        public void Structure_OneMovingPartAlongWeaponUp(string weaponId)
        {
            using var copy = new TempCopy(weaponId);
            Transform root = copy.Root.transform;
            SightAdjustablePart[] parts = copy.Root.GetComponentsInChildren<SightAdjustablePart>(true);
            Assert.That(parts.Length, Is.EqualTo(2));
            Assert.That(parts.Count(p => p.Role == SightPartRole.Rear), Is.EqualTo(1));
            foreach (SightAdjustablePart part in parts)
            {
                Assert.That(part.NeutralLocalPosition, Is.EqualTo(part.transform.localPosition));
                Assert.That(part.Offset, Is.EqualTo(0f));
                Assert.That((part.transform.parent.lossyScale - Vector3.one).magnitude, Is.LessThan(1e-4f), "рамка детали — мировой масштаб 1");
            }
            SightSplitPartSpec movingSpec = copy.Spec.MovingPart;
            Assert.That(parts.Count(p => p.IsAdjustable), Is.EqualTo(movingSpec != null ? 1 : 0));
            if (movingSpec == null) return;

            SightAdjustablePart moving = copy.Result.Parts[movingSpec];
            Assert.That(moving.TravelDown, Is.EqualTo(movingSpec.TravelDown));
            Assert.That(moving.TravelUp, Is.EqualTo(movingSpec.TravelUp));
            Assert.That(Quaternion.Angle(moving.transform.localRotation, Quaternion.identity), Is.LessThan(1e-3f), "ход по локальной Y — поворот детали нулевой");
            Assert.That(Vector3.Dot(moving.transform.up, root.up), Is.GreaterThan(0.99999f), "локальная Y детали — вверх оружия");

            // Сдвиг на 1 мм по локальной Y — вершины детали поднимаются на 1 мм мира.
            Transform meshObject = copy.Result.PartRenderers[movingSpec].transform;
            Vector3 vertex = copy.Result.Splits.Values.SelectMany(s => s.Parts).Single().Mesh.vertices[0];
            Vector3 before = meshObject.TransformPoint(vertex);
            moving.transform.localPosition = moving.NeutralLocalPosition + Vector3.up * 1e-3f;
            Vector3 delta = meshObject.TransformPoint(vertex) - before;
            Assert.That((delta - root.up * 1e-3f).magnitude, Is.LessThan(1e-6f), $"{weaponId}: ход 1 мм дал {delta * 1e3f} мм");
        }

        [Test]
        public void Scar_RaiseMatchesHingeAxis()
        {
            using var copy = new TempCopy("Scar", apply: false);
            Transform root = copy.Root.transform;
            var folded = copy.Spec.Raises.Select(r => (r, node: root.Find(r.NodePath))).Select(x => (x.r, x.node,
                position: SightSplitPaths.PrefabMetres(root, x.node.position), rotation: Quaternion.Inverse(root.rotation) * x.node.rotation)).ToList();
            copy.Result = SightPartSplitter.Apply(copy.Root, copy.Spec);
            foreach (var item in folded)
            {
                Quaternion q = Quaternion.AngleAxis(item.r.AngleDeg, item.r.AxisDirPrefab);
                Vector3 expected = item.r.AxisPointPrefabM + q * (item.position - item.r.AxisPointPrefabM);
                Assert.That((SightSplitPaths.PrefabMetres(root, item.node.position) - expected).magnitude, Is.LessThan(PivotTolerance), $"{item.r.NodePath}: позиция после подъёма");
                Assert.That(Quaternion.Angle(Quaternion.Inverse(root.rotation) * item.node.rotation, q * item.rotation), Is.LessThan(0.05f), $"{item.r.NodePath}: поворот после подъёма");
            }
        }

        [Test]
        public void Apply_RefusesPrefabAsset()
        {
            SightSplitSpec spec = SightSplitTable.Get("AK105");
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
            Assert.Throws<InvalidOperationException>(() => SightPartSplitter.Apply(asset, spec));
        }

        [Test]
        public void Apply_RefusesSecondSplit()
        {
            using var copy = new TempCopy("Gun");
            Assert.Throws<InvalidOperationException>(() => SightPartSplitter.Apply(copy.Root, copy.Spec));
        }

        [TestCaseSource(nameof(Weapons))]
        public void Plan_IsDryRun(string weaponId)
        {
            SightSplitSpec spec = SightSplitTable.Get(weaponId);
            string before = Hash(spec.PrefabPath);
            string plan = SightPartSplitter.Plan(weaponId);
            StringAssert.Contains(weaponId, plan);
            Assert.That(Hash(spec.PrefabPath), Is.EqualTo(before), "Plan не пишет префаб");
            Assert.That(EditorUtility.IsDirty(AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath)), Is.False);
        }

        private static IEnumerable<(int a, int b, int c)> Triangles(int[] indices)
        {
            for (int i = 0; i < indices.Length; i += 3) yield return (indices[i], indices[i + 1], indices[i + 2]);
        }

        private static string Hash(string assetPath)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(assetPath)));
        }
    }
}
