using System;
using System.Collections.Generic;
using Mirror;
using Mirror.Discovery;
using UnityEditor;
using UnityEditor.MPE;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools;
using UltimateXR.Core;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;
using VrBattlegrounds.Managers;
#if !VRBG_NO_E2E
using VrBattlegrounds.DevTools.E2E;
#endif

namespace VrBattlegrounds.EditorTools.TestStand
{
    /// <summary>Локальный участник; IPC не вызывает действий сетевой копии или запуск сети.</summary>
    [InitializeOnLoad]
    internal static class PlayModeStandParticipant
    {
        private static readonly string Session = Guid.NewGuid().ToString("N");
        private static readonly Dictionary<string, GameObject> Markers = new Dictionary<string, GameObject>();
        private static StandManifest _run;
        private static StandRequestGate _gate;
        private static IDisposable _profile;
        private static IDisposable _identity;
        private static IDisposable _focus;
        private static IDisposable _launch;
        private static StartupRouteHandle _startupRoute;
        private static Func<string> _beforeServerStart;
        private static Func<string> _clientAddress;
        private static Func<GameNetworkDiscovery.AppRole?> _roleOverride;
        private static string _startupError;
        private static Action _unsubscribe;
        private static string _originalProfile;
        private static string _originalToken;
        private static bool _originalTokenPresent;
        private static bool _restored;
        private static double _nextHello;
        private static int _connectionEpoch;
        private static bool _connected;
#if !VRBG_NO_E2E
        private static E2ERunner _e2e;
#endif
        private const string BaselineKey = "VrBattlegrounds.TestStand.Baseline";

        [Serializable]
        private sealed class Baseline
        {
            public string RunId;
            public string Profile;
            public string Token;
            public bool TokenPresent;
        }

        static PlayModeStandParticipant()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += HandlePlay;
            AssemblyReloadEvents.beforeAssemblyReload += Leave;
            EditorApplication.quitting += Leave;
            SceneManager.sceneLoaded += HandleScene;
            Sync();
        }

        internal static void Sync()
        {
            var next = StandManifest.Read();
            string participant = StandManifest.ParticipantName();
            if (next == null || !next.Admits(participant)) { if (_run != null) Leave(); return; }
            if (_run != null && _run.RunId != next.RunId) Leave();
            _run = next;
            if (_gate != null) { if (next.Phase != "running") Release(); return; }
            string saved = SessionState.GetString(BaselineKey, "");
            var baseline = string.IsNullOrEmpty(saved) ? null : JsonUtility.FromJson<Baseline>(saved);
            if (baseline == null || baseline.RunId != next.RunId)
            {
                if (next.Phase != "running") return;
                baseline = new Baseline { RunId = next.RunId, Profile = LocalClientProfile.OverrideStateKey,
                    TokenPresent = PlayerPrefs.HasKey("DeviceToken"), Token = PlayerPrefs.GetString("DeviceToken", "") };
                SessionState.SetString(BaselineKey, JsonUtility.ToJson(baseline));
            }
            _originalProfile = baseline.Profile;
            _originalTokenPresent = baseline.TokenPresent;
            _originalToken = baseline.Token;
            if (next.Phase != "running")
            {
                if (_restored) return;
                // Reload уничтожил scope; восстановление выполняет тот же владелец профиля.
                using (LocalClientProfile.BeginTemporaryOverride(ClientDeviceType.VR, false, GameRole.Player, _originalProfile)) { }
                Release();
                return;
            }
            var configuration = next.Configuration == null ? new PlayLaunchConfiguration { Enabled = false, HostIsAdmin = false, PauseOnFocusLoss = false }
                : PlayLaunch.ResolveParticipantConfiguration(next, participant);
            _profile = LocalClientProfile.BeginTemporaryOverride((ClientDeviceType)Enum.Parse(typeof(ClientDeviceType), configuration.DeviceType),
                configuration.Role == "host" && configuration.HostIsAdmin, (GameRole)Enum.Parse(typeof(GameRole), configuration.GameRole), _originalProfile);
            try { _identity = ClientDeviceIdentity.BeginTemporaryToken("stand-" + next.RunId + "-" + participant); }
            catch { _profile.Dispose(); _profile = null; throw; }
            try
            {
                _focus = UxrManager.BeginEditorFocusPauseOverride(configuration.PauseOnFocusLoss);
                if (!next.MarkerProbe) _launch = DebugBootstrapGate.BeginManagedLaunch();
            }
            catch { Release(); throw; }
            _restored = false;
            _startupError = null;
            _gate = new StandRequestGate(next.RunId, participant, Session, 256, StandManifest.CurrentPid);
            EventService.Start();
            _unsubscribe = EventService.RegisterEventHandler(StandManifest.CommandEvent(next.RunId, Session),
                (Func<string, object[], object>)HandleCommand);
        }

