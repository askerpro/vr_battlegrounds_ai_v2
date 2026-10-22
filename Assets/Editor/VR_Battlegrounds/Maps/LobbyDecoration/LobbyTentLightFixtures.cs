using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    /// Светильники шатра Lobby для ровного базового света и акцентов (задача lobby-reflections, решение пользователя
    /// 2026-10-22: «по всему шатру маленькие тусклые источники равномерно, над точками акцента — большая яркая лампа»;
    /// фон — плоские светящиеся круги под кровлей, акценты — длинные плафоны у арсенала и над точками стрельбы у торцов).
    /// Владеет группой <c>ArmoryRangeZone/OpenTent/TentLightFixtures</c> и пересобирает её целиком при каждом запуске.
    /// Ставит только геометрию и сокеты «LightingSocket__&lt;светильник&gt;__CommonLightingOwner»: источники света, их
    /// яркость, пробы и GI задаёт ночной пресет (<see cref="LobbyNightLighting"/>).
    /// <para>
    /// Подвесные плафоны SM_TopLight (RuralTown, не военная тематика) раньше ставил <see cref="LobbyCanopyBaysBuilder"/>
    /// в WestWallDetail/Pendants; сборщик их больше не создаёт, а этот инструмент убирает оставшиеся в сцене (миграция)
    /// и ставит на их место лампы сетки.
    /// </para>
    /// </summary>
    public static class LobbyTentLightFixtures
    {
        private const string MenuPath = "Tools/VR Battlegrounds/Lobby Decoration/Светильники шатра (сетка и акценты)";
        private const string ScenePath = "Assets/Scenes/Lobby.unity";
        public const string RootName = "TentLightFixtures";
        public const string FillPrefix = "TentFill_";
        public const string AccentPrefix = "TentAccent_";
        private const string LegacyPendantsPath = "OpenTent/WestWallDetail/Pendants";
        private const string LegacyBayLampsPath = "EastBays/Lamps";

        private const string SteelPath = "Assets/Materials/Lobby/Exterior/CampSteel.mat";
        // Свой материал кругов: непрозрачное Unlit тёплого белого (LobbyNightWindowGlow и тусклый тёплый (.50, .35, .19) в шлеме
        // читались коричневыми, пользователь 2026-10-22). Круг светится, в свет не идёт — свет у источника на сокете. Цвет — sRGB.
        private const string DiscGlowPath = "Assets/Materials/Lobby/Night/LobbyTentDiscGlow.mat";
        private static readonly Color DiscGlow = new Color(.95f, .86f, .70f);

        // Кровля TentRoof_LOD0 (как в LobbyCanopyBaysBuilder): карниз y 4.19 при |x| = 7.75, конёк y 5.59 при x = 0.
        private const float RoofEaveY = 4.19f, RoofRidgeY = 5.59f, RoofHalfWidth = 7.75f;
        // Круг фонового света: плоский светящийся диск под кровлей (решение пользователя 2026-10-22).
        private const float DiscDiameter = .32f, DiscThickness = .012f, DiscBelowRoof = .06f;

        // Сетка базового света с шагом ~3.7 м: весь фон шатра — круги; плафоны SM_Lamp_long — только над акцентами.
        // Модель освещённости — tasks/lobby-reflections/tools/tent_light_model.py.
        private static readonly Vector2[] FillPoints =
        {
            new Vector2(-1.9f, -5.6f), new Vector2(-1.9f, -1.9f), new Vector2(-1.9f, 1.9f), new Vector2(-1.9f, 5.6f),
            new Vector2(1.9f, -5.6f), new Vector2(1.9f, -1.9f), new Vector2(1.9f, 1.9f), new Vector2(1.9f, 5.6f),
            new Vector2(-5.8f, -1.6f), new Vector2(-5.8f, 1.6f), new Vector2(5.8f, -1.6f), new Vector2(5.8f, 1.6f),
            // Бывшие подвесные плафоны у западной стены (Pendant_NW/SW).
            new Vector2(-7.0f, 6.3f), new Vector2(-7.05f, -5.95f),
            // Бывшие плафоны CampLighting (±5.8; ±4.8) и BayLamp (7.3; ±3.0): плафоны — только над акцентами.
            new Vector2(-5.8f, -4.8f), new Vector2(-5.8f, 4.8f), new Vector2(5.8f, -4.8f), new Vector2(5.8f, 4.8f),
            new Vector2(7.3f, -3.0f), new Vector2(7.3f, 3.0f),
        };

        // Акценты — длинные плафоны SM_Lamp_long: над стеной арсенала CT вдоль стены перед её корпусом (корпус до X −6.09,
        // высота 4.05) и поперёк шатра у северного и южного торцов над точками стрельбы (кровля до |Z| 7.48).
        private const float AccentArsenalX = -5.75f, AccentArsenalTopY = 3.45f;
        private const float AccentRangeZ = 6.9f, AccentRangeTopY = 3.9f;

        [MenuItem(MenuPath)]
        public static void Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Откройте только " + ScenePath + " вне Play.");
            if (scene.isDirty)
                throw new InvalidOperationException("Сначала сохраните свои изменения Lobby; инструмент не перезаписывает dirty сцену.");
            var environment = scene.GetRootGameObjects().SingleOrDefault(r => r.name == "Environment")
                ?? throw new InvalidOperationException("В Lobby нет корня Environment.");
            var zone = environment.transform.Find("ArmoryRangeZone") ?? throw new InvalidOperationException("Нет Environment/ArmoryRangeZone.");
            var tent = zone.Find("OpenTent") ?? throw new InvalidOperationException("Нет ArmoryRangeZone/OpenTent.");
            var arsenal = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
                .FirstOrDefault(c => c != null && c.GetType().Name == "ArsenalStationAnchor")
                ?? throw new InvalidOperationException("Нет ArsenalStationAnchor — акцент арсенала не к чему привязать.");

            var steel = Load(SteelPath);
            var bulb = DiscMaterial();
            var lampLong = LobbyMilitaryCampSources.Prefab("SM_Lamp_long") ?? throw new InvalidOperationException("Нет MilitaryCamp SM_Lamp_long.");
            var log = new StringBuilder();

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Светильники шатра Lobby");
            try
            {
                var legacy = zone.Find(LegacyPendantsPath);
                if (legacy != null)
                {
                    log.AppendLine("Убраны плафоны SM_TopLight: " + string.Join(", ", legacy.Cast<Transform>().Select(t => t.name)));
                    Undo.DestroyObjectImmediate(legacy.gameObject);
                }
                // Миграция 2026-10-22: неакцентные плафоны шатра со своими лампами (CampLighting сборщика LobbyDecorationAuthoring,
                // лежат прямо в ArmoryRangeZone; BayLamp сборщика LobbyCanopyBaysBuilder) — сборщики их больше не создают.
                var legacyLamps = zone.Cast<Transform>().Where(t => t.name == "SM_Lamp_long" || t.name == "InteriorWarmLight").ToList();
                var bayLamps = tent.Find(LegacyBayLampsPath);
                if (bayLamps != null) legacyLamps.Add(bayLamps);
                if (legacyLamps.Count > 0)
                {
                    log.AppendLine("Убраны неакцентные плафоны и их лампы: " + legacyLamps.Count + ".");
                    foreach (Transform t in legacyLamps) Undo.DestroyObjectImmediate(t.gameObject);
                }
                var old = tent.Find(RootName);
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
                var root = new GameObject(RootName).transform;
                Undo.RegisterCreatedObjectUndo(root.gameObject, "Светильники шатра");
                root.SetParent(tent, false);

                for (int i = 0; i < FillPoints.Length; i++)
                    FillDisc(root, FillPrefix + (i + 1), FillPoints[i], bulb);
                Vector3 anchor = arsenal.transform.position;
                AccentLamp(root, AccentPrefix + "ArsenalCT", lampLong, new Vector3(AccentArsenalX, AccentArsenalTopY, anchor.z), 0f, steel);
                AccentLamp(root, AccentPrefix + "RangeNorth", lampLong, new Vector3(0f, AccentRangeTopY, AccentRangeZ), 90f, steel);
                AccentLamp(root, AccentPrefix + "RangeSouth", lampLong, new Vector3(0f, AccentRangeTopY, -AccentRangeZ), 90f, steel);
                log.AppendLine("Круги фонового света: " + FillPoints.Length + "; акценты: стена арсенала CT (z " + anchor.z.ToString("F2") +
                    "), точки стрельбы север/юг.");
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            GameLog.Debug.Info("[LobbyTentLightFixtures] " + log.ToString().Trim().Replace('\n', ' ') +
                " Дальше: «Ночь: подготовить запекание», Generate Lighting, сохранить Lobby.");
        }

        // Круг фонового света: плоский тёплый диск (цилиндр примитива, ≈ 80 треугольников) горизонтально под кровлей.
        private static void FillDisc(Transform root, string name, Vector2 xz, Material glow)
        {
            var fixture = new GameObject(name).transform;
            fixture.SetParent(root, false);
            float y = RoofY(xz.x) - DiscBelowRoof;
            Piece(fixture, "Disc", PrimitiveType.Cylinder, new Vector3(xz.x, y, xz.y), new Vector3(DiscDiameter, DiscThickness, DiscDiameter), glow);
            Socket(fixture, name, new Vector3(xz.x, y - .12f, xz.y));
        }

        // Длинный плафон акцента: SM_Lamp_long (длина вдоль Z, yaw 90 — вдоль X) на двух подвесах до кровли, сокет под ним.
        private static void AccentLamp(Transform root, string name, GameObject source, Vector3 top, float yaw, Material steel)
        {
            var fixture = new GameObject(name).transform;
            fixture.SetParent(root, false);
            var lamp = (GameObject)PrefabUtility.InstantiatePrefab(source, fixture);
            lamp.name = "Fixture__" + source.name;
            lamp.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Strip(lamp);
            Bounds b = Measure(lamp.transform);
            float length = Mathf.Max(b.size.x, b.size.z), width = Mathf.Min(b.size.x, b.size.z);
            if (length < 2f || width > .4f || b.size.y > .35f) throw new InvalidOperationException("SM_Lamp_long неожиданного габарита: " + b.size);
            lamp.transform.position += new Vector3(top.x - b.center.x, top.y - b.max.y, top.z - b.center.z);
            b = Measure(lamp.transform);
            Vector3 along = b.size.z >= b.size.x ? Vector3.forward : Vector3.right;
            foreach (float s in new[] { -.7f, .7f })
            {
                Vector3 p = b.center + along * (s * length / 2);
                float roof = RoofY(p.x);
                Piece(fixture, "HangerRod", PrimitiveType.Cube, new Vector3(p.x, (b.max.y + roof) / 2, p.z),
                    new Vector3(.012f, roof - b.max.y, .012f), steel);
            }
            Socket(fixture, name, new Vector3(b.center.x, b.min.y - .05f, b.center.z));
        }

        private static void Piece(Transform parent, string name, PrimitiveType type, Vector3 center, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            // Примитивы Unity: куб 1×1×1, цилиндр — диаметр 1, высота 2.
            go.transform.localScale = type == PrimitiveType.Cylinder ? new Vector3(size.x, size.y / 2, size.z) : size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            Strip(go);
        }

        private static void Socket(Transform fixture, string name, Vector3 position)
        {
            var socket = new GameObject("LightingSocket__" + name + "__CommonLightingOwner").transform;
            socket.SetParent(fixture, false);
            socket.position = position;
        }

        // Как Sanitize сборщиков декора: только меш и рендерер, без теней; GI, пробы и отражения задаёт ночной пресет.
        private static void Strip(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<Component>(true))
                if (!(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer) && !(c is LODGroup)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, GameObjectUtility.GetStaticEditorFlags(t.gameObject)
                    | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic);
        }

        private static float RoofY(float x) => RoofRidgeY - (RoofRidgeY - RoofEaveY) * Mathf.Clamp01(Mathf.Abs(x) / RoofHalfWidth);

        private static Bounds Measure(Transform subtree)
        {
            var renderers = subtree.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Нет геометрии: " + subtree.name);
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // Материал кругов принадлежит этому инструменту: создаётся при отсутствии и получает цвет при каждом запуске.
        private static Material DiscMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(DiscGlowPath);
            if (material == null)
            {
                var unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? throw new InvalidOperationException("Нет шейдера URP Unlit.");
                material = new Material(unlit) { name = "LobbyTentDiscGlow" };
                AssetDatabase.CreateAsset(material, DiscGlowPath);
            }
            material.SetColor("_BaseColor", DiscGlow);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static Material Load(string path) =>
            AssetDatabase.LoadAssetAtPath<Material>(path) ?? throw new InvalidOperationException("Нет материала " + path + ".");
    }
}
