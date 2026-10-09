using UltimateXR.Haptics;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Haptics;

namespace VrBattlegrounds.EditorTools.Haptics
{
    /// <summary>
    /// Окно «Вибрация» (<c>Tools/VR Battlegrounds/Haptics/Вибрация</c>): живое состояние рук, реестр форм
    /// (<see cref="UxrHapticWaveform" />, выбор открывает инспектор формы), отклик взаимодействий по умолчанию
    /// (<see cref="InteractionFeedbackConfig" />) и клипы всех префабов отклика (<see cref="HapticPlayer" /> в
    /// <c>Assets/Prefabs/Feedback</c>) с пробой. Подбор в Play Mode через Quest Link: правка сразу действует на контроллеры,
    /// исполнитель пересоздаёт активные отклики.
    /// </summary>
    internal sealed class HapticTuningWindow : EditorWindow
    {
        private const string FeedbackFolder = "Assets/Prefabs/Feedback";

        private Vector2 _scroll;
        private UnityEditor.Editor _configEditor;

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
            EditorGUILayout.LabelField("Отклик по умолчанию", EditorStyles.boldLabel);
            var config = Resources.Load<InteractionFeedbackConfig>(nameof(InteractionFeedbackConfig));
            if (config == null)
            {
                EditorGUILayout.HelpBox("Нет Assets/Resources/InteractionFeedbackConfig.asset.", MessageType.Error);
            }
            else
            {
                UnityEditor.Editor.CreateCachedEditor(config, null, ref _configEditor);
                _configEditor.OnInspectorGUI();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Вибрация префабов отклика", EditorStyles.boldLabel);
            if (AssetDatabase.IsValidFolder(FeedbackFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { FeedbackFolder }))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                    if (prefab == null) continue;
                    foreach (HapticPlayer player in prefab.GetComponentsInChildren<HapticPlayer>(true))
                        DrawPlayer(prefab, player);
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Записать формы и отклик сейчас")) HapticAutoSave.SaveAll();
            EditorGUILayout.EndScrollView();
        }

        /// <summary>Режим и клип проигрывателя прямо в ассете префаба: правка сохраняется, в Play — сразу слышна.</summary>
        private static void DrawPlayer(GameObject prefab, HapticPlayer player)
        {
            string label = player.gameObject == prefab ? prefab.name : $"{prefab.name}/{player.name}";
            if (GUILayout.Button(label, EditorStyles.label)) Selection.activeObject = prefab;

            var serialized = new SerializedObject(player);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serialized.FindProperty("_mode"));
            EditorGUILayout.PropertyField(serialized.FindProperty("_clip"), true);
            EditorGUI.indentLevel--;
            if (serialized.ApplyModifiedProperties()) PrefabUtility.SavePrefabAsset(prefab);
        }

        private void OnDisable()
        {
            if (_configEditor != null) DestroyImmediate(_configEditor);
        }
    }
}
