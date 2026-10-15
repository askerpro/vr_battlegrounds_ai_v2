using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Maps.Detail;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    /// Редактор профиля детализации сцены (задача scene-detail): сбор записей для
    /// <see cref="SceneDetailApplier" />, группы mipmap limit текстур сцены, проверка слотов материалов,
    /// перенос сохранённого на шлеме профиля в ассет. Дистанция — по горизонтали от края пола
    /// <see cref="PhysicalArenaDefinition" /> до центра bounds объекта.
    /// </summary>
    public static class SceneDetailSetup
    {
        private const string Menu = "Tools/VR Battlegrounds/Detail/";
        private const string ApplierName = "SceneDetail";
        private const string ReportFolder = "Library/SceneDetail/";   // вне Git

        [MenuItem(Menu + "Собрать записи сцены")]
        public static void Collect()
        {
            Scene scene = CheckScene();
            Bounds floor = Floor(scene);
            SceneDetailProfile profile = LoadOrCreateProfile(scene);
            Transform[] exempt = ExemptRoots(scene, profile);
            // Записи не зависят от текущих границ: профиль может позднее приблизить средний план.
            const float minStart = 0f;
            var log = new StringBuilder("Сбор записей детализации " + scene.path + " " + DateTime.Now.ToString("s") + "\n");

            var lods = new List<SceneDetailApplier.LodEntry>();
            var renderers = new List<SceneDetailApplier.RendererEntry>();
            var inGroups = new HashSet<Renderer>();
            var nearestUse = new Dictionary<string, float>(StringComparer.Ordinal);
            var unmanagedUse = new HashSet<string>(StringComparer.Ordinal);

            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(true))
            {
                Renderer[] all = group.GetLODs().SelectMany(l => l.renderers).Where(r => r != null).ToArray();
                foreach (Renderer r in all) inGroups.Add(r);
                if (all.Length == 0 || IsExempt(group.transform, exempt) ||
                    all.Any(r => IsExempt(r.transform, exempt) || IsLightFixture(r))) continue;
                Bounds bounds = Encapsulate(all);
                float distance = DistanceToFloor(floor, bounds.center);
                if (distance < minStart) continue;
                lods.Add(new SceneDetailApplier.LodEntry { group = group, distance = distance });
                if (bounds.size.magnitude < SceneDetailProfile.MaxCullSize)
                    foreach (Renderer r in all)
                        renderers.Add(new SceneDetailApplier.RendererEntry { renderer = r, distance = distance, size = bounds.size.magnitude });
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                bool exemptRenderer = IsExempt(renderer.transform, exempt);
                float distance = DistanceToFloor(floor, renderer.bounds.center);
                foreach (string texture in Textures(renderer, profile))
                {
                    if (exemptRenderer || distance < minStart) unmanagedUse.Add(texture);
                    nearestUse[texture] = nearestUse.TryGetValue(texture, out float d) ? Mathf.Min(d, distance) : distance;
                }
                if (exemptRenderer || inGroups.Contains(renderer) || IsLightFixture(renderer) || distance < minStart) continue;
                float size = renderer.bounds.size.magnitude;
                if (size < SceneDetailProfile.MaxCullSize)
                    renderers.Add(new SceneDetailApplier.RendererEntry { renderer = renderer, distance = distance, size = size });
            }

            AssignTextureGroups(nearestUse, unmanagedUse, log);

            SceneDetailApplier applier = PlaceApplier(scene);
            Undo.RecordObject(applier, "Детализация: записи сцены");
            applier.SetData(profile, lods.ToArray(), renderers.ToArray());
            EditorUtility.SetDirty(applier);
            EditorSceneManager.MarkSceneDirty(scene);

            log.AppendLine($"LODGroup {lods.Count}, мелких рендереров {renderers.Count}; исключения: {string.Join(", ", profile.exemptPaths)}.");
            WriteReport(scene, "collect", log);
            GameLog.Perf.Info("[SceneDetail] " + log.ToString().Trim().Replace("\n", " | "));
        }

        [MenuItem(Menu + "Проверка слотов материалов (обрезать лишние)")]
        public static void TrimMaterialSlots()
        {
            Scene scene = CheckScene();
            var log = new StringBuilder("Слоты материалов " + scene.path + "\n");
            int trimmed = 0;
            Undo.IncrementCurrentGroup();
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                Material[] materials = renderer.sharedMaterials;
                if (mesh == null || materials.Length <= mesh.subMeshCount) continue;
                // Лишний слот рисует последний подмеш ещё раз чужим материалом: лишний проход и «розовые» куски.
                Undo.RecordObject(renderer, "Детализация: обрезка слотов материалов");
                renderer.sharedMaterials = materials.Take(mesh.subMeshCount).ToArray();
                if (PrefabUtility.IsPartOfPrefabInstance(renderer)) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                log.AppendLine($"{PathOf(renderer.transform)}: {materials.Length} → {mesh.subMeshCount} ({mesh.name})");
                trimmed++;
            }
            if (trimmed > 0) EditorSceneManager.MarkSceneDirty(scene);
            log.AppendLine("Обрезано рендереров: " + trimmed);
            WriteReport(scene, "material-slots", log);
            GameLog.Perf.Info("[SceneDetail] Обрезано слотов материалов у " + trimmed + " рендереров.");
        }

        [MenuItem(Menu + "Забрать профиль со шлема")]
        public static void PullFromDevice()
        {
            Scene scene = CheckScene();
            SceneDetailProfile profile = LoadOrCreateProfile(scene);
            string remote = "/sdcard/Android/data/" + PlayerSettings.applicationIdentifier + "/files/" +
                            SceneDetailDebugSection.SavedPath(scene.name).Replace('\\', '/');
            // Уникальный путь исключает применение старого JSON после неудачного adb pull.
            string local = Path.Combine(Path.GetTempPath(), "scene-detail-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                string output = RunAdb($"pull \"{remote}\" \"{local}\"");
                if (!File.Exists(local)) throw new InvalidOperationException("Профиль со шлема не получен: " + output);
                var imported = ScriptableObject.CreateInstance<SceneDetailProfile>();
                try
                {
                    // FromJsonOverwrite сохраняет прежние поля, если их нет в JSON.
                    // Маркеры позволяют отвергнуть пустой или неполный профиль до изменения ассета.
                    imported.midStart = imported.farStart = float.NaN;
                    imported.mid = imported.far = new SceneDetailProfile.PlaneRule
                    {
                        lodSkip = -1, textureBias = -1, cullSize = float.NaN
                    };
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(local), imported);
                    if (!IsFinite(imported.midStart) || !IsFinite(imported.farStart) ||
                        !IsComplete(imported.mid) || !IsComplete(imported.far))
                        throw new InvalidOperationException("JSON не содержит полного профиля детализации сцены.");
                    Undo.RecordObject(profile, "Детализация: профиль со шлема");
                    // Списки принадлежат редактору; с устройства переносятся только правила.
                    profile.midStart = Mathf.Max(0f, imported.midStart);
                    profile.farStart = Mathf.Max(profile.midStart, imported.farStart);
                    profile.mid = imported.mid.Clamped();
                    profile.far = imported.far.Clamped();
                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssetIfDirty(profile);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(imported);
                }
            }
            finally
            {
                if (File.Exists(local)) File.Delete(local);
            }
            GameLog.Perf.Info($"[SceneDetail] Профиль {AssetDatabase.GetAssetPath(profile)} обновлён со шлема: средний с {profile.midStart} м, дальний с {profile.farStart} м.");
        }

        // ---------- helpers ----------

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsComplete(SceneDetailProfile.PlaneRule rule) =>
            rule.lodSkip >= 0 && rule.textureBias >= 0 && IsFinite(rule.cullSize);

        private static Scene CheckScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Нужен свободный Editor вне Play/компиляции.");
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("Откройте сохранённую сцену.");
            return scene;
        }

        private static string ProfilePath(Scene scene) =>
            Path.Combine(Path.GetDirectoryName(scene.path), scene.name, scene.name + "DetailProfile.asset").Replace('\\', '/');

        private static SceneDetailProfile LoadOrCreateProfile(Scene scene)
        {
            string path = ProfilePath(scene);
            var profile = AssetDatabase.LoadAssetAtPath<SceneDetailProfile>(path);
            if (profile != null) return profile;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            profile = ScriptableObject.CreateInstance<SceneDetailProfile>();
            profile.exemptPaths = new[] { "Gameplay" };
            AssetDatabase.CreateAsset(profile, path);
            GameLog.Perf.Info("[SceneDetail] Создан профиль " + path + " (по умолчанию: дальний план — худший LOD).");
            return profile;
        }

        private static Bounds Floor(Scene scene)
        {
            PhysicalArenaDefinition arena = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<PhysicalArenaDefinition>(true)).FirstOrDefault()
                ?? throw new InvalidOperationException("В сцене нет PhysicalArenaDefinition: не от чего считать планы.");
            if (!arena.TryFloorBounds(out Bounds floor)) throw new InvalidOperationException("Нет границ пола PhysicalArenaDefinition.");
            return floor;
        }

        private static Transform[] ExemptRoots(Scene scene, SceneDetailProfile profile)
        {
            var result = new List<Transform>();
            foreach (string path in profile.exemptPaths)
            {
                string[] parts = path.Split('/');
                GameObject root = scene.GetRootGameObjects().FirstOrDefault(r => r.name == parts[0]);
                Transform t = root == null ? null : parts.Length == 1 ? root.transform : root.transform.Find(string.Join("/", parts.Skip(1)));
                if (t == null) GameLog.Perf.Warning("[SceneDetail] Исключение не найдено в сцене: " + path);
                else result.Add(t);
            }
            return result.ToArray();
        }

        private static bool IsExempt(Transform t, Transform[] exempt) => exempt.Any(t.IsChildOf);

        // Светильники остаются видимыми: свет запечён от них, без корпуса пятно света «висит в воздухе».
        private static bool IsLightFixture(Renderer renderer) =>
            renderer.GetComponentInParent<Light>(true) != null
            || renderer.sharedMaterials.Any(m => m != null && m.IsKeywordEnabled("_EMISSION"));

        private static Bounds Encapsulate(Renderer[] renderers)
        {
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        internal static float DistanceToFloor(Bounds floor, Vector3 point)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(point.x - floor.center.x) - floor.extents.x);
            float dz = Mathf.Max(0f, Mathf.Abs(point.z - floor.center.z) - floor.extents.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static IEnumerable<string> Textures(Renderer renderer, SceneDetailProfile profile)
        {
            if (profile.textureFolders.Length == 0) yield break;
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                foreach (string property in material.GetTexturePropertyNames())
                {
                    Texture texture = material.GetTexture(property);
                    if (texture == null) continue;
                    string path = AssetDatabase.GetAssetPath(texture);
                    if (profile.textureFolders.Any(f => path.StartsWith(f, StringComparison.Ordinal))) yield return path;
                }
            }
        }

        // Группа — полоса ближайшего использования; текстура ближнего плана или исключения — без группы.
        private static void AssignTextureGroups(Dictionary<string, float> nearestUse, HashSet<string> unmanaged, StringBuilder log)
        {
            float[] edges = SceneDetailApplier.BandEdges;
            for (int band = 0; band < edges.Length; band++)
                if (!TextureMipmapLimitGroups.HasGroup(SceneDetailApplier.BandGroupPrefix + band))
                    TextureMipmapLimitGroups.CreateGroup(SceneDetailApplier.BandGroupPrefix + band);

            int changed = 0;
            var perBand = new int[edges.Length];
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (KeyValuePair<string, float> pair in nearestUse)
                {
                    if (!(AssetImporter.GetAtPath(pair.Key) is TextureImporter importer)) continue;
                    string group = "";
                    if (!unmanaged.Contains(pair.Key))
                    {
                        int band = 0;
                        for (int i = edges.Length - 1; i >= 0; i--) if (pair.Value >= edges[i]) { band = i; break; }
                        group = SceneDetailApplier.BandGroupPrefix + band;
                        perBand[band]++;
                    }
                    if (importer.mipmapLimitGroupName == group && !importer.ignoreMipmapLimit) continue;
                    importer.mipmapLimitGroupName = group;
                    importer.ignoreMipmapLimit = false;
                    importer.SaveAndReimport();
                    changed++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            log.AppendLine($"Текстуры сцены: {nearestUse.Count}, без группы (ближний план/исключения) {unmanaged.Count}, изменено {changed}; по полосам " +
                           string.Join(", ", perBand.Select((n, i) => edges[i] + "м:" + n)) + ".");
        }

        private static SceneDetailApplier PlaceApplier(Scene scene)
        {
            SceneDetailApplier applier = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SceneDetailApplier>(true)).FirstOrDefault();
            if (applier != null) return applier;
            GameObject environment = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Environment");
            var go = new GameObject(ApplierName);
            Undo.RegisterCreatedObjectUndo(go, "Детализация: применитель");
            if (environment != null) go.transform.SetParent(environment.transform, false);
            return Undo.AddComponent<SceneDetailApplier>(go);
        }

        private static string RunAdb(string arguments)
        {
            string sdk = EditorPrefs.GetString("AndroidSdkRoot");
            if (string.IsNullOrEmpty(sdk))
                sdk = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/SDK");
            string adb = Path.Combine(sdk, "platform-tools", Application.platform == RuntimePlatform.WindowsEditor ? "adb.exe" : "adb");
            if (!File.Exists(adb)) adb = "adb";
            var start = new ProcessStartInfo(adb, arguments) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using (Process process = Process.Start(start))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60000))
                {
                    process.Kill(); // Только запущенная этой командой операция adb.
                    process.WaitForExit();
                    throw new TimeoutException("Операция adb не завершилась за 60 секунд.");
                }
                string output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Операция adb завершилась с кодом " + process.ExitCode + ": " + output.Trim());
                return output.Trim();
            }
        }

        private static void WriteReport(Scene scene, string kind, StringBuilder log)
        {
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllText(ReportFolder + scene.name + "-" + kind + ".txt", log.ToString(), new UTF8Encoding(false));
        }

        private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    }
}
