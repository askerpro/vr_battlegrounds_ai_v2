using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Оформление двух существующих казарм без изменения их оболочки и игрового состояния.</summary>
    public static class LobbyBarracksDressingBuilder
    {
        public static readonly string[] RequiredSources = { "SM_Chair", "SM_Door_lamp_a", "SM_Door_single", "SM_Building_wall_2m_A" };
        private const string ReportPath = "tasks/lobby-decoration/reports/implementation/barracks-zone/latest-build.json";
        [Serializable] public sealed class Item
        {
            public string role, source;
            public Bounds allLodBounds, lod0Bounds;
            public int lodLevels;
            public float groundContactError;
        }
        [Serializable] public sealed class Frontage
        {
            public string building, socket;
            public Bounds shell, entrance, clearApproach, stoop;
            public Bounds door, opening, leftFill, rightFill, upperJoint;
            public Vector3 socketPosition;
        }
        [Serializable] public sealed class Report
        {
            public bool layoutValidated;
            public Bounds arena, tent, reservation, actualBounds;
            public List<Item> items = new List<Item>();
            public List<Frontage> frontages = new List<Frontage>();
            public int colliders, lights, scripts, audioSources, animators, renderers;
            public string decision = "native_scale_closed_doors_with_concrete_side_fills";
        }
        public static Report LastReport { get; private set; }

        public static void Build(Transform parent, Bounds arena, Bounds tent)
        {
            LastReport = null;
            if (parent == null || !parent.gameObject.scene.IsValid()) throw new ArgumentException("Нужен родитель сцены.");
            if (parent.Find("BarracksDressing") != null) throw new InvalidOperationException("BarracksDressing уже существует.");
            if ((parent.lossyScale - Vector3.one).sqrMagnitude > .000001f || Quaternion.Angle(parent.rotation, Quaternion.identity) > .001f)
                throw new InvalidOperationException("Нужен родитель с мировыми scale=1, rotation=identity.");
            if (!Finite(arena.center) || !Finite(arena.size) || arena.size.x <= 0 || arena.size.z <= 0 || !Finite(tent.center))
                throw new InvalidOperationException("Некорректные bounds арены/шатра.");
            var buildings = parent.Find("BaseBuildings");
            if (buildings == null) throw new InvalidOperationException("Сначала требуется BaseBuildings.");
            var shells = new[] { buildings.Find("Казарма A"), buildings.Find("Казарма B") };
            if (shells.Any(t => t == null)) throw new InvalidOperationException("Обе исходные казармы обязательны.");
            var sources = RequiredSources.ToDictionary(s => s, LobbyMilitaryCampSources.Prefab);
            if (sources.Values.Any(s => s == null)) throw new InvalidOperationException("Не импортирован источник казарм.");
            var concrete = Material("Assets/Art/Lobby/Imported/MilitaryCamp/Art/Materials/M_Concrete_01.mat");
            var metal = Material("Assets/Materials/Lobby/Exterior/Truck_oliveDark.mat");
            var soil = Material("Assets/Materials/Lobby/Exterior/WornSoil.mat");
            var origin = new Vector3(arena.center.x, arena.max.y, arena.center.z);
            LastReport = new Report { arena = arena, tent = tent,
                reservation = Envelope(origin, -31, -16, -24, -8, 4) };
            var root = Group("BarracksDressing", parent);
            root.position = Vector3.zero;
            try
            {
                // Один короткий упорядоченный подход к двум входам. Поверхность не меняет физический пол.
                var path = GameObject.CreatePrimitive(PrimitiveType.Quad);
                path.name = "SharedApproach__WornSoil";
                path.transform.SetParent(root, false);
                path.transform.SetPositionAndRotation(origin + new Vector3(-23.25f, .006f, -23.1f), Quaternion.Euler(90, 0, 0));
                path.transform.localScale = new Vector3(11.5f, 1.7f, 1);
                path.GetComponent<MeshRenderer>().sharedMaterial = soil;
                Sanitize(path); Record(path, "read_only_WornSoil_approach");
                for (int i = 0; i < shells.Length; i++)
                    Dress(root, shells[i], i == 0 ? "A" : "B", sources, concrete, metal, origin);
                Validate(root);
                LastReport.layoutValidated = true;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                throw;
            }
            finally
            {
                var reportFile = Path.Combine(Application.dataPath, "../" + ReportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(reportFile));
                File.WriteAllText(reportFile, JsonUtility.ToJson(LastReport, true));
            }
        }

        private static void Dress(Transform root, Transform shell, string letter, Dictionary<string, GameObject> sources,
            Material concrete, Material metal, Vector3 origin)
        {
            var entry = shell.Find("SM_Building_wall_entrance_2m_A");
            if (entry == null) throw new InvalidOperationException("Не найден настоящий вход: " + shell.name);
            var sb = Measure(shell); var eb = Measure(entry);
            float expectedX = origin.x + (letter == "A" ? -29 : -22);
            if (Math.Abs(sb.min.x - expectedX) > .03f || Math.Abs(sb.min.z - (origin.z - 22)) > .03f ||
                Math.Abs(eb.size.x - 3) > .015f || Math.Abs(eb.size.y - 2.50023f) > .015f ||
                Math.Abs(eb.min.z - sb.min.z) > .03f || Math.Abs(eb.min.y - origin.y) > .015f ||
                Quaternion.Angle(entry.rotation, Quaternion.identity) > .001f)
                throw new InvalidOperationException("Изменилась согласованная южная геометрия входа: " + shell.name);
            var group = Group("Barracks" + letter, root);
            var facade = CloseEntrance(group, entry, eb, sources, letter);
            var corridor = new Bounds(new Vector3(eb.center.x, origin.y + 1.25f, eb.min.z - .9f), new Vector3(1.8f, 2.5f, 2.1f));
            var step = Block(group, "EntryStoop", new Vector3(eb.center.x, origin.y + .04f, eb.min.z - .302f),
                new Vector3(2.8f, .08f, .6f), Quaternion.identity, concrete);
            var chair = Place(sources["SM_Chair"], group, "PersonalChair__" + letter, 0);
            var cb = Measure(chair.transform, true);
            chair.transform.position += new Vector3(eb.min.x + 3.52f - cb.center.x, origin.y - cb.min.y, eb.min.z - .72f - cb.center.z);
            Record(chair, AssetDatabase.GetAssetPath(sources["SM_Chair"]), origin.y);
            if (IntersectsXZ(Measure(chair.transform), corridor) || IntersectsXZ(Measure(chair.transform), sb))
                throw new InvalidOperationException("Стул мешает входу или стене: " + letter);

            // Таблички над проёмом и оконной полосой; восточная читается из шатра.
            Plaque(group, "EntryPlaque", "КАЗАРМА " + letter,
                new Vector3(eb.center.x, origin.y + 2.69f, eb.min.z - .025f), 0, metal);
            Plaque(group, "ArenaFacingPlaque", "КАЗАРМА " + letter,
                new Vector3(sb.max.x + .025f, origin.y + 2.47f, sb.min.z + 1.35f), -90, metal);
            var fixture = Place(sources["SM_Door_lamp_a"], group, "EntryFixture__" + letter, 0);
            var fb = Measure(fixture.transform);
            fixture.transform.position += new Vector3(eb.center.x - fb.center.x, origin.y + 2.94f - fb.center.y,
                eb.min.z - .006f - fb.max.z);
            Record(fixture, AssetDatabase.GetAssetPath(sources["SM_Door_lamp_a"]));
            fb = Measure(fixture.transform);
            if (fb.max.y > sb.max.y || fb.max.z > eb.min.z || fb.min.y < origin.y + 2.85f)
                throw new InvalidOperationException("Светильник не помещается над входом: " + letter);
            var socket = Group("LightingSocket__Barracks" + letter + "__CommonLightingOwner", group);
            socket.position = new Vector3(fb.center.x, fb.min.y - .045f, fb.min.z - .07f);
            LastReport.frontages.Add(new Frontage { building = shell.name, shell = sb, entrance = eb, clearApproach = corridor,
                opening = facade.opening, door = facade.door, leftFill = facade.leftFill,
                rightFill = facade.rightFill, upperJoint = facade.upperJoint,
                stoop = Measure(step.transform), socket = "BarracksDressing/Barracks" + letter + "/" + socket.name,
                socketPosition = socket.position });
        }

        private static Frontage CloseEntrance(Transform group, Transform entry, Bounds eb,
            Dictionary<string, GameObject> sources, string letter)
        {
            // Свет проёма измерен по LOD0 FBX, а не по внешнему металлическому обрамлению.
            float x0 = eb.min.x + .438261f, x1 = eb.min.x + 2.561739f;
            float y0 = eb.min.y + .013306f, y1 = eb.min.y + 2.144446f;
            var door = Place(sources["SM_Door_single"], group, "ClosedDoor__" + letter, 180);
            var native = Measure(door.transform);
            if (Math.Abs(native.size.x - 1.101196f) > .002f || Math.Abs(native.size.y - 2.131141f) > .002f ||
                Math.Abs(native.size.z - .193237f) > .002f)
                throw new InvalidOperationException("Изменились исходные размеры двери: " + letter);
            // Полотно X[0,019868;1,081608]; петли выступают за него. При yaw180 фурнитура смотрит на юг.
            const float leafMin = .019868f, leafMax = 1.081608f, leafBack = .020605f;
            door.transform.position = new Vector3((x0 + x1) * .5f + (leafMin + leafMax) * .5f,
                y0 - native.min.y, eb.min.z + .102f + leafBack);
            var db = Measure(door.transform);
            float leafLeft = door.transform.position.x - leafMax, leafRight = door.transform.position.x - leafMin;
            // Весь envelope включает задние части петель: бетон отнесён за них, а не только за полотно.
            const float minimumRearClearance = .004f;
            float fillZ = db.max.z + .005f;
            var left = FacadeFill(sources["SM_Building_wall_2m_A"], group, "EntryLeftFill__" + letter,
                new Vector3(x0 - .003f, y0 - .003f, fillZ), leafLeft - x0 + .005f, y1 - y0 + .006f);
            var right = FacadeFill(sources["SM_Building_wall_2m_A"], group, "EntryRightFill__" + letter,
                new Vector3(leafRight - .002f, y0 - .003f, fillZ), x1 - leafRight + .005f, y1 - y0 + .006f);
            var upper = FacadeFill(sources["SM_Building_wall_2m_A"], group, "EntryHiddenUpperJoint__" + letter,
                new Vector3(x0 - .003f, y1 + .004f, fillZ), x1 - x0 + .006f, .12f);
            Record(door, AssetDatabase.GetAssetPath(sources["SM_Door_single"]), y0);
            var result = new Frontage { door = db, opening = new Bounds(new Vector3((x0 + x1) * .5f,
                (y0 + y1) * .5f, eb.center.z), new Vector3(x1 - x0, y1 - y0, eb.size.z)),
                leftFill = Measure(left.transform), rightFill = Measure(right.transform), upperJoint = Measure(upper.transform) };
            if (Math.Abs(db.min.y - y0) > .002f || Math.Abs(db.max.y - y1) > .002f ||
                door.transform.localScale != Vector3.one || Quaternion.Angle(door.transform.rotation, Quaternion.Euler(0, 180, 0)) > .001f ||
                result.leftFill.max.x < leafLeft || result.rightFill.min.x > leafRight ||
                result.leftFill.min.x > x0 || result.rightFill.max.x < x1 ||
                result.upperJoint.max.y > eb.max.y || result.leftFill.min.z <= eb.min.z ||
                result.rightFill.min.z <= eb.min.z ||
                result.leftFill.min.z <= db.max.z + minimumRearClearance ||
                result.rightFill.min.z <= db.max.z + minimumRearClearance ||
                result.upperJoint.min.z <= db.max.z + minimumRearClearance ||
                Quaternion.Angle(entry.rotation, Quaternion.identity) > .001f)
                throw new InvalidOperationException("Нарушена геометрия закрытого фасада: " + letter);
            return result;
        }

        private static GameObject FacadeFill(GameObject source, Transform parent, string name, Vector3 min, float width, float height)
        {
            if (width <= 0 || height <= 0) throw new InvalidOperationException("Неверный размер заполнения фасада.");
            var go = Place(source, parent, name, 0);
            var native = Measure(go.transform);
            if (Math.Abs(native.size.x - 2.000061f) > .002f || Math.Abs(native.size.y - 2.000846f) > .002f ||
                Math.Abs(native.size.z - .200073f) > .002f)
                throw new InvalidOperationException("Изменился исходный бетонный модуль.");
            go.transform.localScale = new Vector3(width / native.size.x, height / native.size.y, 1);
            go.transform.position = min - Vector3.Scale(native.min, go.transform.localScale);
            foreach (var lod in go.GetComponentsInChildren<LODGroup>(true)) lod.RecalculateBounds();
            Record(go, AssetDatabase.GetAssetPath(source));
            return go;
        }

        private static void Plaque(Transform parent, string name, string text, Vector3 center, float yaw, Material metal)
        {
            var rotation = Quaternion.Euler(0, yaw, 0);
            Block(parent, name + "__Plate", center, new Vector3(1.55f, .29f, .035f), rotation, metal);
            var go = new GameObject(name + "__Text"); go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center + rotation * Vector3.back * .0205f, rotation);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) throw new InvalidOperationException("Не найден шрифт вывески.");
            var label = go.AddComponent<TextMesh>();
            label.text = text; label.font = font; label.fontSize = 64; label.characterSize = .12f;
            label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
            label.color = new Color(.9f, .88f, .77f);
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = font.material;
            var bounds = renderer.bounds;
            float width = yaw == 0 ? bounds.size.x : bounds.size.z;
            if (width <= .0001f || bounds.size.y <= .0001f) throw new InvalidOperationException("Пустой TextMesh bounds.");
            go.transform.localScale = Vector3.one * Mathf.Min(1.43f / width, .205f / bounds.size.y);
            Configure(renderer); GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
            Record(go, "TextMesh_bounds_fitted_to_plate");
            var actual = renderer.bounds;
            if ((yaw == 0 ? actual.size.x : actual.size.z) > 1.431f || actual.size.y > .206f)
                throw new InvalidOperationException("Текст вышел за табличку.");
        }

        private static GameObject Block(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rotation, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, rotation); go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material; Sanitize(go); Record(go, "small_architectural_support"); return go;
        }
        private static GameObject Place(GameObject source, Transform parent, string name, float yaw)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name; go.transform.localScale = Vector3.one;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, yaw, 0));
            Sanitize(go); return go;
        }
        private static Material Material(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                throw new InvalidOperationException("Неверный исходный материал: " + path);
            return material;
        }
        private static Transform Group(string name, Transform parent)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
        }
        private static void Record(GameObject go, string source, float? ground = null)
        {
            var lod0 = Measure(go.transform, true);
            LastReport.items.Add(new Item { role = go.name, source = source, allLodBounds = Measure(go.transform), lod0Bounds = lod0,
                lodLevels = go.GetComponentsInChildren<LODGroup>(true).Sum(l => l.GetLODs().Length),
                groundContactError = ground.HasValue ? lod0.min.y - ground.Value : 0 });
        }
        private static Bounds Measure(Transform root, bool lod0 = false)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var groups = root.GetComponentsInChildren<LODGroup>(true);
            var covered = new HashSet<Renderer>(groups.SelectMany(g => g.GetLODs()).SelectMany(l => l.renderers));
            var selected = lod0 ? groups.SelectMany(g => g.GetLODs().Take(1)).SelectMany(l => l.renderers)
                .Concat(renderers.Where(r => !covered.Contains(r))).Distinct() : renderers;
            bool first = true; Bounds result = default;
            foreach (var r in selected)
            {
                if (r == null) throw new InvalidOperationException("Missing renderer.");
                if (r.GetComponent<TextMesh>() != null)
                {
                    if (first) { result = r.bounds; first = false; } else result.Encapsulate(r.bounds);
                    continue;
                }
                if (!r.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) throw new InvalidOperationException("Нет статичного mesh.");
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var point = filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!Finite(point)) throw new InvalidOperationException("Неконечные bounds.");
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; } else result.Encapsulate(point);
                }
            }
            if (first) throw new InvalidOperationException("Нет геометрии: " + root.name);
            return result;
        }
        private static void Validate(Transform root)
        {
            foreach (var item in LastReport.items)
            {
                var a = item.allLodBounds; var b = LastReport.reservation;
                if (a.min.x < b.min.x - .002f || a.max.x > b.max.x + .002f || a.min.z < b.min.z - .002f ||
                    a.max.z > b.max.z + .002f || a.min.y < b.min.y - .01f || a.max.y > b.max.y)
                    throw new InvalidOperationException("Оформление вышло из резерва казарм: " + item.role);
                if (IntersectsXZ(a, LastReport.arena) || IntersectsXZ(a, LastReport.tent))
                    throw new InvalidOperationException("Оформление пересекает арену/шатёр: " + item.role);
                if (Math.Abs(item.groundContactError) > .002f) throw new InvalidOperationException("Стул не опирается на грунт.");
            }
            LastReport.actualBounds = Measure(root);
            LastReport.renderers = root.GetComponentsInChildren<Renderer>(true).Length;
            LastReport.colliders = root.GetComponentsInChildren<Collider>(true).Length;
            LastReport.lights = root.GetComponentsInChildren<Light>(true).Length;
            LastReport.scripts = root.GetComponentsInChildren<MonoBehaviour>(true).Length;
            LastReport.audioSources = root.GetComponentsInChildren<AudioSource>(true).Length;
            LastReport.animators = root.GetComponentsInChildren<Animator>(true).Length;
            if (LastReport.colliders + LastReport.lights + LastReport.scripts + LastReport.audioSources + LastReport.animators != 0)
                throw new InvalidOperationException("Казармы содержат запрещённые компоненты.");
        }
        private static void Sanitize(GameObject go)
        {
            if (go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0) throw new InvalidOperationException("Допустимы статичные mesh.");
            // Сначала зависимые URP/Mono-компоненты: иначе RequireComponent удерживает исходный Light.
            foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null) throw new InvalidOperationException("Missing script в источнике.");
                if (!(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer) && !(c is LODGroup)) UnityEngine.Object.DestroyImmediate(c);
            }
            foreach (var lod in go.GetComponentsInChildren<LODGroup>(true))
            {
                if (lod.GetLODs().Length == 0 || lod.GetLODs().Any(l => l.renderers.Length == 0 || l.renderers.Any(r => r == null)))
                    throw new InvalidOperationException("Неполная исходная LOD цепочка.");
                lod.RecalculateBounds();
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) Configure(r);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.OccludeeStatic);
        }
        private static void Configure(Renderer r)
        {
            if (r.sharedMaterials.Length == 0 || r.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader"))
                throw new InvalidOperationException("Отсутствует исходный материал/шейдер.");
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }
        private static Bounds Envelope(Vector3 o, float x0, float x1, float z0, float z1, float height)
            => new Bounds(o + new Vector3((x0 + x1) / 2, height / 2, (z0 + z1) / 2), new Vector3(x1 - x0, height, z1 - z0));
        private static bool IntersectsXZ(Bounds a, Bounds b)
            => a.min.x < b.max.x && a.max.x > b.min.x && a.min.z < b.max.z && a.max.z > b.min.z;
        private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
            && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }
}
