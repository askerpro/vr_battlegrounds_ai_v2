using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Avatar.Controllers;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Editor.HandGeometry;

namespace VrBattlegrounds.Editor.HandRigQuality
{
    internal sealed class HandRigMesh
    {
        public HandMeshSample Sample;
        public float[] HandWeights, PalmWeights;
        public string[] Segments;
        public int[] HandTriangles;
        public readonly Dictionary<string,HashSet<int>> DescendantBones=new Dictionary<string,HashSet<int>>();
    }

    /// <summary>Копирует только Transform и выбранные SMR: игровые компоненты и сеть не запускаются.</summary>
    internal sealed class HandRigCapture : IDisposable
    {
        readonly PreviewRenderUtility _preview=new PreviewRenderUtility();
        public readonly HandRigSnapshot Data=new HandRigSnapshot();
        public readonly List<HandRigMesh> Meshes=new List<HandRigMesh>();

        public HandRigCapture(string assetPath, string poseName, string sensorPath, HandRigQualityRequest settings)
        {
            try {Build(assetPath,poseName,sensorPath,settings);} catch {Dispose();throw;}
        }

        void Build(string assetPath, string poseName, string sensorPath, HandRigQualityRequest settings)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            var avatar=source?source.GetComponent<UxrAvatar>():null;
            if(!avatar) throw new ArgumentException("Префаб не содержит UxrAvatar: "+assetPath);
            var hand=avatar.GetHand(settings.Side);
            if(hand==null || !hand.Wrist || !hand.HasFullHandData()) throw new ArgumentException("Неполная разметка кисти: "+assetPath);
            // Getter SDK пересчитывает persistent rig при старой версии. Диагностика должна только читать источник.
            var version=new SerializedObject(avatar).FindProperty("_rigInfo")?.FindPropertyRelative("_version");
            var current=typeof(UxrAvatarRigInfo).GetField("CurrentVersion",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public);
            if(version==null||current==null||version.intValue!=(int)current.GetRawConstantValue())
                throw new NotSupportedException("RigInfo источника устарел; обновите его штатным сборщиком до диагностики.");
            var axes=avatar.AvatarRigInfo.GetArmInfo(settings.Side);
            if(axes?.HandUniversalLocalAxes==null || axes.FingerUniversalLocalAxes==null)
                throw new ArgumentException("Нет сохранённых универсальных осей SDK: "+assetPath);
            var controller=source.GetComponent<UxrStandardAvatarController>();
            var so=controller?new SerializedObject(controller):null;
            var eyeProperty=so?.FindProperty("_bodyIKSettings")?.FindPropertyRelative("_eyesBaseHeight");
            if(eyeProperty==null || eyeProperty.floatValue<=0 || !Finite(eyeProperty.floatValue))
                throw new ArgumentException("Нет корректной итоговой высоты глаз в контроллере: "+assetPath);
            Vector3 sourceScale=source.transform.lossyScale;
            if(Mathf.Abs(sourceScale.x-sourceScale.y)>1e-5f || Mathf.Abs(sourceScale.x-sourceScale.z)>1e-5f || sourceScale.x<=0)
                throw new NotSupportedException("Игровая нормализация роста требует положительного uniform scale корня.");

