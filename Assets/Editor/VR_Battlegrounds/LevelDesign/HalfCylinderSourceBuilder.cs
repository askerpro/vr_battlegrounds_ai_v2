using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Maps;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Явный генератор вертикального полуцилиндра; существующие формы и сцены не переписывает.</summary>
    public static class HalfCylinderSourceBuilder
    {
        public const string SourceKey = "LD_HalfCylinder_Mid";
        public const string PrefabPath = "Assets/Prefabs/LevelDesign/LD_Alphabet/LD_HalfCylinder_Mid.prefab";
        public const string MeshPath = "Assets/Art/Models/LevelDesign/LD_HalfCylinder_Mid_Mesh.asset";
        public const string DefinitionPath = "Assets/Settings/LevelDesign/BlockoutBlocks/HalfCylinder.asset";

        public static Mesh BuildMesh(float diameter = 1.2f, float height = 1.6f)
        {
            if(float.IsNaN(diameter)||float.IsInfinity(diameter)||diameter<=0||float.IsNaN(height)||float.IsInfinity(height)||height<=0)
                throw new ArgumentException("Диаметр и высота полуцилиндра должны быть положительными конечными числами.");
            const int segments=32;float radius=diameter/2;
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            Action<Vector3,Vector3,Vector3> triangle=(a,b,c)=>
            {
                int offset=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);
                uv.Add(new Vector2(a.x,a.z)/.3f);uv.Add(new Vector2(b.x,b.z)/.3f);uv.Add(new Vector2(c.x,c.z)/.3f);
                triangles.Add(offset);triangles.Add(offset+1);triangles.Add(offset+2);
            };
            Action<Vector3,Vector3,Vector3,Vector3,float> quad=(a,b,c,d,width)=>
            {
                int offset=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);
                uv.Add(Vector2.zero);uv.Add(new Vector2(0,height/.3f));uv.Add(new Vector2(width/.3f,height/.3f));uv.Add(new Vector2(width/.3f,0));
                triangles.AddRange(new[]{offset,offset+1,offset+2,offset,offset+2,offset+3});
            };
            for(int i=0;i<segments;i++)
            {
                float angleA=Mathf.PI*i/segments,angleB=Mathf.PI*(i+1)/segments;
                var a=new Vector3(Mathf.Cos(angleA)*radius,0,i==0?0:Mathf.Sin(angleA)*radius);
                var b=new Vector3(Mathf.Cos(angleB)*radius,0,i==segments-1?0:Mathf.Sin(angleB)*radius);
                triangle(Vector3.zero,a,b);
                triangle(Vector3.up*height,b+Vector3.up*height,a+Vector3.up*height);
                quad(a,a+Vector3.up*height,b+Vector3.up*height,b,Mathf.PI*radius/segments);
            }
            var left=new Vector3(-radius,0,0);var right=new Vector3(radius,0,0);
            // Разделение плоской грани совпадает с ребром веера крышек: нет T-стыка в центре.
            quad(left,left+Vector3.up*height,Vector3.up*height,Vector3.zero,radius);
            quad(Vector3.zero,Vector3.up*height,right+Vector3.up*height,right,radius);
            var mesh=new Mesh {name=SourceKey+"_Mesh"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);
            mesh.RecalculateBounds();mesh.RecalculateNormals();mesh.RecalculateTangents();return mesh;
        }

        /// <summary>Под замком Unity: резервирует только целевые данные, создаёт источник и публикует шестую форму.</summary>
        public static string Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("Создание источника выполняется вне Play Mode после компиляции.");
            var data=JsonUtility.FromJson<BlockoutGridSettings.Data>(JsonUtility.ToJson(BlockoutGridSettings.Current));
            BlockoutGridSettings.AddMissingActiveRows(data);BlockoutGridSettings.Validate(data);
            var row=data.blocks.Single(b=>b.name==SourceKey);
            string backup="Temp/LevelDesign/HalfCylinder/Before-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");Directory.CreateDirectory(backup);
            foreach(string path in new[]{PrefabPath,MeshPath,DefinitionPath,BlockoutGridSettings.Path,BlockoutRegistryFactory.RegistryPath})
            {
                if(File.Exists(path))File.Copy(path,Path.Combine(backup,Path.GetFileName(path)));
                if(File.Exists(path+".meta"))File.Copy(path+".meta",Path.Combine(backup,Path.GetFileName(path)+".meta"));
            }
            var mesh=BuildMesh(row.width);GameObject contents=null;bool loaded=false;
            try
            {
                if(File.Exists(PrefabPath)){contents=PrefabUtility.LoadPrefabContents(PrefabPath);loaded=true;}
                else contents=new GameObject(SourceKey);
                // Проверка предшествует изменению общего mesh asset: отказ не должен менять существующие экземпляры.
                if(loaded)
                {
                    var colliders=contents.GetComponentsInChildren<Collider>(true);
                    var filters=contents.GetComponentsInChildren<MeshFilter>(true);
                    if(colliders.Length!=1||!(colliders[0] is MeshCollider)||colliders[0].gameObject!=contents
                        ||filters.Length!=1||filters[0].gameObject!=contents)
                        throw new InvalidOperationException("Состав источника полуцилиндра изменён; автоматическая перезапись отклонена.");
                }
                var asset=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
                if(asset==null){AssetDatabase.CreateAsset(mesh,MeshPath);asset=mesh;mesh=null;}
                else {EditorUtility.CopySerialized(mesh,asset);EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);}
                contents.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);contents.transform.localScale=Vector3.one;
                var filter=contents.GetComponent<MeshFilter>();if(filter==null)filter=contents.AddComponent<MeshFilter>();filter.sharedMesh=asset;
                if(contents.GetComponent<MeshRenderer>()==null)contents.AddComponent<MeshRenderer>();
                var collider=contents.GetComponent<MeshCollider>();if(collider==null)collider=contents.AddComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=asset;collider.convex=true;
                // Источник палитры хранит форму. Игровой CoverSurface создаётся на секциях экземпляра.
                foreach(var surface in contents.GetComponents<CoverSurface>())Object.DestroyImmediate(surface);
                BlockoutRegistryFactory.ApplyDisplayMaterial(contents,CoverClass.Hard);
                var prefab=PrefabUtility.SaveAsPrefabAsset(contents,PrefabPath,out bool saved);
                if(!saved)throw new InvalidOperationException("Не удалось сохранить полуцилиндр; резервная копия: "+backup);
                var definition=AssetDatabase.LoadAssetAtPath<BlockoutBlockDefinition>(DefinitionPath);
                if(definition==null){definition=ScriptableObject.CreateInstance<BlockoutBlockDefinition>();definition.shapeId="half-cylinder";AssetDatabase.CreateAsset(definition,DefinitionPath);}
                definition.geometryPrefab=prefab;definition.dimensionsSourceKey=SourceKey;definition.title="HalfCylinder";
                definition.displayName="Полуцилиндр";definition.description="Вертикальная половина цилиндра с плоской задней гранью. Высота и материал выбираются отдельно; связь с укрытием задаётся вручную.";
                definition.gameplayGeometry=true;definition.heightEditable=true;definition.supportsCellWall=false;definition.supportsOpenings=false;definition.editableDimensions=false;
                definition.openingDisabledReason="Для полуцилиндра генератор сквозных отверстий пока не реализован.";
                definition.defaultMaterial=CoverClass.Hard;definition.allowedMaterials=new[]{CoverClass.Hard,CoverClass.Soft};definition.minDimensions.y=1.2f;definition.maxDimensions.y=2.5f;
                BlockoutCanonicalRegistry.SetPlanCaps(definition,new Vector2(row.width,row.depth));EditorUtility.SetDirty(definition);AssetDatabase.SaveAssetIfDirty(definition);
                BlockoutGridSettings.Save(data);BlockoutCanonicalRegistry.Apply();BlockoutGridSettingsPanel.Reload();BlockoutRegistryFactory.InvalidateSource();return backup;
            }
            finally {if(contents!=null){if(loaded)PrefabUtility.UnloadPrefabContents(contents);else Object.DestroyImmediate(contents);}if(mesh!=null)Object.DestroyImmediate(mesh);}
        }

        public static Dictionary<string,object> Probe()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);var mesh=prefab!=null?prefab.GetComponent<MeshFilter>()?.sharedMesh:null;
            return new Dictionary<string,object> {
                {"exists",prefab!=null&&mesh!=null},{"bounds",mesh!=null?mesh.bounds:new Bounds()},
                {"bottomZero",mesh!=null&&Mathf.Abs(mesh.bounds.min.y)<.0001f},
                {"flatFaceZZero",mesh!=null&&Mathf.Abs(mesh.bounds.min.z)<.0001f},
                {"depthHalfDiameter",mesh!=null&&Mathf.Abs(mesh.bounds.size.z-mesh.bounds.size.x/2)<.0001f},
                {"sameColliderMesh",prefab!=null&&prefab.GetComponent<MeshCollider>()?.sharedMesh==mesh},
                {"rootScaleOne",prefab!=null&&prefab.transform.localScale==Vector3.one},
                {"prefabGUID",AssetDatabase.AssetPathToGUID(PrefabPath)},{"meshGUID",AssetDatabase.AssetPathToGUID(MeshPath)}
            };
        }
    }
}
