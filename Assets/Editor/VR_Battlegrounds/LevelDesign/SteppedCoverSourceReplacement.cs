using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Maps;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Точечная замена прежней пирамиды ступенчатым укрытием с сохранением GUID и наружных габаритов.</summary>
    public static class SteppedCoverSourceReplacement
    {
        public const string SourcePrefabPath = "Assets/Prefabs/LevelDesign/LD_Alphabet/LD_Dorito_Mid.prefab";
        public const string SourcePrefabGuid = "1db53474e0111634a87467c58a9fca6b";
        public const string SourceMeshGuid = "433eafb0cafe02648abf8bac319a706a";
        public const float Module = .3f, LowerHeight = 1.2f, UpperWidth = .9f, UpperDepth = .9f;
        public struct Profile
        {
            public Bounds envelope, lower, upper;
        }
        public static Profile Describe(Bounds envelope)
        {
            if(Mathf.Abs(envelope.size.x-1.5f)>.001f||Mathf.Abs(envelope.size.z-1.5f)>.001f||Mathf.Abs(envelope.size.y-1.6f)>.001f)
                throw new InvalidOperationException("Точечная замена рассчитана на существующее укрытие 1,5×1,6×1,5 м; исходные габариты изменились.");
            float upperHeight=envelope.size.y-LowerHeight;
            return new Profile
            {
                envelope=envelope,
                lower=new Bounds(new Vector3(envelope.center.x,envelope.min.y+LowerHeight/2,envelope.center.z),new Vector3(envelope.size.x,LowerHeight,envelope.size.z)),
                upper=new Bounds(new Vector3(envelope.center.x,envelope.min.y+LowerHeight+upperHeight/2,envelope.center.z),new Vector3(UpperWidth,upperHeight,UpperDepth))
            };
        }
        /// <summary>Единый наружный меш объединения двух призм; внутренних граней на стыке нет.</summary>
        public static Mesh BuildMesh(Bounds envelope)
        {
            Describe(envelope);
            return BlockoutSteppedGeometry.BuildMesh(envelope.size.y, new Vector2(envelope.size.x,envelope.size.z),
                new Vector2(UpperWidth,UpperDepth),new Vector3(envelope.center.x,envelope.min.y,envelope.center.z),LowerHeight);
        }
        private static GameObject Source()
        {
            if(AssetDatabase.AssetPathToGUID(SourcePrefabPath)!=SourcePrefabGuid) throw new InvalidOperationException("GUID исходного укрытия изменился.");
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if(source==null||source.transform.localScale!=Vector3.one) throw new InvalidOperationException("Исходный префаб отсутствует или его масштаб изменился.");
            var filter=source.GetComponent<MeshFilter>();var collider=source.GetComponent<MeshCollider>();
            if(filter==null||collider==null||filter.sharedMesh==null||collider.sharedMesh!=filter.sharedMesh||collider.convex||source.GetComponentsInChildren<Collider>(true).Length!=1)
                throw new InvalidOperationException("Ожидались общий исходный mesh и один невыпуклый MeshCollider на корне.");
            if(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(filter.sharedMesh))!=SourceMeshGuid) throw new InvalidOperationException("GUID исходного меша изменился.");
            return source;
        }
        /// <summary>Явно вызывается владельцем замка Unity после preview-пробы. Сохраняет только исходный меш и его префаб.</summary>
        public static string Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling) throw new InvalidOperationException("Замена выполняется вне Play Mode после компиляции.");
            var source=Source();var asset=source.GetComponent<MeshFilter>().sharedMesh;string path=AssetDatabase.GetAssetPath(asset);
            var generated=BuildMesh(asset.bounds);
            try
            {
                string backup="Temp/LevelDesign/SteppedCover/Before-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");Directory.CreateDirectory(backup);
                foreach(string target in new[]{path,SourcePrefabPath})
                {
                    File.Copy(target,Path.Combine(backup,Path.GetFileName(target)),false);
                    if(File.Exists(target+".meta")) File.Copy(target+".meta",Path.Combine(backup,Path.GetFileName(target)+".meta"),false);
                }
                var contents=PrefabUtility.LoadPrefabContents(SourcePrefabPath);
                try
                {
                    var provider=contents.GetComponent<BlockoutSteppedGeometry>();
                    if(provider==null) provider=contents.AddComponent<BlockoutSteppedGeometry>();
                    provider.totalHeight=1.6f;provider.lowerHeightLimit=LowerHeight;
                    provider.baseSize=new Vector2(1.5f,1.5f);provider.topSize=new Vector2(UpperWidth,UpperDepth);
                    provider.localBottomCenter=new Vector3(asset.bounds.center.x,asset.bounds.min.y,asset.bounds.center.z);
                    EditorUtility.CopySerialized(generated,asset);EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);
                    contents.GetComponent<MeshFilter>().sharedMesh=asset;
                    var collider=contents.GetComponent<MeshCollider>();collider.sharedMesh=null;collider.convex=false;collider.sharedMesh=asset;
                    BlockoutRegistryFactory.ApplyDisplayMaterial(contents,CoverClass.Hard);
                    PrefabUtility.SaveAsPrefabAsset(contents,SourcePrefabPath,out bool saved);
                    if(!saved) throw new InvalidOperationException("Не удалось сохранить профиль исходного префаба; резервная копия: "+backup);
                }
                finally {PrefabUtility.UnloadPrefabContents(contents);}
                BlockoutRegistryFactory.InvalidateSource(source);
                return backup;
            }
            finally {Object.DestroyImmediate(generated);}
        }
        /// <summary>Геометрическая проверка в изолированной preview scene; не обещает антропометрический плейтест.</summary>
        public static Dictionary<string,object> Probe(bool originalSource = false)
        {
            var source=Source();var original=source.GetComponent<MeshFilter>().sharedMesh;var profile=Describe(original.bounds);
            var scene=EditorSceneManager.NewPreviewScene();Mesh mesh=null;
            try
            {
                var clone=(GameObject)PrefabUtility.InstantiatePrefab(source,scene);
                if(!originalSource)
                {
                    mesh=BuildMesh(original.bounds);
                    clone.GetComponent<MeshFilter>().sharedMesh=mesh;clone.GetComponent<MeshCollider>().sharedMesh=mesh;
                }
                var actualMesh=clone.GetComponent<MeshFilter>().sharedMesh;
                Bounds actualBounds=actualMesh.bounds;
                clone.transform.position=Vector3.zero;Physics.SyncTransforms();var collider=clone.GetComponent<MeshCollider>();
                bool allRays=true;int rays=0;
                foreach(var normal in new[]{Vector3.forward,Vector3.back,Vector3.right,Vector3.left})
                {
                    var tangent=Vector3.Cross(Vector3.up,normal);
                    foreach(float offset in new[]{-.6f,0f,.6f})
                    {
                        Vector3 target=new Vector3(profile.envelope.center.x,profile.envelope.min.y+.6f,profile.envelope.center.z)+tangent*offset;
                        allRays&=collider.Raycast(new Ray(target-normal*2,normal),out _,4);rays++;
                    }
                }
                bool shouldersOpen=true,upperBlocks=true;
                foreach(var normal in new[]{Vector3.forward,Vector3.back,Vector3.right,Vector3.left})
                {
                    var tangent=Vector3.Cross(Vector3.up,normal);Vector3 center=new Vector3(profile.envelope.center.x,profile.lower.max.y+.2f,profile.envelope.center.z);
                    foreach(float offset in new[]{-.6f,.6f}) {shouldersOpen&=!collider.Raycast(new Ray(center+tangent*offset-normal*2,normal),out _,4);rays++;}
                    upperBlocks&=collider.Raycast(new Ray(center-normal*2,normal),out _,4);rays++;
                }
                var parts=new[]{profile.lower,profile.upper};bool agreement=true;int agreementRays=0;
                foreach(float x in new[]{-.7f,-.6f,-.3f,0f,.3f,.6f,.7f}) foreach(float y in new[]{.1f,.6f,1.1f,1.3f,1.5f})
                {
                    var ray=new Ray(new Vector3(profile.envelope.center.x+x,profile.envelope.min.y+y,profile.envelope.min.z-1),Vector3.forward);
                    bool expected=parts.Any(part=>part.IntersectRay(ray));bool actual=collider.Raycast(ray,out _,4);agreement&=expected==actual;agreementRays++;
                }
                var sourceMaterials=source.GetComponent<Renderer>().sharedMaterials;var cloneMaterials=clone.GetComponent<Renderer>().sharedMaterials;
                bool watertight=Watertight(actualMesh);
                bool initialColliderAgreement=!collider.convex&&collider.sharedMesh==clone.GetComponent<MeshFilter>().sharedMesh&&clone.GetComponentsInChildren<Collider>(true).Length==1;
                bool heightProfiles=true,worldPartsAgree=true;
                var provider=clone.GetComponent<BlockoutSteppedGeometry>();
                if(provider==null) provider=clone.AddComponent<BlockoutSteppedGeometry>();
                provider.localBottomCenter=new Vector3(profile.envelope.center.x,profile.envelope.min.y,profile.envelope.center.z);
                foreach(float height in new[]{1.2f,1.6f,2.5f})
                {
                    provider.ApplyHeight(height);Physics.SyncTransforms();
                    var local=provider.LocalParts().ToArray();var built=clone.GetComponent<MeshFilter>().sharedMesh;
                    heightProfiles &= local.Length==(height<=1.2f?1:2)&&Watertight(built)&&
                        Mathf.Abs(built.bounds.size.y-height)<.00001f&&Mathf.Abs(built.bounds.min.y-profile.envelope.min.y)<.00001f&&
                        Mathf.Abs(local[0].size.y-1.2f)<.00001f;
                    var origin=new Vector3(3,2,-5);var rotation=Quaternion.Euler(0,45,0);
                    clone.transform.SetPositionAndRotation(origin,rotation);
                    var actualParts=provider.WorldParts().ToArray();var previewParts=provider.WorldPartsAtHeight(height,origin,rotation).ToArray();
                    worldPartsAgree &= actualParts.Length==previewParts.Length;
                    for(int i=0;i<actualParts.Length;i++) worldPartsAgree &= (actualParts[i].center-previewParts[i].center).sqrMagnitude<.000001f&&
                        (actualParts[i].size-previewParts[i].size).sqrMagnitude<.000001f&&Quaternion.Angle(actualParts[i].rotation,previewParts[i].rotation)<.0001f;
                    clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                }
                return new Dictionary<string,object>
                {
                    {"originalSourceRegression",originalSource},{"heightProfilesLowMidTall",heightProfiles},{"rotatedPreviewPartsAgree",worldPartsAgree},
                    {"envelope",actualBounds.size},{"bottom",actualBounds.min.y},{"lower",profile.lower.size},{"upper",profile.upper.size},
                    {"envelopePreserved",(actualBounds.min-original.bounds.min).sqrMagnitude<.000001f&&(actualBounds.max-original.bounds.max).sqrMagnitude<.000001f},
                    {"bottomPreserved",Mathf.Abs(actualBounds.min.y-original.bounds.min.y)<.00001f},
                    {"materialPreserved",sourceMaterials.SequenceEqual(cloneMaterials)},
                    {"upperCenteredXZ",Mathf.Abs(profile.upper.center.x-profile.lower.center.x)<.00001f&&Mathf.Abs(profile.upper.center.z-profile.lower.center.z)<.00001f},
                    {"upperDimensionsMultiple03",Mathf.Abs(profile.upper.size.x/Module-Mathf.Round(profile.upper.size.x/Module))<.00001f&&Mathf.Abs(profile.upper.size.z/Module-Mathf.Round(profile.upper.size.z/Module))<.00001f},
                    {"heightSumPreserved",Mathf.Abs(profile.lower.size.y+profile.upper.size.y-original.bounds.size.y)<.00001f},
                    {"lowerSilhouetteBlocks",allRays},{"shoulderRaysPass",shouldersOpen},{"upperCenterBlocks",upperBlocks},
                    {"meshAndTwoPrismRayAgreement",agreement},{"closedOuterMesh",watertight},
                    {"sharedNonconvexCollider",initialColliderAgreement},
                    {"profileRays",rays},{"agreementRays",agreementRays},{"sourcePrefabGuidPreserved",AssetDatabase.AssetPathToGUID(SourcePrefabPath)==SourcePrefabGuid},
                    {"sourceMeshGuidPreserved",AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original))==SourceMeshGuid},
                    {"limit","Проверен геометрический профиль; полную защиту взрослого в принятой позе подтверждает плейтест человека."}
                };
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);if(mesh!=null)Object.DestroyImmediate(mesh);}
        }
        private static bool Watertight(Mesh mesh)
        {
            var vertices=mesh.vertices;var indices=mesh.triangles;var positions=new Dictionary<Vector3,int>();var edges=new Dictionary<long,int>();
            int Id(Vector3 vertex) {if(!positions.TryGetValue(vertex,out int id)){id=positions.Count;positions.Add(vertex,id);}return id;}
            for(int triangle=0;triangle<indices.Length;triangle+=3) for(int edge=0;edge<3;edge++)
            {
                int a=Id(vertices[indices[triangle+edge]]),b=Id(vertices[indices[triangle+(edge+1)%3]]);
                long key=((long)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);edges.TryGetValue(key,out int count);edges[key]=count+1;
            }
            return edges.Count>0&&edges.Values.All(count=>count==2);
        }
    }
}
