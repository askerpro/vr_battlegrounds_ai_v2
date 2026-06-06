using UnityEngine;
using UnityEditor;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using System.Collections.Generic;

namespace VR_Battlegrounds.Editor.Avatars
{
    public class AvatarHandAligner : EditorWindow
    {
        private UxrAvatar sourceTemplateAvatar;
        private UxrAvatar targetCustomAvatar;

        private bool mirrorRotation = true;
        private bool mirrorPosition = false;
        private Vector3 rotationMultiplier = new Vector3(1f, -1f, -1f);
        private Vector3 positionMultiplier = new Vector3(-1f, 1f, 1f);

        private enum MirrorDirection { RightToLeft, LeftToRight }
        private MirrorDirection mirrorDirection = MirrorDirection.RightToLeft;

        [MenuItem("Tools/VR Battlegrounds/Avatars/Avatar Finger Configurator")]
        public static void ShowWindow()
        {
            GetWindow<AvatarHandAligner>("Finger Configurator");
        }

        private void OnGUI()
        {
            GUILayout.Label("Auto-Fit Fingers to Template", EditorStyles.boldLabel);
            sourceTemplateAvatar = (UxrAvatar)EditorGUILayout.ObjectField("Template Avatar (UXR)", sourceTemplateAvatar, typeof(UxrAvatar), true);
            targetCustomAvatar = (UxrAvatar)EditorGUILayout.ObjectField("Target Avatar (Custom)", targetCustomAvatar, typeof(UxrAvatar), true);

            if (GUILayout.Button("Auto-Fit target fingers to template (Left Hand)"))
            {
                if (CheckAvatarsAssigned()) AutoFitTarget(UxrHandSide.Left);
            }
            if (GUILayout.Button("Auto-Fit target fingers to template (Right Hand)"))
            {
                if (CheckAvatarsAssigned()) AutoFitTarget(UxrHandSide.Right);
            }
            if (GUILayout.Button("Auto-Fit target fingers to template (Both Hands)"))
            {
                if (CheckAvatarsAssigned())
                {
                    AutoFitTarget(UxrHandSide.Left);
                    AutoFitTarget(UxrHandSide.Right);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.Space();

            GUILayout.Label("Mirror Finger Poses", EditorStyles.boldLabel);
            targetCustomAvatar = (UxrAvatar)EditorGUILayout.ObjectField("Target Avatar for Mirror", targetCustomAvatar, typeof(UxrAvatar), true);
            mirrorDirection = (MirrorDirection)EditorGUILayout.EnumPopup("Mirror Direction", mirrorDirection);
            
            mirrorRotation = EditorGUILayout.BeginToggleGroup("Mirror Local Rotation", mirrorRotation);
            rotationMultiplier = EditorGUILayout.Vector3Field("Rotation Axis Multipliers", rotationMultiplier);
            EditorGUILayout.EndToggleGroup();

            mirrorPosition = EditorGUILayout.BeginToggleGroup("Mirror Local Position", mirrorPosition);
            positionMultiplier = EditorGUILayout.Vector3Field("Position Axis Multipliers", positionMultiplier);
            EditorGUILayout.EndToggleGroup();

            if (GUILayout.Button("Mirror Hand Pose"))
            {
                if (targetCustomAvatar != null)
                {
                    MirrorPoses();
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Target Avatar is not assigned.", "OK");
                }
            }
        }

        private bool CheckAvatarsAssigned()
        {
            if (sourceTemplateAvatar == null || targetCustomAvatar == null)
            {
                EditorUtility.DisplayDialog("Error", "Assign both Template and Target avatars.", "OK");
                return false;
            }
            return true;
        }

        private void AutoFitTarget(UxrHandSide handSide)
        {
            UxrAvatarHand sourceHand = handSide == UxrHandSide.Left ? sourceTemplateAvatar.AvatarRig.LeftArm.Hand : sourceTemplateAvatar.AvatarRig.RightArm.Hand;
            UxrAvatarHand targetHand = handSide == UxrHandSide.Left ? targetCustomAvatar.AvatarRig.LeftArm.Hand : targetCustomAvatar.AvatarRig.RightArm.Hand;

            if (sourceHand == null || targetHand == null)
            {
                Debug.LogError("Source or Target hand is missing from the rig.");
                return;
            }

            Undo.RecordObjects(targetCustomAvatar.GetComponentsInChildren<Transform>(), "Auto-Fit Fingers");

            Quaternion sourcePalmRot = GetAnatomicalPalmRotation(sourceHand);
            Quaternion targetPalmRot = GetAnatomicalPalmRotation(targetHand);

            FitFinger(targetHand.Thumb, sourceHand.Thumb, targetPalmRot, sourcePalmRot);
            FitFinger(targetHand.Index, sourceHand.Index, targetPalmRot, sourcePalmRot);
            FitFinger(targetHand.Middle, sourceHand.Middle, targetPalmRot, sourcePalmRot);
            FitFinger(targetHand.Ring, sourceHand.Ring, targetPalmRot, sourcePalmRot);
            FitFinger(targetHand.Little, sourceHand.Little, targetPalmRot, sourcePalmRot);
            
            Debug.Log($"Auto-Fitted {handSide} Hand Finger Bones using Anatomical Palm Frame.");
        }

        private Quaternion GetAnatomicalPalmRotation(UxrAvatarHand hand)
        {
            if (hand.Middle == null || hand.Middle.Proximal == null ||
                hand.Index == null || hand.Index.Proximal == null ||
                hand.Little == null || hand.Little.Proximal == null ||
                hand.Wrist == null)
            {
                return hand.Wrist.rotation; // fallback
            }

            Vector3 forward = (hand.Middle.Proximal.position - hand.Wrist.position).normalized;
            Vector3 indexToLittle = (hand.Little.Proximal.position - hand.Index.Proximal.position).normalized;
            Vector3 up = Vector3.Cross(forward, indexToLittle).normalized;
            
            if (forward == Vector3.zero || up == Vector3.zero) return hand.Wrist.rotation;

            return Quaternion.LookRotation(forward, up);
        }

        private void FitFinger(UxrAvatarFinger customFinger, UxrAvatarFinger templateFinger, Quaternion customPalmRot, Quaternion templatePalmRot)
        {
            if (customFinger == null || templateFinger == null) return;

            // Metacarpal 
            if (customFinger.Metacarpal)
            {
                Transform cChild = GetNextBone(customFinger, 0);
                Transform tBone = templateFinger.Metacarpal != null ? templateFinger.Metacarpal : templateFinger.Proximal;
                Transform tChild = GetNextBone(templateFinger, templateFinger.Metacarpal != null ? 0 : 1);
                AlignSegment(customFinger.Metacarpal, cChild, tBone, tChild, customPalmRot, templatePalmRot);
            }
            
            // Proximal
            if (customFinger.Proximal)
            {
                Transform cChild = GetNextBone(customFinger, 1);
                Transform tBone = templateFinger.Proximal;
                Transform tChild = GetNextBone(templateFinger, 1);
                AlignSegment(customFinger.Proximal, cChild, tBone, tChild, customPalmRot, templatePalmRot);
            }

            // Intermediate
            if (customFinger.Intermediate)
            {
                Transform cChild = GetNextBone(customFinger, 2);
                Transform tBone = templateFinger.Intermediate;
                Transform tChild = GetNextBone(templateFinger, 2);
                AlignSegment(customFinger.Intermediate, cChild, tBone, tChild, customPalmRot, templatePalmRot);
            }

            // Distal
            if (customFinger.Distal)
            {
                Transform cChild = GetNextBone(customFinger, 3);
                Transform tBone = templateFinger.Distal;
                Transform tChild = GetNextBone(templateFinger, 3);
                AlignSegment(customFinger.Distal, cChild, tBone, tChild, customPalmRot, templatePalmRot);
            }
        }

        private Transform GetNextBone(UxrAvatarFinger finger, int startIndex)
        {
            if (startIndex < 1 && finger.Proximal) return finger.Proximal;
            if (startIndex < 2 && finger.Intermediate) return finger.Intermediate;
            if (startIndex < 3 && finger.Distal) return finger.Distal;
            if (finger.Distal && finger.Distal.childCount > 0) return finger.Distal.GetChild(0);
            return null;
        }

        private void AlignSegment(Transform cBone, Transform cChild, Transform tBone, Transform tChild, Quaternion customPalmRot, Quaternion templatePalmRot)
        {
            if (cBone == null || cChild == null || tBone == null || tChild == null) return;

            Vector3 currentDir = (cChild.position - cBone.position).normalized;
            Vector3 templateWorldDir = (tChild.position - tBone.position).normalized;

            if (currentDir != Vector3.zero && templateWorldDir != Vector3.zero)
            {
                // Convert template world direction to template palm's local space
                Vector3 templateLocalDir = Quaternion.Inverse(templatePalmRot) * templateWorldDir;
                
                // Convert local direction back to custom palm's world space
                Vector3 targetWorldDir = customPalmRot * templateLocalDir;

                Quaternion rot = Quaternion.FromToRotation(currentDir, targetWorldDir);
                cBone.rotation = rot * cBone.rotation;
            }
        }

        private void MirrorPoses()
        {
            UxrAvatarHand sourceHand = mirrorDirection == MirrorDirection.RightToLeft ? targetCustomAvatar.AvatarRig.RightArm.Hand : targetCustomAvatar.AvatarRig.LeftArm.Hand;
            UxrAvatarHand targetHand = mirrorDirection == MirrorDirection.RightToLeft ? targetCustomAvatar.AvatarRig.LeftArm.Hand : targetCustomAvatar.AvatarRig.RightArm.Hand;

            if (sourceHand == null || targetHand == null)
            {
                Debug.LogError("Source or Target hand is missing from the target custom avatar rig.");
                return;
            }

            Undo.RecordObjects(targetCustomAvatar.GetComponentsInChildren<Transform>(), "Mirror Finger Poses");

            MirrorFinger(sourceHand.Thumb, targetHand.Thumb);
            MirrorFinger(sourceHand.Index, targetHand.Index);
            MirrorFinger(sourceHand.Middle, targetHand.Middle);
            MirrorFinger(sourceHand.Ring, targetHand.Ring);
            MirrorFinger(sourceHand.Little, targetHand.Little);
            
            Debug.Log($"Mirrored Hand Pose (Direction: {mirrorDirection}).");
        }

        private void MirrorFinger(UxrAvatarFinger sourceF, UxrAvatarFinger destF)
        {
            if (sourceF == null || destF == null) return;

            MirrorBone(sourceF.Proximal, destF.Proximal);
            MirrorBone(sourceF.Intermediate, destF.Intermediate);
            MirrorBone(sourceF.Distal, destF.Distal);
        }

        private void MirrorBone(Transform src, Transform dst)
        {
            if (src == null || dst == null) return;

            if (mirrorPosition)
            {
                Vector3 p = src.localPosition;
                dst.localPosition = new Vector3(p.x * positionMultiplier.x, p.y * positionMultiplier.y, p.z * positionMultiplier.z);
            }

            if (mirrorRotation)
            {
                Vector3 r = src.localEulerAngles;
                dst.localEulerAngles = new Vector3(r.x * rotationMultiplier.x, r.y * rotationMultiplier.y, r.z * rotationMultiplier.z);
            }
        }
    }
}
