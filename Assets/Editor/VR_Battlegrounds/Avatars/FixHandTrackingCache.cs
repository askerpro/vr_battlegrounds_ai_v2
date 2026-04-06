using UnityEngine;
using UnityEditor;
using UltimateXR.Devices;
using UltimateXR.Avatar;

public static class FixHandTrackingCache
{
    [MenuItem("Tools/VR Battlegrounds/Avatars/Fix Hand Tracking")]
    public static void Execute()
    {
        string[] prefabs = new[] {
            "Assets/Prefabs/Player/PlayerBase.prefab",
            "Assets/Prefabs/Player/Heavy_Soldier_Base_Avatar.prefab",
            "Assets/Prefabs/Player/Military_Soldier_Base_Avatar.prefab",
            "Assets/Prefabs/Player/Spy_Base_Avatar.prefab",
            "Assets/Prefabs/Player/Military_Cap_Base_Avatar.prefab"
        };

        foreach (var path in prefabs)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            using (var editingScope = new PrefabUtility.EditPrefabContentsScope(path))
            {
                UxrHandTracking[] trackers = editingScope.prefabContentsRoot.GetComponentsInChildren<UxrHandTracking>(true);
                int count = 0;
                foreach (var tracker in trackers)
                {
                    var so = new SerializedObject(tracker);
                    
                    var leftProp = so.FindProperty("_leftCalibrationData");
                    if (leftProp != null) leftProp.ClearArray();
                    
                    var rightProp = so.FindProperty("_rightCalibrationData");
                    if (rightProp != null) rightProp.ClearArray();
                    
                    so.ApplyModifiedProperties();
                    count++;
                }

                if (count > 0)
                {
                    Debug.Log($"Cleared calibration data on {count} UxrHandTracking components in {path}");
                }
            }
        }
        
        AssetDatabase.SaveAssets();
    }
}
