using VrBattlegrounds.Core;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace VRBattlegrounds.Editor
{
    public class ApplyEyeMapping : UnityEditor.Editor
    {
        public static void MapEyes()
        {
            if (!CustomAvatarPipelineMenu.TryGetSelectedFbxAssetPath(out string fbxPath, true))
                return;

            MapEyes(fbxPath);
        }

        public static void MapEyes(string fbxPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;

            if (importer == null)
            {
                throw new System.InvalidOperationException("FBX не найден: " + fbxPath);
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (!model || importer.animationType != ModelImporterAnimationType.Human) throw new System.InvalidOperationException("Нужен Humanoid FBX.");
            var transforms = model.GetComponentsInChildren<Transform>(true);
            if (transforms.Count(t => t.name == "LeftEye") != 1 || transforms.Count(t => t.name == "RightEye") != 1)
                throw new System.InvalidOperationException("Нужны единственные кости LeftEye и RightEye. Сначала добавьте их в копию FBX.");
            HumanDescription desc = importer.humanDescription;
            List<HumanBone> bones = desc.human != null ? new List<HumanBone>(desc.human) : new List<HumanBone>();
            
            bool changed = false;

            // Отключаем Strip Bones, иначе Unity вырежет LeftEye и RightEye
            if (importer.optimizeBones)
            {
                importer.optimizeBones = false;
                changed = true;
                GameLog.Player.Info("Disabled 'Optimize Bones' (Strip Bones) so eye bones won't be deleted.");
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
                GameLog.Player.Info("Mapped LeftEye.");
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
                GameLog.Player.Info("Mapped RightEye.");
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
                GameLog.Player.Info("Successfully reimported FBX with eye mappings!");
            }


            else
            {
                GameLog.Player.Info("Eyes are already mapped!");
            }
        }
    }
}
