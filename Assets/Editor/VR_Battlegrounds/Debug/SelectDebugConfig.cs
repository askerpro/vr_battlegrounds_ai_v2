using UnityEditor;
using UnityEngine;
using VrBattlegrounds.DevTools;

namespace VrBattlegrounds.Editor
{
    public static class SelectDebugConfig
    {
        [MenuItem("Tools/VR Battlegrounds/Select Debug Config")]
        public static void SelectConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:DebugBootstrapConfig");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                DebugBootstrapConfig config = AssetDatabase.LoadAssetAtPath<DebugBootstrapConfig>(path);
                
                if (config != null)
                {
                    Selection.activeObject = config;
                    EditorGUIUtility.PingObject(config);
                    Debug.Log($"[DebugTools] Selected DebugBootstrapConfig at: {path}");
                }
                else
                {
                    Debug.LogWarning($"[DebugTools] Failed to load DebugBootstrapConfig at: {path}");
                }
            }
            else
            {
                Debug.LogWarning("[DebugTools] DebugBootstrapConfig asset not found in the project. Please create one.");
            }
        }
    }
}
