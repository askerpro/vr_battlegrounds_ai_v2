using UnityEngine;
using UnityEditor;
using VrBattlegrounds.Player.UI;

namespace VrBattlegrounds.EditorScripts
{
    public static class HUDInjector
    {
        [MenuItem("Tools/VR Battlegrounds/Avatars/Inject HUD to Selected Avatar")]
        public static void Inject()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select an Avatar GameObject.", "OK");
                return;
            }

            // Find Camera
            Camera camera = selected.GetComponentInChildren<Camera>(true);
            if (camera == null)
            {
                EditorUtility.DisplayDialog("Error", "Could not find a Camera in the selected avatar hierarchy.", "OK");
                return;
            }

            // Add Manager
            PlayerHUDManager manager = selected.GetComponent<PlayerHUDManager>();
            if (manager == null)
            {
                manager = selected.AddComponent<PlayerHUDManager>();
                Undo.RegisterCreatedObjectUndo(manager, "Add PlayerHUDManager");
            }

            Undo.SetCurrentGroupName("Inject HUD Container");
            int undoGroup = Undo.GetCurrentGroup();

            // Create Container if not exists
            Transform existingContainer = camera.transform.Find("HUDContainer");
            GameObject containerGo;
            if (existingContainer != null)
            {
                containerGo = existingContainer.gameObject;
            }
            else
            {
                containerGo = new GameObject("HUDContainer");
                Undo.RegisterCreatedObjectUndo(containerGo, "Create HUDContainer");
                
                containerGo.transform.SetParent(camera.transform, false);
                
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
                if (containerProp.objectReferenceValue != containerGo.GetComponent<RectTransform>())
                {
                    containerProp.objectReferenceValue = containerGo.GetComponent<RectTransform>();
                    serializedManager.ApplyModifiedProperties();
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            
            Debug.Log($"[HUDInjector] HUD successfully injected into {selected.name}");
            EditorUtility.DisplayDialog("Success", $"HUD Container injected into {selected.name}.", "OK");
        }

        [MenuItem("Tools/VR Battlegrounds/Avatars/Inject HUD to Selected Avatar", true)]
        private static bool InjectValidation()
        {
            return Selection.activeGameObject != null;
        }
    }
}
