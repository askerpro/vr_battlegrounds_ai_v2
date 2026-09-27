using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using kcp2k;
using Mirror.Discovery;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Порт сервера не должен оставаться занятым из-за дочернего процесса редактора.
    ///
    /// <para>
    /// Что было. В Windows дочерний процесс по умолчанию получает копии всех наследуемых
    /// дескрипторов родителя, а Mono создаёт сокеты наследуемыми. MCP-сервер, запущенный
    /// посреди Play Mode, унаследовал сокеты KCP (7778) и обнаружения (47777). Mirror при
    /// выходе закрыл свои копии, но порт остался за python, и следующий <c>StartHost</c>
    /// падал с «только одно использование адреса сокета». Владельцем порта Windows при этом
    /// показывает Unity — это сбивает с толку. Разбор — <c>Docs/troubleshooting.md</c>.
    /// </para>
    ///
    /// <para>
    /// Что стало. Серверные сокеты KCP и обнаружения помечаются ненаследуемыми сразу
    /// после создания (<c>Mirror.SocketInheritance</c>). Тесты воспроизводят порядок
    /// событий: открыть сокет → запустить дочерний процесс → закрыть сокет → порт свободен.
    /// </para>
    /// </summary>
    public class SocketInheritanceTests
    {
        private Process _child;

        [SetUp]
        public void SetUp()
        {
            Assume.That(Application.platform, Is.EqualTo(RuntimePlatform.WindowsEditor),
                "Наследование дескрипторов — поведение CreateProcess в Windows.");
        }

        [TearDown]
        public void TearDown()
        {
            KillChildTree();
        }

        [Test]
        public void Порт_KCP_освобождается_несмотря_на_дочерний_процесс()
        {
            ushort port = FreeUdpPort();

            KcpServer server = NewKcpServer();
            server.Start(port);

            SpawnChild();
            server.Stop();

            KcpServer probe = NewKcpServer();
            try
            {
                Assert.DoesNotThrow(() => probe.Start(port),
                    $"Порт {port} занят после KcpServer.Stop(): сокет унаследовал дочерний процесс. " +
                    "Серверный сокет KCP должен быть ненаследуемым.");
            }
            finally
            {
                probe.Stop();
            }
        }

        [Test]
        public void Порт_обнаружения_освобождается_несмотря_на_дочерний_процесс()
        {
            ushort port = FreeUdpPort();

            var go = new GameObject("DiscoveryUnderTest");
            try
            {
                NetworkDiscovery discovery = go.AddComponent<NetworkDiscovery>();
                FieldInfo portField = typeof(NetworkDiscoveryBase<ServerRequest, ServerResponse>)
                    .GetField("serverBroadcastListenPort", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(portField, "В NetworkDiscoveryBase не найдено поле serverBroadcastListenPort.");
                portField.SetValue(discovery, (int)port);

                discovery.AdvertiseServer();

                SpawnChild();
                discovery.StopDiscovery();

                Assert.IsTrue(CanBindUdp(port),
                    $"Порт {port} занят после StopDiscovery(): сокет унаследовал дочерний процесс. " +
                    "Серверный сокет обнаружения должен быть ненаследуемым.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static KcpServer NewKcpServer()
        {
            return new KcpServer(
                (id, endPoint) => { },
                (id, data, channel) => { },
                id => { },
                (id, error, reason) => { },
                new KcpConfig(DualMode: true, RecvBufferSize: 1024 * 64, SendBufferSize: 1024 * 64));
        }

        /// <summary>
        /// Дочерний процесс, который живёт дольше теста. Запуск через CreateProcess
        /// с наследованием дескрипторов — так же, как MCP запускает свой сервер.
        /// </summary>
        private void SpawnChild()
        {
            var info = new ProcessStartInfo("cmd.exe", "/c ping -n 60 127.0.0.1 >nul")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            _child = Process.Start(info);
        }

        /// <summary>Убивает дерево целиком: <c>ping</c> тоже держит унаследованные дескрипторы.</summary>
        private void KillChildTree()
        {
            if (_child == null)
            {
                return;
            }

            try
            {
                if (!_child.HasExited)
                {
                    using (Process kill = Process.Start(new ProcessStartInfo("taskkill", $"/T /F /PID {_child.Id}")
                           {
                               UseShellExecute = false,
                               CreateNoWindow = true,
                           }))
                    {
                        kill?.WaitForExit(5000);
                    }
                }
            }
            catch (Exception)
            {
                // процесс уже завершился — убирать нечего
            }

            _child.Dispose();
            _child = null;
        }

        private static ushort FreeUdpPort()
        {
            using (var probe = new UdpClient(0))
            {
                return (ushort)((IPEndPoint)probe.Client.LocalEndPoint).Port;
            }
        }

        private static bool CanBindUdp(ushort port)
        {
            try
            {
                using (new UdpClient(port))
                {
                    return true;
                }
            }
            catch (SocketException)
            {
                return false;
            }
        }
    }
}
