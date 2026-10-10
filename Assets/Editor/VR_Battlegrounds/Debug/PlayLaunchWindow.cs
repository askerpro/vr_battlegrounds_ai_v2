using UnityEditor;
using UnityEngine;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.EditorTools.TestStand;

namespace VrBattlegrounds.Editor
{
    /// <summary>Окно и MCP используют один Plan/Play/Cancel; профиль сохраняется только явно.</summary>
    public sealed class PlayLaunchWindow : EditorWindow
    {
        private PlayLaunchConfiguration _draft;
        private string _error;
        [MenuItem("Tools/VR Battlegrounds/Debug/Play Launch")]
        public static void Open() => GetWindow<PlayLaunchWindow>("Play Launch");
        private void OnEnable() { try { _draft = PlayLaunchSettings.ReadProfile(); } catch (System.Exception e) { _error = e.Message; } }
        private void OnInspectorUpdate() => Repaint();
        private void OnGUI()
        {
            if (_draft == null)
            {
                EditorGUILayout.HelpBox(_error ?? "Профиль не прочитан.", MessageType.Error);
                if (GUILayout.Button("Новый профиль по умолчанию")) { _draft = new PlayLaunchConfiguration(); _error = null; }
                return;
            }
            var status = JsonUtility.FromJson<PlayLaunchStatus>(PlayLaunch.Status());
            EditorGUILayout.LabelField("Состояние", status.State ?? "Idle");
            if (!string.IsNullOrEmpty(status.Owner)) EditorGUILayout.LabelField("Владелец", status.Owner);
            if (!string.IsNullOrEmpty(status.Error)) EditorGUILayout.HelpBox(status.Error, MessageType.Error);
            using (new EditorGUI.DisabledScope(PlayLaunchSettings.RequestActive || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUILayout.LabelField("Профиль этого checkout", EditorStyles.boldLabel);
                _draft.SceneSource = Choice("Сцена", _draft.SceneSource, new[] { "active", "lobby", "scene" });
                if (_draft.SceneSource == "scene") _draft.ScenePath = EditorGUILayout.TextField("Путь Assets/...unity", _draft.ScenePath);
                _draft.Role = Choice("Главный процесс", _draft.Role, new[] { "host", "server", "client", "ask" });
                _draft.ClientCount = EditorGUILayout.IntField("Дополнительных клиентов", _draft.ClientCount);
                if (_draft.ClientCount >= 0 && _draft.ClientCount <= 16)
                {
                    if (_draft.AdditionalPlayerRoles == null || _draft.AdditionalPlayerRoles.Length != _draft.ClientCount)
                    {
                        var previous = _draft.AdditionalPlayerRoles; _draft.AdditionalPlayerRoles = new string[_draft.ClientCount];
                        for (int i = 0; i < _draft.ClientCount; i++) _draft.AdditionalPlayerRoles[i] = previous != null && i < previous.Length ? previous[i] : "client";
                    }
                    for (int i = 0; i < _draft.ClientCount; i++) _draft.AdditionalPlayerRoles[i] = Choice("Player " + (i + 2), _draft.AdditionalPlayerRoles[i], new[] { "client", "host", "server" });
                }
                _draft.ClientAddress = EditorGUILayout.TextField("Адрес сервера", _draft.ClientAddress);
                _draft.NetworkPortPolicy = Choice("Сетевые порты", _draft.NetworkPortPolicy, new[] { "default", "fixed", "auto" });
                if (_draft.NetworkPortPolicy == "fixed")
                {
                    _draft.NetworkPort = EditorGUILayout.IntField("Игровой порт", _draft.NetworkPort);
                    _draft.DiscoveryPort = EditorGUILayout.IntField("Discovery порт", _draft.DiscoveryPort);
                }
                EditorGUILayout.LabelField("Файл профиля", PlayLaunchSettings.ProfilePath);
                _draft.HostIsAdmin = EditorGUILayout.Toggle("Host — админ", _draft.HostIsAdmin);
                _draft.ModeId = EditorGUILayout.TextField("modeId (пусто — без серии)", _draft.ModeId);
                _draft.AutoGoLive = EditorGUILayout.Toggle("Автостарт серии", _draft.AutoGoLive);
                _draft.MinPlayers = EditorGUILayout.IntField("Минимум игроков", _draft.MinPlayers);
                _draft.BotCount = EditorGUILayout.IntField("Ботов", _draft.BotCount);
                _draft.PauseOnFocusLoss = EditorGUILayout.Toggle("Пауза XR без фокуса", _draft.PauseOnFocusLoss);
                _draft.AllowUnmarked = EditorGUILayout.Toggle("Разрешить Unmarked", _draft.AllowUnmarked);
                _draft.ScreenshotOnButtonB = EditorGUILayout.Toggle("Скриншот по B", _draft.ScreenshotOnButtonB);
                _draft.TimeoutSeconds = EditorGUILayout.IntField("Таймаут, секунд", _draft.TimeoutSeconds);
                string json = JsonUtility.ToJson(_draft);
                var plan = JsonUtility.FromJson<ResolvedPlayPlan>(PlayLaunch.Plan(json));
                EditorGUILayout.HelpBox(plan.Passed ? plan.SceneKind + ": " + plan.StartScenePath + " → " + plan.TargetScenePath : plan.Code + ": " + plan.Message,
                    plan.Passed ? MessageType.Info : MessageType.Warning);
                if (GUILayout.Button("Сохранить профиль")) Run(() => PlayLaunchSettings.SaveProfile(_draft));
                using (new EditorGUI.DisabledScope(!plan.Passed))
                {
                    if (GUILayout.Button("Play один раз с этим конфигом")) Run(() => Start(json, plan.PlanHash));
                    if (GUILayout.Button("Play по сохранённому профилю")) Run(() => Start(null, null));
                }
                if (GUILayout.Button("Импортировать прежние EditorPrefs")) Run(() => { PlayLaunchSettings.ImportLegacyProfile(); _draft = PlayLaunchSettings.ReadProfile(); });
            }
            if (status.Owner == "window" && GUILayout.Button("Остановить свой запуск")) Run(() => PlayLaunch.Cancel(status.RunId, "window"));
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
        }
        private static string Choice(string label, string value, string[] choices)
        { int selected = System.Array.IndexOf(choices, value); return choices[EditorGUILayout.Popup(label, Mathf.Max(0, selected), choices)]; }
        private void Run(System.Action action) { try { action(); _error = null; } catch (System.Exception e) { _error = e.Message; } }
        private static void Start(string json, string hash)
        {
            string reply = PlayLaunch.Play(json, "window", hash);
            var status = JsonUtility.FromJson<PlayLaunchStatus>(reply);
            if (string.IsNullOrEmpty(status.State)) { var failed = JsonUtility.FromJson<ResolvedPlayPlan>(reply); throw new PlayLaunchException(failed.Code, failed.Message); }
        }
    }
}