        private static void Tick()
        {
            try
            {
                Sync();
                if (_run == null || EditorApplication.timeSinceStartup < _nextHello) return;
                _nextHello = EditorApplication.timeSinceStartup + 0.5;
                EventService.Emit(StandManifest.HelloEvent, JsonUtility.ToJson(Snapshot()), -1, EventDataSerialization.JsonUtility);
            }
            catch (Exception e) { GameLog.Debug.Error("[TestStand] Участник: " + e.Message); }
        }

        private static object HandleCommand(string name, object[] args)
        {
            try
            {
                Sync();
                var request = args != null && args.Length > 0 ? JsonUtility.FromJson<StandRequest>(args[0] as string) : null;
                StandReply reply = _gate == null ? StandReply.Error("inactive") : _gate.Execute(request, Execute);
                return JsonUtility.ToJson(reply);
            }
            catch (Exception e) { return JsonUtility.ToJson(StandReply.Error("invalid-request", e.Message)); }
        }

        private static StandReply Execute(StandRequest request)
        {
            UpdateConnectionEpoch();
            if (request.Action == "run-e2e")
            {
#if VRBG_NO_E2E
                return StandReply.Error("E2EDisabled");
#else
                if (request.Scenario != "session-recovery-on-reconnect") return StandReply.Error("e2e-scenario-unsupported");
                if (!EditorApplication.isPlaying || !DebugBootstrapGate.ManagedLaunchActive) return StandReply.Error("managed-launch-required");
                if (_run.Configuration.AutoGoLive || _run.Configuration.BotCount != 0) return StandReply.Error("e2e-launch-conflict");
                if (!MapRunAdmission.IsLocalPlayable || !(MapBootstrap.ForScene(SceneManager.GetActiveScene())?.LocalRunKey.IsValid ?? false) ||
                    SceneManager.GetActiveScene().name != System.IO.Path.GetFileNameWithoutExtension(_run.TargetScenePath))
                    return StandReply.Error("e2e-not-ready");
                if (!_run.Configuration.HasOwnedServer || _run.Configuration.ClientCount < 1 ||
                    EnumerableHasHost(_run.Configuration)) return StandReply.Error("e2e-topology-unsupported");
                var local = PlayLaunch.ResolveParticipantConfiguration(_run, StandManifest.ParticipantName());
                if (local.Role == "server" ? !NetworkServer.active || NetworkClient.active :
                    local.Role != "client" || NetworkServer.active || !NetworkClient.active || !NetworkClient.isConnected)
                    return StandReply.Error("e2e-role-not-ready");
                string result = System.IO.Path.Combine(StandManifest.ProjectRoot, "Temp", "VRBattlegroundsTestStand", _run.RunId, "e2e-" + StandManifest.ParticipantName() + ".json");
                var context = E2EContext.Create(request.Scenario, local.Role == "server" || local.Role == "host" ? "server" : "client",
                    result, request.ScenarioTimeoutSeconds, System.IO.Path.GetFileNameWithoutExtension(_run.TargetScenePath),
                    _run.Configuration.ClientCount, local.ClientAddress, ClientDeviceIdentity.BaseToken, true);
                _e2e = E2ERunner.Begin(context, false);
                return StandReply.Ok("e2e-started");
#endif
            }
            if (request.Action == "disconnect" || request.Action == "reconnect")
            {
                if (!EditorApplication.isPlaying) return StandReply.Error("not-playing");
                if (request.ExpectedConnectionEpoch != _connectionEpoch) return StandReply.Error("wrong-connection-epoch");
                uint avatar = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<PlayerSession>()?.ActiveAvatarNetId ?? 0 : 0;
                if (request.ExpectedAvatarNetId != 0 && request.ExpectedAvatarNetId != avatar) return StandReply.Error("wrong-avatar");
                var discovery = NetworkManager.singleton != null ? NetworkManager.singleton.GetComponent<GameNetworkDiscovery>() : null;
                if (discovery == null) return StandReply.Error("client-manager-unavailable");
                if (NetworkServer.active) return StandReply.Error("host-disconnect-unsupported");
                if (request.Action == "disconnect") discovery.RequestClientDisconnect();
                else discovery.RequestClientConnect(_run.Configuration?.ClientAddress);
                return StandReply.Ok(request.Action + "-requested");
            }
            if (request.Action == "state")
            {
                var reply = StandReply.Ok("state");
                reply.Data = JsonUtility.ToJson(Snapshot());
                return reply;
            }
            if (!EditorApplication.isPlaying) return StandReply.Error("not-playing");
            bool exists = Markers.TryGetValue(request.MarkerName, out GameObject marker) && marker != null;
            if (request.Action == "create-marker" && !exists)
            {
                marker = new GameObject("__VRTestStand_" + _run.RunId + "_" + request.MarkerName);
                marker.hideFlags = HideFlags.DontSave;
                Markers[request.MarkerName] = marker;
                exists = true;
            }
            else if (request.Action == "remove-marker")
            {
                if (marker != null) UnityEngine.Object.DestroyImmediate(marker);
                Markers.Remove(request.MarkerName);
                exists = false;
            }
            return StandReply.Ok(request.Action, request.MarkerName, MarkerCount(), exists);
        }

