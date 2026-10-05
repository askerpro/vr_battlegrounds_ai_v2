using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Maps;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Главный поток владеет копиями мешей и паспортами коллайдеров. Игровые lifecycle-компоненты не клонируются.</summary>
    internal sealed class MapGrowthFrozenGeometry : IDisposable
    {
        private sealed class Node
        {
            public int parent = -1, layer, sectionIndex = -1, sectionOwner = -1;
            public string name;
            public Vector3 position, scale;
            public Quaternion rotation;
            public bool vaultable, sectionGroup, hasCover, hasBlock;
            public CoverClass cover, material;
            public float penetration;
            public Vector3 dimensions;
            public BlockoutOpeningSettings openings;
            public BlockoutSectionSettings[] sections;
            public BlockoutSolidPart[][] solids;
            public readonly List<Shape> shapes = new List<Shape>();
        }
        private sealed class Shape
        {
            public Type type;
            public Vector3 center, size;
            public float radius, height, contactOffset;
            public int direction;
            public bool convex;
            public MeshColliderCookingOptions cooking;
            public Mesh mesh;
            public string ownerId;
        }
        private readonly List<Node> nodes = new List<Node>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private bool disposed;

        internal static Collider[] SourceColliders(Scene scene)
        {
            var physical = PhysicalArenaSources.Collect(scene);
            return scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Collider>(false))
                .Where(c => BlockoutSupportSurfaces.IsActiveSolid(c) && c.attachedRigidbody == null && !physical.Contains(c))
                .OrderBy(c => GlobalObjectId.GetGlobalObjectIdSlow(c).ToString(), StringComparer.Ordinal).ToArray();
        }
        public static MapGrowthFrozenGeometry Capture(Scene scene, GameObject[] excludedRoots = null)
        {
            var captured = new MapGrowthFrozenGeometry();
            try
            {
                var indices = new Dictionary<Transform, int>();
                var meshCopies = new Dictionary<Mesh, Mesh>();
                foreach (var collider in SourceColliders(scene))
                {
                    if (excludedRoots != null && MapGrowthGeneratedOwnership.Excludes(collider, excludedRoots)) continue;
                    int index = captured.AddNode(collider.transform, indices);
                    var shape = new Shape { type = collider.GetType(), contactOffset = collider.contactOffset,
                        ownerId = GlobalObjectId.GetGlobalObjectIdSlow(collider).ToString() };
                    if (collider is BoxCollider box) { shape.center = box.center; shape.size = box.size; }
                    else if (collider is SphereCollider sphere) { shape.center = sphere.center; shape.radius = sphere.radius; }
                    else if (collider is CapsuleCollider capsule)
                    { shape.center = capsule.center; shape.radius = capsule.radius; shape.height = capsule.height; shape.direction = capsule.direction; }
                    else if (collider is MeshCollider meshCollider)
                    {
                        if (meshCollider.sharedMesh == null) throw new ArgumentException("Пустой MeshCollider: " + shape.ownerId);
                        if (!meshCopies.TryGetValue(meshCollider.sharedMesh, out shape.mesh))
                        {
                            shape.mesh = Object.Instantiate(meshCollider.sharedMesh);
                            shape.mesh.hideFlags = HideFlags.HideAndDontSave;
                            captured.meshes.Add(shape.mesh); meshCopies.Add(meshCollider.sharedMesh, shape.mesh);
                        }
                        shape.convex = meshCollider.convex; shape.cooking = meshCollider.cookingOptions;
                    }
                    else throw new NotSupportedException("Захват не поддерживает " + shape.type.Name + ": " + shape.ownerId);
                    captured.nodes[index].shapes.Add(shape);
                }
                // Секции получают копию своего общего владельца, а не ссылки в рабочую сцену.
                foreach (var pair in indices.ToArray())
                {
                    var part = pair.Key.GetComponent<BlockoutSectionPart>();
                    if (part == null || part.owner == null || !part.owner.Initialized) continue;
                    int owner = captured.AddNode(part.owner.transform, indices);
                    captured.nodes[owner].sectionGroup = true;
                    if (captured.nodes[owner].solids == null)
                    {
                        captured.nodes[owner].solids = new BlockoutSolidPart[3][];
                        for (int s = 0; s < 3; s++)
                            if (part.owner.TryCopyBuiltSolidParts(s, out var solid)) captured.nodes[owner].solids[s] = solid;
                    }
                    captured.nodes[pair.Value].sectionOwner = owner;
                    captured.nodes[pair.Value].sectionIndex = part.sectionIndex;
                }
                return captured;
            }
            catch { captured.Dispose(); throw; }
        }
        private int AddNode(Transform source, Dictionary<Transform, int> indices)
        {
            if (indices.TryGetValue(source, out int existing)) return existing;
            int parent = source.parent == null ? -1 : AddNode(source.parent, indices);
            var node = new Node { name = source.name, layer = source.gameObject.layer, parent = parent,
                position = source.localPosition, rotation = source.localRotation, scale = source.localScale,
                vaultable = source.GetComponent<VaultableObstacle>() != null };
            var surface = source.GetComponent<CoverSurface>();
            if (surface != null) { node.hasCover = true; node.cover = surface.Class; node.penetration = surface.PenetrationModifier; }
            var block = source.GetComponent<BlockoutBlockInstance>();
            if (block != null)
            {
                node.hasBlock = true; node.dimensions = block.dimensions; node.material = block.material;
                node.openings = block.openings; node.sections = BlockoutSectionSettings.Copy(block.sections);
            }
            int index = nodes.Count; nodes.Add(node); indices.Add(source, index); return index;
        }
        public Dictionary<Collider, string> Instantiate(Scene scene)
        {
            if (disposed) throw new ObjectDisposedException(nameof(MapGrowthFrozenGeometry));
            var objects = new GameObject[nodes.Count]; var ownership = new Dictionary<Collider, string>();
            try
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i]; var go = new GameObject(node.name); objects[i] = go;
                    go.hideFlags = HideFlags.HideAndDontSave; go.layer = node.layer;
                    if (node.parent >= 0) go.transform.SetParent(objects[node.parent].transform, false);
                    else SceneManager.MoveGameObjectToScene(go, scene);
                    go.transform.localPosition = node.position; go.transform.localRotation = node.rotation; go.transform.localScale = node.scale;
                    if (node.hasBlock)
                    {
                        var block = go.AddComponent<BlockoutBlockInstance>(); block.dimensions = node.dimensions;
                        block.material = node.material; block.openings = node.openings; block.sections = BlockoutSectionSettings.Copy(node.sections);
                    }
                    if (node.vaultable) go.AddComponent<VaultableObstacle>();
                    if (node.sectionGroup) go.AddComponent<BlockoutSectionGeometry>();
                    if (node.sectionOwner >= 0)
                    {
                        var part = go.AddComponent<BlockoutSectionPart>();
                        part.owner = objects[node.sectionOwner].GetComponent<BlockoutSectionGeometry>(); part.sectionIndex = node.sectionIndex;
                    }
                    if (node.hasCover)
                    {
                        var surface = go.GetComponent<CoverSurface>() ?? go.AddComponent<CoverSurface>();
                        surface.Class = node.cover; surface.PenetrationModifier = node.penetration;
                    }
                    foreach (var shape in node.shapes)
                    {
                        // RequireComponent секции уже создал свой единственный MeshCollider.
                        var collider = node.sectionOwner >= 0 && shape.type == typeof(MeshCollider)
                            ? go.GetComponent<MeshCollider>() : (Collider)go.AddComponent(shape.type);
                        if (collider is BoxCollider box) { box.center = shape.center; box.size = shape.size; }
                        else if (collider is SphereCollider sphere) { sphere.center = shape.center; sphere.radius = shape.radius; }
                        else if (collider is CapsuleCollider capsule)
                        { capsule.center = shape.center; capsule.radius = shape.radius; capsule.height = shape.height; capsule.direction = shape.direction; }
                        else if (collider is MeshCollider mesh)
                        {
                            mesh.cookingOptions = shape.cooking; mesh.sharedMesh = shape.mesh; mesh.convex = shape.convex;
                            var part = go.GetComponent<BlockoutSectionPart>();
                            if (part != null) { go.GetComponent<MeshFilter>().sharedMesh = shape.mesh; part.SetMesh(shape.mesh); }
                        }
                        collider.contactOffset = shape.contactOffset; ownership.Add(collider, shape.ownerId);
                    }
                }
                for (int i = 0; i < nodes.Count; i++) if (nodes[i].sectionGroup)
                {
                    var parts = new BlockoutSectionPart[3];
                    for (int j = 0; j < nodes.Count; j++) if (nodes[j].sectionOwner == i)
                    {
                        int section = nodes[j].sectionIndex;
                        if (section < 0 || section > 2 || parts[section] != null) throw new ArgumentException("Неоднозначная секция замороженного блока.");
                        parts[section] = objects[j].GetComponent<BlockoutSectionPart>();
                    }
                    objects[i].GetComponent<BlockoutSectionGeometry>().AdoptFrozenParts(parts, nodes[i].solids);
                }
                Physics.SyncTransforms(); return ownership;
            }
            catch
            {
                for (int i = 0; i < objects.Length; i++) if (objects[i] != null && nodes[i].parent < 0) Object.DestroyImmediate(objects[i]);
                throw;
            }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            foreach (var mesh in meshes) if (mesh != null) Object.DestroyImmediate(mesh);
            meshes.Clear(); nodes.Clear();
        }
    }
}
