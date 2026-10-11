using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Низкие границы шатра и отдельные склады за задним торцом; геометрией арены не управляет.</summary>
    public static class LobbyPerimeterDecorationBuilder
    {
        private const string Report = "tasks/lobby-decoration/reports/implementation/exterior-perimeter/";
        private static readonly string[] Keys = { "SandBarrierA", "SandBarrierB", "AmmoA", "AmmoB", "CrateA", "CrateB", "Barrel", "Fence" };
        private static readonly string[] Guids =
        {
            "abc00000000008060387139124418761", "abc00000000010680102058838128715", "abc00000000012017236203265661070", "abc00000000003273235488023318933",
            "abc00000000015158636773066772218", "abc00000000006183764951078403713",
            "abc00000000011043161318511504812", "abc00000000004669335183269123642"
        };

        [Serializable] private sealed class Dependency
        {
            public string role;
            public string guid;
            public string path;
            public bool loaded;
        }

        [Serializable] private sealed class DependencyReport
        {
            public string scope = "GUID lookup and source readiness only; native preview/Quest/user acceptance separate";
            public Dependency[] dependencies;
            public string[] errors;
        }

        /// <summary>Bounds передаются в общей локальной системе авторинга; +Z — единственное направление стрельбы.</summary>
        public static void Build(Transform parent, Bounds arena, Bounds tent)
        {
            if (!parent) throw new ArgumentNullException(nameof(parent));
            ValidateBounds(arena, tent);
            if (parent.Find("PerimeterDecoration")) throw new InvalidOperationException("PerimeterDecoration уже существует: общий авторинг должен подготовить новый собственный draft.");
            var sources = ResolveSources();
            var root = Group("PerimeterDecoration", parent, Vector3.zero, 0);
            float ground = arena.max.y;
            float width = tent.size.x;

            // Низкая непрерывная линия находится за краем шатра, но внутри физической арены.
            var barriers = new[] { sources["SandBarrierA"], sources["SandBarrierB"] };
            Boundary(root, "NorthLowShootingBoundary", tent.center.x, ground, tent.max.z + .44f, width, barriers, false);
            Boundary(root, "SouthLowCampBoundary", tent.center.x, ground, tent.min.z - .44f, width, barriers, true);

            // Только низкие границы; припасы и техника принадлежат своим функциональным зонам.
        }

        private static void ValidateBounds(Bounds arena, Bounds tent)
        {
            if (arena.size.x < 6 || arena.size.z < 8 || tent.size.x < 6 || tent.size.z < 4)
                throw new ArgumentException("Слишком малые bounds для подготовленного периметра.");
            if (Mathf.Abs(tent.min.x - arena.min.x) > .05f || Mathf.Abs(tent.max.x - arena.max.x) > .05f ||
                Mathf.Abs((tent.min.z - arena.min.z) - 2f) > .05f || Mathf.Abs((arena.max.z - tent.max.z) - 2f) > .05f)
                throw new ArgumentException("Периметр ожидает ширину шатра по арене и отступ каждого торца ровно 2 м.");
        }

        private static Dictionary<string, GameObject> ResolveSources()
        {
            var dependencies = new List<Dependency>();
            var errors = new List<string>();
            var result = new Dictionary<string, GameObject>();
            for (int i = 0; i < Keys.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(Guids[i]);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                dependencies.Add(new Dependency { role = Keys[i], guid = Guids[i], path = path, loaded = source != null });
                if (!source) errors.Add("Не найден source " + Keys[i] + " GUID=" + Guids[i]);
                else result.Add(Keys[i], source);
            }
            Directory.CreateDirectory(Report);
            File.WriteAllText(Report + "asset-dependencies.json", JsonUtility.ToJson(new DependencyReport { dependencies = dependencies.ToArray(), errors = errors.ToArray() }, true));
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            return result;
        }

        private static void Boundary(Transform parent, string name, float x, float y, float z, float width, GameObject[] sources, bool rear)
        {
            var line = Group(name, parent, Vector3.zero, 0);
            int count = Mathf.CeilToInt((width - .6f) / 1.65f);
            float step = (width - .6f) / count;
            for (int i = 0; i < count; i++)
            {
                float center = x - (width - .6f) * .5f + (i + .5f) * step;
                float angle = i % 3 == 0 ? -2 : i % 3 == 1 ? 1.5f : .5f;
                var bag = Place(sources[0], "MilitaryCampSandBarrier", line,
                    new Vector3(center, y, z + (i % 2 == 0 ? .025f : -.025f)), .55f, 90 + angle, new Vector2(step + .035f, .78f));
                // Продольная ось source01 — Z: поворот переводит её в X. Размер сохраняем равномерным.
                var finalBounds = LocalBounds(bag,line);
                if (finalBounds.max.y > y + .65f) throw new InvalidOperationException("Барьер выше допуска 0,65 м.");
            }
            foreach (int side in new[] { -1,1 })
                Place(sources[1], "MilitaryCampFlankBagPile", line,
                    new Vector3(x + side*(width*.5f-.9f), y, z + (rear?-.62f:.62f)), .52f, side*13, new Vector2(.8f,.8f));
        }

        private static void RearStore(Transform parent, string name, Vector3 foot, float yaw, Dictionary<string, GameObject> sources, bool variant)
        {
            var group = Group(name, parent, foot, yaw);
            var baseCrate = Place(sources["CrateA"], "ClosedSupplyCrate", group, new Vector3(-.85f, 0, 0), .60f, variant ? -12f : 0, new Vector2(1.8f, 1.1f));
            Place(sources["CrateB"], "SeparatedSupplyCrate", group, new Vector3(.9f, 0, variant ? 1.1f : -.8f), .46f, variant ? 24f : 9f, new Vector2(1.5f, .9f));
            Place(sources[variant ? "AmmoB" : "AmmoA"], "SmallAmmoCase", group, new Vector3(-.75f, LocalBounds(baseCrate, group).max.y + .01f, 0), .18f, -9f, new Vector2(.65f, .5f));
            Place(sources["Barrel"], "SupplyBarrel", group, new Vector3(variant ? 2.5f : -2.4f, 0, .45f), .95f, 0, new Vector2(.85f, .85f));
        }

        private static GameObject Place(GameObject source, string name, Transform parent, Vector3 foot, float height, float yaw, Vector2 footprint)
        {
            var go = UnityEngine.Object.Instantiate(source, parent);
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one;
            Sanitize(go);
            var bounds = LocalBounds(go, parent);
            if (bounds.size.y < .001f) throw new InvalidOperationException("Source имеет пустую высоту: " + source.name);
            float scale = Mathf.Min(height / bounds.size.y, footprint.x / Mathf.Max(.001f, bounds.size.x), footprint.y / Mathf.Max(.001f, bounds.size.z));
            go.transform.localScale *= scale;
            bounds = LocalBounds(go, parent);
            go.transform.localPosition = foot - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            return go;
        }

        private static Bounds LocalBounds(GameObject go, Transform parent)
        {
            var filters = go.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh!=null).ToArray();
            if (filters.Length == 0) throw new InvalidOperationException("Source без static Mesh: " + go.name);
            Bounds result = new Bounds();
            bool first = true;
            foreach (var filter in filters)
                foreach (int x in new[] { -1, 1 })
                    foreach (int y in new[] { -1, 1 })
                        foreach (int z in new[] { -1, 1 })
                        {
                            var b = filter.sharedMesh.bounds;
                            var p = parent.InverseTransformPoint(filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z))));
                            if (first) { result = new Bounds(p, Vector3.zero); first = false; }
                            else result.Encapsulate(p);
                        }
            return result;
        }

        private static void RequireInside(GameObject go, Transform root, Bounds arena, float margin, float maxHeight)
        {
            var b = LocalBounds(go, root);
            if (b.min.x < arena.min.x + margin || b.max.x > arena.max.x - margin || b.min.z < arena.min.z + margin || b.max.z > arena.max.z - margin || b.max.y > arena.max.y + maxHeight + .01f)
                throw new InvalidOperationException("Low strip prop нарушил bounds/высоту: " + go.name);
        }

        private static Transform Group(string name, Transform parent, Vector3 position, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        private static void Sanitize(GameObject go)
        {
            // Сначала companion behaviours, чтобы RequireComponent не мешал удалить source Light.
            foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(behaviour);
            foreach (var component in go.GetComponentsInChildren<Component>(true))
                if (component && !(component is Transform) && !(component is MeshFilter) && !(component is Renderer) && !(component is LODGroup))
                    UnityEngine.Object.DestroyImmediate(component);
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.OccludeeStatic);
            // Все source LOD levels сохраняются; не пересобираем группу в четыре уровня.
        }

        private static Material CanvasMaterial()
        {
            const string folder = "Assets/Materials/Lobby/Exterior/Perimeter";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Materials/Lobby/Exterior")) throw new InvalidOperationException("Общий авторинг должен подготовить существующий Exterior material root.");
                AssetDatabase.CreateFolder("Assets/Materials/Lobby/Exterior", "Perimeter");
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(folder + "/BoundaryCanvas.mat");
            if (material) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("URP/Lit отсутствует.");
            material = new Material(shader) { name = "BoundaryCanvas", enableInstancing = true };
            material.SetColor("_BaseColor", new Color(.43f, .41f, .29f));
            material.SetFloat("_Smoothness", .08f);
            AssetDatabase.CreateAsset(material, folder + "/BoundaryCanvas.mat");
            return material;
        }
    }
}
