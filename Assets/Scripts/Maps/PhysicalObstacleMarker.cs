using System;
using UnityEngine;

namespace VrBattlegrounds.LevelDesign
{
    /// <summary>Физический резерв: координаты относительно арены или явные коллайдеры.</summary>
    [DisallowMultipleComponent]
    public sealed class PhysicalObstacleMarker : MonoBehaviour
    {
        public string markerId="";
        public bool useColliders;
        public Vector3 center;
        public Vector3 size= new Vector3(.4f,2.5f,.4f);
        public float yaw;
        public Collider[] sourceColliders=Array.Empty<Collider>();
        private void Reset() => markerId=Guid.NewGuid().ToString("N");
        private void OnEnable()
        {
            if (Application.IsPlaying(gameObject)) PhysicalArenaSources.ApplyProjectileLayers(this);
        }
        public bool TryBounds(PhysicalArenaDefinition arena,out Bounds bounds,out string reason)
        {
            bounds=default; reason="";
            if(arena==null||!transform.IsChildOf(arena.transform)||string.IsNullOrWhiteSpace(markerId)) {reason="Маркер должен принадлежать арене и иметь ID.";return false;}
            if(useColliders)
            {
                if(sourceColliders==null||sourceColliders.Length==0){reason="Список Collider пуст.";return false;}
                bool first=true;
                foreach(var c in sourceColliders)
                {
                    if(c==null||!c.enabled||c.isTrigger||!c.gameObject.activeInHierarchy||!c.transform.IsChildOf(arena.transform)||c==arena.floor
                       ||(!(c is BoxCollider)&&!(c is SphereCollider)&&!(c is CapsuleCollider)&&!(c is MeshCollider))) {reason="Неверный/неподдерживаемый Collider или пол вместо препятствия.";return false;}
                    if(first){bounds=c.bounds;first=false;}else bounds.Encapsulate(c.bounds);
                }
            }
            else
            {
                if(!PhysicalArenaDefinition.Finite(center.x)||!PhysicalArenaDefinition.Finite(center.y)||!PhysicalArenaDefinition.Finite(center.z)||!PhysicalArenaDefinition.Finite(size.x)||!PhysicalArenaDefinition.Finite(size.y)||!PhysicalArenaDefinition.Finite(size.z)||!PhysicalArenaDefinition.Finite(yaw)||size.x<=0||size.y<=0||size.z<=0){reason="Координаты должны быть конечными, размеры положительными.";return false;}
                var rotation=Quaternion.Euler(0,yaw,0); var worldCenter=arena.transform.TransformPoint(center);
                bounds=new Bounds(worldCenter,Vector3.zero);
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2) bounds.Encapsulate(worldCenter+rotation*Vector3.Scale(size*.5f,new Vector3(x,y,z)));
            }
            if(!PhysicalArenaDefinition.Finite(bounds.center.x)||!PhysicalArenaDefinition.Finite(bounds.center.y)||!PhysicalArenaDefinition.Finite(bounds.center.z)||!PhysicalArenaDefinition.Finite(bounds.size.x)||!PhysicalArenaDefinition.Finite(bounds.size.y)||!PhysicalArenaDefinition.Finite(bounds.size.z)||bounds.size.x<=0||bounds.size.y<=0||bounds.size.z<=0){reason="Пустой или неконечный объём препятствия.";return false;}
            return true;
        }
    }
}
