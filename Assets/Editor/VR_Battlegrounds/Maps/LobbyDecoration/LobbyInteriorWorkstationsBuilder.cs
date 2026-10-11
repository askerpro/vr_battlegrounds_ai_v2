using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Полноразмерные металлические полки и компактные склады кейсов у арсеналов.</summary>
    public static class LobbyInteriorWorkstationsBuilder
    {
        private const string ReportPath = "tasks/lobby-decoration/reports/implementation/interior-workstations/latest-build.json";
        [Serializable] public sealed class Placement
        {
            public string name, source, guid, decision;
            public Bounds bounds;
            public Bounds nativeBounds;
            public Bounds blockingBounds;
            public Bounds contactBounds;
            public int lodLevels;
            public float uniformScale;
        }
        [Serializable] public sealed class Report
        {
            public Bounds arena, tent;
            public int cabinets, westCases, eastCases, existingWorktables, rejected, missing;
            public int westFullSizeCases, eastFullSizeCases, existingClosedCabinets;
            public bool minimumCompositionMet;
            public Bounds westSupport;
            public List<Placement> placements = new List<Placement>();
            public List<Placement> tableStructures = new List<Placement>();
        }
        public static Report LastReport { get; private set; }

        public static void Build(Transform parent, Bounds arena, Bounds tent, Bounds[] exclusions)
        {
            if (parent == null || !parent.gameObject.scene.IsValid()) throw new ArgumentException("Нужен родитель сцены.");
            if (parent.Find("InteriorWorkstations") != null) throw new InvalidOperationException("InteriorWorkstations уже существует.");
            var root = new GameObject("InteriorWorkstations"); root.transform.SetParent(parent, false);
            LastReport = new Report { arena = arena, tent = tent };
            var obstacles = new List<Bounds>(exclusions ?? Array.Empty<Bounds>());
            foreach (var collider in parent.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Collider>(true)))
                if (collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy && HasTableAncestor(collider.transform))
                {
                    obstacles.Add(collider.bounds);
                    LastReport.tableStructures.Add(new Placement { name = collider.name, bounds = collider.bounds,
                        decision = "fixed_table_physics: сохранённый активный коллайдер" });
                }
            var sidewall = parent.Find("SidewallDecoration");
            var eastTable = sidewall == null ? null : sidewall.Find("Восточный полевой стол");
            var tableVolumes = SourceTableStructures(eastTable);
            // Существующие визуальные группы тоже препятствия: старые кресла/жилеты переносит только интегратор.
            foreach (var groupName in new[] { "SidewallDecoration", "InteriorDecoration" })
            {
                var group = parent.name == groupName ? parent : parent.Find(groupName);
                if (group == null) continue;
                foreach (var filter in group.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null))
                {
                    // Воздух под проверенным исходным столом не считается сплошной стеной.
                    if (tableVolumes != null && filter.transform.IsChildOf(eastTable)) continue;
                    obstacles.Add(MeshBounds(filter));
                }
            }
            if (tableVolumes != null) obstacles.AddRange(tableVolumes);
            LastReport.existingWorktables = CountTables(parent);
            try
            {
                float y = arena.max.y + .005f;
                // Это открытые полки, а не закрытые комоды. Конфликт не разрешаем уменьшением мебели.
                var eastCabinet = Put(root.transform, "Восточные металлические открытые полки", "SM_cabinet_a",
                    new Vector3(tent.max.x - .47f, y, arena.center.z + 4.85f), 90, Vector3.one * 10, tent, obstacles, null, .9f);
                LastReport.cabinets = eastCabinet != null ? 1 : 0;
                LastReport.existingClosedCabinets = sidewall == null ? 0 : sidewall.GetComponentsInChildren<Transform>(true)
                    .Count(t => t.gameObject.activeInHierarchy && t.name == "Западный шкаф одежды" && t.GetComponentsInChildren<MeshRenderer>(true).Length > 0);

                // Западный склад опирается на оставленный сегмент стола. Опору исключаем только для его содержимого.
                var westSupport = FindSurface(parent, arena.center.x - 7.48f, arena.center.z + 6.45f);
                if (westSupport.HasValue)
                {
                    Bounds support = westSupport.Value;
                    LastReport.westSupport = support;
                    var surfaceObstacles = obstacles.Where(b => !SameSupport(b, support)).ToList();
                    // Ручки не образуют плоскую опору: четыре отдельных кейса вместо парящих стопок.
                    for (int i = 0; i < 4; i++)
                        LastReport.westCases += Case(root.transform, "Западный арсенал, кейс " + (i + 1),
                            i % 2 == 0 ? "SM_AmmoCase_01" : "SM_AmmoCase_02", -7.48f, 6.38f + i * .18f,
                            support.max.y, 90, arena, tent, surfaceObstacles, support);
                }
                else Reject("Западный склад кейсов", "missing_support: нет активной столешницы у арсенала");
                // Проверяем четыре одиночных места. Занятые места не заменяем башней или уменьшением кейсов.
                for (int column = 0; column < 2; column++)
                    LastReport.eastCases += Case(root.transform, "Восточный арсенал, патронный кейс " + (column + 1),
                        "SM_AmmoCase_01", 6.99f + column * .44f, 5.60f,
                        arena.max.y, -90, arena, tent, obstacles, null);
                float[] westZ = { 4.65f, 5.60f, 6.55f };
                for (int i = 0; i < westZ.Length; i++)
                    LastReport.westFullSizeCases += FullCase(root.transform, "Западный арсенал, большой военный кейс " + (i + 1),
                        -6.99f, westZ[i], 0, arena, tent, obstacles);
                if (tableVolumes == null) Reject("Восточный кейс под столом", "missing_structure: источник/ножки реального SM_Table не подтверждены");
                else LastReport.eastFullSizeCases += FullCase(root.transform, "Восточный арсенал, большой кейс под столом",
                    7.03f, 6.25f, 90, arena, tent, obstacles);
                LastReport.placements.Add(new Placement { name = "Восточная ниша у северного торца",
                    decision = "reserved_boundary: Z7.12 занят физическим Table_North; размещение не выполняется" });
                LastReport.placements.Add(new Placement { name = "Стулья и стойка жилетов", decision = "needs_free_niche: прежние позиции заняты настоящими шкафами и кейсами; не переносим в sweep или центр" });
                LastReport.minimumCompositionMet = LastReport.existingWorktables >= 4 && LastReport.cabinets + LastReport.existingClosedCabinets >= 2
                    && LastReport.westFullSizeCases >= 3 && LastReport.eastFullSizeCases >= 1 && LastReport.eastCases >= 2;
            }
            finally
            {
                string path = Path.Combine(Application.dataPath, "../" + ReportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(LastReport, true));
            }
            GameLog.Debug.Info($"[LobbyDecoration] Полки {LastReport.cabinets}, существующие закрытые шкафы {LastReport.existingClosedCabinets}, патронные кейсы запад/восток {LastReport.westCases}/{LastReport.eastCases}, полноразмерные {LastReport.westFullSizeCases}/{LastReport.eastFullSizeCases}; минимальные количества={LastReport.minimumCompositionMet}; требуется визуальная проверка; {ReportPath}");
        }

        private static int FullCase(Transform parent, string name, float x, float z, float yaw,
            Bounds arena, Bounds tent, List<Bounds> obstacles)
        {
            return Put(parent, name, "SM_MilitaryCrate_01", new Vector3(arena.center.x + x, arena.max.y, arena.center.z + z),
                yaw, Vector3.one * 10, tent, obstacles, null, 1, true) == null ? 0 : 1;
        }

        private static List<Bounds> SourceTableStructures(Transform holder)
        {
            if (holder == null) return null;
            var prefab = LobbyMilitaryCampSources.Prefab("SM_Table");
            // Root identity prefab/FBX не определяет сохранённую геометрию; проверяем сам исходник во всех LOD.
            var candidates = holder.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == prefab.name && MatchesTableSource(t, prefab.transform)).ToArray();
            var instance = candidates.Length == 1 ? candidates[0] : null;
            var colliders = prefab.GetComponentsInChildren<BoxCollider>(true);
            if (instance == null || colliders.Length != 5) return null;
            var result = new List<Bounds>();
            foreach (var collider in colliders)
            {
                bool first = true; Bounds bounds = default;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = collider.center + Vector3.Scale(collider.size * .5f, new Vector3(x, y, z));
                    point = instance.TransformPoint(prefab.transform.InverseTransformPoint(collider.transform.TransformPoint(point)));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                }
                result.Add(bounds);
                LastReport.tableStructures.Add(new Placement { name = collider.name, source = AssetDatabase.GetAssetPath(prefab),
                    guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)), bounds = bounds,
                    decision = "source_structure: ножки/раскосы/столешница в фактической матрице SM_Table" });
            }
            return result;
        }

        private static bool MatchesTableSource(Transform actual, Transform source)
        {
            var actualGroup = actual.GetComponent<LODGroup>();
            var sourceGroup = source.GetComponent<LODGroup>();
            if (actualGroup == null || sourceGroup == null
                || actual.GetComponentsInChildren<LODGroup>(true).Length != 1
                || actual.GetComponentsInChildren<MeshFilter>(true).Length != source.GetComponentsInChildren<MeshFilter>(true).Length) return false;
            var actualLevels = actualGroup.GetLODs(); var sourceLevels = sourceGroup.GetLODs();
            if (actualLevels.Length != 3 || sourceLevels.Length != 3) return false;
            for (int level = 0; level < 3; level++)
            {
                if (actualLevels[level].renderers.Length != sourceLevels[level].renderers.Length) return false;
                for (int index = 0; index < sourceLevels[level].renderers.Length; index++)
                {
                    var actualRenderer = actualLevels[level].renderers[index]; var sourceRenderer = sourceLevels[level].renderers[index];
                    if (actualRenderer == null || sourceRenderer == null) return false;
                    var actualMesh = actualRenderer.GetComponent<MeshFilter>(); var sourceMesh = sourceRenderer.GetComponent<MeshFilter>();
                    if (actualMesh == null || sourceMesh == null || sourceMesh.sharedMesh == null
                        || actualMesh.sharedMesh != sourceMesh.sharedMesh
                        || !actualRenderer.sharedMaterials.SequenceEqual(sourceRenderer.sharedMaterials)) return false;
                    Matrix4x4 actualLocal = actual.worldToLocalMatrix * actualRenderer.transform.localToWorldMatrix;
                    Matrix4x4 sourceLocal = source.worldToLocalMatrix * sourceRenderer.transform.localToWorldMatrix;
                    for (int element = 0; element < 16; element++)
                        if (Mathf.Abs(actualLocal[element] - sourceLocal[element]) > .0001f) return false;
                }
            }
            return true;
        }

        private static int Case(Transform parent, string name, string source, float x, float z, float y, float yaw,
            Bounds arena, Bounds tent, List<Bounds> obstacles, Bounds? support)
        {
            var bottom = new Vector3(arena.center.x + x, y, arena.center.z + z);
            return Put(parent, name, source, bottom, yaw, new Vector3(.36f, .28f, .34f),
                tent, obstacles, support, null, true) == null ? 0 : 1;
        }

        private static GameObject Put(Transform parent, string name, string source, Vector3 bottom, float yaw,
            Vector3 maximum, Bounds tent, List<Bounds> obstacles, Bounds? support = null, float? fixedScale = null, bool surfaceContact = false)
        {
            GameObject prefab;
            try { prefab = LobbyMilitaryCampSources.Prefab(source); }
            catch (Exception e) { LastReport.missing++; Reject(name, "missing_source: " + source + ": " + e.Message); return null; }
            var holder = new GameObject(name); holder.transform.SetParent(parent, false);
            holder.transform.SetPositionAndRotation(bottom, Quaternion.Euler(0, yaw, 0));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
            instance.transform.localPosition = Vector3.zero; instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            foreach (var c in instance.GetComponentsInChildren<Component>(true))
                if (!(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer) && !(c is LODGroup)) UnityEngine.Object.DestroyImmediate(c);
            var local = Measure(instance.transform, holder.transform);
            if (local.size.x <= 0 || local.size.y <= 0 || local.size.z <= 0) throw new InvalidOperationException("Нет объёмного Mesh: " + source);
            float worldX = Mathf.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad)) * local.size.x + Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) * local.size.z;
            float worldZ = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) * local.size.x + Mathf.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad)) * local.size.z;
            float scale = fixedScale ?? Mathf.Min(1, maximum.x / worldX, maximum.y / local.size.y, maximum.z / worldZ);
            instance.transform.localScale = Vector3.one * scale;
            instance.transform.localPosition = new Vector3(-local.center.x, -local.min.y, -local.center.z) * scale;
            if (surfaceContact)
            {
                // Контакт измеряется после yaw/scale по реально рисуемому LOD0, без слепого шага высоты.
                Bounds contact = PrimaryRendererBounds(instance.transform);
                holder.transform.position += Vector3.up * (bottom.y - contact.min.y);
            }
            foreach (var node in instance.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(node.gameObject, StaticEditorFlags.OccludeeStatic);
            foreach (var r in instance.GetComponentsInChildren<MeshRenderer>(true)) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
            foreach (var lod in instance.GetComponentsInChildren<LODGroup>(true)) lod.RecalculateBounds();
            Bounds b = Measure(holder.transform); Bounds expanded = b; expanded.Expand(.035f);
            string reason = null;
            Bounds blocking = default;
            if (b.min.x < tent.min.x + .025f || b.max.x > tent.max.x - .025f || b.min.z < tent.min.z + .025f || b.max.z > tent.max.z - .025f) reason = "outside_tent";
            else if (obstacles.Any(expanded.Intersects))
            {
                blocking = obstacles.First(expanded.Intersects);
                reason = "excluded: sweep/окно/зеркало/колонна/существующая группа; нужна правка ниши владельцем, масштаб сохранён";
            }
            else if (support.HasValue && (b.min.x < support.Value.min.x + .012f || b.max.x > support.Value.max.x - .012f
                || b.min.z < support.Value.min.z + .012f || b.max.z > support.Value.max.z - .012f)) reason = "outside_support";
            var record = new Placement { name = name, source = source, guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)),
                bounds = b, nativeBounds = local, blockingBounds = blocking, contactBounds = PrimaryRendererBounds(instance.transform),
                lodLevels = instance.GetComponentsInChildren<LODGroup>(true).Sum(l => l.GetLODs().Length), uniformScale = scale,
                decision = reason ?? "placed" };
            LastReport.placements.Add(record);
            if (reason != null) { LastReport.rejected++; UnityEngine.Object.DestroyImmediate(holder); return null; }
            obstacles.Add(b); return holder;
        }

        private static Bounds PrimaryRendererBounds(Transform item)
        {
            var groups = item.GetComponentsInChildren<LODGroup>(true);
            var renderers = groups.SelectMany(g => g.GetLODs().Take(1).SelectMany(l => l.renderers))
                .Where(r => r != null).Distinct().ToArray();
            if (renderers.Length == 0) renderers = item.GetComponentsInChildren<MeshRenderer>(true).Cast<Renderer>().ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException("Нет Renderer опоры: " + item.name);
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static Bounds Measure(Transform item, Transform relative = null)
        {
            bool first = true; Bounds result = default;
            foreach (var f in item.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null))
            {
                var b = f.sharedMesh.bounds;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                {
                    var p = f.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z)));
                    if (relative != null) p = relative.InverseTransformPoint(p);
                    if (first) { result = new Bounds(p, Vector3.zero); first = false; } else result.Encapsulate(p);
                }
            }
            if (first) throw new InvalidOperationException("Нет Mesh в " + item.name);
            return result;
        }
        private static Bounds MeshBounds(MeshFilter filter)
        {
            var b = filter.sharedMesh.bounds; bool first = true; Bounds result = default;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            {
                var p = filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z)));
                if (first) { result = new Bounds(p, Vector3.zero); first = false; } else result.Encapsulate(p);
            }
            return result;
        }
        private static Bounds? FindSurface(Transform parent, float x, float z)
        {
            return parent.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BoxCollider>(true))
                .Where(c => c.enabled && c.gameObject.activeInHierarchy && HasTableAncestor(c.transform) && c.bounds.max.y < 1.2f
                    && x >= c.bounds.min.x && x <= c.bounds.max.x && z >= c.bounds.min.z && z <= c.bounds.max.z)
                .OrderByDescending(c => c.bounds.max.y).Select(c => (Bounds?)c.bounds).FirstOrDefault();
        }
        private static bool HasTableAncestor(Transform item)
        {
            for (var node = item; node != null; node = node.parent)
                if (node.name.StartsWith("Table_", StringComparison.Ordinal)) return true;
            return false;
        }
        private static bool SameSupport(Bounds b, Bounds support) => (b.center - support.center).sqrMagnitude < .0025f && (b.size - support.size).sqrMagnitude < .0025f;
        private static int CountTables(Transform parent)
        {
            int old = parent.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Count(t => t.gameObject.activeInHierarchy && (t.name == "Table_North" || t.name == "Table_South") && t.GetComponentsInChildren<MeshRenderer>(true).Length > 0);
            var side = parent.Find("SidewallDecoration");
            return old + (side == null ? 0 : side.Cast<Transform>().Count(t => t.name.EndsWith("полевой стол", StringComparison.Ordinal)));
        }
        private static void Reject(string name, string reason) { LastReport.rejected++; LastReport.placements.Add(new Placement { name = name, decision = reason }); }
    }
}
