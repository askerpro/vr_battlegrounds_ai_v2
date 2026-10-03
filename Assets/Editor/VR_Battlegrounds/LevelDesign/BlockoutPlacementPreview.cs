using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using VrBattlegrounds.LevelDesign;
using Object=UnityEngine.Object;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Одна подготовленная форма кисти: высота пересобирается при выборе параметра, а не на repaint.</summary>
    [InitializeOnLoad]
    public static class BlockoutPlacementPreview
    {
        private static GameObject prepared;
        private static Scene ownedScene;
        private static BlockoutBlockDefinition definition;
        private static Vector3 dimensions;
        private static Hash128 sourceHash;
        private static readonly List<Vector3> edges=new List<Vector3>();
        static BlockoutPlacementPreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload+=Clear;
            EditorApplication.quitting+=Clear;
            EditorApplication.projectChanged+=Clear;
            EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.ExitingEditMode)Clear();};
        }
        private static GameObject Prepare(BlockoutBlockDefinition requested,Vector3 size)
        {
            var hash=AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(requested.geometryPrefab));
            if(prepared!=null&&definition==requested&&dimensions==size&&sourceHash==hash)return prepared;
            Clear();definition=requested;dimensions=size;ownedScene=EditorSceneManager.NewPreviewScene();
            sourceHash=hash;
            prepared=Object.Instantiate(requested.geometryPrefab);prepared.hideFlags=HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(prepared,ownedScene);
            var stepped=prepared.GetComponent<BlockoutSteppedGeometry>();
            if(stepped!=null)stepped.ApplyHeight(size.y);
            else if(requested.heightEditable)BlockoutHeightGeometryEditor.Initialize(prepared,BlockoutRegistryFactory.GeometryBounds(requested.geometryPrefab).size.y,size.y);
            foreach(var filter in prepared.GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.sharedMesh==null)continue;
                var vertices=filter.sharedMesh.vertices;var triangles=filter.sharedMesh.triangles;
                for(int i=0;i<triangles.Length;i+=3)for(int j=0;j<3;j++)
                {
                    edges.Add(prepared.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[triangles[i+j]])));
                    edges.Add(prepared.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[triangles[i+(j+1)%3]])));
                }
            }
            return prepared;
        }
        public static bool Conflict(BlockoutBlockDefinition requested,Vector3 size,float yaw,Scene scene,Bounds floor,Bounds target)
            => Conflict(requested,size,yaw,scene,floor,target,out _);
        public static bool Conflict(BlockoutBlockDefinition requested,Vector3 size,float yaw,Scene scene,Bounds floor,Bounds target,out BlockoutWallEditing.ConflictInfo conflict)
        {
            var source=Prepare(requested,size);
            return BlockoutWallEditing.ProbeConflict(source,scene,floor,target,yaw,out conflict);
        }
        public static void Draw(BlockoutBlockDefinition requested,Vector3 size,float yaw,Bounds target,Color color)
        {
            var source=Prepare(requested,size);source.transform.rotation=Quaternion.Euler(0,yaw,0);
            source.transform.position+=target.min-BlockoutPainter.WorldBounds(source).min;
            using(new Handles.DrawingScope(color,source.transform.localToWorldMatrix))Handles.DrawLines(edges.ToArray());
        }
        public static void Clear()
        {
            if(prepared!=null)Object.DestroyImmediate(prepared);prepared=null;definition=null;edges.Clear();
            if(ownedScene.IsValid())EditorSceneManager.ClosePreviewScene(ownedScene);ownedScene=default;
        }
        public static void InvalidateSource(GameObject source=null)
        {if(source==null||definition!=null&&definition.geometryPrefab==source)Clear();}
    }
}
