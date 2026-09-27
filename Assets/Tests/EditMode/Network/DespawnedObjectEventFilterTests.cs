using Mirror;
using NUnit.Framework;
using UltimateXR.Core.Unique;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    ///     Объект, который Mirror уже снял со спавна, не шлёт событий в канал состояния.
    ///
    ///     <para>
    ///     Замер 2026-09-27, хост + клиент MPPM, смена скина: сервер получил
    ///     <c>UpdateTeleportState(False, False, False, identity)</c> от
    ///     <c>Player_2178 (Local)/…/TeleportLeft</c> (свой аватар клиента) и от
    ///     <c>Player_4153 (Remote)/…/TeleportLeft</c> (копия чужого) — это
    ///     <c>UxrTeleportLocomotion.OnDisable</c> аватара, уже снятого сетью.
    ///     </para>
    ///
    ///     <para>
    ///     Состояние «снят, но ещё жив» воспроизводится как в <c>NetworkServer.DestroyObject</c>:
    ///     объект убран из <c>spawned</c>, <c>netId</c> остаётся, <c>Destroy</c> отложен.
    ///     </para>
    /// </summary>
    public class DespawnedObjectEventFilterTests : MirrorTestHarness
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

        private UxrGrabbableObjectAnchor CreateNetworkObjectWithChild(out NetworkIdentity identity)
        {
            GameObject go = CreateNetworkObject("NetworkAvatar");
            identity = EnableNetworking(go);

            var child = new GameObject("TeleportLeft");
            child.transform.SetParent(go.transform);

            _registered = go;
            return child.AddComponent<UxrGrabbableObjectAnchor>();
        }

        [Test]
        public void Снятый_со_спавна_объект_отсекается()
        {
            SilenceMirrorNoise();
            UxrGrabbableObjectAnchor component = CreateNetworkObjectWithChild(out NetworkIdentity identity);
            SpawnOnServer(identity);
            Assume.That(identity.netId, Is.Not.EqualTo(0u), "Контроль: спавн выдал netId.");

            NetworkServer.spawned.Remove(identity.netId);

            Assert.IsTrue(DespawnedObjectEventFilter.ShouldDrop(component),
                "OnDisable снятого аватара ушёл бы стороне, у которой объекта уже нет.");
        }

        [Test]
        public void Заспавненный_объект_проходит()
        {
            SilenceMirrorNoise();
            UxrGrabbableObjectAnchor component = CreateNetworkObjectWithChild(out NetworkIdentity identity);
            SpawnOnServer(identity);

            Assert.IsFalse(DespawnedObjectEventFilter.ShouldDrop(component),
                "Живой сетевой объект — обычный источник событий (здоровье, захваты).");
        }

        [Test]
        public void Объект_до_спавна_проходит_к_гейту()
        {
            SilenceMirrorNoise();
            UxrGrabbableObjectAnchor component = CreateNetworkObjectWithChild(out _);

            Assert.IsFalse(DespawnedObjectEventFilter.ShouldDrop(component),
                "До спавна netId = 0: событие должен придержать AvatarStateEventGate, а не выбросить фильтр.");
        }

        [Test]
        public void Объект_без_NetworkIdentity_проходит()
        {
            SilenceMirrorNoise();
            GameObject go = CreateObject("WorldItem");
            UxrGrabbableObject item = go.AddComponent<UxrGrabbableObject>();
            _registered = go;

            Assert.IsFalse(DespawnedObjectEventFilter.ShouldDrop(item));
        }
    }
}