        private static int MarkerCount()
        {
            int count = 0;
            foreach (var marker in Markers.Values) if (marker != null) count++;
            return count;
        }

        private static bool EnumerableHasHost(PlayLaunchConfiguration configuration)
        {
            for (int i = 0; i <= configuration.ClientCount; i++)
                if (configuration.RoleForParticipant(i) == "host") return true;
            return false;
        }

        internal static StandParticipantState Snapshot()
        {
            UpdateConnectionEpoch();
            var session = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<PlayerSession>() : null;
            var discovery = NetworkManager.singleton != null ? NetworkManager.singleton.GetComponent<GameNetworkDiscovery>() : null;
            return new StandParticipantState
            {
                RunId = _run?.RunId,
                ParticipantId = StandManifest.ParticipantName(),
                ProcessSessionId = Session,
                ProcessId = StandManifest.CurrentPid,
                Role = string.Join(",", Unity.Multiplayer.PlayMode.CurrentPlayer.Tags),
                NetworkRole = discovery?.CurrentRole?.ToString(),
                RequestedRole = _run?.Configuration != null ? PlayLaunch.ResolveParticipantConfiguration(_run, StandManifest.ParticipantName()).Role : null,
                Playing = EditorApplication.isPlaying,
                Server = NetworkServer.active,
                Client = NetworkClient.active,
                Connected = NetworkClient.isConnected,
                Active = _gate != null,
                MarkerCount = MarkerCount(),
                ProfileTemporary = LocalClientProfile.HasTemporaryOverride,
                DeviceType = LocalClientProfile.LocalDeviceType.ToString(),
                GameRole = LocalClientProfile.LocalRole.ToString(),
                Admin = LocalClientProfile.IsLocalAdmin,
                ProfileRestored = _restored,
                PersistentTokenUnchanged = PlayerPrefs.HasKey("DeviceToken") == _originalTokenPresent && PlayerPrefs.GetString("DeviceToken", "") == _originalToken,
                Scene = SceneManager.GetActiveScene().name,
                MapPlayable = MapRunAdmission.IsLocalPlayable,
                MapRunKey = MapBootstrap.ForScene(SceneManager.GetActiveScene())?.LocalRunKey.ToString(),
                MapRunValid = MapBootstrap.ForScene(SceneManager.GetActiveScene())?.LocalRunKey.IsValid ?? false,
                StartupError = _startupError ?? PlayLaunchNetworkPorts.StartupError,
                NetworkPort = NetworkManager.singleton != null && NetworkManager.singleton.transport is PortTransport port ? port.Port : 0,
                DiscoveryPort = PlayLaunchNetworkPorts.ActualDiscoveryPort,
                ConnectionEpoch = _connectionEpoch, AvatarNetId = session?.ActiveAvatarNetId ?? 0, SessionNetId = session?.netId ?? 0,
                ServerSessions = PlayersManager.Instance?.Sessions.Count ?? 0, ClientManagerReady = GameNetworkDiscovery.ClientManagerReady,
                DeviceToken = ClientDeviceIdentity.HasTemporaryToken ? ClientDeviceIdentity.BaseToken : PlayerPrefs.GetString("DeviceToken", ""),
#if !VRBG_NO_E2E
                E2EStatus = _e2e != null ? _e2e.Result?.Status : null, E2EFinished = _e2e != null && _e2e.Finished,
                E2EPassed = _e2e != null && _e2e.Result != null && _e2e.Result.Passed
#endif
            };
        }

