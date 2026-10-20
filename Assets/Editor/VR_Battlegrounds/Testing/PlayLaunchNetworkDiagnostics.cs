using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Mirror;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools;

namespace VrBattlegrounds.EditorTools.TestStand
{
    /// <summary>Пассивный trace одного managed участника; сеть, readiness и таймауты не меняет.</summary>
    internal sealed class PlayLaunchNetworkDiagnostics : IDisposable
    {
        [Serializable]
        private sealed class Entry
        {
            public string utc, runId, participant, processSession, kind, detail, scene;
            public long monotonicTicks, frequency;
            public int pid, frame;
            public bool server, client, connected;
        }

        private sealed class Binding
        {
            public Transport Transport;
            public kcp2k.KcpTransport Kcp;
            public bool DebugLog;
        }

        private struct Gap
        {
            public long Previous, Maximum, Count;
        }

        private readonly string _runId, _participant, _session;
        private readonly StreamWriter _writer;
        private readonly List<Binding> _bindings = new List<Binding>();
        private readonly Action<string> _previousKcpInfo;
        private Action<string> _installedKcpInfo;
        private Gap _early, _late, _editor;
        private bool _loopAttached, _writing, _disposed, _ioFailed;

        internal static IDisposable Begin(string runId, string participant, string session)
        {
            try { return new PlayLaunchNetworkDiagnostics(runId, participant, session); }
            catch (Exception e)
            {
                GameLog.Network.Warning("[LaunchDiag] Trace не открыт: " + e.Message);
                return null;
            }
        }

        private PlayLaunchNetworkDiagnostics(string runId, string participant, string session)
        {
            if (DebugBootstrapGate.EditorBeforeNetworkAwake != null || DebugBootstrapGate.EditorNetworkDiagnosticEvent != null)
                throw new InvalidOperationException("Диагностические hooks уже принадлежат другому scope.");
            _runId = runId; _participant = participant; _session = session;
            string directory = PlayModeStandParticipant.LogArchiveDirectory(runId);
            Directory.CreateDirectory(directory);
            _writer = new StreamWriter(Path.Combine(directory, "startup-" + StandManifest.CurrentPid + ".jsonl"), true, new UTF8Encoding(false)) { AutoFlush = true };
            _previousKcpInfo = kcp2k.Log.Info;
            DebugBootstrapGate.EditorBeforeNetworkAwake = BeforeAwake;
            DebugBootstrapGate.EditorNetworkDiagnosticEvent = NetworkEvent;
            Application.logMessageReceived += LogMessage;
            EditorApplication.playModeStateChanged += PlayState;
            EditorApplication.update += EditorTick;
            SceneManager.sceneLoaded += SceneLoaded;
            _editor.Previous = System.Diagnostics.Stopwatch.GetTimestamp();
            Write("scope-begin", "KCP debug включается до Awake; данные диагностики не являются проверкой поведения.");
        }

        private void BeforeAwake(NetworkManager manager)
        {
            try
            {
                // Даже ожидающий уничтожения дубликат Offline может выполнить KCP Awake
                // и сменить process-wide Log.Info: диагностический scope учитывает и его.
                var transport = manager.GetComponent<Transport>();
                if (transport == null || _bindings.Exists(b => b.Transport == transport)) return;
                var binding = new Binding { Transport = transport, Kcp = transport as kcp2k.KcpTransport };
                if (binding.Kcp != null) { binding.DebugLog = binding.Kcp.debugLog; binding.Kcp.debugLog = true; }
                _bindings.Add(binding);
                transport.OnClientConnected += ClientConnected;
                transport.OnClientDisconnected += ClientDisconnected;
                transport.OnClientError += ClientError;
                transport.OnClientTransportException += ClientException;
                transport.OnServerConnectedWithAddress += ServerConnected;
                transport.OnServerDisconnected += ServerDisconnected;
                transport.OnServerError += ServerError;
                transport.OnServerTransportException += ServerException;
                // Mirror сбрасывает hooks в BeforeSceneLoad; Awake менеджера идёт после этого сброса.
                if (!_loopAttached)
                {
                    NetworkLoop.OnEarlyUpdate += EarlyTick;
                    NetworkLoop.OnLateUpdate += LateTick;
                    _early.Previous = _late.Previous = System.Diagnostics.Stopwatch.GetTimestamp();
                    _loopAttached = true;
                }
                Write("before-network-awake", transport.GetType().Name + "; KCP debugLog=" + (binding.Kcp != null));
            }
            catch (Exception e) { Write("diagnostic-error", e.ToString()); }
        }

        private void CaptureKcpLogger()
        {
            if (_installedKcpInfo == null && _bindings.Exists(b => b.Kcp != null))
                _installedKcpInfo = kcp2k.Log.Info;
        }

        private void NetworkEvent(string kind, string detail)
        {
            CaptureKcpLogger();
            Write(kind, detail);
        }

        private void ClientConnected() => Write("transport-client-connected", "");
        private void ClientDisconnected() => Write("transport-client-disconnected", "");
        private void ClientError(TransportError error, string reason) => Write("transport-client-error", error + ": " + reason);
        private void ClientException(Exception error) => Write("transport-client-exception", error.ToString());
        private void ServerConnected(int id, string address) => Write("transport-server-connected", id + " " + address);
        private void ServerDisconnected(int id) => Write("transport-server-disconnected", id.ToString(CultureInfo.InvariantCulture));
        private void ServerError(int id, TransportError error, string reason) => Write("transport-server-error", id + " " + error + ": " + reason);
        private void ServerException(int id, Exception error) => Write("transport-server-exception", id + " " + error);
        private void PlayState(PlayModeStateChange state) { CaptureKcpLogger(); Write("play-mode", state.ToString()); }
        private void SceneLoaded(Scene scene, LoadSceneMode mode) => Write("scene-loaded", scene.path + " " + mode);

