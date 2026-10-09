using System;
using System.Collections.Generic;
using Mirror;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.DevTools.BotCombatStand;
using VrBattlegrounds.Managers;
using VrBattlegrounds.EditorTools.TestStand;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Стенд ботов — потребитель общего Play Launch; профиль/build list/registry не меняет.</summary>
    [InitializeOnLoad]
    public static class BotCombatStandEditor
    {
        private const string Key = "BotCombatStand.";
        private const string Owner = "BotCombatStand";
        private static double _deadline;
        static BotCombatStandEditor()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredEditMode) Restore(); };
        }
        [MenuItem("Tools/VR Battlegrounds/Bots/Stand/Open Scene")]
        private static void OpenScene() => EditorSceneManager.OpenScene(BotCombatStandBuilder.ScenePath);
        [MenuItem("Tools/VR Battlegrounds/Bots/Stand/Run Smoke")]
        private static void MenuSmoke() => Prepare(new BotStandRunOptions { Capture = true }, true);
        [MenuItem("Tools/VR Battlegrounds/Bots/Stand/Run Tactical Suite")]
        private static void MenuAll() => Prepare(new BotStandRunOptions { Capture = true }, false);
        [MenuItem("Tools/VR Battlegrounds/Bots/Stand/Run Capability Cover")]
        private static void MenuCover() => Prepare(new BotStandRunOptions { Capture = true, Profile = BotStandProfile.Capability,
            Cases = new[] { BotStandScenarioId.T03, BotStandScenarioId.T04, BotStandScenarioId.T05 } }, false);

        public static string Prepare(BotStandRunOptions options, bool smoke = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SessionState.GetBool(Key + "owned", false))
                throw new InvalidOperationException("Редактор или запуск уже занят.");
            var configuration = new PlayLaunchConfiguration {
                Role = "server", SceneSource = "scene", ScenePath = BotCombatStandBuilder.ScenePath,
                ModeId = "respawn", AutoGoLive = false, BotCount = 0, ClientCount = 0,
                PauseOnFocusLoss = false, HostIsAdmin = false, Readiness = "map-playable" };
            var plan = JsonUtility.FromJson<ResolvedPlayPlan>(PlayLaunch.Plan(JsonUtility.ToJson(configuration)));
            if (!plan.Passed) throw new PlayLaunchException(plan.Code, plan.Message);
            SessionState.SetString(Key + "options", JsonConvert.SerializeObject(options ?? new BotStandRunOptions()));
            SessionState.SetBool(Key + "smoke", smoke);
            SessionState.SetString(Key + "runId", "");
            SessionState.SetString(Key + "error", "");
            SessionState.SetString(Key + "stage", "WaitingLaunch");
            var launch = JsonUtility.FromJson<PlayLaunchStatus>(PlayLaunch.Play(JsonUtility.ToJson(configuration), Owner));
            SessionState.SetString(Key + "launchId", launch.RunId);
            SessionState.SetBool(Key + "owned", true);
            _deadline = EditorApplication.timeSinceStartup + 90;
            return "Общий Play Launch запускает отдельный сервер стенда.";
        }

        public static Dictionary<string, object> Status()
        {
            string id = SessionState.GetString(Key + "runId", "");
            var result = BotCombatStand.GetStatus(id);
            return new Dictionary<string, object> {
                { "owned", SessionState.GetBool(Key + "owned", false) }, { "stage", SessionState.GetString(Key + "stage", "") },
                { "runId", id }, { "launchId", SessionState.GetString(Key + "launchId", "") }, { "error", SessionState.GetString(Key + "error", "") },
                { "finished", result != null && result.IsFinished }, { "passed", result?.Passed }, { "completed", result?.Completed },
                { "invalidFixture", result?.InvalidFixture }, { "reportPath", result?.ReportPath } };
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(Key + "owned", false) || !EditorApplication.isPlaying || NetworkServer.isLoadingScene) return;
            string stage = SessionState.GetString(Key + "stage", "");
            if (stage == "Running" || stage == "Failed") return;
            try
            {
                if (_deadline == 0) _deadline = EditorApplication.timeSinceStartup + 90;
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("Стенд не достиг готовности.");
                var launch = JsonUtility.FromJson<PlayLaunchStatus>(PlayLaunch.Status());
                if (launch.State == "Failed") throw new InvalidOperationException(launch.Error);
                if (!launch.Ready || BotCombatStand.Instance == null || MapReferee.Instance == null) return;
                if (!MapReferee.Instance.IsLiveOrPaused) { MapReferee.Instance.GoLive(); return; }
                var options = JsonConvert.DeserializeObject<BotStandRunOptions>(SessionState.GetString(Key + "options", "{}"));
                var handle = options.Cases != null && options.Cases.Length > 0 ? BotCombatStand.RunCases(options, options.Cases) :
                    SessionState.GetBool(Key + "smoke", true) ? BotCombatStand.RunCases(options, BotStandScenarioId.T01, BotStandScenarioId.T03,
                        BotStandScenarioId.T09, BotStandScenarioId.T10) : BotCombatStand.RunAll(options);
                SessionState.SetString(Key + "runId", handle.RunId); SessionState.SetString(Key + "stage", "Running");
            }
            catch (Exception e)
            {
                SessionState.SetString(Key + "error", e.Message); SessionState.SetString(Key + "stage", "Failed");
                GameLog.Debug.Error("[BotCombatStand] " + e.Message);
                PlayLaunch.Cancel(SessionState.GetString(Key + "launchId", ""), Owner);
            }
        }

        [MenuItem("Tools/VR Battlegrounds/Bots/Stand/Stop Own Run")]
        public static void StopOwnRun()
        {
            if (!SessionState.GetBool(Key + "owned", false)) throw new InvalidOperationException("Запуск не принадлежит стенду.");
            BotCombatStand.Cancel(SessionState.GetString(Key + "runId", ""));
            PlayLaunch.Cancel(SessionState.GetString(Key + "launchId", ""), Owner);
        }
        public static string Restore()
        {
            if (!SessionState.GetBool(Key + "owned", false)) return "Нет своего запуска.";
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Сначала Stop Own Run.");
            SessionState.SetBool(Key + "owned", false); _deadline = 0;
            return "Общий запуск завершён; профиль, список сцен и registry не изменялись.";
        }
    }
}
