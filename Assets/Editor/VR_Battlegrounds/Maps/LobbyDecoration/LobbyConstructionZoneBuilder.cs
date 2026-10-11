using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Статичная стройплощадка: только экземпляры, без изменения исходных моделей.</summary>
    public static class LobbyConstructionZoneBuilder
    {
        public static readonly string[] RequiredSources = {
            "SM_Concrete_beam_3m_A", "SM_Concrete_pile_A", "SM_Concrete_pile_B",
            "SM_Concrete_support_A", "SM_pipe_15",
            "SM_Concrete_Barier_01"
        };

        public static void Build(Transform parent, Bounds arena, Bounds tent)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (parent.Find("ConstructionZone") != null)
                throw new InvalidOperationException("ConstructionZone уже существует; повторная сборка запрещена.");
            if (!Finite(arena.center) || !Finite(arena.size) || arena.size.x <= 0 || arena.size.z <= 0)
                throw new InvalidOperationException("Некорректные авторитетные bounds арены.");
            var sources = RequiredSources.ToDictionary(s => s, LobbyMilitaryCampSources.Prefab);
            if (sources.Any(p => p.Value == null)) throw new InvalidOperationException("Отсутствует источник стройплощадки.");
            var root = new GameObject("ConstructionZone").transform;
            root.SetParent(parent, false);
            var origin = new Vector3(arena.center.x, arena.max.y, arena.center.z);
            var occupied = new List<Bounds>();
            try
            {
                WorkPad(root, origin);
                // Восточный проход X[-23,-20] обращён к центру лагеря и остаётся свободным.
                Stockpile(sources, root, origin, occupied, "SM_Concrete_pile_A", "ConcretePileStockA",
                    new Vector2(-28.5f, 12.4f), new Vector2(1.6f, 3f), new Vector3(90, 0, 0), 4, 2);
                Stockpile(sources, root, origin, occupied, "SM_Concrete_pile_B", "ConcretePileStockB",
                    new Vector2(-28.5f, 16.1f), new Vector2(1.6f, 3.3f), new Vector3(90, 0, 0), 4, 2);
                Stockpile(sources, root, origin, occupied, "SM_pipe_15", "MetalPipeStock",
                    new Vector2(-25f, 19.5f), new Vector2(1.4f, 2f), new Vector3(90, 0, 0), 4, 1);
                Place(sources, root, origin, occupied, "SM_Concrete_Barier_01", "LowWestBoundary",
                    new Vector2(-30.5f, 16.1f), new Vector2(.8f, 3.4f), 0);
                // Отдельное место сборки, материалы лежат на грунте, а не висят над подложкой.
                Stockpile(sources, root, origin, occupied, "SM_Concrete_beam_3m_A", "ConcreteBeamStock",
                    new Vector2(-25.2f, 13.6f), new Vector2(1.4f, 5.4f), new Vector3(0, 90, 0), 2, 2);
                Place(sources, root, origin, occupied, "SM_Concrete_support_A", "AssemblySupport",
                    new Vector2(-28.5f, 20f), new Vector2(.7f, 2.8f), 0, .004f);
                ValidateZone(root, origin, arena, tent);
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                throw;
            }
        }

        private static void Stockpile(Dictionary<string, GameObject> sources, Transform root, Vector3 origin,
            List<Bounds> occupied, string stem, string role, Vector2 center, Vector2 envelope,
            Vector3 angles, int columns, int layers)
        {
            var group = new GameObject(role).transform;
            group.SetParent(root, false);
            var first = (GameObject)PrefabUtility.InstantiatePrefab(sources[stem], group);
            first.transform.localRotation = Quaternion.Euler(angles);
            first.transform.localScale = Vector3.one;
            Sanitize(first);
            var native = MeshBounds(first.transform, group);
            if (native.size.z < 1 || native.size.y >= native.size.z)
                throw new InvalidOperationException(stem + " не является горизонтальным длинным материалом после заданного поворота: " + native.size);
            first.transform.localPosition -= new Vector3(native.center.x, native.min.y, native.center.z);
            for (var layer = 0; layer < layers; layer++)
            for (var column = 0; column < columns; column++)
            {
                var piece = layer == 0 && column == 0 ? first : UnityEngine.Object.Instantiate(first, group);
                piece.name = stem + "_Layer" + layer + "_Column" + column;
                piece.transform.localPosition = first.transform.localPosition +
                    new Vector3(column * (native.size.x + .03f), layer * native.size.y, 0);
            }
            var bounds = MeshBounds(group, root);
            if (bounds.size.x > envelope.x || bounds.size.z > envelope.y)
                throw new InvalidOperationException(stem + " stockpile не помещается: actual=" + bounds.size);
            group.localPosition += origin + new Vector3(center.x, 0, center.y)
                - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            bounds = MeshBounds(group, root);
            RegisterBounds(occupied, bounds, role);
        }

        private static void WorkPad(Transform root, Vector3 origin)
        {
            const string folder = "Assets/Materials/Lobby/Exterior/ConstructionZone";
            const string path = folder + "/CompactedWorkPad.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Materials/Lobby/Exterior", "ConstructionZone");
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP/Lit отсутствует для рабочей подложки.");
                material = new Material(shader) { name = "CompactedWorkPad", enableInstancing = true };
                material.SetColor("_BaseColor", new Color(.31f, .29f, .25f));
                material.SetFloat("_Smoothness", .04f);
                AssetDatabase.CreateAsset(material, path);
            }
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "AssemblyWorkPad__ProceduralSurface";
            pad.transform.SetParent(root, false);
            pad.transform.localPosition = origin + new Vector3(-28.5f, .002f, 20f);
            pad.transform.localScale = new Vector3(2.2f, .004f, 3.3f);
            pad.GetComponent<MeshRenderer>().sharedMaterial = material;
            Sanitize(pad);
        }

        private static void Place(Dictionary<string, GameObject> sources, Transform root, Vector3 origin,
            List<Bounds> occupied, string stem, string role, Vector2 center, Vector2 envelope, float yaw, float groundLift = 0)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(sources[stem], root);
            go.name = role + "__" + stem;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one;
            Sanitize(go);
            var bounds = MeshBounds(go.transform, root);
            if (bounds.size.x > envelope.x || bounds.size.z > envelope.y)
                throw new InvalidOperationException(stem + " не помещается в зарезервированный footprint " + envelope + "; actual=" + bounds.size);
            go.transform.localPosition += origin + new Vector3(center.x, groundLift, center.y)
                - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            bounds = MeshBounds(go.transform, root);
            RegisterBounds(occupied, bounds, role);
        }

        private static void RegisterBounds(List<Bounds> occupied, Bounds bounds, string role)
        {
            foreach (var other in occupied)
                if (HorizontalOverlap(bounds, other, .06f))
                    throw new InvalidOperationException("Пересечение строительных материалов: " + role);
            occupied.Add(bounds);
        }

        private static bool HorizontalOverlap(Bounds a, Bounds b, float gap) =>
            a.min.x < b.max.x + gap && a.max.x > b.min.x - gap &&
            a.min.z < b.max.z + gap && a.max.z > b.min.z - gap;

        private static void ValidateZone(Transform root, Vector3 origin, Bounds arena, Bounds tent)
        {
            var bounds = MeshBounds(root, root);
            if (bounds.min.x < origin.x - 31 || bounds.max.x > origin.x - 23 ||
                bounds.min.z < origin.z + 10 || bounds.max.z > origin.z + 22)
                throw new InvalidOperationException("Стройплощадка вышла из зоны или заняла восточный проход: " + bounds);
            if (bounds.min.y < arena.max.y - .005f)
                throw new InvalidOperationException("Материалы оказались ниже опорного грунта.");
            if (HorizontalOverlap(bounds, tent, 0))
                throw new InvalidOperationException("Стройплощадка пересекает шатёр.");
        }

        private static Bounds MeshBounds(Transform subtree, Transform space)
        {
            var result = new Bounds();
            var hasMesh = false;
            foreach (var filter in subtree.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) throw new InvalidOperationException("Пустой mesh: " + filter.name);
                var b = filter.sharedMesh.bounds;
                var matrix = space.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (var i = 0; i < 8; i++)
                {
                    var point = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!hasMesh) { result = new Bounds(point, Vector3.zero); hasMesh = true; }
                    else result.Encapsulate(point);
                }
            }
            if (!hasMesh) throw new InvalidOperationException("Источник без геометрии: " + subtree.name);
            return result;
        }

        private static void Sanitize(GameObject go)
        {
            if (go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0)
                throw new InvalidOperationException("Статичная стройплощадка не принимает skinned-источник: " + go.name);
            foreach (var lod in go.GetComponentsInChildren<LODGroup>(true))
                if (lod.GetLODs().Any(level => level.renderers.Length == 0 || level.renderers.Any(renderer => renderer == null)))
                    throw new InvalidOperationException("Пустой LOD в источнике: " + go.name);
            foreach (var component in go.GetComponentsInChildren<MonoBehaviour>(true))
                UnityEngine.Object.DestroyImmediate(component);
            foreach (var component in go.GetComponentsInChildren<Component>(true))
            {
                if (component == null) throw new InvalidOperationException("Missing script в исходном prefab.");
                if (!(component is Transform) && !(component is MeshRenderer) &&
                    !(component is MeshFilter) && !(component is LODGroup))
                    UnityEngine.Object.DestroyImmediate(component);
            }
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterials.Any(material => material == null))
                    throw new InvalidOperationException("Не назначен материал: " + renderer.name);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
            foreach (var transform in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(transform.gameObject, StaticEditorFlags.OccludeeStatic);
        }

        private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
            && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }
}
