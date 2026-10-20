using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Performance
{
    /// <summary>
    /// Точечные правки сцены Lobby ради GPU-кадра (решение пользователя 2026-10-20):
    /// зеркало остаётся, но рисует отражение в 512² и только ближние 8 м; сторожевые вышки
    /// (LOD0 142k треугольников) переходят на последний LOD4 (8,9k) на всех дистанциях лобби.
    /// Меняет только загруженную сцену Lobby и сохраняет её; «Revert» возвращает прежние значения.
    /// </summary>
    public static class LobbyGpuSceneTweaks
    {
        const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
        const string MenuRoot = "VR Battlegrounds/Performance/Lobby GPU/";

        // Зеркало: прежние значения сцены (переопределения префаба MirrorDisplayClean) — 768 и 16 м.
        public const int MirrorTextureSize = 512;
        public const float MirrorDistance = 8f;
        const int MirrorTextureSizeBefore = 768;
        const float MirrorDistanceBefore = 16f;

        // Вышка: LOD4 выбирается при относительной высоте ниже 0,65 (игрок к вышкам не подходит ближе ~25 м).
        const string TowerMeshPrefix = "SM_WatchTower_01_LOD";
        static readonly float[] TowerHeights = { 0.95f, 0.85f, 0.75f, 0.65f };

        [MenuItem(MenuRoot + "Apply scene tweaks (mirror, watchtower LOD)")]
        static void ApplyMenu() => Apply();

        [MenuItem(MenuRoot + "Revert scene tweaks")]
        static void RevertMenu() => Revert();

        public static string Apply() => Run(revert: false);

        public static string Revert() => Run(revert: true);

        static string Run(bool revert)
        {
            Scene scene = SceneManager.GetSceneByPath(LobbyScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                GameLog.Perf.Warning("[LobbyGpuSceneTweaks] Откройте сцену Lobby.");
                return null;
            }

            var log = new List<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    // Только включённые зеркала: неактивный образец UXR (Scene/Rooms/.../SpawnRoom) не трогаем.
                    if (behaviour == null || behaviour.GetType().Name != "UxrPlanarReflectionUrp" || !behaviour.gameObject.activeInHierarchy) continue;
                    var so = new SerializedObject(behaviour);
                    SerializedProperty size = so.FindProperty("_textureSize");
                    SerializedProperty distance = so.FindProperty("_maxReflectionDistance");
                    size.intValue = revert ? MirrorTextureSizeBefore : MirrorTextureSize;
                    distance.floatValue = revert ? MirrorDistanceBefore : MirrorDistance;
                    so.ApplyModifiedProperties();
                    log.Add($"mirror {behaviour.name}: texture {size.intValue}, distance {distance.floatValue}");
                }

                foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(true))
                {
                    LOD[] lods = group.GetLODs();
                    if (lods.Length != TowerHeights.Length + 1 || !IsTower(lods[0])) continue;
                    if (revert)
                    {
                        if (PrefabUtility.IsPartOfPrefabInstance(group))
                            PrefabUtility.RevertObjectOverride(group, InteractionMode.AutomatedAction);
                        log.Add($"tower {group.name}: LOD из префаба");
                        continue;
                    }
                    Undo.RecordObject(group, "Lobby GPU tower LOD");
                    for (int i = 0; i < TowerHeights.Length; i++) lods[i].screenRelativeTransitionHeight = TowerHeights[i];
                    group.SetLODs(lods);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(group);
                    log.Add($"tower {group.name}: LOD4 ниже {TowerHeights[TowerHeights.Length - 1]:F2}");
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            string summary = string.Join("\n", log);
            GameLog.Perf.Info($"[LobbyGpuSceneTweaks] {(revert ? "Возвращено" : "Применено")}:\n{summary}");
            return summary;
        }

        static bool IsTower(LOD lod)
        {
            Renderer renderer = lod.renderers.FirstOrDefault(r => r != null);
            var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            return filter != null && filter.sharedMesh != null && filter.sharedMesh.name.StartsWith(TowerMeshPrefix);
        }
    }
}
