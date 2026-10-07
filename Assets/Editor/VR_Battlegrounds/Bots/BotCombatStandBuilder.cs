using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools.BotCombatStand;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Собственная диагностическая сцена; исходные карты и префабы не меняются.</summary>
    public static class BotCombatStandBuilder
    {
        public const string ScenePath = "Assets/Scenes/Debug/BotCombatStand.unity";
        private const string DataPath = "Assets/Scenes/Debug/BotCombatStandMap.asset";

        [MenuItem("Tools/VR Battlegrounds/Bots/Stand/Build Scene")]
        private static void MenuBuild() => GameLog.Debug.Info(Build());

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Сборка только вне Play.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                return "Сцена уже существует: " + ScenePath;
            if (!AssetDatabase.IsValidFolder("Assets/Scenes/Debug")) AssetDatabase.CreateFolder("Assets/Scenes", "Debug");
            MapData source = AssetDatabase.FindAssets("t:MapData").Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<MapData>).First(m => m != null && m.sceneName == "TestMap1");
            var map = UnityEngine.Object.Instantiate(source);
            map.name = "BotCombatStandMap"; map.sceneName = "BotCombatStand"; map.displayName = "Стенд ботов";
            map.supportedModes = AssetDatabase.FindAssets("t:GameModeData").Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<VrBattlegrounds.GameModes.GameModeData>).Where(m => m != null).ToArray();
            map.debugOnly = true; map.arenaSizeMeters = new Vector2(24, 20);
            map.kind = VrBattlegrounds.Maps.Runtime.MapRunKind.Debug;
            AssetDatabase.CreateAsset(map, DataPath);
            if (!AssetDatabase.CopyAsset("Assets/Scenes/Maps/TestMap1.unity", ScenePath))
                throw new InvalidOperationException("Не удалось создать копию сцены.");
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject env = scene.GetRootGameObjects().First(x => x.name == "Environment");
                if (PrefabUtility.IsPartOfPrefabInstance(env)) PrefabUtility.UnpackPrefabInstance(env, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                while (env.transform.childCount > 0) UnityEngine.Object.DestroyImmediate(env.transform.GetChild(0).gameObject);
                env.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                env.transform.localScale = Vector3.one;
                // Старая SDK-комната остаётся сервисом, её геометрия не участвует в стенде.
                GameObject services = scene.GetRootGameObjects().First(x => x.name == "Scene");
                foreach (Renderer r in services.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                foreach (Collider c in services.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                var neutral = Material("BotStandNeutral", new Color(0.38f, 0.42f, 0.48f));
                var hard = Material("BotStandHard", new Color(0.22f, 0.30f, 0.40f));
                var soft = Material("BotStandSoft", new Color(0.60f, 0.36f, 0.16f));
                Box(env.transform, "Floor", new Vector3(0, -0.15f, 0), new Vector3(24, 0.3f, 20), neutral);
                Box(env.transform, "North_Hard", new Vector3(0, 1.5f, 10), new Vector3(24, 3, 0.3f), neutral);
                Box(env.transform, "South_Hard", new Vector3(0, 1.5f, -10), new Vector3(24, 3, 0.3f), neutral);
                Box(env.transform, "West_Hard", new Vector3(-12, 1.5f, 0), new Vector3(0.3f, 3, 20), neutral);
                Box(env.transform, "East_Hard", new Vector3(12, 1.5f, 0), new Vector3(0.3f, 3, 20), neutral);
                // Копия карты несёт её MapRoot: запуск стенда — свой паспорт, тот же MapBootstrap.
                foreach (var root in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<VrBattlegrounds.Maps.Runtime.MapRoot>(true)))
                {
                    var rootSo = new SerializedObject(root);
                    rootSo.FindProperty("_map").objectReferenceValue = map;
                    rootSo.ApplyModifiedPropertiesWithoutUndo();
                }
                var standGO = new GameObject("[DevStand] BotCombatStand");
                SceneManager.MoveGameObjectToScene(standGO, scene);
                var stand = standGO.AddComponent<BotCombatStand>();
                stand.Map = map;
                ConfigureStations(scene, map);
                stand.DetourWall = Box(env.transform, "Detour_Hard", new Vector3(0, 1.05f, 0), new Vector3(6, 2.1f, 0.6f), hard);
                stand.HardCover = Box(env.transform, "Cover_Hard", new Vector3(0, 1.05f, -1.5f), new Vector3(3.5f, 2.1f, 0.6f), hard);
                stand.SoftCover = Box(env.transform, "Cover_Soft", new Vector3(0, 1.05f, -1.5f), new Vector3(3.5f, 2.1f, 0.6f), soft);
                stand.VisualCover = Box(env.transform, "Cover_Visual", new Vector3(0, 1.05f, -1.5f), new Vector3(3.5f, 2.1f, 0.6f), soft);
                stand.DetourWall.SetActive(false); stand.HardCover.SetActive(false); stand.SoftCover.SetActive(false); stand.VisualCover.SetActive(false);
                stand.Weapons = AssetDatabase.FindAssets("t:WeaponInfo").Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<WeaponInfo>).Where(w => w != null && w.WeaponPrefab != null &&
                        (w.Category == WeaponCategory.Pistol || w.Category == WeaponCategory.Rifle)).OrderBy(w => w.name).ToArray();
                var lightGO = new GameObject("StandLight"); lightGO.transform.SetParent(env.transform);
                var light = lightGO.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.4f;
                lightGO.transform.rotation = Quaternion.Euler(50, -35, 0);
                var cameraGO = new GameObject("StandOverview"); cameraGO.transform.SetParent(standGO.transform);
                var camera = cameraGO.AddComponent<Camera>(); camera.useOcclusionCulling = false;
                cameraGO.transform.position = new Vector3(14, 16, -19); cameraGO.transform.LookAt(Vector3.zero);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssetIfDirty(map);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
            // Стенд запускается MapBootstrap, как карта: каталог должен знать его паспорт и отпечаток.
            object migration = VrBattlegrounds.EditorTools.MapBootstrapMigration.ApplyDebugStands();
            return "Создан серверный стенд; " + ScenePath + "; миграция: " + Newtonsoft.Json.JsonConvert.SerializeObject(migration);
        }

        /// <summary>Исправление собственного стенда ранней версии; игровые карты не открываются.</summary>
        public static string NormalizeFixture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Нормализация только вне Play.");
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var env = scene.GetRootGameObjects().First(x => x.name == "Environment").transform;
                env.SetPositionAndRotation(Vector3.zero, Quaternion.identity); env.localScale = Vector3.one;
                SetBox(env, "Floor", new Vector3(0,-.15f,0), new Vector3(24,.3f,20));
                SetBox(env, "North_Hard", new Vector3(0,1.5f,10), new Vector3(24,3,.3f));
                SetBox(env, "South_Hard", new Vector3(0,1.5f,-10), new Vector3(24,3,.3f));
                SetBox(env, "West_Hard", new Vector3(-12,1.5f,0), new Vector3(.3f,3,20));
                SetBox(env, "East_Hard", new Vector3(12,1.5f,0), new Vector3(.3f,3,20));
                SetBox(env, "Detour_Hard", new Vector3(0,1.05f,0), new Vector3(6,2.1f,.6f));
                foreach (var name in new[] { "Cover_Hard", "Cover_Soft", "Cover_Visual" })
                    SetBox(env, name, new Vector3(0,1.05f,-1.5f), new Vector3(3.5f,2.1f,.6f));
                ConfigureStations(scene, AssetDatabase.LoadAssetAtPath<MapData>(DataPath));
                EditorSceneManager.SaveScene(scene);
                return "Размеры и ориентация геометрии стенда восстановлены.";
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static void SetBox(Transform root, string name, Vector3 position, Vector3 scale)
        {
            var child = root.Find(name);
            if (child == null) throw new InvalidOperationException("Нет геометрии " + name);
            child.SetPositionAndRotation(position, Quaternion.identity); child.localScale = scale;
        }

        private static void ConfigureStations(Scene scene, MapData map)
        {
            const string path="Assets/Scenes/Debug/BotCombatStandRegistry.asset";
            var registry=AssetDatabase.LoadAssetAtPath<MapRegistry>(path);
            if(registry==null)
            {
                registry=ScriptableObject.CreateInstance<MapRegistry>(); registry.name="BotCombatStandRegistry";
                registry.maps=new[]{map}; AssetDatabase.CreateAsset(registry,path);
            }
            foreach(var binding in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ArsenalStationPresetBinding>(true)))
                binding.ConfigureRegistry(registry);
        }

        private static Material Material(string name, Color color)
        {
            string path = "Assets/Scenes/Debug/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Нет URP Lit.");
            material = new Material(shader) { name = name, color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent, false); go.transform.position = position; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (CoverClassRules.TryExpectedClass(go, out CoverClass coverClass))
                go.AddComponent<CoverSurface>().Class = coverClass;
            return go;
        }
    }
}
