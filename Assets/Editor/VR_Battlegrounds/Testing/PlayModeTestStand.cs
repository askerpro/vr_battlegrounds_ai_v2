using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using UnityEditor;
using UnityEditor.MPE;
using UnityEngine;
using VrBattlegrounds.Core;
using UltimateXR.Core;
using VrBattlegrounds.DevTools;
using UnityEditor.SceneManagement;

namespace VrBattlegrounds.EditorTools.TestStand
{
    /// <summary>Фасад для execute_code. Координатор не выполняет команды вместо выбранного клиента.</summary>
    [InitializeOnLoad]
    public static class PlayModeTestStand
    {
        private static readonly Dictionary<string, StandParticipantState> Peers = new Dictionary<string, StandParticipantState>();
        private static readonly Dictionary<string, double> Seen = new Dictionary<string, double>();
        private static readonly Dictionary<string, StandOperation> Operations = new Dictionary<string, StandOperation>();
        private static readonly Dictionary<string, string> Fingerprints = new Dictionary<string, string>();
        private static StandManifest _run;
        private static ScriptableObject _temporaryScenario;
        private static double _stopRequestedAt;
        private static bool _nativeStopRequested;
        private static bool _cleanupPassed;
        private static string _lastRunId;

        static PlayModeTestStand()
        {
            EventService.RegisterEventHandler(StandManifest.HelloEvent, (Action<string, object[]>)ReceiveHello);
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += HandlePlay;
            _run = StandManifest.Read();
        }

        public static string StartProbe(bool debugBootstrapEnabled = true)
        {
            return PlayLaunch.StartMarkerProbe(debugBootstrapEnabled);
        }

        internal static string StartConfigured(PlayLaunchConfiguration configuration, string owner, string runId,
            string startScene, string targetScene, string kind, bool markerProbe, bool expectTargetScene)
        {
            RequireMain();
            if (EditorApplication.isPlayingOrWillChangePlaymode || NativeState() != "Idle")
                throw new InvalidOperationException("Сначала завершите текущий Play Mode сценарий.");
            var existing = StandManifest.Read();
            if (existing != null) throw new InvalidOperationException("Уже существует активный запуск стенда.");
            UnityEngine.Object previous = ActiveScenario();
            _run = new StandManifest
            {
                RunId = runId, OwnerPid = StandManifest.CurrentPid, Phase = "running",
                Participants = Enumerable.Range(0, configuration.ClientCount + 1).Select(i => i == 0 ? "main" : "Player " + (i + 1)).ToArray(),
                PreviousScenarioName = previous.name, PreviousScenarioPath = AssetDatabase.GetAssetPath(previous),
                NetworkPort = configuration.HasOwnedServer ? FreeUdpPort() : 0, DiscoveryPort = configuration.HasOwnedServer ? FreeUdpPort() : 0,
                Settings = Array.Empty<StandSetting>(), Owner = owner, Configuration = configuration.Copy(),
                StartScenePath = startScene, TargetScenePath = targetScene, SceneKind = kind, MarkerProbe = markerProbe,
                ExpectTargetScene = expectTargetScene,
                PreviousStartScenePath = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)
            };
            while (_run.NetworkPort > 0 && _run.DiscoveryPort == _run.NetworkPort) _run.DiscoveryPort = FreeUdpPort();
            Peers.Clear(); Seen.Clear(); Operations.Clear(); Fingerprints.Clear();
            _cleanupPassed = false;
            _nativeStopRequested = false;
            try
            {
                _run.Write();
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(startScene);
                PlayModeStandParticipant.Sync();
                if (!ChannelService.IsRunning()) ChannelService.Start();
                EventService.Start();
                _temporaryScenario = BuildScenario();
                SetScenario(_temporaryScenario);
                NativeMethod("Start").Invoke(null, null);
                return Status();
            }
            catch
            {
                BeginStop();
                _nativeStopRequested = true;
                if (EditorApplication.isPlayingOrWillChangePlaymode || NativeState() != "Idle") NativeMethod("Stop").Invoke(null, null);
                else Restore();
                throw;
            }
        }

        public static string Status()
        {
            // Продвижение запуска и cleanup принадлежит EditorApplication.update.
            // Чтение статуса не должно инициировать Stop/restore, в том числе
            // при восстановлении MCP-соединения после domain reload.
            var run = StandManifest.Read();
            foreach (var peer in Peers.Values)
                peer.LastSeenAgeSeconds = Seen.TryGetValue(peer.ParticipantId, out double seen)
                    ? Math.Max(0, EditorApplication.timeSinceStartup - seen) : double.MaxValue;
            return JsonUtility.ToJson(new StandStatus
            {
                RunId = run?.RunId, Phase = run?.Phase ?? "idle", Playing = EditorApplication.isPlaying,
                CleanupPassed = _cleanupPassed, LastRunId = _lastRunId,
                Participants = Peers.Values.Where(p => p.RunId == (run?.RunId ?? _lastRunId)).OrderBy(p => p.ParticipantId).ToArray()
            });
        }

