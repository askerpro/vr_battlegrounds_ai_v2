using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Репликация состояния <see cref="PlayerSession"/> с сервера на клиента.
    ///
    /// Данные прогоняются через настоящую сериализацию Mirror и применяются
    /// к отдельному объекту-двойнику — как у удалённого клиента. Host-режим (ярус B)
    /// для этого не годится: там сервер и клиент делят один экземпляр объекта,
    /// поэтому «долетело» получается само собой и не проверяет ничего.
    /// </summary>
    public class PlayerSessionReplicationTests : MirrorTestHarness
    {
        /// <summary>Создаёт сессию на сервере и её клиентского двойника.</summary>
        private void CreateSessionPair(out PlayerSession server, out PlayerSession client)
        {
            server = CreateNetworkComponent<PlayerSession>("ServerSession");
            SpawnOnServer(server);

            client = CreateNetworkComponent<PlayerSession>("ClientSession");
        }

        [Test]
        public void SyncVar_команды_долетает_до_клиента()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            server.TeamIndex = 2;
            server.PlayerName = "Rambo";
            server.Score = 42;

            Assert.AreEqual(0, client.TeamIndex, "Двойник должен стартовать с пустой командой.");

            ReplicateToClient(server, client);

            Assert.AreEqual(2, client.TeamIndex,
                "SyncVar TeamIndex не доехал до клиента — сломана сериализация PlayerSession.");
            Assert.AreEqual("Rambo", client.PlayerName, "SyncVar PlayerName не доехал до клиента.");
            Assert.AreEqual(42, client.Score, "SyncVar Score не доехал до клиента.");

            Assert.IsNotNull(client.Team,
                "TeamIndex доехал, но клиент не смог найти команду в TeamRegistry.");
            Assert.AreEqual(server.Team.displayName, client.Team.displayName,
                "Клиент и сервер видят разные команды при одинаковом TeamIndex.");
        }

        /// <summary>
        /// Находка T-11: связь «сессия → аватар» живёт только на сервере.
        /// <c>ActiveAvatar</c> — обычное C#-свойство, не SyncVar, поэтому у удалённого
        /// клиента оно остаётся null. Единственный путь клиента к аватару —
        /// <c>PlayerController.SessionNetId</c> в обратную сторону.
        ///
        /// Тест зафиксирован «от обратного» намеренно: в host-режиме сервер и клиент
        /// делят объект, там ActiveAvatar не null всегда, и проверка «виден клиенту»
        /// дала бы зелёный на пустом месте.
        /// </summary>
        [Test]
        public void ActiveAvatar_не_долетает_до_клиента()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            GameObject avatarObject = CreateNetworkObject("Avatar");
            avatarObject.AddComponent<UxrActor>();
            PlayerController avatar = avatarObject.AddComponent<PlayerController>();
            EnableNetworking(avatarObject);
            SpawnOnServer(avatar);

            server.TeamIndex = 1;
            server.ActiveAvatar = avatar;
            avatar.SessionNetId = server.netId;

            ReplicateToClient(server, client);

            Assert.AreEqual(1, client.TeamIndex,
                "Контроль: обычный SyncVar доехать обязан, иначе тест ничего не доказывает.");

            Assert.IsNull(client.ActiveAvatar,
                "ActiveAvatar внезапно стал сетевым полем. Если так — T-11 закрыта, тест надо переписать.");

            Assert.AreNotEqual(0u, avatar.SessionNetId,
                "SessionNetId на аватаре — единственная сетевая связь сессии и аватара, она не должна быть нулевой.");
        }
    }
}