        private static void HandlePlay(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode) Release();
        }

        private static void HandleScene(Scene scene, LoadSceneMode mode)
        {
            if (_run == null || _gate == null || NetworkServer.active || NetworkClient.active) return;
            if (!_run.MarkerProbe && _launch != null)
            {
                var configuration = PlayLaunch.ResolveParticipantConfiguration(_run, StandManifest.ParticipantName());
                var role = (GameNetworkDiscovery.AppRole)Enum.Parse(typeof(GameNetworkDiscovery.AppRole), configuration.Role, true);
                _roleOverride = () => role;
                DebugBootstrapGate.EditorRoleOverride = _roleOverride;
            }
            _clientAddress = () => _run?.Configuration?.ClientAddress;
            DebugBootstrapGate.EditorClientAddressOverride = _clientAddress;
            _beforeServerStart = PrepareStartupRoute;
            DebugBootstrapGate.EditorBeforeServerStart = _beforeServerStart;
        }

        private static string PrepareStartupRoute()
        {
            if (_run == null || _gate == null || _run.MarkerProbe || _run.SceneKind != "Game" || _startupRoute != null)
                return _startupError;
            var config = PlayLaunch.ResolveParticipantConfiguration(_run, StandManifest.ParticipantName());
            if (config.Role != "server" && config.Role != "host") return null;
            string target = System.IO.Path.GetFileNameWithoutExtension(_run.TargetScenePath);
            if (target == "Lobby") return null;
            if (!ServerStartupRoute.TryRequest(target, config.ModeId, _run.RunId + ":" + StandManifest.ParticipantName(),
                out _startupRoute, out _startupError)) return _startupError;
            return null;
        }

        private static void Release()
        {
            if (DebugBootstrapGate.EditorClientAddressOverride == _clientAddress)
                DebugBootstrapGate.EditorClientAddressOverride = null;
            _clientAddress = null;
            if (DebugBootstrapGate.EditorRoleOverride == _roleOverride)
                DebugBootstrapGate.EditorRoleOverride = null;
            _roleOverride = null;
            _startupRoute?.Dispose(); _startupRoute = null;
            if (DebugBootstrapGate.EditorBeforeServerStart == _beforeServerStart)
                DebugBootstrapGate.EditorBeforeServerStart = null;
            _beforeServerStart = null;
#if !VRBG_NO_E2E
            if (_e2e != null) UnityEngine.Object.DestroyImmediate(_e2e.gameObject);
            _e2e = null;
#endif
            _gate?.Revoke(); _gate = null;
            _unsubscribe?.Invoke(); _unsubscribe = null;
            foreach (var marker in Markers.Values) if (marker != null) UnityEngine.Object.DestroyImmediate(marker);
            Markers.Clear();
            _identity?.Dispose(); _identity = null;
            _focus?.Dispose(); _focus = null;
            _launch?.Dispose(); _launch = null;
            _profile?.Dispose(); _profile = null;
            if (_run != null) _restored = LocalClientProfile.OverrideStateKey == _originalProfile;
        }

        private static void Leave() { Release(); _run = null; _connectionEpoch = 0; _connected = false; }
        private static void UpdateConnectionEpoch()
        {
            bool connected = NetworkClient.isConnected;
            if (connected && !_connected) _connectionEpoch++;
            _connected = connected;
        }
    }
}
