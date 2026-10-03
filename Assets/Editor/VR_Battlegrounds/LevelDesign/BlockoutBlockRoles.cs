using UnityEditor;
using UnityEngine;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Роль защиты не зависит от имени экземпляра. Совместимость старых оболочек до явной связи с маркером.</summary>
    public static class BlockoutBlockRoles
    {
        private const string LegacyProtectionSourceGuid="e02bc19f379cee64780db790b217b570";
        public static bool IsProtection(GameObject go)
        {
            if(go==null)return false;
            if(go.GetComponentInParent<PhysicalObstacleProtection>()!=null)return true;
            var root=PrefabUtility.GetNearestPrefabInstanceRoot(go);
            // Новая игровая форма этого же источника имеет явный BlockInstance и не является защитой.
            return root!=null&&root.GetComponent<BlockoutBlockInstance>()==null
                &&AssetDatabase.AssetPathToGUID(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root))==LegacyProtectionSourceGuid;
        }
    }
}
