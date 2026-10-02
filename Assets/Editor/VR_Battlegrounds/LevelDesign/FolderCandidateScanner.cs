using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Грубые геометрические предложения по импортированному паку; исходники не меняет.</summary>
    public static class FolderCandidateScanner
    {
        public const string PackRoot = "Assets/env_packs";
        public const string DataFolder = "Assets/Editor/VR_Battlegrounds/LevelDesign/Data/Automatic";
        public const string ReportFolder = "Temp/LevelDesign/FolderCandidates";

        [Serializable] private class Rule { public string[] nameContains; public string requiredCoverClass; }
        [Serializable] private class Policy { public string[] excludedAssetRoots; public Rule[] materialMatchingRules; }
        [Serializable] public class Candidate : AssetCandidateReviewScene.Recipe
        {
            public string status = "candidate", selection = "automatic_bounds";
            public string[] warnings;
            public float[] target_size;
            public float score, dimension_error, unfitted_dimension_error;
            public int triangles;
        }
        [Serializable] public class CandidateGroup { public string block; public Candidate[] candidates; }
        [Serializable] public class Measurement
        {
            public string path, error, requiredCoverClass;
            public float[] size;
            public int triangles;
        }
        [Serializable] public class Report
        {
            public string folder, generatedUtc, recipesPath, csvPath;
            public string algorithm = "bounds-v1";
            public int scanned, measured, skipped, matched;
            public Measurement[] models;
            public CandidateGroup[] groups;
        }
        private class Target { public string path, name, cover; public Vector3 size; public List<Candidate> candidates = new List<Candidate>(); }

        public static string ValidateFolder(string folder)
        {
            folder = (folder ?? "").Replace('\\', '/').TrimEnd('/');
            if (!folder.StartsWith(PackRoot + "/", StringComparison.Ordinal) ||
                folder.Split('/').Any(p => p == "." || p == "..") || !AssetDatabase.IsValidFolder(folder))
                throw new ArgumentException("Выберите существующую папку конкретного пака: Assets/env_packs/<имя пака>.");
            return folder;
        }

        public static Session Begin(string folder) => new Session(ValidateFolder(folder));

        /// <summary>Синхронный запуск для агента; окно выполняет те же шаги по одному за Editor update.</summary>
        public static Report Run(string folder)
        {
            var session = Begin(folder);
            while (!session.Step()) { }
            return session.Save();
        }

        public sealed class Session
        {
            private readonly string folder;
            private readonly Policy policy;
            private readonly string[] paths;
            private readonly Target[] targets;
            private readonly List<Measurement> measured = new List<Measurement>();
            private int index;
            private bool saved;
            public int Count => paths.Length;
            public int Done => index;
            public string Current => index < paths.Length ? paths[index] : "Готово";

            internal Session(string folder)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                    throw new InvalidOperationException("Запускайте подбор вне Play Mode, после завершения импорта и компиляции.");
                this.folder = folder;
                policy = JsonUtility.FromJson<Policy>(File.ReadAllText("Tools/AssetCatalog/catalog-policy.json"));
                var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)).ToArray();
                // FBX/OBJ без обёртки также пригодны. Модели, уже использованные префабами, не дублируем.
                var wrapped = new HashSet<string>(prefabs.SelectMany(p => AssetDatabase.GetDependencies(p, true)));
                var models = AssetDatabase.FindAssets("t:Model", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => !wrapped.Contains(p));
                paths = prefabs.Concat(models).Distinct().Where(p => !(policy.excludedAssetRoots ?? new string[0])
                    .Any(r => (p + "/").StartsWith(r, StringComparison.OrdinalIgnoreCase))).OrderBy(p => p).ToArray();
                if (paths.Length == 0) throw new InvalidOperationException("В папке нет префабов или самостоятельных моделей для анализа.");
                targets = CatalogMarkingService.BlockPaths().Select(p =>
                {
                    var block = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    return new Target { path = p, name = block.name, cover = CatalogMarkingService.Cover(block).ToString(),
                        size = CatalogMarkingService.BoundsOf(new[] { block }).size };
                }).Where(t => Min(t.size) > .0001f).ToArray();
                if (targets.Length == 0) throw new InvalidOperationException("Не найдены эталоны LD_Alphabet.");
            }

            public bool Step()
            {
                if (index == paths.Length) return true;
                var entry = new Measurement { path = paths[index++] };
                measured.Add(entry);
                try
                {
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(entry.path);
                    if (source == null) throw new InvalidDataException("Не удалось загрузить модель.");
                    if (source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0)
                        throw new InvalidDataException("SkinnedMeshRenderer: анимированные модели пока пропускаются.");
                    var filters = CatalogMarkingService.Filters(new[] { source });
                    if (filters.Sum(f => (long)f.sharedMesh.vertexCount) > 500000)
                        throw new InvalidDataException("Более 500000 вершин: выберите отдельные элементы вместо целой сцены.");
                    var points = filters.SelectMany(f => f.sharedMesh.vertices.Select(v => f.transform.TransformPoint(v))).ToArray();
                    var original = PointBounds(points, Quaternion.identity).size;
                    entry.size = Array(original);
                    entry.triangles = filters.Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount)
                        .Sum(s => (int)(f.sharedMesh.GetIndexCount(s) / 3)));
                    var text = Normalize(entry.path + " " + string.Join(" ", source.GetComponentsInChildren<Transform>(true).Select(t => t.name))
                        + " " + string.Join(" ", filters.Select(f => AssetDatabase.GetAssetPath(f.sharedMesh))));
                    var required = (policy.materialMatchingRules ?? new Rule[0]).Where(r => r.nameContains.Any(n => text.Contains(Normalize(n))))
                        .Select(r => r.requiredCoverClass).Distinct().ToArray();
                    if (required.Length > 1) throw new InvalidDataException("Противоречащие правила материала: " + string.Join(", ", required));
                    entry.requiredCoverClass = required.FirstOrDefault();
                    if (Mathf.Max(original.x, Mathf.Max(original.y, original.z)) > 15 || Min(original) <= .001f)
                        throw new InvalidDataException("Габариты вне диапазона элементов укрытия: нулевая толщина или размер более 15 м.");
                    bool stackable = new[] { "box", "crate", "barrel", "sandbag", "pallet", "palet", "concrete_block" }.Any(text.Contains);
                    var repeats = stackable ? new[] { new[] { 1, 1, 1 }, new[] { 2, 1, 1 }, new[] { 3, 1, 1 },
                        new[] { 4, 1, 1 }, new[] { 1, 2, 1 }, new[] { 2, 2, 1 } } : new[] { new[] { 1, 1, 1 } };
                    var pitches = new[] { "pallet", "palet", "wooden_box", "wooden_crate" }.Any(text.Contains) ? new[] { 0, 90 } : new[] { 0 };
                    foreach (int pitch in pitches)
                    foreach (int turn in new[] { 0, 90 })
                    {
                        var rotated = PointBounds(points, Quaternion.Euler(pitch, turn, 0)).size;
                        foreach (var repeat in repeats)
                        foreach (var target in targets)
                        {
                            if (entry.requiredCoverClass != null && entry.requiredCoverClass != target.cover) continue;
                            var size = Vector3.Scale(rotated, new Vector3(repeat[0], repeat[1], repeat[2]));
                            float scale = Mathf.Clamp(Mathf.Pow(target.size.x / size.x * target.size.y / size.y * target.size.z / size.z, 1f / 3), .8f, 1.5f);
                            var actual = size * scale;
                            var error = RelativeError(actual, target.size);
                            // Поосевая подгонка максимум ±15% от равномерного масштаба, только как предложение.
                            var fit = new Vector3(target.size.x / size.x, target.size.y / size.y, target.size.z / size.z);
                            bool fitAxes = Max(RelativeError(fit, Vector3.one * scale)) <= .15f;
                            if (Max(error) > .25f && !fitAxes) continue;
                            float score = (error.x + error.y + error.z) / 3 + .025f * (repeat[0] * repeat[1] * repeat[2] - 1);
                            if (fitAxes && Max(error) > .03f) { actual = target.size; score += .04f; }
                            else fitAxes = false;
                            var flags = new List<string> { "Грубый подбор по габаритам; силуэт, щели, материал защиты и коллайдеры не проверены" };
                            if (entry.requiredCoverClass != null) flags.Add("Обязательный класс: " + entry.requiredCoverClass);
                            if (fitAxes) flags.Add("Предложена подгонка XYZ до 15% относительно равномерного масштаба");
                            if (scale > 1.2f) flags.Add("Увеличение модели более 20%");
                            if (repeat[0] * repeat[1] * repeat[2] > 1) flags.Add("Проверить опору и щели в составном укрытии");
                            var candidate = new Candidate {
                                id = "auto_" + AssetDatabase.AssetPathToGUID(target.path) + "_" + AssetDatabase.AssetPathToGUID(entry.path) + "_" + pitch + "_" + turn + "_" + string.Join("", repeat),
                                path = entry.path, model = source.name, rotation_x = pitch, rotation_y = turn, repeat_xyz = repeat,
                                uniform_scale = scale, scale_xyz = fitAxes ? Array(fit) : null,
                                actual_size = Array(actual), target_size = Array(target.size), score = score,
                                dimension_error = Max(RelativeError(actual, target.size)), unfitted_dimension_error = Max(error),
                                triangles = entry.triangles * repeat[0] * repeat[1] * repeat[2], warnings = flags.ToArray(),
                                variant = "Грубый подбор / " + (fitAxes ? "подгонка XYZ" : "масштаб " + scale.ToString("0.##", CultureInfo.InvariantCulture)) + " / " + repeat[0] * repeat[1] * repeat[2] + " шт."
                            };
                            target.candidates.Add(candidate);
                        }
                    }
                }
                catch (Exception exception)
                {
                    entry.error = exception.Message;
                    foreach (var target in targets) target.candidates.RemoveAll(c => c.path == entry.path);
                }
                // Ограничиваем память во время прохода, а не только в итоговом отчёте.
                foreach (var target in targets)
                    target.candidates = target.candidates.OrderBy(c => c.score).ThenBy(c => c.id, StringComparer.Ordinal)
                        .GroupBy(c => c.path).Select(g => g.First()).Take(3).ToList();
                return index == paths.Length;
            }

            public Report Save()
            {
                if (index != paths.Length || saved) throw new InvalidOperationException("Сканирование ещё не завершено или уже сохранено.");
                if (measured.All(m => m.error != null)) throw new InvalidOperationException("Ни одна модель не измерена. Прежние предложения сохранены. " + measured[0].error);
                var groups = targets.Select(t => new CandidateGroup { block = t.name, candidates = t.candidates.OrderBy(c => c.score)
                    .ThenBy(c => c.id, StringComparer.Ordinal).GroupBy(c => c.path).Select(g => g.First()).Take(3).ToArray() }).ToArray();
                string key = AssetDatabase.AssetPathToGUID(folder);
                string recipesPath = DataFolder + "/" + key + ".json";
                string csvPath = ReportFolder + "/" + key + ".csv";
                var report = new Report { folder = folder, generatedUtc = DateTime.UtcNow.ToString("o"), recipesPath = recipesPath,
                    csvPath = csvPath, scanned = measured.Count, measured = measured.Count(m => m.error == null),
                    skipped = measured.Count(m => m.error != null), matched = groups.Sum(g => g.candidates.Length), models = measured.ToArray(), groups = groups };
                Directory.CreateDirectory(ReportFolder);
                EnsureFolder(DataFolder);
                var csv = new StringBuilder("block,model,path,rotation_x,rotation_y,count,scale_xyz,dimension_error,triangles,warnings\n");
                foreach (var group in groups)
                foreach (var c in group.candidates)
                    csv.AppendLine(string.Join(",", new[] { group.block, c.model, c.path, c.rotation_x.ToString(), c.rotation_y.ToString(),
                        (c.repeat_xyz[0] * c.repeat_xyz[1] * c.repeat_xyz[2]).ToString(),
                        string.Join(" / ", (c.scale_xyz ?? new[] { c.uniform_scale, c.uniform_scale, c.uniform_scale }).Select(v => v.ToString("0.####", CultureInfo.InvariantCulture))),
                        c.dimension_error.ToString("0.####", CultureInfo.InvariantCulture), c.triangles.ToString(), string.Join("; ", c.warnings) }.Select(Quote)));
                File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(true));
                var json = JsonUtility.ToJson(report, true);
                File.WriteAllText(ReportFolder + "/" + key + ".json", json, new UTF8Encoding(false));
                // Атомарная замена рецептов: отменённый/ошибочный проход не очищает прежние соответствия.
                string pending = recipesPath + ".pending";
                File.WriteAllText(pending, json, new UTF8Encoding(false));
                if (File.Exists(recipesPath)) File.Replace(pending, recipesPath, null); else File.Move(pending, recipesPath);
                AssetDatabase.ImportAsset(recipesPath);
                saved = true;
                return report;
            }
        }

        public static AssetCandidateReviewScene.Catalog LoadRecipes()
        {
            var groups = new List<AssetCandidateReviewScene.Group>();
            if (Directory.Exists(DataFolder))
            foreach (var path in Directory.GetFiles(DataFolder, "*.json").OrderBy(p => p))
            {
                var report = JsonUtility.FromJson<Report>(File.ReadAllText(path));
                // Пак мог быть удалён; не переносим потерянные источники на стенд.
                if (!AssetDatabase.IsValidFolder(report.folder)) continue;
                groups.AddRange(report.groups.Select(g => new AssetCandidateReviewScene.Group { block = g.block,
                    candidates = g.candidates.Where(c => AssetDatabase.LoadAssetAtPath<GameObject>(c.path) != null)
                        .Cast<AssetCandidateReviewScene.Recipe>().ToArray() }));
            }
            return new AssetCandidateReviewScene.Catalog { groups = groups.GroupBy(g => g.block)
                .Select(g => new AssetCandidateReviewScene.Group { block = g.Key,
                    candidates = g.SelectMany(x => x.candidates).GroupBy(c => c.id).Select(x => x.Last()).ToArray() }).ToArray() };
        }

        private static Bounds PointBounds(Vector3[] points, Quaternion rotation)
        {
            var bounds = new Bounds(rotation * points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(rotation * point);
            return bounds;
        }
        private static float[] Array(Vector3 value) => new[] { value.x, value.y, value.z };
        private static Vector3 RelativeError(Vector3 actual, Vector3 target) => new Vector3(Mathf.Abs(actual.x / target.x - 1), Mathf.Abs(actual.y / target.y - 1), Mathf.Abs(actual.z / target.z - 1));
        private static float Min(Vector3 value) => Mathf.Min(value.x, Mathf.Min(value.y, value.z));
        private static float Max(Vector3 value) => Mathf.Max(value.x, Mathf.Max(value.y, value.z));
        private static string Normalize(string value) => value.ToLowerInvariant().Replace('\\', '/').Replace(' ', '_').Replace('-', '_');
        private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