            var map=new Dictionary<Transform,Transform>();
            Transform Copy(Transform original, Transform parent)
            {
                var go=new GameObject(original.name){hideFlags=HideFlags.HideAndDontSave};
                var node=go.transform;if(!parent)_preview.AddSingleGO(go);
                node.SetParent(parent,false);node.localPosition=original.localPosition;
                node.localRotation=original.localRotation;node.localScale=original.localScale;map.Add(original,node);
                foreach(Transform child in original)Copy(child,node);
                return node;
            }
            Transform root=Copy(source.transform,null);
            root.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            float scale=settings.EyeHeightMeters/eyeProperty.floatValue;
            root.localScale*=scale;
            Transform Resolve(Transform original) => original && map.TryGetValue(original,out var copy)?copy:null;
            UxrAvatarFinger Finger(UxrAvatarFinger f) => new UxrAvatarFinger {
                Metacarpal=Resolve(f.Metacarpal),Proximal=Resolve(f.Proximal),
                Intermediate=Resolve(f.Intermediate),Distal=Resolve(f.Distal)
            };
            var copiedHand=new UxrAvatarHand {Wrist=Resolve(hand.Wrist),Thumb=Finger(hand.Thumb),
                Index=Finger(hand.Index),Middle=Finger(hand.Middle),Ring=Finger(hand.Ring),Little=Finger(hand.Little)};
            UxrHandPoseAsset pose=null;
            if(settings.CommonSdkPose || !string.IsNullOrEmpty(poseName))
            {
                pose=settings.CommonSdkPose?AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(HandRigQualityCalibration.CommonPose):avatar.GetHandPose(poseName);
                if(!pose)throw new ArgumentException("У аватара нет позы: "+poseName);
                if(pose.PoseType==UxrHandPoseType.Fixed)
                    UxrAvatarRig.UpdateHandUsingDescriptor(copiedHand,pose.GetHandDescriptor(settings.Side,pose.PoseType,UxrBlendPoseType.None),axes.HandUniversalLocalAxes,axes.FingerUniversalLocalAxes);
                else
                    UxrAvatarRig.UpdateHandUsingDescriptor(copiedHand,pose.GetHandDescriptor(settings.Side,pose.PoseType,UxrBlendPoseType.OpenGrip),pose.GetHandDescriptor(settings.Side,pose.PoseType,UxrBlendPoseType.ClosedGrip),settings.Blend,axes.HandUniversalLocalAxes,axes.FingerUniversalLocalAxes);
            }
            Vector3 forward=copiedHand.Wrist.TransformDirection(axes.HandUniversalLocalAxes.LocalForward);
            Vector3 up=copiedHand.Wrist.TransformDirection(axes.HandUniversalLocalAxes.LocalUp);
            var frame=HandFrame.FromWrist(copiedHand.Wrist,forward,up);
            if(settings.Frame=="sensor")
            {
                // В Editor локальные runtime offsets SetupSensor не инициализированы: берём явный авторский sensor Transform.
                Transform sensor=null;
                if(!string.IsNullOrEmpty(sensorPath))sensor=source.transform.Find(sensorPath);
                else
                {
                    var sensors=source.GetComponentsInChildren<UxrControllerTracking>(true)
                        .Where(t=>AuthoredActive(t.transform,source.transform))
                        .Select(t=>new SerializedObject(t).FindProperty(settings.Side==UxrHandSide.Left?"_leftHandSensor":"_rightHandSensor")?.objectReferenceValue as Transform)
                        .Where(t=>t).Distinct().ToArray();
                    if(sensors.Length==1)sensor=sensors[0];
                }
                if(!sensor || !map.ContainsKey(sensor)) throw new ArgumentException("Рамка S неоднозначна/отсутствует: задайте путь sensor Transform относительно аватара.");
                var copy=Resolve(sensor);frame=new HandFrame("configured_sensor_rest",copy.position,copy.rotation);
                Data.Limitations.Add("S — авторский sensor Transform в Rest; соответствие реальному grip pose контроллера/Quest не проверено.");
            }
            else if(settings.Frame!="wrist")throw new ArgumentException("Рамка должна быть wrist или sensor.");
            Matrix4x4 matrix=frame.WorldToFrame;
            Data.Avatar=HandAssetIdentity.Capture(source);Data.Pose=pose?HandAssetIdentity.Capture(pose):null;
            Data.PoseName=settings.CommonSdkPose?"common_sdk_default":string.IsNullOrEmpty(poseName)?"authored_rest":poseName;Data.Side=settings.Side.ToString();Data.FrameKind=frame.Kind;
            if(settings.CommonSdkPose)Data.Limitations.Add("Общий descriptor Cyborg Default применён через оси каждого рига; длины и позиции суставов сохраняются. Это не игровой хват.");
            Data.EyesBaseHeightMeters=eyeProperty.floatValue;Data.EyeHeightMeters=settings.EyeHeightMeters;
            Data.SourceScale=sourceScale;Data.AppliedScale=sourceScale.x*scale;
            Data.Wrist=matrix.MultiplyPoint3x4(copiedHand.Wrist.position);
            var forearm=Resolve(avatar.GetArm(settings.Side).Forearm);
            Data.Forearm=forearm?(Vector3?)matrix.MultiplyPoint3x4(forearm.position):null;
            if(!forearm)Data.Limitations.Add("Нет размеченного предплечья: манжета и кручение предплечья неприменимы (например, SDK BigHands).");
            Data.SdkForward=matrix.MultiplyVector(forward).normalized;Data.SdkUp=matrix.MultiplyVector(up).normalized;

