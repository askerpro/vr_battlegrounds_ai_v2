using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.Core;
using VrBattlegrounds.EditorTools.TestStand;

namespace VrBattlegrounds.Editor
{
    /// <summary>
    /// Автоматически стартует игру с Offline-сцены, независимо от того какая сцена сейчас открыта.
    /// Перед стартом проверяет текущую сцену: если это карта (отличная от Offline),
    /// она прописывается в DebugBootstrapConfig, чтобы запуститься сразу после инициализации менеджеров.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeStartFromOffline
    {
        private const string OfflineScenePath = "Assets/Scenes/Offline.unity";
        private const string PrefKey = "VrBattlegrounds.StartFromOffline";
        private const string TemporarySceneKey = "VrBattlegrounds.TemporaryPlayScene";
        private const string TemporaryOwnerKey = "VrBattlegrounds.TemporaryPlaySceneOwner";
        private const string PreviousSceneKey = "VrBattlegrounds.PreviousPlayScene";

        /// <summary>Единственный writer стартовой сцены принимает временный запрос диагностического стенда.</summary>
        public static bool TrySetTemporaryStartScene(SceneAsset scene, string owner)
        {
            if (VrBattlegrounds.DevTools.PlayLaunchSettings.RequestActive) return false;
            if (scene == null || string.IsNullOrWhiteSpace(owner) || EditorApplication.isPlayingOrWillChangePlaymode
                || !string.IsNullOrEmpty(SessionState.GetString(TemporaryOwnerKey, ""))) return false;
            SessionState.SetString(PreviousSceneKey, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetString(TemporaryOwnerKey, owner);
            SessionState.SetString(TemporarySceneKey, AssetDatabase.GetAssetPath(scene));
            UpdateState();
            return true;
        }

        /// <summary>
        /// Снимает только запрос своего владельца, возвращая прежнюю стартовую сцену. При возврате в Edit Mode запрос снимается
        /// сам (<see cref="OnPlayModeStateChanged"/>): меню стенда снимать его не обязано.
        /// </summary>
        public static bool ClearTemporaryStartScene(string owner)
        {
            if (SessionState.GetString(TemporaryOwnerKey, "") != owner) return false;
            string previous = SessionState.GetString(PreviousSceneKey, "");
            SessionState.EraseString(TemporaryOwnerKey); SessionState.EraseString(TemporarySceneKey);
            SessionState.EraseString(PreviousSceneKey);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
            return true;
        }

        private static SceneAsset TemporaryStartScene => AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(TemporarySceneKey, ""));

        // Добавляем пункт в менюшку для возможности выключения этого поведения
        [MenuItem("Tools/VR Battlegrounds/Debug/Start from Offline Scene")]
        public static void ToggleAction()
        {
            PlayLaunchWindow.Open();
        }

        [MenuItem("Tools/VR Battlegrounds/Debug/Start from Offline Scene", true)]
        public static bool ToggleActionValidate()
        {
            Menu.SetChecked("Tools/VR Battlegrounds/Debug/Start from Offline Scene", false);
            return true;
        }

        static PlayModeStartFromOffline()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += UpdateState;
        }

        /// <summary>Папка сцен стендов: Play в открытой сцене стенда стартует её саму, мимо штатного Offline → Lobby.</summary>
        private const string StandScenesFolder = "Assets/Scenes/Dev/";

        /// <summary>
        /// Открыта сцена стенда (<see cref="StandScenesFolder"/>). Штатный старт через Offline поднимает сеть и Lobby, и в стенд
        /// приходит локальный аватар игрока — стенды запускаются только сами, при любом значении «Start from Offline Scene».
        /// </summary>
        private static bool IsStandSceneOpen() => EditorSceneManager.GetActiveScene().path.StartsWith(StandScenesFolder);

        /// <summary>
        /// Открыта карта под запуском MapBootstrap (в сцене есть <see cref="VrBattlegrounds.Maps.Runtime.MapRoot"/>).
        /// Такой сцене нужен процессный корень (менеджеры, сеть, SDK): без Offline она не запустится вовсе.
        /// </summary>
        private static bool IsMapSceneOpen()
        {
            Scene active = EditorSceneManager.GetActiveScene();
            return active.IsValid() && active.path != OfflineScenePath && active.GetRootGameObjects()
                .Any(root => root.GetComponentInChildren<VrBattlegrounds.Maps.Runtime.MapRoot>(true) != null);
        }

        /// <summary>С какой сцены стартует Play.</summary>
        public enum StartKind { Temporary, ActiveScene, Offline }

