using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.LevelDesign
{
    /// <summary>Единственный владелец трёх секционных мешей. Исходный профиль сериализован, производные меши восстанавливаются.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BlockoutSectionGeometry : MonoBehaviour
    {
        [Serializable] private sealed class SourceMesh
        {
            public Vector3[] vertices;
            public Vector2[] uv;
            public int[] triangles;
            public float verticalUvScale = 1;
        }
        [SerializeField] private SourceMesh[] sources = Array.Empty<SourceMesh>();
        [SerializeField] private Bounds sourceBounds;
        [SerializeField] private float sourceUvHeight;
        [SerializeField] private Vector3 anchorOffset;
        [SerializeField] private bool anchorInitialized;
        [SerializeField] private bool initialized;
        [SerializeField] private float penetrationModifier = 3;
        public BlockoutSectionPart[] parts = new BlockoutSectionPart[3];
        private readonly Mesh[] generated = new Mesh[3];
        private readonly List<BlockoutSolidPart>[] solidParts = new List<BlockoutSolidPart>[3];
        private string builtRecipe;
        private bool frozen;
        public bool Initialized => initialized;
        public Vector3 AnchorOffset => anchorOffset;
        public bool AnchorInitialized => anchorInitialized;
        /// <summary>Редактор уже перенёс старый прямоугольный корень в центр; новая параметрическая форма сохраняет этот центр.</summary>
        public void AdoptCenteredAnchor() { anchorInitialized = true; builtRecipe = null; }
        public static bool Owns(GameObject root) => root != null && root.GetComponent<BlockoutSectionGeometry>() is BlockoutSectionGeometry geometry && geometry.initialized;

        /// <summary>Неизменяемая копия для оценки: мешами владеет захват, группа сохраняет общую семантику прострела.</summary>
        public void AdoptFrozenParts(BlockoutSectionPart[] capturedParts, BlockoutSolidPart[][] capturedSolids = null)
        {
            if (initialized || capturedParts == null || capturedParts.Length != 3)
                throw new InvalidOperationException("Замороженная группа создаётся один раз до инициализации геометрии.");
            for (int i = 0; i < capturedParts.Length; i++)
                if (capturedParts[i] != null && (capturedParts[i].owner != this || capturedParts[i].sectionIndex != i))
                    throw new ArgumentException("Секция должна принадлежать этой группе и иметь свой индекс.");
            if (capturedSolids != null && capturedSolids.Length != 3) throw new ArgumentException("Нужны три списка объёмов секций.");
            parts = (BlockoutSectionPart[])capturedParts.Clone(); frozen = true; initialized = true;
            for (int i = 0; i < 3; i++) solidParts[i] = capturedSolids?[i] == null ? null : new List<BlockoutSolidPart>(capturedSolids[i]);
        }

        /// <summary>Снимок исходной формы до выключения прежних Renderer/Collider.</summary>
        public void CaptureSource(GameObject source = null)
        {
            source = source != null ? source : gameObject;
            var surface = source.GetComponent<CoverSurface>();
            if (!initialized && surface != null) penetrationModifier = surface.PenetrationModifier;
            if (GetComponent<BlockoutCellWall>() != null || GetComponent<BlockoutSteppedGeometry>() != null)
            { sources = Array.Empty<SourceMesh>(); initialized = true; builtRecipe = null; return; }
            var meshes = new List<SourceMesh>(); bool first = true;
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.GetComponentInParent<BlockoutSectionPart>() != null || filter.sharedMesh == null) continue;
                var mesh = filter.sharedMesh;
                if (!mesh.isReadable) throw new ArgumentException("Исходный меш секций должен быть доступен для чтения: " + mesh.name);
                var matrix = source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var vertices = mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
                foreach (var vertex in vertices)
                { if (first) { sourceBounds = new Bounds(vertex, Vector3.zero); first = false; } else sourceBounds.Encapsulate(vertex); }
                meshes.Add(new SourceMesh { vertices = vertices, uv = mesh.uv, triangles = mesh.triangles });
            }
            if (first && GetComponent<BlockoutCellWall>() == null && GetComponent<BlockoutSteppedGeometry>() == null)
                throw new ArgumentException("Форма не содержит исходного меша для секций.");
            var heightGeometry = source.GetComponent<BlockoutHeightGeometry>();
            sourceUvHeight = heightGeometry != null && heightGeometry.Initialized ? heightGeometry.BaseHeight : sourceBounds.size.y;
            sources = meshes.ToArray(); initialized = true; builtRecipe = null;
        }

        private void OnEnable() { if (initialized) Rebuild(); }
        private void OnDestroy() => ReleaseMeshes();
        public void ReleaseMeshes()
        {
            for (int i = 0; i < generated.Length; i++)
                if (generated[i] != null)
                {
                    if (parts[i] != null)
                    {
                        parts[i].SetMesh(null);
                        var collider = parts[i].GetComponent<MeshCollider>();
                        if (collider.sharedMesh == generated[i]) collider.sharedMesh = null;
                        var filter = parts[i].GetComponent<MeshFilter>();
                        if (filter.sharedMesh == generated[i]) filter.sharedMesh = null;
                    }
                    if (Application.isPlaying) Destroy(generated[i]); else DestroyImmediate(generated[i]);
                    generated[i] = null;
                }
            builtRecipe = null;
        }

        public void Rebuild()
        {
            if (frozen) return;
            var instance = GetComponent<BlockoutBlockInstance>();
            if (!initialized || instance == null || !instance.HasSections || parts.Length != 3 || parts.Any(p => p == null)) return;
            // Поза принадлежит корню даже при прямой правке Transform дочернего объекта.
            // Восстановление выполняется и при попадании в кеш геометрии.
            foreach (var part in parts)
            { part.transform.localPosition = Vector3.zero; part.transform.localRotation = Quaternion.identity; part.transform.localScale = Vector3.one; }
            var wall = GetComponent<BlockoutCellWall>(); var stepped = GetComponent<BlockoutSteppedGeometry>();
            UpdateAnchor(instance, wall, stepped);
            string recipe = CurrentRecipe(instance, wall, stepped);
            bool intact = builtRecipe == recipe;
            for (int i = 0; i < 3 && intact; i++)
                intact = generated[i] != null && parts[i].GetComponent<MeshFilter>().sharedMesh == generated[i]
                    && (generated[i].vertexCount == 0 ? parts[i].GetComponent<MeshCollider>().sharedMesh == null : parts[i].GetComponent<MeshCollider>().sharedMesh == generated[i]);
            if (intact) return;
            ReleaseMeshes();
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                if (renderer.GetComponentInParent<BlockoutSectionPart>() == null) renderer.enabled = false;
            foreach (var collider in GetComponentsInChildren<Collider>(true))
                if (collider.GetComponentInParent<BlockoutSectionPart>() == null) collider.enabled = false;
            for (int i = 0; i < 3; i++)
            {
                var part = parts[i]; part.owner = this; part.sectionIndex = i;
                part.transform.localPosition = Vector3.zero; part.transform.localRotation = Quaternion.identity; part.transform.localScale = Vector3.one;
                var settings = instance.sections[i];
                part.name = BlockoutSectionSettings.ChildName(i) + "_" + settings.material;
                float bottom = BlockoutSectionSettings.Bottom(i), top = Mathf.Min(BlockoutSectionSettings.Top(i), instance.dimensions.y);
                var builder = new MeshBuilder(); solidParts[i] = new List<BlockoutSolidPart>();
                if (top > bottom + .000001f)
                {
                    if (wall != null) BuildWall(builder, wall, settings.openings, instance.dimensions, bottom, top, solidParts[i]);
                    else if (stepped != null)
                    {
                        var source = BlockoutSteppedGeometry.BuildMesh(instance.dimensions.y, stepped.baseSize, stepped.topSize,
                            new Vector3(stepped.localBottomCenter.x, 0, stepped.localBottomCenter.z), stepped.lowerHeightLimit);
                        try { Slice(builder, new SourceMesh { vertices = source.vertices, uv = source.uv, triangles = source.triangles }, bottom, top); }
                        finally { if (Application.isPlaying) Destroy(source); else DestroyImmediate(source); }
                        foreach (var bounds in stepped.LocalPartsAtHeight(instance.dimensions.y))
                        {
                            float y = Mathf.Max(bounds.min.y, bottom), Y = Mathf.Min(bounds.max.y, top);
                            if (Y > y + .000001f) solidParts[i].Add(new BlockoutSolidPart {
                                center = new Vector3(bounds.center.x, (y + Y) / 2, bounds.center.z), size = new Vector3(bounds.size.x, Y - y, bounds.size.z), rotation = Quaternion.identity });
                        }
                    }
                    else
                    {
                        foreach (var source in sources)
                        {
                            var scaled = new SourceMesh { vertices = source.vertices.Select(p => new Vector3(p.x,
                                (p.y - sourceBounds.min.y) * instance.dimensions.y / sourceBounds.size.y, p.z)).ToArray(), uv = source.uv, triangles = source.triangles,
                                verticalUvScale = instance.dimensions.y / (sourceUvHeight > 0 ? sourceUvHeight : sourceBounds.size.y) };
                            Slice(builder, scaled, bottom, top);
                        }
                    }
                }
                builder.Translate(-anchorOffset);
                for (int s = 0; s < solidParts[i].Count; s++) { var solid = solidParts[i][s]; solid.center -= anchorOffset; solidParts[i][s] = solid; }
                var mesh = builder.Create("Блокаут · " + BlockoutSectionSettings.ChildName(i));
                mesh.hideFlags = HideFlags.HideAndDontSave; generated[i] = mesh;
                part.SetMesh(mesh);
                var filter = part.GetComponent<MeshFilter>(); filter.sharedMesh = mesh;
                var collider = part.GetComponent<MeshCollider>(); collider.sharedMesh = null; collider.convex = false;
                if (mesh.vertexCount > 0) collider.sharedMesh = mesh;
                var renderer = part.GetComponent<MeshRenderer>(); bool present = mesh.vertexCount > 0;
                renderer.enabled = present; collider.enabled = present;
                var cover = part.GetComponent<CoverSurface>(); cover.Class = settings.material; cover.PenetrationModifier = penetrationModifier;
            }
            builtRecipe = CurrentRecipe(instance, wall, stepped);
        }

        private string CurrentRecipe(BlockoutBlockInstance instance, BlockoutCellWall wall, BlockoutSteppedGeometry stepped)
            => JsonUtility.ToJson(instance) + JsonUtility.ToJson(this)
                + (wall != null ? JsonUtility.ToJson(wall) : "") + (stepped != null ? JsonUtility.ToJson(stepped) : "");

        /// <summary>Только чтение уже построенного точного разбиения; не перестраивает рабочую сцену.</summary>
        public bool TryCopyBuiltSolidParts(int index, out BlockoutSolidPart[] result)
        {
            result = null;
            if (index < 0 || index > 2 || !initialized || parts == null || parts.Length != 3 || parts[index] == null
                || solidParts[index] == null || solidParts[index].Count == 0) return false;
            if (!frozen)
            {
                var instance = GetComponent<BlockoutBlockInstance>();
                if (instance == null || builtRecipe != CurrentRecipe(instance, GetComponent<BlockoutCellWall>(), GetComponent<BlockoutSteppedGeometry>())
                    || generated[index] == null || parts[index].GetComponent<MeshFilter>().sharedMesh != generated[index]
                    || parts[index].GetComponent<MeshCollider>().sharedMesh != generated[index]
                    || parts[index].transform.localPosition != Vector3.zero || parts[index].transform.localRotation != Quaternion.identity
                    || parts[index].transform.localScale != Vector3.one) return false;
            }
            result = solidParts[index].ToArray(); return true;
        }

        public IEnumerable<BlockoutSolidPart> LocalSolidParts()
        {
            Rebuild();
            return solidParts.Where(p => p != null).SelectMany(p => p);
        }

        private void UpdateAnchor(BlockoutBlockInstance instance, BlockoutCellWall wall, BlockoutSteppedGeometry stepped)
        {
            Bounds bounds = sourceBounds;
            if (wall != null)
            {
                bool first = true;
                foreach (var part in BlockoutCellWall.SolidParts(wall.cells, Vector3.zero, wall.cellSize, instance.dimensions.y,
                    default, wall.geometryMode, instance.dimensions.x, instance.dimensions.z, wall.yaw))
                { if (first) { bounds = part.BroadphaseBounds; first = false; } else bounds.Encapsulate(part.BroadphaseBounds); }
            }
            else if (stepped != null)
                bounds = new Bounds(stepped.localBottomCenter, new Vector3(stepped.baseSize.x, 0, stepped.baseSize.y));
            var center = new Vector3(bounds.center.x, 0, bounds.center.z);
            if (!anchorInitialized)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying && (gameObject.hideFlags & HideFlags.DontSave) == 0
                    && !UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene))
                    UnityEditor.Undo.RecordObjects(new UnityEngine.Object[] { transform, this }, "Центрировать нижний якорь блока");
