using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Единый критерий свободного объёма для предпросмотра и применения.</summary>
    [InitializeOnLoad]
    public static class BlockoutWallEditing
    {
        public static System.Func<Scene,Bounds,GameObject,bool> PhysicalReserveConflict;
        public static System.Func<Scene,Bounds,GameObject,ConflictInfo> PhysicalReserveReason;
        public sealed class ConflictInfo
        {
            public string message;
            public Object target;
        }
        public static ConflictInfo FloorConflict(Bounds bounds,Bounds floor)
        {
            var sides=new List<string>();
            if(bounds.min.x<floor.min.x-.001f)sides.Add($"−X на {floor.min.x-bounds.min.x:0.###} м");
            if(bounds.max.x>floor.max.x+.001f)sides.Add($"+X на {bounds.max.x-floor.max.x:0.###} м");
            if(bounds.min.z<floor.min.z-.001f)sides.Add($"−Z на {floor.min.z-bounds.min.z:0.###} м");
            if(bounds.max.z>floor.max.z+.001f)sides.Add($"+Z на {bounds.max.z-floor.max.z:0.###} м");
            return sides.Count==0?null:new ConflictInfo {message="Выход за границу пола: "+string.Join("; ",sides)+"."};
        }
        private static ConflictInfo ReserveConflict(Scene scene,Bounds bounds,GameObject ignore)
            => PhysicalReserveReason!=null?PhysicalReserveReason(scene,bounds,ignore):
                PhysicalReserveConflict?.Invoke(scene,bounds,ignore)==true?new ConflictInfo {message="Пересечение зарезервированной области физической арены."}:null;
        private static ConflictInfo Hit(Collider collider)=>new ConflictInfo
        {message=$"Пересечение с «{collider.name}» (ID {collider.gameObject.GetInstanceID()}).",target=collider.gameObject};
        private static Scene auxiliaryScene;
        private static GameObject auxiliaryRoot;
        private static BoxCollider auxiliaryBox,auxiliaryOther;
        private static void AuxiliaryBoxes()
        {
            if(auxiliaryRoot!=null)return;
            auxiliaryScene=EditorSceneManager.NewPreviewScene();
            auxiliaryRoot=new GameObject("Blockout collision auxiliaries") {hideFlags=HideFlags.HideAndDontSave};
            SceneManager.MoveGameObjectToScene(auxiliaryRoot,auxiliaryScene);
            auxiliaryBox=auxiliaryRoot.AddComponent<BoxCollider>();auxiliaryOther=auxiliaryRoot.AddComponent<BoxCollider>();
            auxiliaryBox.enabled=true;auxiliaryOther.enabled=true;
        }
        private static void ReleaseAuxiliaries()
        {
            if(auxiliaryRoot!=null)Object.DestroyImmediate(auxiliaryRoot);
            if(auxiliaryScene.IsValid())EditorSceneManager.ClosePreviewScene(auxiliaryScene);
            auxiliaryScene=default;auxiliaryRoot=null;probeBoxes.Clear();
        }
        static BlockoutWallEditing()
        {
            AssemblyReloadEvents.beforeAssemblyReload+=ReleaseAuxiliaries;
            EditorApplication.quitting+=ReleaseAuxiliaries;
            EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.ExitingEditMode)ReleaseAuxiliaries();};
            Undo.undoRedoPerformed += RefreshGeometry;
            BlockoutRegistryFactory.PreciseGeometryConflict=(go,volumes)=>
            {
                if(!BlockoutGrid.TryFloor(go.scene,out var floor)) return true;
                bool registered=BlockoutRegistryFactory.TryDefinition(go,out var definition);
                if(go.GetComponent<BlockoutCellWall>()!=null || go.GetComponent<BlockoutSteppedGeometry>()!=null) return Conflict(go.scene,floor,volumes,go);
                if(!registered)return Conflict(go.scene,floor,volumes,go);
                var source=go.GetComponent<BlockoutHeightGeometry>()!=null?go:definition.geometryPrefab;
                return PrefabConflictAtYaw(source,go.scene,floor,BlockoutPainter.WorldBounds(go),go.transform.eulerAngles.y,go);
            };
        }
        private static void RefreshGeometry()
        {
            foreach(var wall in Resources.FindObjectsOfTypeAll<BlockoutCellWall>())
                if(wall.gameObject.scene.IsValid()) wall.Rebuild();
            foreach(var stepped in Resources.FindObjectsOfTypeAll<BlockoutSteppedGeometry>())
                if(stepped.gameObject.scene.IsValid())stepped.Rebuild();
        }
        public static bool IsWall(GameObject prefab) => prefab!=null&&BlockoutRegistryFactory.Current!=null
            && BlockoutRegistryFactory.Current.Definitions.Any(d=>d!=null&&d.supportsCellWall
                &&(d.geometryPrefab==prefab||d.materialVariants.Any(v=>v.sourcePrefab==prefab)));
        /// <summary>Точный SAT без зависимости от готовности native-коллайдера или physics scene.</summary>
        public static bool IntersectsPart(Bounds candidate,BlockoutSolidPart part)
            => IntersectsPart(new BlockoutSolidPart {center=candidate.center,size=candidate.size,rotation=Quaternion.identity},part);
        public static bool IntersectsPart(BlockoutSolidPart a,BlockoutSolidPart b)
        {
            var axesA=new[]{a.rotation*Vector3.right,a.rotation*Vector3.up,a.rotation*Vector3.forward};
            var axesB=new[]{b.rotation*Vector3.right,b.rotation*Vector3.up,b.rotation*Vector3.forward};
            Vector3 delta=b.center-a.center;
            foreach(var axis in axesA)if(Separated(axis))return false;
            foreach(var axis in axesB)if(Separated(axis))return false;
            foreach(var axisA in axesA)foreach(var axisB in axesB)
                if(Separated(Vector3.Cross(axisA,axisB)))return false;
            return true;
            bool Separated(Vector3 axis)
            {
                if(axis.sqrMagnitude<.00000001f)return false;
                axis.Normalize();
                float reach=0;
                for(int i=0;i<3;i++)reach+=a.size[i]*.5f*Mathf.Abs(Vector3.Dot(axis,axesA[i]))+b.size[i]*.5f*Mathf.Abs(Vector3.Dot(axis,axesB[i]));
                return reach-Mathf.Abs(Vector3.Dot(delta,axis))<=.001f;
            }
        }
        private static BlockoutSolidPart BoxPart(BoxCollider box)
        {
            var scale=box.transform.lossyScale;
            scale=new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
            return new BlockoutSolidPart {center=box.transform.TransformPoint(box.center),size=Vector3.Scale(box.size,scale),rotation=box.transform.rotation};
        }
        public static bool PrefabConflict(GameObject prefab, Scene scene, Bounds floor, Bounds target, int turns, GameObject ignore=null)
            => PrefabConflictAtYaw(prefab,scene,floor,target,turns*90f,ignore);
        public static bool PrefabConflictAtYaw(GameObject prefab, Scene scene, Bounds floor, Bounds target, float yaw, GameObject ignore=null)
            => PrefabConflictAtYaw(prefab,scene,floor,target,yaw,out _,ignore);
        public static bool PrefabConflictAtYaw(GameObject prefab, Scene scene, Bounds floor, Bounds target, float yaw,out ConflictInfo conflict, GameObject ignore=null)
        {
            var stepped=prefab.GetComponent<BlockoutSteppedGeometry>();
            if(stepped!=null)return Conflict(scene,floor,BlockoutRegistryFactory.SteppedParts(stepped,target.min,yaw,stepped.TotalHeight),out conflict,ignore);
            var probe=Object.Instantiate(prefab); probe.hideFlags=HideFlags.HideAndDontSave; SceneManager.MoveGameObjectToScene(probe,scene);
            try{return ProbeConflict(probe,scene,floor,target,yaw,out conflict,ignore);}
            finally{probeBoxes.Remove(probe);Object.DestroyImmediate(probe);}
        }
        private static readonly Dictionary<GameObject,BoxCollider> probeBoxes=new Dictionary<GameObject,BoxCollider>();
        private static BlockoutSolidPart[] KnownParts(Collider collider)
        {
            var section=collider.GetComponent<BlockoutSectionPart>();
            if(section!=null&&section.owner!=null&&(section.owner.GetComponent<BlockoutCellWall>()!=null||section.owner.GetComponent<BlockoutSteppedGeometry>()!=null))
                return BlockoutCellWall.TransformParts(section.owner.SectionSolidParts(section.sectionIndex),section.owner.transform.position,section.owner.transform.rotation).ToArray();
            var wall=collider.GetComponent<BlockoutCellWall>();return wall!=null?wall.SolidParts().ToArray():null;
        }
        /// <summary>Проверка уже подготовленного изолированного клона без создания мешей или объектов на repaint.</summary>
        public static bool ProbeConflict(GameObject probe,Scene scene,Bounds floor,Bounds target,float yaw,GameObject ignore=null)
            => ProbeConflict(probe,scene,floor,target,yaw,out _,ignore);
        public static bool ProbeConflict(GameObject probe,Scene scene,Bounds floor,Bounds target,float yaw,out ConflictInfo conflict,GameObject ignore=null)
        {
            conflict=null;
            if(BlockoutSectionGeometry.Owns(probe)&&probe.GetComponent<BlockoutCellWall>() is BlockoutCellWall sectionalWall)
            {
                probe.transform.rotation=Quaternion.Euler(0,yaw-sectionalWall.yaw,0);
                probe.transform.position+=target.min-BlockoutPainter.WorldBounds(probe).min;
                return Conflict(scene,floor,sectionalWall.SolidParts(),out conflict,ignore);
            }
            if(probe.GetComponent<BlockoutSteppedGeometry>() is BlockoutSteppedGeometry stepped)
                return Conflict(scene,floor,BlockoutRegistryFactory.SteppedParts(stepped,target.min,yaw,stepped.TotalHeight),out conflict,ignore);
            try
            {
                probe.transform.rotation=Quaternion.Euler(0,Mathf.DeltaAngle(probe.transform.eulerAngles.y,yaw),0)*probe.transform.rotation;
                probe.transform.localScale=Vector3.one;
                probe.transform.position+=target.min-BlockoutPainter.WorldBounds(probe).min;
                probeBoxes.TryGetValue(probe,out var occupiedCell);
                var candidates=probe.GetComponentsInChildren<Collider>(false).Where(c=>BlockoutSupportSurfaces.IsActiveSolid(c)&&c!=occupiedCell).ToArray();
                foreach(var c in candidates) if(c is MeshCollider mesh) mesh.convex=true;
                if(occupiedCell==null){occupiedCell=probe.AddComponent<BoxCollider>();occupiedCell.enabled=true;probeBoxes[probe]=occupiedCell;}
                Physics.SyncTransforms();
                var obstacles=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Collider>(false))
                    .Where(c=>BlockoutSupportSurfaces.IsActiveSolid(c)&&!c.transform.IsChildOf(probe.transform)&&(ignore==null||!c.transform.IsChildOf(ignore.transform))&&!BlockoutSupportSurfaces.IsSupportSurface(scene,c)).ToArray();
                var wallVolumes=obstacles.Select(c=>(collider:c,parts:KnownParts(c))).Where(p=>p.parts!=null).ToDictionary(p=>p.collider,p=>p.parts);
                foreach(var c in candidates)
                {
                    Bounds b=c.bounds;
                    conflict=FloorConflict(b,floor)??ReserveConflict(scene,b,ignore);if(conflict!=null)return true;
                    foreach(var other in obstacles)
                    {
                        if(!b.Intersects(other.bounds)) continue;
                        if(wallVolumes.TryGetValue(other,out var knownParts))
                        {
                            foreach(var volume in knownParts)
                            {
                                if(c is BoxCollider wallCandidateBox)
                                {if(b.Intersects(volume.BroadphaseBounds)&&IntersectsPart(BoxPart(wallCandidateBox),volume)){conflict=Hit(other);return true;}}
                                else
                                {
                                    occupiedCell.size=volume.size; occupiedCell.center=Vector3.zero;
                                    if(b.Intersects(volume.BroadphaseBounds)&&Physics.ComputePenetration(c,c.transform.position,c.transform.rotation,occupiedCell,volume.center,volume.rotation,out _,out float cellDepth)&&cellDepth>.001f){conflict=Hit(other);return true;}
                                }
                            }
                        }
                        else if(c is BoxCollider candidateBox&&other is BoxCollider obstacleBox)
                        {if(IntersectsPart(BoxPart(candidateBox),BoxPart(obstacleBox))){conflict=Hit(other);return true;}}
                        else if(Physics.ComputePenetration(c,c.transform.position,c.transform.rotation,other,other.transform.position,other.transform.rotation,out _,out float depth)&&depth>.001f){conflict=Hit(other);return true;}
                    }
                }
                return false;
            }
            finally { }
        }
        public static bool Conflict(Scene scene, Bounds floor, IEnumerable<Bounds> volumes, GameObject ignore = null)
            => Conflict(scene,floor,volumes.Select(b=>new BlockoutSolidPart {center=b.center,size=b.size,rotation=Quaternion.identity}),ignore);
        public static bool Conflict(Scene scene, Bounds floor, IEnumerable<BlockoutSolidPart> volumes, GameObject ignore = null)
            => Conflict(scene,floor,volumes,out _,ignore);
        public static bool Conflict(Scene scene, Bounds floor, IEnumerable<BlockoutSolidPart> volumes,out ConflictInfo conflict, GameObject ignore = null)
        {
            conflict=null;
            AuxiliaryBoxes();var box=auxiliaryBox;
            try
            {
                Physics.SyncTransforms();
                var obstacles = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Collider>(false))
                    .Where(c=>BlockoutSupportSurfaces.IsActiveSolid(c) && (ignore==null || !c.transform.IsChildOf(ignore.transform))
                        && !BlockoutSupportSurfaces.IsSupportSurface(scene,c)).ToArray();
                var wallVolumes=obstacles.Select(c=>(collider:c,parts:KnownParts(c))).Where(p=>p.parts!=null).ToDictionary(p=>p.collider,p=>p.parts);
                foreach(var part in volumes)
                {
                    var b=part.BroadphaseBounds;
                    conflict=FloorConflict(b,floor)??ReserveConflict(scene,b,ignore);if(conflict!=null)return true;
                    box.size=part.size; box.center=Vector3.zero;
                    foreach(var other in obstacles)
                    {
                        if(!b.Intersects(other.bounds)) continue;
                        if(wallVolumes.TryGetValue(other,out var knownParts))
                        {
                            foreach(var occupied in knownParts)
                            {
                                if(b.Intersects(occupied.BroadphaseBounds)&&IntersectsPart(part,occupied)){conflict=Hit(other);return true;}
                            }
                        }
                        else if(other is BoxCollider obstacleBox)
                        {if(IntersectsPart(part,BoxPart(obstacleBox))){conflict=Hit(other);return true;}}
                        else if(Physics.ComputePenetration(box,part.center,part.rotation,
                            other,other.transform.position,other.transform.rotation,out _,out float depth) && depth>.001f){conflict=Hit(other);return true;}
                    }
                }
                return false;
            }
            finally { }
        }
        public static IEnumerable<Bounds> Volumes(IEnumerable<Vector2Int> cells, Vector3 origin, float step, float height)
            => cells.Select(c=>new Bounds(origin+new Vector3((c.x+.5f)*step,height/2,(c.y+.5f)*step),new Vector3(step,height,step)));

        public static GameObject Create(GameObject prefab, Scene scene, Vector3 origin, float yaw)
        {
            var definition=BlockoutRegistryFactory.Current?.Definitions.FirstOrDefault(d=>d.geometryPrefab==prefab||d.materialVariants.Any(v=>v.sourcePrefab==prefab));
            if(definition==null) throw new System.InvalidOperationException("Форма отсутствует в реестре блоков.");
            var material=VrBattlegrounds.Maps.CoverClassRules.TryExpectedClass(prefab,out var expected)?expected:definition.defaultMaterial;
            return BlockoutRegistryFactory.Create(definition,scene,origin,yaw,BlockoutRegistryFactory.DefaultDimensions(definition),material,BlockoutOpeningSettings.Default);
        }
        public static Bounds Outward(Bounds before, Vector2 origin, float step)
        {
            var min=before.min; var max=before.max;
            min.x=origin.x+Mathf.Floor((min.x-origin.x+.00001f)/step)*step;
            min.z=origin.y+Mathf.Floor((min.z-origin.y+.00001f)/step)*step;
            max.x=origin.x+Mathf.Ceil((max.x-origin.x-.00001f)/step)*step;
            max.z=origin.y+Mathf.Ceil((max.z-origin.y-.00001f)/step)*step;
            var result=new Bounds(); result.SetMinMax(min,max); return result;
        }
        public static BlockoutCellWall Convert(GameObject go, Bounds occupied, float yaw, bool rectangle)
        {
            if(!BlockoutRegistryFactory.TryDefinition(go,out var definition))
                definition=BlockoutRegistryFactory.Current?.Definitions.FirstOrDefault(d=>d.supportsCellWall);
            if(definition==null) throw new System.InvalidOperationException("Для перевода нужна клеточная форма в реестре.");
            var material=VrBattlegrounds.Maps.CoverClassRules.TryExpectedClass(go,out var expected)?expected:definition.defaultMaterial;
            if(!BlockoutRegistryFactory.AdoptCellWall(go,definition,occupied,rectangle?0:yaw,material,BlockoutOpeningSettings.Default,out var reason))
                throw new System.InvalidOperationException(reason);
            return go.GetComponent<BlockoutCellWall>();
        }
    }
}
