using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace VrBattlegrounds.Editor.UXR
{
    public class ApplyEyeMapping : UnityEditor.Editor
    {
        [MenuItem("Tools/VR Battlegrounds/Avatars/Map Eyes To FBX")]
        public static void MapEyes()
        {
            string fbxPath = "Assets/ThirdParty/Military Soldier Mega Bundle/Heavy Soldier/Mesh/Heavy_Soldier_Rig_Mask_Winter.fbx";
            ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;

            if (importer == null)
            {
                Debug.LogError("FBX not found at: " + fbxPath);
                return;
            }

            HumanDescription desc = importer.humanDescription;
            List<HumanBone> bones = desc.human != null ? new List<HumanBone>(desc.human) : new List<HumanBone>();
            
            bool changed = false;

            // Отключаем Strip Bones, иначе Unity вырежет LeftEye и RightEye
            if (importer.optimizeBones)
            {
                importer.optimizeBones = false;
                changed = true;
                Debug.Log("Disabled 'Optimize Bones' (Strip Bones) so eye bones won't be deleted.");
            }

            // Если не привязан левый глаз
            if (!bones.Any(b => b.humanName == "LeftEye"))
            {
                HumanBone lb = new HumanBone();
                lb.humanName = "LeftEye";
                lb.boneName = "LeftEye"; 
                // Имя кости как в иерархии (в Blender мы назвали её LeftEye)
                lb.limit = new HumanLimit();
                lb.limit.useDefaultValues = true;
                bones.Add(lb);
                changed = true;
                Debug.Log("Mapped LeftEye.");
            }

            // Если не привязан правый глаз
            if (!bones.Any(b => b.humanName == "RightEye"))
            {
                HumanBone rb = new HumanBone();
                rb.humanName = "RightEye";
                rb.boneName = "RightEye";
                rb.limit = new HumanLimit();
                rb.limit.useDefaultValues = true;
                bones.Add(rb);
                changed = true;
                Debug.Log("Mapped RightEye.");
            }

            if (changed)
            {
                // To safely update LeftEye and RightEye in the SkeletonBones list, we need their real local transforms!
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (asset != null)
                {
                    Transform leftEyeT = asset.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "LeftEye");
                    Transform rightEyeT = asset.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "RightEye");

                    List<SkeletonBone> skeleton = new List<SkeletonBone>(desc.skeleton);
                    
                    if (leftEyeT != null && !skeleton.Any(b => b.name == "LeftEye"))
                    {
                        skeleton.Add(new SkeletonBone { name = "LeftEye", position = leftEyeT.localPosition, rotation = leftEyeT.localRotation, scale = leftEyeT.localScale });
                    }
                    if (rightEyeT != null && !skeleton.Any(b => b.name == "RightEye"))
                    {
                        skeleton.Add(new SkeletonBone { name = "RightEye", position = rightEyeT.localPosition, rotation = rightEyeT.localRotation, scale = rightEyeT.localScale });
                    }
                    
                    desc.skeleton = skeleton.ToArray();
                }

                desc.human = bones.ToArray();
                importer.humanDescription = desc;
                importer.SaveAndReimport();
                Debug.Log("Successfully reimported FBX with eye mappings!");
            }


            else
            {
                Debug.Log("Eyes are already mapped!");
            }
        }
    }
}
