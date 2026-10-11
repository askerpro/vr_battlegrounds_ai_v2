using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Статичная мебель вдоль закрытых боковин; размеры и запретные объёмы задаёт главный авторинг.</summary>
    public static class LobbySidewallDecorationBuilder
    {
        private const string ReportPath = "tasks/lobby-decoration/reports/implementation/interior-sidewalls/latest-build.json";
        [Serializable] public sealed class Placement
        {
            public string name, sourceGuid, decision;
            public Bounds bounds;
            public Vector3 position;
            public int lodLevels;
        }
        [Serializable] public sealed class BuildReport
        {
            public Bounds arena, tent;
            public int placed, excluded, missing, invalid;
            public List<Placement> placements = new List<Placement>();
        }
        public static BuildReport LastReport { get; private set; }

        private readonly struct Source
        {
            public readonly string Guid, Name;
            public readonly float Height;
            public Source(string guid, string name, float height) { Guid = guid; Name = name; Height = height; }
        }
        private readonly struct Candidate
        {
            public readonly Source Source;
            public readonly float X, Z, Yaw;
            public Candidate(Source source, float x, float z, float yaw) { Source = source; X = x; Z = z; Yaw = yaw; }
        }

        public static void Build(Transform parent, Bounds arena, Bounds tent, Bounds[] exclusions)
        {
            if (parent == null || !parent.gameObject.scene.IsValid()) throw new ArgumentException("Нужен родитель сцены.");
            if (parent.Find("SidewallDecoration") != null) throw new InvalidOperationException("SidewallDecoration уже создана главным авторингом.");
            if (tent.size.x <= 2 || tent.size.z <= 2 || tent.min.x < arena.min.x - .001f || tent.max.x > arena.max.x + .001f
                || tent.min.z < arena.min.z - .001f || tent.max.z > arena.max.z + .001f)
                throw new InvalidOperationException("Шатёр должен быть внутри измеренной арены.");
            LastReport = new BuildReport { arena = arena, tent = tent };
            var root = new GameObject("SidewallDecoration"); root.transform.SetParent(parent, false);
            var occupied = new List<Bounds>(exclusions ?? Array.Empty<Bounds>());
            // Эти согласованные ячейки соответствуют сценовым вырезам старых столов; не ищем место в проходе.
            var candidates = new[]
            {
                new Candidate(new Source("abc00000000018269680259753661502", "Западный шкаф одежды", 1.95f), -7.28f, -4.7675f, 92),
                new Candidate(new Source("abc00000000009853783468641101578", "Западный полевой стол", .86f), -7.28f, -5.925f, 90),
                new Candidate(new Source("abc00000000009853783468641101578", "Восточный полевой стол", .86f), 7.28f, 6.25f, -90)
            };
            try
            {
                for (int i = 0; i < candidates.Length; i++)
                    {
                        var candidate = candidates[i];
                        Source source = candidate.Source;
                        string assetPath = AssetDatabase.GUIDToAssetPath(source.Guid);
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                        if (prefab == null)
                        {
                            LastReport.missing++;
                            LastReport.placements.Add(new Placement { name = source.Name, sourceGuid = source.Guid, decision = "missing_source: импорт принадлежит владельцу общего пакета" });
                            continue;
                        }
                        var bottom = new Vector3(arena.center.x + candidate.X, arena.max.y + .005f, arena.center.z + candidate.Z);
                        var item = InstantiateFitted(prefab, root.transform, source.Name,
                            bottom, Quaternion.Euler(0, candidate.Yaw, 0), new Vector3(.72f, source.Height, 1.10f));
                        var bounds = VisibleBounds(item);
                        if (!Inside(tent, bounds, .04f) || bounds.min.y < arena.max.y - .002f || bounds.size.y < .15f)
                        {
                            LastReport.invalid++;
                            LastReport.placements.Add(new Placement { name = item.name, sourceGuid = source.Guid, position = bottom, bounds = bounds, decision = "invalid_bounds: не помещается в шатёр" });
                            UnityEngine.Object.DestroyImmediate(item); continue;
                        }
                        if (Overlaps(bounds, occupied, .055f))
                        {
                            LastReport.excluded++;
                            LastReport.placements.Add(new Placement { name = item.name, sourceGuid = source.Guid, position = bottom, bounds = bounds, decision = "excluded: станция/окно/зеркало/колонна/соседняя мебель" });
                            UnityEngine.Object.DestroyImmediate(item); continue;
                        }
                        LastReport.placed++; occupied.Add(bounds);
                        LastReport.placements.Add(new Placement { name = item.name, sourceGuid = source.Guid, position = bottom, bounds = bounds,
                            lodLevels = item.GetComponentsInChildren<LODGroup>(true).Sum(l => l.GetLODs().Length), decision = "placed" });
                        if (source.Name.EndsWith("полевой стол", StringComparison.Ordinal))
                        {
                            AddSupplies(item.transform, bounds, tent, occupied, i);
                            AddPersonalDetails(item.transform, bounds, tent, occupied, candidate.X < 0);
                        }
                    }
            }
            finally { SaveReport(); }
            GameLog.Debug.Info($"[LobbyDecoration] Боковая мебель: {LastReport.placed}, исключено {LastReport.excluded}, нет источников {LastReport.missing}; {ReportPath}");
        }

        private static void AddSupplies(Transform parent, Bounds surface, Bounds tent, List<Bounds> occupied, int variation)
        {
            string guid = variation % 2 == 0 ? "abc00000000007223829749494322384" : "abc00000000012017236203265661070";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null) { LastReport.missing++; LastReport.placements.Add(new Placement { name = "Настольные припасы", sourceGuid = guid, decision = "missing_source" }); return; }
            var bottom = new Vector3(surface.center.x, surface.max.y + .006f, surface.center.z + .16f);
            var item = InstantiateFitted(prefab, parent, "Оставленные припасы", bottom, Quaternion.Euler(0, variation * 9, 0), new Vector3(.30f, .20f, .38f));
            var b = VisibleBounds(item);
            // Верх стола — опора, а не препятствие; остальная мебель и внешние исключения остаются запрещены.
            var obstacles = occupied.Where(o => o != surface).ToList();
            if (!Inside(tent, b, .04f) || b.max.y > 1.19f || Overlaps(b, obstacles, .045f)
                || b.min.x < surface.min.x + .03f || b.max.x > surface.max.x - .03f
                || b.min.z < surface.min.z + .03f || b.max.z > surface.max.z - .03f)
            { UnityEngine.Object.DestroyImmediate(item); LastReport.excluded++; return; }
            occupied.Add(b); LastReport.placed++;
            LastReport.placements.Add(new Placement { name = item.name, sourceGuid = guid, position = bottom, bounds = b,
                lodLevels = item.GetComponentsInChildren<LODGroup>(true).Sum(l => l.GetLODs().Length), decision = "placed_on_surface" });
        }

        private static void AddPersonalDetails(Transform parent, Bounds surface, Bounds tent, List<Bounds> occupied, bool west)
        {
            const string mug = "abc00000000004222768201726487176";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(mug)) == null)
            { LastReport.missing++; LastReport.placements.Add(new Placement { name = "Личные мелочи нового стола", sourceGuid = mug, decision = "missing_source" }); return; }
            var item = LobbyInteriorDecorationBuilder.CreateDeskPersonalDetails(parent, surface, west);
            var b = VisibleBounds(item);
            if (!Inside(tent, b, .04f) || b.max.y > 1.19f || Overlaps(b, occupied.Where(o => o != surface), .045f)
                || b.min.x < surface.min.x + .025f || b.max.x > surface.max.x - .025f
                || b.min.z < surface.min.z + .025f || b.max.z > surface.max.z - .025f)
            {
                LastReport.excluded++;
                LastReport.placements.Add(new Placement { name = item.name, bounds = b, position = item.transform.position, decision = "excluded_personal_surface: опора/окно/пересечение" });
                UnityEngine.Object.DestroyImmediate(item); return;
            }
            LastReport.placed++; occupied.Add(b);
            LastReport.placements.Add(new Placement { name = item.name, bounds = b, position = item.transform.position, decision = "placed_personal_surface" });
        }

        internal static GameObject InstantiateFitted(GameObject prefab, Transform parent, string name, Vector3 bottom, Quaternion rotation, Vector3 maximumSize)
        {
            // Все source LOD сохраняются; никаких ручных обрезаний массива LOD или замены материала.
            var holder = new GameObject(name); holder.transform.SetParent(parent, false);
            holder.transform.SetPositionAndRotation(bottom, rotation);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            foreach (var component in instance.GetComponentsInChildren<Component>(true))
                if (!(component is Transform) && !(component is MeshFilter) && !(component is MeshRenderer) && !(component is LODGroup))
                    UnityEngine.Object.DestroyImmediate(component);
            var filters = instance.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToArray();
            if (filters.Length == 0) { UnityEngine.Object.DestroyImmediate(holder); throw new InvalidOperationException("В источнике нет static Mesh: " + prefab.name); }
            bool first = true; Bounds local = default;
            foreach (var filter in filters)
                EncapsulateMesh(filter, holder.transform, ref local, ref first);
            if (local.size.x <= 0 || local.size.y <= 0 || local.size.z <= 0)
            { UnityEngine.Object.DestroyImmediate(holder); throw new InvalidOperationException("Некорректный источник: " + prefab.name); }
            // Ограничения заданы в world XYZ, а holder может поворачиваться: переводим world envelope в его оси.
            Vector3 axisX = rotation * Vector3.right, axisZ = rotation * Vector3.forward;
            float worldX = Mathf.Abs(axisX.x) * local.size.x + Mathf.Abs(axisZ.x) * local.size.z;
            float worldZ = Mathf.Abs(axisX.z) * local.size.x + Mathf.Abs(axisZ.z) * local.size.z;
            float scale = Mathf.Min(maximumSize.x / worldX, maximumSize.y / local.size.y, maximumSize.z / worldZ);
            instance.transform.localScale = Vector3.one * scale;
            instance.transform.localPosition = new Vector3(-local.center.x, -local.min.y, -local.center.z) * scale;
            foreach (var node in instance.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(node.gameObject, StaticEditorFlags.OccludeeStatic);
            foreach (var lod in instance.GetComponentsInChildren<LODGroup>(true)) lod.RecalculateBounds();
            return holder;
        }

        internal static Bounds VisibleBounds(GameObject item)
        {
            bool first = true; Bounds b = default;
            foreach (var filter in item.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null))
                EncapsulateMesh(filter, null, ref b, ref first);
            if (first) throw new InvalidOperationException("Нет видимой геометрии: " + item.name);
            return b;
        }
        private static void EncapsulateMesh(MeshFilter filter, Transform relative, ref Bounds result, ref bool first)
        {
            var b = filter.sharedMesh.bounds;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            {
                Vector3 point = filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z)));
                if (relative != null) point = relative.InverseTransformPoint(point);
                if (first) { result = new Bounds(point, Vector3.zero); first = false; } else result.Encapsulate(point);
            }
        }
        internal static bool Inside(Bounds tent, Bounds b, float margin) => b.min.x >= tent.min.x + margin && b.max.x <= tent.max.x - margin
            && b.min.z >= tent.min.z + margin && b.max.z <= tent.max.z - margin;
        internal static bool Overlaps(Bounds b, IEnumerable<Bounds> exclusions, float margin)
        {
            b.Expand(margin * 2);
            return exclusions.Any(b.Intersects);
        }
        private static void SaveReport()
        {
            string path = Path.Combine(Application.dataPath, "../" + ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(LastReport, true));
        }
    }
}
