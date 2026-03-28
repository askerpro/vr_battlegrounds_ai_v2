using UnityEngine;
using UnityEditor;

public static class ExtractPlayerBase
{
    [MenuItem("Tools/Extract Player Base")]
    public static void Execute()
    {
        string sourcePath = "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab";
        string targetPath = "Assets/Prefabs/Player/PlayerBase.prefab";

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (prefab == null) {
            Debug.LogError("Cyborg prefab not found.");
            return;
        }

        GameObject go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
        go.name = "PlayerBase";

        Transform cyborg = go.transform.Find("Cyborg");
        if (cyborg != null) {
            Object.DestroyImmediate(cyborg.gameObject);
            Debug.Log("Deleted Cyborg mesh child.");
        }

        PrefabUtility.SaveAsPrefabAsset(go, targetPath);
        Object.DestroyImmediate(go);
        
        Debug.Log($"PlayerBase created successfully at {targetPath}");
    }
}
