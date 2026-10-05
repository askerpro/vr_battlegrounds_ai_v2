using VrBattlegrounds.Core;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace VRBattlegrounds.Editor
{
    public class ModularGloveBoneMapper : EditorWindow
    {
        // Указываем, что Unity должна сериализовать этот список, чтобы он отображался в окне
        [SerializeField] 
        private List<SkinnedMeshRenderer> gloveMeshes = new List<SkinnedMeshRenderer>();
        
        private Transform avatarSkeletonRoot;
        private SerializedObject so;
        private SerializedProperty meshesProp;

        public static void ShowWindow()
        {
            GetWindow<ModularGloveBoneMapper>("Glove Bone Mapper");
        }

        private void OnEnable()
        {
            so = new SerializedObject(this);
            meshesProp = so.FindProperty("gloveMeshes");
        }

        private void OnGUI()
        {
            so.Update();

            GUILayout.Label("Attach Modular Gloves to Avatar Skeleton", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This tool swaps the bones array of a SkinnedMeshRenderer (the glove) to point to the matching bones in your Avatar's skeleton based on their GameObject names.", MessageType.Info);
            
            avatarSkeletonRoot = (Transform)EditorGUILayout.ObjectField("Avatar Root (e.g. VR_Avatar)", avatarSkeletonRoot, typeof(Transform), true);

            GUILayout.Space(10);
            EditorGUILayout.PropertyField(meshesProp, new GUIContent("Glove Meshes"), true);
            
            so.ApplyModifiedProperties();

            GUILayout.Space(10);
            if (GUILayout.Button("Rebind Bones", GUILayout.Height(30)))
            {
                if (gloveMeshes.Count == 0 || avatarSkeletonRoot == null)
                {
                    EditorUtility.DisplayDialog("Error", "Please assign at least one Glove Mesh and the Avatar Root.", "OK");
                    return;
                }
                
                int totalMatched = 0;
                foreach (var glove in gloveMeshes)
                {
                    if (glove != null)
                    {
                        totalMatched += RebindBones(glove, avatarSkeletonRoot);
                    }
                }
                
                GameLog.Player.Info($"✅ Rebinding complete for all assigned meshes! Total matched bones: {totalMatched}");
            }
        }

        private int RebindBones(SkinnedMeshRenderer smr, Transform root)
        {
            Transform[] newBones = new Transform[smr.bones.Length];
            Transform[] targetBones = root.GetComponentsInChildren<Transform>(true);
            
            // Create a dictionary for fast lookup of target bones by name
            Dictionary<string, Transform> boneDict = new Dictionary<string, Transform>();
            foreach (Transform t in targetBones)
            {
                if (!boneDict.ContainsKey(t.name))
                {
                    boneDict.Add(t.name, t);
                }
            }

            int missingBones = 0;
            int matchedBones = 0;

            for (int i = 0; i < smr.bones.Length; i++)
            {
                if (smr.bones[i] != null)
                {
                    string boneName = smr.bones[i].name;
                    
                    if (boneDict.TryGetValue(boneName, out Transform matchedBone))
                    {
                        newBones[i] = matchedBone;
                        matchedBones++;
                    }
                    else
                    {
                        GameLog.Player.Warning($"[ModularGloveBoneMapper] Could not find matching bone for '{boneName}' in the target skeleton!");
                        missingBones++;
                        newBones[i] = smr.bones[i]; // Keep original bone if no match found
                    }
                }
            }

            Undo.RecordObject(smr, "Rebind Glove Bones");
            smr.bones = newBones;
            
            // Rebind root bone
            if (smr.rootBone != null && boneDict.TryGetValue(smr.rootBone.name, out Transform matchedRoot))
            {
                smr.rootBone = matchedRoot;
            }

            GameLog.Player.Info($"Matched: {matchedBones}, Missing: {missingBones} for mesh {smr.name}");
            return matchedBones;
        }
    }
}
