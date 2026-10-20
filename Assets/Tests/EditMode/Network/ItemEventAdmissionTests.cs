using Mirror;
using NUnit.Framework;
using UltimateXR.Core.Unique;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    ///     Допуск событий сетевых предметов (контракт avatar-root-event-admission v2,
    ///     tasks/e2e-combat/contracts/avatar-root-event-admission-v2.md).
    ///
    ///     <para>
    ///     Замер 2026-10-20, сервер + 2 клиента MPPM (e2e <c>scripted-combat-round</c>): стрелок отпустил курок, пока
    ///     сервер в Resolution уже снимал его оружие, — сервер получил <c>SyncTriggerPressStates</c> для уничтоженного
    ///     Viper и писал Error декодера. Клиент кладёт в конверт netId предмета; сервер до декодера отбрасывает
    ///     событие предмета, которого уже нет в <c>spawned</c>.
    ///     </para>
    /// </summary>
    public class ItemEventAdmissionTests : MirrorTestHarness
    {
        private GameObject _registered;

        [TearDown]
        public void UnregisterIds()
        {
            if (_registered == null) return;
            foreach (IUxrUniqueId unique in _registered.GetComponentsInChildren<IUxrUniqueId>(true))
                unique.Unregister();
            _registered = null;
        }

        private UxrGrabbableObjectAnchor CreateItemWithChild(out NetworkIdentity identity)
        {
            GameObject go = CreateNetworkObject("NetworkItem");
            identity = EnableNetworking(go);

            var child = new GameObject("Slide");
            child.transform.SetParent(go.transform);

            _registered = go;
            return child.AddComponent<UxrGrabbableObjectAnchor>();
        }

        [Test]
        public void Событие_заспавненного_предмета_несёт_его_netId_и_допускается()
        {
            SilenceMirrorNoise();
            UxrGrabbableObjectAnchor component = CreateItemWithChild(out NetworkIdentity identity);
            SpawnOnServer(identity);
            Assume.That(identity.netId, Is.Not.EqualTo(0u), "Контроль: спавн выдал netId.");

            uint netId = ItemEventAdmission.CaptureOutgoing(component, NetworkServer.spawned);

            Assert.AreEqual(identity.netId, netId, "Ребёнок предмета адресуется ближайшей NetworkIdentity.");
            Assert.IsTrue(ItemEventAdmission.IsAvailable(netId, NetworkServer.spawned));
        }

        [Test]
        public void Снятый_предмет_недоступен_серверу()
        {
            SilenceMirrorNoise();
            UxrGrabbableObjectAnchor component = CreateItemWithChild(out NetworkIdentity identity);
            SpawnOnServer(identity);
            uint netId = ItemEventAdmission.CaptureOutgoing(component, NetworkServer.spawned);

            NetworkServer.spawned.Remove(identity.netId);

            Assert.IsFalse(ItemEventAdmission.IsAvailable(netId, NetworkServer.spawned),
                "Событие предмета, снятого, пока оно было в пути, сервер отбрасывает до декодера.");
        }

        [Test]
        public void Предмет_до_спавна_и_объект_без_NetworkIdentity_идут_прежним_путём()
        {
            SilenceMirrorNoise();
            UxrGrabbableObjectAnchor unspawned = CreateItemWithChild(out _);
            Assert.AreEqual(0u, ItemEventAdmission.CaptureOutgoing(unspawned, NetworkServer.spawned),
                "До спавна netId = 0: конверт без предмета, событие идёт прежним путём.");

            UnregisterIds();
            GameObject world = CreateObject("WorldItem");
            UxrGrabbableObject item = world.AddComponent<UxrGrabbableObject>();
            _registered = world;
            Assert.AreEqual(0u, ItemEventAdmission.CaptureOutgoing(item, NetworkServer.spawned));
        }

        [Test]
        public void Нулевой_netId_не_считается_доступным_предметом()
        {
            Assert.IsFalse(ItemEventAdmission.IsAvailable(0u, NetworkServer.spawned));
        }
    }
}
