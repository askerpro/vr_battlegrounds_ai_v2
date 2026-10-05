// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrPreviewHandGripMesh.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Extensions.Unity.Render;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;

namespace UltimateXR.Editor.Manipulation.HandPoses
{
    /// <summary>
    /// VR Battlegrounds patch: preview использует runtime-применение pose и native Unity skinning.
    /// Исходный avatar не изменяется; vertices остаются в rigid grabber space для SDK proxy.
    /// </summary>
    public class UxrPreviewHandGripMesh
    {
        public bool IsValid => UnityMesh && _skin && _pose && _hand != null && _sourceIndices != null;
        public Mesh UnityMesh { get; private set; }
        private UxrPreviewHandGripMesh() { }

        public static UxrPreviewHandGripMesh Build(UxrGrabbableObject grabbableObject, UxrAvatar avatar, int grabPoint, UxrHandSide handSide)
        {
            if(!grabbableObject || !avatar) return null;
            var selected=grabbableObject.GetGrabPoint(grabPoint).GetGripPoseInfo(avatar)?.HandPose;
            var pose=selected?avatar.GetHandPose(selected.name):null;
            if(!pose) return null;
            var preview=BuildForAvatar(avatar,pose,handSide,grabbableObject.GetGrabPoint(grabPoint).GetGripPoseInfo(avatar).PoseBlendValue);
            if(preview!=null)preview.UnityMesh.name=UxrGrabPointIndex.GetIndexDisplayName(grabbableObject,grabPoint)+(handSide==UxrHandSide.Left?UxrConstants.LeftGrabPoseMeshSuffix:UxrConstants.RightGrabPoseMeshSuffix);
            return preview;
        }

        /// <summary>Read-only deformation текущего pose asset (включая несохранённый), без grab/scene SDK clone.</summary>
        public static UxrPreviewHandGripMesh BuildForAvatar(UxrAvatar avatar,UxrHandPoseAsset pose,UxrHandSide handSide,float blendValue=0)
        {
            if(!avatar || !pose)return null;
            var grabber=avatar.GetComponentsInChildren<UxrGrabber>().FirstOrDefault(g=>g.Side==handSide&&g.HandRenderer is SkinnedMeshRenderer);
            if(!grabber) return null;
            var preview=new UxrPreviewHandGripMesh();
            try {
                var skin=(SkinnedMeshRenderer)grabber.HandRenderer;
                preview.CreateMesh(avatar,skin,handSide);
                preview.CreateBoneList(avatar,skin,pose,handSide);
                preview.UnityMesh.name=pose.name+" "+handSide+" preview";
                preview.SetBlendPoseValue(grabber,avatar.GetHandBone(handSide),pose.PoseType==UxrHandPoseType.Blend,blendValue);
                return preview;
            } catch {
                if(preview.UnityMesh) Object.DestroyImmediate(preview.UnityMesh);
                throw;
            }
        }

        public bool Refresh(UxrGrabbableObject grabbableObject,UxrAvatar avatar,int grabPoint,UxrHandSide handSide,bool reloadBoneData=false)
        {
            if(!grabbableObject || !avatar) return false;
            var grabber=avatar.GetComponentsInChildren<UxrGrabber>().FirstOrDefault(g=>g.Side==handSide);
            var selected=grabbableObject.GetGrabPoint(grabPoint).GetGripPoseInfo(avatar)?.HandPose;
            var pose=selected?avatar.GetHandPose(selected.name):null;
            if(!grabber || !pose || !(grabber.HandRenderer is SkinnedMeshRenderer skin)) return false;
            // Geometry mapping принадлежит конкретному mesh/rig; при смене renderer источник перевычисляется.
            if(_skin!=skin || _sourceMesh!=skin.sharedMesh || _side!=handSide || _mappingWrist!=avatar.GetHand(handSide).Wrist || !_mappingBones.SequenceEqual(skin.bones)) {
                CreateMesh(avatar,skin,handSide);
            }
            CreateBoneList(avatar,skin,pose,handSide);
            SetBlendPoseValue(grabber,avatar.GetHandBone(handSide),pose.PoseType==UxrHandPoseType.Blend,grabbableObject.GetGrabPoint(grabPoint).GetGripPoseInfo(avatar).PoseBlendValue);
            return true;
        }

        public void SetBlendPoseValue(UxrGrabber grabber,Transform handTransform,bool blend,float blendValue=0)
        {
            if(!IsValid || !grabber || !handTransform) return;
            ComputePose(grabber.transform,blend,blendValue);
        }

