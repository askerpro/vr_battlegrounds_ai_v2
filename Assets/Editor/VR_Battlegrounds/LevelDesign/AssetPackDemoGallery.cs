using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.DebugTools;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Копии авторских демо в стенде; исходные сцены никогда не сохраняются.</summary>
    public static class AssetPackDemoGallery
    {
        public const string RootName = "Демо-сцены паков";
        private static readonly string[] Roots = {
            "Assets/RPG_FPS_game_assets_industrial", "Assets/HIVEMIND"
        };

        [Serializable] public class Entry
        {
            public string source, group;
            public Vector3 originalCenter, size, offset;
            public int objects, renderers, missingScripts, unsupportedMaterials;
        }
        [Serializable] public class Report { public Entry[] demos; }

        public static string[] Sources() => AssetDatabase.FindAssets("t:Scene", Roots)
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();

        private static Scene Review()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Галерею изменяют только вне Play Mode.");
            var scene = SceneManager.GetSceneByPath(AssetCandidateReviewScene.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Сначала открыть IndustrialCandidateReview.");
            return scene;
        }

        private static GameObject Gallery(Scene scene)
        {
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (root != null) return root;
            root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        /// <summary>Добавляет одну сцену; позволяет выполнять большой импорт по шагам.</summary>
        public static Entry Append(string path)
        {
            if (!Sources().Contains(path)) throw new ArgumentException("Сцена вне импортированных паков.");
            var target = Review();
            var gallery = Gallery(target);
            string name = path.Substring("Assets/".Length).Replace(".unity", "");
            if (gallery.transform.Cast<Transform>().Any(t => t.name == name))
                throw new InvalidOperationException("Демо уже добавлено: " + path);
            if (SceneManager.GetSceneByPath(path).isLoaded)
                throw new InvalidOperationException("Исходная сцена уже открыта: " + path);
            var source = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var roots = source.GetRootGameObjects();
                var renderers = roots.SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).ToArray();
                var terrains = roots.SelectMany(g => g.GetComponentsInChildren<Terrain>(true)).ToArray();
                var bounds = new Bounds();
                bool hasBounds = false;
                foreach (var renderer in renderers)
                {
                    if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) || !renderer.gameObject.activeInHierarchy) continue;
                    if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                foreach (var terrain in terrains)
                {
                    if (terrain.terrainData == null) continue;
                    var b = new Bounds(terrain.transform.position + terrain.terrainData.size * 0.5f, terrain.terrainData.size);
                    if (!hasBounds) { bounds = b; hasBounds = true; } else bounds.Encapsulate(b);
                }
                float cursor = 300;
                foreach (Transform child in gallery.transform)
                foreach (var renderer in child.GetComponentsInChildren<Renderer>(true))
                    cursor = Mathf.Max(cursor, renderer.bounds.max.x + 40);
                var zone = new GameObject(name);
                SceneManager.MoveGameObjectToScene(zone, target);
                zone.transform.SetParent(gallery.transform, false);
                var entry = new Entry { source = path, group = name, size = bounds.size,
                    originalCenter = bounds.center, renderers = renderers.Length };
                foreach (var root in roots)
                {
                    SceneManager.MoveGameObjectToScene(root, target);
                    root.transform.SetParent(zone.transform, true);
                }
                entry.offset = new Vector3(cursor - bounds.min.x, -bounds.min.y, -bounds.center.z);
                zone.transform.position = entry.offset;
                foreach (var t in zone.GetComponentsInChildren<Transform>(true))
                {
                    entry.objects++;
                    entry.missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                }
                // Просмотр в Scene View: авторские контроллеры, камеры и звук не запускаются.
                foreach (var camera in zone.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                foreach (var listener in zone.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
                foreach (var audio in zone.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
                foreach (var script in zone.GetComponentsInChildren<MonoBehaviour>(true))
                    if (script != null && !(script.GetType().Namespace ?? "").StartsWith("UnityEngine.Rendering")) script.enabled = false;
                foreach (var behaviour in zone.GetComponentsInChildren<Behaviour>(true))
                    if (behaviour != null && behaviour.GetType().Name == "Volume") behaviour.enabled = false;
                foreach (var light in zone.GetComponentsInChildren<Light>(true))
                    if (light.type == LightType.Directional) light.enabled = false;
                foreach (var collider in zone.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                entry.unsupportedMaterials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null)
                    .Distinct().Count(m => m.shader == null || !m.shader.isSupported || m.shader.name.StartsWith("HDRP/"));
                var marker = new GameObject("Источник демо");
                marker.transform.SetParent(zone.transform, false);
                marker.transform.position = new Vector3(cursor, 1, -bounds.size.z * 0.5f - 4);
                marker.hideFlags = HideFlags.DontSaveInBuild;
                var label = marker.AddComponent<AssetCandidateReviewLabel>();
                label.Caption = path + "\nОригинальная композиция / " + entry.objects + " объектов";
                label.Color = Color.cyan;
                zone.SetActive(false);
                EditorSceneManager.MarkSceneDirty(target);
                return entry;
            }
            finally
            {
                EditorSceneManager.CloseScene(source, true);
                SceneManager.SetActiveScene(target);
            }
        }

        public static void Finish(Entry[] entries)
        {
            var scene = Review();
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Галерея не сохранена.");
            Directory.CreateDirectory("Temp/LevelDesign/CandidateReview");
            File.WriteAllText("Temp/LevelDesign/CandidateReview/demo-gallery.json", JsonUtility.ToJson(new Report { demos = entries }, true));
            Show("RPG_FPS_game_assets_industrial/Map_v1");
            EditorSceneManager.SaveScene(scene);
        }

        public static void Show(string group)
        {
            var gallery = Gallery(Review());
            var chosen = gallery.transform.Cast<Transform>().First(t => t.name == group);
            foreach (Transform child in gallery.transform) child.gameObject.SetActive(child == chosen);
            Selection.activeGameObject = chosen.gameObject;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            GameLog.Debug.Info("Демо пака: Assets/" + group + ".unity");
        }

        [MenuItem("Tools/VR Battlegrounds/Level Design/Demos/Show Selected Demo")]
        public static void ShowSelected()
        {
            var selected = Selection.activeTransform;
            while (selected != null && selected.parent != null && selected.parent.name != RootName) selected = selected.parent;
            if (selected == null || selected.parent == null) throw new InvalidOperationException("Выберите группу внутри «Демо-сцены паков».");
            Show(selected.name);
        }

        [MenuItem("Tools/VR Battlegrounds/Level Design/Demos/Next Demo")]
        public static void Next()
        {
            var children = Gallery(Review()).transform.Cast<Transform>().ToArray();
            if (children.Length == 0) return;
            int current = Array.FindIndex(children, t => t.gameObject.activeSelf);
            Show(children[(current + 1) % children.Length].name);
        }
    }
}
