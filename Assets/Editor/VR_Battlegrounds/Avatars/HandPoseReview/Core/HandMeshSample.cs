using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandGeometry
{
    /// <summary>Рамка без масштаба: координаты сохраняют физические метры.</summary>
    public sealed class HandFrame
    {
        public readonly string Kind;
        public readonly Vector3 Origin;
        public readonly Quaternion Rotation;
        public Matrix4x4 WorldToFrame => Matrix4x4.TRS(Origin, Rotation, Vector3.one).inverse;
        public HandFrame(string kind, Vector3 origin, Quaternion rotation)
        { Kind=kind; Origin=origin; Rotation=rotation; }
        public static HandFrame FromObject(Transform root) => new HandFrame("object",root.position,root.rotation);
        public static HandFrame FromWrist(Transform wrist, Vector3 forward, Vector3 up) =>
            new HandFrame("wrist",wrist.position,Quaternion.LookRotation(forward,up));
    }

    /// <summary>Идентичность ассета; общий слой не знает форматы отчётов потребителей.</summary>
    public class HandAssetIdentity
    {
        public string Path, Guid, DependencyHash;
        public long LocalFileId;
        public static HandAssetIdentity Capture(UnityEngine.Object asset)
        {
            string path=AssetDatabase.GetAssetPath(asset);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset,out string guid,out long id);
            return new HandAssetIdentity {Path=path,Guid=guid,LocalFileId=id,
                DependencyHash=string.IsNullOrEmpty(path)?"":AssetDatabase.GetAssetDependencyHash(path).ToString()};
        }
    }

    public struct HandBoneWeight
    {
        public int BoneIndex;
        public float Weight;
    }

    /// <summary>Полный запечённый renderer: индексы и веса не теряются при отсечении кисти.</summary>
    public sealed class HandMeshSample
    {
        public string RendererPath;
        public HandAssetIdentity Asset;
        public Vector3[] Vertices, Normals;
        public int[][] SubmeshTriangles;
        public MeshTopology[] SubmeshTopology;
        public int[][] SubmeshIndices;
        public int[] WeightOffsets;
        public HandBoneWeight[] Weights;
        [JsonIgnore] public Transform[] Bones;
        public string[] BonePaths;
        public int[] DominantBones;
        [JsonIgnore] public int[] Triangles => SubmeshTriangles.SelectMany(x=>x).ToArray();

        public float WeightOf(int vertex, ISet<int> boneIndices)
        {
            float total=0;
            for(int i=WeightOffsets[vertex];i<WeightOffsets[vertex+1];i++)
                if(boneIndices.Contains(Weights[i].BoneIndex)) total+=Weights[i].Weight;
            return total;
        }

        public static string PathOf(Transform node, Transform root)
        {
            if(!node) return "";
            var parts=new List<string>();
            while(node && node!=root) {parts.Add(node.name);node=node.parent;}
            parts.Reverse();return string.Join("/",parts);
        }
    }

    public static class HandMeshCapture
    {
        /// <summary>Читает штатный skinning, включая blend shapes и все влияния, без смены importer.</summary>
        public static HandMeshSample Capture(SkinnedMeshRenderer skin, Matrix4x4 worldToFrame, Transform root, bool captureAssetIdentity=true)
        {
            if(!skin || !skin.sharedMesh) throw new ArgumentException("Нет SkinnedMeshRenderer/sharedMesh.");
            Mesh source=skin.sharedMesh;
            var baked=new Mesh {hideFlags=HideFlags.HideAndDontSave};
            try
            {
                // Компенсируем scale SMR: полный localToWorld ниже применяет его ровно один раз.
                skin.BakeMesh(baked,true);
                var matrix=worldToFrame*skin.localToWorldMatrix;
                var normalMatrix=matrix.inverse.transpose;
                var sample=new HandMeshSample {
                    RendererPath=HandMeshSample.PathOf(skin.transform,root),Asset=captureAssetIdentity?HandAssetIdentity.Capture(source):null,
                    Vertices=baked.vertices.Select(matrix.MultiplyPoint3x4).ToArray(),
                    Normals=baked.normals.Select(v=>normalMatrix.MultiplyVector(v).normalized).ToArray(),
                    SubmeshTriangles=Enumerable.Range(0,baked.subMeshCount).Select(i=>baked.GetTopology(i)==MeshTopology.Triangles?baked.GetTriangles(i):Array.Empty<int>()).ToArray(),
                    SubmeshTopology=Enumerable.Range(0,baked.subMeshCount).Select(baked.GetTopology).ToArray(),
                    SubmeshIndices=Enumerable.Range(0,baked.subMeshCount).Select(i=>baked.GetIndices(i)).ToArray(),
                    Bones=skin.bones,WeightOffsets=new int[baked.vertexCount+1],DominantBones=new int[baked.vertexCount]
                };
                sample.BonePaths=sample.Bones.Select(t=>HandMeshSample.PathOf(t,root)).ToArray();
                using(var counts=source.GetBonesPerVertex())
                using(var all=source.GetAllBoneWeights())
                {
                    if(counts.Length!=sample.Vertices.Length) throw new InvalidOperationException("Число вершин skinning и весов не совпадает.");
                    sample.Weights=new HandBoneWeight[all.Length];
                    int offset=0;
                    for(int v=0;v<counts.Length;v++)
                    {
                        sample.WeightOffsets[v]=offset;sample.DominantBones[v]=-1;float best=0;
                        for(int j=0;j<counts[v];j++,offset++)
                        {
                            var w=all[offset];
                            if(w.boneIndex<0 || w.boneIndex>=sample.Bones.Length || !Finite(w.weight) || w.weight<0)
                                throw new InvalidOperationException("Некорректный индекс/вес кости.");
                            sample.Weights[offset]=new HandBoneWeight{BoneIndex=w.boneIndex,Weight=w.weight};
                            if(w.weight>best) {best=w.weight;sample.DominantBones[v]=w.boneIndex;}
                        }
                    }
                    sample.WeightOffsets[counts.Length]=offset;
                    if(offset!=all.Length) throw new InvalidOperationException("Смещения весов не покрывают весь массив.");
                }
                if(sample.Vertices.Any(v=>!Finite(v.x)||!Finite(v.y)||!Finite(v.z)))
                    throw new InvalidOperationException("В запечённом меше NaN/Infinity.");
                return sample;
            }
            finally {UnityEngine.Object.DestroyImmediate(baked);}
        }

        static bool Finite(float value) => !float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
