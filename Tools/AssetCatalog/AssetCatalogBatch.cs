// Изолированное измерение геометрии и превью. В игровой Assets этот файл не устанавливается.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class AssetCatalogBatch
{
    [Serializable] public class Entry
    {
        public string path, name, preview, error, skipped;
        public Vector3 size, center;
        public int triangles, renderers, materialSlots, materials, colliders, lodGroups, missingScripts;
        public string[] shaders;
        public float[] coverage;
        public string[] masks;
    }

    [Serializable] public class Catalog
    {
        public string unityVersion;
        public int maskResolution = 16;
        public string[] directions = { "front", "side", "top" };
        public List<Entry> models = new List<Entry>();
        public List<Entry> blocks = new List<Entry>();
    }

    private static string Output;
    private static Scene PreviewScene;
    private static Camera Camera;

    [Serializable] public class Recipe
    {
        public string id, path;
        public int rotation_y;
        public int[] repeat_xyz;
        public float uniform_scale;
    }
    [Serializable] public class Group { public string block; public Recipe[] candidates; }
    [Serializable] public class Recipes { public Group[] groups; }

    private static void Initialize()
    {
        Output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../IndustrialAnalysis"));
        Directory.CreateDirectory(Output);
        Directory.CreateDirectory(Path.Combine(Output, "previews"));
        PreviewScene = EditorSceneManager.NewPreviewScene();
        var cameraObject = new GameObject("CatalogCamera");
        SceneManager.MoveGameObjectToScene(cameraObject, PreviewScene);
        Camera = cameraObject.AddComponent<Camera>();
        Camera.scene = PreviewScene;
        Camera.backgroundColor = new Color(0.12f, 0.14f, 0.17f);
        Camera.clearFlags = CameraClearFlags.SolidColor;
        Camera.orthographic = true;
        Camera.nearClipPlane = 0.01f;
        var lightObject = new GameObject("CatalogLight");
        SceneManager.MoveGameObjectToScene(lightObject, PreviewScene);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        lightObject.transform.rotation = Quaternion.Euler(35, -35, 0);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
    }

    public static void BuildGallery()
    {
        try
        {
            // В batchmode открыт несохранённый Untitled: добавление сцены к нему запрещено.
            // Это отдельный проект каталога, поэтому создаём его рабочую сцену единственной.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Initialize();
            var recipes = JsonUtility.FromJson<Recipes>(File.ReadAllText(Path.Combine(Output, "recipes-for-unity.json")));
            var neutral = new Material(Shader.Find("Standard")) { color = new Color(0.3f, 0.65f, 0.8f) };
            const string neutralPath = "Assets/CatalogNeutral.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(neutralPath) == null) AssetDatabase.CreateAsset(neutral, neutralPath);
            else { UnityEngine.Object.DestroyImmediate(neutral); neutral = AssetDatabase.LoadAssetAtPath<Material>(neutralPath); }
            int row = 0;
            var assembledSizes = new List<Entry>();
            foreach (var group in recipes.groups)
            {
                var rowObject = new GameObject(group.block);
                SceneManager.MoveGameObjectToScene(rowObject, scene);
                var reference = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/LevelDesign/LD_Alphabet/" + group.block + ".prefab"));
                Clean(reference);
                foreach (var renderer in reference.GetComponentsInChildren<MeshRenderer>())
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => neutral).ToArray();
                var referenceBounds = GeometryBounds(reference);
                reference.transform.position -= new Vector3(referenceBounds.center.x, referenceBounds.min.y, referenceBounds.center.z);
                reference.transform.SetParent(rowObject.transform, true);
                float x = referenceBounds.size.x * 0.5f + 3;
                Label(rowObject, group.block + " / эталон", new Vector3(0, 0.02f, -referenceBounds.size.z * 0.5f - 1));
                foreach (var recipe in group.candidates.Take(3))
                {
                    var assembly = Assemble(recipe);
                    var bounds = GeometryBounds(assembly);
                    var expectedRecipe = JsonUtility.ToJson(recipe);
                    assembledSizes.Add(new Entry { name = recipe.id, size = bounds.size, path = expectedRecipe });
                    RenderViews(bounds, Path.Combine(Output, "previews/" + recipe.id + ".png"));
                    SceneManager.MoveGameObjectToScene(assembly, scene);
                    assembly.transform.SetParent(rowObject.transform, true);
                    assembly.transform.position = new Vector3(x + bounds.size.x * 0.5f, 0, 0);
                    Label(rowObject, recipe.id + " / КАНДИДАТ", new Vector3(assembly.transform.position.x, 0.02f, -bounds.size.z * 0.5f - 1));
                    x += bounds.size.x + 3;
                }
                if (group.candidates.Length == 0) Label(rowObject, "Подходящего рецепта пока нет", new Vector3(x + 3, 0.02f, 0));
                rowObject.transform.position = new Vector3(0, 0, row++ * 15);
            }
            var galleryCamera = new GameObject("ReviewCamera");
            SceneManager.MoveGameObjectToScene(galleryCamera, scene);
            var view = galleryCamera.AddComponent<Camera>();
            view.backgroundColor = new Color(0.12f, 0.14f, 0.17f);
            view.clearFlags = CameraClearFlags.SolidColor;
            galleryCamera.transform.position = new Vector3(10, 18, -20);
            galleryCamera.transform.LookAt(new Vector3(10, 0, 10));
            view.farClipPlane = 500;
            var galleryLight = new GameObject("ReviewLight");
            SceneManager.MoveGameObjectToScene(galleryLight, scene);
            galleryLight.AddComponent<Light>().type = LightType.Directional;
            galleryLight.transform.rotation = Quaternion.Euler(40, -35, 0);
            Directory.CreateDirectory("Assets/Scenes");
            if (!EditorSceneManager.SaveScene(scene, "Assets/Scenes/CatalogReview.unity"))
                throw new IOException("Не удалось сохранить сцену каталога");
            File.WriteAllText(Path.Combine(Output, "assembled-sizes.json"), JsonUtility.ToJson(new Catalog { models = assembledSizes }, true));
            File.WriteAllText(Path.Combine(Output, "gallery-complete.txt"), assembledSizes.Count.ToString());
            EditorSceneManager.ClosePreviewScene(PreviewScene);
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(Output ?? Directory.GetCurrentDirectory(), "gallery-failure.txt"), exception.ToString());
            EditorApplication.Exit(1);
        }
    }

    private static GameObject Assemble(Recipe recipe)
    {
        var root = new GameObject(recipe.id);
        SceneManager.MoveGameObjectToScene(root, PreviewScene);
        for (int x = 0; x < recipe.repeat_xyz[0]; x++)
        for (int y = 0; y < recipe.repeat_xyz[1]; y++)
        for (int z = 0; z < recipe.repeat_xyz[2]; z++)
        {
            var part = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(recipe.path));
            Clean(part);
            SceneManager.MoveGameObjectToScene(part, PreviewScene);
            part.transform.rotation = Quaternion.Euler(0, recipe.rotation_y, 0) * part.transform.rotation;
            var bounds = GeometryBounds(part);
            part.transform.position += new Vector3(
                (x - (recipe.repeat_xyz[0] - 1) * 0.5f) * bounds.size.x - bounds.center.x,
                y * bounds.size.y - bounds.min.y,
                (z - (recipe.repeat_xyz[2] - 1) * 0.5f) * bounds.size.z - bounds.center.z);
            part.transform.SetParent(root.transform, true);
        }
        root.transform.localScale = Vector3.one * recipe.uniform_scale;
        return root;
    }

    private static void Clean(GameObject instance)
    {
        foreach (var transform in instance.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
        foreach (var group in instance.GetComponentsInChildren<LODGroup>(true))
        {
            var lods = group.GetLODs();
            foreach (var lod in lods.Skip(1)) foreach (var renderer in lod.renderers)
                if (renderer != null && !lods[0].renderers.Contains(renderer)) renderer.enabled = false;
            group.enabled = false;
        }
    }

    private static Bounds GeometryBounds(GameObject instance)
    {
        bool hasBounds = false;
        var bounds = new Bounds();
        foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (filter.sharedMesh == null || renderer == null || !renderer.enabled) continue;
            foreach (var vertex in filter.sharedMesh.vertices)
            {
                var point = filter.transform.TransformPoint(vertex);
                if (!hasBounds) { bounds = new Bounds(point, Vector3.zero); hasBounds = true; }
                else bounds.Encapsulate(point);
            }
        }
        if (!hasBounds) throw new InvalidOperationException("Нет геометрии у " + instance.name);
        return bounds;
    }

    private static void Label(GameObject parent, string text, Vector3 position)
    {
        var label = new GameObject(text);
        label.transform.SetParent(parent.transform, false);
        label.transform.localPosition = position;
        label.transform.localRotation = Quaternion.Euler(90, 0, 0);
        var mesh = label.AddComponent<TextMesh>();
        mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        mesh.GetComponent<MeshRenderer>().sharedMaterial = mesh.font.material;
        mesh.text = text;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.fontSize = 32;
        mesh.characterSize = 0.15f;
        mesh.color = Color.white;
    }

    public static void Run()
    {
        try
        {
            Initialize();
            var catalog = new Catalog { unityVersion = Application.unityVersion };
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/RPG_FPS_game_assets_industrial" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();
            for (int i = 0; i < paths.Length; i++)
            {
                catalog.models.Add(Measure(paths[i], "model_" + i.ToString("D3")));
                if (i % 10 == 0)
                    File.WriteAllText(Path.Combine(Output, "progress.txt"), (i + 1) + "/" + paths.Length);
            }
            foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/LevelDesign/LD_Alphabet" })
                         .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
                catalog.blocks.Add(Measure(path, Path.GetFileNameWithoutExtension(path)));
            File.WriteAllText(Path.Combine(Output, "measurements.json"), JsonUtility.ToJson(catalog, true));
            File.WriteAllText(Path.Combine(Output, "progress.txt"), "complete");
            EditorSceneManager.ClosePreviewScene(PreviewScene);
            if (catalog.models.Count == 0 || catalog.models.Any(e => e.error != null) || catalog.blocks.Any(e => e.error != null))
                EditorApplication.Exit(1);
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(Output, "failure.txt"), exception.ToString());
            EditorApplication.Exit(1);
        }
    }

    private static Entry Measure(string path, string id)
    {
        var entry = new Entry { path = path, name = Path.GetFileNameWithoutExtension(path) };
        GameObject instance = null;
        var fallback = new Material(Shader.Find("Standard")) { color = new Color(0.6f, 0.65f, 0.7f) };
        bool previousBackfaces = Physics.queriesHitBackfaces;
        try
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            instance = UnityEngine.Object.Instantiate(asset);
            instance.transform.position = Vector3.zero;
            SceneManager.MoveGameObjectToScene(instance, PreviewScene);
            var ignored = new HashSet<Renderer>();
            var groups = instance.GetComponentsInChildren<LODGroup>(true);
            entry.lodGroups = groups.Length;
            foreach (var group in groups)
            {
                var lods = group.GetLODs();
                foreach (var lod in lods.Skip(1))
                    foreach (var renderer in lod.renderers)
                        if (renderer != null && !lods[0].renderers.Contains(renderer)) ignored.Add(renderer);
                group.enabled = false;
            }
            foreach (var renderer in ignored) renderer.enabled = false;
            entry.colliders = instance.GetComponentsInChildren<Collider>(true).Length;
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            entry.missingScripts = instance.GetComponentsInChildren<Transform>(true)
                .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            var allMaterials = new HashSet<Material>();
            var rays = new List<MeshCollider>();
            var bounds = new Bounds();
            bool hasBounds = false;
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                var mesh = filter.sharedMesh;
                if (mesh == null || renderer == null || !renderer.enabled || !filter.gameObject.activeInHierarchy || ignored.Contains(renderer)) continue;
                entry.renderers++;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    if (mesh.GetTopology(sub) == MeshTopology.Triangles) entry.triangles += (int)(mesh.GetIndexCount(sub) / 3);
                foreach (var vertex in mesh.vertices)
                {
                    var point = filter.transform.TransformPoint(vertex);
                    if (!hasBounds) { bounds = new Bounds(point, Vector3.zero); hasBounds = true; }
                    else bounds.Encapsulate(point);
                }
                entry.materialSlots += renderer.sharedMaterials.Length;
                foreach (var material in renderer.sharedMaterials) if (material != null) allMaterials.Add(material);
                // Исходные материалы сохраняются в статистике; отсутствующие заменяются только для превью.
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m != null && m.shader != null && m.shader.isSupported ? m : fallback).ToArray();
                var rayCollider = filter.gameObject.AddComponent<MeshCollider>();
                rayCollider.sharedMesh = mesh;
                rays.Add(rayCollider);
            }
            if (!hasBounds) { entry.skipped = "Нет статической геометрии: VFX или пустой префаб"; return entry; }
            entry.size = bounds.size;
            entry.center = bounds.center;
            entry.materials = allMaterials.Count;
            entry.shaders = allMaterials.Select(m => m.shader == null ? "MISSING" : m.shader.name).Distinct().ToArray();
            Physics.queriesHitBackfaces = true;
            Physics.SyncTransforms();
            entry.masks = new string[3];
            entry.coverage = new float[3];
            for (int view = 0; view < 3; view++)
            {
                entry.masks[view] = Mask(bounds, rays, view);
                entry.coverage[view] = entry.masks[view].Count(c => c == '1') / 256f;
            }
            entry.preview = "previews/" + id + ".png";
            RenderViews(bounds, Path.Combine(Output, entry.preview));
        }
        catch (Exception exception) { entry.error = exception.ToString(); }
        finally
        {
            Physics.queriesHitBackfaces = previousBackfaces;
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            UnityEngine.Object.DestroyImmediate(fallback);
        }
        return entry;
    }

    private static string Mask(Bounds bounds, List<MeshCollider> colliders, int view)
    {
        var result = new char[256];
        float margin = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) + 1;
        for (int row = 0; row < 16; row++) for (int column = 0; column < 16; column++)
        {
            float u = (column + 0.5f) / 16, v = (row + 0.5f) / 16;
            Vector3 origin, direction;
            if (view == 0) { origin = new Vector3(bounds.min.x + u * bounds.size.x, bounds.min.y + v * bounds.size.y, bounds.min.z - margin); direction = Vector3.forward; }
            else if (view == 1) { origin = new Vector3(bounds.min.x - margin, bounds.min.y + v * bounds.size.y, bounds.min.z + u * bounds.size.z); direction = Vector3.right; }
            else { origin = new Vector3(bounds.min.x + u * bounds.size.x, bounds.max.y + margin, bounds.min.z + v * bounds.size.z); direction = Vector3.down; }
            var ray = new Ray(origin, direction);
            // Односторонние панели имеют разный winding: силуэт проверяем с обеих сторон.
            var reverse = new Ray(origin + direction * (margin * 3), -direction);
            result[row * 16 + column] = colliders.Any(c => c.Raycast(ray, out _, margin * 3) ||
                c.Raycast(reverse, out _, margin * 3)) ? '1' : '0';
        }
        return new string(result);
    }

    private static void RenderViews(Bounds bounds, string target)
    {
        const int size = 192;
        var texture = new Texture2D(size * 3, size, TextureFormat.RGB24, false);
        var renderTarget = new RenderTexture(size, size, 24);
        var previous = RenderTexture.active;
        try
        {
            Camera.targetTexture = renderTarget;
            float diameter = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            Camera.farClipPlane = diameter * 10 + 10;
            Camera.orthographicSize = Mathf.Max(0.1f, diameter * 0.8f);
            var directions = new[] { new Vector3(0, 0.35f, -1), new Vector3(1, 0.35f, 0), new Vector3(1, 1, -1) };
            for (int i = 0; i < directions.Length; i++)
            {
                Camera.transform.position = bounds.center + directions[i].normalized * (diameter * 3 + 2);
                Camera.transform.LookAt(bounds.center);
                Camera.Render();
                RenderTexture.active = renderTarget;
                texture.ReadPixels(new Rect(0, 0, size, size), i * size, 0);
            }
            texture.Apply();
            File.WriteAllBytes(target, texture.EncodeToPNG());
        }
        finally
        {
            Camera.targetTexture = null;
            RenderTexture.active = previous;
            renderTarget.Release();
            UnityEngine.Object.DestroyImmediate(renderTarget);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
