using UnityEditor;
using UnityEngine;
using UltimateXR.Manipulation;

public class RefineBackPocket
{
    [MenuItem("Tools/VR Battlegrounds/Refine Back Pocket Proximity")]
    public static void Refine()
    {
        string prefabPath = "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab";
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);

        try
        {
            Transform spine02 = prefabRoot.transform.Find("Cyborg/CyborgRig/Spine01/Spine02");
            if (spine02 == null)
            {
                Debug.LogError("Could not find Spine02 at Cyborg/CyborgRig/Spine01/Spine02");
                return;
            }

            Transform anchorBack = spine02.Find("Anchor_Back");
            Transform proximityBackR = spine02.Find("Proximity_Back_R");

            if (anchorBack == null || proximityBackR == null)
            {
                Debug.LogError($"Missing sub-objects: anchorBack={anchorBack != null}, proximityBackR={proximityBackR != null}");
                return;
            }

            UxrGrabbableObjectAnchor anchor = anchorBack.GetComponent<UxrGrabbableObjectAnchor>();
            if (anchor == null)
            {
                Debug.LogError("UxrGrabbableObjectAnchor component missing on Anchor_Back");
                return;
            }

            // Use SerializedObject to set private fields safely
            SerializedObject so = new SerializedObject(anchor);
            so.FindProperty("_dropProximityTransformUseSelf").boolValue = false;
            so.FindProperty("_dropProximityTransform").objectReferenceValue = proximityBackR;
            so.FindProperty("_maxPlaceDistance").floatValue = 0.2f;
            so.ApplyModifiedProperties();

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            Debug.Log("Successfully refined Back Pocket Proximity on prefab.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }
}
