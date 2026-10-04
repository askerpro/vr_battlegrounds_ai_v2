using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VrBattlegrounds.LevelDesign
{
    /// <summary>Коллайдеры источников разметки не являются игровой геометрией попаданий и анализа.</summary>
    public static class PhysicalArenaSources
    {
        public static HashSet<Collider> Collect(Scene scene)
        {
            var result = new HashSet<Collider>();
            if (!scene.IsValid() || !scene.isLoaded) return result;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var layout in root.GetComponentsInChildren<PhysicalArenaLayout>(true))
                    foreach (var collider in layout.GetComponentsInChildren<Collider>(true)) result.Add(collider);
                foreach (var marker in root.GetComponentsInChildren<PhysicalObstacleMarker>(true))
                    foreach (var collider in Of(marker)) result.Add(collider);
            }
            return result;
        }

        /// <summary>Роль задана ссылками маркера и компонентами, без поиска имён и тегов.</summary>
        public static IEnumerable<Collider> Of(PhysicalObstacleMarker marker)
        {
            if (marker == null) yield break;
            var arena = marker.GetComponentInParent<PhysicalArenaDefinition>(true);
            if (arena == null) yield break;
            foreach (var collider in marker.GetComponentsInChildren<Collider>(true))
                if (IsSource(collider, arena)) yield return collider;
            if (!marker.useColliders || marker.sourceColliders == null) yield break;
            foreach (var source in marker.sourceColliders)
            {
                if (!IsSource(source, arena)) continue;
                // Слой принадлежит GameObject: все его коллайдеры должны иметь одну роль.
                foreach (var collider in source.GetComponents<Collider>())
                    if (IsSource(collider, arena)) yield return collider;
            }
        }

        private static bool IsSource(Collider collider, PhysicalArenaDefinition arena) =>
            collider != null && collider.transform.IsChildOf(arena.transform)
            && (arena.floor == null || collider.gameObject != arena.floor.gameObject)
            && collider.GetComponentInParent<BlockoutBlockInstance>() == null
            && collider.GetComponentInParent<PhysicalObstacleProtection>() == null
            && collider.GetComponentInParent<VrBattlegrounds.Maps.CoverSurface>() == null;

        /// <summary>В Play Mode источник исключён из HitLayers.ProjectileMask и штатных лучей Unity; резерв сохраняется.</summary>
        public static void ApplyProjectileLayers(PhysicalObstacleMarker marker)
        {
            int layer = LayerMask.NameToLayer("Ignore Raycast");
            foreach (var collider in Of(marker)) collider.gameObject.layer = layer;
        }
    }
}
