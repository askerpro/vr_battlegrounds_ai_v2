using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.Core;

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

        // Добавляем пункт в менюшку для возможности выключения этого поведения
        [MenuItem("VrBattlegrounds/Start from Offline Scene")]
        public static void ToggleAction()
        {
            bool enabled = EditorPrefs.GetBool(PrefKey, true);
            EditorPrefs.SetBool(PrefKey, !enabled);
            UpdateState();
        }

        [MenuItem("VrBattlegrounds/Start from Offline Scene", true)]
        public static bool ToggleActionValidate()
        {
            Menu.SetChecked("VrBattlegrounds/Start from Offline Scene", EditorPrefs.GetBool(PrefKey, true));
            return true;
        }

        static PlayModeStartFromOffline()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += UpdateState;
        }

        private static void UpdateState()
        {
            bool enabled = EditorPrefs.GetBool(PrefKey, true);
            if (enabled)
            {
                var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(OfflineScenePath);
                if (sceneAsset != null)
                {
                    EditorSceneManager.playModeStartScene = sceneAsset;
                }
                else
                {
                    Debug.LogWarning($"[PlayModeStartFromOffline] Сцена {OfflineScenePath} не найдена. Создайте или обновите путь.");
                }
            }
            else
            {
                EditorSceneManager.playModeStartScene = null;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                // Принудительно обновляем начальную сцену перед самым стартом
                UpdateState();
                
                GameLog.Info(LogLevel.Info, $"[PlayModeStartFromOffline] Exiting Edit Mode. playModeStartScene is {(EditorSceneManager.playModeStartScene != null ? EditorSceneManager.playModeStartScene.name : "null")}");

                if (!EditorPrefs.GetBool(PrefKey, true)) return;

                Scene activeScene = EditorSceneManager.GetActiveScene();
                
                // Если мы уже в оффлайн-сцене, ничего специально делать не нужно
                if (activeScene.path == OfflineScenePath) return;

                // Находим конфиг DebugBootstrapConfig для авто-проброса текущей карты
                string[] guids = AssetDatabase.FindAssets("t:DebugBootstrapConfig");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    DebugBootstrapConfig config = AssetDatabase.LoadAssetAtPath<DebugBootstrapConfig>(path);
                    
                    if (config != null && config.enabled)
                    {
                        // Прописываем активную сцену для авто-загрузки
                        config.autoLoadMapScene = activeScene.name;
                        EditorUtility.SetDirty(config);
                        AssetDatabase.SaveAssets(); 
                        Debug.Log($"[PlayModeStartFromOffline] Активная сцена '{activeScene.name}' добавлена в {config.name} для автозагрузки.");
                    }
                }
            }
        }
    }
}
