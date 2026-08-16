#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Mirror;

public class CleanupPreviews : EditorWindow
{
    [MenuItem("Tools/Cleanup Arsenal Previews")]
    public static void Clean()
    {
        int removedCount = 0;
        
        string[] prefabs = new[] {
            "Assets/Prefabs/Arsenal/StandardArsenalWall.prefab",
            "Assets/Prefabs/Arsenal/Slots/ShelfSlotPrefab Variant.prefab",
            "Assets/Prefabs/Arsenal/Slots/ShelfSlotPrefab.prefab",
            "Assets/Prefabs/Arsenal/Slots/FirearmSlotPrefab.prefab"
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
                            Debug.Log($"Removed NetworkIdentity from {t.name} in {path}");
                        }
                        
                        DestroyImmediate(t.gameObject, true);
                        modified = true;
                        Debug.Log($"Removed {t.name} in {path}");
                    }
                }
            }
        }

        var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
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
            Debug.Log($"Removed {sceneRemovedCount} preview objects from active scene.");
        }
        
        Debug.Log($"Cleanup complete. Removed {removedCount} preview network identities / objects.");
    }
}
#endif
