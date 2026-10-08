using UltimateXR.Haptics;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Haptics;

namespace VrBattlegrounds.EditorTools.Haptics
{
    /// <summary>
    /// Окно «Вибрация» (<c>Tools/VR Battlegrounds/Haptics/Вибрация</c>): живое состояние рук, роли вибрации игры
    /// (<see cref="HapticRoles" />) с пробой каждого клипа и реестр форм (<see cref="UxrHapticWaveform" />) — выбор формы
    /// открывает её инспектор. Подбор в Play Mode через Quest Link: правка сразу действует на контроллеры.
    /// </summary>
    internal sealed class HapticTuningWindow : EditorWindow
    {
        private Vector2 _scroll;
        private UnityEditor.Editor _rolesEditor;

        [MenuItem("Tools/VR Battlegrounds/Haptics/Вибрация")]
        private static void Open() => GetWindow<HapticTuningWindow>("Вибрация");

        private void OnInspectorUpdate()
        {
            if (Application.isPlaying) Repaint();
        }

        private void OnGUI()
        {
            HapticEditorPreview.DrawLiveState();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Формы", EditorStyles.boldLabel);
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(UxrHapticWaveform), new[] { "Assets/Data" }))
            {
                var waveform = AssetDatabase.LoadAssetAtPath<UxrHapticWaveform>(AssetDatabase.GUIDToAssetPath(guid));
                if (waveform == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(waveform.name, GUILayout.Width(180))) Selection.activeObject = waveform;
                    HapticEditorPreview.DrawWaveform(GUILayoutUtility.GetRect(10f, 22f, GUILayout.ExpandWidth(true)), waveform);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Роли игры", EditorStyles.boldLabel);
            var roles = Resources.Load<HapticRoles>(nameof(HapticRoles));
            if (roles == null)
            {
                EditorGUILayout.HelpBox("Нет Assets/Resources/HapticRoles.asset.", MessageType.Error);
            }
            else
            {
                UnityEditor.Editor.CreateCachedEditor(roles, null, ref _rolesEditor);
                _rolesEditor.OnInspectorGUI();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Записать формы и роли сейчас")) HapticAutoSave.SaveAll();
            EditorGUILayout.EndScrollView();
        }

        private void OnDisable()
        {
            if (_rolesEditor != null) DestroyImmediate(_rolesEditor);
        }
    }
}
