using VrBattlegrounds.Core;
using UnityEngine;
using UnityEditor;

public static class InspectAvatar
{
    public static void Inspect()
    {
        string path = "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) return;
        
        GameLog.Player.Info("--- CYBORG CHILDREN ---");
        foreach(Transform child in prefab.transform) {
            GameLog.Player.Info($"- {child.name}");
        }
        
        // Find Camera
        Camera cam = prefab.GetComponentInChildren<Camera>(true);
        if (cam != null) {
            GameLog.Player.Info($"Camera found at: {AnimationUtility.CalculateTransformPath(cam.transform, prefab.transform)}");
        }
        GameLog.Player.Info("--- END ---");
    }
}
