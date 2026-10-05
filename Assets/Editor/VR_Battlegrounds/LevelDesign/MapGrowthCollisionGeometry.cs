using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.LevelDesign;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.LevelDesign
{
    public enum MapGrowthGeometryDefect { Intersection, BelowFloor, OutsideFloor, Floating, Forbidden, Reserved, PositionArea, UncheckedGeometry, UncheckedSupport, StaleInput }
    public sealed class MapGrowthGeometryIssue
    {
        public MapGrowthGeometryDefect kind;
        public string recipeId, otherId, message;
        public float depth;
        public bool IsUnchecked => kind == MapGrowthGeometryDefect.UncheckedGeometry || kind == MapGrowthGeometryDefect.UncheckedSupport;
    }

    /// <summary>Собственные прокси только для пересечений; исходные MeshCollider обзора/прострела не меняются.</summary>
    internal sealed class MapGrowthCollisionGeometry : IDisposable
    {
        private sealed class Shape
        {
            public string id;
            public Collider source;
            public Collider[] pieces;
            public Bounds Bounds => source.bounds;
        }
        private readonly Scene proxyScene;
        private readonly List<Shape> fixedShapes = new List<Shape>();
        private readonly List<Shape> candidateShapes = new List<Shape>();
        private readonly List<Rect> supports = new List<Rect>();
        private readonly List<Mesh> proxyMeshes = new List<Mesh>();
        private readonly MapGrowthSnapshot snapshot;
        private readonly float floorY;
        private readonly List<MapGrowthGeometryIssue> issues;
        private bool disposed;
        public MapGrowthCollisionGeometry(MapGrowthPreview preview, MapGrowthSnapshot snapshot, float floorY,
            IReadOnlyDictionary<GameObject, MapGrowthBlockRecipe> generated, List<MapGrowthGeometryIssue> issues)
        {
            this.snapshot = snapshot; this.floorY = floorY; this.issues = issues;
            proxyScene = EditorSceneManager.NewPreviewScene();
            try
            {
                int ground = LayerMask.NameToLayer("Ground");
                foreach (var entry in preview.FixedColliderIds)
                {
                    if (entry.Key.gameObject.layer == ground)
                    {
                        if (GroundRectangle(entry.Key, out var support)) supports.Add(support);
                        continue;
                    }
                    fixedShapes.Add(Build(entry.Key, entry.Value));
                }
                foreach (var entry in generated)
                    foreach (var collider in entry.Key.GetComponentsInChildren<Collider>(false).Where(BlockoutSupportSurfaces.IsActiveSolid))
                        candidateShapes.Add(Build(collider, entry.Value.RecipeId));
                Physics.SyncTransforms();
            }
            catch { Dispose(); throw; }
        }

        public IEnumerable<MapEvaluationWork> CheckSteps()
        {
            var layout = snapshot.CopyLayout();
            foreach (var group in candidateShapes.GroupBy(s => s.id))
            {
                var bounds = group.First().Bounds;
                foreach (var shape in group.Skip(1)) bounds.Encapsulate(shape.Bounds);
                if (bounds.min.y < floorY - .001f) Issue(MapGrowthGeometryDefect.BelowFloor, group.Key, "floor", "Низ ниже пола.", floorY - bounds.min.y);
                if (bounds.min.y > floorY + .001f) Issue(MapGrowthGeometryDefect.Floating, group.Key, "floor", "Низ не опирается на уровень пола.", bounds.min.y - floorY);
                var max = snapshot.Origin + new Vector2(snapshot.Width, snapshot.Depth) * snapshot.Cell;
                if (bounds.min.x < snapshot.Origin.x - .001f || bounds.min.z < snapshot.Origin.y - .001f || bounds.max.x > max.x + .001f || bounds.max.z > max.y + .001f)
                    Issue(MapGrowthGeometryDefect.OutsideFloor, group.Key, "floor", "Настоящая геометрия выходит за границы пола.");
                else if (!RectUnionContains(supports, new Rect(bounds.min.x, bounds.min.z, bounds.size.x, bounds.size.z)))
                    Issue(MapGrowthGeometryDefect.UncheckedSupport, group.Key, "floor", "Непрерывная опора не доказана: поверхность сложнее проверенного прямоугольного пола.");
            }
            foreach (var candidate in candidateShapes)
            {
                if (candidate.pieces == null)
                { Issue(MapGrowthGeometryDefect.UncheckedGeometry, candidate.id, "", "Нет точного выпуклого разбиения коллайдера кандидата."); continue; }
                foreach (var obstacle in fixedShapes)
                {
                    if (!candidate.Bounds.Intersects(obstacle.Bounds)) continue;
                    if (obstacle.pieces == null)
                    { Issue(MapGrowthGeometryDefect.UncheckedGeometry, candidate.id, obstacle.id, "Пересечение с невыпуклой геометрией не проверено; false native-запроса не доказывает свободный объём."); continue; }
                    foreach (var a in candidate.pieces) foreach (var b in obstacle.pieces)
                        if (a.bounds.Intersects(b.bounds)) yield return Pair(a, b, candidate.id, obstacle.id, MapGrowthGeometryDefect.Intersection);
                }
                foreach (var p in layout.positions)
                {
                    var box = new Bounds(new Vector3((p.min.x + p.max.x) * .5f, floorY + LevelDesignRules.BodyTop * .5f, (p.min.y + p.max.y) * .5f),
                        new Vector3(p.max.x - p.min.x, LevelDesignRules.BodyTop, p.max.y - p.min.y));
                    if (candidate.Bounds.Intersects(box))
                        foreach (var piece in candidate.pieces) yield return Box(piece, box, candidate.id, p.id, MapGrowthGeometryDefect.PositionArea);
                }
                foreach (var volume in snapshot.Constraints)
                {
                    var box = volume.Bounds;
                    if (!candidate.Bounds.Intersects(box)) continue;
                    // Точный объём ограничения; растеризованная маска не служит доказательством пересечения.
                    var kind = volume.PhysicalReserve ? MapGrowthGeometryDefect.Reserved : MapGrowthGeometryDefect.Forbidden;
                    foreach (var piece in candidate.pieces) yield return Box(piece, box, candidate.id, volume.OwnerId, kind);
                }
            }
            for (int i = 0; i < candidateShapes.Count; i++) for (int j = i + 1; j < candidateShapes.Count; j++)
            {
                var a = candidateShapes[i]; var b = candidateShapes[j];
                if (a.id == b.id || a.pieces == null || b.pieces == null || !a.Bounds.Intersects(b.Bounds)) continue;
                foreach (var first in a.pieces) foreach (var second in b.pieces)
                    if (first.bounds.Intersects(second.bounds)) yield return Pair(first, second, a.id, b.id, MapGrowthGeometryDefect.Intersection);
            }
        }

        private MapEvaluationWork Pair(Collider a, Collider b, string id, string other, MapGrowthGeometryDefect kind)
            => new MapEvaluationWork(1, () => {
                if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out _, out float depth) && depth > .001f)
                    Issue(kind, id, other, "Пересечение объёмов; глубина " + depth.ToString("F4") + " м.", depth);
            });
        private MapEvaluationWork Box(Collider a, Bounds bounds, string id, string other, MapGrowthGeometryDefect kind)
            => new MapEvaluationWork(1, () => {
                var box = NewObject("Ограничение").AddComponent<BoxCollider>();
                try
                {
                    box.size = bounds.size; box.transform.position = bounds.center;
                    if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, box, bounds.center, Quaternion.identity, out _, out float depth) && depth > .001f)
                        Issue(kind, id, other, "Геометрия входит в " + (kind == MapGrowthGeometryDefect.PositionArea ? "область игрока." : "заданный запрет/резерв."), depth);
                }
                finally { Object.DestroyImmediate(box.gameObject); }
            });
        private void Issue(MapGrowthGeometryDefect kind, string id, string other, string message, float depth = 0)
        {
            if (issues.Any(v => v.kind == kind && v.recipeId == id && v.otherId == other)) return;
            issues.Add(new MapGrowthGeometryIssue { kind = kind, recipeId = id, otherId = other, message = message, depth = depth });
        }
        private Shape Build(Collider source, string id)
        {
            var shape = new Shape { source = source, id = id };
            if (source is BoxCollider || source is SphereCollider || source is CapsuleCollider || source is MeshCollider convex && convex.convex)
            { shape.pieces = new[] { source }; return shape; }
            if (!(source is MeshCollider mesh) || mesh.sharedMesh == null) return shape;
            var part = source.GetComponent<BlockoutSectionPart>();
            if (part != null && part.owner != null && (part.owner.transform.lossyScale - Vector3.one).sqrMagnitude < 1e-8f
                && part.owner.TryCopyBuiltSolidParts(part.sectionIndex, out var solids))
            {
                shape.pieces = solids.Select(s => {
                    var box = NewObject("Объём " + id).AddComponent<BoxCollider>();
                    box.size = s.size; box.transform.position = part.owner.transform.TransformPoint(s.center);
                    box.transform.rotation = part.owner.transform.rotation * s.rotation; return (Collider)box;
                }).ToArray(); return shape;
            }
            if (!mesh.sharedMesh.isReadable || !MapGrowthConvexMesh.TryCookableGeometry(mesh.sharedMesh.vertices, mesh.sharedMesh.triangles, out var vertices, out var triangles)) return shape;
            var proxyMesh = new Mesh { name = "Проверенный выпуклый объём " + id, hideFlags = HideFlags.HideAndDontSave };
            proxyMeshes.Add(proxyMesh); proxyMesh.vertices = vertices; proxyMesh.triangles = triangles; proxyMesh.RecalculateBounds();
            var copy = NewObject("Выпуклый объём " + id).AddComponent<MeshCollider>();
            copy.transform.position = source.transform.position; copy.transform.rotation = source.transform.rotation;
            copy.transform.localScale = source.transform.lossyScale; copy.convex = true; copy.sharedMesh = proxyMesh;
            Physics.SyncTransforms();
            if ((copy.bounds.center - source.bounds.center).sqrMagnitude > 1e-6f || (copy.bounds.size - source.bounds.size).sqrMagnitude > 1e-6f)
            { Object.DestroyImmediate(copy.gameObject); return shape; }
            shape.pieces = new Collider[] { copy }; return shape;
        }
        private GameObject NewObject(string name)
        {
            var go = new GameObject(name); go.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(go, proxyScene); return go;
        }
        private bool GroundRectangle(Collider source, out Rect rectangle)
        {
            rectangle = default;
            if (Mathf.Abs(source.bounds.max.y - floorY) > .001f) return false;
            if (source is BoxCollider && Mathf.Abs(Vector3.Dot(source.transform.up, Vector3.up)) > .99999f)
            {
                Vector3 x = source.transform.right;
                if (Mathf.Abs(x.x) > .99999f || Mathf.Abs(x.z) > .99999f)
                { var b = source.bounds; rectangle = new Rect(b.min.x, b.min.z, b.size.x, b.size.z); return true; }
            }
            if (source is MeshCollider mesh && mesh.sharedMesh != null && mesh.sharedMesh.isReadable)
                return MapGrowthFloorSupport.TryRectangle(mesh.sharedMesh.vertices.Select(source.transform.TransformPoint).ToArray(), mesh.sharedMesh.triangles, floorY, out rectangle);
            return false;
        }
        internal static bool RectUnionContains(IReadOnlyList<Rect> source, Rect target)
        {
            var x = new SortedSet<float> { target.xMin, target.xMax };
            foreach (var r in source) { if (r.xMin > target.xMin && r.xMin < target.xMax) x.Add(r.xMin); if (r.xMax > target.xMin && r.xMax < target.xMax) x.Add(r.xMax); }
            var edges = x.ToArray();
            for (int i = 1; i < edges.Length; i++)
            {
                float middle = (edges[i - 1] + edges[i]) * .5f, covered = target.yMin;
                foreach (var r in source.Where(r => r.xMin <= middle && r.xMax >= middle).OrderBy(r => r.yMin))
                { if (r.yMin > covered + .001f) break; covered = Mathf.Max(covered, r.yMax); }
                if (covered < target.yMax - .001f) return false;
            }
            return edges.Length >= 2;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (proxyScene.IsValid() && proxyScene.isLoaded) EditorSceneManager.ClosePreviewScene(proxyScene);
            foreach (var mesh in proxyMeshes) if (mesh != null) Object.DestroyImmediate(mesh);
            proxyMeshes.Clear();
            fixedShapes.Clear(); candidateShapes.Clear();
        }
    }
}
