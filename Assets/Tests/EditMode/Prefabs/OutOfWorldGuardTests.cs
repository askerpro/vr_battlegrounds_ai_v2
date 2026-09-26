using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Предмет, выпавший из мира, удаляется (PHY-01).
    ///
    /// <para>
    /// Страховка поверх <see cref="WeaponDropPhysicsTests" />: если коллизии снова дадут дыру,
    /// предмет не будет падать вечно и слать <c>UpdateRigidbody</c> по сети без конца.
    /// Сетевая ветка (<c>NetworkServer.Destroy</c>) здесь не проверяется — в EditMode нет сессии;
    /// проверяется локальная, с тем же решением «удалять или нет».
    /// </para>
    /// </summary>
    public class OutOfWorldGuardTests
    {
        private static readonly string[] WeaponRoots = { "Assets/Prefabs/Weapons" };

        [Test]
        public void DynamicWeaponPrefabs_HaveOutOfWorldGuard()
        {
            var missing = new List<string>();

            foreach (var prefab in DynamicPrefabs())
                if (prefab.GetComponent<OutOfWorldGuard>() == null)
                    missing.Add(AssetDatabase.GetAssetPath(prefab));

            Assert.IsEmpty(missing, "Нет OutOfWorldGuard:\n" + string.Join("\n", missing));
        }

        [Test]
        public void Guard_RemovesOnlyBelowKillY()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();

            try
            {
                foreach (var prefab in DynamicPrefabs())
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    var guard    = instance.GetComponent<OutOfWorldGuard>();
                    Assert.IsNotNull(guard, $"{prefab.name}: нет OutOfWorldGuard");

                    instance.transform.position = new Vector3(0f, guard.KillY + 1f, 0f);
                    Assert.IsFalse(guard.CheckNow(), $"{prefab.name}: удалён выше порога");
                    Assert.IsTrue(instance != null, $"{prefab.name}: объект пропал выше порога");

                    instance.transform.position = new Vector3(0f, guard.KillY - 1f, 0f);
                    Assert.IsTrue(guard.CheckNow(), $"{prefab.name}: не удалён ниже порога");
                    Assert.IsTrue(instance == null, $"{prefab.name}: объект остался ниже порога");
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static IEnumerable<GameObject> DynamicPrefabs()
        {
            int found = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", WeaponRoots))
            {
                var prefab    = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                var grabbable = prefab.GetComponent<UxrGrabbableObject>();

                if (grabbable == null || prefab.GetComponent<Rigidbody>() == null) continue;
                if (!grabbable.RigidBodyDynamicOnRelease) continue;

                found++;
                yield return prefab;
            }

            Assert.That(found, Is.GreaterThan(0), "Не найдено ни одного префаба оружия — тест ничего не проверил.");
        }
    }
}
