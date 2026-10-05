using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Разрешение замены только явно выбранных корней из сохранённого generated-набора.</summary>
    public static class MapGrowthGeneratedOwnership
    {
        public static BlockoutGeneratedSet Find(Scene scene)
        {
            var container = BlockoutContainerHierarchy.Find(scene, out string reason);
            if (reason != null) throw new InvalidOperationException(reason);
            var sets = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BlockoutGeneratedSet>(true)).ToArray();
            if (sets.Length > 1 || sets.Length == 1 && (container == null || sets[0].gameObject != container.gameObject))
                throw new InvalidOperationException("Список выращенных блоков должен принадлежать единственному контейнеру блокаута.");
            return sets.FirstOrDefault();
        }
        public static GameObject[] Available(Scene scene)
        {
            var set = Find(scene);
            var roots = set != null ? set.Objects.Where(g => g != null).ToArray() : Array.Empty<GameObject>();
            if (roots.Distinct().Count() != roots.Length || roots.Any(g => g.scene != scene || g.transform.parent != set.transform || g.GetComponent<BlockoutBlockInstance>() == null))
                throw new InvalidOperationException("Список выращенных корней повреждён или блок перемещён из контейнера.");
            return roots;
        }
        internal static GameObject[] Resolve(Scene scene, IEnumerable<GameObject> requested)
        {
            var roots = requested?.ToArray() ?? Array.Empty<GameObject>();
            if (roots.Length == 0) return roots;
            var available = new HashSet<GameObject>(Available(scene));
            if (roots.Any(g => g == null || !available.Contains(g)) || roots.Distinct().Count() != roots.Length)
                throw new ArgumentException("Заменять можно только явно выбранные корни из созданного выращивателем набора.");
            if (roots.Any(g => g.GetComponentsInChildren<BlockoutBlockInstance>(true).Any(b => b.gameObject != g)))
                throw new ArgumentException("В заменяемый блок вложен другой блок. Перенесите его в контейнер перед заменой.");
            return roots;
        }
        internal static bool Excludes(Collider collider, GameObject[] roots)
            => roots.Any(g => collider.transform == g.transform || collider.transform.IsChildOf(g.transform));
    }
}
