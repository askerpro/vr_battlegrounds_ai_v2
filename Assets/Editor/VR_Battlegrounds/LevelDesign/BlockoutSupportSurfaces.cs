using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Опорный пол задаёт высоту размещения, но не является препятствием.</summary>
    public static class BlockoutSupportSurfaces
    {
        /// <summary>Корневой коллайдер выключенного GO тоже возвращается GetComponentsInChildren(false).</summary>
        public static bool IsActiveSolid(Collider collider)
            => collider != null && collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy;

        public static bool IsSupportSurface(Scene scene, Collider collider)
        {
            if(!IsActiveSolid(collider)||collider.gameObject.scene!=scene)return false;
            // Игровой блок и физический резерв сохраняют свою роль даже при ошибочном слое Ground.
            if(collider.GetComponentInParent<PhysicalObstacleMarker>()!=null||collider.GetComponentInParent<PhysicalObstacleProtection>()!=null
                ||collider.GetComponentInParent<BlockoutBlockInstance>()!=null||collider.GetComponentInParent<BlockoutCellWall>()!=null)return false;
            var arena=PhysicalArenaPanel.Find(scene);
            if(arena==null&&PhysicalArenaPanel.HasDefinitions(scene))return false;
            if(arena!=null&&collider==arena.floor)return true;
            if(collider.gameObject.layer!=LayerMask.NameToLayer("Ground"))return false;
            if(arena!=null&&!collider.transform.IsChildOf(arena.transform))return false;
            if(!BlockoutGrid.TryFloor(scene,out var floor))return false;
            var bounds=collider.bounds;
            if(Mathf.Abs(bounds.max.y-floor.max.y)>.002f||bounds.size.x<=0||bounds.size.z<=0)return false;
            // Дополнительный меш пола должен быть плоским в мировых координатах;
            // импортированный меш может иметь поворот 270/90 без наклона поверхности.
            if(collider is MeshCollider)return bounds.size.y<=.02f;
            return collider is BoxCollider&&Mathf.Abs(Vector3.Dot(collider.transform.up,Vector3.up))>.99999f;
        }
    }
}