        private void ComputePose(Transform grabberTransform,bool blend,float blendValue)
        {
            // Временные Transform/SMR не содержат SDK components и не запускают Avatar/UID lifecycle.
            var preview=new PreviewRenderUtility();Mesh baked=null;
            try {
                var map=new Dictionary<Transform,Transform>();
                Transform Copy(Transform source) {
                    if(!source) return null;
                    if(map.TryGetValue(source,out var existing)) return existing;
                    var parent=Copy(source.parent);
                    var go=new GameObject("Grip skinning bone"){hideFlags=HideFlags.HideAndDontSave};
                    if(!parent) preview.AddSingleGO(go);
                    var node=go.transform;node.SetParent(parent,false);node.localPosition=source.localPosition;node.localRotation=source.localRotation;node.localScale=source.localScale;
                    map.Add(source,node);return node;
                }
                UxrAvatarFinger Finger(UxrAvatarFinger finger) => new UxrAvatarFinger {
                    Metacarpal=Copy(finger.Metacarpal),Proximal=Copy(finger.Proximal),Intermediate=Copy(finger.Intermediate),Distal=Copy(finger.Distal)
                };
                var hand=new UxrAvatarHand{Wrist=Copy(_hand.Wrist),Thumb=Finger(_hand.Thumb),Index=Finger(_hand.Index),Middle=Finger(_hand.Middle),Ring=Finger(_hand.Ring),Little=Finger(_hand.Little)};
                var skin=Copy(_skin.transform).gameObject.AddComponent<SkinnedMeshRenderer>();
                skin.enabled=false;skin.sharedMesh=_sourceMesh;skin.bones=_skin.bones.Select(Copy).ToArray();skin.rootBone=Copy(_skin.rootBone);skin.quality=_skin.quality;
                for(int i=0;i<_sourceMesh.blendShapeCount;i++)skin.SetBlendShapeWeight(i,_skin.GetBlendShapeWeight(i));
                if(_pose.PoseType==UxrHandPoseType.Fixed) UxrAvatarRig.UpdateHandUsingDescriptor(hand,_side==UxrHandSide.Left?_pose.HandDescriptorLeft:_pose.HandDescriptorRight,_handAxes,_fingerAxes);
                else UxrAvatarRig.UpdateHandUsingDescriptor(hand,_side==UxrHandSide.Left?_pose.HandDescriptorOpenLeft:_pose.HandDescriptorOpenRight,_side==UxrHandSide.Left?_pose.HandDescriptorClosedLeft:_pose.HandDescriptorClosedRight,blend?blendValue:0,_handAxes,_fingerAxes);
                baked=new Mesh{hideFlags=HideFlags.HideAndDontSave};skin.BakeMesh(baked,false);
                var grabberCopy=Copy(grabberTransform);
                // Proxy имеет unit scale: сохраняем avatar scale, исключая только масштаб grabbable hierarchy.
                var toGrabber=Matrix4x4.TRS(grabberCopy.position,grabberCopy.rotation,Vector3.one).inverse*skin.localToWorldMatrix;
                var normalMatrix=toGrabber.inverse.transpose;
                var sourceVertices=baked.vertices;var sourceNormals=baked.normals;
                for(int i=0;i<_sourceIndices.Length;i++) {
                    _vertices[i]=toGrabber.MultiplyPoint3x4(sourceVertices[_sourceIndices[i]]);
                    _normals[i]=normalMatrix.MultiplyVector(sourceNormals[_sourceIndices[i]]).normalized;
                }
                UnityMesh.vertices=_vertices;UnityMesh.normals=_normals;UnityMesh.RecalculateBounds();
            } finally {
                if(baked) Object.DestroyImmediate(baked);
                preview.Cleanup();
            }
        }

        private void CreateMesh(UxrAvatar avatar,SkinnedMeshRenderer skin,UxrHandSide side)
        {
            _sourceMesh=skin.sharedMesh;
            _mappingBones=skin.bones.ToArray();_mappingWrist=avatar.GetHand(side).Wrist;
            var extracted=MeshExt.ExtractSubMesh(skin,avatar.GetHand(side).Wrist,MeshExt.ExtractSubMeshOperation.BoneAndChildren,out _sourceIndices);
            if(UnityMesh) {
                // Mesh reference разделяют proxy и cached GripPoseInfo; её нельзя заменять при remap.
                try {var oldName=UnityMesh.name;EditorUtility.CopySerialized(extracted,UnityMesh);UnityMesh.name=oldName;}
                finally {Object.DestroyImmediate(extracted);}
            } else UnityMesh=extracted;
            _initialVertices=UnityMesh.vertices;
            _vertices=new Vector3[_sourceIndices.Length];_normals=new Vector3[_sourceIndices.Length];
        }

        // Сохранена точка подготовки данных preview; абсолютные source-rig matrices не участвуют.
        private void CreateBoneList(UxrAvatar avatar,SkinnedMeshRenderer skin,UxrHandPoseAsset pose,UxrHandSide side)
        {
            _skin=skin;_hand=avatar.GetHand(side);_pose=pose;_side=side;
            var arm=avatar.AvatarRigInfo.GetArmInfo(side);_handAxes=arm.HandUniversalLocalAxes;_fingerAxes=arm.FingerUniversalLocalAxes;
        }

        private SkinnedMeshRenderer _skin;
        private UxrAvatarHand _hand;
        private UxrHandPoseAsset _pose;
        private UxrHandSide _side;
        private UltimateXR.Core.Math.UxrUniversalLocalAxes _handAxes,_fingerAxes;
        private Mesh _sourceMesh;
        private Transform[] _mappingBones;
        private Transform _mappingWrist;
        private int[] _sourceIndices;
        private Vector3[] _initialVertices,_vertices,_normals;
    }
}
