using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Два казарменных корпуса и склад-мастерская из совместимых модулей MilitaryCamp.</summary>
    public static class LobbyBaseBuildingsBuilder
    {
        private const float WallHeight = 3.002f;
        private const string ReportPath = "tasks/lobby-decoration/reports/implementation/base-buildings/latest-build.json";
        [Serializable] public sealed class Module
        {
            public string building, source, guid;
            public Vector3 scale;
            public Bounds bounds;
            public int lodLevels;
            public bool plainFiller;
        }
        [Serializable] public sealed class Building
        {
            public string name, decision;
            public Bounds actualBounds, reservedEnvelope;
            public int modules;
        }
        [Serializable] public sealed class Report
        {
            public Bounds arena, tent;
            public List<Module> modules = new List<Module>();
            public List<Building> buildings = new List<Building>();
            public List<string> missing = new List<string>();
            public int placed, invalid;
        }
        public static Report LastReport { get; private set; }

        public static void Build(Transform parent, Bounds arena, Bounds tent)
        {
            if (parent == null || !parent.gameObject.scene.IsValid()) throw new ArgumentException("Нужен родитель сцены.");
            if (parent.Find("BaseBuildings") != null) throw new InvalidOperationException("BaseBuildings уже существует.");
            LastReport = new Report { arena = arena, tent = tent };
            var root = new GameObject("BaseBuildings"); root.transform.SetParent(parent, false);
            var sources = new Dictionary<string, GameObject>();
            foreach (string stem in new[] { "SM_Building_wall_4x4m_A", "SM_Building_wall_2m_A", "SM_Building_wall_window_2m_A",
                "SM_Building_wall_window_4m_A", "SM_Building_wall_entrance_2m_A", "SM_Building_roof_4x4_01", "SM_Building_roof_4x4_02" })
            {
                try
                {
                    var prefab = LobbyMilitaryCampSources.Prefab(stem);
                    if (prefab == null) throw new InvalidOperationException("Prefab ещё не импортирован Unity.");
                    sources.Add(stem, prefab);
                }
                catch (Exception e) { LastReport.missing.Add(stem + ": " + e.Message); }
            }
            try
            {
                if (LastReport.missing.Count == 0)
                {
                    Assemble(root.transform, sources, "Казарма A", -29, -22, 4, 12, false, arena,
                        Envelope(arena, -31, -16, -24, -8));
                    Assemble(root.transform, sources, "Казарма B", -22, -22, 4, 12, true, arena,
                        Envelope(arena, -31, -16, -24, -8));
                    Assemble(root.transform, sources, "Склад и мастерская", 20, -27, 8, 12, true, arena,
                        Envelope(arena, 18, 30, -29, -13));
                }
            }
            finally
            {
                string path = Path.Combine(Application.dataPath, "../" + ReportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(LastReport, true));
            }
            GameLog.Debug.Info($"[LobbyDecoration] Корпуса базы: {LastReport.placed}, неверные {LastReport.invalid}, нет источников {LastReport.missing.Count}; {ReportPath}");
        }

        private static Bounds Envelope(Bounds arena, float x0, float x1, float z0, float z1) => new Bounds(
            new Vector3(arena.center.x + (x0 + x1) / 2, arena.max.y + 2, arena.center.z + (z0 + z1) / 2), new Vector3(x1 - x0, 4, z1 - z0));

        private static void Assemble(Transform parent, Dictionary<string, GameObject> sources, string name,
            float x, float z, int width, int depth, bool alternate, Bounds arena, Bounds reserved)
        {
            var building = new GameObject(name); building.transform.SetParent(parent, false);
            var origin = new Vector3(arena.center.x + x, arena.max.y, arena.center.z + z);
            int firstModule = LastReport.modules.Count;
            // Южный вход имеет настоящий трёхметровый проём; одиночная дверь 1.1 м сюда не подходит.
            Piece(sources, building.transform, name, "SM_Building_wall_entrance_2m_A", origin, 0);
            Fill(sources, building.transform, name, origin + Vector3.up * 2.50023f, 0, 3, WallHeight - 2.50023f);
            Fill(sources, building.transform, name, origin + Vector3.right * 3, 0, 1, WallHeight);
            if (width == 8)
            {
                Piece(sources, building.transform, name, "SM_Building_wall_window_4m_A", origin + Vector3.right * 4, 0);
                Fill(sources, building.transform, name, origin + new Vector3(4, 2, 0), 0, 4, WallHeight - 2);
            }
            // Северная стена: полные глухие модули 4 м без изменения размеров.
            for (int n = 0; n < width / 4; n++)
                Piece(sources, building.transform, name, "SM_Building_wall_4x4m_A", origin + new Vector3(width - n * 4, 0, depth), 180);
            // Длинные стены: окна 2 м чередуются с глухими секциями; сплошной верх закрывает разницу высот.
            for (int side = 0; side < 2; side++) for (int segment = 0; segment < depth / 2; segment++)
            {
                float yaw = side == 0 ? 90 : -90;
                var start = origin + new Vector3(side == 0 ? 0 : width, 0, side == 0 ? depth - segment * 2 : segment * 2);
                bool window = (segment + (alternate ? 1 : 0)) % 2 == 0;
                Piece(sources, building.transform, name, window ? "SM_Building_wall_window_2m_A" : "SM_Building_wall_2m_A", start, yaw);
                Fill(sources, building.transform, name, start + Vector3.up * 2, yaw, 2, WallHeight - 2);
            }
            // Родные квадратные плиты, плоская крыша без увеличения hangar-моделей.
            for (int roofX = 0; roofX < width; roofX += 4) for (int roofZ = 0; roofZ < depth; roofZ += 4)
                Piece(sources, building.transform, name, alternate ? "SM_Building_roof_4x4_02" : "SM_Building_roof_4x4_01",
                    origin + new Vector3(roofX, WallHeight, roofZ), 0);
            Bounds actual = Measure(building.transform);
            bool valid = actual.min.x >= reserved.min.x && actual.max.x <= reserved.max.x && actual.min.z >= reserved.min.z
                && actual.max.z <= reserved.max.z && actual.min.y >= arena.max.y - .002f && actual.max.y <= reserved.max.y;
            foreach (var other in LastReport.buildings.Where(b => b.decision == "placed")) if (actual.Intersects(other.actualBounds)) valid = false;
            LastReport.buildings.Add(new Building { name = name, actualBounds = actual, reservedEnvelope = reserved,
                modules = LastReport.modules.Count - firstModule, decision = valid ? "placed" : "invalid_envelope_or_overlap" });
            if (valid) LastReport.placed++;
            else { LastReport.invalid++; UnityEngine.Object.DestroyImmediate(building); }
        }

        private static void Fill(Dictionary<string, GameObject> sources, Transform parent, string building, Vector3 start,
            float yaw, float width, float height) => Piece(sources, parent, building, "SM_Building_wall_2m_A", start, yaw, new Vector2(width, height));

        private static void Piece(Dictionary<string, GameObject> sources, Transform parent, string building, string source,
            Vector3 start, float yaw, Vector2? plainFill = null)
        {
            var holder = new GameObject(source + (plainFill.HasValue ? " / глухое заполнение" : "")); holder.transform.SetParent(parent, false);
            holder.transform.SetPositionAndRotation(start, Quaternion.Euler(0, yaw, 0));
            var prefab = sources[source]; var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
            instance.transform.localPosition = Vector3.zero; instance.transform.localRotation = Quaternion.identity; instance.transform.localScale = Vector3.one;
            foreach (var c in instance.GetComponentsInChildren<Component>(true))
                if (!(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer) && !(c is LODGroup)) UnityEngine.Object.DestroyImmediate(c);
            Bounds local = Measure(instance.transform, holder.transform);
            Vector3 scale = plainFill.HasValue ? new Vector3(plainFill.Value.x / local.size.x, plainFill.Value.y / local.size.y, 1) : Vector3.one;
            instance.transform.localScale = scale; instance.transform.localPosition = -Vector3.Scale(local.min, scale);
            foreach (var node in instance.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(node.gameObject, StaticEditorFlags.OccludeeStatic);
            foreach (var lod in instance.GetComponentsInChildren<LODGroup>(true)) lod.RecalculateBounds();
            LastReport.modules.Add(new Module { building = building, source = source, guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)),
                bounds = Measure(holder.transform), scale = scale, plainFiller = plainFill.HasValue,
                lodLevels = instance.GetComponentsInChildren<LODGroup>(true).Sum(l => l.GetLODs().Length) });
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
            if (first) throw new InvalidOperationException("Нет статического Mesh: " + item.name);
            return result;
        }
    }
}
