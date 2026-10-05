using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.DebugTools;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Визуальное сравнение эталонов и рецептов; не игровая карта и не утверждение замен.</summary>
    public static class AssetCandidateReviewScene
    {
        public const string ScenePath = "Assets/Scenes/Tools/IndustrialCandidateReview.unity";
        private const string DataPath = "Assets/Editor/VR_Battlegrounds/LevelDesign/Data/IndustrialCandidateRecipes.json";
        private const string MaterialPath = "Assets/Art/Materials/CatalogReview/Floor.mat";

        [Serializable] public class Recipe
        {
            public string id, path, model, variant;
            public int rotation_x, rotation_y;
            public int[] repeat_xyz;
            public float uniform_scale;
            public float[] scale_xyz;
            public float[] actual_size;
        }
        [Serializable] public class Group { public string block; public Recipe[] candidates; }
        [Serializable] public class Catalog { public Group[] groups; }
        [Serializable] private class MaterialRule { public string[] nameContains; public string requiredCoverClass; }
        [Serializable] private class Policy { public MaterialRule[] materialMatchingRules; }
        [Serializable] public class Result { public int blocks, recipes, parts; public float maxSizeError; public string scene; }

        [MenuItem("Tools/VR Battlegrounds/Level Design/Art Pass/Decorate/Build Industrial Candidate Review", false, 200)]
        public static void BuildFromMenu()
        {
            Result result = Build();
            GameLog.Debug.Info($"Стенд кандидатов: {result.blocks} блоков, {result.recipes} рецептов. {ScenePath}");
        }

        public static Catalog LoadCatalog()
        {
            var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(DataPath));
            // Производные списки имеют отдельного владельца: удалённая метка не должна
            // воскресать из старого объединённого Python-отчёта.
            foreach (var group in catalog.groups)
                group.candidates = group.candidates.Where(c => !c.id.StartsWith("manual_", StringComparison.Ordinal)
                    && !c.id.StartsWith("auto_", StringComparison.Ordinal)).ToArray();
            var extra = FolderCandidateScanner.LoadRecipes().groups.ToList();
            if (File.Exists(CatalogMarkingService.RecipePath))
                extra.AddRange(JsonUtility.FromJson<Catalog>(File.ReadAllText(CatalogMarkingService.RecipePath)).groups);
            var groups = catalog.groups.ToList();
            foreach (var group in extra)
            {
                var existing = groups.FirstOrDefault(g => g.block == group.block);
                if (existing == null) groups.Add(group);
                else existing.candidates = existing.candidates.Concat(group.candidates)
                    .GroupBy(c => c.id).Select(g => g.Last()).ToArray();
            }
            catalog.groups = groups.ToArray();
            return catalog;
        }

        /// <summary>Обновляет только ручные кандидаты, сохраняя демо и все остальные правки стенда.</summary>
        public static Result RefreshMarkedCandidates()
            => RefreshOwnedCandidates(JsonUtility.FromJson<Catalog>(File.ReadAllText(CatalogMarkingService.RecipePath)), "manual_");

        /// <summary>Обновляет только предложения сканера папок; сохраняет ручные кандидаты и демо.</summary>
        public static Result RefreshAutomaticCandidates()
            => RefreshOwnedCandidates(FolderCandidateScanner.LoadRecipes(), "auto_");

        private static Result RefreshOwnedCandidates(Catalog catalog, string prefix)
        {
            FolderCandidateScanner.RequireInstalledCatalog();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Обновление стенда доступно вне Play Mode.");
            foreach (var group in catalog.groups)
            {
                LoadPrefab(BlockPath(group.block));
                foreach (var recipe in group.candidates)
                {
                    if (!recipe.id.StartsWith(prefix, StringComparison.Ordinal))
                        throw new InvalidDataException("Чужой ID в производном каталоге: " + recipe.id);
                    LoadPrefab(recipe.path);
                }
            }
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name.StartsWith("Industrial Candidate Review", StringComparison.Ordinal));
            if (root == null) throw new InvalidOperationException("В сцене отсутствует корень стенда.");
            var result = new Result { scene = ScenePath };
            // Убираем только собственные прежние экземпляры, включая метки, удалённые из реестра.
            foreach (Transform row in root.transform)
            foreach (var child in row.Cast<Transform>().Where(t => t.name.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            foreach (var group in catalog.groups)
            {
                var row = root.transform.Find(group.block);
                if (row == null)
                {
                    row = NewObject(group.block, scene, root.transform).transform;
                    var reference = Instantiate(BlockPath(group.block), scene, row);
                    var size = BoundsOf(reference).size;
                    Place(reference, size.x * .5f);
                    Label(row, ReferenceCaption(reference, group.block), new Vector3(size.x * .5f, .025f, -size.z * .5f - 1.5f), new Color(.5f, .85f, 1));
                    int slot = root.transform.childCount - 2;
                    row.position = new Vector3(slot % 3 * 55, 0, slot / 3 * 22);
                }
                var position = row.position;
                try
                {
                    row.position = Vector3.zero;
                    float cursor = BoundsOf(row.gameObject).max.x + 3;
                    foreach (var recipe in group.candidates)
                    {
                        var assembly = Assemble(recipe, scene, row, result);
                        var bounds = BoundsOf(assembly);
                        float error = (bounds.size - new Vector3(recipe.actual_size[0], recipe.actual_size[1], recipe.actual_size[2])).magnitude;
                        if (error > .002f) throw new InvalidDataException("Габариты кандидата не совпали: " + recipe.id);
                        result.maxSizeError = Mathf.Max(result.maxSizeError, error);
                        float center = cursor + bounds.size.x * .5f;
                        Place(assembly, center);
                        Label(row, recipe.model + "\n" + recipe.variant + "\n" + Size(bounds.size),
                            new Vector3(center, .025f, -bounds.size.z * .5f - 1.5f), new Color(1, .8f, .4f)).name = recipe.id + "__Label";
                        cursor += bounds.size.x + 3;
                        result.recipes++;
                    }
                    if (group.candidates.Length > 0)
                        foreach (var label in row.GetComponentsInChildren<AssetCandidateReviewLabel>(true))
                            if (label.Caption == "Кандидатов пока нет") UnityEngine.Object.DestroyImmediate(label.gameObject);
                    result.blocks++;
                }
                finally { row.position = position; }
            }
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Не удалось сохранить кандидаты на стенде.");
            return result;
        }


        public static Result Build()
        {
            FolderCandidateScanner.RequireInstalledCatalog();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Создавать стенд можно только вне Play Mode.");
            var catalog = LoadCatalog();
            var policy = JsonUtility.FromJson<Policy>(File.ReadAllText("Tools/AssetCatalog/catalog-policy.json"));
            // Проверяем все входы до изменения сцены.
            foreach (Group group in catalog.groups)
            {
                var block = LoadPrefab(BlockPath(group.block));
                foreach (Recipe recipe in group.candidates)
                {
                    string name = (recipe.model + " " + recipe.path).ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
                    foreach (var rule in policy.materialMatchingRules)
                    {
                        if (!rule.nameContains.Any(term => name.Contains(term))) continue;
                        CoverClass expected;
                        if (!CoverClassRules.TryExpectedClass(block, out expected) || expected.ToString() != rule.requiredCoverClass)
                            throw new InvalidDataException("Материал кандидата требует " + rule.requiredCoverClass + ": " + recipe.id);
                        var surface = block.GetComponent<CoverSurface>();
                        if (surface == null || surface.Class != expected)
                            throw new InvalidDataException("Неверная разметка защиты блока: " + group.block);
                    }
                    LoadPrefab(recipe.path);
                    if (recipe.uniform_scale <= 0 || recipe.repeat_xyz.Length != 3 || recipe.repeat_xyz.Any(n => n < 1))
                        throw new InvalidDataException("Некорректный рецепт: " + recipe.id);
                    if (recipe.scale_xyz != null && recipe.scale_xyz.Length > 0 &&
                        (recipe.scale_xyz.Length != 3 || recipe.scale_xyz.Any(n => n <= 0)))
                        throw new InvalidDataException("Некорректный масштаб XYZ: " + recipe.id);
                }
            }
            Scene previous = SceneManager.GetActiveScene();
            Scene existingReview = SceneManager.GetSceneByPath(ScenePath);
            if (existingReview.IsValid() && existingReview.isLoaded && existingReview.isDirty)
                throw new InvalidOperationException("Сохраните правки существующего стенда перед пересборкой.");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            // При обновлении соответствий сохраняем добавленные авторские демо.
            if (existingReview.IsValid() && existingReview.isLoaded)
            {
                var demos = existingReview.GetRootGameObjects().FirstOrDefault(g => g.name == AssetPackDemoGallery.RootName);
                if (demos != null) SceneManager.MoveGameObjectToScene(demos, scene);
            }
            if (existingReview.IsValid() && existingReview.isLoaded) EditorSceneManager.CloseScene(existingReview, true);
            var result = new Result { scene = ScenePath };
            try
            {
                var root = NewObject("Industrial Candidate Review — визуальные кандидаты", scene);
                GameObject firstReview = null;
                for (int index = 0; index < catalog.groups.Length; index++)
                {
                    Group group = catalog.groups[index];
                    var row = NewObject(group.block, scene, root.transform);
                    var reference = Instantiate(BlockPath(group.block), scene, row.transform);
                    reference.name = group.block + " — ЭТАЛОН";
                    Bounds target = BoundsOf(reference);
                    Place(reference, target.size.x * 0.5f);
                    Label(row.transform, ReferenceCaption(reference, group.block),
                        new Vector3(target.size.x * 0.5f, 0.025f, -target.size.z * 0.5f - 1.5f), new Color(0.5f, 0.85f, 1));
                    float cursor = target.size.x + 3;
                    foreach (Recipe recipe in group.candidates)
                    {
                        GameObject assembly = Assemble(recipe, scene, row.transform, result);
                        Bounds bounds = BoundsOf(assembly);
                        Vector3 expected = new Vector3(recipe.actual_size[0], recipe.actual_size[1], recipe.actual_size[2]);
                        float error = (bounds.size - expected).magnitude;
                        result.maxSizeError = Mathf.Max(result.maxSizeError, error);
                        if (error > 0.002f) throw new InvalidDataException("Габариты сборки не совпали: " + recipe.id);
                        float center = cursor + bounds.size.x * 0.5f;
                        Place(assembly, center);
                        int count = recipe.repeat_xyz[0] * recipe.repeat_xyz[1] * recipe.repeat_xyz[2];
                        string scale = recipe.scale_xyz != null && recipe.scale_xyz.Length == 3
                            ? $"XYZ {recipe.scale_xyz[0]:0.###}/{recipe.scale_xyz[1]:0.###}/{recipe.scale_xyz[2]:0.###}"
                            : recipe.uniform_scale.ToString("0.###");
                        string caption = recipe.model + "\n" + (recipe.variant ?? "КАНДИДАТ")
                            + $" / {count} шт. / масштаб {scale}\n" + Size(bounds.size);
                        Label(row.transform, caption, new Vector3(center, 0.025f, -bounds.size.z * 0.5f - 1.5f), new Color(1, 0.8f, 0.4f)).name = recipe.id + "__Label";
                        cursor += bounds.size.x + 3;
                        result.recipes++;
                    }
                    if (group.candidates.Length == 0)
                        Label(row.transform, "Кандидатов пока нет", new Vector3(cursor + 3, 0.025f, 0), Color.white);
                    row.transform.position = new Vector3(index % 3 * 55, 0, index / 3 * 22);
                    if (group.block == "LD_Crate_Soft") firstReview = row;
                    result.blocks++;
                }
                Material floorMaterial = FloorMaterial();
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(floor, scene);
                floor.name = "Пол стенда";
                floor.transform.SetParent(root.transform, false);
                floor.transform.position = new Vector3(80, -0.075f, 55);
                floor.transform.localScale = new Vector3(185, 0.15f, 140);
                floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                floor.GetComponent<Collider>().enabled = false;
                var light = NewObject("Освещение стенда", scene).AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.4f;
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(45, -35, 0);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.65f, 0.65f, 0.65f);
                Bounds focus = BoundsOf(firstReview ?? root);
                Camera camera = NewObject("Review Camera", scene).AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.14f, 0.17f);
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(3.5f, focus.size.x * 0.4f);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 500;
                camera.transform.position = focus.center + new Vector3(3, 15, -19);
                camera.transform.LookAt(focus.center);
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                AssetDatabase.Refresh();
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Не удалось сохранить стенд.");
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory("Temp/LevelDesign/CandidateReview");
                File.WriteAllText("Temp/LevelDesign/CandidateReview/build-result.json", JsonUtility.ToJson(result, true));
                // Никакие чужие несохранённые сцены не закрываем.
                bool canOpen = true;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i) != scene && SceneManager.GetSceneAt(i).isDirty) canOpen = false;
                if (canOpen)
                {
                    scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    firstReview = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>())
                        .First(t => t.name == "LD_Crate_Soft" || t.name.StartsWith("LD_Crate_Soft [", StringComparison.Ordinal)).gameObject;
                    Selection.activeGameObject = firstReview;
                    if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
                }
                else
                {
                    EditorSceneManager.CloseScene(scene, true);
                    SceneManager.SetActiveScene(previous);
                }
                return result;
            }
            catch
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                else if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                throw;
            }
        }

        private static GameObject Assemble(Recipe recipe, Scene scene, Transform parent, Result result)
        {
            var root = NewObject(recipe.id, scene, parent);
            for (int x = 0; x < recipe.repeat_xyz[0]; x++)
            for (int y = 0; y < recipe.repeat_xyz[1]; y++)
            for (int z = 0; z < recipe.repeat_xyz[2]; z++)
            {
                GameObject part = Instantiate(recipe.path, scene, root.transform);
                part.transform.rotation = Quaternion.Euler(recipe.rotation_x, recipe.rotation_y, 0) * part.transform.rotation;
                Bounds b = BoundsOf(part);
                part.transform.position += new Vector3((x - (recipe.repeat_xyz[0] - 1) * 0.5f) * b.size.x - b.center.x,
                    y * b.size.y - b.min.y, (z - (recipe.repeat_xyz[2] - 1) * 0.5f) * b.size.z - b.center.z);
                result.parts++;
            }
            root.transform.localScale = recipe.scale_xyz != null && recipe.scale_xyz.Length == 3
                ? new Vector3(recipe.scale_xyz[0], recipe.scale_xyz[1], recipe.scale_xyz[2])
                : Vector3.one * recipe.uniform_scale;
            return root;
        }

        private static string BlockPath(string name) => "Assets/Prefabs/LevelDesign/LD_Alphabet/" + name + ".prefab";
        private static GameObject LoadPrefab(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path)
            ?? throw new InvalidDataException("Префаб не найден: " + path);
        private static GameObject NewObject(string name, Scene scene, Transform parent = null)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }
        private static GameObject Instantiate(string path, Scene scene, Transform parent)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadPrefab(path), scene);
            instance.transform.SetParent(parent, true);
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            // Стенд и замер используют один LOD0: наложенные уровни детализации
            // могут иметь другие bounds и давать ложную ошибку размеров рецепта.
            foreach (var group in instance.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = group.GetLODs();
                if (lods.Length == 0) continue;
                var first = lods[0].renderers.Where(r => r != null).ToArray();
                foreach (var renderer in lods.Skip(1).SelectMany(l => l.renderers).Where(r => r != null))
                    if (!first.Contains(renderer)) renderer.enabled = false;
                foreach (var renderer in first) renderer.enabled = true;
                group.enabled = false;
            }
            return instance;
        }
        private static void Place(GameObject root, float centerX)
        {
            Bounds b = BoundsOf(root);
            root.transform.position += new Vector3(centerX - b.center.x, -b.min.y, -b.center.z);
        }
        private static Bounds BoundsOf(GameObject root)
        {
            bool found = false;
            Bounds bounds = new Bounds();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled) continue;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 point = filter.transform.TransformPoint(vertex);
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            }
            if (!found) throw new InvalidDataException("Нет геометрии: " + root.name);
            return bounds;
        }
        private static string Size(Vector3 size) => $"{size.x:0.##} × {size.y:0.##} × {size.z:0.##} м";
        /// <summary>Свойства берём из компонентов блока, цвет не определяет проходимость.</summary>
        public static string ReferenceCaption(GameObject reference, string blockName)
        {
            bool vaultable = reference.GetComponent<VaultableObstacle>() != null;
            var surface = reference.GetComponent<CoverSurface>();
            var cover = surface != null ? surface.Class : CoverClass.Hard;
            return blockName + "\n" + (vaultable ? "Можно перешагивать" : "Нельзя перешагивать")
                + ", " + cover.ToString().ToLowerInvariant() + "\n" + Size(BoundsOf(reference).size);
        }
        private static GameObject Label(Transform parent, string caption, Vector3 position, Color color)
        {
            var go = new GameObject("Подпись");
            go.hideFlags = HideFlags.DontSaveInBuild;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var label = go.AddComponent<AssetCandidateReviewLabel>();
            label.Caption = caption;
            label.Color = color;
            return go;
        }
        private static Material FloorMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            AssetDatabase.Refresh();
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.18f, 0.2f, 0.23f) };
            material.SetFloat("_Smoothness", 0);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }
    }
}
