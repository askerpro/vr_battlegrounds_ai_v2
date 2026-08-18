using System.Reflection;
using Mirror;
using NUnit.Framework;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Время жизни статической ссылки <see cref="PlayerSession.LocalSession"/> — находка NET-11,
    /// задача T-18.
    ///
    /// Что доказывают тесты. Ссылку ставили в <c>OnStartClient</c> и не снимали никогда:
    /// после отключения она указывала на уничтоженный объект. Заметно это стало после T-11 —
    /// именно через <c>LocalSession</c> клиентский код добирается до своего аватара, и мёртвая
    /// сессия выглядит как живая.
    ///
    /// Второй тест держит очевидную ловушку обратного свойства: обнулять безусловно нельзя.
    /// При переподключении новая сессия встаёт в <c>LocalSession</c> раньше, чем Mirror
    /// доберётся до деспавна старой, и безусловный сброс стёр бы актуальную ссылку.
    /// </summary>
    public class LocalSessionLifetimeTests : MirrorTestHarness
    {
        /// <summary>
        /// Объявляет объект локальным игроком. В бою это делает Mirror при спавне
        /// с <c>NetworkServer.AddPlayerForConnection</c>; сеттер internal, отсюда рефлексия.
        /// </summary>
        private static void MarkAsLocalPlayer(NetworkBehaviour behaviour)
        {
            PropertyInfo property = typeof(NetworkIdentity).GetProperty(
                "isLocalPlayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.IsNotNull(property, "Mirror: не найдено NetworkIdentity.isLocalPlayer — тест несовместим с этой версией SDK.");

            MethodInfo setter = property.GetSetMethod(true);
            Assert.IsNotNull(setter, "Mirror: у NetworkIdentity.isLocalPlayer больше нет сеттера.");

            setter.Invoke(behaviour.netIdentity, new object[] { true });
        }

        /// <summary>Сессия, которую клиент считает своей: как после спавна локального игрока.</summary>
        private PlayerSession CreateLocalSession(string name)
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>(name);
            MarkAsLocalPlayer(session);
            session.OnStartClient();
            return session;
        }

        [Test]
        public void Отключение_снимает_ссылку_на_локальную_сессию()
        {
            SilenceMirrorNoise();

            PlayerSession session = CreateLocalSession("LocalSession");
            Assert.AreSame(session, PlayerSession.LocalSession,
                "Контроль: после старта клиента ссылка обязана указывать на свою сессию.");

            session.OnStopClient();

            Assert.IsNull(PlayerSession.LocalSession,
                "Клиент отключился, а LocalSession всё ещё указывает на уничтожаемую сессию. " +
                "Через неё идёт клиентский доступ к аватару — код увидит живую ссылку на мертвеца.");
        }

        [Test]
        public void Деспавн_старой_сессии_не_сбивает_ссылку_на_новую()
        {
            SilenceMirrorNoise();

            PlayerSession previous = CreateLocalSession("PreviousSession");
            PlayerSession current = CreateLocalSession("CurrentSession");

            Assert.AreSame(current, PlayerSession.LocalSession,
                "Контроль: последняя стартовавшая локальная сессия и есть текущая.");

            // Порядок при переподключении: новая сессия уже заспавнена, старая только
            // теперь доезжает до деспавна.
            previous.OnStopClient();

            Assert.AreSame(current, PlayerSession.LocalSession,
                "Деспавн старой сессии обнулил ссылку на новую. Значит сброс сделан без " +
                "проверки LocalSession == this, и переподключение оставляет клиента без сессии.");
        }
    }
}
