using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Версия действующего входа, включая несохранённую геометрию и настройки. Только главный поток.</summary>
    public static class MapGrowthSourceVersion
    {
        public static string Compute(Scene scene, BlockoutMarkup markup, MapGrowthSettings settings, GameObject[] replacedRoots = null)
        {
            MapGrowthSnapshotBuilder.RequireMainThread();
            if (!scene.IsValid() || !scene.isLoaded || markup == null || settings == null)
                throw new ArgumentException("Нужны загруженная сцена, разметка и настройки.");
            Physics.SyncTransforms();
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write("map-growth-capture-v1"); writer.Write(scene.path); writer.Write(scene.name);
                writer.Write(Physics.queriesHitBackfaces);
                writer.Write(typeof(MapGrowthSourceVersion).Assembly.ManifestModule.ModuleVersionId.ToString());
                writer.Write(typeof(BlockoutSectionGeometry).Assembly.ManifestModule.ModuleVersionId.ToString());
                writer.Write(EditorJsonUtility.ToJson(markup)); writer.Write(EditorJsonUtility.ToJson(settings));
                var generated = MapGrowthGeneratedOwnership.Find(scene);
                writer.Write(generated != null ? EditorJsonUtility.ToJson(generated) : "no-generated-set");
                var replacement = MapGrowthGeneratedOwnership.Resolve(scene, replacedRoots);
                writer.Write(replacement.Length);
                foreach (var root in replacement.OrderBy(g => g.GetEntityId().ToString(), StringComparer.Ordinal)) writer.Write(root.GetEntityId().ToString());
                writer.Write(markup.bodyProfile != null ? JsonUtility.ToJson(markup.bodyProfile.data) : "missing-body");
                var registry = BlockoutRegistryFactory.Current;
                if (registry == null) throw new ArgumentException("Реестр блоков отсутствует.");
                Dependency(writer, registry);
                foreach (var definition in registry.Definitions.Where(d => d != null).OrderBy(d => d.shapeId, StringComparer.Ordinal))
                {
                    writer.Write(EditorJsonUtility.ToJson(definition)); Dependency(writer, definition);
                    Vector(writer, BlockoutRegistryFactory.DefaultDimensions(definition));
                    var step = definition.geometryPrefab != null ? definition.geometryPrefab.GetComponent<BlockoutSteppedGeometry>() : null;
                    if (step != null) { writer.Write(step.topSize.x); writer.Write(step.topSize.y); }
                }
                var colliders = MapGrowthFrozenGeometry.SourceColliders(scene); writer.Write(colliders.Length);
                foreach (var collider in colliders)
                {
                    writer.Write(GlobalObjectId.GetGlobalObjectIdSlow(collider).ToString());
                    writer.Write(EditorJsonUtility.ToJson(collider)); Transform(writer, collider.transform);
                    for (var t = collider.transform; t != null; t = t.parent)
                    {
                        Transform(writer, t);
                        var surface = t.GetComponent<CoverSurface>();
                        if (surface != null) writer.Write(EditorJsonUtility.ToJson(surface));
                        var block = t.GetComponent<BlockoutBlockInstance>();
                        if (block != null) writer.Write(EditorJsonUtility.ToJson(block));
                        var part = t.GetComponent<BlockoutSectionPart>();
                        if (part != null)
                        {
                            writer.Write(part.sectionIndex);
                            writer.Write(part.owner != null ? GlobalObjectId.GetGlobalObjectIdSlow(part.owner).ToString() : "missing-owner");
                            writer.Write(part.owner != null && part.owner.Initialized);
                            if (part.owner != null) foreach (var sibling in part.owner.parts)
                                writer.Write(sibling != null ? GlobalObjectId.GetGlobalObjectIdSlow(sibling).ToString() : "missing-part");
                        }
                        writer.Write(t.GetComponent<VaultableObstacle>() != null);
                    }
                    if (collider is MeshCollider mesh && mesh.sharedMesh != null) Mesh(writer, mesh.sharedMesh);
                }
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var zone in root.GetComponentsInChildren<TeamSpawnZone>(true))
                    {
                        writer.Write(GlobalObjectId.GetGlobalObjectIdSlow(zone).ToString()); Transform(writer, zone.transform);
                        writer.Write(zone.HomeTeam != null ? zone.HomeTeam.name : "");
                        var box = zone.GetComponent<BoxCollider>(); writer.Write(box != null ? EditorJsonUtility.ToJson(box) : "missing-zone-box");
                    }
                    foreach (var arena in root.GetComponentsInChildren<PhysicalArenaDefinition>(true))
                    { writer.Write(EditorJsonUtility.ToJson(arena)); Transform(writer, arena.transform); }
                    foreach (var marker in root.GetComponentsInChildren<PhysicalObstacleMarker>(true))
                    {
                        writer.Write(EditorJsonUtility.ToJson(marker)); Transform(writer, marker.transform);
                        var arena = marker.GetComponentInParent<PhysicalArenaDefinition>(true);
                        bool valid = marker.TryBounds(arena, out var bounds, out var reason); writer.Write(valid); writer.Write(reason ?? "");
                        if (valid) { Vector(writer, bounds.center); Vector(writer, bounds.size); }
                    }
                    foreach (var shape in root.GetComponentsInChildren<PhysicalArenaShape>(true))
                    { writer.Write(EditorJsonUtility.ToJson(shape)); Transform(writer, shape.transform); }
                }
                writer.Flush();
                using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }
        private static void Dependency(BinaryWriter writer, UnityEngine.Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset); writer.Write(path ?? "");
            writer.Write(string.IsNullOrEmpty(path) ? "" : AssetDatabase.GetAssetDependencyHash(path).ToString());
        }
        private static void Mesh(BinaryWriter writer, Mesh mesh)
        {
            Dependency(writer, mesh); writer.Write(mesh.vertexCount); writer.Write(mesh.subMeshCount);
            if (!mesh.isReadable)
            {
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mesh)))
                    throw new NotSupportedException("Нельзя зафиксировать версию нечитаемого временного меша: " + mesh.name);
                return;
            }
            var vertices = mesh.vertices; writer.Write(vertices.Length); foreach (var vertex in vertices) Vector(writer, vertex);
            for (int i = 0; i < mesh.subMeshCount; i++)
            { var indices = mesh.GetIndices(i); writer.Write((int)mesh.GetTopology(i)); writer.Write(indices.Length); foreach (int index in indices) writer.Write(index); }
        }
        private static void Transform(BinaryWriter writer, Transform transform)
        {
            writer.Write(transform.name); writer.Write(transform.gameObject.layer);
            var matrix = transform.localToWorldMatrix; for (int i = 0; i < 16; i++) writer.Write(matrix[i]);
        }
        private static void Vector(BinaryWriter writer, Vector3 vector)
        { writer.Write(vector.x); writer.Write(vector.y); writer.Write(vector.z); }
    }
}
