using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Назначение меток и экспорт визуальных снимков, без подгонки и изменения исходников.</summary>
    public static class CatalogMarkingService
    {
        public const string LedgerPath = "Assets/Editor/VR_Battlegrounds/LevelDesign/Data/CatalogMarkings.asset";
        public const string RecipePath = "Assets/Editor/VR_Battlegrounds/LevelDesign/Data/MarkedCandidateRecipes.json";
        public const string TablePath = "Assets/Editor/VR_Battlegrounds/LevelDesign/Data/MarkedCandidates.csv";
        private const string OutputFolder = "Assets/Prefabs/LevelDesign/Decorated/Marked";
        private const string MeshFolder = "Assets/Art/Models/CatalogVariants/Marked";
        public const string BlocksFolder = "Assets/Prefabs/LevelDesign/LD_Alphabet";
        public const string GizmoPreference = "VrBattlegrounds.CatalogMarks.ShowGizmos";

        [Serializable] private sealed class MaterialRule { public string[] nameContains; public string requiredCoverClass; }
        [Serializable] private sealed class Policy { public MaterialRule[] materialMatchingRules; }
        [Serializable] public sealed class MarkedRecipe : AssetCandidateReviewScene.Recipe
        {
            public string theme, notes, selection = "user", status = "marked", preview = "";
            public string[] source_ids, source_names, warnings;
            public float[] target_size;
            public float dimension_error, silhouette_error = -1;
            public int triangles, material_slots;
        }
        [Serializable] public sealed class MarkedGroup { public string block; public MarkedRecipe[] candidates; }
        [Serializable] public sealed class MarkedCatalog { public MarkedGroup[] groups; }

        public static CatalogMarkings Load(bool create = false)
        {
            var ledger = AssetDatabase.LoadAssetAtPath<CatalogMarkings>(LedgerPath);
            if (ledger != null || !create) return ledger;
            EnsureFolder(Path.GetDirectoryName(LedgerPath).Replace('\\', '/'));
            ledger = ScriptableObject.CreateInstance<CatalogMarkings>();
            AssetDatabase.CreateAsset(ledger, LedgerPath);
            return ledger;
        }

        public static string[] BlockPaths() => AssetDatabase.FindAssets("t:Prefab", new[] { BlocksFolder })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();

        public static GameObject[] SelectionRoots(GameObject[] selection) => selection.Where(g => g != null)
            .Distinct().Where(g => !selection.Any(other => other != null && other != g && g.transform.IsChildOf(other.transform))).ToArray();

        public static GameObject Block(CatalogMarkings.Entry entry)
        {
            var path = AssetDatabase.GUIDToAssetPath(entry.blockGuid);
            if (!path.StartsWith(BlocksFolder + "/", StringComparison.Ordinal)) throw new InvalidOperationException("Эталон метки вне LD_Alphabet: " + entry.title);
            var block = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (block == null) throw new InvalidOperationException("Эталон метки удалён: " + entry.title);
            return block;
        }

        public static GameObject[] Resolve(CatalogMarkings.Entry entry) => entry.objectIds.Select(id =>
        {
            GlobalObjectId parsed;
            return GlobalObjectId.TryParse(id, out parsed) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) as GameObject : null;
        }).ToArray();

        private static void Editable()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Разметка и сборка доступны вне Play Mode и компиляции.");
        }

        public static CatalogMarkings.Entry Mark(GameObject[] selected, string blockPath, string title, string theme, string notes)
        {
            Editable();
            var roots = SelectionRoots(selected);
            if (roots.Length == 0 || roots.Length > 32) throw new InvalidOperationException("Выберите от 1 до 32 объектов одной сборки.");
            foreach (var go in roots)
            {
                if (EditorUtility.IsPersistent(go) || !go.scene.IsValid() || string.IsNullOrEmpty(go.scene.path))
                    throw new InvalidOperationException("Выберите объекты в сохранённой сцене, а не ассеты в Project.");
                for (var parent = go.transform; parent != null; parent = parent.parent)
                    if (parent.name.StartsWith("manual_", StringComparison.Ordinal))
                        throw new InvalidOperationException("Выберите источник в демо, а не собранный ручной кандидат: его экземпляр обновляется при сборке.");
            }
            var block = AssetDatabase.LoadAssetAtPath<GameObject>(blockPath);
            if (block == null || !blockPath.StartsWith(BlocksFolder + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("Эталон должен быть из LD_Alphabet.");
            ValidateMaterial(roots, block);
            Filters(roots); // Ограничение размера и пригодность проверяются до записи метки.
            var ids = roots.Select(g => GlobalObjectId.GetGlobalObjectIdSlow(g).ToString()).OrderBy(id => id).ToArray();
            var ledger = Load(true);
            string guid = AssetDatabase.AssetPathToGUID(blockPath);
            Undo.RecordObject(ledger, "Назначить LEGO-метку");
            var entry = ledger.entries.FirstOrDefault(e => e.blockGuid == guid && e.objectIds.SequenceEqual(ids));
            if (entry == null)
            {
                entry = new CatalogMarkings.Entry { id = "manual_" + Guid.NewGuid().ToString("N") };
                ledger.entries.Add(entry);
            }
            entry.blockGuid = guid;
            entry.objectIds = ids;
            entry.title = string.IsNullOrWhiteSpace(title) ? string.Join(" + ", roots.Select(g => g.name)) : title.Trim();
            entry.sourceNames = roots.Select(g => g.name).ToArray();
            entry.theme = theme;
            entry.notes = notes;
            Save(ledger);
            return entry;
        }

        public static void Save(CatalogMarkings ledger)
        {
            EditorUtility.SetDirty(ledger);
            AssetDatabase.SaveAssetIfDirty(ledger);
            CatalogMarkingGizmos.Invalidate();
            SceneView.RepaintAll();
        }

        public static void Remove(CatalogMarkings.Entry entry)
        {
            var ledger = Load();
            if (ledger == null) return;
            Undo.RecordObject(ledger, "Убрать LEGO-метку");
            ledger.entries.Remove(entry);
            Save(ledger);
        }

        public static MeshFilter[] Filters(GameObject[] roots)
        {
            var filters = new List<MeshFilter>();
            foreach (var root in roots)
            {
                var excluded = new HashSet<Renderer>();
                var firstLod = new HashSet<Renderer>();
                foreach (var group in root.GetComponentsInChildren<LODGroup>(true))
                {
                    var lods = group.GetLODs();
                    var first = lods.Length == 0 ? new HashSet<Renderer>() : new HashSet<Renderer>(lods[0].renderers);
                    firstLod.UnionWith(first);
                    foreach (var renderer in lods.Skip(1).SelectMany(l => l.renderers))
                        if (!first.Contains(renderer)) excluded.Add(renderer);
                }
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (filter.sharedMesh == null || filter.sharedMesh.vertexCount == 0 || renderer == null || excluded.Contains(renderer)) continue;
                    bool active = true;
                    for (var t = filter.transform; t != root.transform && t != null; t = t.parent)
                        if (!t.gameObject.activeSelf) active = false;
                    // Вся демо-зона может быть скрыта; внутренние выключенные детали не экспортируются.
                    if (active && (renderer.enabled || firstLod.Contains(renderer))) filters.Add(filter);
                    if (filters.Count > 128) throw new InvalidOperationException("Слишком большая сборка: более 128 статических мешей. Выберите отдельное укрытие или секцию.");
                }
            }
            if (filters.Count == 0) throw new InvalidOperationException("Выбор не содержит статических MeshRenderer. Анимация и Terrain пока не поддерживаются.");
            return filters.Distinct().ToArray();
        }

        public static Bounds BoundsOf(GameObject[] roots)
        {
            bool found = false;
            var bounds = new Bounds();
            foreach (var filter in Filters(roots))
            foreach (var vertex in filter.sharedMesh.vertices)
            {
                var p = filter.transform.TransformPoint(vertex);
                if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; }
                else bounds.Encapsulate(p);
            }
            return bounds;
        }

        public static CoverClass Cover(GameObject block) => block.GetComponent<CoverSurface>() != null
            ? block.GetComponent<CoverSurface>().Class : CoverClass.Hard;

        private static void ValidateMaterial(GameObject[] roots, GameObject block)
        {
            var policy = JsonUtility.FromJson<Policy>(File.ReadAllText("Tools/AssetCatalog/catalog-policy.json"));
            foreach (var go in roots.SelectMany(g => g.GetComponentsInChildren<Transform>(true)))
            {
                var filter = go.GetComponent<MeshFilter>();
                string ancestors = "";
                for (var parent = go.parent; parent != null; parent = parent.parent) ancestors += "/" + parent.name + ".prefab ";
                string name = ("/" + go.name + ".prefab " + ancestors + (filter != null ? AssetDatabase.GetAssetPath(filter.sharedMesh) : "") + " " + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go.gameObject)
                    + " " + AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromOriginalSource(go.gameObject)))
                    .ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
                foreach (var rule in policy.materialMatchingRules)
                    if (rule.nameContains.Any(term => name.Contains(term)) && Cover(block).ToString() != rule.requiredCoverClass)
                        throw new InvalidOperationException(go.name + " должен сопоставляться только с " + rule.requiredCoverClass + ".");
            }
        }

        public static MarkedCatalog Export()
        {
            Editable();
            var ledger = Load(true);
            // Сначала проверяем все источники, чтобы отсутствующая сцена не очистила прежний каталог.
            foreach (var entry in ledger.entries)
            {
                Guid id;
                if (entry.id == null || !entry.id.StartsWith("manual_", StringComparison.Ordinal) || !Guid.TryParseExact(entry.id.Substring(7), "N", out id))
                    throw new InvalidOperationException("Повреждён ID метки: " + entry.title);
                var roots = Resolve(entry);
                if (roots.Length == 0 || roots.Any(g => g == null))
                    throw new InvalidOperationException("Не найден источник «" + entry.title + "». Откройте сцену с этой меткой; удалённые объекты нужно пометить заново.");
                ValidateMaterial(roots, Block(entry));
                foreach (var filter in Filters(roots))
                    if (filter.GetComponent<MeshRenderer>().sharedMaterials.Any(m => m != null && !EditorUtility.IsPersistent(m)))
                        throw new InvalidOperationException("У «" + entry.title + "» есть материал в памяти. Сначала сохраните его как ассет.");
            }
            EnsureFolder(OutputFolder);
            EnsureFolder(MeshFolder);
            var recipes = new List<Tuple<string, MarkedRecipe>>();
            foreach (var entry in ledger.entries)
            {
                var roots = Resolve(entry);
                var filters = Filters(roots);
                var bounds = BoundsOf(roots);
                var origin = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                var block = Block(entry);
                var output = new GameObject("Catalog_" + entry.id + "_" + Cover(block));
                int triangles = 0, materials = 0;
                try
                {
                    for (int i = 0; i < filters.Length; i++)
                    {
                        var filter = filters[i];
                        var mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
                        mesh.name = entry.id + "_" + i;
                        var matrix = Matrix4x4.Translate(-origin) * filter.transform.localToWorldMatrix;
                        var vertices = mesh.vertices;
                        for (int v = 0; v < vertices.Length; v++) vertices[v] = matrix.MultiplyPoint3x4(vertices[v]);
                        mesh.vertices = vertices;
                        var normals = mesh.normals;
                        var normalMatrix = matrix.inverse.transpose;
                        for (int n = 0; n < normals.Length; n++) normals[n] = normalMatrix.MultiplyVector(normals[n]).normalized;
                        mesh.normals = normals;
                        var tangents = mesh.tangents;
                        for (int t = 0; t < tangents.Length; t++)
                        {
                            var direction = matrix.MultiplyVector(new Vector3(tangents[t].x, tangents[t].y, tangents[t].z)).normalized;
                            tangents[t] = new Vector4(direction.x, direction.y, direction.z, tangents[t].w * (matrix.determinant < 0 ? -1 : 1));
                        }
                        mesh.tangents = tangents;
                        if (matrix.determinant < 0)
                            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                            {
                                var indices = mesh.GetTriangles(sub);
                                for (int t = 0; t < indices.Length; t += 3) { int swap = indices[t]; indices[t] = indices[t + 1]; indices[t + 1] = swap; }
                                mesh.SetTriangles(indices, sub);
                            }
                        mesh.RecalculateBounds();
                        string meshPath = MeshFolder + "/" + mesh.name + ".asset";
                        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                        if (saved == null) { AssetDatabase.CreateAsset(mesh, meshPath); saved = mesh; }
                        else { EditorUtility.CopySerialized(mesh, saved); UnityEngine.Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); AssetDatabase.SaveAssetIfDirty(saved); }
                        var piece = new GameObject(filter.name);
                        piece.transform.SetParent(output.transform, false);
                        piece.AddComponent<MeshFilter>().sharedMesh = saved;
                        var renderer = piece.AddComponent<MeshRenderer>();
                        var source = filter.GetComponent<MeshRenderer>();
                        renderer.sharedMaterials = source.sharedMaterials;
                        renderer.shadowCastingMode = source.shadowCastingMode;
                        renderer.receiveShadows = source.receiveShadows;
                        for (int sub = 0; sub < saved.subMeshCount; sub++) triangles += (int)saved.GetIndexCount(sub) / 3;
                        materials += source.sharedMaterials.Length;
                    }
                    var surface = output.AddComponent<CoverSurface>();
                    surface.Class = Cover(block);
                    var referenceSurface = block.GetComponent<CoverSurface>();
                    if (referenceSurface != null) surface.PenetrationModifier = referenceSurface.PenetrationModifier;
                    string path = OutputFolder + "/" + entry.id + ".prefab";
                    if (PrefabUtility.SaveAsPrefabAsset(output, path) == null) throw new IOException("Не сохранён кандидат: " + entry.title);
                    var target = BoundsOf(new[] { block }).size;
                    float error = Mathf.Max(Mathf.Abs(bounds.size.x - target.x) / Mathf.Max(target.x, .001f),
                        Mathf.Abs(bounds.size.y - target.y) / Mathf.Max(target.y, .001f), Mathf.Abs(bounds.size.z - target.z) / Mathf.Max(target.z, .001f));
                    recipes.Add(Tuple.Create(block.name, new MarkedRecipe {
                        id = entry.id, path = path, model = entry.title, variant = "Ручная метка / " + entry.theme,
                        repeat_xyz = new[] { 1, 1, 1 }, uniform_scale = 1, scale_xyz = new[] { 1f, 1f, 1f },
                        actual_size = new[] { bounds.size.x, bounds.size.y, bounds.size.z },
                        target_size = new[] { target.x, target.y, target.z }, dimension_error = error,
                        theme = entry.theme, notes = entry.notes, source_ids = entry.objectIds, source_names = entry.sourceNames,
                        triangles = triangles, material_slots = materials,
                        warnings = new[] { "Визуальная копия LOD0: без коллайдеров и игровых скриптов", "Габариты сохранены; подгонка не выполнялась", "Отклонение от эталона: " + error.ToString("P1", CultureInfo.InvariantCulture) }
                    }));
                }
                finally { UnityEngine.Object.DestroyImmediate(output); }
            }
            var catalog = new MarkedCatalog { groups = recipes.GroupBy(r => r.Item1).Select(g =>
                new MarkedGroup { block = g.Key, candidates = g.Select(r => r.Item2).ToArray() }).ToArray() };
            File.WriteAllText(RecipePath, JsonUtility.ToJson(catalog, true), new UTF8Encoding(false));
            var csv = new StringBuilder("Метка,Эталон,Название,Тема,Размер XYZ,Размер эталона XYZ,Отклонение,Префаб,Заметка,Проверить\n");
            foreach (var item in recipes)
            {
                var r = item.Item2;
                csv.AppendLine(string.Join(",", new[] { r.id, item.Item1, r.model, r.theme,
                    string.Join(" / ", r.actual_size.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))),
                    string.Join(" / ", r.target_size.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))),
                    r.dimension_error.ToString("P1", CultureInfo.InvariantCulture), r.path, r.notes, string.Join("; ", r.warnings) }.Select(Quote)));
            }
            File.WriteAllText(TablePath, csv.ToString(), new UTF8Encoding(true));
            AssetDatabase.ImportAsset(RecipePath);
            AssetDatabase.ImportAsset(TablePath);
            return catalog;
        }

        private static string Quote(string text) => "\"" + (text ?? "").Replace("\"", "\"\"") + "\"";

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }
    }
}
