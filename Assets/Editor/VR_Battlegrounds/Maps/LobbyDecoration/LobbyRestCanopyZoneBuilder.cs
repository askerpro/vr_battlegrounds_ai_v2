using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Статичная зона отдыха: исходный навес, два стола и шесть мест без игровой физики.</summary>
    public static class LobbyRestCanopyZoneBuilder
    {
        public static readonly string[] RequiredSources = { "SM_Tent_b", "SM_Table", "SM_Chair", "SM_Lamp_long" };
        public const string MugSourcePath = "Assets/Art/Lobby/Interior/Imported/HauntedFarmHouse/URP/Art/Prefabs/SM_Mug_01.prefab";
        private const string VariantFolder = "Assets/Art/Lobby/Exterior/RestCanopy";
        private const string ClothMaterialPath = "Assets/Materials/Lobby/Interior/Roof/RoofCloth.mat";
        private const string MetalMaterialPath = "Assets/Art/Lobby/Imported/MilitaryCamp/Art/Materials/M_Metal_01.mat";
        private const string OriginalClothMaterialPath = "Assets/Art/Lobby/Imported/MilitaryCamp/Art/Materials/M_Tent_01_b.mat";
        [Serializable] public sealed class GeometryLod
        {
            public int level, sourceSubmeshes, removedClothTriangles, preservedMetalTriangles, roofTriangles, roofVertices;
            public int roofBoundaryEdges, roofInternalBoundaryEdges, roofNonManifoldEdges;
            public string sourceMesh, metalMaterial, frameAsset, roofAsset;
            public Bounds localFrameBounds, localRoofBounds;
        }
        [Serializable] public sealed class Item
        {
            public string role, sourcePath;
            public Bounds nativeBounds, worldBounds, lod0Bounds;
            public float uniformScale, contactY;
            public int renderers, lodLevels;
        }
        [Serializable] public sealed class Report
        {
            public bool layoutValidated;
            public Bounds arena, tent, reservation, actualBounds, centralAisle;
            public List<Item> items = new List<Item>();
            public int renderers, colliders, lights, scripts, audioSources;
            public string lightingSocket;
            public Vector3 lightingPosition;
            public string geometry = "vendor_metal_frame_with_own_connected_two_slope_roof", ridgeAxis = "X", clothMaterial;
            public float localEaveY, localRidgeY, measuredEaveVertexMaxY, supportSlope, maximumFrameProtrusion, roofAttachmentOffset = .008f;
            public float fixtureMinimumRoofHeight, fixtureRoofClearance;
            public Bounds sourceClothedEnvelope, frameLod0Bounds, ownRoofBounds;
            public List<GeometryLod> geometryLods = new List<GeometryLod>();
        }
        public static Report LastReport { get; private set; }

        public static void Build(Transform parent, Bounds arena, Bounds tent)
        {
            LastReport = null;
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (parent.Find("RestCanopyZone") != null) throw new InvalidOperationException("RestCanopyZone уже существует.");
            if (!Finite(arena.center) || !Finite(arena.size) || arena.size.x <= 0 || arena.size.z <= 0 ||
                !Finite(tent.center) || !Finite(tent.size)) throw new InvalidOperationException("Некорректные bounds арены/шатра.");
            if ((parent.lossyScale - Vector3.one).sqrMagnitude > .000001f || Quaternion.Angle(parent.rotation, Quaternion.identity) > .001f)
                throw new InvalidOperationException("Нужен родитель с мировыми scale=1 и rotation=identity.");
            var sources = RequiredSources.ToDictionary(s => s, LobbyMilitaryCampSources.Prefab);
            sources.Add("SM_Mug_01", AssetDatabase.LoadAssetAtPath<GameObject>(MugSourcePath));
            if (sources.Values.Any(s => s == null)) throw new InvalidOperationException("Отсутствует импортированный источник зоны отдыха.");
            var origin = new Vector3(arena.center.x, arena.max.y, arena.center.z);
            var root = Group("RestCanopyZone", parent);
            root.position = Vector3.zero;
            LastReport = new Report { arena = arena, tent = tent, reservation = Envelope(origin, -31, -13, -36, -25, 4),
                centralAisle = Envelope(origin, -23, -21, -34, -27, 2.1f) };
            try
            {
                var canopy = Place(sources["SM_Tent_b"], root, "Canopy__SM_Tent_b", origin + new Vector3(-22, 0, -30.5f), 0);
                var canopyBounds = Measure(canopy.transform);
                if (canopy.GetComponentsInChildren<LODGroup>(true).Sum(l => l.GetLODs().Length) != 5 ||
                    (canopyBounds.size - new Vector3(15.494f, 3.641f, 8.035f)).sqrMagnitude > .03f)
                    throw new InvalidOperationException("Неожиданные исходные габариты/LOD Tent_b: " + canopyBounds.size);
                LastReport.sourceClothedEnvelope = canopyBounds;
                OpenCanopy(canopy);
                var west = DiningGroup(sources, root, origin, "WestRestTable", -25.5f, -1);
                var east = DiningGroup(sources, root, origin, "EastRestTable", -18.5f, 1);
                // Светильник располагается под скатами; реальный Light добавляет общий владелец света.
                var fixture = Place(sources["SM_Lamp_long"], root, "RidgeFixture__SM_Lamp_long", origin + new Vector3(-22, 0, -30.5f), 90);
                var fixtureBounds = Measure(fixture.transform);
                float crossSlopeDistance = Math.Max(Math.Abs(fixtureBounds.min.z - LastReport.ownRoofBounds.center.z),
                    Math.Abs(fixtureBounds.max.z - LastReport.ownRoofBounds.center.z));
                LastReport.fixtureMinimumRoofHeight = LastReport.ownRoofBounds.max.y - LastReport.supportSlope * crossSlopeDistance;
                fixture.transform.position += Vector3.up * (LastReport.fixtureMinimumRoofHeight - .008f - fixtureBounds.max.y);
                LastReport.fixtureRoofClearance = LastReport.fixtureMinimumRoofHeight - Measure(fixture.transform).max.y;
                if (LastReport.fixtureRoofClearance < .007f) throw new InvalidOperationException("Светильник пересекает скаты крыши.");
                Refresh(fixture);
                var socket = Group("LightingSocket", fixture.transform);
                socket.position = Measure(fixture.transform, true).center + Vector3.down * .08f;
                LastReport.lightingSocket = "RestCanopyZone/RidgeFixture__SM_Lamp_long/LightingSocket";
                LastReport.lightingPosition = socket.position;
                Validate(root, new[] { west, east }, origin);
                LastReport.layoutValidated = true;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                throw;
            }
        }

        private static Transform DiningGroup(Dictionary<string, GameObject> sources, Transform root, Vector3 origin, string name, float x, int outer)
        {
            var group = Group(name, root);
            var table = Place(sources["SM_Table"], group, name + "__Table", origin + new Vector3(x, 0, -30.5f), 0);
            var tableBounds = Measure(table.transform, true);
            if (tableBounds.size.y < .6f || tableBounds.size.y > 1.2f || tableBounds.size.x > 3 || tableBounds.size.z > 2)
                throw new InvalidOperationException("Исходный стол не соответствует человеческому масштабу.");
            // Два места по длинным сторонам и одно у внешнего торца; между группами свободно 2 м.
            Place(sources["SM_Chair"], group, name + "__ChairNorth", new Vector3(tableBounds.center.x, origin.y, tableBounds.max.z + .65f), 180);
            Place(sources["SM_Chair"], group, name + "__ChairSouth", new Vector3(tableBounds.center.x, origin.y, tableBounds.min.z - .65f), 0);
            float endX = outer < 0 ? tableBounds.min.x - .65f : tableBounds.max.x + .65f;
            Place(sources["SM_Chair"], group, name + "__ChairEnd", new Vector3(endX, origin.y, tableBounds.center.z), outer < 0 ? 90 : -90);
            for (int i = 0; i < 2; i++)
            {
                var mug = Place(sources["SM_Mug_01"], group, name + "__Mug" + i,
                    new Vector3(tableBounds.center.x + (i == 0 ? -.28f : .28f), tableBounds.max.y, tableBounds.center.z + (i == 0 ? -.12f : .12f)), i * 130);
                var mb = Measure(mug.transform);
                if (mb.size.y < .05f || mb.size.y > .25f || mb.min.x < tableBounds.min.x || mb.max.x > tableBounds.max.x ||
                    mb.min.z < tableBounds.min.z || mb.max.z > tableBounds.max.z)
                    throw new InvalidOperationException("Кружка не помещается на исходном столе.");
            }
            var furniture = group.GetComponentsInChildren<LODGroup>(true).Select(l => l.transform).ToArray();
            for (int i = 0; i < furniture.Length; i++) for (int j = 0; j < i; j++)
                if (OverlapXZ(Measure(furniture[i]), Measure(furniture[j]), .08f))
                    throw new InvalidOperationException("Пересечение мебели: " + furniture[i].name + "/" + furniture[j].name);
            return group;
        }

        private static GameObject Place(GameObject source, Transform parent, string role, Vector3 surfaceCenter, float yaw)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = role;
            go.transform.localScale = Vector3.one;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.transform.position = Vector3.zero;
            Sanitize(go);
            var native = Measure(go.transform);
            var contact = Measure(go.transform, true);
            go.transform.position += new Vector3(surfaceCenter.x - native.center.x, surfaceCenter.y - contact.min.y, surfaceCenter.z - native.center.z);
            LastReport.items.Add(new Item { role = role, sourcePath = AssetDatabase.GetAssetPath(source), nativeBounds = native,
                uniformScale = 1, worldBounds = Measure(go.transform), lod0Bounds = Measure(go.transform, true), contactY = surfaceCenter.y,
                renderers = go.GetComponentsInChildren<Renderer>(true).Length,
                lodLevels = go.GetComponentsInChildren<LODGroup>(true).Sum(l => l.GetLODs().Length) });
            return go;
        }
        private static void Refresh(GameObject go)
        {
            var item = LastReport.items.Single(i => i.role == go.name);
            item.worldBounds = Measure(go.transform); item.lod0Bounds = Measure(go.transform, true); item.contactY = item.lod0Bounds.min.y;
        }
        private static void OpenCanopy(GameObject canopy)
        {
            var cloth = AssetDatabase.LoadAssetAtPath<Material>(ClothMaterialPath);
            if (cloth == null || cloth.shader == null || cloth.shader.name == "Hidden/InternalErrorShader" || !cloth.HasProperty("_Cull") || cloth.GetFloat("_Cull") != 0)
                throw new InvalidOperationException("Нужен проверенный двусторонний RoofCloth без изменения материала.");
            LastReport.clothMaterial = ClothMaterialPath;
            var group = canopy.GetComponent<LODGroup>();
            if (group == null || group.GetLODs().Length != 5) throw new InvalidOperationException("Исходный каркас должен иметь 5 LOD.");
            var lods = group.GetLODs();
            EnsureFolder(VariantFolder);
            Vector3[] lod0FramePoints = null;
            for (int i = 0; i < lods.Length; i++)
            {
                if (lods[i].renderers.Length != 1) throw new InvalidOperationException("Неожиданная структура исходного Tent_b LOD.");
                var renderer = lods[i].renderers[0];
                var filter = renderer.GetComponent<MeshFilter>();
                var source = filter.sharedMesh;
                var materials = renderer.sharedMaterials;
                // Проверяются конкретные импортированные материалы каждого LOD, а не предполагаемый номер слота.
                if (source == null || !source.isReadable || source.subMeshCount != 2 || materials.Length != 2 ||
                    AssetDatabase.GetAssetPath(materials[0]) != OriginalClothMaterialPath || AssetDatabase.GetAssetPath(materials[1]) != MetalMaterialPath ||
                    source.GetTopology(0) != MeshTopology.Triangles || source.GetTopology(1) != MeshTopology.Triangles)
                    throw new InvalidOperationException("Изменились submesh/material slots Tent_b LOD" + i);
                var report = new GeometryLod { level = i, sourceSubmeshes = source.subMeshCount,
                    sourceMesh = AssetDatabase.GetAssetPath(source), metalMaterial = MetalMaterialPath,
                    removedClothTriangles = (int)(source.GetIndexCount(0) / 3), preservedMetalTriangles = (int)(source.GetIndexCount(1) / 3),
                    frameAsset = VariantFolder + "/Frame_LOD" + i + ".asset", roofAsset = VariantFolder + "/Roof_LOD" + i + ".asset" };
                if (report.removedClothTriangles == 0 || report.preservedMetalTriangles == 0)
                    throw new InvalidOperationException("Пустой исходный submesh Tent_b.");
                var frame = MetalOnly(source);
                if (i == 0) lod0FramePoints = frame.vertices.Select(v => canopy.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();
                filter.sharedMesh = SaveOwnMesh(frame, report.frameAsset);
                renderer.sharedMaterials = new[] { materials[1] };
                report.localFrameBounds = filter.sharedMesh.bounds;
                LastReport.geometryLods.Add(report);
            }
            group.RecalculateBounds();
            // Исходная ткань свисала ниже металла. Контакт открытого каркаса пересчитывается по его LOD0.
            canopy.transform.position += Vector3.up * (LastReport.arena.max.y - Measure(canopy.transform, true).min.y);
            Refresh(canopy);
            LastReport.frameLod0Bounds = Measure(canopy.transform, true);
            var upper = lod0FramePoints.Where(v => v.y > 1.5f).ToArray();
            if (upper.Length < 100) throw new InvalidOperationException("Нельзя определить верхнюю часть исходного каркаса.");
            var supportBounds = new Bounds(upper[0], Vector3.zero);
            foreach (var p in upper) supportBounds.Encapsulate(p);
            var ridge = upper.Where(v => Math.Abs(v.z - supportBounds.center.z) < .25f).ToArray();
            var eaves = upper.Where(v => Math.Abs(v.z - supportBounds.center.z) > supportBounds.extents.z * .8f).ToArray();
            if (ridge.Length < 20 || eaves.Length < 20 || supportBounds.size.x < 13 || supportBounds.size.x > 15 || supportBounds.size.z < 5.5f || supportBounds.size.z > 6.5f)
                throw new InvalidOperationException("Верхний каркас не соответствует нативному профилю Tent_b.");
            LastReport.localRidgeY = ridge.Max(v => v.y);
            LastReport.measuredEaveVertexMaxY = eaves.Max(v => v.y);
            // Плоскость касается верхних внешних труб. Просто maxY всего карниза дал бы зазор над ними.
            LastReport.supportSlope = eaves.Min(v => (LastReport.localRidgeY - v.y) / Math.Abs(v.z - supportBounds.center.z));
            LastReport.localEaveY = LastReport.localRidgeY - LastReport.supportSlope * supportBounds.extents.z;
            LastReport.maximumFrameProtrusion = upper.Max(v => v.y - (LastReport.localRidgeY - LastReport.supportSlope * Math.Abs(v.z - supportBounds.center.z) + LastReport.roofAttachmentOffset));
            if (LastReport.maximumFrameProtrusion > .001f) throw new InvalidOperationException("Исходный каркас выступает сквозь плоскость новой крыши.");
            if (LastReport.localRidgeY < 3.3f || LastReport.localRidgeY > 3.6f || LastReport.localEaveY < 2 || LastReport.localEaveY > 2.3f)
                throw new InvalidOperationException("Неожиданные нативные высоты конька/карниза.");
            var roofRoot = Group("OwnOpenClothRoof", canopy.transform);
            var roofLods = new LOD[5];
            int[] cellsX = { 24, 16, 12, 8, 4 }, cellsZ = { 8, 6, 4, 4, 2 };
            for (int i = 0; i < 5; i++)
            {
                var report = LastReport.geometryLods[i];
                var mesh = RoofGrid(supportBounds, LastReport.localEaveY, LastReport.localRidgeY, cellsX[i], cellsZ[i]);
                AuditRoof(mesh, report);
                mesh = SaveOwnMesh(mesh, report.roofAsset);
                var child = Group("OwnRoof_LOD" + i, roofRoot);
                child.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = child.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = cloth;
                roofLods[i] = new LOD(lods[i].screenRelativeTransitionHeight, new Renderer[] { renderer });
            }
            var ownGroup = roofRoot.gameObject.AddComponent<LODGroup>(); ownGroup.SetLODs(roofLods); ownGroup.RecalculateBounds();
            Sanitize(roofRoot.gameObject);
            LastReport.ownRoofBounds = Measure(roofRoot);
            LastReport.items.Add(new Item { role = "OwnOpenClothRoof", sourcePath = VariantFolder + "/Roof_LOD*.asset + " + ClothMaterialPath,
                nativeBounds = LastReport.geometryLods[0].localRoofBounds, worldBounds = LastReport.ownRoofBounds,
                lod0Bounds = Measure(roofRoot, true), uniformScale = 1, contactY = canopy.transform.TransformPoint(new Vector3(0, LastReport.localEaveY, 0)).y,
                renderers = 5, lodLevels = 5 });
        }
        private static Mesh MetalOnly(Mesh source)
        {
            var oldIndices = source.GetTriangles(1);
            var references = oldIndices.Distinct().ToArray();
            var remap = new Dictionary<int, int>();
            for (int i = 0; i < references.Length; i++) remap.Add(references[i], i);
            var mesh = new Mesh { name = source.name + "__MetalOnly", indexFormat = source.indexFormat };
            var vertices = source.vertices; mesh.vertices = references.Select(i => vertices[i]).ToArray();
            var normals = source.normals; if (normals.Length == vertices.Length) mesh.normals = references.Select(i => normals[i]).ToArray();
            var tangents = source.tangents; if (tangents.Length == vertices.Length) mesh.tangents = references.Select(i => tangents[i]).ToArray();
            var colors = source.colors; if (colors.Length == vertices.Length) mesh.colors = references.Select(i => colors[i]).ToArray();
            for (int channel = 0; channel < 8; channel++)
            {
                var uv = new List<Vector4>(); source.GetUVs(channel, uv);
                if (uv.Count == vertices.Length) mesh.SetUVs(channel, references.Select(i => uv[i]).ToList());
                else if (uv.Count != 0) throw new InvalidOperationException("Неполный UV канал исходного металлического submesh.");
            }
            // Все исходные металлические треугольники сохраняются без отсечения или перемещения вершин.
            mesh.SetTriangles(oldIndices.Select(i => remap[i]).ToArray(), 0); mesh.RecalculateBounds();
            if (mesh.vertexCount != references.Length || mesh.GetIndexCount(0) != source.GetIndexCount(1))
                throw new InvalidOperationException("Нарушена сохранность металлического submesh.");
            return mesh;
        }
        private static Mesh RoofGrid(Bounds supports, float eave, float ridge, int nx, int nz)
        {
            const float overhang = .12f;
            float halfZ = supports.extents.z, centerZ = supports.center.z;
            var vertices = new Vector3[(nx + 1) * (nz + 1)]; var uv = new Vector2[vertices.Length];
            var indices = new int[nx * nz * 6]; int write = 0;
            for (int z = 0; z <= nz; z++) for (int x = 0; x <= nx; x++)
            {
                float u = x / (float)nx, v = z / (float)nz;
                float px = Mathf.Lerp(supports.min.x - overhang, supports.max.x + overhang, u);
                float pz = Mathf.Lerp(supports.min.z - overhang, supports.max.z + overhang, v);
                // Продолжение плоскости ската на небольшой свес; замкнутых стен и лоскутного clipping нет.
                float height = ridge + (eave - ridge) * Math.Abs(pz - centerZ) / halfZ + LastReport.roofAttachmentOffset;
                int index = z * (nx + 1) + x; vertices[index] = new Vector3(px, height, pz); uv[index] = new Vector2(u, v);
            }
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
            {
                int a = z * (nx + 1) + x, b = a + nx + 1;
                indices[write++] = a; indices[write++] = b; indices[write++] = a + 1;
                indices[write++] = a + 1; indices[write++] = b; indices[write++] = b + 1;
            }
            var mesh = new Mesh { name = "OpenCanopyRoof_" + nx + "x" + nz };
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = indices;
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
        }
        private static void AuditRoof(Mesh mesh, GeometryLod report)
        {
            var edges = new Dictionary<ulong, int>();
            var triangles = mesh.triangles; var vertices = mesh.vertices;
            for (int t = 0; t < triangles.Length; t += 3) for (int e = 0; e < 3; e++)
            {
                uint a = (uint)triangles[t + e], b = (uint)triangles[t + (e + 1) % 3];
                ulong key = ((ulong)Math.Min(a, b) << 32) | Math.Max(a, b);
                edges[key] = edges.TryGetValue(key, out int count) ? count + 1 : 1;
            }
            var bounds = mesh.bounds;
            foreach (var edge in edges)
            {
                if (edge.Value > 2) report.roofNonManifoldEdges++;
                if (edge.Value != 1) continue;
                report.roofBoundaryEdges++;
                var a = vertices[(int)(edge.Key >> 32)]; var b = vertices[(int)(edge.Key & uint.MaxValue)];
                bool outer = (Math.Abs(a.x - bounds.min.x) < .001f && Math.Abs(b.x - bounds.min.x) < .001f) ||
                    (Math.Abs(a.x - bounds.max.x) < .001f && Math.Abs(b.x - bounds.max.x) < .001f) ||
                    (Math.Abs(a.z - bounds.min.z) < .001f && Math.Abs(b.z - bounds.min.z) < .001f) ||
                    (Math.Abs(a.z - bounds.max.z) < .001f && Math.Abs(b.z - bounds.max.z) < .001f);
                if (!outer) report.roofInternalBoundaryEdges++;
            }
            report.roofTriangles = triangles.Length / 3; report.roofVertices = vertices.Length; report.localRoofBounds = bounds;
            if (report.roofInternalBoundaryEdges != 0 || report.roofNonManifoldEdges != 0 || vertices.Any(v => !Finite(v)))
                throw new InvalidOperationException("Нарушена связность чистой двускатной крыши.");
        }
        private static Mesh SaveOwnMesh(Mesh mesh, string path)
        {
            if (!path.StartsWith(VariantFolder + "/", StringComparison.Ordinal)) throw new InvalidOperationException("Mesh вне собственной области.");
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            existing.Clear(false); existing.indexFormat = mesh.indexFormat;
            existing.vertices = mesh.vertices; existing.normals = mesh.normals; existing.tangents = mesh.tangents; existing.colors = mesh.colors;
            for (int channel = 0; channel < 8; channel++) { var uv = new List<Vector4>(); mesh.GetUVs(channel, uv); if (uv.Count > 0) existing.SetUVs(channel, uv); }
            existing.subMeshCount = mesh.subMeshCount;
            for (int sub = 0; sub < mesh.subMeshCount; sub++) existing.SetTriangles(mesh.GetTriangles(sub), sub);
            existing.bounds = mesh.bounds; existing.UploadMeshData(false); EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh); return existing;
        }
        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/'); string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string child = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(child)) AssetDatabase.CreateFolder(current, parts[i]);
                current = child;
            }
        }
        private static Transform Group(string name, Transform parent)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
        }
        private static Bounds Envelope(Vector3 origin, float minX, float maxX, float minZ, float maxZ, float height)
            => new Bounds(origin + new Vector3((minX + maxX) / 2, height / 2, (minZ + maxZ) / 2), new Vector3(maxX - minX, height, maxZ - minZ));
        private static bool OverlapXZ(Bounds a, Bounds b, float gap) => a.min.x < b.max.x + gap && a.max.x > b.min.x - gap && a.min.z < b.max.z + gap && a.max.z > b.min.z - gap;
        private static void Validate(Transform root, Transform[] dining, Vector3 origin)
        {
            foreach (var item in LastReport.items)
            {
                var a = item.worldBounds; var b = LastReport.reservation;
                if (a.min.x < b.min.x - .002f || a.max.x > b.max.x + .002f || a.min.z < b.min.z - .002f || a.max.z > b.max.z + .002f ||
                    a.min.y < b.min.y - .04f || a.max.y > b.max.y + .002f)
                    throw new InvalidOperationException("Объект выходит из резерва навеса: " + item.role);
                if (OverlapXZ(a, LastReport.arena, .1f) || OverlapXZ(a, LastReport.tent, .1f) ||
                    OverlapXZ(a, Envelope(origin, -31, -16, -24, -8, 12), .1f))
                    throw new InvalidOperationException("Навес заходит в защищённую/соседнюю зону: " + item.role);
            }
            foreach (var group in dining) if (OverlapXZ(Measure(group), LastReport.centralAisle, 0))
                throw new InvalidOperationException("Мебель занимает центральный проход: " + group.name);
            LastReport.actualBounds = Measure(root);
            LastReport.renderers = root.GetComponentsInChildren<Renderer>(true).Length;
            LastReport.colliders = root.GetComponentsInChildren<Collider>(true).Length;
            LastReport.lights = root.GetComponentsInChildren<Light>(true).Length;
            LastReport.scripts = root.GetComponentsInChildren<MonoBehaviour>(true).Length;
            LastReport.audioSources = root.GetComponentsInChildren<AudioSource>(true).Length;
            if (LastReport.colliders + LastReport.lights + LastReport.scripts + LastReport.audioSources != 0)
                throw new InvalidOperationException("Навес содержит запрещённые компоненты.");
        }

        private static Bounds Measure(Transform subtree, bool lod0 = false)
        {
            var all = subtree.GetComponentsInChildren<Renderer>(true);
            var groups = subtree.GetComponentsInChildren<LODGroup>(true);
            var covered = new HashSet<Renderer>(groups.SelectMany(g => g.GetLODs()).SelectMany(l => l.renderers));
            var selected = lod0 ? groups.SelectMany(g => g.GetLODs().Take(1)).SelectMany(l => l.renderers).Concat(all.Where(r => !covered.Contains(r))).Distinct() : all;
            Bounds result = default; bool first = true;
            foreach (var renderer in selected)
            {
                if (renderer == null || !renderer.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null)
                    throw new InvalidOperationException("Отсутствует статичная геометрия.");
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var point = filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!Finite(point)) throw new InvalidOperationException("Неконечные bounds.");
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; } else result.Encapsulate(point);
                }
            }
            if (first) throw new InvalidOperationException("Нет renderer геометрии: " + subtree.name);
            return result;
        }
        private static void Sanitize(GameObject go)
        {
            if (go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0) throw new InvalidOperationException("Разрешены только статичные mesh.");
            foreach (var lod in go.GetComponentsInChildren<LODGroup>(true))
                if (lod.GetLODs().Length == 0 || lod.GetLODs().Any(l => l.renderers.Length == 0 || l.renderers.Any(r => r == null)))
                    throw new InvalidOperationException("Неполная исходная LOD цепочка.");
            foreach (var component in go.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(component);
            foreach (var component in go.GetComponentsInChildren<Component>(true))
            {
                if (component == null) throw new InvalidOperationException("Missing script в источнике навеса.");
                if (!(component is Transform) && !(component is MeshRenderer) && !(component is MeshFilter) && !(component is LODGroup)) UnityEngine.Object.DestroyImmediate(component);
            }
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterials.Length == 0 || renderer.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader"))
                    throw new InvalidOperationException("Отсутствует исходный материал/шейдер.");
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.OccludeeStatic);
        }
        private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }
}
