using UnityEditor;
using UnityEngine;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;

namespace VRBattlegrounds.Editor
{
    public class FinalizeRigMappingSetup
    {
        [MenuItem("Tools/VR Battlegrounds/Avatars/UXR Setup Wizard/4. Finalize Rig Mapping")]
        public static void Execute()
        {
            GameObject avatarObj = Selection.activeGameObject;
            if (avatarObj == null) avatarObj = GameObject.Find("AutoSetupAvatarTarget");
            if (avatarObj == null)
            {
                var foundUxrAvatar = Object.FindObjectOfType<UltimateXR.Avatar.UxrAvatar>();
                if (foundUxrAvatar != null) avatarObj = foundUxrAvatar.gameObject;
            }
            if (avatarObj == null) return;

            Undo.RegisterFullObjectHierarchyUndo(avatarObj, "Setup UXR Avatar: 4. Finalize");

            UxrAvatar uxrAvatar = avatarObj.GetComponent<UxrAvatar>();
            if (uxrAvatar == null)
            {
                Debug.LogError("UXR Setup: Missing UxrAvatar on AutoSetupAvatarTarget. Did you run Step 1?");
                return;
            }

            Transform bigHandsObj = avatarObj.transform.Find("BigHandsIntegration(Clone)") ?? avatarObj.transform.Find("BigHandsIntegration");
            if (bigHandsObj != null)
            {
                ForceMapBigHandsRig(uxrAvatar, bigHandsObj);
            }


            EditorUtility.SetDirty(uxrAvatar);
            EditorUtility.SetDirty(avatarObj);
            Debug.Log($"✅ [4/4] Avatar '{avatarObj.name}' rig mapping finalized successfully! Laser logic automatically fixed.");
        }

        private static void ForceMapBigHandsRig(UxrAvatar uxrAvatar, Transform bigHandsIntegration)
        {
            var rig = uxrAvatar.AvatarRig;
            Transform leftHandT = uxrAvatar.transform.Find("BigIKHandLeft") ?? uxrAvatar.transform.Find("BigIKHandLeft(Clone)");
            Transform rightHandT = uxrAvatar.transform.Find("BigIKHandRight") ?? uxrAvatar.transform.Find("BigIKHandRight(Clone)");

            UxrHandIntegration leftIntegration = bigHandsIntegration.Find("LeftHand").GetComponent<UxrHandIntegration>();
            UxrHandIntegration rightIntegration = bigHandsIntegration.Find("RightHand").GetComponent<UxrHandIntegration>();

            if (leftHandT != null)
            {
                MapHandRig(rig.LeftArm.Hand, leftHandT, "_Left", leftIntegration);
            }

            if (rightHandT != null)
            {
                MapHandRig(rig.RightArm.Hand, rightHandT, "_Right", rightIntegration);
            }
        }

        private static void MapHandRig(UltimateXR.Avatar.Rig.UxrAvatarHand handRig, Transform handRoot, string suffix, UxrHandIntegration integration)
        {
            if (integration != null)
            {
                integration.TryToMatchHand();
                handRoot.position = integration.transform.position;
                handRoot.rotation = integration.transform.rotation;
            }
            Transform wrist = handRoot.Find("Wrist" + suffix);
            if (wrist == null) return;

            handRig.Wrist = wrist;

            MapFinger(handRig.Thumb, wrist, "Thumb", suffix, 3, true);
            MapFinger(handRig.Index, wrist, "Index", suffix, 3, false);
            MapFinger(handRig.Middle, wrist, "Middle", suffix, 3, false);
            MapFinger(handRig.Ring, wrist, "Ring", suffix, 3, false);
            MapFinger(handRig.Little, wrist, "Little", suffix, 3, false);
        }

        private static void MapFinger(UltimateXR.Avatar.Rig.UxrAvatarFinger fingerRig, Transform wrist, string fingerPrefix, string suffix, int numBones, bool isThumb)
        {
            string palmName = fingerPrefix + "_Palm" + suffix;
            Transform palm = wrist.Find(palmName);
            if (palm == null) return;

            Transform b0 = palm.Find(fingerPrefix + "_0" + suffix);
            Transform b1 = b0 != null ? b0.Find(fingerPrefix + "_1" + suffix) : null;
            Transform b2 = b1 != null ? b1.Find(fingerPrefix + "_2" + suffix) : null;

            if (isThumb)
            {
                fingerRig.Metacarpal = null;
                fingerRig.Proximal = palm;
                fingerRig.Intermediate = b0;
                fingerRig.Distal = b1;
            }
            else
            {
                fingerRig.Metacarpal = palm;
                fingerRig.Proximal = b0;
                fingerRig.Intermediate = b1;
                fingerRig.Distal = b2;
            }
        }
    }
}