        private void LogMessage(string message, string stack, LogType type)
        {
            if (_writing || _disposed || message.StartsWith("[LaunchDiag]", StringComparison.Ordinal)) return;
            if (message.IndexOf("[KCP]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.StartsWith("[GameNetwork", StringComparison.Ordinal) ||
                message.StartsWith("[PlayLaunch]", StringComparison.Ordinal) ||
                message.StartsWith("[MapLoader]", StringComparison.Ordinal) ||
                message.StartsWith("[MapBootstrap]", StringComparison.Ordinal) ||
                message.StartsWith("[BotDirector]", StringComparison.Ordinal) ||
                message.StartsWith("Client Transport Error", StringComparison.Ordinal) ||
                message.StartsWith("Server Transport Error", StringComparison.Ordinal))
                Write("engine-log", type + ": " + message);
        }

        private void EarlyTick() { CaptureKcpLogger(); ObserveGap(ref _early, "network-early-completion-gap"); }
        private void LateTick() => ObserveGap(ref _late, "network-late-hook-gap");
        private void EditorTick() => ObserveGap(ref _editor, "editor-update-gap");

        private void ObserveGap(ref Gap gap, string kind)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            long elapsed = gap.Previous == 0 ? 0 : now - gap.Previous;
            gap.Previous = now;
            gap.Count++;
            if (elapsed > gap.Maximum) gap.Maximum = elapsed;
            // Только редкие интервалы >=1s; в обычном кадре нет строк, JSON или файловых операций.
            if (elapsed >= System.Diagnostics.Stopwatch.Frequency)
                Write(kind, (elapsed * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("F3", CultureInfo.InvariantCulture) + " ms");
        }

        private void Write(string kind, string detail)
        {
            if (_writing || _disposed) return;
            _writing = true;
            try
            {
                var entry = new Entry {
                    utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    monotonicTicks = System.Diagnostics.Stopwatch.GetTimestamp(), frequency = System.Diagnostics.Stopwatch.Frequency,
                    runId = _runId, participant = _participant, processSession = _session, pid = StandManifest.CurrentPid,
                    frame = Time.frameCount, scene = SceneManager.GetActiveScene().name,
                    server = NetworkServer.active, client = NetworkClient.active, connected = NetworkClient.isConnected,
                    kind = kind, detail = detail != null && detail.Length > 4096 ? detail.Substring(0, 4096) + " [truncated]" : detail
                };
                string json = JsonUtility.ToJson(entry);
                if (!_ioFailed)
                {
                    try { _writer.WriteLine(json); }
                    catch (Exception e) { _ioFailed = true; GameLog.Network.Warning("[LaunchDiag] Запись trace прекращена: " + e.Message); }
                }
                GameLog.Network.Info("[LaunchDiag] " + json);
            }
            catch (Exception e) { GameLog.Network.Warning("[LaunchDiag] Диагностика пропустила событие: " + e.Message); }
            finally { _writing = false; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            CaptureKcpLogger();
            Write("scope-end", "max gaps ms: networkEarlyCompletion=" + Milliseconds(_early.Maximum) +
                "; networkLateHook=" + Milliseconds(_late.Maximum) + "; editorUpdate=" + Milliseconds(_editor.Maximum) +
                "; samples=" + _early.Count + "/" + _late.Count + "/" + _editor.Count);
            _disposed = true;
            if (DebugBootstrapGate.EditorBeforeNetworkAwake == BeforeAwake) DebugBootstrapGate.EditorBeforeNetworkAwake = null;
            if (DebugBootstrapGate.EditorNetworkDiagnosticEvent == NetworkEvent) DebugBootstrapGate.EditorNetworkDiagnosticEvent = null;
            Application.logMessageReceived -= LogMessage;
            EditorApplication.playModeStateChanged -= PlayState;
            EditorApplication.update -= EditorTick;
            SceneManager.sceneLoaded -= SceneLoaded;
            NetworkLoop.OnEarlyUpdate -= EarlyTick;
            NetworkLoop.OnLateUpdate -= LateTick;
            foreach (var binding in _bindings)
            {
                var transport = binding.Transport;
                if (transport == null) continue;
                transport.OnClientConnected -= ClientConnected;
                transport.OnClientDisconnected -= ClientDisconnected;
                transport.OnClientError -= ClientError;
                transport.OnClientTransportException -= ClientException;
                transport.OnServerConnectedWithAddress -= ServerConnected;
                transport.OnServerDisconnected -= ServerDisconnected;
                transport.OnServerError -= ServerError;
                transport.OnServerTransportException -= ServerException;
                if (binding.Kcp != null) binding.Kcp.debugLog = binding.DebugLog;
            }
            if (_installedKcpInfo != null && kcp2k.Log.Info == _installedKcpInfo) kcp2k.Log.Info = _previousKcpInfo;
            try { _writer.Dispose(); }
            catch (Exception e) { GameLog.Network.Warning("[LaunchDiag] Закрытие trace: " + e.Message); }
        }

        private static string Milliseconds(long ticks) =>
            (ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("F3", CultureInfo.InvariantCulture);
    }
}