            var segmentBones=new Dictionary<string,Transform>();
            void AddFinger(UxrAvatarFinger finger, string name)
            {
                var nodes=new[]{finger.Metacarpal,finger.Proximal,finger.Intermediate,finger.Distal};
                var names=new[]{"metacarpal","proximal","intermediate","distal"};string parent="wrist";
                for(int i=0;i<nodes.Length;i++)
                {
                    if(!nodes[i])continue;
                    string key=name+"/"+names[i];segmentBones.Add(key,nodes[i]);
                    Transform next=nodes.Skip(i+1).FirstOrDefault(n=>n);
                    Vector3 axis=next?(next.position-nodes[i].position).normalized:nodes[i].TransformDirection(axes.FingerUniversalLocalAxes.LocalForward);
                    Data.Joints.Add(new HandRigJoint {Segment=key,ParentSegment=parent,Path=HandMeshSample.PathOf(nodes[i],root),
                        Position=matrix.MultiplyPoint3x4(nodes[i].position),Forward=matrix.MultiplyVector(axis).normalized,
                        Up=matrix.MultiplyVector(nodes[i].TransformDirection(axes.FingerUniversalLocalAxes.LocalUp)).normalized,
                        LengthMeters=next?(float?)Vector3.Distance(nodes[i].position,next.position):null});
                    parent=key;
                }
            }
            AddFinger(copiedHand.Thumb,"thumb");AddFinger(copiedHand.Index,"index");AddFinger(copiedHand.Middle,"middle");
            AddFinger(copiedHand.Ring,"ring");AddFinger(copiedHand.Little,"little");
            var handSet=new HashSet<Transform>(copiedHand.Wrist.GetComponentsInChildren<Transform>(true));
            var eligible=new HashSet<Transform>(forearm?forearm.GetComponentsInChildren<Transform>(true):Array.Empty<Transform>());
            eligible.UnionWith(handSet);
            var excluded=new HashSet<Renderer>();
            foreach(var lod in source.GetComponentsInChildren<LODGroup>(true))
            {
                var levels=lod.GetLODs();if(levels.Length==0)continue;var first=new HashSet<Renderer>(levels[0].renderers);
                foreach(var level in levels.Skip(1))foreach(var renderer in level.renderers)if(renderer&&!first.Contains(renderer))excluded.Add(renderer);
            }
            foreach(var skin in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // activeInHierarchy у prefab asset не задаёт авторскую видимость: asset не принадлежит живой сцене.
                if(!skin.enabled || !AuthoredActive(skin.transform,source.transform) || excluded.Contains(skin) || !skin.sharedMesh)continue;
                if(!skin.bones.Any(b=>Resolve(b)&&eligible.Contains(Resolve(b))))continue;
                var copy=Resolve(skin.transform).gameObject.AddComponent<SkinnedMeshRenderer>();
                copy.enabled=false;copy.sharedMesh=skin.sharedMesh;copy.bones=skin.bones.Select(Resolve).ToArray();
                copy.rootBone=Resolve(skin.rootBone);copy.quality=skin.quality;
                for(int i=0;i<skin.sharedMesh.blendShapeCount;i++)copy.SetBlendShapeWeight(i,skin.GetBlendShapeWeight(i));
                var sample=HandMeshCapture.Capture(copy,matrix,root);
                var mesh=new HandRigMesh {Sample=sample};
                var handIndices=new HashSet<int>(Enumerable.Range(0,sample.Bones.Length).Where(i=>handSet.Contains(sample.Bones[i])));
                var palmIndices=new HashSet<int>(Enumerable.Range(0,sample.Bones.Length).Where(i=>sample.Bones[i]==copiedHand.Wrist));
                mesh.HandWeights=Enumerable.Range(0,sample.Vertices.Length).Select(i=>sample.WeightOf(i,handIndices)).ToArray();
                mesh.PalmWeights=Enumerable.Range(0,sample.Vertices.Length).Select(i=>sample.WeightOf(i,palmIndices)).ToArray();
                mesh.Segments=sample.Bones.Select(b=>segmentBones.FirstOrDefault(p=>p.Value==b).Key??(b==copiedHand.Wrist?"wrist":"other")).ToArray();
                foreach(var pair in segmentBones)
                    mesh.DescendantBones[pair.Key]=new HashSet<int>(Enumerable.Range(0,sample.Bones.Length).Where(i=>sample.Bones[i]&&(sample.Bones[i]==pair.Value||sample.Bones[i].IsChildOf(pair.Value))));
                var ix=sample.Triangles;var selected=new List<int>();
                for(int i=0;i<ix.Length;i+=3)if((mesh.HandWeights[ix[i]]+mesh.HandWeights[ix[i+1]]+mesh.HandWeights[ix[i+2]])/3>=.5f)
                    selected.AddRange(new[]{ix[i],ix[i+1],ix[i+2]});
                mesh.HandTriangles=selected.ToArray();Meshes.Add(mesh);
                Data.Surfaces.Add(new HandRigSurface {RendererPath=sample.RendererPath,Mesh=sample.Asset,Vertices=sample.Vertices.Length,
                    Triangles=mesh.HandTriangles.Length/3,Influences=sample.Weights.Length,BoneCount=sample.Bones.Length,BoneSegments=mesh.Segments});
            }
            if(!Meshes.Any(m=>m.HandTriangles.Length>0))throw new NotSupportedException("На активном LOD0 нет видимой поверхности размеченной кисти.");
            Data.Limitations.Add("Preview-нормализация высоты по итоговому _eyesBaseHeight; серверная калибровка, IK и Quest не запускались.");
            Data.Limitations.Add("Все веса сохранены; BakeMesh использует фактическую quality renderer/Editor, предел влияний на Quest проверяется отдельно.");
        }

        static bool Finite(float value) => !float.IsNaN(value)&&!float.IsInfinity(value);
        static bool AuthoredActive(Transform node, Transform root)
        {
            while(node){if(!node.gameObject.activeSelf)return false;if(node==root)return true;node=node.parent;}
            return false;
        }
        public void Dispose() => _preview.Cleanup();
    }
}
