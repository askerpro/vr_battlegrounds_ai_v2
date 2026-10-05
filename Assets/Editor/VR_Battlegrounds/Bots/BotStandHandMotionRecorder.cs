using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Bots;
using VrBattlegrounds.DevTools.BotCombatStand;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Покадровая лента плавного движения в пространстве корпуса; не управляет руками.</summary>
    [InitializeOnLoad]
    public static class BotStandHandMotionRecorder
    {
        private static BotStandCaseResult _case;
        private static BotStandSession _session;
        private static readonly List<object> Frames=new List<object>();
        private static readonly List<Vector3> FinalRight=new List<Vector3>(),SourceRight=new List<Vector3>();
        private static int _blended;
        private static bool _truncated;
        private static float _start,_end;
        static BotStandHandMotionRecorder()
        {
            BotCombatStand.CaseStarted += (result,session)=>{_case=result;_session=session;Frames.Clear();FinalRight.Clear();SourceRight.Clear();_blended=0;_truncated=false;_start=0;_end=0;};
            BotCombatStand.CaseFinished += Finish; UxrManager.AvatarsUpdated+=Sample;
            EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.ExitingPlayMode) Reset();};
            AssemblyReloadEvents.beforeAssemblyReload+=Reset;
        }
        private static void Sample()
        {
            if(_case==null || !_case.Capture || _session.Subject?.ActiveAvatar==null) return;
            if(Frames.Count>=3000){_truncated=true;return;}
            try
            {
                var player=_session.Subject.ActiveAvatar; var body=player.GetComponent<BotBody>(); var driver=player.GetComponent<BotCombatDriver>();
                var avatar=player.GetComponent<UxrAvatar>(); var animator=driver?.PoseAnimator;
                if(body==null || avatar==null || animator==null) return;
                var right=avatar.GetHandBone(UxrHandSide.Right); var left=avatar.GetHandBone(UxrHandSide.Left);
                var sourceRight=animator.GetBoneTransform(HumanBodyBones.RightHand); var sourceLeft=animator.GetBoneTransform(HumanBodyBones.LeftHand);
                if(right==null || left==null || sourceRight==null || sourceLeft==null) return;
                var inverse=Quaternion.Inverse(body.BodyRotation);
                Vector3 final=inverse*(right.position-body.Feet), source=Quaternion.Inverse(animator.transform.rotation)*(sourceRight.position-animator.transform.position);
                if(Frames.Count==0) _start=Time.time; _end=Time.time;
                FinalRight.Add(final); SourceRight.Add(source);
                var layers=new List<object>();
                for(int layer=0;layer<animator.layerCount;layer++)
                {
                    var current=animator.GetCurrentAnimatorClipInfo(layer); var next=animator.GetNextAnimatorClipInfo(layer);
                    if(animator.IsInTransition(layer) || current.Count(c=>c.weight>.01f)+next.Count(c=>c.weight>.01f)>1) _blended++;
                    layers.Add(new { layer,weight=animator.GetLayerWeight(layer),transition=animator.IsInTransition(layer),
                        current=current.Select(c=>new {name=c.clip.name,c.weight}).ToArray(),next=next.Select(c=>new {name=c.clip.name,c.weight}).ToArray() });
                }
                Frames.Add(new {frame=Time.frameCount,time=Time.time,bodyId=player.GetInstanceID(),bodyFeet=new[]{body.Feet.x,body.Feet.y,body.Feet.z},yaw=body.Yaw,
                    aiming=driver.Target!=null,layers,nativeAnimators=avatar.GetComponentsInChildren<Animator>(true).Where(a=>a!=animator).Select(a=>new {name=a.name,enabled=a.enabled,active=a.gameObject.activeInHierarchy,controller=AssetDatabase.GetAssetPath(a.runtimeAnimatorController),
                        clips=a.isInitialized ? Enumerable.Range(0,a.layerCount).SelectMany(i=>a.GetCurrentAnimatorClipInfo(i)).Select(c=>new{name=c.clip.name,c.weight}).ToArray() : null}).ToArray(),
                    rightSource=new[]{source.x,source.y,source.z},rightFinal=new[]{final.x,final.y,final.z},
                    leftSource=Position(Quaternion.Inverse(animator.transform.rotation)*(sourceLeft.position-animator.transform.position)),leftFinal=Position(inverse*(left.position-body.Feet)) });
            }
            catch(Exception e) { _case.Status="InvalidFixture";_case.Error="Покадровое движение: "+e.Message; }
        }
        private static void Finish(BotStandCaseResult result)
        {
            if(result!=_case) return;
            try
            {
                var folder=Path.Combine(BotCombatStand.LastOutput,result.Id+"-"+result.Seed);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,"hand-motion.json"),JsonConvert.SerializeObject(Frames,Formatting.Indented));
                result.Metrics["handMotionFrames"]=Frames.Count;
                result.Metrics["sourceClipBlendLayerFrames"]=_blended;
                result.Metrics["handMotionTruncated"]=_truncated;
                result.Metrics["handMotionSampleStartTime"]=_start;result.Metrics["handMotionSampleEndTime"]=_end;
                if(FinalRight.Count>0) {result.Metrics["rightFinalBodySpacePeakToPeakMm"]=Extent(FinalRight);result.Metrics["rightSourceRigSpacePeakToPeakMm"]=Extent(SourceRight);}
                result.Metrics["handMotionLimit"]="Includes intentional movement/aiming; per-axis extents, no aesthetic acceptance threshold. Source and final are different reference spaces.";
            }
            finally { Reset(); }
        }
        private static float[] Position(Vector3 p)=>new[]{p.x,p.y,p.z};
        private static float[] Extent(List<Vector3> p)=>new[]{(p.Max(v=>v.x)-p.Min(v=>v.x))*1000,(p.Max(v=>v.y)-p.Min(v=>v.y))*1000,(p.Max(v=>v.z)-p.Min(v=>v.z))*1000};
        private static void Reset(){_case=null;_session=null;Frames.Clear();FinalRight.Clear();SourceRight.Clear();}
    }
}