#endif
                // Первая миграция сохраняет каждый мировой треугольник; дальнейшее изменение размера сохраняет центр корня.
                transform.position += transform.TransformVector(center - anchorOffset); anchorInitialized = true;
            }
            anchorOffset = center;
        }
        public IEnumerable<BlockoutSolidPart> SectionSolidParts(int index)
        {
            Rebuild(); return solidParts[index] != null ? (IEnumerable<BlockoutSolidPart>)solidParts[index] : Enumerable.Empty<BlockoutSolidPart>();
        }

        private static void BuildWall(MeshBuilder builder, BlockoutCellWall wall, BlockoutOpeningSettings openings,
            Vector3 dimensions, float bottom, float top, List<BlockoutSolidPart> solids)
        {
            var basis = BlockoutCellWall.SolidParts(wall.cells, Vector3.zero, wall.cellSize, dimensions.y,
                default, wall.geometryMode, dimensions.x, dimensions.z, 0).Select(p => new Bounds(p.center, p.size)).ToArray();
            if (basis.Length == 0) return;
            var xs = new SortedSet<float>(); var zs = new SortedSet<float>();
            foreach (var b in basis) { xs.Add(b.min.x); xs.Add(b.max.x); zs.Add(b.min.z); zs.Add(b.max.z); }
            bool alongX = wall.geometryMode == BlockoutGeometryMode.ThinStraight || xs.Max - xs.Min >= zs.Max - zs.Min;
            var gaps = new List<Vector2>();
            if (openings.enabled)
            {
                var axis = alongX ? xs : zs; float low = axis.Min, high = axis.Max;
                for (float center = low + openings.spacing / 2; center + openings.width / 2 < high - .001f; center += openings.spacing)
                {
                    float a = center - openings.width / 2, b = center + openings.width / 2;
                    if (a <= low + .001f) continue;
                    axis.Add(a); axis.Add(b); gaps.Add(new Vector2(a, b));
                }
            }
            var ys = new SortedSet<float> { bottom, top };
            if (openings.enabled) { ys.Add(bottom + openings.sillHeight); ys.Add(top - openings.lintelHeight); }
            float[] x = xs.ToArray(), z = zs.ToArray(), y = ys.Where(v => v >= bottom && v <= top).ToArray();
            var occupied = new bool[x.Length - 1, y.Length - 1, z.Length - 1];
            Quaternion rotation = wall.geometryMode == BlockoutGeometryMode.ThinStraight ? Quaternion.Euler(0, wall.yaw, 0) : Quaternion.identity;
            for (int ix = 0; ix < x.Length - 1; ix++) for (int iy = 0; iy < y.Length - 1; iy++) for (int iz = 0; iz < z.Length - 1; iz++)
            {
                var center = new Vector3((x[ix] + x[ix + 1]) / 2, (y[iy] + y[iy + 1]) / 2, (z[iz] + z[iz + 1]) / 2);
                bool inside = basis.Any(b => center.x > b.min.x - .000001f && center.x < b.max.x + .000001f && center.z > b.min.z - .000001f && center.z < b.max.z + .000001f);
                float axis = alongX ? center.x : center.z;
                bool gap = openings.enabled && center.y > bottom + openings.sillHeight && center.y < top - openings.lintelHeight && gaps.Any(g => axis > g.x && axis < g.y);
                occupied[ix, iy, iz] = inside && !gap;
                if (inside && !gap) solids.Add(new BlockoutSolidPart { center = rotation * center,
                    size = new Vector3(x[ix + 1] - x[ix], y[iy + 1] - y[iy], z[iz + 1] - z[iz]), rotation = rotation });
            }
            bool Has(int a, int b, int c) => a >= 0 && b >= 0 && c >= 0 && a < x.Length - 1 && b < y.Length - 1 && c < z.Length - 1 && occupied[a, b, c];
            for (int ix = 0; ix < x.Length - 1; ix++) for (int iy = 0; iy < y.Length - 1; iy++) for (int iz = 0; iz < z.Length - 1; iz++)
            {
                if (!Has(ix, iy, iz)) continue;
                float a = x[ix], A = x[ix + 1], b = y[iy], B = y[iy + 1], c = z[iz], C = z[iz + 1];
                void Face(Vector3 p, Vector3 q, Vector3 r, Vector3 s) => builder.Quad(p, q, r, s, rotation);
                if (!Has(ix, iy - 1, iz)) Face(new Vector3(a,b,c),new Vector3(A,b,c),new Vector3(A,b,C),new Vector3(a,b,C));
                if (!Has(ix, iy + 1, iz)) Face(new Vector3(a,B,C),new Vector3(A,B,C),new Vector3(A,B,c),new Vector3(a,B,c));
                if (!Has(ix, iy, iz - 1)) Face(new Vector3(a,B,c),new Vector3(A,B,c),new Vector3(A,b,c),new Vector3(a,b,c));
                if (!Has(ix, iy, iz + 1)) Face(new Vector3(a,b,C),new Vector3(A,b,C),new Vector3(A,B,C),new Vector3(a,B,C));
                if (!Has(ix - 1, iy, iz)) Face(new Vector3(a,b,c),new Vector3(a,b,C),new Vector3(a,B,C),new Vector3(a,B,c));
                if (!Has(ix + 1, iy, iz)) Face(new Vector3(A,B,c),new Vector3(A,B,C),new Vector3(A,b,C),new Vector3(A,b,c));
            }
        }

        private struct Vertex { public Vector3 p; public Vector2 uv; }
        private static void Slice(MeshBuilder builder, SourceMesh source, float bottom, float top)
        {
            if (source.vertices.Length == 0) return;
            float min = SnapPlane(source.vertices.Min(p => p.y), bottom, top), max = SnapPlane(source.vertices.Max(p => p.y), bottom, top);
            var bottomEdge = new List<Vector3>(); var topEdge = new List<Vector3>();
            for (int i = 0; i < source.triangles.Length; i += 3)
            {
                var polygon = new List<Vertex>();
                for (int j = 0; j < 3; j++)
                {
                    int index = source.triangles[i + j]; var p = source.vertices[index];
                    // Bounds.center + size/2 может дать 1.6000001 вместо 1.6. Та же
                    // каноническая плоскость должна участвовать в Clip и выборе крышки.
                    p.y = SnapPlane(p.y, bottom, top);
                    polygon.Add(new Vertex { p = p, uv = source.uv != null && source.uv.Length == source.vertices.Length ? source.uv[index] : new Vector2(p.x + p.z, p.y) / 1.8f });
                }
                bool flat = Mathf.Abs(polygon[0].p.y - polygon[1].p.y) < .000001f && Mathf.Abs(polygon[0].p.y - polygon[2].p.y) < .000001f;
                // Крышки используют X/Z, а вертикальный UV сохраняет метрическую сетку при изменении высоты.
                if (!flat && source.verticalUvScale != 1)
                    for (int j = 0; j < polygon.Count; j++) { var vertex = polygon[j]; vertex.uv.y *= source.verticalUvScale; polygon[j] = vertex; }
                if (flat && ((bottom > min + .000001f && Mathf.Abs(polygon[0].p.y - bottom) < .000001f)
                    || (top < max - .000001f && Mathf.Abs(polygon[0].p.y - top) < .000001f))) continue;
                polygon = Clip(Clip(polygon, bottom, true), top, false);
                if (polygon.Count < 3) continue;
                // Касание плоскости (например, широкий низ Step у 1.2 м) не является гранью нового среза.
                bool hasArea = false;
                for (int j = 1; j < polygon.Count - 1; j++)
                    hasArea |= Vector3.Cross(polygon[j].p - polygon[0].p, polygon[j + 1].p - polygon[0].p).sqrMagnitude >= .000000000001f;
                if (!hasArea) continue;
                if (!flat)
                    foreach (var vertex in polygon)
                    { if (Mathf.Abs(vertex.p.y - bottom) < .000001f) bottomEdge.Add(vertex.p); if (Mathf.Abs(vertex.p.y - top) < .000001f) topEdge.Add(vertex.p); }
                for (int j = 1; j < polygon.Count - 1; j++) builder.Triangle(polygon[0].p, polygon[j].p, polygon[j + 1].p, polygon[0].uv, polygon[j].uv, polygon[j + 1].uv);
            }
            if (bottom > min + .000001f) Cap(builder, bottomEdge, false);
            if (top < max - .000001f) Cap(builder, topEdge, true);
        }
        private static float SnapPlane(float y, float bottom, float top)
        {
            if (Mathf.Abs(y - bottom) <= .000001f) return bottom;
            if (Mathf.Abs(y - top) <= .000001f) return top;
            return y;
        }
        private static List<Vertex> Clip(List<Vertex> input, float plane, bool above)
        {
            var output = new List<Vertex>(); if (input.Count == 0) return output;
            Vertex previous = input[input.Count - 1]; bool previousInside = above ? previous.p.y >= plane : previous.p.y <= plane;
            foreach (var current in input)
            {
                bool inside = above ? current.p.y >= plane : current.p.y <= plane;
                if (inside != previousInside)
                {
                    float t = (plane - previous.p.y) / (current.p.y - previous.p.y);
                    var point = Vector3.LerpUnclamped(previous.p, current.p, t); point.y = plane;
                    output.Add(new Vertex { p = point, uv = Vector2.LerpUnclamped(previous.uv, current.uv, t) });
                }
                if (inside) output.Add(current);
                previous = current; previousInside = inside;
            }
            for (int i = output.Count - 1; i > 0; i--)
                if ((output[i].p - output[i - 1].p).sqrMagnitude < .0000000001f) output.RemoveAt(i);
            if (output.Count > 1 && (output[0].p - output[output.Count - 1].p).sqrMagnitude < .0000000001f) output.RemoveAt(output.Count - 1);
            return output;
        }
        /// <summary>Активные неклеточные формы имеют выпуклое горизонтальное сечение; исходный обод сохраняется.</summary>
        private static void Cap(MeshBuilder builder, List<Vector3> points, bool up)
        {
            var sorted = points.OrderBy(p => p.x).ThenBy(p => p.z).ToArray(); var unique = new List<Vector3>();
            foreach (var p in sorted) if (unique.Count == 0 || (unique[unique.Count - 1] - p).sqrMagnitude > .0000000001f) unique.Add(p);
            if (unique.Count < 3) return;
            float Cross(Vector3 a, Vector3 b, Vector3 c) => (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
            var hull = new List<Vector3>();
            foreach (var p in unique) { while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= .0000001f) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
            int lower = hull.Count;
            for (int i = unique.Count - 2; i >= 0; i--) { var p = unique[i]; while (hull.Count > lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= .0000001f) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
            hull.RemoveAt(hull.Count - 1);
            // Крышка сохраняет все точки разреза на прямых рёбрах, включая пересечение диагонали.
            // Без них длинное ребро крышки образует T-стык с двумя рёбрами боковой грани.
            var outline = new List<Vector3>();
            for (int i = 0; i < hull.Count; i++)
            {
                var a = hull[i]; var b = hull[(i + 1) % hull.Count]; var edge = b - a; float length = edge.sqrMagnitude;
                var candidates = unique.Where(p => Mathf.Abs(Cross(a, b, p)) < .0000001f
                    && Vector3.Dot(p - a, edge) >= -.0000001f && Vector3.Dot(p - a, edge) < length - .0000001f)
                    .OrderBy(p => Vector3.Dot(p - a, edge));
                outline.AddRange(candidates);
            }
            var center = Vector3.zero; foreach (var p in hull) center += p; center /= hull.Count;
            for (int i = 0; i < outline.Count; i++)
            {
                var a = center; var b = outline[i]; var c = outline[(i + 1) % outline.Count];
                if (up) { var swap = b; b = c; c = swap; }
                builder.Triangle(a, b, c, new Vector2(a.x, a.z) / 1.8f, new Vector2(b.x, b.z) / 1.8f, new Vector2(c.x, c.z) / 1.8f);
            }
        }

        private sealed class MeshBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector2> uv = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();
            public void Translate(Vector3 offset) { for (int i = 0; i < vertices.Count; i++) vertices[i] += offset; }
            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector2 A, Vector2 B, Vector2 C)
            {
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < .000000000001f) return;
                int i = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); uv.Add(A); uv.Add(B); uv.Add(C); triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Quaternion rotation)
            {
                bool horizontal = Mathf.Abs(a.y - b.y) + Mathf.Abs(a.y - c.y) < .000001f;
                Vector2 UV(Vector3 p) => horizontal ? new Vector2(p.x, p.z) / 1.8f : new Vector2(p.x + p.z, p.y) / 1.8f;
                Triangle(rotation * a, rotation * b, rotation * c, UV(a), UV(b), UV(c)); Triangle(rotation * a, rotation * c, rotation * d, UV(a), UV(c), UV(d));
            }
            public Mesh Create(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
                if (vertices.Count > 0) { mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents(); }
                return mesh;
            }
        }
    }
}
