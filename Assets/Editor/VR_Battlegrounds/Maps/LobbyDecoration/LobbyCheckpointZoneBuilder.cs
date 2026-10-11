using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Статичный КПП: открытый въезд, настоящий пост охраны и равномерно масштабированная вышка.</summary>
    public static class LobbyCheckpointZoneBuilder
    {
        public static readonly string[] RequiredSources = {
            "SM_WatchTower_01", "SM_Building_wall_4x4m_A", "SM_Building_wall_window_2m_A",
            "SM_Building_roof_4x4_01", "SM_Door_single", "SM_Fence_long_4m",
            "SM_Concrete_Barier_01", "SM_Concrete_Barier_02", "SM_Door_lamp_a"
        };
        [Serializable] public sealed class Item
        {
            public string role, source;
            public Bounds nativeBounds, worldBounds;
            public float uniformScale;
            public int renderers, lodLevels, lod0Triangles;
        }
        [Serializable] public sealed class Report
        {
            public bool layoutValidated;
            public Bounds arena, tent, actualBounds, road;
            public List<Item> items = new List<Item>();
            public int renderers, lod0Triangles, colliders, lights, scripts;
            public string lightingSocket;
            public Vector3 lightingPosition;
        }
        public static Report LastReport { get; private set; }

        public static void Build(Transform parent, Bounds arena, Bounds tent)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (parent.Find("CheckpointZone") != null) throw new InvalidOperationException("CheckpointZone уже существует.");
            if (!Finite(arena.center) || !Finite(arena.size) || arena.size.x <= 0 || arena.size.z <= 0 ||
                !Finite(tent.center) || !Finite(tent.size)) throw new InvalidOperationException("Некорректные bounds площадки/шатра.");
            // Габариты приходят в мировом пространстве; нельзя молча исказить их масштабом родителя.
            if ((parent.lossyScale - Vector3.one).sqrMagnitude > .000001f || Quaternion.Angle(parent.rotation, Quaternion.identity) > .001f)
                throw new InvalidOperationException("Родитель декорации должен иметь мировой scale=1 и rotation=identity.");
            var sources = RequiredSources.ToDictionary(s => s, LobbyMilitaryCampSources.Prefab);
            if (sources.Values.Any(s => s == null)) throw new InvalidOperationException("Не импортирован источник КПП.");
            var origin = new Vector3(arena.center.x, arena.max.y, arena.center.z);
            var root = new GameObject("CheckpointZone").transform;
            root.SetParent(parent, false);
            root.position = Vector3.zero;
            var createdAssets = new List<string>();
            LastReport = new Report { arena = arena, tent = tent, road = Envelope(origin, 24, 32, -38, -27, 12) };
            try
            {
                var wallMaterial = SourceMaterial(sources["SM_Building_wall_4x4m_A"]);
                // Первый материал забора — прозрачная сетка; стойкам нужен непрозрачный металл.
                var steelMaterial = SourceMaterial(sources["SM_Fence_long_4m"], "M_Metal_01");
                var signMaterial = Finish("CheckpointSignOlive", new Color(.15f, .19f, .14f), createdAssets);
                var letteringMaterial = Finish("CheckpointMarkingIvory", new Color(.80f, .78f, .66f), createdAssets);
                var booth = Group("GuardHousing", root);
                RoadSurface(root, origin, createdAssets);
                GuardHousing(sources, booth, origin, wallMaterial, signMaterial, letteringMaterial);
                var tower = Group("Watchtower", root);
                var towerInstance = Instantiate(sources["SM_WatchTower_01"], tower, "Watchtower__SM_WatchTower_01", 0);
                var native = Measure(towerInstance.transform);
                if (native.size.y < 16 || native.size.y > 18) throw new InvalidOperationException("Неожиданный размер исходной вышки: " + native.size);
                float scale = 8f / native.size.y;
                towerInstance.transform.localScale = Vector3.one * scale;
                GroundCenter(towerInstance.transform, origin + new Vector3(34, 0, -31));
                Record(towerInstance, "Watchtower", "SM_WatchTower_01", native, scale);
                RequireEnvelope(Measure(tower), Envelope(origin, 32.2f, 35.9f, -33, -29, 8.01f), "вышка");
                var westGate = Group("OpenGateWest", root);
                var eastGate = Group("OpenGateEast", root);
                Gate(sources, westGate, origin, 23.70f, steelMaterial);
                Gate(sources, eastGate, origin, 32.30f, steelMaterial);
                var westBarrier = Group("RoadBarrierWest", root);
                Place(sources, westBarrier, origin, "SM_Concrete_Barier_01", "RoadBarrierWest", new Vector3(23.62f, 0, -31.45f), 0);
                var eastBarrier = Group("RoadBarrierEast", root);
                Place(sources, eastBarrier, origin, "SM_Concrete_Barier_01", "RoadBarrierEast", new Vector3(35.3f, 0, -35.9f), 0);
                var stopLine = Group("GuardApproachBarrier", root);
                Place(sources, stopLine, origin, "SM_Concrete_Barier_02", "GuardApproachBarrier", new Vector3(21, 0, -35.3f), 90);
                Validate(root, origin, arena, tent, new[] { booth, tower, westGate, eastGate, westBarrier, eastBarrier, stopLine });
                LastReport.layoutValidated = true;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                // Только собственные новые материалы; существующие assets и source не удаляются.
                foreach (var path in createdAssets) AssetDatabase.DeleteAsset(path);
                throw;
            }
        }

        private static void GuardHousing(Dictionary<string, GameObject> sources, Transform root, Vector3 origin,
            Material wall, Material sign, Material ivory)
        {
            const float height = 3.00190425f;
            Place(sources, root, origin, "SM_Building_wall_4x4m_A", "GuardBackWall", new Vector3(21, 0, -33.9f), 0);
            for (int side = 0; side < 2; side++) for (int bay = 0; bay < 2; bay++)
            {
                float x = side == 0 ? 19.1f : 22.9f;
                Place(sources, root, origin, "SM_Building_wall_window_2m_A", "GuardWindow_" + side + "_" + bay,
                    new Vector3(x, 0, -33 + bay * 2), side == 0 ? -90 : 90);
                Solid(root, origin + new Vector3(x, 2 + (height - 2) * .5f, -33 + bay * 2),
                    new Vector3(.20f, height - 2, 2), wall, "UpperWindowWall__ProceduralFiller");
            }
            var door = Place(sources, root, origin, "SM_Door_single", "GuardEntryDoor", new Vector3(21, .006f, -30.1f), 0);
            var doorBounds = Measure(door.transform);
            if (doorBounds.size.x < 1.05f || doorBounds.size.x > 1.16f || doorBounds.size.y < 2.05f || doorBounds.size.y > 2.20f)
                throw new InvalidOperationException("Неверная натуральная дверь для поста: " + doorBounds.size);
            float opening = doorBounds.size.x + .012f;
            float sideWidth = (4 - opening) * .5f;
            float top = doorBounds.max.y - origin.y + .006f;
            for (int side = -1; side <= 1; side += 2)
                Solid(root, origin + new Vector3(21 + side * (opening + sideWidth) * .5f, height * .5f, -30.1f),
                    new Vector3(sideWidth, height, .20f), wall, "EntrySideWall__ProceduralFiller");
            Solid(root, origin + new Vector3(21, (height + top) * .5f, -30.1f),
                new Vector3(opening, height - top, .20f), wall, "EntryHeader__ProceduralFiller");
            Place(sources, root, origin, "SM_Building_roof_4x4_01", "GuardRoof", new Vector3(21, height, -32), 0);
            Solid(root, origin + new Vector3(21, .005f, -32), new Vector3(3.8f, .01f, 3.8f), wall, "GuardFloor__ProceduralSurface");
            Solid(root, origin + new Vector3(21, 2.38f, -29.985f), new Vector3(1.3f, .32f, .02f), sign, "GuardSignBoard");
            Label(root, origin + new Vector3(21, 2.38f, -29.968f), "КПП", .19f);
            Place(sources, root, origin, "SM_Door_lamp_a", "GuardEntryWallFixture", new Vector3(21, 2.68f, -29.935f), 0);
            var socket = Group("LightingSocket__GuardEntry__CommonLightingOwner", root);
            socket.position = origin + new Vector3(21, 2.63f, -29.84f);
            socket.rotation = Quaternion.Euler(35, 0, 0);
            LastReport.lightingSocket = "CheckpointZone/GuardHousing/" + socket.name;
            LastReport.lightingPosition = socket.position;
            // Небольшая маркировка по обеим сторонам входа, а не новое физическое укрытие.
            for (int side = -1; side <= 1; side += 2)
                Solid(root, origin + new Vector3(21 + side * .64f, .65f, -29.987f), new Vector3(.07f, .65f, .015f), ivory, "DoorFrameMarking");
            RequireEnvelope(Measure(root), Envelope(origin, 18.85f, 23.2f, -34.1f, -29.8f, 3.3f), "пост охраны");
        }

        private static void Gate(Dictionary<string, GameObject> sources, Transform root, Vector3 origin, float x, Material steel)
        {
            Place(sources, root, origin, "SM_Fence_long_4m", "OpenGateLeaf", new Vector3(x, 0, -35.95f), 90);
            Solid(root, origin + new Vector3(x, 1.23f, -37.85f), new Vector3(.22f, 2.46f, .22f), steel, "GateHingePost");
            Solid(root, origin + new Vector3(x, .10f, -37.85f), new Vector3(.29f, .20f, .29f), steel, "GatePostFoot");
            for (int hinge = 0; hinge < 2; hinge++)
                Solid(root, origin + new Vector3(x, .45f + hinge * 1.25f, -37.72f), new Vector3(.15f, .12f, .15f), steel, "GateHinge");
        }

        private static Transform Group(string name, Transform parent)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        private static GameObject Instantiate(GameObject source, Transform parent, string role, float yaw)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            go.name = role;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one;
            Sanitize(go);
            return go;
        }

        private static GameObject Place(Dictionary<string, GameObject> sources, Transform parent, Vector3 origin,
            string source, string role, Vector3 centerAtFloor, float yaw)
        {
            var go = Instantiate(sources[source], parent, role + "__" + source, yaw);
            var native = Measure(go.transform);
            GroundCenter(go.transform, origin + centerAtFloor);
            Record(go, role, source, native, 1);
            return go;
        }

        private static void GroundCenter(Transform go, Vector3 point)
        {
            var b = Measure(go);
            go.position += point - new Vector3(b.center.x, b.min.y, b.center.z);
            foreach (var lod in go.GetComponentsInChildren<LODGroup>(true)) lod.RecalculateBounds();
        }

        private static void Solid(Transform root, Vector3 center, Vector3 size, Material material, string name)
        {
            if (size.x <= 0 || size.y <= 0 || size.z <= 0) throw new InvalidOperationException("Отрицательный размер простенка.");
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            Sanitize(go);
            Record(go, name, "procedural_architectural_fill", Measure(go.transform), 1);
        }

        private static void Label(Transform root, Vector3 center, string text, float size)
        {
            var go = new GameObject("GuardSign__КПП");
            go.transform.SetParent(root, false);
            go.transform.position = center;
            // TextMesh смотрит локальным -Z, надпись обращена к северному подходу.
            go.transform.rotation = Quaternion.Euler(0, 180, 0);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) throw new InvalidOperationException("Нет встроенного шрифта для вывески КПП.");
            var label = go.AddComponent<TextMesh>();
            label.text = text;
            label.font = font;
            label.fontSize = 64;
            label.characterSize = size;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = new Color(.88f, .86f, .75f);
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            var textBounds = go.GetComponent<MeshRenderer>().bounds;
            if (textBounds.size.x <= 0 || textBounds.size.y <= 0) throw new InvalidOperationException("Пустые bounds вывески КПП.");
            // Однократная равномерная подгонка фактического текста внутри таблички 1.3 × .32 м.
            go.transform.localScale = Vector3.one * Mathf.Min(1.22f / textBounds.size.x, .24f / textBounds.size.y);
            ConfigureRenderer(go.GetComponent<MeshRenderer>());
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
        }

        private static void RoadSurface(Transform root, Vector3 origin, List<string> created)
        {
            const string path = "Assets/Materials/Lobby/Exterior/CheckpointZone/CheckpointRoad.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Lobby/OutsideGravel.mat");
                if (source == null || !source.HasProperty("_BaseMap")) throw new InvalidOperationException("Нет грунтового материала проезда.");
                material = new Material(source) { name = "CheckpointRoad", enableInstancing = true };
                material.SetTextureScale("_BaseMap", new Vector2(4, 5.5f));
                if (material.HasProperty("_BumpMap")) material.SetTextureScale("_BumpMap", new Vector2(4, 5.5f));
                material.SetColor("_BaseColor", new Color(.54f, .52f, .46f));
                AssetDatabase.CreateAsset(material, path); created.Add(path);
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "RoadSurface__OwnedGravel"; go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(origin + new Vector3(28, .012f, -32.5f), Quaternion.Euler(90, 0, 0));
            go.transform.localScale = new Vector3(8, 11, 1);
            go.GetComponent<MeshRenderer>().sharedMaterial = material; Sanitize(go);
            Record(go, "RoadSurface", "owned_gravel_surface_no_physics", Measure(go.transform), 1);
        }

        private static Material SourceMaterial(GameObject source, string requiredName = null)
        {
            var material = source.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials)
                .FirstOrDefault(m => m != null && (requiredName == null || m.name == requiredName));
            if (material == null) throw new InvalidOperationException("Нет исходного PBR материала: " + source.name);
            return material;
        }

        private static Material Finish(string name, Color color, List<string> created)
        {
            const string folder = "Assets/Materials/Lobby/Exterior/CheckpointZone";
            string path = folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Materials/Lobby/Exterior", "CheckpointZone");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Нет URP/Lit для маркировки КПП.");
            material = new Material(shader) { name = name, enableInstancing = true };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .12f);
            AssetDatabase.CreateAsset(material, path);
            created.Add(path);
            return material;
        }

        private static void Record(GameObject go, string role, string source, Bounds native, float scale)
        {
            LastReport.items.Add(new Item { role = role, source = source, nativeBounds = native,
                worldBounds = Measure(go.transform), uniformScale = scale,
                renderers = go.GetComponentsInChildren<Renderer>(true).Length,
                lodLevels = go.GetComponentsInChildren<LODGroup>(true).Sum(l => l.GetLODs().Length),
                lod0Triangles = Lod0Triangles(go) });
        }

        private static int Lod0Triangles(GameObject go)
        {
            var lodGroups = go.GetComponentsInChildren<LODGroup>(true);
            var covered = new HashSet<Renderer>(lodGroups.SelectMany(l => l.GetLODs()).SelectMany(l => l.renderers));
            var active = new HashSet<Renderer>(lodGroups.SelectMany(l => l.GetLODs().Take(1)).SelectMany(l => l.renderers));
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true)) if (!covered.Contains(renderer)) active.Add(renderer);
            int triangles = 0;
            foreach (var renderer in active)
            {
                if (renderer == null || !renderer.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) continue;
                for (int i = 0; i < filter.sharedMesh.subMeshCount; i++) triangles += (int)(filter.sharedMesh.GetIndexCount(i) / 3);
            }
            return triangles;
        }

        private static Bounds Envelope(Vector3 origin, float minX, float maxX, float minZ, float maxZ, float height)
            => new Bounds(origin + new Vector3((minX + maxX) * .5f, height * .5f, (minZ + maxZ) * .5f), new Vector3(maxX - minX, height, maxZ - minZ));

        private static void RequireEnvelope(Bounds actual, Bounds allowed, string role)
        {
            const float epsilon = .002f;
            if (actual.min.x < allowed.min.x - epsilon || actual.max.x > allowed.max.x + epsilon ||
                actual.min.z < allowed.min.z - epsilon || actual.max.z > allowed.max.z + epsilon ||
                actual.min.y < allowed.min.y - epsilon || actual.max.y > allowed.max.y + epsilon)
                throw new InvalidOperationException("КПП: " + role + " вне разрешённого envelope: " + actual);
        }

        private static void Validate(Transform root, Vector3 origin, Bounds arena, Bounds tent, Transform[] groups)
        {
            var reservation = Envelope(origin, 17, 36, -38, -27, 8.1f);
            var service = Envelope(origin, 10, 17, -33, -15, 12);
            var workshop = Envelope(origin, 18, 30, -29, -13, 12);
            var bounds = groups.Select(Measure).ToArray();
            for (int i = 0; i < bounds.Length; i++)
            {
                RequireEnvelope(bounds[i], reservation, groups[i].name);
                if (OverlapXZ(bounds[i], LastReport.road, 0) || OverlapXZ(bounds[i], tent, .1f) ||
                    OverlapXZ(bounds[i], arena, .1f) || OverlapXZ(bounds[i], service, 0) || OverlapXZ(bounds[i], workshop, 0))
                    throw new InvalidOperationException("КПП занимает дорогу/защищённую область: " + groups[i].name);
                for (int j = 0; j < i; j++) if (OverlapXZ(bounds[i], bounds[j], .03f))
                    throw new InvalidOperationException("Пересечение групп КПП: " + groups[i].name + "/" + groups[j].name);
            }
            LastReport.actualBounds = Measure(root);
            LastReport.renderers = root.GetComponentsInChildren<Renderer>(true).Length;
            LastReport.lod0Triangles = Lod0Triangles(root.gameObject);
            LastReport.colliders = root.GetComponentsInChildren<Collider>(true).Length;
            LastReport.lights = root.GetComponentsInChildren<Light>(true).Length;
            LastReport.scripts = root.GetComponentsInChildren<MonoBehaviour>(true).Length;
            if (LastReport.colliders != 0 || LastReport.lights != 0 || LastReport.scripts != 0 ||
                root.GetComponentsInChildren<Animator>(true).Length != 0)
                throw new InvalidOperationException("КПП содержит запрещённые игровые/физические компоненты.");
        }

        private static bool OverlapXZ(Bounds a, Bounds b, float gap) =>
            a.min.x < b.max.x + gap && a.max.x > b.min.x - gap && a.min.z < b.max.z + gap && a.max.z > b.min.z - gap;

        private static Bounds Measure(Transform subtree)
        {
            bool first = true;
            Bounds result = default;
            foreach (var filter in subtree.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) throw new InvalidOperationException("Нет mesh: " + filter.name);
                var b = filter.sharedMesh.bounds;
                var matrix = filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!Finite(p)) throw new InvalidOperationException("Неконечная геометрия: " + filter.name);
                    if (first) { result = new Bounds(p, Vector3.zero); first = false; } else result.Encapsulate(p);
                }
            }
            if (first) throw new InvalidOperationException("Нет статической геометрии: " + subtree.name);
            return result;
        }

        private static void Sanitize(GameObject go)
        {
            if (go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0) throw new InvalidOperationException("КПП принимает только статичные mesh.");
            foreach (var lod in go.GetComponentsInChildren<LODGroup>(true))
                if (lod.GetLODs().Length == 0 || lod.GetLODs().Any(l => l.renderers.Length == 0 || l.renderers.Any(r => r == null)))
                    throw new InvalidOperationException("Неполный исходный LOD: " + go.name);
            foreach (var component in go.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(component);
            foreach (var component in go.GetComponentsInChildren<Component>(true))
            {
                if (component == null) throw new InvalidOperationException("Missing script в источнике КПП.");
                if (!(component is Transform) && !(component is MeshRenderer) && !(component is MeshFilter) && !(component is LODGroup))
                    UnityEngine.Object.DestroyImmediate(component);
            }
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader"))
                    throw new InvalidOperationException("Отсутствует PBR/material shader: " + renderer.name);
                ConfigureRenderer(renderer);
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.OccludeeStatic);
        }

        private static void ConfigureRenderer(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
            && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
    }
}
