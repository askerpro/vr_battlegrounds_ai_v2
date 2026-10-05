using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UltimateXR.Avatar.Controllers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Bots;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Изолирует движение одного оружейного idle без Blaze, SDK, физики и подмешивания других состояний.</summary>
    public static class BotStandIdleMotionProbe
    {
        public static Dictionary<string,object> Run(string avatarPath="Assets/Prefabs/Player/Optimized_MEF_Player_Black.prefab")
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Нужен свободный Edit Mode.");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(avatarPath);
            var controller=prefab!=null ? prefab.GetComponent<UxrStandardAvatarController>() : null;
            var assets=Resources.Load<BotCombatAssets>(BotCombatAssets.ResourcePath);
            if(controller==null || controller.Legs.locomotionRig==null || assets==null) throw new InvalidOperationException("Нет humanoid источника/контроллеров.");
            var scene=EditorSceneManager.NewPreviewScene();
            var rows=new List<object>();
            try
            {
                foreach(var variant in new[] { new { name="Pistol", controller=assets.pistol },new { name="Rifle", controller=assets.rifle } })
                {
                    var rig=UnityEngine.Object.Instantiate(controller.Legs.locomotionRig);
                    SceneManager.MoveGameObjectToScene(rig,scene); rig.hideFlags=HideFlags.HideAndDontSave;
                    rig.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                    rig.transform.localScale=prefab.transform.lossyScale;
                    foreach(var behaviour in rig.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(behaviour);
                    var animator=rig.GetComponent<Animator>();
                    if(animator==null || !animator.isHuman) throw new InvalidOperationException("Риг не humanoid.");
                    animator.runtimeAnimatorController=variant.controller; animator.applyRootMotion=false; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                    rig.SetActive(true); animator.enabled=true; animator.Play("Idle",0,0); animator.Update(.001f);
                    var current=animator.GetCurrentAnimatorClipInfo(0);
                    if(current.Length!=1) throw new InvalidOperationException("Изолированный Idle должен содержать один клип.");
                    float duration=Mathf.Clamp(current[0].clip.length*2,2,12);
                    var left=new List<Vector3>(); var right=new List<Vector3>(); var frames=new List<object>();
                    for(int i=0;i<=Mathf.CeilToInt(duration*60);i++)
                    {
                        var l=Quaternion.Inverse(rig.transform.rotation)*(animator.GetBoneTransform(HumanBodyBones.LeftHand).position-rig.transform.position);
                        var r=Quaternion.Inverse(rig.transform.rotation)*(animator.GetBoneTransform(HumanBodyBones.RightHand).position-rig.transform.position);
                        left.Add(l); right.Add(r); frames.Add(new { time=i/60f,left=new[]{l.x,l.y,l.z},right=new[]{r.x,r.y,r.z} });
                        animator.Update(1f/60);
                    }
                    rows.Add(new { category=variant.name, clip=current[0].clip.name, clipPath=AssetDatabase.GetAssetPath(current[0].clip),
                        clipGuid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(current[0].clip)), duration, layerCount=animator.layerCount,
                        leftPeakToPeakMm=Extent(left),rightPeakToPeakMm=Extent(right),frames });
                    UnityEngine.Object.DestroyImmediate(rig);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            string folder=Path.GetFullPath("Docs/tasks/report/bot-combat-stand/idle-motion-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"report.json"),JsonConvert.SerializeObject(new { avatarPath,avatarGuid=AssetDatabase.AssetPathToGUID(avatarPath),method="single Idle clip, 60 Hz manual Animator.Update, no SDK/Blaze/physics; per-axis peak-to-peak, not quality threshold",rows },Formatting.Indented));
            return new Dictionary<string,object> { {"passed",true},{"reportPath",Path.Combine(folder,"report.json")},{"variants",rows.Count} };
        }
        private static float[] Extent(List<Vector3> points) => new[]{ (points.Max(p=>p.x)-points.Min(p=>p.x))*1000,(points.Max(p=>p.y)-points.Min(p=>p.y))*1000,(points.Max(p=>p.z)-points.Min(p=>p.z))*1000 };
    }
}
