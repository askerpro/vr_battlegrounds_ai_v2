using UnityEngine;
using UnityEditor;

public static class InspectAvatar
{
    [MenuItem("Tools/VR Battlegrounds/Avatars/Inspect Cyborg Avatar")]
    public static void Inspect()
    {
        string path = "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) return;
        
        Debug.Log("--- CYBORG CHILDREN ---");
        foreach(Transform child in prefab.transform) {
            Debug.Log($"- {child.name}");
        }
        
        // Find Camera
        Camera cam = prefab.GetComponentInChildren<Camera>(true);
        if (cam != null) {
            Debug.Log($"Camera found at: {AnimationUtility.CalculateTransformPath(cam.transform, prefab.transform)}");
        }
        Debug.Log("--- END ---");
    }
}
