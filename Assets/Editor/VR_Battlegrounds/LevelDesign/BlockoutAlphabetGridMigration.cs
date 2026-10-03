using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.LevelDesign;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Публикация размеров только активных форм: исходный меш, коллайдер, паспорт и общий JSON сохраняют согласованность.</summary>
    public static class BlockoutAlphabetGridMigration
    {
        private sealed class Recipe
        {
            public BlockoutBlockDefinition definition;
            public GameObject source;
            public Vector2 plan, topPlan;
            public Mesh original, candidate;
            public Bounds previous;
        }

        public static void Run() => Apply(BlockoutGridSettings.Current);

        private static Recipe[] Recipes(BlockoutGridSettings.Data data, IEnumerable<string> shapeIds)
        {
            BlockoutGridSettings.Validate(data);
            var registry = BlockoutRegistryFactory.Current;
            if (registry == null || registry.Definitions.Count == 0 || registry.Definitions.Any(d => !BlockoutCanonicalRegistry.IsActiveDefinition(d) || !d.gameplayGeometry))
                throw new InvalidOperationException("Размеры публикуются только для действующего реестра активных форм.");
            var filter = shapeIds == null ? null : new HashSet<string>(shapeIds);
            if (filter != null && filter.Any(id => !registry.Definitions.Any(d => d.shapeId == id)))
                throw new ArgumentException("Выбрана форма вне активного реестра.");
            var result = new List<Recipe>();
            foreach (var definition in registry.Definitions.Where(d => filter == null || filter.Contains(d.shapeId)))
            {
                var dimensions = data.blocks.Single(b => b.name == definition.dimensionsSourceKey);
                Vector2 plan = new Vector2(dimensions.width, dimensions.depth);
                if (!definition.editableDimensions && !BlockoutCanonicalRegistry.IsHalfCylinder(definition) && Mathf.Abs(plan.x - plan.y) > .0001f)
                    throw new ArgumentException("Основание этой формы должно быть квадратным: " + definition.displayName);
                var source = definition.geometryPrefab;
                if (source == null || source.transform.localScale != Vector3.one || source.GetComponentsInChildren<MeshFilter>(true).Length != 1 ||
                    source.GetComponent<MeshFilter>() == null || source.GetComponentsInChildren<Collider>(true).Length != 1 || source.GetComponent<BlockoutHeightGeometry>() != null ||
                    !(source.GetComponent<Collider>() is BoxCollider || source.GetComponent<Collider>() is MeshCollider))
                    throw new InvalidOperationException("Неподдерживаемый состав исходного префаба: " + definition.displayName);
                var mesh = source.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null || !mesh.isReadable || !EditorUtility.IsPersistent(mesh) || mesh.bounds.size.x <= 0 || mesh.bounds.size.z <= 0)
                    throw new InvalidOperationException("Нет читаемого исходного mesh asset: " + definition.displayName);
                var sourceMeshCollider = source.GetComponent<MeshCollider>();
                if (sourceMeshCollider != null && sourceMeshCollider.sharedMesh != mesh)
                    throw new InvalidOperationException("MeshCollider источника должен повторять его визуальный меш: " + definition.displayName);
                var step = source.GetComponent<BlockoutSteppedGeometry>();
                var topPlan = new Vector2(data.stepTopWidth,data.stepTopDepth);
                if (step != null && (plan.x < topPlan.x || plan.y < topPlan.y))
                    throw new ArgumentException("Низ ступенчатой формы не может быть уже её верхней части.");
                result.Add(new Recipe { definition = definition, source = source, plan = plan, topPlan = topPlan, original = mesh, previous = mesh.bounds });
                foreach (var variant in definition.materialVariants.Where(v => v?.sourcePrefab != null).Select(v => v.sourcePrefab).Distinct())
                {
                    if (variant == source || !SameGeometry(source, variant)) continue;
                    var variantMesh = variant.GetComponent<MeshFilter>().sharedMesh;
                    result.Add(new Recipe { definition = definition, source = variant, plan = plan, topPlan = topPlan, original = variantMesh, previous = variantMesh.bounds });
                }
            }
            return result.ToArray();
        }

        private static bool SameGeometry(GameObject a, GameObject b)
        {
            if (b.transform.localScale != Vector3.one || b.GetComponentsInChildren<MeshFilter>(true).Length != 1 || b.GetComponentsInChildren<Collider>(true).Length != 1) return false;
            var am = a.GetComponent<MeshFilter>()?.sharedMesh; var bm = b.GetComponent<MeshFilter>()?.sharedMesh;
            var ac = a.GetComponent<Collider>(); var bc = b.GetComponent<Collider>();
            if (am == null || bm == null || !bm.isReadable || !EditorUtility.IsPersistent(bm) || ac == null || bc == null || ac.GetType() != bc.GetType()) return false;
            if (ac is BoxCollider ab && bc is BoxCollider bb && (ab.center != bb.center || ab.size != bb.size)) return false;
            return am.vertices.SequenceEqual(bm.vertices) && am.triangles.SequenceEqual(bm.triangles);
        }

        private static Mesh Build(Recipe recipe)
        {
            if(BlockoutCanonicalRegistry.IsHalfCylinder(recipe.definition))return HalfCylinderSourceBuilder.BuildMesh(recipe.plan.x,recipe.previous.size.y);
            var step = recipe.source.GetComponent<BlockoutSteppedGeometry>();
            if (step != null) {var bottom=step.localBottomCenter;bottom.y=0;return BlockoutSteppedGeometry.BuildMesh(step.totalHeight, recipe.plan, recipe.topPlan, bottom, step.lowerHeightLimit);}
            var mesh = Object.Instantiate(recipe.original); var vertices = mesh.vertices;
            Bounds bounds = recipe.previous;
            float ratioX = recipe.plan.x / bounds.size.x, ratioZ = recipe.plan.y / bounds.size.z;
            var uvs = mesh.uv; var normals = mesh.normals;
            bool round = !recipe.definition.supportsCellWall && recipe.source.GetComponent<MeshCollider>() != null;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i].y -= bounds.min.y;
                vertices[i].x = bounds.center.x + (vertices[i].x - bounds.center.x) * ratioX;
                vertices[i].z = bounds.center.z + (vertices[i].z - bounds.center.z) * ratioZ;
                // Метровая сетка не растягивается вместе с уменьшением основания.
                if (uvs.Length == vertices.Length && normals.Length == vertices.Length)
                {
                    Vector3 normal = normals[i];
                    if (Mathf.Abs(normal.y) > .99f) uvs[i] = Vector2.Scale(uvs[i], new Vector2(ratioX, ratioZ));
                    else uvs[i].x *= round || Mathf.Abs(normal.z) >= Mathf.Abs(normal.x) ? ratioX : ratioZ;
                }
            }
            mesh.vertices = vertices; mesh.uv = uvs; mesh.RecalculateBounds(); mesh.RecalculateNormals(); mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Под замком Unity. Сначала проверяет весь выбранный набор, затем сохраняет только изменённые активные источники.</summary>
        public static string Apply(BlockoutGridSettings.Data draft, IEnumerable<string> shapeIds = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Публикация размеров выполняется вне Play Mode после компиляции.");
            var data = JsonUtility.FromJson<BlockoutGridSettings.Data>(JsonUtility.ToJson(draft));
            BlockoutGridSettings.AddMissingActiveRows(data);
            var recipes = Recipes(data, shapeIds);
            var changed = recipes.Where(r => Mathf.Abs(r.previous.size.x - r.plan.x) > .0001f || Mathf.Abs(r.previous.size.z - r.plan.y) > .0001f ||
                Mathf.Abs(r.previous.min.y)>.0001f || Mathf.Abs(r.source.transform.localPosition.y)>.0001f ||
                r.source.TryGetComponent<BlockoutSteppedGeometry>(out var profile) && ((profile.topSize-r.topPlan).sqrMagnitude>.000001f||Mathf.Abs(profile.localBottomCenter.y)>.0001f)).ToArray();
            // Строки вне выбранной области остаются прежними, включая архивные формы.
            var published = JsonUtility.FromJson<BlockoutGridSettings.Data>(JsonUtility.ToJson(BlockoutGridSettings.Current));
            published.step = data.step; published.module = data.module;
            if(recipes.Any(r=>r.source.GetComponent<BlockoutSteppedGeometry>()!=null))
            {published.stepTopWidth=data.stepTopWidth;published.stepTopDepth=data.stepTopDepth;}
            foreach (var recipe in recipes)
            {
                var block = published.blocks.Single(b => b.name == recipe.definition.dimensionsSourceKey);
                block.width = recipe.plan.x; block.depth = recipe.plan.y;
                var alias = published.blocks.FirstOrDefault(b => b.name == recipe.source.name);
                if (alias != null) { alias.width = recipe.plan.x; alias.depth = recipe.plan.y; }
            }
            BlockoutGridSettings.Validate(published);
            string backup = "Temp/LevelDesign/ActiveSizes/Before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup);
            var paths = recipes.Select(r => AssetDatabase.GetAssetPath(r.definition)).Concat(changed.Select(r => AssetDatabase.GetAssetPath(r.original)))
                .Concat(changed.Select(r => AssetDatabase.GetAssetPath(r.source))).Append(BlockoutGridSettings.Path).Distinct().ToArray();
            foreach (string path in paths)
            {
                if (File.Exists(path)) File.Copy(path, Path.Combine(backup, Path.GetFileName(path)), false);
                if (File.Exists(path + ".meta")) File.Copy(path + ".meta", Path.Combine(backup, Path.GetFileName(path) + ".meta"), false);
            }
            BlockoutPublishedSceneSync.Report scenePlan = null;
            try
            {
                foreach (var recipe in changed) recipe.candidate = Build(recipe);
                if (changed.GroupBy(r => r.original).Any(group => group.Select(r => r.plan).Distinct().Count() > 1))
                    throw new InvalidOperationException("Один mesh asset используется формами с разными запрошенными размерами.");
                scenePlan = BlockoutPublishedSceneSync.Prepare(recipes.Select(recipe => new BlockoutPublishedSceneSync.Template
                {
                    definition = recipe.definition, source = recipe.source, candidate = recipe.candidate,
                    previousPlan = new Vector2(recipe.previous.size.x, recipe.previous.size.z), plan = recipe.plan,
                    sourceChanges = changed.Contains(recipe), topPlan = recipe.topPlan
                }));
                if (scenePlan.sourceBlockers.Count > 0) throw new InvalidOperationException(string.Join("; ", scenePlan.sourceBlockers));
                if (scenePlan.rejected.Count > 0) throw new InvalidOperationException("Непредставимая геометрия: " + scenePlan.Summary);
                BlockoutPublishedSceneSync.BeginSceneUndo(scenePlan);
                foreach (var recipe in changed)
                {
                    string path = AssetDatabase.GetAssetPath(recipe.source);
                    var contents = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        var step = contents.GetComponent<BlockoutSteppedGeometry>();
                        if (step != null) {step.baseSize = recipe.plan;step.topSize=recipe.topPlan;step.localBottomCenter.y=0;}
                        var position=contents.transform.localPosition;position.y=0;contents.transform.localPosition=position;
                        EditorUtility.CopySerialized(recipe.candidate, recipe.original);
                        EditorUtility.SetDirty(recipe.original); AssetDatabase.SaveAssetIfDirty(recipe.original);
                        contents.GetComponent<MeshFilter>().sharedMesh = recipe.original;
                        var box = contents.GetComponent<BoxCollider>();
                        if (box != null) { box.center = recipe.original.bounds.center; box.size = recipe.original.bounds.size; }
                        var meshCollider = contents.GetComponent<MeshCollider>();
                        if (meshCollider != null) { meshCollider.sharedMesh = null; meshCollider.sharedMesh = recipe.original; }
                        PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);
                        if (!saved) throw new InvalidOperationException("Не удалось сохранить источник; резервная копия: " + backup);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                }
                foreach (var recipe in recipes)
                {
                    BlockoutCanonicalRegistry.SetPlanCaps(recipe.definition, recipe.plan);
                    EditorUtility.SetDirty(recipe.definition); AssetDatabase.SaveAssetIfDirty(recipe.definition);
                }
                BlockoutGridSettings.Save(published);
                BlockoutGridSettingsPanel.Reload();
                BlockoutRegistryFactory.InvalidateSource();
                BlockoutPublishedSceneSync.Apply(scenePlan);
                return backup;
            }
            catch (Exception exception) { if(scenePlan!=null)BlockoutPublishedSceneSync.Rollback(scenePlan);throw new InvalidOperationException("Публикация остановлена. Изменения сцены отменены. Резервная копия: " + backup, exception); }
            finally { foreach (var recipe in changed) if (recipe.candidate != null) Object.DestroyImmediate(recipe.candidate); }
        }

        /// <summary>Сравнивает реальные источники с черновиком; preview=true проверяет кандидата в изолированной сцене.</summary>
        public static Dictionary<string, object> Probe(BlockoutGridSettings.Data draft = null, bool preview = false, IEnumerable<string> shapeIds = null)
        {
            var recipes = Recipes(draft ?? BlockoutGridSettings.Current, shapeIds);
            var facts = new Dictionary<string, object>();
            foreach (var recipe in recipes)
            {
                var scene = EditorSceneManager.NewPreviewScene(); Mesh mesh = null;
                try
                {
                    var clone = (GameObject)PrefabUtility.InstantiatePrefab(recipe.source, scene);
                    if (preview)
                    {
                        mesh = Build(recipe); clone.GetComponent<MeshFilter>().sharedMesh = mesh;
                        var previewBox = clone.GetComponent<BoxCollider>(); if (previewBox != null) { previewBox.center = mesh.bounds.center; previewBox.size = mesh.bounds.size; }
                        var previewMc = clone.GetComponent<MeshCollider>(); if (previewMc != null) { previewMc.sharedMesh = null; previewMc.sharedMesh = mesh; }
                    }
                    Physics.SyncTransforms();
                    Bounds actual = clone.GetComponent<MeshFilter>().sharedMesh.bounds;
                    var collider = clone.GetComponent<Collider>();
                    bool colliderMatches = collider is BoxCollider colliderBox ? (colliderBox.size - actual.size).sqrMagnitude < .000001f && (colliderBox.center - actual.center).sqrMagnitude < .000001f :
                        collider is MeshCollider colliderMesh && colliderMesh.sharedMesh == clone.GetComponent<MeshFilter>().sharedMesh;
                    float worldBottom = collider.bounds.min.y;
                    var step = clone.GetComponent<BlockoutSteppedGeometry>();
                    if (step != null && preview) {step.baseSize = recipe.plan;step.topSize=recipe.topPlan;step.localBottomCenter.y=0;}
                    var heightGeometry = step == null ? clone.GetComponent<BlockoutHeightGeometry>() : null;
                    if (step == null && heightGeometry == null) heightGeometry = clone.AddComponent<BlockoutHeightGeometry>();
                    if (heightGeometry != null && !heightGeometry.Initialized) heightGeometry.Initialize(actual.size.y, 1.6f);
                    bool heightMatrix = true, rayAgreement = true;
                    foreach (float height in new[] { 1.2f, 1.6f, 2.5f })
                    {
                        if (step != null) step.ApplyHeight(height); else heightGeometry.ApplyHeight(height);
                        Physics.SyncTransforms();
                        Bounds world = collider.bounds;
                        heightMatrix &= Mathf.Abs(world.size.y - height) < .001f && Mathf.Abs(world.min.y - worldBottom) < .001f && clone.transform.localScale == Vector3.one;
                        foreach (var direction in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                        {
                            rayAgreement &= collider.Raycast(new Ray(world.center - direction * 2, direction), out _, 4);
                            Vector3 tangent = Vector3.Cross(Vector3.up, direction);
                            float offset = Mathf.Abs(tangent.x) * world.extents.x + Mathf.Abs(tangent.z) * world.extents.z + .05f;
                            rayAgreement &= !collider.Raycast(new Ray(world.center + tangent * offset - direction * 2, direction), out _, 4);
                        }
                    }
                    facts[recipe.definition.title + " / " + recipe.source.name] = new Dictionary<string, object>
                    {
                        { "planMatches", Mathf.Abs(actual.size.x - recipe.plan.x) < .0001f && Mathf.Abs(actual.size.z - recipe.plan.y) < .0001f },
                        { "heightPreservedAndBottomAnchored", Mathf.Abs(actual.size.y - recipe.previous.size.y) < .0001f && Mathf.Abs(actual.min.y) < .0001f },
                        { "actualMeshSize", actual.size }, { "requestedPlan", recipe.plan }, { "rootScaleOne", clone.transform.localScale == Vector3.one },
                        { "colliderMatchesMesh", colliderMatches }, { "heightMatrixLowMidTall", heightMatrix }, { "centerBlocksOutsidePasses", rayAgreement },
                        { "capsMatch", Mathf.Abs(recipe.definition.minDimensions.z - recipe.plan.y) < .0001f && Mathf.Abs(recipe.definition.maxDimensions.z - recipe.plan.y) < .0001f &&
                            (recipe.definition.editableDimensions || (Mathf.Abs(recipe.definition.minDimensions.x - recipe.plan.x) < .0001f && Mathf.Abs(recipe.definition.maxDimensions.x - recipe.plan.x) < .0001f)) },
                        { "sourceMeshGUID", AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(recipe.original)) },
                        { "sourcePrefabGUID", AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(recipe.source)) }
                    };
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); if (mesh != null) Object.DestroyImmediate(mesh); }
            }
            return facts;
        }
    }
}
