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
        private static Action _unsubscribe;
        private static string _originalProfile;
        private static string _originalToken;
        private static bool _originalTokenPresent;
        private static bool _restored;
        private static double _nextHello;
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
            _profile = LocalClientProfile.BeginTemporaryOverride(ClientDeviceType.VR, false, GameRole.Player, _originalProfile);
            try { _identity = ClientDeviceIdentity.BeginTemporaryToken("stand-" + next.RunId + "-" + participant); }
            catch { _profile.Dispose(); _profile = null; throw; }
            _restored = false;
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

        internal static StandParticipantState Snapshot()
        {
            return new StandParticipantState
            {
                RunId = _run?.RunId,
                ParticipantId = StandManifest.ParticipantName(),
                ProcessSessionId = Session,
                ProcessId = StandManifest.CurrentPid,
                Role = string.Join(",", Unity.Multiplayer.PlayMode.CurrentPlayer.Tags),
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
                Scene = SceneManager.GetActiveScene().name
            };
        }

        private static void HandlePlay(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode) Release();
        }

        private static void HandleScene(Scene scene, LoadSceneMode mode)
        {
            if (_run == null || _gate == null || NetworkServer.active || NetworkClient.active) return;
            var manager = UnityEngine.Object.FindAnyObjectByType<NetworkManager>();
            if (manager != null && manager.transport is PortTransport port) port.Port = (ushort)_run.NetworkPort;
            var discovery = UnityEngine.Object.FindAnyObjectByType<NetworkDiscovery>();
            if (discovery != null)
            {
                var serialized = new SerializedObject(discovery);
                serialized.FindProperty("serverBroadcastListenPort").intValue = _run.DiscoveryPort;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void Release()
        {
            _gate?.Revoke(); _gate = null;
            _unsubscribe?.Invoke(); _unsubscribe = null;
            foreach (var marker in Markers.Values) if (marker != null) UnityEngine.Object.DestroyImmediate(marker);
            Markers.Clear();
            _identity?.Dispose(); _identity = null;
            _profile?.Dispose(); _profile = null;
            if (_run != null) _restored = LocalClientProfile.OverrideStateKey == _originalProfile;
        }

        private static void Leave() { Release(); _run = null; }
    }
}
