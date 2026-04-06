using System.Collections.Generic;
using System.IO;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Editor;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;

namespace VRBattlegrounds.Editor
{
    public class HandPosesSetup
    {
        [MenuItem("Tools/VR Battlegrounds/Avatars/UXR Setup Wizard/6. Generate Default Poses")]
        public static void Execute()
        {
            GameObject avatarObj = Selection.activeGameObject;
            if (avatarObj == null) avatarObj = GameObject.Find("AutoSetupAvatarTarget");
            if (avatarObj == null)
            {
                var foundUxrAvatar = Object.FindObjectOfType<UxrAvatar>();
                if (foundUxrAvatar != null) avatarObj = foundUxrAvatar.gameObject;
            }
            if (avatarObj == null)
            {
                Debug.LogError("UXR Setup: Missing VR_Avatar_Ready in scene.");
                return;
            }

            UxrAvatar avatar = avatarObj.GetComponent<UxrAvatar>();
            if (avatar == null)
            {
                Debug.LogError("UXR Setup: Selected object is not a UxrAvatar.");
                return;
            }

            string avatarName = avatarObj.name.Replace("(Clone)", "").Replace("_Ready", "").Replace("VR_Avatar", "").Trim();
            if (string.IsNullOrEmpty(avatarName))
            {
                Object src = PrefabUtility.GetCorrespondingObjectFromSource(avatarObj);
                if (src != null) avatarName = src.name.Replace("_Ready", "").Trim();
            }
            if (string.IsNullOrEmpty(avatarName) || avatarName == "_Ready") avatarName = "CustomAvatar";

            string saveFolder = $"Assets/Art/Avatars/{avatarName}/HandPoses";

            if (!Directory.Exists(saveFolder))
            {
                Directory.CreateDirectory(saveFolder);
            }

            // Load presets
            string[] presetFiles = UxrEditorUtils.GetHandPosePresetFiles();
            List<UxrHandPoseAsset> newPoses = new List<UxrHandPoseAsset>();

            // Temporarily store the original hand poses
            UxrHandDescriptor originalLeftHand = new UxrHandDescriptor(avatar, UxrHandSide.Left);
            UxrHandDescriptor originalRightHand = new UxrHandDescriptor(avatar, UxrHandSide.Right);

            try
            {
                foreach (string file in presetFiles)
                {
                    if (AssetDatabase.GetMainAssetTypeAtPath(file) != typeof(UxrHandPoseAsset))
                        continue;

                    UxrHandPoseAsset srcAsset = AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(file);
                    if (srcAsset == null) continue;

                    string dstPath = $"{saveFolder}/{srcAsset.name}.asset";
                    
                    // Create new empty asset instead of basic instantiation, because we want it to be perfectly serialized
                    UxrHandPoseAsset dstAsset = ScriptableObject.CreateInstance<UxrHandPoseAsset>();
                    dstAsset.PoseType = srcAsset.PoseType;

                    // Match Left and Right fingers using Compute logic for Fixed/Blend poses
                    if (srcAsset.PoseType == UxrHandPoseType.Fixed)
                    {
                        UxrAvatarRig.UpdateHandUsingDescriptor(avatar, UxrHandSide.Left, srcAsset.HandDescriptorLeft);
                        UxrAvatarRig.UpdateHandUsingDescriptor(avatar, UxrHandSide.Right, srcAsset.HandDescriptorRight);
                        
                        dstAsset.HandDescriptorLeft = new UxrHandDescriptor(avatar, UxrHandSide.Left);
                        dstAsset.HandDescriptorRight = new UxrHandDescriptor(avatar, UxrHandSide.Right);
                    }
                    else if (srcAsset.PoseType == UxrHandPoseType.Blend)
                    {
                        UxrAvatarRig.UpdateHandUsingDescriptor(avatar, UxrHandSide.Left, srcAsset.HandDescriptorOpenLeft);
                        UxrAvatarRig.UpdateHandUsingDescriptor(avatar, UxrHandSide.Right, srcAsset.HandDescriptorOpenRight);
                        
                        dstAsset.HandDescriptorOpenLeft = new UxrHandDescriptor(avatar, UxrHandSide.Left);
                        dstAsset.HandDescriptorOpenRight = new UxrHandDescriptor(avatar, UxrHandSide.Right);

                        UxrAvatarRig.UpdateHandUsingDescriptor(avatar, UxrHandSide.Left, srcAsset.HandDescriptorClosedLeft);
                        UxrAvatarRig.UpdateHandUsingDescriptor(avatar, UxrHandSide.Right, srcAsset.HandDescriptorClosedRight);

                        dstAsset.HandDescriptorClosedLeft = new UxrHandDescriptor(avatar, UxrHandSide.Left);
                        dstAsset.HandDescriptorClosedRight = new UxrHandDescriptor(avatar, UxrHandSide.Right);
                    }

                    // Delete existing if any
                    if (File.Exists(dstPath))
                    {
                        AssetDatabase.DeleteAsset(dstPath);
                    }

                    AssetDatabase.CreateAsset(dstAsset, dstPath);
                    newPoses.Add(dstAsset);
                }

                AssetDatabase.SaveAssets();

                // Apply poses to the UxrAvatar component
                SerializedObject avatarSerialized = new SerializedObject(avatar);
                SerializedProperty handPosesProp = avatarSerialized.FindProperty("_handPoses");
                
                handPosesProp.ClearArray();

                UxrHandPoseAsset defaultPose = null;

                for (int i = 0; i < newPoses.Count; i++)
                {
                    handPosesProp.InsertArrayElementAtIndex(i);
                    handPosesProp.GetArrayElementAtIndex(i).objectReferenceValue = newPoses[i];

                    if (newPoses[i].name.ToLower().Contains("default"))
                    {
                        defaultPose = newPoses[i];
                    }
                }

                if (defaultPose != null)
                {
                    SerializedProperty defaultPoseProp = avatarSerialized.FindProperty("_defaultHandPose");
                    if (defaultPoseProp != null)
                    {
                        defaultPoseProp.objectReferenceValue = defaultPose;
                    }
                }

                avatarSerialized.ApplyModifiedProperties();

                // Restore object's original hand locations and mark dirty
                UxrAvatarRig.UpdateHandUsingDescriptor(avatar, UxrHandSide.Left, originalLeftHand);
                UxrAvatarRig.UpdateHandUsingDescriptor(avatar, UxrHandSide.Right, originalRightHand);

                // Re-apply Default Pose to hierarchy if available so the visual state matches the default
                if (defaultPose != null)
                {
                    if (defaultPose.PoseType == UxrHandPoseType.Fixed)
                    {
                        UxrAvatarRig.UpdateHandUsingDescriptor(avatar, UxrHandSide.Left, defaultPose.HandDescriptorLeft);
                        UxrAvatarRig.UpdateHandUsingDescriptor(avatar, UxrHandSide.Right, defaultPose.HandDescriptorRight);
                    }
                }

                // Map hand poses to controller events
                var avatarController = avatarObj.GetComponent<UltimateXR.Avatar.Controllers.UxrStandardAvatarController>();
                if (avatarController != null)
                {
                    SerializedObject controllerSerialized = new SerializedObject(avatarController);
                    SerializedProperty eventsProp = controllerSerialized.FindProperty("_listControllerEvents");
                    if (eventsProp != null)
                    {
                        eventsProp.ClearArray();

                        UxrHandPoseAsset pointingPose = newPoses.Find(p => p.name.ToLower().Contains("pointing"));
                        UxrHandPoseAsset grabPose = newPoses.Find(p => p.name.ToLower() == "grab");

                        if (pointingPose != null)
                        {
                            eventsProp.InsertArrayElementAtIndex(eventsProp.arraySize);
                            var element = eventsProp.GetArrayElementAtIndex(eventsProp.arraySize - 1);
                            element.FindPropertyRelative("_animationType").intValue = (int)UltimateXR.Avatar.Controllers.UxrAnimationType.LeftFingerPoint;
                            element.FindPropertyRelative("_buttons").intValue = (int)UltimateXR.Devices.UxrInputButtons.Button1;
                            element.FindPropertyRelative("_handPose").objectReferenceValue = pointingPose;

                            eventsProp.InsertArrayElementAtIndex(eventsProp.arraySize);
                            element = eventsProp.GetArrayElementAtIndex(eventsProp.arraySize - 1);
                            element.FindPropertyRelative("_animationType").intValue = (int)UltimateXR.Avatar.Controllers.UxrAnimationType.RightFingerPoint;
                            element.FindPropertyRelative("_buttons").intValue = (int)UltimateXR.Devices.UxrInputButtons.Button1;
                            element.FindPropertyRelative("_handPose").objectReferenceValue = pointingPose;
                        }

                        if (grabPose != null)
                        {
                            eventsProp.InsertArrayElementAtIndex(eventsProp.arraySize);
                            var element = eventsProp.GetArrayElementAtIndex(eventsProp.arraySize - 1);
                            element.FindPropertyRelative("_animationType").intValue = (int)UltimateXR.Avatar.Controllers.UxrAnimationType.LeftHandGrab;
                            element.FindPropertyRelative("_buttons").intValue = (int)UltimateXR.Devices.UxrInputButtons.Grip;
                            element.FindPropertyRelative("_handPose").objectReferenceValue = grabPose;

                            eventsProp.InsertArrayElementAtIndex(eventsProp.arraySize);
                            element = eventsProp.GetArrayElementAtIndex(eventsProp.arraySize - 1);
                            element.FindPropertyRelative("_animationType").intValue = (int)UltimateXR.Avatar.Controllers.UxrAnimationType.RightHandGrab;
                            element.FindPropertyRelative("_buttons").intValue = (int)UltimateXR.Devices.UxrInputButtons.Grip;
                            element.FindPropertyRelative("_handPose").objectReferenceValue = grabPose;
                        }

                        controllerSerialized.ApplyModifiedProperties();
                        EditorUtility.SetDirty(avatarController);
                    }
                }

                EditorUtility.SetDirty(avatarObj);
                
                // If it is a prefab instance, apply changes to prefab
                if (PrefabUtility.IsPartOfPrefabInstance(avatarObj))
                {
                    PrefabUtility.ApplyPrefabInstance(avatarObj, InteractionMode.AutomatedAction);
                }

                Debug.Log($"✅ [6/6] Hand Poses successfully configured! Generated {newPoses.Count} poses into {saveFolder} and applied them to {avatarObj.name}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error assigning hand poses: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                // Safety cleanup of temp hierarchy changes
                UxrAvatarRig.UpdateHandUsingDescriptor(avatar, UxrHandSide.Left, originalLeftHand);
                UxrAvatarRig.UpdateHandUsingDescriptor(avatar, UxrHandSide.Right, originalRightHand);
            }
        }
    }
}
