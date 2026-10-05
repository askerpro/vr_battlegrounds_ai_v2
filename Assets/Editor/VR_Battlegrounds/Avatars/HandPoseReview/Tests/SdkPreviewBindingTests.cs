using System;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Editor.Manipulation.HandPoses;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace VrBattlegrounds.Editor.HandPoseReview.Tests
{
    public class SdkPreviewBindingTests
    {
        const string AvatarPath="Assets/Prefabs/Player/Optimized_MEF_Player.prefab";
        const string WeaponPath="Assets/Prefabs/Weapons/AK105/AK105.prefab";
        static UxrAvatar Avatar=>AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPath).GetComponent<UxrAvatar>();
        static GameObject Weapon=>AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPath);
        static HandPoseFitRequest Request(UxrHandSide side=UxrHandSide.Right,int point=0)=>new HandPoseFitRequest{Side=side,GrabPoint=point,IncludeOtherHand=false,RenderImages=false};

        [TestCase(UxrHandSide.Left,0)]
        [TestCase(UxrHandSide.Right,0)]
        [TestCase(UxrHandSide.Left,1)]
        [TestCase(UxrHandSide.Right,1)]
        public void SnapshotMatchesSdkPreviewAtSelectedSnap(UxrHandSide side,int point)
        {
            var avatar=Avatar;var root=Weapon;var pose=avatar.GetHandPose(point==0?"Kinemation_AK105_Grip":"Kinemation_AK105_Support");
            var request=Request(side,point);var grabbable=root.GetComponent<UxrGrabbableObject>();
            string beforeAvatar=EditorJsonUtility.ToJson(avatar),beforePose=EditorJsonUtility.ToJson(pose),beforeWeapon=EditorJsonUtility.ToJson(grabbable);
            var visible=ScriptableObject.CreateInstance<UxrHandPoseAsset>();UxrPreviewHandGripMesh sdk=null;
            try {
                visible.PoseType=UxrHandPoseType.Fixed;visible.HandDescriptorLeft=new UxrHandDescriptor(avatar,UxrHandSide.Left);visible.HandDescriptorRight=new UxrHandDescriptor(avatar,UxrHandSide.Right);
                sdk=UxrPreviewHandGripMesh.BuildForAvatar(avatar,visible,side);
                using(var snapshot=HandPoseEditorCapture.Capture(avatar,pose,0,root,request,out var report)) {
                    var align=grabbable.Editor_GetGrabPointGrabAlignTransform(avatar,point,side);
                    var frame=Matrix4x4.TRS(root.transform.position,root.transform.rotation,Vector3.one).inverse;
                    var matrix=frame*Matrix4x4.TRS(align.position,align.rotation,Vector3.one);
                    var vertices=sdk.UnityMesh.vertices;var expected=new System.Collections.Generic.List<FitTriangle>();
                    for(int sub=0;sub<sdk.UnityMesh.subMeshCount;sub++) {
                        var ix=sdk.UnityMesh.GetTriangles(sub);
                        for(int i=0;i<ix.Length;i+=3) {
                            var t=new FitTriangle(matrix.MultiplyPoint3x4(vertices[ix[i]]),matrix.MultiplyPoint3x4(vertices[ix[i+1]]),matrix.MultiplyPoint3x4(vertices[ix[i+2]]),"reference");
                            if(t.Area>=1e-12f)expected.Add(t);
                        }
                    }
                    Assert.That(snapshot.Hand.Count,Is.EqualTo(expected.Count));
                    float max=0;
                    for(int i=0;i<expected.Count;i++){max=Mathf.Max(max,Vector3.Distance(snapshot.Hand[i].A,expected[i].A));max=Mathf.Max(max,Vector3.Distance(snapshot.Hand[i].B,expected[i].B));max=Mathf.Max(max,Vector3.Distance(snapshot.Hand[i].C,expected[i].C));}
                    Assert.That(max,Is.LessThan(.00001f));
                    Assert.That(Vector3.Distance(snapshot.GripCenter,frame.MultiplyPoint3x4(align.position)),Is.LessThan(.00001f));
                    Assert.That(report.Surfaces.Count(s=>s.Kind=="hand"),Is.EqualTo(1));
                    Assert.That(snapshot.OtherHand.Count,Is.Zero);
                    Assert.That(report.CaptureSource,Is.EqualTo("editor_sdk_grip_preview"));
                    Assert.That(report.EditorAppliedPoseStateJson,Is.Not.Empty);
                }
            } finally {if(sdk?.UnityMesh)Object.DestroyImmediate(sdk.UnityMesh);Object.DestroyImmediate(visible);}
            Assert.That(EditorJsonUtility.ToJson(avatar),Is.EqualTo(beforeAvatar));
            Assert.That(EditorJsonUtility.ToJson(pose),Is.EqualTo(beforePose));
            Assert.That(EditorJsonUtility.ToJson(grabbable),Is.EqualTo(beforeWeapon));
        }

        [Test]
        public void UnchangedInputReusesFrozenGeometry()
        {
            var avatar=Avatar;var pose=avatar.GetHandPose("Kinemation_AK105_Grip");var request=Request();
            using(var session=new HandPoseSdkPreviewSession()) {
                Assert.That(session.Ensure(avatar,pose,0,Weapon,request),Is.True);
                var snapshot=session.Snapshot;string hash=session.Metadata.EditorSnapshotHash;
                for(int i=0;i<64;i++)Assert.That(session.Ensure(avatar,pose,0,Weapon,request.Copy()),Is.False);
                Assert.That(session.CaptureCount,Is.EqualTo(1));Assert.That(session.Snapshot,Is.SameAs(snapshot));
                Assert.That(session.Metadata.EditorSnapshotHash,Is.EqualTo(hash));
                var metadata=session.CopyMetadata();metadata.Surfaces.Clear();
                Assert.That(session.Metadata.Surfaces.Count,Is.GreaterThan(0),"Export metadata не меняет cached metadata");
            }
        }

        [Test]
        public void PoseRevisionReusesObjectButPointMovesHand()
        {
            var avatar=Avatar;var pose=avatar.GetHandPose("Kinemation_AK105_Grip");var request=Request();var copy=Object.Instantiate(pose);
            try {
                using(var session=new HandPoseSdkPreviewSession()) {
                    session.Ensure(avatar,pose,0,Weapon,request);
                    var objectSurface=session.Metadata.Surfaces.First(s=>s.Kind=="object");string objectHash=session.ObjectHash;
                    session.Ensure(avatar,copy,0,Weapon,request);
                    Assert.That(session.Metadata.Surfaces.First(s=>s.Kind=="object"),Is.SameAs(objectSurface));
                    Assert.That(session.ObjectHash,Is.EqualTo(objectHash));
                    var a=session.Snapshot.GripCenter;string aHash=session.Metadata.EditorSnapshotHash;
                    request.GrabPoint=1;Assert.That(session.Ensure(avatar,copy,0,Weapon,request),Is.True);
                    Assert.That(Vector3.Distance(a,session.Snapshot.GripCenter),Is.GreaterThan(.01f));
                    Assert.That(session.Metadata.EditorSnapshotHash,Is.Not.EqualTo(aHash));
                    request.GrabPoint=0;session.Ensure(avatar,copy,0,Weapon,request);
                    Assert.That(Vector3.Distance(a,session.Snapshot.GripCenter),Is.LessThan(.00001f));
                    Assert.That(session.Metadata.EditorSnapshotHash,Is.EqualTo(aHash));
                    Assert.That(session.Ensure(avatar,copy,0,Weapon,request,true),Is.True);
                }
            } finally {Object.DestroyImmediate(copy);}
        }

        [Test]
        public void ObjectKeyTracksNotifiedMeshMutation()
        {
            var preview=new PreviewRenderUtility();Mesh mesh=null;
            try {
                var root=new GameObject("owned object key fixture"){hideFlags=HideFlags.HideAndDontSave};preview.AddSingleGO(root);
                mesh=new Mesh{hideFlags=HideFlags.HideAndDontSave};mesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};mesh.triangles=new[]{0,1,2};
                root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<MeshRenderer>();
                var avatar=Avatar;var pose=avatar.GetHandPose("Kinemation_AK105_Grip");var request=Request();
                string key=HandPoseSdkPreviewSession.BuildInputKey(avatar,pose,0,root,request,false);
                var vertices=mesh.vertices;vertices[0]=new Vector3(.001f,0,0);mesh.vertices=vertices;EditorUtility.SetDirty(mesh);
                Assert.That(HandPoseSdkPreviewSession.BuildInputKey(avatar,pose,0,root,request,false),Is.Not.EqualTo(key));
            } finally {preview.Cleanup();if(mesh)Object.DestroyImmediate(mesh);}
        }

        [Test]
        public void VisibleBonesOverrideStaleSavedPose()
        {
            var avatar=Avatar;var root=Weapon;var pose=avatar.GetHandPose("Kinemation_AK105_Grip");UxrPreviewHandGripMesh saved=null;
            try {
                saved=UxrPreviewHandGripMesh.BuildForAvatar(avatar,pose,UxrHandSide.Right);
                using(var snapshot=HandPoseEditorCapture.Capture(avatar,pose,0,root,Request(),out var report)) {
                    var align=root.GetComponent<UxrGrabbableObject>().Editor_GetGrabPointGrabAlignTransform(avatar,0,UxrHandSide.Right);
                    var frame=Matrix4x4.TRS(root.transform.position,root.transform.rotation,Vector3.one).inverse*Matrix4x4.TRS(align.position,align.rotation,Vector3.one);
                    var vertices=saved.UnityMesh.vertices;var geometry=new System.Collections.Generic.List<FitTriangle>();
                    for(int sub=0;sub<saved.UnityMesh.subMeshCount;sub++){var ix=saved.UnityMesh.GetTriangles(sub);for(int i=0;i<ix.Length;i+=3){var t=new FitTriangle(frame.MultiplyPoint3x4(vertices[ix[i]]),frame.MultiplyPoint3x4(vertices[ix[i+1]]),frame.MultiplyPoint3x4(vertices[ix[i+2]]));if(t.Area>=1e-12f)geometry.Add(t);}}
                    Assert.That(HandPoseEditorCapture.Hash(snapshot.Hand),Is.Not.EqualTo(HandPoseEditorCapture.Hash(geometry)),"Prefab bind pose отличается от сохранённого AK105 grip: capture обязан читать текущие bones");
                    Assert.That(report.EditorAppliedPoseStateJson,Is.Not.EqualTo(report.EditorPoseStateJson));
                }
            } finally {if(saved?.UnityMesh)Object.DestroyImmediate(saved.UnityMesh);}
        }

        [Test]
        public void BoneRotationWithoutAssetSaveInvalidatesKey()
        {
            var preview=new PreviewRenderUtility();Mesh mesh=null;
            try {
                var root=new GameObject("owned unsaved bone key fixture"){hideFlags=HideFlags.HideAndDontSave};preview.AddSingleGO(root);
                var bone=new GameObject("finger");bone.transform.SetParent(root.transform,false);var skin=root.AddComponent<SkinnedMeshRenderer>();
                mesh=new Mesh{hideFlags=HideFlags.HideAndDontSave};mesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};mesh.triangles=new[]{0,1,2};skin.sharedMesh=mesh;skin.bones=new[]{bone.transform};skin.rootBone=bone.transform;
                var avatar=Avatar;var pose=avatar.GetHandPose("Kinemation_AK105_Grip");var request=Request();string poseJson=EditorJsonUtility.ToJson(pose);
                string key=HandPoseSdkPreviewSession.BuildInputKey(avatar,pose,0,root,request,false);
                bone.transform.localRotation=Quaternion.Euler(0,30,0);
                Assert.That(HandPoseSdkPreviewSession.BuildInputKey(avatar,pose,0,root,request,false),Is.Not.EqualTo(key));
                Assert.That(EditorJsonUtility.ToJson(pose),Is.EqualTo(poseJson));
            } finally {preview.Cleanup();if(mesh)Object.DestroyImmediate(mesh);}
        }

        [Test]
        public void ObjectKeyTracksLodMembership()
        {
            var preview=new PreviewRenderUtility();
            try {
                var root=new GameObject("owned LOD key fixture"){hideFlags=HideFlags.HideAndDontSave};preview.AddSingleGO(root);
                var first=new GameObject("first");first.transform.SetParent(root.transform,false);var a=first.AddComponent<MeshRenderer>();
                var second=new GameObject("second");second.transform.SetParent(root.transform,false);var b=second.AddComponent<MeshRenderer>();
                var lod=root.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.5f,new Renderer[]{a}),new LOD(.1f,new Renderer[]{b})});
                var avatar=Avatar;var pose=avatar.GetHandPose("Kinemation_AK105_Grip");var request=Request();
                string key=HandPoseSdkPreviewSession.BuildInputKey(avatar,pose,0,root,request,false);
                lod.SetLODs(new[]{new LOD(.5f,new Renderer[]{b}),new LOD(.1f,new Renderer[]{a})});EditorUtility.SetDirty(lod);
                Assert.That(HandPoseSdkPreviewSession.BuildInputKey(avatar,pose,0,root,request,false),Is.Not.EqualTo(key));
            } finally {preview.Cleanup();}
        }

        [Test]
        public void CameraSuppressionDoesNotInvalidateObjectKey()
        {
            var preview=new PreviewRenderUtility();var scope=new HandPoseCameraScope();
            try {
                var root=new GameObject("owned camera key fixture"){hideFlags=HideFlags.HideAndDontSave};preview.AddSingleGO(root);var renderer=root.AddComponent<MeshRenderer>();
                var camera=preview.camera;var avatar=Avatar;var pose=avatar.GetHandPose("Kinemation_AK105_Grip");var request=Request();
                string key=HandPoseSdkPreviewSession.BuildInputKey(avatar,pose,0,root,request,false);
                scope.Begin(camera,true,new[]{renderer});scope.End(camera);
                camera.transform.position=new Vector3(1,2,3);camera.fieldOfView=30;
                Assert.That(HandPoseSdkPreviewSession.BuildInputKey(avatar,pose,0,root,request,false),Is.EqualTo(key));
            } finally {scope.Dispose();preview.Cleanup();}
        }
    }
}
