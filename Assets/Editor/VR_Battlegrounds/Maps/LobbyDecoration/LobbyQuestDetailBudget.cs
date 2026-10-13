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

        // Бюджет GPU Lobby (решение пользователя 2026-10-13, «в ущерб картинке»): разбивка performance показала
        // упор в чтение текстур — все текстуры Lobby на Android сжаты ASTC, ближние 512, дальние 256.
        private const int NearTextureLimit = 512;
        private const int FarTextureLimit = 256;
        private const TextureImporterFormat NearFormat = TextureImporterFormat.ASTC_6x6;
        private const TextureImporterFormat FarFormat = TextureImporterFormat.ASTC_8x8;
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

        // Дальний декор (решение пользователя 2026-10-13, бюджет GPU): дальше FarDistance от края пола арены
        // объект с LOD показывает только последний уровень; мелочь дальше DropDistance не рисуется совсем.
        // Стрельбище (NearZone) и светильники с ночной эмиссией не трогаются.
        private const float FarDistance = 10f;
        private const float DropDistance = 15f;
        private const float SmallSize = 1.2f;
        private const string NightMaterialPrefix = "LobbyNight";

        [MenuItem(Menu + "Quest: дальний декор — худший LOD, мелочь убрать")]
        public static void ApplyFarDecor()
        {
            Scene scene = CheckScene();
            GameObject environment = Root(scene, "Environment");
            Transform nearZone = environment.transform.Find(NearZone)
                ?? throw new InvalidOperationException("Нет Environment/" + NearZone + ".");
            var arena = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LevelDesign.PhysicalArenaDefinition>(true)).Single();
            if (!arena.TryFloorBounds(out Bounds floor)) throw new InvalidOperationException("Нет границ пола PhysicalArenaDefinition.");

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Lobby Quest: дальний декор");
            int group = Undo.GetCurrentGroup();
            var lines = new List<string> { DateTime.Now.ToString("O", CultureInfo.InvariantCulture) };
            int lastLodOnly = 0, droppedGroups = 0, droppedRenderers = 0;

            var lodRenderers = new HashSet<Renderer>();
            foreach (LODGroup lodGroup in environment.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = lodGroup.GetLODs();
                Renderer[] all = lods.SelectMany(l => l.renderers).Where(r => r != null).ToArray();
                foreach (Renderer r in all) lodRenderers.Add(r);
                if (all.Length == 0 || lodGroup.transform.IsChildOf(nearZone) || !lodGroup.gameObject.activeInHierarchy || all.Any(IsNightFixture)) continue;

                Bounds bounds = all[0].bounds;
                foreach (Renderer r in all) bounds.Encapsulate(r.bounds);
                float distance = DistanceToFloor(floor, bounds.center);
                if (distance < FarDistance) continue;

                if (bounds.size.magnitude < SmallSize && distance >= DropDistance)
                {
                    foreach (Renderer r in all) Disable(r);
                    droppedGroups++;
                    lines.Add("убран " + PathOf(lodGroup.transform) + " d=" + F(distance));
                    continue;
                }
                if (lods.Length < 2) continue;

                for (int i = 0; i < lods.Length - 1; i++)
                    foreach (Renderer r in lods[i].renderers)
                        if (r != null && !lods[lods.Length - 1].renderers.Contains(r)) Disable(r);
                Undo.RecordObject(lodGroup, "Lobby Quest: дальний декор");
                LOD last = lods[lods.Length - 1];
                lodGroup.SetLODs(new[] { new LOD(last.screenRelativeTransitionHeight, last.renderers) });
                if (PrefabUtility.IsPartOfPrefabInstance(lodGroup)) PrefabUtility.RecordPrefabInstancePropertyModifications(lodGroup);
                lastLodOnly++;
            }

            foreach (Renderer renderer in environment.GetComponentsInChildren<Renderer>(true))
            {
                if (lodRenderers.Contains(renderer) || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                    || renderer.transform.IsChildOf(nearZone) || IsNightFixture(renderer)) continue;
                float distance = DistanceToFloor(floor, renderer.bounds.center);
                if (distance < DropDistance || renderer.bounds.size.magnitude >= SmallSize) continue;
                Disable(renderer);
                droppedRenderers++;
                lines.Add("убран " + PathOf(renderer.transform) + " d=" + F(distance));
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            string summary = "Дальний декор: только последний LOD у " + lastLodOnly + " LODGroup; убрано мелких LODGroup " + droppedGroups +
                             ", отдельных рендереров " + droppedRenderers + " (дальше " + FarDistance + "/" + DropDistance + " м от пола арены, мельче " + SmallSize + " м).";
            lines.Insert(1, summary);
            WriteReport("far-decor.txt", lines);
            GameLog.Perf.Info("[LobbyQuestBudget] " + summary + " Отчёт: " + Report + "far-decor.txt");
        }

        private static void Disable(Renderer renderer)
        {
            if (!renderer.enabled) return;
            Undo.RecordObject(renderer, "Lobby Quest: дальний декор");
            renderer.enabled = false;
            if (PrefabUtility.IsPartOfPrefabInstance(renderer)) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }

        internal static bool IsNightFixture(Renderer renderer) =>
            renderer.sharedMaterials.Any(m => m != null && m.name.StartsWith(NightMaterialPrefix, StringComparison.Ordinal));

        // Горизонтальное расстояние от точки до прямоугольника пола арены.
        internal static float DistanceToFloor(Bounds floor, Vector3 point)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(point.x - floor.center.x) - floor.extents.x);
            float dz = Mathf.Max(0f, Mathf.Abs(point.z - floor.center.z) - floor.extents.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
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
                    if (Limit(path, NearTextureLimit, NearFormat, lines)) nearSet++; else skipped++;
                foreach (string path in far.OrderBy(p => p))
                    if (Limit(path, FarTextureLimit, FarFormat, lines)) farSet++; else skipped++;
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

        /// <summary>
        /// Android: размер не больше лимита и ASTC не мельче заданного блока (уже более грубый ASTC сохраняется).
        /// Standalone и прочие платформы не трогает.
        /// </summary>
        private static bool Limit(string path, int limit, TextureImporterFormat format, List<string> lines)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return false;
            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(AndroidPlatform);
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            int size = settings.overridden ? settings.maxTextureSize : importer.maxTextureSize;
            bool sizeOk = Mathf.Min(Mathf.Max(width, height), size) <= limit;
            bool formatOk = settings.overridden && IsAstc(settings.format) && AstcBlock(settings.format) >= AstcBlock(format);
            if (sizeOk && formatOk) return false;

            string before = settings.overridden ? settings.maxTextureSize + "/" + settings.format : "нет override";
            if (!formatOk) settings.format = format;
            settings.overridden = true;
            settings.maxTextureSize = Mathf.Min(size, limit);
            importer.SetPlatformTextureSettings(settings);
            importer.SaveAndReimport();
            lines.Add(limit + ": " + path + " (было " + before + ")");
            return true;
        }

        private static bool IsAstc(TextureImporterFormat format) => format.ToString().StartsWith("ASTC", StringComparison.Ordinal);

        // Сторона блока ASTC_NxN (больше — грубее и дешевле); HDR и прочие варианты — по той же цифре.
        private static int AstcBlock(TextureImporterFormat format)
        {
            string digits = new string(format.ToString().SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, out int block) ? block : 0;
        }

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
