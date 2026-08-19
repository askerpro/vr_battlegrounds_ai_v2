using System.Reflection;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// T-17: <c>MapManager</c> ждёт готовности Mirror по явному условию, а не по таймеру.
    ///
    /// <para>
    /// Что было. <c>DeferredLoadMap</c> ждал события <c>PlayerConnected</c> с таймаутом
    /// в пять секунд. Таймаут стоял ради Server-only режима без host-клиента: там ждать
    /// некого, и каждая загрузка карты стоила пять секунд простоя и предупреждение в лог.
    /// Ожидание по времени — это угадывание: оно не описывает, чего именно ждут.
    /// </para>
    ///
    /// <para>
    /// Что стало. Условие названо: ни одно соединение не находится в середине
    /// <c>AddPlayer</c>, то есть нет соединения с <c>isReady</c> и без <c>identity</c>.
    /// Эти тесты фиксируют именно контракт — все четыре сочетания
    /// <c>isReady</c> × <c>identity</c>.
    /// </para>
    /// </summary>
    public class MapLoadReadinessTests : MirrorTestHarness
    {
        /// <summary>
        /// <c>ConnectionsSettled</c> приватный: это внутреннее условие менеджера,
        /// а не часть его API — T-17 прямо запрещает расширять API менеджеров.
        /// Тест читает его рефлексией, как харнесс читает внутренности Mirror.
        /// </summary>
        private static bool ConnectionsSettled()
        {
            MethodInfo method = typeof(MapManager).GetMethod("ConnectionsSettled",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method,
                "MapManager.ConnectionsSettled не найден. Условие готовности переименовали — " +
                "проверять, что загрузка карты не ждёт по таймеру, стало нечем.");

            return (bool)method.Invoke(null, null);
        }

        /// <summary>Кладёт в <c>NetworkServer.connections</c> соединение с нужным состоянием.</summary>
        private NetworkConnectionToClient AddFakeConnection(int id, bool isReady, bool withIdentity)
        {
            NetworkConnectionToClient conn = new NetworkConnectionToClient(id);
            conn.isReady = isReady;

            if (withIdentity)
            {
                PlayerSession session = CreateNetworkComponent<PlayerSession>($"Session{id}");
                SpawnOnServer(session);

                // identity объявлен с internal set — ставим через сеттер рефлексией.
                PropertyInfo identityProperty = typeof(NetworkConnection).GetProperty("identity",
                    BindingFlags.Public | BindingFlags.Instance);
                identityProperty.SetValue(conn, session.netIdentity);
            }

            NetworkServer.connections[id] = conn;
            return conn;
        }

        [TearDown]
        public void ClearFakeConnections()
        {
            NetworkServer.connections.Clear();
        }

        [Test]
        public void Без_соединений_условие_выполнено_сразу()
        {
            SilenceMirrorNoise();
            NetworkServer.connections.Clear();

            Assert.IsTrue(ConnectionsSettled(),
                "Выделенный сервер без клиентов обязан грузить карту немедленно. " +
                "Именно этот случай раньше стоил пяти секунд таймаута.");
        }

        [Test]
        public void Соединение_в_середине_AddPlayer_задерживает_смену_сцены()
        {
            SilenceMirrorNoise();
            NetworkServer.connections.Clear();

            AddFakeConnection(1, isReady: true, withIdentity: false);

            Assert.IsFalse(ConnectionsSettled(),
                "Клиент сообщил о готовности, но сессию сервер ему ещё не создал. " +
                "Сменить сцену сейчас — получить второй AddPlayer в новой сцене.");
        }

        [Test]
        public void Соединение_с_сессией_не_задерживает_смену_сцены()
        {
            SilenceMirrorNoise();
            NetworkServer.connections.Clear();

            AddFakeConnection(1, isReady: true, withIdentity: true);

            Assert.IsTrue(ConnectionsSettled(),
                "AddPlayer завершён — ждать больше нечего.");
        }

        [Test]
        public void Ещё_не_готовое_соединение_не_задерживает_смену_сцены()
        {
            SilenceMirrorNoise();
            NetworkServer.connections.Clear();

            AddFakeConnection(1, isReady: false, withIdentity: false);

            Assert.IsTrue(ConnectionsSettled(),
                "Соединение, которое ещё не догрузило текущую сцену, AddPlayer не начинало " +
                "и после ServerChangeScene пройдёт весь путь заново. Ожидание такого клиента " +
                "означало бы вечное зависание на роли наблюдателя: она сессию не создаёт вовсе.");
        }

        [Test]
        public void Одно_незавершённое_соединение_задерживает_всю_смену_сцены()
        {
            SilenceMirrorNoise();
            NetworkServer.connections.Clear();

            AddFakeConnection(1, isReady: true, withIdentity: true);
            AddFakeConnection(2, isReady: true, withIdentity: false);

            Assert.IsFalse(ConnectionsSettled(),
                "Условие обязано быть по всем соединениям сразу, а не по первому подходящему.");
        }
    }
}
