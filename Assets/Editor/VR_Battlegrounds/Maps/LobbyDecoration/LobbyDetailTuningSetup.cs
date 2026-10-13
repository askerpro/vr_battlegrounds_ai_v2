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
using VrBattlegrounds.DevTools;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    /// Подготовка подбора детализации Lobby в шлеме (<see cref="LobbyDetailTuner" />, решение пользователя
    /// 2026-10-13): возвращает полную детализацию среднего и дальнего планов (откат правила «дальний декор»
    /// для экземпляров префабов), даёт текстурам Lobby общую базу Android 512 ASTC 6×6 и группу mipmap limit
    /// по полосе ближайшего использования, ставит в сцену настройщик с дистанциями объектов.
    /// После выбора уровня пользователем правило переносится в сцену и импорт, настройщик удаляется.
    /// </summary>
    public static class LobbyDetailTuningSetup
    {
        private const string Menu = "Tools/VR Battlegrounds/Lobby Decoration/";
        private const string ScenePath = "Assets/Scenes/Lobby.unity";
        private const string NearZone = "ArmoryRangeZone";
        private const string TunerName = "DetailTuner";
        private const string Report = "tasks/lobby-decoration/reports/implementation/quest-budget/detail-tuning-setup.txt";
        private const string AndroidPlatform = "Android";
        private const int BaseTextureSize = 512;
        private static readonly string[] TextureFolders = { "Assets/Art/Lobby/", "Assets/Materials/Lobby/" };

        // Правило «дальний декор» действовало дальше 10 м от пола арены: только там откатываются его следы.
        private const float FarDecorDistance = 10f;

        [MenuItem(Menu + "Quest: подготовить подбор детализации")]
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Откройте только " + ScenePath + " вне Play Mode.");
            GameObject environment = scene.GetRootGameObjects().Single(r => r.name == "Environment");
            GameObject gameplay = scene.GetRootGameObjects().Single(r => r.name == "Gameplay");
            Transform nearZone = environment.transform.Find(NearZone) ?? throw new InvalidOperationException("Нет Environment/" + NearZone + ".");
            var arena = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PhysicalArenaDefinition>(true)).Single();
            if (!arena.TryFloorBounds(out Bounds floor)) throw new InvalidOperationException("Нет границ пола PhysicalArenaDefinition.");
            var log = new StringBuilder();
            log.AppendLine("Подготовка подбора детализации Lobby " + DateTime.Now.ToString("O", CultureInfo.InvariantCulture));

            RestoreFarDecor(environment.transform, nearZone, floor, log);
            LobbyQuestDetailBudget.ApplyLodDistances();
            AssignTextures(environment.transform, gameplay.transform, nearZone, floor, log);
            PlaceTuner(environment.transform, nearZone, floor, log);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllText(Report, log.ToString(), new UTF8Encoding(false));
            GameLog.Perf.Info("[LobbyDetailTuning] Подготовлено. Отчёт: " + Report);
        }

        // Откат следов «дальнего декора» у экземпляров префабов: LODGroup с урезанным списком уровней
        // и выключенные рендереры, включённые в источнике. Сцена-объекты без префаба не восстанавливаются.
        private static void RestoreFarDecor(Transform environment, Transform nearZone, Bounds floor, StringBuilder log)
        {
            int lods = 0, renderers = 0, sceneOnly = 0;
            foreach (LODGroup group in environment.GetComponentsInChildren<LODGroup>(true))
            {
                if (group.transform.IsChildOf(nearZone)) continue;
                LODGroup source = PrefabUtility.GetCorrespondingObjectFromSource(group);
                if (source == null) { if (group.lodCount == 1) sceneOnly++; continue; }
                if (source.lodCount <= group.lodCount) continue;
                PrefabUtility.RevertObjectOverride(group, InteractionMode.AutomatedAction);
                lods++;
            }
            foreach (Renderer renderer in environment.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.enabled || renderer.transform.IsChildOf(nearZone) || !renderer.gameObject.activeInHierarchy) continue;
                if (LobbyQuestDetailBudget.DistanceToFloor(floor, renderer.bounds.center) < FarDecorDistance) continue;
                Renderer source = PrefabUtility.GetCorrespondingObjectFromSource(renderer);
                if (source == null || !source.enabled) continue;
                var serialized = new SerializedObject(renderer);
                PrefabUtility.RevertPropertyOverride(serialized.FindProperty("m_Enabled"), InteractionMode.AutomatedAction);
                renderers++;
            }
            log.AppendLine("Возвращено LODGroup " + lods + ", рендереров " + renderers + "; LODGroup без префаба с одним уровнем (не восстановить) " + sceneOnly + ".");
        }

        // Все текстуры Lobby: Android 512 ASTC 6×6 (база подбора); группа mipmap limit — полоса ближайшего
        // использования вне ближнего плана, у текстур ближнего плана и Gameplay группы нет.
        private static void AssignTextures(Transform environment, Transform gameplay, Transform nearZone, Bounds floor, StringBuilder log)
        {
            var nearest = new Dictionary<string, float>(StringComparer.Ordinal);
            var near = new HashSet<string>(StringComparer.Ordinal);
            foreach (Renderer renderer in environment.GetComponentsInChildren<Renderer>(true))
            {
                bool isNear = renderer.transform.IsChildOf(nearZone);
                float distance = LobbyQuestDetailBudget.DistanceToFloor(floor, renderer.bounds.center);
                foreach (string path in TexturesOf(renderer))
                {
                    if (isNear) near.Add(path);
                    nearest[path] = nearest.TryGetValue(path, out float d) ? Mathf.Min(d, distance) : distance;
                }
            }
            foreach (Renderer renderer in gameplay.GetComponentsInChildren<Renderer>(true))
                foreach (string path in TexturesOf(renderer)) { near.Add(path); nearest[path] = 0f; }

            for (int band = 0; band < LobbyDetailTuner.BandEdges.Length; band++)
            {
                string name = LobbyDetailTuner.BandGroupPrefix + band;
                if (!TextureMipmapLimitGroups.HasGroup(name)) TextureMipmapLimitGroups.CreateGroup(name);
            }

            int changed = 0;
            var perBand = new int[LobbyDetailTuner.BandEdges.Length];
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (KeyValuePair<string, float> pair in nearest.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (!(AssetImporter.GetAtPath(pair.Key) is TextureImporter importer)) continue;
                    string group = "";
                    if (!near.Contains(pair.Key))
                    {
                        int band = BandOf(pair.Value);
                        group = LobbyDetailTuner.BandGroupPrefix + band;
                        perBand[band]++;
                    }
                    TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings(AndroidPlatform);
                    bool same = android.overridden && android.maxTextureSize == BaseTextureSize && android.format == TextureImporterFormat.ASTC_6x6
                                && importer.mipmapLimitGroupName == group && !importer.ignoreMipmapLimit;
                    if (same) continue;
                    android.overridden = true;
                    android.maxTextureSize = BaseTextureSize;
                    android.format = TextureImporterFormat.ASTC_6x6;
                    importer.SetPlatformTextureSettings(android);
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
            log.AppendLine("Текстуры Lobby: " + nearest.Count + " (ближнего плана " + near.Count + "), изменено " + changed + "; по полосам " +
                           string.Join(", ", perBand.Select((n, i) => LobbyDetailTuner.BandEdges[i] + "м:" + n)) + ".");
        }

        private static void PlaceTuner(Transform environment, Transform nearZone, Bounds floor, StringBuilder log)
        {
            float minStart = LobbyDetailTuner.MidStartSteps.Min();
            float maxSmall = LobbyDetailTuner.SmallCullSteps.Max();
            var lods = new List<LobbyDetailTuner.LodEntry>();
            var small = new List<LobbyDetailTuner.SmallEntry>();
            var inGroups = new HashSet<Renderer>();

            foreach (LODGroup group in environment.GetComponentsInChildren<LODGroup>(true))
            {
                Renderer[] all = group.GetLODs().SelectMany(l => l.renderers).Where(r => r != null).ToArray();
                foreach (Renderer r in all) inGroups.Add(r);
                if (all.Length == 0 || group.transform.IsChildOf(nearZone) || all.Any(LobbyQuestDetailBudget.IsNightFixture)) continue;
                Bounds bounds = all[0].bounds;
                foreach (Renderer r in all) bounds.Encapsulate(r.bounds);
                float distance = LobbyQuestDetailBudget.DistanceToFloor(floor, bounds.center);
                if (distance < minStart) continue;
                lods.Add(new LobbyDetailTuner.LodEntry { group = group, distance = distance });
                if (bounds.size.magnitude < maxSmall)
                    foreach (Renderer r in all)
                        small.Add(new LobbyDetailTuner.SmallEntry { renderer = r, distance = distance, size = bounds.size.magnitude });
            }
            foreach (Renderer renderer in environment.GetComponentsInChildren<Renderer>(true))
            {
                if (inGroups.Contains(renderer) || !renderer.enabled || renderer.transform.IsChildOf(nearZone) || LobbyQuestDetailBudget.IsNightFixture(renderer)) continue;
                float distance = LobbyQuestDetailBudget.DistanceToFloor(floor, renderer.bounds.center);
                float size = renderer.bounds.size.magnitude;
                if (distance < minStart || size >= maxSmall) continue;
                small.Add(new LobbyDetailTuner.SmallEntry { renderer = renderer, distance = distance, size = size });
            }

            Transform holder = environment.Find(TunerName);
            if (holder == null)
            {
                var go = new GameObject(TunerName);
                Undo.RegisterCreatedObjectUndo(go, "Lobby: подбор детализации");
                go.transform.SetParent(environment, false);
                holder = go.transform;
            }
            LobbyDetailTuner tuner = holder.GetComponent<LobbyDetailTuner>() ?? Undo.AddComponent<LobbyDetailTuner>(holder.gameObject);
            Undo.RecordObject(tuner, "Lobby: подбор детализации");
            tuner.SetData(lods.ToArray(), small.ToArray());
            EditorUtility.SetDirty(tuner);
            log.AppendLine("Настройщик Environment/" + TunerName + ": LODGroup " + lods.Count + ", мелких рендереров " + small.Count + ".");
        }

        private static int BandOf(float distance)
        {
            float[] edges = LobbyDetailTuner.BandEdges;
            for (int i = edges.Length - 1; i >= 0; i--)
                if (distance >= edges[i]) return i;
            return 0;
        }

        private static IEnumerable<string> TexturesOf(Renderer renderer)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                foreach (string property in material.GetTexturePropertyNames())
                {
                    Texture texture = material.GetTexture(property);
                    if (texture == null) continue;
                    string path = AssetDatabase.GetAssetPath(texture);
                    if (TextureFolders.Any(f => path.StartsWith(f, StringComparison.Ordinal))) yield return path;
                }
            }
        }
    }
}