        /// <summary>
        /// Единое правило старта Play. Временная сцена стенда — её владелец решил сам. Сцена стенда (Dev) — сама
        /// себя. Карта с MapRoot — всегда через Offline: её запускает MapBootstrap общим серверным путём, а процессный
        /// корень создаёт только Offline; личная галочка «Start from Offline Scene» карту не обходит. Прочие сцены —
        /// по галочке.
        /// </summary>
        public static StartKind ResolveStart(bool hasTemporary, bool standScene, bool mapScene, bool startFromOffline)
        {
            if (hasTemporary) return StartKind.Temporary;
            if (standScene) return StartKind.ActiveScene;
            if (mapScene || startFromOffline) return StartKind.Offline;
            return StartKind.ActiveScene;
        }

        private static StartKind CurrentStart() =>
            ResolveStart(TemporaryStartScene != null, IsStandSceneOpen() && PlayLaunchSettings.Effective.SceneSource == "active", IsMapSceneOpen(), PlayLaunchSettings.Effective.SceneSource != "active");

        private static void UpdateState()
        {
            if (VrBattlegrounds.DevTools.PlayLaunchSettings.RequestActive) return;
            SceneAsset temporary = TemporaryStartScene;
            StartKind start = CurrentStart();
            if (start == StartKind.Temporary)
            {
                EditorSceneManager.playModeStartScene = temporary;
                return;
            }

            if (start == StartKind.Offline)
            {
                var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(OfflineScenePath);
                if (sceneAsset != null)
                {
                    EditorSceneManager.playModeStartScene = sceneAsset;
                }
                else
                {
                    GameLog.Debug.Warning($"[PlayModeStartFromOffline] Сцена {OfflineScenePath} не найдена. Создайте или обновите путь.");
                }
            }
            else
            {
                EditorSceneManager.playModeStartScene = null;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Временная стартовая сцена стенда — на один Play. Снимает её этот класс, а не меню стенда: подписка меню на
            // возврат в Edit Mode стирается перезагрузкой домена при входе в Play, и запрос оставался висеть — следующий
            // стенд получал отказ «стартовую сцену уже занял другой стенд» до перезапуска редактора.
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                PlayLaunch.ReleaseNativePlay();
                string owner = SessionState.GetString(TemporaryOwnerKey, "");
                if (!string.IsNullOrEmpty(owner)) ClearTemporaryStartScene(owner);
                UpdateState();
                return;
            }

            if (state == PlayModeStateChange.ExitingEditMode)
            {
                if (!PlayLaunchSettings.RequestActive && TemporaryStartScene == null && PlayLaunchSettings.Effective.SceneSource != "active")
                {
                    try { PlayLaunch.PrepareNativePlay(); }
                    catch (System.Exception error)
                    {
                        GameLog.Debug.Error("[PlayLaunch] Toolbar Play отклонён: " + error.Message);
                        EditorApplication.isPlaying = false;
                    }
                    return;
                }
                // Принудительно обновляем начальную сцену перед самым стартом
                UpdateState();
                
                GameLog.Debug.Info($"[PlayModeStartFromOffline] Exiting Edit Mode. playModeStartScene is {(EditorSceneManager.playModeStartScene != null ? EditorSceneManager.playModeStartScene.name : "null")}");

                if (CurrentStart() != StartKind.Offline) return;

                Scene activeScene = EditorSceneManager.GetActiveScene();
                
                // Если мы уже в оффлайн-сцене, ничего специально делать не нужно
                if (activeScene.path == OfflineScenePath) return;

                // Карта автозапуска — в SessionState текущей сессии редактора (DebugBootstrapSettings), не в ассет:
                // раньше каждый Play из карты правил ассет под git.
                if (DebugBootstrapSettings.Enabled && !VrBattlegrounds.DevTools.PlayLaunchSettings.RequestActive)
                {
                    DebugBootstrapSettings.AutoLoadMapScene = PlayLaunchSettings.Effective.SceneSource == "active" ? activeScene.name : "";
                    GameLog.Debug.Info($"[PlayModeStartFromOffline] Карта '{activeScene.name}' загрузится после старта сервера (Bootstrap Settings).");
                }
                else
                {
                    GameLog.Debug.Info($"[PlayModeStartFromOffline] Play из '{activeScene.name}' стартует с Offline; Bootstrap Settings " +
                        "выключены — обычный старт игры, карта сама не загрузится.");
                }
            }
        }
    }
}
