using Mirror;
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

        /// <summary>
        /// Создаёт заспавненный аватар, привязанный к сессии.
        /// UxrActor обязателен: он в <c>[RequireComponent]</c> у PlayerController.
        /// </summary>
        private PlayerController CreateAvatar(string name, PlayerSession session)
        {
            GameObject avatarObject = CreateNetworkObject(name);
            avatarObject.AddComponent<UxrActor>();
            PlayerController avatar = avatarObject.AddComponent<PlayerController>();
            EnableNetworking(avatarObject);

            avatar.SessionNetId = session.netId;
            SpawnOnServer(avatar);

            return avatar;
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
        /// T-11: связь «сессия → аватар» должна доезжать до клиента.
        ///
        /// До правки тест красный: <c>ActiveAvatar</c> был обычным C#-свойством, жившим
        /// только на сервере, и у удалённого клиента оставался null. Единственной сетевой
        /// связью был <c>PlayerController.SessionNetId</c> в обратную сторону, из-за чего
        /// клиентский код угадывал аватар в <c>NetworkClient.localPlayer</c> и ошибался.
        ///
        /// Проверка идёт через настоящую сериализацию (ярус A+), а не в host-режиме:
        /// там сервер и клиент делят один экземпляр объекта, и «виден клиенту» получилось бы
        /// зелёным на пустом месте.
        /// </summary>
        [Test]
        public void ActiveAvatar_долетает_до_клиента()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            PlayerController avatar = CreateAvatar("Avatar", server);

            server.TeamIndex = 1;
            server.ActiveAvatar = avatar;

            ReplicateToClient(server, client);

            Assert.AreEqual(1, client.TeamIndex,
                "Контроль: обычный SyncVar доехать обязан, иначе тест ничего не доказывает.");

            Assert.AreNotEqual(0u, avatar.SessionNetId,
                "SessionNetId на аватаре — обратная сторона связи, она не должна быть нулевой.");

            Assert.AreEqual(avatar.netId, client.ActiveAvatarNetId,
                "netId аватара не доехал до клиента — связь снова односторонняя.");

            Assert.IsNotNull(client.ActiveAvatar,
                "Связь сессия → аватар не доехала до клиента: клиентскому коду негде спросить, где его аватар.");
            Assert.AreSame(avatar, client.ActiveAvatar,
                "Клиент разрешил ActiveAvatarNetId не в тот аватар.");
        }

        /// <summary>
        /// Смена скина: сервер пересоздаёт аватар, клиент обязан переключиться на новый.
        /// Проверяет, что старая ссылка не залипает — пункт 1 живого прогона из T-11.
        /// </summary>
        [Test]
        public void Смена_аватара_переключает_связь_на_клиенте()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            PlayerController firstAvatar = CreateAvatar("AvatarOld", server);
            server.ActiveAvatar = firstAvatar;
            ReplicateToClient(server, client);

            Assert.AreSame(firstAvatar, client.ActiveAvatar, "Контроль: первый аватар должен доехать.");

            PlayerController secondAvatar = CreateAvatar("AvatarNew", server);
            server.ActiveAvatar = secondAvatar;
            ReplicateToClient(server, client);

            Assert.AreSame(secondAvatar, client.ActiveAvatar,
                "Клиент остался на старом аватаре после смены скина.");
        }

        /// <summary>
        /// Гонка спавнов: netId аватара приезжает раньше самого аватара.
        ///
        /// Порядок доставки спавн-сообщений Mirror не гарантирует, поэтому хук
        /// <c>ActiveAvatarNetId</c> обязан пережить «объекта ещё нет в spawned»
        /// и не оставить связь оборванной навсегда: её закрывает сам аватар
        /// в <c>OnStartClient</c>.
        ///
        /// Отсутствие объекта эмулируется изъятием его из <c>NetworkServer.spawned</c> —
        /// в EditMode это тот же словарь, из которого читает клиентская половина.
        /// </summary>
        [Test]
        public void Аватар_заспавненный_позже_сам_чинит_связь()
        {
            SilenceMirrorNoise();

            PlayerSession session = CreateNetworkComponent<PlayerSession>("Session");
            SpawnOnServer(session);

            PlayerController avatar = CreateAvatar("Avatar", session);
            uint avatarNetId = avatar.netId;

            // SyncVar выставляем напрямую, минуя свойство: так это выглядит на клиенте,
            // где значение приезжает сериализацией, а объект ещё не заспавнен.
            session.ActiveAvatarNetId = avatarNetId;
            NetworkServer.spawned.Remove(avatarNetId);

            Assert.IsNull(session.ActiveAvatar,
                "Объекта нет в spawned — ссылки быть не должно, но и падать разрешение не имеет права.");

            // Аватар стартовал на этой машине и закрывает связь со своей стороны.
            avatar.OnStartClient();

            Assert.AreSame(avatar, session.ActiveAvatar,
                "Аватар заспавнился после сессии — связь так и осталась оборванной.");

            NetworkServer.spawned[avatarNetId] = avatar.netIdentity; // вернуть для штатной уборки
        }

        /// <summary>
        /// <see cref="PlayerController.Session"/> кэшируется, а не ищется в словаре spawned
        /// на каждое обращение: через него идут <c>Team</c> и <c>TeamIndex</c>,
        /// которые вызываются в циклах по всем игрокам.
        ///
        /// Проверка от противного: убираем сессию из spawned. Если свойство ходит в словарь
        /// каждый раз — вернётся null.
        /// </summary>
        [Test]
        public void Session_аватара_кэшируется_а_не_ищется_каждый_раз()
        {
            SilenceMirrorNoise();

            PlayerSession session = CreateNetworkComponent<PlayerSession>("Session");
            SpawnOnServer(session);

            PlayerController avatar = CreateAvatar("Avatar", session);

            Assert.AreSame(session, avatar.Session, "Контроль: сессия должна разрешаться при старте аватара.");

            uint sessionNetId = session.netId;
            NetworkServer.spawned.Remove(sessionNetId);

            Assert.AreSame(session, avatar.Session,
                "Session ищется в spawned при каждом обращении — кэш не работает.");

            NetworkServer.spawned[sessionNetId] = session.netIdentity; // вернуть для штатной уборки
        }
    }
}
