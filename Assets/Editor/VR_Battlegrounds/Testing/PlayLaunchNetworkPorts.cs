using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Mirror;
using Mirror.Discovery;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.EditorTools.TestStand
{
    /// <summary>Единственный Editor-writer портов. Пара выбирается координатором, участники только применяют её.</summary>
    [InitializeOnLoad]
    internal static class PlayLaunchNetworkPorts
    {
        private const string NativeKey = "VrBattlegrounds.PlayLaunch.NativePorts";
        private const string BaselineKey = "VrBattlegrounds.PlayLaunch.PortBaseline";
        private const string ErrorKey = "VrBattlegrounds.PlayLaunch.PortError";
        private const string ErrorRunKey = "VrBattlegrounds.PlayLaunch.PortErrorRun";
        internal static string StartupError
        {
            get
            {
                var run = StandManifest.Read();
                // Предыдущий отказ не должен отменять новый run до его ExitingEditMode.
                return run != null && SessionState.GetString(ErrorRunKey, "") == run.RunId
                    ? SessionState.GetString(ErrorKey, "") : "";
            }
        }
        internal static int ActualDiscoveryPort
        {
            get
            {
                var discovery = UnityEngine.Object.FindAnyObjectByType<NetworkDiscovery>();
                return discovery == null ? 0 : new SerializedObject(discovery).FindProperty("serverBroadcastListenPort")?.intValue ?? 0;
            }
        }
        private static string NativePath => Path.Combine(StandManifest.ProjectRoot, "Temp", "VRBattlegroundsTestStand", "native-ports.json");

        [Serializable] private sealed class FrozenPorts
        {
            public string RunId;
            public int OwnerPid;
            public long OwnerStart;
            public int NetworkPort;
            public int DiscoveryPort;
        }
        [Serializable] private sealed class ComponentBaseline
        {
            public string TransportId;
            public int NetworkPort;
            public string DiscoveryId;
            public int DiscoveryPort;
        }
        [Serializable] private sealed class Baselines { public List<ComponentBaseline> Values = new List<ComponentBaseline>(); }

        static PlayLaunchNetworkPorts()
        {
            DebugBootstrapGate.EditorBeforeNetworkStart = Prepare;
            EditorApplication.playModeStateChanged += HandlePlay;
        }

        internal static void Resolve(PlayLaunchConfiguration configuration, bool managed, out int network, out int discovery)
        {
            configuration.Validate();
            network = discovery = 0;
            if (configuration.NetworkPortPolicy == "fixed")
            {
                network = configuration.NetworkPort; discovery = configuration.DiscoveryPort;
                return;
            }
            // Принятый managed stand сохраняет прежний автоматический подбор для своего сервера.
            if (configuration.NetworkPortPolicy != "auto" && !(managed && configuration.HasOwnedServer)) return;
            if (managed && !configuration.HasOwnedServer)
                throw new PlayLaunchException("AutoPortsRequireServer", "Auto предназначен для локального запуска со своим сервером; внешнему клиенту нужны default/fixed.");
            network = FreeUdpPort();
            do { discovery = FreeUdpPort(); } while (discovery == network);
        }

        private static int FreeUdpPort()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                return ((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }

        private static void HandlePlay(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) { SessionState.EraseString(ErrorKey); SessionState.EraseString(ErrorRunKey); }
            if (state == PlayModeStateChange.ExitingEditMode && Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor && StandManifest.Read() == null)
            {
                try
                {
                    Resolve(PlayLaunchSettings.Effective, false, out int network, out int discovery);
                    using (var owner = Process.GetCurrentProcess())
                    {
                        var frozen = new FrozenPorts { RunId = Guid.NewGuid().ToString("N"), OwnerPid = owner.Id,
                            OwnerStart = owner.StartTime.ToUniversalTime().Ticks, NetworkPort = network, DiscoveryPort = discovery };
                        string json = JsonUtility.ToJson(frozen);
                        Directory.CreateDirectory(Path.GetDirectoryName(NativePath));
                        string temporary = NativePath + ".new";
                        File.WriteAllText(temporary, json, new System.Text.UTF8Encoding(false));
                        if (File.Exists(NativePath)) File.Replace(temporary, NativePath, null); else File.Move(temporary, NativePath);
                        SessionState.SetString(NativeKey, json);
                    }
                }
                catch (Exception error)
                {
                    GameLog.Debug.Error("[PlayLaunch] Порты не подготовлены: " + error.Message);
                    EditorApplication.isPlaying = false;
                }
            }
            if (state != PlayModeStateChange.EnteredEditMode) return;
            Restore();
            string own = SessionState.GetString(NativeKey, "");
            if (Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor && !string.IsNullOrEmpty(own) && File.Exists(NativePath))
            {
                var saved = JsonUtility.FromJson<FrozenPorts>(File.ReadAllText(NativePath));
                var baseline = JsonUtility.FromJson<FrozenPorts>(own);
                if (saved.RunId == baseline.RunId && saved.OwnerPid == StandManifest.CurrentPid) File.Delete(NativePath);
            }
            SessionState.EraseString(NativeKey);
        }

        private static void ReadPorts(out int network, out int discovery)
        {
            var run = StandManifest.Read();
            if (run != null)
            {
                if (run.Phase == "stopping" && run.Admits(StandManifest.ParticipantName()))
                    throw new PlayLaunchException("LaunchStopping", "Владелец завершает запуск; поздний старт сети отменён.");
                if (run.Phase != "running" || !run.Admits(StandManifest.ParticipantName()))
                    throw new PlayLaunchException("PortParticipantInactive", "Процесс не входит в активный запуск.");
                network = run.NetworkPort; discovery = run.DiscoveryPort; return;
            }
            if (File.Exists(NativePath))
            {
                var frozen = JsonUtility.FromJson<FrozenPorts>(File.ReadAllText(NativePath));
                bool alive = false;
                try
                {
                    using (var owner = Process.GetProcessById(frozen.OwnerPid))
                        alive = !owner.HasExited && owner.StartTime.ToUniversalTime().Ticks == frozen.OwnerStart;
                }
                catch (ArgumentException) { } // Процесс завершён: не принимаем его пару и не удаляем чужой descriptor.
                if (alive) { network = frozen.NetworkPort; discovery = frozen.DiscoveryPort; return; }
            }
            // Fixed/default могут использоваться в штатном MPP сценарии без Play главного редактора.
            string json = SessionState.GetString(NativeKey, "");
            if (string.IsNullOrEmpty(json))
            {
                var config = PlayLaunchSettings.Effective;
                if (config.NetworkPortPolicy == "auto")
                    throw new PlayLaunchException("NativeAutoCoordinatorMissing", "Для Auto включите Play главного редактора или запускайте через Play Launch.");
                Resolve(config, false, out network, out discovery);
                SessionState.SetString(NativeKey, JsonUtility.ToJson(new FrozenPorts { NetworkPort = network, DiscoveryPort = discovery }));
                return;
            }
            var local = JsonUtility.FromJson<FrozenPorts>(json);
            network = local.NetworkPort; discovery = local.DiscoveryPort;
        }

        private static string Prepare(NetworkManager manager, NetworkDiscovery discovery, GameNetworkDiscovery.AppRole role)
        {
            try
            {
                ReadPorts(out int networkPort, out int discoveryPort);
                if (networkPort == 0 && discoveryPort == 0) return null;
                if (networkPort < 1 || networkPort > 65535 || discoveryPort < 1 || discoveryPort > 65535 || networkPort == discoveryPort)
                    return Fail("PortInvalid");
                if (manager == null || !(manager.transport is PortTransport transport) || discovery == null)
                    return Fail("PortTransportUnsupported");
                if (NetworkServer.active || NetworkClient.active) return Fail("NetworkAlreadyActive");
                var serialized = new SerializedObject(discovery);
                var listenPort = serialized.FindProperty("serverBroadcastListenPort");
                if (listenPort == null) return Fail("DiscoveryPortUnsupported");
                if (role != GameNetworkDiscovery.AppRole.Client)
                {
                    // Это preflight, а не резервирование: транспорт обязан сообщить реальную ошибку bind.
                    RequireFree(networkPort); RequireFree(discoveryPort);
                }
                var baselines = ReadBaselines();
                string transportId = manager.transport.GetEntityId().ToString(), discoveryId = discovery.GetEntityId().ToString();
                if (!baselines.Values.Exists(value => value.TransportId == transportId && value.DiscoveryId == discoveryId))
                {
                    baselines.Values.Add(new ComponentBaseline { TransportId = transportId, NetworkPort = transport.Port,
                        DiscoveryId = discoveryId, DiscoveryPort = listenPort.intValue });
                    SessionState.SetString(BaselineKey, JsonUtility.ToJson(baselines));
                }
                transport.Port = (ushort)networkPort;
                listenPort.intValue = discoveryPort;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return null;
            }
            catch (Exception error)
            {
                if (error is PlayLaunchException launch)
                    return launch.Code == "LaunchStopping" ? launch.Code : Fail(launch.Code + ": " + launch.Message);
                return Fail(error.GetType().Name + ": " + error.Message);
            }
        }

        private static string Fail(string error)
        {
            SessionState.SetString(ErrorRunKey, StandManifest.Read()?.RunId ?? "");
            SessionState.SetString(ErrorKey, error);
            PlayLaunch.ReportNativeNetworkFailure(error);
            return error;
        }

        private static void RequireFree(int port)
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.ExclusiveAddressUse = true;
                    socket.Bind(new IPEndPoint(IPAddress.Any, port));
                }
            }
            catch (SocketException) { throw new PlayLaunchException("PortOccupied", "UDP порт занят: " + port); }
        }

        private static Baselines ReadBaselines()
        {
            string json = SessionState.GetString(BaselineKey, "");
            return string.IsNullOrEmpty(json) ? new Baselines() : JsonUtility.FromJson<Baselines>(json);
        }

        private static void Restore()
        {
            foreach (var baseline in ReadBaselines().Values)
            {
                foreach (var candidate in Resources.FindObjectsOfTypeAll<Transport>())
                    if (candidate.GetEntityId().ToString() == baseline.TransportId && candidate is PortTransport transport)
                        transport.Port = (ushort)baseline.NetworkPort;
                foreach (var discovery in Resources.FindObjectsOfTypeAll<NetworkDiscovery>())
                {
                    if (discovery.GetEntityId().ToString() != baseline.DiscoveryId) continue;
                    var serialized = new SerializedObject(discovery);
                    var port = serialized.FindProperty("serverBroadcastListenPort");
                    if (port != null) { port.intValue = baseline.DiscoveryPort; serialized.ApplyModifiedPropertiesWithoutUndo(); }
                }
            }
            SessionState.EraseString(BaselineKey);
        }
    }
}
