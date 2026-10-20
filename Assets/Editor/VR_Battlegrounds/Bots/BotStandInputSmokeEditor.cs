using System;
using System.IO;
using Mirror;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.DevTools.BotCombatStand;
using VrBattlegrounds.EditorTools.TestStand;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    /// Демо bot-input итерации 1 (tasks/bots-input/contracts/bot-input-design.md): сервер стенда ботов через общий Play
    /// Launch, затем <see cref="BotStandInputSmoke"/> — бот на вводе устройства идёт головой, берёт пистолет из кобуры по
    /// Grip и стреляет курком. Смотреть в Scene view (у выделенного сервера нет локального аватара). Профиль, build list и
    /// registry не меняются.
    /// </summary>
    [InitializeOnLoad]
    public static class BotStandInputSmokeEditor
    {
        private const string Key = "BotStandInputSmoke.";
        private const string Owner = "BotStandInputSmoke";
        private static double _deadline;

        static BotStandInputSmokeEditor()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key + "owned", false);
            };
        }

        [MenuItem("Tools/VR Battlegrounds/Bots/Stand/Run Input Smoke (demo)")]
        private static void Menu() => GameLog.Debug.Info("[BotStandInputSmoke] " + Prepare());

        /// <summary>Запускает сервер стенда и демо. Отчёт — <c>tasks/bots-input/reports/input-smoke-*/summary.json</c>.</summary>
        public static string Prepare(string outputDirectory = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SessionState.GetBool(Key + "owned", false))
                throw new InvalidOperationException("Редактор или запуск уже занят.");
            var configuration = new PlayLaunchConfiguration {
                Role = "server", SceneSource = "scene", ScenePath = BotCombatStandBuilder.ScenePath,
                ModeId = "respawn", AutoGoLive = false, BotCount = 0, ClientCount = 0,
                PauseOnFocusLoss = false, HostIsAdmin = false, Readiness = "map-playable" };
            var plan = JsonUtility.FromJson<ResolvedPlayPlan>(PlayLaunch.Plan(JsonUtility.ToJson(configuration)));
            if (!plan.Passed) throw new PlayLaunchException(plan.Code, plan.Message);

            string output = string.IsNullOrEmpty(outputDirectory)
                ? Path.GetFullPath("tasks/bots-input/reports/input-smoke-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"))
                : Path.GetFullPath(outputDirectory);
            SessionState.SetString(Key + "output", output);
            SessionState.SetString(Key + "stage", "WaitingLaunch");
            SessionState.SetString(Key + "error", "");
            var launch = JsonUtility.FromJson<PlayLaunchStatus>(PlayLaunch.Play(JsonUtility.ToJson(configuration), Owner));
            SessionState.SetString(Key + "launchId", launch.RunId);
            SessionState.SetBool(Key + "owned", true);
            _deadline = EditorApplication.timeSinceStartup + 120;
            return "Общий Play Launch запускает сервер стенда; демо Input Smoke стартует после готовности. Отчёт: " + output;
        }

        /// <summary>Состояние запуска и демо (для агента через MCP).</summary>
        public static string Status()
        {
            BotStandInputSmoke run = BotStandInputSmoke.Current;
            return JsonUtility.ToJson(new StatusView
            {
                owned = SessionState.GetBool(Key + "owned", false),
                launchStage = SessionState.GetString(Key + "stage", ""),
                error = SessionState.GetString(Key + "error", ""),
                output = SessionState.GetString(Key + "output", ""),
                stage = run != null ? run.Stage : "",
                finished = run != null && run.Finished,
                passed = run != null && run.Passed,
                runError = run != null ? run.Error : "",
                journal = run != null ? string.Join("\n", run.Journal) : ""
            });
        }

        [MenuItem("Tools/VR Battlegrounds/Bots/Stand/Stop Input Smoke")]
        public static void Stop()
        {
            if (!SessionState.GetBool(Key + "owned", false)) throw new InvalidOperationException("Запуск не принадлежит демо.");
            PlayLaunch.Cancel(SessionState.GetString(Key + "launchId", ""), Owner);
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(Key + "owned", false) || !EditorApplication.isPlaying || NetworkServer.isLoadingScene) return;
            if (SessionState.GetString(Key + "stage", "") != "WaitingLaunch") return;
            try
            {
                if (_deadline == 0) _deadline = EditorApplication.timeSinceStartup + 120;
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("Стенд не достиг готовности.");
                var launch = JsonUtility.FromJson<PlayLaunchStatus>(PlayLaunch.Status());
                if (launch.State == "Failed") throw new InvalidOperationException(launch.Error);
                if (!launch.Ready || BotCombatStand.Instance == null || MapReferee.Instance == null) return;
                if (!MapReferee.Instance.IsLiveOrPaused) { MapReferee.Instance.GoLive(); return; }

                string refusal = BotStandInputSmoke.Begin(SessionState.GetString(Key + "output", ""));
                if (refusal != null) throw new InvalidOperationException(refusal);
                SessionState.SetString(Key + "stage", "Running");
            }
            catch (Exception e)
            {
                SessionState.SetString(Key + "error", e.Message);
                SessionState.SetString(Key + "stage", "Failed");
                GameLog.Debug.Error("[BotStandInputSmoke] " + e.Message);
                PlayLaunch.Cancel(SessionState.GetString(Key + "launchId", ""), Owner);
            }
        }

        [Serializable]
        private struct StatusView
        {
            public bool owned;
            public string launchStage;
            public string error;
            public string output;
            public string stage;
            public bool finished;
            public bool passed;
            public string runError;
            public string journal;
        }
    }
}
