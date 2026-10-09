using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using VrBattlegrounds.Editor.HandGeometry;

namespace VrBattlegrounds.Editor.HandPoseReview.Tests
{
    public class HandMeshCaptureTests
    {
        [TestCase(.5f)] [TestCase(1.5f)]
        public void CaptureAppliesAvatarScaleOnce(float scale)
        {
            var root=new GameObject("Scale fixture");var mesh=new Mesh();
            try
            {
                var bone=new GameObject("Bone").transform;bone.SetParent(root.transform,false);bone.localPosition=new Vector3(0,.2f,0);
                mesh.vertices=new[]{new Vector3(.1f,.3f,.2f),new Vector3(.2f,.3f,.2f),new Vector3(.1f,.4f,.2f)};
                mesh.normals=new[]{Vector3.forward,Vector3.forward,Vector3.forward};mesh.triangles=new[]{0,1,2};
                mesh.bindposes=new[]{bone.worldToLocalMatrix*root.transform.localToWorldMatrix};
                mesh.boneWeights=new[]{new BoneWeight{boneIndex0=0,weight0=1},new BoneWeight{boneIndex0=0,weight0=1},new BoneWeight{boneIndex0=0,weight0=1}};
                var skin=root.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.bones=new[]{bone};skin.rootBone=bone;
                root.transform.SetPositionAndRotation(new Vector3(2,3,4),Quaternion.Euler(20,30,40));root.transform.localScale=Vector3.one*scale;
                var sample=HandMeshCapture.Capture(skin,Matrix4x4.identity,root.transform,false);
                for(int i=0;i<mesh.vertexCount;i++)
                    Assert.That(Vector3.Distance(sample.Vertices[i],bone.localToWorldMatrix.MultiplyPoint3x4(mesh.bindposes[0].MultiplyPoint3x4(mesh.vertices[i]))),Is.LessThan(.00001f));
            }
            finally{Object.DestroyImmediate(root);Object.DestroyImmediate(mesh);}
        }

        [Test] public void CapturePreservesFiveInfluencesSubmeshIndicesAndScaledNormals()
        {
            var root=new GameObject("Capture fixture");var mesh=new Mesh();
            try
            {
                var bones=new Transform[5];for(int i=0;i<bones.Length;i++){bones[i]=new GameObject("Bone"+i).transform;bones[i].SetParent(root.transform,false);}
                mesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};
                var normal=new Vector3(1,1,0).normalized;mesh.normals=new[]{normal,normal,normal};
                mesh.subMeshCount=2;mesh.SetTriangles(new[]{0,1,2},0);mesh.SetTriangles(new[]{2,1,0},1);
                mesh.bindposes=new[]{Matrix4x4.identity,Matrix4x4.identity,Matrix4x4.identity,Matrix4x4.identity,Matrix4x4.identity};
                var values=new[]{.5f,.2f,.15f,.1f,.05f};var weightData=new BoneWeight1[15];
                for(int vertex=0;vertex<3;vertex++)for(int bone=0;bone<5;bone++)weightData[vertex*5+bone]=new BoneWeight1{boneIndex=bone,weight=values[bone]};
                using(var counts=new NativeArray<byte>(new byte[]{5,5,5},Allocator.Temp))
                using(var weights=new NativeArray<BoneWeight1>(weightData,Allocator.Temp))
                {
                    mesh.SetBoneWeights(counts,weights);
                }
                var skin=root.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.bones=bones;skin.rootBone=bones[0];
                // Не меняем bone scale: проверяется перевод уже запечённой нормали в новую рамку.
                var frame=Matrix4x4.Scale(new Vector3(2,1,.5f));
                var sample=HandMeshCapture.Capture(skin,frame,root.transform,false);
                Assert.That(sample.Weights.Length,Is.EqualTo(15));Assert.That(sample.WeightOffsets,Is.EqualTo(new[]{0,5,10,15}));
                Assert.That(sample.Weights[4].BoneIndex,Is.EqualTo(4));
                // Unity квантирует записанные веса: capture должен сохранить именно хранимое влияние.
                using(var stored=mesh.GetAllBoneWeights())Assert.That(sample.Weights[4].Weight,Is.EqualTo(stored[4].weight));
                Assert.That(sample.Weights[4].Weight,Is.GreaterThan(.04f));
                Assert.That(sample.SubmeshTriangles[0],Is.EqualTo(new[]{0,1,2}));Assert.That(sample.SubmeshTriangles[1],Is.EqualTo(new[]{2,1,0}));
                var expected=new Vector3(.5f,1,0).normalized;
                Assert.That(Vector3.Angle(sample.Normals[0],expected),Is.LessThan(.1f));
            }
            finally {Object.DestroyImmediate(root);Object.DestroyImmediate(mesh);}
        }
    }
}
