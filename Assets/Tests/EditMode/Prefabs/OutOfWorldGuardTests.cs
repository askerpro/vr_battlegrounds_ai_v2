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

        /// <summary>
        /// Кто удаляет выпавший предмет. Свой <c>netId</c> есть только у заспавненного
        /// отдельно: встроенный магазин <c>Machinegun</c>/<c>Shotgun</c>/<c>M16</c> его не имеет,
        /// и <c>NetworkServer.Destroy</c> до клиентов не дойдёт — такой предмет каждая машина
        /// удаляет сама (позиция синхронизирована, падение видят все).
        /// </summary>
        [TestCase(true,  true,  true,  true,  OutOfWorldGuard.Removal.Keep,          TestName = "В руке — не трогать")]
        [TestCase(false, true,  true,  true,  OutOfWorldGuard.Removal.ServerDestroy, TestName = "Хост, свой netId — удаляет сервер")]
        [TestCase(false, true,  false, true,  OutOfWorldGuard.Removal.ServerDestroy, TestName = "Выделенный сервер, свой netId — удаляет сервер")]
        [TestCase(false, false, true,  true,  OutOfWorldGuard.Removal.WaitForServer, TestName = "Клиент, свой netId — ждёт сервер")]
        [TestCase(false, false, true,  false, OutOfWorldGuard.Removal.LocalDestroy,  TestName = "Клиент, без netId — удаляет сам")]
        [TestCase(false, true,  true,  false, OutOfWorldGuard.Removal.LocalDestroy,  TestName = "Хост, без netId — удаляет сам")]
        [TestCase(false, false, false, false, OutOfWorldGuard.Removal.LocalDestroy,  TestName = "Вне сессии — удаляет сам")]
        public void Decide_ChoosesRemovalByNetworkRole(bool grabbed, bool server, bool client, bool hasOwnNetId, OutOfWorldGuard.Removal expected)
        {
            Assert.AreEqual(expected, OutOfWorldGuard.Decide(grabbed, server, client, hasOwnNetId));
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
