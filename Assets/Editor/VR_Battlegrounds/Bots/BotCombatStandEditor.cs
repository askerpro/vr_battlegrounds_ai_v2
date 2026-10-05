using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Newtonsoft.Json;
using UltimateXR.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.DevTools.BotCombatStand;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Временная конфигурация запуска и восстановление независимо от исхода прогона.</summary>
    [InitializeOnLoad]
    public static class BotCombatStandEditor
    {
        private const string Key = "BotCombatStand.";
        private const string Owner = "BotCombatStand";
        private static MapRegistry _registry, _originalRegistry;
        private static double _deadline;
        [Serializable] private sealed class SceneEntry { public string Path; public bool Enabled; }
        [Serializable] private sealed class Prefs
        {
            public bool Focus, Enabled, Go, Fallback;
            public int Role, Bots;
            public string Map;
        }

        static BotCombatStandEditor()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredEditMode) Restore(); };
        }

        [MenuItem("Tools/VR Battlegrounds/Bots/Stand/Open Scene")]
        private static void OpenScene() => EditorSceneManager.OpenScene(BotCombatStandBuilder.ScenePath);
        [MenuItem("Tools/VR Battlegrounds/Bots/Stand/Run Smoke")]
        private static void MenuSmoke() { Prepare(new BotStandRunOptions { Capture = true }, true); EditorApplication.isPlaying = true; }
        [MenuItem("Tools/VR Battlegrounds/Bots/Stand/Run Tactical Suite")]
        private static void MenuAll() { Prepare(new BotStandRunOptions { Capture=true }, false); EditorApplication.isPlaying=true; }
        [MenuItem("Tools/VR Battlegrounds/Bots/Stand/Run Capability Cover")]
        private static void MenuCover() { Prepare(new BotStandRunOptions { Capture=true, Profile=BotStandProfile.Capability, Cases=new[]{BotStandScenarioId.T03, BotStandScenarioId.T04, BotStandScenarioId.T05} }, false); EditorApplication.isPlaying=true; }
        [MenuItem("Tools/VR Battlegrounds/Bots/Stand/Stop Own Run")]
        public static void StopOwnRun()
        {
            if(!SessionState.GetBool(Key+"owned",false)) throw new InvalidOperationException("Редакторный запуск не принадлежит стенду.");
            BotCombatStand.Cancel(SessionState.GetString(Key+"runId",""));
            EditorApplication.isPlaying=false;
        }

        public static string Prepare(BotStandRunOptions options, bool smoke = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SessionState.GetBool(Key + "owned", false))
                throw new InvalidOperationException("Редактор или запуск уже занят.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BotCombatStandBuilder.ScenePath) == null) throw new InvalidOperationException("Сначала Build Scene.");
            var offline = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Offline.unity");
            if (!VrBattlegrounds.Editor.PlayModeStartFromOffline.TrySetTemporaryStartScene(offline, Owner))
                throw new InvalidOperationException("Стартовой сценой владеет другой запрос.");
            var prefs = new Prefs { Focus = UxrManager.EditorFocusPauseEnabled, Enabled = DebugBootstrapSettings.Enabled,
                Go = DebugBootstrapSettings.AutoGoLive, Fallback = DebugBootstrapSettings.AutoStartFallbackRole,
                Role = (int)DebugBootstrapSettings.FallbackRole, Bots = DebugBootstrapSettings.BotCount, Map = DebugBootstrapSettings.AutoLoadMapScene };
            SessionState.SetString(Key + "prefs", JsonConvert.SerializeObject(prefs));
            SessionState.SetString(Key + "scenes", JsonConvert.SerializeObject(EditorBuildSettings.scenes.Select(s => new SceneEntry { Path = s.path, Enabled = s.enabled }).ToArray()));
            SessionState.SetString(Key + "options", JsonConvert.SerializeObject(options));
            SessionState.SetBool(Key + "smoke", smoke); SessionState.SetBool(Key + "owned", true);
            SessionState.SetString(Key + "stage", "WaitingServer"); SessionState.SetString(Key + "runId", ""); SessionState.SetString(Key + "error", "");
            try
            {
            _deadline = EditorApplication.timeSinceStartup + 90;
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Where(s => s.path != BotCombatStandBuilder.ScenePath)
                .Concat(new[] { new EditorBuildSettingsScene(BotCombatStandBuilder.ScenePath, true) }).ToArray();
            UxrManager.EditorFocusPauseEnabled = false; DebugBootstrapSettings.Enabled = true;
            DebugBootstrapSettings.AutoGoLive = false; DebugBootstrapSettings.AutoStartFallbackRole = true;
            DebugBootstrapSettings.FallbackRole = GameNetworkDiscovery.AppRole.Server; DebugBootstrapSettings.BotCount = 0;
            DebugBootstrapSettings.AutoLoadMapScene = "";
            return "Подготовлен отдельный сервер стенда.";
            }
            catch { Restore(); throw; }
        }

        public static Dictionary<string, object> Status()
        {
            string id = SessionState.GetString(Key + "runId", "");
            var status = BotCombatStand.GetStatus(id);
            return new Dictionary<string, object> { { "owned", SessionState.GetBool(Key + "owned", false) },
                { "stage", SessionState.GetString(Key + "stage", "") }, { "runId", id }, { "error", SessionState.GetString(Key + "error", "") },
                { "finished", status != null && status.IsFinished }, { "passed", status?.Passed }, { "completed", status?.Completed },
                { "invalidFixture", status?.InvalidFixture }, { "reportPath", status?.ReportPath } };
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(Key + "owned", false) || !EditorApplication.isPlaying || NetworkServer.isLoadingScene) return;
            string stage = SessionState.GetString(Key + "stage", "");
            if (stage == "Running" || stage == "Failed") return;
            if (_deadline == 0) _deadline = EditorApplication.timeSinceStartup + 90;
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("Запуск стенда не завершился за 90 с.");
                if (!NetworkServer.active || SessionManager.Instance == null) return;
                if (stage == "WaitingServer")
                {
                    var original = SessionManager.Instance.MapRegistry;
                    if (original == null) throw new InvalidOperationException("Нет реестра карт.");
                    _originalRegistry = original; _registry = UnityEngine.Object.Instantiate(original);
                    _registry.name = "BotCombatStand runtime registry"; _registry.hideFlags = HideFlags.DontSave;
                    SessionState.SetString(Key + "registry", AssetDatabase.GetAssetPath(original));
                    MapData map = AssetDatabase.LoadAssetAtPath<MapData>("Assets/Scenes/Debug/BotCombatStandMap.asset");
                    _registry.maps = original.maps.Concat(new[] { map }).ToArray();
                    // Конфигурация только живого компонента: исходный asset не изменяется.
                    var so = new SerializedObject(SessionManager.Instance);
                    so.FindProperty("_mapRegistry").objectReferenceValue = _registry; so.ApplyModifiedPropertiesWithoutUndo();
                    SessionManager.Instance.SetSeries("respawn", new[] { "BotCombatStand" });
                    SessionState.SetString(Key + "stage", "LoadingStand");
                    MapLoader.Instance.LoadMap("BotCombatStand");
                    return;
                }
                if (BotCombatStand.Instance == null || MapReferee.Instance == null) return;
                if (!MapReferee.Instance.IsLiveOrPaused) { MapReferee.Instance.GoLive(); return; }
                var options = JsonConvert.DeserializeObject<BotStandRunOptions>(SessionState.GetString(Key + "options", "{}"));
                var handle = options.Cases != null && options.Cases.Length>0 ? BotCombatStand.RunCases(options,options.Cases) : SessionState.GetBool(Key + "smoke", true)
                    ? BotCombatStand.RunCases(options, BotStandScenarioId.T01, BotStandScenarioId.T03, BotStandScenarioId.T09, BotStandScenarioId.T10)
                    : BotCombatStand.RunAll(options);
                SessionState.SetString(Key + "runId", handle.RunId); SessionState.SetString(Key + "stage", "Running");
            }
            catch (Exception e) { SessionState.SetString(Key + "error", e.ToString()); SessionState.SetString(Key + "stage", "Failed"); GameLog.Debug.Error("[BotCombatStand] " + e.Message); }
        }

        public static string Restore()
        {
            if (!SessionState.GetBool(Key + "owned", false)) return "Нет временного запуска.";
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Сначала остановите собственный Play через Stop Own Run.");
            if (SessionManager.Instance != null)
            {
                var original = _originalRegistry != null ? _originalRegistry : AssetDatabase.LoadAssetAtPath<MapRegistry>(SessionState.GetString(Key + "registry", ""));
                if (original != null)
                {
                    var so = new SerializedObject(SessionManager.Instance); so.FindProperty("_mapRegistry").objectReferenceValue = original; so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            if (_registry != null) UnityEngine.Object.DestroyImmediate(_registry);
            _registry = null; _originalRegistry = null;
            var prefs = JsonConvert.DeserializeObject<Prefs>(SessionState.GetString(Key + "prefs", "{}"));
            UxrManager.EditorFocusPauseEnabled = prefs.Focus; DebugBootstrapSettings.Enabled = prefs.Enabled;
            DebugBootstrapSettings.AutoGoLive = prefs.Go; DebugBootstrapSettings.AutoStartFallbackRole = prefs.Fallback;
            DebugBootstrapSettings.FallbackRole = (GameNetworkDiscovery.AppRole)prefs.Role; DebugBootstrapSettings.BotCount = prefs.Bots;
            DebugBootstrapSettings.AutoLoadMapScene = prefs.Map;
            var oldScenes=JsonConvert.DeserializeObject<SceneEntry[]>(SessionState.GetString(Key + "scenes", "[]"));
            var current=EditorBuildSettings.scenes.Where(s=>s.path!=BotCombatStandBuilder.ScenePath).ToList();
            int oldIndex=Array.FindIndex(oldScenes,s=>s.Path==BotCombatStandBuilder.ScenePath);
            if(oldIndex>=0) current.Insert(Math.Min(oldIndex,current.Count),new EditorBuildSettingsScene(oldScenes[oldIndex].Path,oldScenes[oldIndex].Enabled));
            EditorBuildSettings.scenes=current.ToArray();
            VrBattlegrounds.Editor.PlayModeStartFromOffline.ClearTemporaryStartScene(Owner);
            SessionState.SetBool(Key + "owned", false); _deadline = 0;
            return "Настройки, реестр и стартовая сцена восстановлены.";
        }
    }
}