        internal static void RequireSingleEditorNativePlay(string expectedRole)
        {
            RequireMain();
            var active = ActiveScenario();
            if (active == null) throw new PlayLaunchException("NativeScenarioUnknown", "Нельзя определить native topology; используйте Play Launch.");
            var serialized = new SerializedObject(active);
            var editors = serialized.FindProperty("m_EnableEditors");
            var additionalEditors = serialized.FindProperty("m_EditorInstances");
            var localPlayers = serialized.FindProperty("m_LocalInstances");
            var remotePlayers = serialized.FindProperty("m_RemoteInstances");
            if (editors == null || additionalEditors == null)
                throw new PlayLaunchException("NativeScenarioUnknown", "Нельзя определить дополнительные процессы native scenario.");
            if (!editors.boolValue)
                throw new PlayLaunchException("NativeEditorsDisabled", "В explicit native scenario выключена Editor-группа; используйте Play Launch или одиночный Host/Client scenario.");
            if ((editors.boolValue && additionalEditors.arraySize > 0) ||
                (localPlayers != null && localPlayers.arraySize > 0) ||
                (remotePlayers != null && remotePlayers.arraySize > 0))
                throw new PlayLaunchException("NativeTopologyUnsupported", "Явный toolbar launch поддерживает один редактор; несколько процессов запускайте через Play Launch.");
            var main = serialized.FindProperty("m_MainEditorInstance");
            var tag = main?.FindPropertyRelative("m_PlayerTag");
            if (tag == null) throw new PlayLaunchException("NativeScenarioUnknown", "Не определена роль main editor.");
            string role = tag.stringValue;
            if ((string.Equals(role, "server", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(role, "host", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(role, "client", StringComparison.OrdinalIgnoreCase)) &&
                !string.Equals(role, expectedRole, StringComparison.OrdinalIgnoreCase))
                throw new PlayLaunchException("NativeRoleMismatch", "Роль native scenario отличается от frozen профиля; используйте Play Launch.");
        }

        public static string Configuration()
        {
            var active = ActiveScenario();
            return JsonUtility.ToJson(new StandConfiguration {
                ScenarioName = active?.name, ScenarioPath = active == null ? "" : AssetDatabase.GetAssetPath(active),
                Settings = new[] { Capture("VrBattlegrounds.DebugBootstrap.Enabled", true),
                    Capture("VrBattlegrounds.DebugBootstrap.HostIsAdmin", true),
                    Capture("VrBattlegrounds.PauseXrWhenEditorUnfocused", true) }
            });
        }

        public static string Send(StandRequest request)
        {
            RequireMain();
            _run = StandManifest.Read();
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrEmpty(request.RequestId)) request.RequestId = Guid.NewGuid().ToString("N");
            string fingerprint = JsonUtility.ToJson(request);
            if (Operations.TryGetValue(request.RequestId, out var prior))
            {
                if (Fingerprints[request.RequestId] == fingerprint) return JsonUtility.ToJson(prior);
                return JsonUtility.ToJson(Completed(request, "request-conflict"));
            }
            if (Operations.Count >= 1024) return JsonUtility.ToJson(Completed(request, "capacity"));
            var operation = new StandOperation { RequestId = request.RequestId };
            if (_run == null || _run.Phase != "running") operation = Completed(request, "inactive");
            else if (_run.OwnerPid != StandManifest.CurrentPid) operation = Completed(request, "wrong-owner");
            else if (request.RunId != _run.RunId) operation = Completed(request, "wrong-run");
            else if (request.ParticipantId == null || !Peers.TryGetValue(request.ParticipantId, out var target) ||
                     !Seen.TryGetValue(request.ParticipantId, out double seen) || EditorApplication.timeSinceStartup - seen > 5)
                operation = Completed(request, "target-unavailable");
            else if (request.ProcessSessionId != target.ProcessSessionId) operation = Completed(request, "wrong-session", target);
            else if (request.TimeBudgetMs < 1 || request.TimeBudgetMs > 30000) operation = Completed(request, "invalid-budget", target);
            else
            {
                string name = StandManifest.CommandEvent(_run.RunId, target.ProcessSessionId);
                if (EventService.IsRequestPending(name)) operation = Completed(request, "target-busy", target);
                else
                {
                    Operations[request.RequestId] = operation;
                    Fingerprints[request.RequestId] = fingerprint;
                    try { EventService.Request(name, (error, data) =>
                    {
                        operation.Completed = true;
                        if (error != null)
                        {
                            operation.EffectUnknown = true;
                            operation.Reply = StandReply.Error("transport-error", error.Message);
                            return;
                        }
                        try
                        {
                            var reply = JsonUtility.FromJson<StandReply>(data[0] as string);
                            if (reply == null || reply.RunId != request.RunId || reply.ParticipantId != request.ParticipantId ||
                                reply.ProcessSessionId != request.ProcessSessionId || reply.RequestId != request.RequestId || reply.ProcessId != target.ProcessId)
                            {
                                operation.EffectUnknown = true;
                                operation.Reply = StandReply.Error("wrong-target-reply");
                            }
                            else operation.Reply = reply;
                        }
                        catch (Exception e) { operation.EffectUnknown = true; operation.Reply = StandReply.Error("invalid-reply", e.Message); }
                    }, JsonUtility.ToJson(request), request.TimeBudgetMs, EventDataSerialization.JsonUtility); }
                    catch (Exception e)
                    {
                        operation.Completed = true;
                        operation.EffectUnknown = true;
                        operation.Reply = StandReply.Error("transport-error", e.Message);
                    }
                }
            }
            Operations[request.RequestId] = operation;
            Fingerprints[request.RequestId] = fingerprint;
            return JsonUtility.ToJson(operation);
        }

        public static string Operation(string requestId)
        {
            return Operations.TryGetValue(requestId, out var value) ? JsonUtility.ToJson(value) :
                JsonUtility.ToJson(new StandOperation { RequestId = requestId, Completed = true, Reply = StandReply.Error("operation-unavailable") });
        }

        public static string Stop()
        {
            RequireMain();
            BeginStop();
            return Status();
        }

        private static void BeginStop()
        {
            _run = StandManifest.Read();
            if (_run == null || _run.OwnerPid != StandManifest.CurrentPid) return;
            if (_run.Phase == "stopping") return;
            _run.Phase = "stopping";
            _run.Write();
            _stopRequestedAt = EditorApplication.timeSinceStartup;
            PlayModeStandParticipant.Sync();
        }

        private static void Tick()
        {
            _run = StandManifest.Read();
            if (_run == null || _run.OwnerPid != StandManifest.CurrentPid) return;
            var local = PlayModeStandParticipant.Snapshot();
            if (local.RunId == _run.RunId) { Peers["main"] = local; Seen["main"] = EditorApplication.timeSinceStartup; }
            if (_run.Phase != "stopping") return;
            _nativeStopRequested |= _run.NativeStopRequested;
            if (_run.ReleasedParticipants != null)
                foreach (var state in _run.ReleasedParticipants)
                    if (!Peers.ContainsKey(state.ParticipantId)) Peers[state.ParticipantId] = state;
            bool released = _run.Participants.All(p => Peers.TryGetValue(p, out var state) && !state.Active && state.ProfileRestored && state.MarkerCount == 0);
            if (!_nativeStopRequested && (released || EditorApplication.timeSinceStartup - _stopRequestedAt > 8))
            {
                _nativeStopRequested = true;
                _run.NativeStopRequested = true;
                _run.ReleasedParticipants = Peers.Values.Where(p => p.RunId == _run.RunId).ToArray();
                _run.Write();
                NativeMethod("Stop").Invoke(null, null);
            }
            if (_nativeStopRequested && !EditorApplication.isPlayingOrWillChangePlaymode && NativeState() == "Idle") Restore();
        }

        private static void HandlePlay(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) BeginStop();
        }

