using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.EditorTools.TestStand;

namespace VrBattlegrounds.Editor
{
    /// <summary>Окно и MCP используют один Plan/Play/Cancel; профиль сохраняется только явно.</summary>
    /// <remarks>
    /// Plan (разметка сцены, поиск NetworkIdentity в preview-сценах, hash) и Status (чтение манифеста стенда) не бесплатны,
    /// а OnGUI вызывается на каждое событие: окно кэширует их и перерисовывается только при изменении показанного.
    /// План пересчитывается при правке черновика, по событиям сцен/проекта/Prefab Mode и не реже
    /// <see cref="PlanRefreshSeconds"/> вне Play (preview-сцены LoadPrefabContents событий не дают).
    /// </remarks>
    public sealed class PlayLaunchWindow : EditorWindow
    {
        private const double PlanRefreshSeconds = 2.0;
        private static readonly string[] SceneSources = { "active", "lobby", "scene" };
        private static readonly string[] MainRoles = { "host", "server", "client", "ask" };
        private static readonly string[] PlayerRoles = { "client", "host", "server" };
        private static readonly string[] PortPolicies = { "default", "fixed", "auto" };
        private PlayLaunchConfiguration _draft;
        private string _error;
        private string _profilePath;
        private PlayLaunchStatus _status;
        private bool _statusStale = true;
        private bool _requestActive;
        private bool _playing;
        private ResolvedPlayPlan _plan;
        private string _planJson;
        private bool _planStale = true;
        private double _planTime;
        [MenuItem("Tools/VR Battlegrounds/Debug/Play Launch")]
        public static void Open() => GetWindow<PlayLaunchWindow>("Play Launch");
        private void OnEnable()
        {
            try { _draft = PlayLaunchSettings.ReadProfile(); } catch (System.Exception e) { _error = e.Message; }
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            PrefabStage.prefabStageOpened += OnPrefabStage;
            PrefabStage.prefabStageClosing += OnPrefabStage;
            EditorBuildSettings.sceneListChanged += InvalidatePlan;
            EditorApplication.projectChanged += InvalidatePlan;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }
        private void OnDisable()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            PrefabStage.prefabStageOpened -= OnPrefabStage;
            PrefabStage.prefabStageClosing -= OnPrefabStage;
            EditorBuildSettings.sceneListChanged -= InvalidatePlan;
            EditorApplication.projectChanged -= InvalidatePlan;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }
        private void OnActiveSceneChanged(Scene previous, Scene next) => InvalidatePlan();
        private void OnSceneOpened(Scene scene, OpenSceneMode mode) => InvalidatePlan();
        private void OnSceneClosed(Scene scene) => InvalidatePlan();
        private void OnPrefabStage(PrefabStage stage) => InvalidatePlan();
        private void OnPlayModeChanged(PlayModeStateChange change) { _statusStale = true; InvalidatePlan(); }
        private void InvalidatePlan() { _planStale = true; Repaint(); }
        private void OnInspectorUpdate()
        {
            if (RefreshStatus()) Repaint();
            // Вне Play и чужого запроса план мог устареть без события (LoadPrefabContents, правка зависимостей сцены).
            // Пересчёт идёт в OnGUI: скрытая вкладка окна план не считает.
            if (!_planStale && !_playing && !_requestActive && EditorApplication.timeSinceStartup - _planTime >= PlanRefreshSeconds) InvalidatePlan();
        }
        /// <summary>true — изменилось то, что окно показывает или от чего зависит доступность кнопок.</summary>
        private bool RefreshStatus()
        {
            var status = JsonUtility.FromJson<PlayLaunchStatus>(PlayLaunch.Status());
            bool requestActive = PlayLaunchSettings.RequestActive, playing = EditorApplication.isPlayingOrWillChangePlaymode;
            bool changed = _status == null || status.State != _status.State || status.Owner != _status.Owner || status.Error != _status.Error ||
                status.RunId != _status.RunId || requestActive != _requestActive || playing != _playing;
            _status = status; _requestActive = requestActive; _playing = playing; _statusStale = false;
            return changed;
        }
        private void ComputePlan(string json)
        {
            _plan = JsonUtility.FromJson<ResolvedPlayPlan>(PlayLaunch.Plan(json));
            _planJson = json; _planStale = false; _planTime = EditorApplication.timeSinceStartup;
        }
        private void OnGUI()
        {
            if (_draft == null)
            {
                EditorGUILayout.HelpBox(_error ?? "Профиль не прочитан.", MessageType.Error);
                if (GUILayout.Button("Новый профиль по умолчанию")) { _draft = new PlayLaunchConfiguration(); _error = null; }
                return;
            }
            // Layout и Repaint одного кадра должны видеть одно состояние: статус обновляем только перед Layout.
            if (_status == null || (_statusStale && Event.current.type == EventType.Layout)) RefreshStatus();
            var status = _status;
            EditorGUILayout.LabelField("Состояние", status.State ?? "Idle");
            if (!string.IsNullOrEmpty(status.Owner)) EditorGUILayout.LabelField("Владелец", status.Owner);
            if (!string.IsNullOrEmpty(status.Error)) EditorGUILayout.HelpBox(status.Error, MessageType.Error);
            using (new EditorGUI.DisabledScope(_requestActive || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUILayout.LabelField("Профиль этого checkout", EditorStyles.boldLabel);
                _draft.SceneSource = Choice("Сцена", _draft.SceneSource, SceneSources);
                if (_draft.SceneSource == "scene") _draft.ScenePath = EditorGUILayout.TextField("Путь Assets/...unity", _draft.ScenePath);
                _draft.Role = Choice("Главный процесс", _draft.Role, MainRoles);
                _draft.ClientCount = EditorGUILayout.IntField("Дополнительных клиентов", _draft.ClientCount);
                if (_draft.ClientCount >= 0 && _draft.ClientCount <= 16)
                {
                    if (_draft.AdditionalPlayerRoles == null || _draft.AdditionalPlayerRoles.Length != _draft.ClientCount)
                    {
                        var previous = _draft.AdditionalPlayerRoles; _draft.AdditionalPlayerRoles = new string[_draft.ClientCount];
                        for (int i = 0; i < _draft.ClientCount; i++) _draft.AdditionalPlayerRoles[i] = previous != null && i < previous.Length ? previous[i] : "client";
                    }
                    for (int i = 0; i < _draft.ClientCount; i++) _draft.AdditionalPlayerRoles[i] = Choice("Player " + (i + 2), _draft.AdditionalPlayerRoles[i], PlayerRoles);
                }
                _draft.ClientAddress = EditorGUILayout.TextField("Адрес сервера", _draft.ClientAddress);
                _draft.NetworkPortPolicy = Choice("Сетевые порты", _draft.NetworkPortPolicy, PortPolicies);
                if (_draft.NetworkPortPolicy == "fixed")
                {
                    _draft.NetworkPort = EditorGUILayout.IntField("Игровой порт", _draft.NetworkPort);
                    _draft.DiscoveryPort = EditorGUILayout.IntField("Discovery порт", _draft.DiscoveryPort);
                }
                EditorGUILayout.LabelField("Файл профиля", _profilePath ??= PlayLaunchSettings.ProfilePath);
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
                // В Repaint не пересчитываем: высота подсказки должна совпасть с Layout этого кадра.
                if (_plan == null || ((_planStale || json != _planJson) && Event.current.type != EventType.Repaint)) ComputePlan(json);
                var plan = _plan;
                EditorGUILayout.HelpBox(plan.Passed ? plan.SceneKind + ": " + plan.StartScenePath + " → " + plan.TargetScenePath : plan.Code + ": " + plan.Message,
                    plan.Passed ? MessageType.Info : MessageType.Warning);
                if (GUILayout.Button("Сохранить профиль")) Run(() => PlayLaunchSettings.SaveProfile(_draft));
                using (new EditorGUI.DisabledScope(!plan.Passed))
                {
                    // План на момент нажатия, как прежде: кэш мог отстать от сцен/ассетов на PlanRefreshSeconds.
                    if (GUILayout.Button("Play один раз с этим конфигом")) Run(() => { ComputePlan(json); Start(json, _plan.PlanHash); });
                    if (GUILayout.Button("Play по сохранённому профилю")) Run(() => Start(null, null));
                }
                if (GUILayout.Button("Импортировать прежние EditorPrefs")) Run(() => { PlayLaunchSettings.ImportLegacyProfile(); _draft = PlayLaunchSettings.ReadProfile(); });
            }
            if (status.Owner == "window" && GUILayout.Button("Остановить свой запуск")) Run(() => PlayLaunch.Cancel(status.RunId, "window"));
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
        }
        private static string Choice(string label, string value, string[] choices)
        { int selected = System.Array.IndexOf(choices, value); return choices[EditorGUILayout.Popup(label, Mathf.Max(0, selected), choices)]; }
        private void Run(System.Action action)
        {
            try { action(); _error = null; } catch (System.Exception e) { _error = e.Message; }
            _statusStale = true; _planStale = true; Repaint();
        }
        private static void Start(string json, string hash)
        {
            string reply = PlayLaunch.Play(json, "window", hash);
            var status = JsonUtility.FromJson<PlayLaunchStatus>(reply);
            if (string.IsNullOrEmpty(status.State)) { var failed = JsonUtility.FromJson<ResolvedPlayPlan>(reply); throw new PlayLaunchException(failed.Code, failed.Message); }
        }
    }
}
