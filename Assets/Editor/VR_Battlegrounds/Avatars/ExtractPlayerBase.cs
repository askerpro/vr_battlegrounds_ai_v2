using VrBattlegrounds.Core;
using UnityEngine;
using UnityEditor;

public static class ExtractPlayerBase
{
    public static void Execute()
    {
        string sourcePath = "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab";
        string targetPath = "Assets/Prefabs/Player/PlayerBase.prefab";

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (prefab == null) {
            GameLog.Player.Error("Cyborg prefab not found.");
            return;
        }

        GameObject go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
        go.name = "PlayerBase";

        Transform cyborg = go.transform.Find("Cyborg");
        if (cyborg != null) {
            Object.DestroyImmediate(cyborg.gameObject);
            GameLog.Player.Info("Deleted Cyborg mesh child.");
        }

        PrefabUtility.SaveAsPrefabAsset(go, targetPath);
        Object.DestroyImmediate(go);
        
        GameLog.Player.Info($"PlayerBase created successfully at {targetPath}");
    }
}