        private static void ReceiveHello(string name, object[] args)
        {
            try
            {
                _run = StandManifest.Read();
                if (_run == null || _run.OwnerPid != StandManifest.CurrentPid || args == null || args.Length == 0) return;
                var state = JsonUtility.FromJson<StandParticipantState>(args[0] as string);
                if (state == null || state.RunId != _run.RunId || !_run.Admits(state.ParticipantId) || string.IsNullOrEmpty(state.ProcessSessionId)) return;
                Peers[state.ParticipantId] = state;
                Seen[state.ParticipantId] = EditorApplication.timeSinceStartup;
            }
            catch (Exception e) { GameLog.Debug.Error("[TestStand] Регистрация: " + e.Message); }
        }

        private static StandOperation Completed(StandRequest request, string code, StandParticipantState target = null)
        {
            var reply = StandReply.Error(code);
            reply.RunId = _run?.RunId; reply.ParticipantId = target?.ParticipantId;
            reply.ProcessSessionId = target?.ProcessSessionId; reply.ProcessId = target?.ProcessId ?? 0;
            reply.RequestId = request.RequestId;
            return new StandOperation { RequestId = request.RequestId, Completed = true, Reply = reply };
        }

        private static ScriptableObject BuildScenario()
        {
            var original = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Settings/PlayMode/Server+client.asset");
            if (original == null) throw new InvalidOperationException("Нет сценария Server+client.");
            var copy = UnityEngine.Object.Instantiate(original);
            copy.name = "VR Test Stand " + _run.RunId;
            copy.hideFlags = HideFlags.DontSave;
            var serialized = new SerializedObject(copy);
            var clients = serialized.FindProperty("m_EditorInstances");
            if (clients == null || clients.arraySize != 1) throw new InvalidOperationException("Изменился контракт EditorInstances сценария.");
            for (int i = 1; i < _run.Configuration.ClientCount; i++) clients.InsertArrayElementAtIndex(i);
            if (_run.Configuration.ClientCount == 0) clients.ClearArray();
            // Флаг включает всю Editor-группу, в том числе главный процесс при пустом списке дополнительных.
            serialized.FindProperty("m_EnableEditors").boolValue = true;
            serialized.FindProperty("m_MainEditorInstance").FindPropertyRelative("m_PlayerTag").stringValue =
                _run.Configuration.Role == "host" ? "Host" : _run.Configuration.Role == "server" ? "Server" : "Client";
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(_run.StartScenePath);
            if (scene == null) throw new InvalidOperationException("Стартовая сцена отсутствует.");
            serialized.FindProperty("m_MainEditorInstance").FindPropertyRelative("m_InitialScene").objectReferenceValue = scene;
            for (int i = 0; i < clients.arraySize; i++)
            {
                var child = clients.GetArrayElementAtIndex(i); string name = "Player " + (i + 2);
                child.FindPropertyRelative("Name").stringValue = name;
                child.FindPropertyRelative("m_PlayerTag").stringValue = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(_run.Configuration.RoleForParticipant(i + 1));
                child.FindPropertyRelative("<CorrespondingNodeId>k__BackingField").stringValue = name + "|" + (i + 1) + "_run";
                var nodes = child.FindPropertyRelative("m_Nodes");
                for (int n = 0; n < nodes.arraySize; n++) nodes.GetArrayElementAtIndex(n).stringValue = name + "|" + (i + 1) + (n == 0 ? "_run" : "_deploy");
                child.FindPropertyRelative("m_InitialScene").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Offline.unity");
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return copy;
        }

        private static StandSetting Capture(string key, bool fallback) => new StandSetting { Key = key, HadKey = EditorPrefs.HasKey(key), Value = EditorPrefs.GetBool(key, fallback) };

        private static void Restore()
        {
            if (_run == null) return;
            _lastRunId = _run.RunId;
            _cleanupPassed = _run.Participants.All(p => Peers.TryGetValue(p, out var state) && !state.Active && state.ProfileRestored && state.MarkerCount == 0 && state.PersistentTokenUnchanged);
            PlayModeStandParticipant.Sync();
            var configurations = (IEnumerable)NativeType("Unity.PlayMode.Editor.PlayModeScenarioUtils").GetMethod("GetAllConfigs", Flags).Invoke(null, null);
            UnityEngine.Object previous = null;
            foreach (UnityEngine.Object item in configurations)
                if ((!string.IsNullOrEmpty(_run.PreviousScenarioPath) && AssetDatabase.GetAssetPath(item) == _run.PreviousScenarioPath) ||
                    (string.IsNullOrEmpty(_run.PreviousScenarioPath) && item.name == _run.PreviousScenarioName)) { previous = item; break; }
            var active = ActiveScenario();
            if (_temporaryScenario == null && active != null && active.name == "VR Test Stand " + _run.RunId)
                _temporaryScenario = active as ScriptableObject;
            if (previous != null) SetScenario(previous);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(_run.PreviousStartScenePath);
            if (File.Exists(StandManifest.FilePath)) File.Delete(StandManifest.FilePath);
            if (_temporaryScenario != null) UnityEngine.Object.DestroyImmediate(_temporaryScenario);
            _temporaryScenario = null;
            _run = null;
            PlayLaunch.ReleaseCompletedRun(_lastRunId);
        }

        private static int FreeUdpPort()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return ((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }

        private static void RequireMain()
        {
            if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor) throw new InvalidOperationException("Координатор доступен только в управляющем редакторе.");
        }

        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        [Serializable]
        private sealed class StandConfiguration
        {
            public string ScenarioName;
            public string ScenarioPath;
            public StandSetting[] Settings;
        }
        private static Type NativeType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null);
        private static Type Manager => NativeType("Unity.PlayMode.Editor.PlayModeScenarioManager");
        private static MethodInfo NativeMethod(string name) => Manager.GetMethod(name, Flags);
        private static string NativeState() => Manager.GetProperty("State", Flags).GetValue(null).ToString();
        private static UnityEngine.Object ActiveScenario() => (UnityEngine.Object)Manager.GetProperty("ActiveScenario", Flags).GetValue(null);
        private static void SetScenario(UnityEngine.Object scenario) => Manager.GetProperty("ActiveScenario", Flags).SetValue(null, scenario);
    }
}
