// Ярус C (два процесса) — изоляция портов e2e-прогона в собранном плеере.
#if !VRBG_NO_E2E
using System;
using System.Net;
using System.Net.Sockets;
using Mirror;
using Mirror.Discovery;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.DevTools.E2E
{
    /// <summary>
    /// Единственный writer портов e2e-прогона в собранном плеере (аналог Editor-владельца
    /// <c>PlayLaunchNetworkPorts</c>). Пару задаёт дирижёр аргументами <c>-e2eNetworkPort</c>/<c>-e2eDiscoveryPort</c>,
    /// здесь она только применяется — через шов <see cref="DebugBootstrapGate.PlayerBeforeNetworkStart"/>,
    /// который <see cref="GameNetworkDiscovery"/> вызывает перед стартом сети каждой роли.
    ///
    /// Пара применяется при каждом вызове к переданным менеджеру и Discovery, без кэша и без «один раз»:
    /// при возврате в Offline появляется новый экземпляр MANAGERS со своим транспортом и Discovery
    /// на портах сцены. Отказ запоминается в <see cref="LastRefusal"/> — по нему <see cref="E2ERunner"/>
    /// валит прогон, не давая сценарию уйти в свой фоллбэк на порты по умолчанию.
    /// </summary>
    public static class E2EPlayerPorts
    {
        private static int _networkPort;
        private static int _discoveryPort;
        private static string _contextError;

        /// <summary>Последний отказ подготовки портов; null — отказов не было.</summary>
        public static string LastRefusal { get; private set; }

        /// <summary>
        /// Ставит шов плеера. Вызывается из бутстрапа харнесса (BeforeSceneLoad), то есть до Start
        /// <see cref="GameNetworkDiscovery"/> первой сцены. В редакторе ничего не делает: там порты ведёт Editor-владелец.
        /// Ошибка разбора пары тоже ставит шов — он запрещает старт сети, чтобы процесс не ушёл на 7778/47777.
        /// </summary>
        public static void Install(E2EContext context)
        {
            if (context == null || Application.isEditor)
                return;

            if (context.PortError != null)
            {
                _contextError = context.PortError;
                DebugBootstrapGate.PlayerBeforeNetworkStart = Prepare;
                GameLog.Network.Error($"[E2E] Порты прогона заданы неверно, старт сети запрещён: {context.PortError}");
                return;
            }

            if (!context.HasPorts)
                return;

            _networkPort = context.NetworkPort;
            _discoveryPort = context.DiscoveryPort;
            DebugBootstrapGate.PlayerBeforeNetworkStart = Prepare;
            GameLog.Network.Info($"[E2E] Порты прогона: KCP {_networkPort}, Discovery {_discoveryPort}");
        }

        /// <summary>
        /// Применяет пару к транспорту менеджера и к Discovery до старта роли. Возвращает код отказа или null.
        /// Проверки — как у Editor-владельца: диапазон, различие портов, PortTransport, наличие Discovery,
        /// неактивная сеть, а для Server/Host ещё preflight свободы портов. Клиент применяет оба порта:
        /// Discovery — для broadcast, KCP — для прямого фоллбэка по адресу.
        /// </summary>
        private static string Prepare(NetworkManager manager, NetworkDiscovery discovery, GameNetworkDiscovery.AppRole role)
        {
            try
            {
                if (_contextError != null)
                    return Refuse("PortInvalid: " + _contextError);

                string invalid = E2EContext.ValidatePorts(_networkPort, _discoveryPort);
                if (invalid != null)
                    return Refuse("PortInvalid: " + invalid);
                if (manager == null || !(manager.transport is PortTransport transport))
                    return Refuse("PortTransportUnsupported");
                if (discovery == null)
                    return Refuse("DiscoveryMissing");
                // Discovery рекламирует URI своего транспорта: чужой транспорт отдал бы клиентам непереназначенный порт.
                if (discovery.transport != null && discovery.transport != manager.transport)
                    return Refuse("DiscoveryTransportMismatch");
                if (NetworkServer.active || NetworkClient.active)
                    return Refuse("NetworkAlreadyActive");

                if (role != GameNetworkDiscovery.AppRole.Client)
                {
                    // Это preflight, а не резервирование: транспорт и Discovery всё равно сообщат реальную ошибку bind.
                    string occupied = RequireFree(_networkPort) ?? RequireFree(_discoveryPort);
                    if (occupied != null)
                        return Refuse(occupied);
                }

                transport.Port = (ushort)_networkPort;
                discovery.ServerBroadcastListenPort = _discoveryPort;
                GameLog.Network.Info($"[E2E] Порты применены для {role}: KCP {_networkPort}, Discovery {_discoveryPort}");
                return null;
            }
            catch (Exception error)
            {
                return Refuse(error.GetType().Name + ": " + error.Message);
            }
        }

        private static string Refuse(string code)
        {
            LastRefusal = code;
            GameLog.Network.Error($"[E2E] Подготовка портов отклонена: {code}");
            return code;
        }

        private static string RequireFree(int port)
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.ExclusiveAddressUse = true;
                    socket.Bind(new IPEndPoint(IPAddress.Any, port));
                }
                return null;
            }
            catch (SocketException)
            {
                return "PortOccupied: UDP порт занят: " + port;
            }
        }
    }
}
#endif
