#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Mirror;
using VrBattlegrounds.Core;

public class CleanupPreviews : EditorWindow
{
    public static void Clean() => Clean(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

    public static void Clean(UnityEngine.SceneManagement.Scene activeScene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !activeScene.IsValid() || !activeScene.isLoaded)
            throw new System.InvalidOperationException("Очистка требует загруженную сцену вне Play Mode.");
        int removedCount = 0;
        
        string[] prefabs = new[] {
            "Assets/Prefabs/Arsenal/CommonOpenArsenalStation.prefab",
            "Assets/Prefabs/Arsenal/Slots/ShelfSlotPrefab Variant.prefab",
            "Assets/Prefabs/Arsenal/Slots/ShelfSlotPrefab.prefab",
            "Assets/Prefabs/Arsenal/Slots/FireArmSlotPrefab.prefab"
        };
        
        foreach (var path in prefabs)
        {
            using (var editScope = new PrefabUtility.EditPrefabContentsScope(path))
            {
                var root = editScope.prefabContentsRoot;
                var transforms = root.GetComponentsInChildren<Transform>(true);
                bool modified = false;
                foreach (var t in transforms)
                {
                    if (t == null) continue;
                    if (t.name == "__ItemPreview__" || t.name == "__MagPreview__")
                    {
                        var identity = t.GetComponent<NetworkIdentity>();
                        if (identity != null)
                        {
                            DestroyImmediate(identity, true);
                            modified = true;
                            removedCount++;
                            GameLog.Arsenal.Info($"Removed NetworkIdentity from {t.name} in {path}");
                        }
                        
                        DestroyImmediate(t.gameObject, true);
                        modified = true;
                        GameLog.Arsenal.Info($"Removed {t.name} in {path}");
                    }
                }
            }
        }

        var rootObjects = activeScene.GetRootGameObjects();
        int sceneRemovedCount = 0;
        foreach (var root in rootObjects)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (t == null) continue;
                if (t.name == "__ItemPreview__" || t.name == "__MagPreview__")
                {
                    DestroyImmediate(t.gameObject, true);
                    sceneRemovedCount++;
                }
            }
        }
        
        if (sceneRemovedCount > 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(activeScene);
            GameLog.Arsenal.Info($"Removed {sceneRemovedCount} preview objects from active scene.");
        }
        
        GameLog.Arsenal.Info($"Cleanup complete. Removed {removedCount} preview network identities / objects.");
    }
}
#endif
