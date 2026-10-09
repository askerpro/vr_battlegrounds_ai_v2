using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor
{
    /// <summary>Совместимая точка старого меню/раскладки; настройки редактирует только Play Launch.</summary>
    public class DebugBootstrapWindow : EditorWindow
    {
        [MenuItem("Tools/VR Battlegrounds/Debug/Bootstrap Settings…")]
        private static void Open() => PlayLaunchWindow.Open();
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Настройки запуска перенесены в Play Launch. Профиль хранится отдельно для этого checkout.", MessageType.Info);
            if (GUILayout.Button("Открыть Play Launch")) { PlayLaunchWindow.Open(); Close(); }
        }
    }
}
