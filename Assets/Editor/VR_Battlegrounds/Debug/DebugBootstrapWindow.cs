using UnityEditor;
using UnityEngine;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Editor
{
    /// <summary>
    /// Окно личных настроек быстрых отладочных сценариев (<see cref="DebugBootstrapSettings"/>, <c>DebugOrchestrator</c>).
    /// Значения — EditorPrefs этой машины, карта автозапуска — SessionState текущей сессии; в git не попадают.
    /// </summary>
    public class DebugBootstrapWindow : EditorWindow
    {
        [MenuItem("Tools/VR Battlegrounds/Debug/Bootstrap Settings…")]
        private static void Open()
        {
            GetWindow<DebugBootstrapWindow>(true, "Debug Bootstrap").minSize = new Vector2(380f, 380f);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Быстрые отладочные сценарии Play в редакторе. Личные настройки этой машины — в git и в сборку не попадают.",
                MessageType.None);

            DebugBootstrapSettings.Enabled = EditorGUILayout.Toggle(
                new GUIContent("Включено", "Выключить — обычный старт игры."), DebugBootstrapSettings.Enabled);

            using (new EditorGUI.DisabledScope(!DebugBootstrapSettings.Enabled))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Сеть", EditorStyles.boldLabel);
                DebugBootstrapSettings.AutoStartFallbackRole = EditorGUILayout.Toggle(
                    new GUIContent("Стартовать без выбора роли", "Без тега Multiplayer Play Mode сразу стартовать с ролью ниже."),
                    DebugBootstrapSettings.AutoStartFallbackRole);
                using (new EditorGUI.DisabledScope(!DebugBootstrapSettings.AutoStartFallbackRole))
                {
                    DebugBootstrapSettings.FallbackRole = (GameNetworkDiscovery.AppRole)EditorGUILayout.EnumPopup(
                        "Роль", DebugBootstrapSettings.FallbackRole);
                }
                DebugBootstrapSettings.HostIsAdmin = EditorGUILayout.Toggle(
                    new GUIContent("Хост — админ", "Хост получает профиль VR-игрока с правами админа."),
                    DebugBootstrapSettings.HostIsAdmin);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Карта и матч", EditorStyles.boldLabel);
                DebugBootstrapSettings.AutoLoadMapScene = EditorGUILayout.TextField(
                    new GUIContent("Карта (эта сессия)", "Загрузить после старта сервера. Play на открытой карте подставляет её сам."),
                    DebugBootstrapSettings.AutoLoadMapScene);
                DebugBootstrapSettings.AutoGameModeId = EditorGUILayout.TextField(
                    new GUIContent("Режим (modeId)", "Режим для автозагруженной карты. Пусто — без режима."),
                    DebugBootstrapSettings.AutoGameModeId);
                DebugBootstrapSettings.AutoGoLive = EditorGUILayout.Toggle(
                    new GUIContent("Автостарт матча", "Запустить матч, как только игроков хватает."),
                    DebugBootstrapSettings.AutoGoLive);
                DebugBootstrapSettings.MinPlayersOverride = EditorGUILayout.IntField(
                    new GUIContent("Минимум игроков", "0 — из данных режима."), DebugBootstrapSettings.MinPlayersOverride);
                DebugBootstrapSettings.BotCount = EditorGUILayout.IntField(
                    new GUIContent("Ботов", "Сколько ботов-противников сервер добавит сам."), DebugBootstrapSettings.BotCount);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Инструменты", EditorStyles.boldLabel);
            DebugBootstrapSettings.ScreenshotOnButtonB = EditorGUILayout.Toggle(
                new GUIContent("Скриншот по кнопке B", "VRScreenshotCapture: PNG в Screenshots/ по кнопке B правого контроллера. Со следующего Play."),
                DebugBootstrapSettings.ScreenshotOnButtonB);

            EditorGUILayout.Space();
            if (GUILayout.Button("Сбросить к значениям по умолчанию"))
            {
                DebugBootstrapSettings.ResetToDefaults();
                GUI.FocusControl(null);
            }
        }
    }
}
