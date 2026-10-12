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
using VrBattlegrounds.Core;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    /// Бюджет детализации Lobby для Quest: ранние переходы LOD у декора Environment
    /// и Android-лимиты текстур Lobby. Глобальные настройки качества не меняет — только читает.
    /// </summary>
    public static class LobbyQuestDetailBudget
    {
        private const string Menu = "Tools/VR Battlegrounds/Lobby Decoration/";
        private const string ScenePath = "Assets/Scenes/Lobby.unity";
        private const string Report = "tasks/lobby-decoration/reports/implementation/quest-budget/";
        private const string QualityAsset = "ProjectSettings/QualitySettings.asset";
        private const string QuestQualityName = "Quest";
        private const string AndroidPlatform = "Android";

        /// <summary>Вертикальный FOV Quest 3, градусы.</summary>
        private const float QuestVerticalFov = 96f;

        /// <summary>Дистанции переходов LOD0→1, LOD1→2, LOD2→3; дальше — последняя ×1.6^k.</summary>
        private static readonly float[] TransitionDistances = { 8f, 16f, 30f };
        private const float FarStepFactor = 1.6f;
        private const float MaxThreshold = 0.999f;
        private const float MinGap = 0.0005f;

        private const int NearTextureLimit = 1024;
        private const int FarTextureLimit = 512;
        private static readonly string[] TextureFolders = { "Assets/Art/Lobby/", "Assets/Materials/Lobby/" };
        private const string NearZone = "ArmoryRangeZone";

        [MenuItem(Menu + "Quest: детализация по дистанции")]
        public static void ApplyLodDistances()
        {
            Scene scene = CheckScene();
            GameObject environment = Root(scene, "Environment");
            float lodBias = ReadQuestLodBias();
            float tanHalf = Mathf.Tan(QuestVerticalFov * 0.5f * Mathf.Deg2Rad);

            var lines = new List<string>
            {
                DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
                "Сцена: " + scene.path + "; lodBias(Quest)=" + lodBias.ToString(CultureInfo.InvariantCulture) +
                "; FOV верт.=" + QuestVerticalFov + "°; цели: " + string.Join("/", TransitionDistances) + " м, далее ×" + FarStepFactor,
            };
            var examples = new List<string>();

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Lobby Quest: детализация по дистанции");
            int group = Undo.GetCurrentGroup();

            int total = 0, changed = 0, singleLevel = 0, alreadyEarly = 0, transitionsChanged = 0;
            foreach (LODGroup lodGroup in environment.GetComponentsInChildren<LODGroup>(true))
            {
                total++;
                LOD[] lods = lodGroup.GetLODs();
                if (lods.Length < 2) { singleLevel++; continue; }

                Vector3 scale = lodGroup.transform.lossyScale;
                float worldSize = lodGroup.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                float[] before = lods.Select(l => l.screenRelativeTransitionHeight).ToArray();

                bool any = false;
                float upper = 1f;
                // Последний порог — отсечение (cull): не трогаем.
                for (int i = 0; i < lods.Length - 1; i++)
                {
                    float current = lods[i].screenRelativeTransitionHeight;
                    float target = worldSize * lodBias / (2f * TargetDistance(i) * tanHalf);
                    target = Mathf.Min(target, MaxThreshold, upper - MinGap);
                    if (target > current)
                    {
                        lods[i].screenRelativeTransitionHeight = target;
                        any = true;
                        transitionsChanged++;
                    }
                    upper = lods[i].screenRelativeTransitionHeight;
                }

                if (!StrictlyDecreasing(lods))
                    throw new InvalidOperationException("Нарушено строгое убывание порогов LOD: " + PathOf(lodGroup.transform));

                if (!any) { alreadyEarly++; continue; }

                Undo.RecordObject(lodGroup, "Lobby Quest LOD");
                lodGroup.SetLODs(lods);
                if (PrefabUtility.IsPartOfPrefabInstance(lodGroup))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(lodGroup);
                changed++;

                string line = PathOf(lodGroup.transform) + " size=" + F(worldSize) + " м: " + Join(before) + " → " +
                              Join(lods.Select(l => l.screenRelativeTransitionHeight).ToArray());
                lines.Add(line);
                if (examples.Count < 10) examples.Add(line);
            }

            Undo.CollapseUndoOperations(group);
            if (changed > 0) EditorSceneManager.MarkSceneDirty(scene);

            string summary = "LODGroup под Environment: " + total + "; изменено " + changed + " (переходов " + transitionsChanged +
                             "); уже раньше целей " + alreadyEarly + "; один уровень " + singleLevel;
            lines.Insert(2, summary);
            WriteReport("lod-distances.txt", lines);
            GameLog.Perf.Info("[LobbyQuestBudget] " + summary + ". Отчёт: " + Report + "lod-distances.txt");
        }

        [MenuItem(Menu + "Quest: лимит текстур дальних объектов")]
        public static void ApplyTextureLimits()
        {
            Scene scene = CheckScene();
            GameObject environment = Root(scene, "Environment");
            GameObject gameplay = Root(scene, "Gameplay");
            Transform nearZone = environment.transform.Find(NearZone)
                ?? throw new InvalidOperationException("Нет Environment/" + NearZone + ".");

            var near = new HashSet<string>();
            var protectedPaths = new HashSet<string>();
            var far = new HashSet<string>();

            foreach (Renderer renderer in nearZone.GetComponentsInChildren<Renderer>(true)) CollectTextures(renderer, near);
            foreach (Renderer renderer in gameplay.GetComponentsInChildren<Renderer>(true)) CollectTextures(renderer, near);
            foreach (Renderer renderer in environment.GetComponentsInChildren<Renderer>(true))
                if (!renderer.transform.IsChildOf(nearZone)) CollectTextures(renderer, far);
            // Прочие корни сцены не относятся ни к ближнему плану, ни к дальнему: их текстуры не уменьшаем.
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root != environment && root != gameplay)
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) CollectTextures(renderer, protectedPaths);

            far.ExceptWith(near);
            far.ExceptWith(protectedPaths);

            var lines = new List<string> { DateTime.Now.ToString("O", CultureInfo.InvariantCulture) };
            int nearSet = 0, farSet = 0, skipped = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in near.OrderBy(p => p))
                    if (Limit(path, NearTextureLimit, lines)) nearSet++; else skipped++;
                foreach (string path in far.OrderBy(p => p))
                    if (Limit(path, FarTextureLimit, lines)) farSet++; else skipped++;
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            string summary = "Текстуры Lobby (Android): " + NearTextureLimit + " → " + nearSet + ", " + FarTextureLimit + " → " + farSet +
                             "; без изменений " + skipped + " (уже в лимите); ближних всего " + near.Count + ", дальних " + far.Count;
            lines.Insert(1, summary);
            WriteReport("texture-limits.txt", lines);
            GameLog.Perf.Info("[LobbyQuestBudget] " + summary + ". Отчёт: " + Report + "texture-limits.txt");
        }

        private static float TargetDistance(int transition)
        {
            if (transition < TransitionDistances.Length) return TransitionDistances[transition];
            int extra = transition - TransitionDistances.Length + 1;
            return TransitionDistances[TransitionDistances.Length - 1] * Mathf.Pow(FarStepFactor, extra);
        }

        private static bool StrictlyDecreasing(LOD[] lods)
        {
            for (int i = 1; i < lods.Length; i++)
                if (lods[i].screenRelativeTransitionHeight >= lods[i - 1].screenRelativeTransitionHeight) return false;
            return true;
        }

        /// <summary>Только уменьшает Android-лимит; Standalone и прочие платформы не трогает.</summary>
        private static bool Limit(string path, int limit, List<string> lines)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return false;
            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(AndroidPlatform);
            if (settings.overridden && settings.maxTextureSize <= limit) return false;
            if (!settings.overridden)
            {
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                if (Mathf.Min(Mathf.Max(width, height), importer.maxTextureSize) <= limit) return false;
            }

            string before = settings.overridden ? settings.maxTextureSize + "/" + settings.format : "нет override";
            if (!settings.overridden || !IsAstc(settings.format)) settings.format = TextureImporterFormat.ASTC_6x6;
            settings.overridden = true;
            settings.maxTextureSize = limit;
            importer.SetPlatformTextureSettings(settings);
            importer.SaveAndReimport();
            lines.Add(limit + ": " + path + " (было " + before + ")");
            return true;
        }

        private static bool IsAstc(TextureImporterFormat format) => format.ToString().StartsWith("ASTC", StringComparison.Ordinal);

        private static void CollectTextures(Renderer renderer, HashSet<string> into)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                foreach (string property in material.GetTexturePropertyNames())
                {
                    Texture texture = material.GetTexture(property);
                    if (texture == null) continue;
                    string path = AssetDatabase.GetAssetPath(texture);
                    if (TextureFolders.Any(folder => path.StartsWith(folder, StringComparison.Ordinal))) into.Add(path);
                }
            }
        }

        /// <summary>lodBias уровня Quest из файла настроек — только чтение.</summary>
        private static float ReadQuestLodBias()
        {
            UnityEngine.Object asset = AssetDatabase.LoadAllAssetsAtPath(QualityAsset).FirstOrDefault()
                ?? throw new InvalidOperationException("Не загружен " + QualityAsset + ".");
            SerializedProperty levels = new SerializedObject(asset).FindProperty("m_QualitySettings");
            for (int i = 0; i < levels.arraySize; i++)
            {
                SerializedProperty level = levels.GetArrayElementAtIndex(i);
                if (level.FindPropertyRelative("name").stringValue == QuestQualityName)
                    return level.FindPropertyRelative("lodBias").floatValue;
            }
            throw new InvalidOperationException("В " + QualityAsset + " нет уровня качества " + QuestQualityName + ".");
        }

        private static Scene CheckScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Нужен свободный Editor вне Play/компиляции/prefab stage.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Откройте только " + ScenePath + ".");
            return scene;
        }

        private static GameObject Root(Scene scene, string name) =>
            scene.GetRootGameObjects().FirstOrDefault(r => r.name == name)
            ?? throw new InvalidOperationException("Нет корня " + name + " в " + scene.path + ".");

        private static void WriteReport(string file, List<string> lines)
        {
            Directory.CreateDirectory(Report);
            File.WriteAllLines(Report + file, lines, new UTF8Encoding(false));
        }

        private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static string Join(float[] values) => "[" + string.Join(", ", values.Select(F)) + "]";
    }
}
