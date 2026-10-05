using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Единственный владелец frozen SDK preview snapshot для overlay, GPU и CPU export.</summary>
    public sealed class HandPoseSdkPreviewSession : IDisposable
    {
        public HandPoseFitSnapshot Snapshot { get; private set; }
        public HandPoseFitReport Metadata { get; private set; }
        public string InputKey { get; private set; }
        public string ObjectHash { get; private set; }
        string _objectInputKey;
        public int CaptureCount { get; private set; }

        public bool Ensure(UxrAvatar avatar,UxrHandPoseAsset pose,float blend,GameObject root,HandPoseFitRequest request,bool force=false)
        {
            string key=BuildInputKey(avatar,pose,blend,root,request);
            if(!force && Snapshot!=null && InputKey==key)return false;
            string objectKey=BuildInputKey(avatar,pose,blend,root,request,false);
            bool reuseObject=!force && Snapshot!=null && _objectInputKey==objectKey;
            var snapshot=HandPoseEditorCapture.Capture(avatar,pose,blend,root,request,out var metadata,reuseObject?Snapshot:null,reuseObject?Metadata:null);
            Snapshot?.Dispose();Snapshot=snapshot;Metadata=metadata;InputKey=key;ObjectHash=metadata.EditorContactObjectHash;_objectInputKey=objectKey;CaptureCount++;
            return true;
        }

        public HandPoseFitReport CopyMetadata()
        {
            if(Metadata==null)throw new InvalidOperationException("Нет frozen preview snapshot.");
            return JsonConvert.DeserializeObject<HandPoseFitReport>(JsonConvert.SerializeObject(Metadata,FitUnityJsonConverter.Settings),FitUnityJsonConverter.Settings);
        }

        /// <summary>Только входные transforms/revisions: без BakeMesh, topology, чтения mesh vertices и камеры.</summary>
        public static string BuildInputKey(UxrAvatar avatar,UxrHandPoseAsset pose,float blend,GameObject root,HandPoseFitRequest request,bool includeHand=true)
        {
            if(!avatar || !pose || !root)throw new ArgumentException("Нет входа SDK preview.");
            using(var stream=new MemoryStream())
            using(var writer=new BinaryWriter(stream)) {
                void Id(UnityEngine.Object obj,bool revision=true){writer.Write(obj?obj.GetEntityId().ToString():"0");if(obj && revision)writer.Write(EditorUtility.GetDirtyCount(obj));}
                void Matrix(Matrix4x4 value){for(int i=0;i<16;i++)writer.Write(value[i]);}
                void Skin(SkinnedMeshRenderer skin) {
                    Id(skin,false);if(!skin)return;Id(skin.sharedMesh);Matrix(skin.localToWorldMatrix);writer.Write((int)skin.quality);
                    Id(skin.rootBone);if(skin.rootBone)Matrix(skin.rootBone.localToWorldMatrix);
                    writer.Write((int)QualitySettings.skinWeights);
                    var bones=skin.bones;writer.Write(bones.Length);
                    foreach(var bone in bones){Id(bone);if(bone)Matrix(bone.localToWorldMatrix);}
                    if(skin.sharedMesh)for(int i=0;i<skin.sharedMesh.blendShapeCount;i++)writer.Write(skin.GetBlendShapeWeight(i));
                }
                Id(root);
                if(includeHand) {
                Id(avatar);Id(pose);writer.Write(blend);
                var grabber=avatar.GetComponentsInChildren<UxrGrabber>(true).FirstOrDefault(g=>g.Side==request.Side);
                Id(grabber);if(grabber){Matrix(grabber.transform.localToWorldMatrix);Skin(grabber.HandRenderer as SkinnedMeshRenderer);}
                var transform=HandPoseFitSnapshot.ResolveIndexPath(root.transform,request.GrabbableIndexPath);
                var grabbable=transform.GetComponent<UxrGrabbableObject>();Id(grabbable);
                if(grabbable && request.GrabPoint>=0 && request.GrabPoint<grabbable.GrabPointCount) {
                    var align=grabbable.Editor_GetGrabPointGrabAlignTransform(avatar,request.GrabPoint,request.Side);
                    Id(align);if(align)Matrix(Matrix4x4.TRS(align.position,align.rotation,Vector3.one));
                }
                }
                Matrix(root.transform.localToWorldMatrix);
                var lods=root.GetComponentsInChildren<LODGroup>(true);writer.Write(lods.Length);
                foreach(var lod in lods) {
                    Id(lod);writer.Write(lod.enabled);var levels=lod.GetLODs();writer.Write(levels.Length);
                    foreach(var level in levels){writer.Write(level.renderers.Length);foreach(var renderer in level.renderers)Id(renderer,false);}
                }
                var renderers=root.GetComponentsInChildren<Renderer>(true);writer.Write(renderers.Length);
                foreach(var renderer in renderers) {
                    if(renderer.GetComponent<UxrGrabbableObjectPreviewMeshProxy>() || renderer is ParticleSystemRenderer || renderer.name.IndexOf("GrabHighlight",StringComparison.OrdinalIgnoreCase)>=0)continue;
                    // forceRenderingOff меняет dirty revision при SceneView suppression, но не геометрию.
                    Id(renderer,false);writer.Write(renderer.enabled);writer.Write(renderer.gameObject.activeInHierarchy);Matrix(renderer.localToWorldMatrix);
                    if(renderer is SkinnedMeshRenderer skin)Skin(skin);else {var filter=renderer.GetComponent<MeshFilter>();Id(filter?filter.sharedMesh:null);}
                }
                writer.Write(JsonConvert.SerializeObject(request,FitUnityJsonConverter.Settings));writer.Flush();stream.Position=0;
                using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
            }
        }

        public void Dispose()
        {
            Snapshot?.Dispose();Snapshot=null;Metadata=null;InputKey=null;ObjectHash=null;_objectInputKey=null;
        }
    }
}
