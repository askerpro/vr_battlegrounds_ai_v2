using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.EditorTools.TestStand
{
    [Serializable]
    public sealed class ResolvedPlayPlan
    {
        public bool Passed;
        public string Code;
        public string Message;
        public string SceneKind;
        public string StartScenePath;
        public string TargetScenePath;
        public string TargetSceneName;
        public bool ExpectTargetScene;
        public PlayLaunchConfiguration Configuration;
        public string PlanHash;
    }
    [Serializable]
    public sealed class PlayLaunchStatus
    {
        public string RunId;
        public string Owner;
        public string State;
        public string Error;
        public bool Ready;
        public bool CleanupPassed;
        public bool Playing;
        public StandParticipantState[] Participants;
    }

    /// <summary>Один facade окна и MCP. Backend запуска остаётся у PlayModeTestStand/штатной сети.</summary>
    [InitializeOnLoad]
    public static class PlayLaunch
    {
        private const string RequestKey = "VrBattlegrounds.PlayLaunch.Request";
        private const string ErrorKey = "VrBattlegrounds.PlayLaunch.Error";
        private const string NativePreviousSceneKey = "VrBattlegrounds.PlayLaunch.NativePreviousScene";
        private static readonly PlayLaunchRequestOwner RequestOwner = new PlayLaunchRequestOwner();
        private static IDisposable _lease;
        private static string _lastState;
        private static double _started;
        private static bool _readyReached;
        private static StartupRouteHandle _nativeStartupRoute;
        private static Func<string> _nativeBeforeServerStart;
        private static Func<string> _nativeClientAddress;
        private static Func<GameNetworkDiscovery.AppRole?> _nativeRole;
        [Serializable] private sealed class RequestState { public string Owner; public string RunId; public ResolvedPlayPlan Plan; }

        static PlayLaunch()
        {
            PlayLaunchSettings.BindActiveLaunch(ParticipantConfiguration, () => StandManifest.Read() != null || ReadRequest() != null);
            EditorApplication.update += Tick;
            SceneManager.sceneLoaded += HandleNativeScene;
        }

        private static RequestState ReadRequest()
        {
            string json = SessionState.GetString(RequestKey, "");
            return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<RequestState>(json);
        }
        private static PlayLaunchConfiguration ParticipantConfiguration()
        {
            var run = StandManifest.Read();
            if (run != null && run.Phase == "running" && run.Admits(StandManifest.ParticipantName()) && run.Configuration != null)
            {
                return ResolveParticipantConfiguration(run, StandManifest.ParticipantName());
            }
            return Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor ? ReadRequest()?.Plan.Configuration.Copy() : null;
        }
        internal static PlayLaunchConfiguration ResolveParticipantConfiguration(StandManifest run, string participant)
        {
            var config = run.Configuration.Copy();
            int index = Array.IndexOf(run.Participants, participant);
            if (index < 0) throw new PlayLaunchException("ParticipantUnknown", "Участник отсутствует в topology.");
            config.Role = run.Configuration.RoleForParticipant(index);
            config.HostIsAdmin = config.Role == "host" && config.HostIsAdmin;
            config.ClientCount = 0; config.AdditionalPlayerRoles = null;
            return config;
        }

        public static string Plan(string json = null) => JsonUtility.ToJson(Resolve(json));
        /// <summary>Toolbar Play с явным источником использует тот же planner и замороженный request.</summary>
        public static void PrepareNativePlay()
        {
            if (ReadRequest() != null || StandManifest.Read() != null) throw new PlayLaunchException("RequestOwnerBusy", "Launch уже принадлежит владельцу.");
            var plan = Resolve(null);
            if (!plan.Passed) throw new PlayLaunchException(plan.Code, plan.Message);
            PlayModeTestStand.RequireSingleEditorNativePlay(plan.Configuration.Role);
            if (plan.Configuration.ClientCount != 0) throw new PlayLaunchException("NativeTopologyUnsupported", "Этот профиль требует запуска через Play Launch.");
            string previous = AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
            Acquire("native-play", plan);
            SessionState.SetString(NativePreviousSceneKey, previous);
            UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.StartScenePath);
        }
        public static void ReleaseNativePlay()
        {
            var request = ReadRequest();
            if (request == null || request.Owner != "native-play" || StandManifest.Read() != null) return;
            _nativeStartupRoute?.Dispose(); _nativeStartupRoute = null;
            if (DebugBootstrapGate.EditorBeforeServerStart == _nativeBeforeServerStart)
                DebugBootstrapGate.EditorBeforeServerStart = null;
            _nativeBeforeServerStart = null;
            if (DebugBootstrapGate.EditorClientAddressOverride == _nativeClientAddress)
                DebugBootstrapGate.EditorClientAddressOverride = null;
            if (DebugBootstrapGate.EditorRoleOverride == _nativeRole)
                DebugBootstrapGate.EditorRoleOverride = null;
            _nativeClientAddress = null; _nativeRole = null;
            string previous = SessionState.GetString(NativePreviousSceneKey, "");
            ReleaseCompletedRun(request.RunId);
            SessionState.EraseString(NativePreviousSceneKey);
            UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        }
        internal static void ReportNativeNetworkFailure(string error)
        {
            var request = ReadRequest();
            if (request == null || request.Owner != "native-play" || StandManifest.Read() != null) return;
            SessionState.SetString(ErrorKey, error);
            EditorApplication.delayCall += () =>
            {
                if (ReadRequest()?.RunId == request.RunId && StandManifest.Read() == null)
                    EditorApplication.isPlaying = false;
            };
        }
        private static void HandleNativeScene(Scene scene, LoadSceneMode mode)
        {
            var request = ReadRequest();
            if (request == null || request.Owner != "native-play" || StandManifest.Read() != null || request.Plan.SceneKind != "Game") return;
            _nativeClientAddress = () => request.Plan.Configuration.ClientAddress;
            _nativeRole = () => (GameNetworkDiscovery.AppRole)Enum.Parse(typeof(GameNetworkDiscovery.AppRole), request.Plan.Configuration.Role, true);
            DebugBootstrapGate.EditorClientAddressOverride = _nativeClientAddress;
            DebugBootstrapGate.EditorRoleOverride = _nativeRole;
            _nativeBeforeServerStart = () =>
            {
                if (_nativeStartupRoute != null || request.Plan.TargetSceneName == "Lobby") return null;
                if (ServerStartupRoute.TryRequest(request.Plan.TargetSceneName, request.Plan.Configuration.ModeId,
                    request.RunId + ":native-play", out _nativeStartupRoute, out string error)) return null;
                SessionState.SetString(ErrorKey, error);
                EditorApplication.delayCall += () =>
                {
                    if (ReadRequest()?.RunId == request.RunId && StandManifest.Read() == null)
                        EditorApplication.isPlaying = false;
                };
                return error;
            };
            DebugBootstrapGate.EditorBeforeServerStart = _nativeBeforeServerStart;
        }

        private static ResolvedPlayPlan Resolve(string json)
        {
            try
            {
                var config = string.IsNullOrWhiteSpace(json) ? PlayLaunchSettings.ReadProfile() : PlayLaunchSettings.Decode(json);
                if (config == null) throw new PlayLaunchException("ConfigurationInvalid", "Нет конфигурации.");
                config.Validate();
                if (config.Role == "ask") throw new PlayLaunchException("RoleRequired", "Выберите роль до автоматического запуска.");
                string target = config.SceneSource == "scene" ? config.ScenePath : config.SceneSource == "lobby" ? "Assets/Scenes/Lobby.unity" : SceneManager.GetActiveScene().path;
                if (string.IsNullOrEmpty(target) || target == "Assets/Scenes/Offline.unity") target = "Assets/Scenes/Lobby.unity";
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(target) == null) throw new PlayLaunchException("SceneNotLoadable", "Сцена не найдена: " + target);
                string[] dependencies = AssetDatabase.GetDependencies(target, true);
                bool game = dependencies.Contains("Assets/Scripts/Maps/Runtime/MapRoot.cs");
                bool standalone = dependencies.Contains("Assets/Scripts/Core/StandaloneSceneMarker.cs");
                if (game && standalone) throw new PlayLaunchException("SceneKindConflict", "MapRoot и StandaloneSceneMarker одновременно.");
                if (!game && !standalone && !config.AllowUnmarked) throw new PlayLaunchException("UnmarkedScene", "Неразмеченная сцена требует AllowUnmarked.");
                string kind = game ? "Game" : standalone ? "Standalone" : "Unmarked";
                if (!game && config.ClientCount > 0) throw new PlayLaunchException("TopologyInvalid", "Standalone не запускает сетевые экземпляры.");
                if (game && !EditorBuildSettings.scenes.Any(s => s.enabled && s.path == target)) throw new PlayLaunchException("SceneNotInBuild", "Сетевой сцены нет в Build Settings.");
                if (game)
                {
                    var catalog = AssetDatabase.LoadAssetAtPath<MapRuntimeCatalog>("Assets/Data/Maps/MapRuntimeCatalog.asset");
                    if (catalog == null) throw new PlayLaunchException("CatalogMissing", "Нет MapRuntimeCatalog.");
                    string sceneName = Path.GetFileNameWithoutExtension(target);
                    var map = catalog.Maps != null ? catalog.Maps.GetBySceneName(sceneName) : null;
                    if (map == null) map = catalog.DebugMaps.FirstOrDefault(m => m != null && m.sceneName == sceneName);
                    if (map == null) throw new PlayLaunchException("MapNotRegistered", "MapRoot есть, но карта не зарегистрирована.");
                    if (config.HasOwnedServer && map.kind.ToString() != "Lobby" && !string.IsNullOrEmpty(config.ModeId) &&
                        !(map.supportedModes ?? Array.Empty<VrBattlegrounds.GameModes.GameModeData>()).Any(m => m != null && m.modeId == config.ModeId))
                        throw new PlayLaunchException("ModeIncompatible", "Карта не поддерживает modeId " + config.ModeId);
                }
                var frozen = config.Copy();
                frozen.SceneSource = game ? "scene" : config.SceneSource;
                frozen.ScenePath = target;
                if (!game) { frozen.Enabled = false; frozen.Readiness = "standalone"; }
                if (target == "Assets/Scenes/Lobby.unity") { frozen.ModeId = ""; frozen.AutoGoLive = false; }
                var resolved = new ResolvedPlayPlan { Passed = true, Code = "Planned", SceneKind = kind,
                    StartScenePath = game ? "Assets/Scenes/Offline.unity" : target, TargetScenePath = target,
                    TargetSceneName = Path.GetFileNameWithoutExtension(target), Configuration = frozen,
                    ExpectTargetScene = config.HasOwnedServer || config.SceneSource == "scene" || config.SceneSource == "lobby",
                    Message = kind == "Unmarked" ? "Неразмеченная сцена: запуск напрямую по явному разрешению." : "" };
                using (var hash = SHA256.Create()) resolved.PlanHash = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(
                    JsonUtility.ToJson(resolved) + AssetDatabase.GetAssetDependencyHash(target)))).Replace("-", "").ToLowerInvariant();
                return resolved;
            }
            catch (PlayLaunchException error) { return new ResolvedPlayPlan { Code = error.Code, Message = error.Message }; }
            catch (Exception error) { return new ResolvedPlayPlan { Code = "PlanInvalid", Message = error.Message }; }
        }

        public static string Request(string json, string owner, string expectedPlanHash = null)
        {
            RequireIdle();
            var plan = Resolve(json);
            if (!plan.Passed) return JsonUtility.ToJson(plan);
            if (expectedPlanHash != null && expectedPlanHash != plan.PlanHash) throw new PlayLaunchException("PlanChanged", "План изменился после preview.");
            Acquire(owner, plan);
            return JsonUtility.ToJson(ReadRequest());
        }
        private static void Acquire(string owner, ResolvedPlayPlan plan)
        {
            if (ReadRequest() != null) throw new PlayLaunchException("RequestOwnerBusy", "Запрос уже принадлежит " + ReadRequest().Owner);
            _lease = RequestOwner.Acquire(owner, plan.Configuration);
            try
            {
                SessionState.SetString(RequestKey, JsonUtility.ToJson(new RequestState { Owner = owner, RunId = RequestOwner.RunId, Plan = plan }));
                SessionState.EraseString(ErrorKey);
            }
            catch { _lease.Dispose(); _lease = null; throw; }
        }
        public static string Play(string json, string owner, string expectedPlanHash = null)
        {
            RequireIdle();
            var request = ReadRequest();
            if (request == null)
            {
                var plan = Resolve(json); if (!plan.Passed) return JsonUtility.ToJson(plan);
                if (expectedPlanHash != null && expectedPlanHash != plan.PlanHash) throw new PlayLaunchException("PlanChanged", "План изменился после preview.");
                Acquire(owner, plan); request = ReadRequest();
            }
            else if (request.Owner != owner || (!string.IsNullOrEmpty(json) && JsonUtility.ToJson(Resolve(json)) != JsonUtility.ToJson(request.Plan)))
                throw new PlayLaunchException("RequestOwnerBusy", "Нельзя заменить чужой/замороженный запрос.");
            if (expectedPlanHash != null && expectedPlanHash != request.Plan.PlanHash) throw new PlayLaunchException("PlanChanged", "Запрос имеет другой plan hash.");
            return Begin(request, false);
        }
        private static string Begin(RequestState request, bool marker)
        {
            try
            {
                _started = EditorApplication.timeSinceStartup;
                _readyReached = false;
                PlayModeTestStand.StartConfigured(request.Plan.Configuration, request.Owner, request.RunId,
                    request.Plan.StartScenePath, request.Plan.TargetScenePath, request.Plan.SceneKind, marker, request.Plan.ExpectTargetScene);
                return Status();
            }
            catch { ReleaseCompletedRun(request.RunId); throw; }
        }
        internal static string StartMarkerProbe(bool legacyBootstrapEnabled)
        {
            RequireIdle();
            var configuration = new PlayLaunchConfiguration { Role = "server", ClientCount = 2, Enabled = false,
                HostIsAdmin = false, PauseOnFocusLoss = false, AutoGoLive = false, BotCount = 0, ModeId = "", Readiness = "connected" };
            Acquire("marker-probe", new ResolvedPlayPlan { Passed = true, Code = "Planned", SceneKind = "Game",
                StartScenePath = "Assets/Scenes/Offline.unity", TargetScenePath = "Assets/Scenes/Lobby.unity", TargetSceneName = "Lobby", Configuration = configuration });
            Begin(ReadRequest(), true);
            return PlayModeTestStand.Status();
        }

        public static string Status()
        {
            var backend = JsonUtility.FromJson<StandStatus>(PlayModeTestStand.Status());
            var run = StandManifest.Read();
            string error = SessionState.GetString(ErrorKey, "");
            if (run != null && string.IsNullOrEmpty(error))
                error = backend.Participants.FirstOrDefault(p => p.RunId == run.RunId && !string.IsNullOrEmpty(p.StartupError))?.StartupError;
            bool ready = run != null && run.Phase == "running" && backend.Participants.Length == run.Participants.Length &&
                backend.Participants.All(p => p.LastSeenAgeSeconds <= 5 && p.Active && p.Playing && (run.SceneKind != "Game" ? !p.Server && !p.Client :
                    ResolveParticipantConfiguration(run, p.ParticipantId).Role == "server" ? p.Server && !p.Client : p.Client && p.Connected));
            if (ready && run.Configuration.Readiness == "map-playable") ready = backend.Participants.All(p => p.MapPlayable &&
                p.MapRunValid && (!run.ExpectTargetScene || p.Scene == Path.GetFileNameWithoutExtension(run.TargetScenePath)));
            if (ready && run.Configuration.Readiness == "map-playable" && run.Configuration.HasOwnedServer)
            {
                var root = backend.Participants.First(p => p.Server);
                ready = backend.Participants.All(p => p.MapRunKey == root.MapRunKey);
            }
            string state = !string.IsNullOrEmpty(error) ? "Failed" : run == null ? (ReadRequest() != null ? "Requested" : "Idle") :
                run.Phase == "stopping" ? "Stopping" : ready ? "Ready" : "WaitingReady";
            return JsonUtility.ToJson(new PlayLaunchStatus { RunId = run?.RunId ?? ReadRequest()?.RunId ?? backend.LastRunId,
                Owner = run?.Owner ?? ReadRequest()?.Owner, State = state, Error = error, Ready = ready,
                CleanupPassed = ReadRequest() != null && run == null ? false : backend.CleanupPassed, Playing = backend.Playing,
                Participants = ReadRequest() != null && run == null ? Array.Empty<StandParticipantState>() : backend.Participants });
        }
        public static string Cancel(string runId, string owner)
        {
            var run = StandManifest.Read(); var request = ReadRequest();
            if ((run != null && (run.RunId != runId || run.Owner != owner)) || (request != null && (request.RunId != runId || request.Owner != owner)))
                throw new PlayLaunchException("WrongOwner", "Cancel не принадлежит этому owner/run.");
            if (run != null) PlayModeTestStand.Stop();
            else if (request != null && request.Owner == "native-play")
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
                else ReleaseNativePlay();
            }
            else ReleaseCompletedRun(runId);
            return Status();
        }
        internal static void ReleaseCompletedRun(string runId)
        {
            var request = ReadRequest(); if (request == null || request.RunId != runId) return;
            SessionState.EraseString(RequestKey); _lease?.Dispose(); _lease = null;
        }
        private static void Tick()
        {
            if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor) return;
            var run = StandManifest.Read();
            if (run == null || run.OwnerPid != StandManifest.CurrentPid || run.Phase != "running") return;
            var status = JsonUtility.FromJson<PlayLaunchStatus>(Status());
            if (status.State == "Failed")
            {
                SessionState.SetString(ErrorKey, status.Error);
                PlayModeTestStand.Stop();
                return;
            }
            if (status.Ready) _readyReached = true;
            if (_lastState != status.State) { GameLog.Debug.Info("[PlayLaunch] " + (_lastState ?? "Idle") + " → " + status.State); _lastState = status.State; }
            if (_started == 0) _started = EditorApplication.timeSinceStartup;
            if (!_readyReached && !status.Ready && EditorApplication.timeSinceStartup - _started > run.Configuration.TimeoutSeconds)
            { SessionState.SetString(ErrorKey, "LaunchNotReady"); PlayModeTestStand.Stop(); }
        }
        private static void RequireIdle()
        {
            if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor) throw new PlayLaunchException("MainEditorRequired", "Запуск координирует главный редактор.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || StandManifest.Read() != null) throw new PlayLaunchException("EditorBusy", "Редактор уже занят.");
            string legacyOwner = SessionState.GetString("VrBattlegrounds.TemporaryPlaySceneOwner", "");
            if (!string.IsNullOrEmpty(legacyOwner)) throw new PlayLaunchException("StartSceneOwnerBusy", "Стартовая сцена занята: " + legacyOwner);
        }
    }
}
