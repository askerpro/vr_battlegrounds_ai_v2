using UnityEditor;
using UnityEngine;
using UltimateXR.Avatar;
using UltimateXR.UI;
using System.Linq;

namespace VrBattlegrounds.EditorTools
{
    public class AvatarFingertipSetup : EditorWindow
    {
        [MenuItem("Tools/VR Battlegrounds/Avatars/Setup Avatar UI Fingertips")]
        private static void ShowWindow()
        {
            SetupFingertips();
        }

        private static void SetupFingertips()
        {
            int updatedCount = 0;
            
            // Allow setting up multiple selected avatars at once
            GameObject[] selectedObjects = Selection.gameObjects;
            
            if (selectedObjects.Length == 0)
            {
                Debug.Log("No objects selected, searching in preset folders for avatars...");
                string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Prefabs/Player" });
                selectedObjects = guids.Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
                
                if (selectedObjects.Length == 0)
                {
                    Debug.LogWarning("Please select one or more Avatar prefabs/objects in the Project or Hierarchy.");
                    return;
                }
            }

            foreach (var go in selectedObjects)
            {
                UxrAvatar avatar = go.GetComponent<UxrAvatar>();
                if (avatar == null)
                {
                    avatar = go.GetComponentInChildren<UxrAvatar>();
                }

                if (avatar == null)
                {
                    Debug.Log($"Skipping {go.name}: No UxrAvatar component found.");
                    continue;
                }

                bool modified = false;

                // Try to locate index finger tips. Typically they are named "index_03_l" or "Index_Tip_Left" or "LeftHandIndex4".
                // Since avatars can have varying bone rigs, we look for bones mapped as index fingers in Unity's Animator, or just by name.
                Animator animator = avatar.GetComponentInChildren<Animator>();
                Transform leftTip = null;
                Transform rightTip = null;

                if (animator != null && animator.isHuman)
                {
                    // Humanoid rig: we can get the distal joint of the index finger
                    Transform leftDistal = animator.GetBoneTransform(HumanBodyBones.LeftIndexDistal);
                    Transform rightDistal = animator.GetBoneTransform(HumanBodyBones.RightIndexDistal);

                    // We typically want the tip of the finger. If the distal bone has a child, that child is the actual physical tip.
                    if (leftDistal != null)
                    {
                        leftTip = leftDistal.childCount > 0 ? leftDistal.GetChild(0) : leftDistal;
                    }
                    if (rightDistal != null)
                    {
                        rightTip = rightDistal.childCount > 0 ? rightDistal.GetChild(0) : rightDistal;
                    }
                }

                // Fallback for Heavy_Soldier specifically or namings if Humanoid wasn't enough
                if (leftTip == null) leftTip = FindBoneRecursive(avatar.transform, "index_03_l", "Index_Tip_Left");
                if (rightTip == null) rightTip = FindBoneRecursive(avatar.transform, "index_03_r", "Index_Tip_Right");

                if (leftTip != null)
                {
                    modified |= CreateFingertip(leftTip);
                }
                else
                {
                    Debug.LogWarning($"Could not find Left Index Tip for {go.name}");
                }

                if (rightTip != null)
                {
                    modified |= CreateFingertip(rightTip);
                }
                else
                {
                    Debug.LogWarning($"Could not find Right Index Tip for {go.name}");
                }

                if (modified)
                {
                    EditorUtility.SetDirty(go);
                    updatedCount++;
                    Debug.Log($"Successfully setup UxrFingerTip for avatar: {go.name}");
                }
            }
            
            Debug.Log($"UI Fingertips setup complete. Updated {updatedCount} avatars.");
        }

        private static bool CreateFingertip(Transform fingerBone)
        {
            // Check if it already has a fingertip
            UxrFingerTip existing = fingerBone.GetComponentInChildren<UxrFingerTip>();
            if (existing != null)
            {
                return false; // Already setup
            }

            // Create a child object for the fingertip
            GameObject tipGo = new GameObject("UxrFingerTip");
            tipGo.transform.SetParent(fingerBone, false);
            tipGo.transform.localPosition = Vector3.zero;
            tipGo.transform.localRotation = Quaternion.identity;
            
            // The UxrFingerTip interactions rely on a "WorldDir" to check if the finger is pushing straight into UI.
            // Z-axis (forward) of the UxrFingerTip should align with the physical forward direction of the finger.
            // On a typical Mixamo rig (index_01 -> index_02 -> index_03), the local Y axis points down the finger towards the tip.
            // We can orient the tip so its forward (Z) matches the bone's Y, or calculate from parent->bone vector.
            
            if (fingerBone.parent != null)
            {
                Vector3 direction = fingerBone.position - fingerBone.parent.position;
                if (direction.sqrMagnitude > 0.001f)
                {
                    tipGo.transform.rotation = Quaternion.LookRotation(direction.normalized);
                }
            }

            // Move the touch point slightly ahead so the UI doesn't clip the mesh before registering
            tipGo.transform.position += tipGo.transform.forward * 0.02f; // 2 centimeters forward 

            // Add UltimateXR component
            var component = tipGo.AddComponent<UxrFingerTip>();

            return true; // Added successfully
        }

        private static Transform FindBoneRecursive(Transform root, params string[] names)
        {
            foreach (string name in names)
            {
                // Simple case-insensitive match
                if (root.name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return root;
                }
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindBoneRecursive(root.GetChild(i), names);
                if (found != null) return found;
            }

            return null;
        }
    }
}
