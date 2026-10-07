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
            RequireMain();
            if (EditorApplication.isPlayingOrWillChangePlaymode || NativeState() != "Idle")
                throw new InvalidOperationException("Сначала завершите текущий Play Mode сценарий.");
            var existing = StandManifest.Read();
            if (existing != null) throw new InvalidOperationException("Уже существует активный запуск стенда.");
            UnityEngine.Object previous = ActiveScenario();
            _run = new StandManifest
            {
                RunId = Guid.NewGuid().ToString("N"), OwnerPid = StandManifest.CurrentPid, Phase = "running",
                Participants = new[] { "main", "Player 2", "Player 3" },
                PreviousScenarioName = previous.name, PreviousScenarioPath = AssetDatabase.GetAssetPath(previous),
                NetworkPort = FreeUdpPort(), DiscoveryPort = FreeUdpPort(),
                Settings = new[] { Capture("VrBattlegrounds.DebugBootstrap.Enabled", true),
                    Capture("VrBattlegrounds.DebugBootstrap.HostIsAdmin", true),
                    Capture("VrBattlegrounds.PauseXrWhenEditorUnfocused", true) }
            };
            while (_run.DiscoveryPort == _run.NetworkPort) _run.DiscoveryPort = FreeUdpPort();
            Peers.Clear(); Seen.Clear(); Operations.Clear(); Fingerprints.Clear();
            _cleanupPassed = false;
            _nativeStopRequested = false;
            try
            {
                _run.Write();
                EditorPrefs.SetBool("VrBattlegrounds.DebugBootstrap.Enabled", debugBootstrapEnabled);
                EditorPrefs.SetBool("VrBattlegrounds.DebugBootstrap.HostIsAdmin", debugBootstrapEnabled);
                UxrManager.EditorFocusPauseEnabled = false;
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
            Tick();
            var run = StandManifest.Read();
            return JsonUtility.ToJson(new StandStatus
            {
                RunId = run?.RunId, Phase = run?.Phase ?? "idle", Playing = EditorApplication.isPlaying,
                CleanupPassed = _cleanupPassed, LastRunId = _lastRunId,
                Participants = Peers.Values.Where(p => p.RunId == (run?.RunId ?? _lastRunId)).OrderBy(p => p.ParticipantId).ToArray()
            });
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
            clients.InsertArrayElementAtIndex(1);
            var third = clients.GetArrayElementAtIndex(1);
            third.FindPropertyRelative("Name").stringValue = "Player 3";
            third.FindPropertyRelative("<CorrespondingNodeId>k__BackingField").stringValue = "Player 3|2_run";
            var nodes = third.FindPropertyRelative("m_Nodes");
            for (int i = 0; i < nodes.arraySize; i++) nodes.GetArrayElementAtIndex(i).stringValue = i == 0 ? "Player 3|2_run" : "Player 3|2_deploy";
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Offline.unity");
            serialized.FindProperty("m_MainEditorInstance").FindPropertyRelative("m_InitialScene").objectReferenceValue = scene;
            for (int i = 0; i < clients.arraySize; i++) clients.GetArrayElementAtIndex(i).FindPropertyRelative("m_InitialScene").objectReferenceValue = scene;
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
            foreach (var setting in _run.Settings)
            {
                if (setting.Key == "VrBattlegrounds.PauseXrWhenEditorUnfocused") UxrManager.EditorFocusPauseEnabled = setting.Value;
                if (setting.HadKey) EditorPrefs.SetBool(setting.Key, setting.Value); else EditorPrefs.DeleteKey(setting.Key);
            }
            if (File.Exists(StandManifest.FilePath)) File.Delete(StandManifest.FilePath);
            if (_temporaryScenario != null) UnityEngine.Object.DestroyImmediate(_temporaryScenario);
            _temporaryScenario = null;
            _run = null;
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
