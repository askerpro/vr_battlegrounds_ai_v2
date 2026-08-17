using Mirror;
using NUnit.Framework;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Ярус B: сервер плюс локальный клиент в одном процессе.
    ///
    /// Проверка самого яруса — что сообщения между <c>LocalConnectionToServer</c>
    /// и <c>LocalConnectionToClient</c> реально ходят и объект, заспавненный сервером,
    /// появляется в <c>NetworkClient.spawned</c>.
    /// </summary>
    public class HostClientHarnessTests : MirrorTestHarness
    {
        protected override bool NeedsLocalClient => true;

        [Test]
        public void Локальный_клиент_поднимается_и_готов()
        {
            SilenceMirrorNoise();

            Assert.IsTrue(NetworkServer.active, "Сервер не поднялся.");
            Assert.IsTrue(NetworkServer.activeHost, "Локального соединения нет — ярус B не собрался.");
            Assert.AreEqual(1, NetworkServer.connections.Count,
                "Локальное соединение не зарегистрировано на сервере: пропущен HostMode.InvokeOnConnected().");
            Assert.IsTrue(NetworkServer.localConnection.isReady,
                "Сервер не принял ReadyMessage. Обычная причина — соединение не помечено isAuthenticated.");
        }

        [Test]
        public void Заспавненный_объект_виден_локальному_клиенту()
        {
            SilenceMirrorNoise();

            PlayerSession session = CreateNetworkComponent<PlayerSession>("Session");
            SpawnOnServer(session);
            PumpNetwork();

            Assert.AreNotEqual(0u, session.netId, "Сервер не выдал netId — объект не заспавнился.");
            Assert.IsTrue(NetworkClient.spawned.ContainsKey(session.netId),
                "SpawnMessage не дошёл до локального клиента: очередь сообщений не прокручена или соединение не готово.");
            Assert.IsTrue(session.isClient,
                "Объект заспавнен, но не считается клиентским — host-режим собран неверно.");
        }
    }
}
