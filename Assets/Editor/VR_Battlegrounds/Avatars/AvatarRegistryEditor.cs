#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Mirror;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Avatars.Workbench;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Кастомный инспектор для AvatarRegistry.
    /// Проверка только читает. Синхронизация запускается явно через редактор аватара.
    /// </summary>
    [CustomEditor(typeof(AvatarRegistry))]
    public class AvatarRegistryEditor : UnityEditor.Editor
    {
        NetworkManager manager;
        string report;
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            EditorGUILayout.Space(15);
            manager = (NetworkManager)EditorGUILayout.ObjectField("NetworkManager prefab", manager, typeof(NetworkManager), false);
            if (GUILayout.Button("Проверить регистрацию")) report = AvatarRegistryTools.Validate((AvatarRegistry)target, manager);
            if (GUILayout.Button("Открыть редактор аватара")) AvatarEditorWindow.OpenFor(target);
            EditorGUILayout.HelpBox("Проверка не записывает ассеты. Явная синхронизация доступна в редакторе аватара после просмотра области записи.", MessageType.Info);
            if (!string.IsNullOrEmpty(report)) EditorGUILayout.HelpBox(report, MessageType.Info);
        }
    }
}
#endif
