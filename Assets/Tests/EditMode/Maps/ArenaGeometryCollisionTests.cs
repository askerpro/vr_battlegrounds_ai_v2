using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Видимая геометрия модели арены — твёрдая.
    ///
    /// <para>
    /// Меш из модели арены (<c>Assets/Models/Arenas/**.fbx</c>) без коллайдера — это
    /// препятствие, которое видно, но сквозь которое летят пули и падает брошенное оружие,
    /// а укрыться за ним нельзя. Так было со столбами <c>nalchik_tolst</c>: они ещё и не
    /// получили тег <c>Environment</c>, потому что тег ставится по коллайдеру.
    /// Декор арсенала, ценники и прочее, что не из модели арены, сюда не попадает.
    /// </para>
    /// </summary>
    public class ArenaGeometryCollisionTests
    {
        private static readonly string[] ArenaPrefabRoots = { "Assets/Prefabs/Arenas" };
        private const string ArenaModelsRoot = "Assets/Models/Arenas/";

        [Test]
        public void ArenaModelMeshes_HaveSolidCollider()
        {
            var missing = new List<string>();
            int checks  = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", ArenaPrefabRoots))
            {
                string path   = AssetDatabase.GUIDToAssetPath(guid);
                var    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(renderer.gameObject);
                    if (source == null || !AssetDatabase.GetAssetPath(source).StartsWith(ArenaModelsRoot)) continue;

                    checks++;
                    if (!HasSolidCollider(renderer.gameObject))
                        missing.Add($"{path} → {renderer.name}");
                }
            }

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одного меша модели арены — тест ничего не проверил.");
            Assert.IsEmpty(missing, "Геометрия арены без твёрдого коллайдера:\n" + string.Join("\n", missing));
        }

        private static bool HasSolidCollider(GameObject go)
        {
            foreach (Collider collider in go.GetComponents<Collider>())
                if (!collider.isTrigger) return true;
            return false;
        }
    }
}
