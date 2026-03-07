using UnityEngine;
using UnityEditor;
using VrBattlegrounds.Player.UI;

namespace VrBattlegrounds.EditorScripts
{
    public static class HUDInjector
    {
        [MenuItem("VrBattlegrounds/Tools/Inject HUD to Avatar")]
        public static void Inject()
        {
            string prefabPath = "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab";
            using (var editingScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                GameObject root = editingScope.prefabContentsRoot;
                
                // Add Manager
                PlayerHUDManager manager = root.GetComponent<PlayerHUDManager>();
                if (manager == null) manager = root.AddComponent<PlayerHUDManager>();

                // Find Camera
                Transform cameraTransform = root.transform.Find("Camera Controller/Camera");
                if (cameraTransform == null)
                {
                    Debug.LogError("[HUDInjector] Could not find Camera Controller/Camera in prefab.");
                    return;
                }

                // Create Container if not exists
                Transform existingContainer = cameraTransform.Find("HUDContainer");
                GameObject containerGo;
                if (existingContainer != null)
                {
                    containerGo = existingContainer.gameObject;
                }
                else
                {
                    containerGo = new GameObject("HUDContainer");
                    containerGo.transform.SetParent(cameraTransform, false);
                    
                    Canvas canvas = containerGo.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.WorldSpace;
                    
                    RectTransform rt = containerGo.GetComponent<RectTransform>();
                    rt.sizeDelta = new Vector2(800, 600);
                    rt.localScale = new Vector3(0.001f, 0.001f, 0.001f);
                    rt.localPosition = new Vector3(0f, -0.2f, 0.5f); // in front and slightly down
                    
                    containerGo.AddComponent<UnityEngine.UI.CanvasScaler>();
                    containerGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                }

                // Wire Reference
                SerializedObject serializedManager = new SerializedObject(manager);
                SerializedProperty containerProp = serializedManager.FindProperty("_hudContainer");
                if (containerProp != null)
                {
                    containerProp.objectReferenceValue = containerGo.GetComponent<RectTransform>();
                    serializedManager.ApplyModifiedProperties();
                }

                Debug.Log("[HUDInjector] HUD successfully injected into " + prefabPath);
            }
        }
    }
}
