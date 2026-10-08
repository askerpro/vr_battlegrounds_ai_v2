using UltimateXR.Haptics;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.EditorTools.Haptics
{
    /// <summary>
    /// Инспектор формы <see cref="UxrHapticWaveform" /> (SDK-патч 66): картинка формы, ползунки силы и длительности каждого
    /// отрезка, добавить/убрать отрезок, проба на руках в Play с выбранной силой. Правка формы сразу слышна во всех клипах,
    /// которые на неё ссылаются (сервис перечитывает форму каждый тик). Запись на диск — при выходе из Play
    /// (<see cref="HapticAutoSave" />).
    /// </summary>
    [CustomEditor(typeof(UxrHapticWaveform))]
    internal sealed class UxrHapticWaveformEditor : UnityEditor.Editor
    {
        private const int MaxSegmentMs = 3000;

        private float _previewGain = 1f;
        private UxrHapticPriority _previewPriority = UxrHapticPriority.Normal;
        private UxrHapticClip _previewClip;

        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var waveform = (UxrHapticWaveform)target;

            HapticEditorPreview.DrawLiveState();
            EditorGUILayout.Space();
            HapticEditorPreview.DrawWaveform(GUILayoutUtility.GetRect(10f, 60f, GUILayout.ExpandWidth(true)), waveform);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("_description"));

            SerializedProperty segments = serializedObject.FindProperty("_segments");
            int remove = -1;
            for (int i = 0; i < segments.arraySize; i++)
            {
                SerializedProperty segment = segments.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"Отрезок {i + 1}", GUILayout.Width(80));
                    using (new EditorGUILayout.VerticalScope())
                    {
                        SerializedProperty amplitude = segment.FindPropertyRelative("Amplitude");
                        amplitude.floatValue = EditorGUILayout.Slider("Сила (0 — пауза)", amplitude.floatValue, 0f, 1f);
                        SerializedProperty duration = segment.FindPropertyRelative("DurationMs");
                        duration.intValue = EditorGUILayout.IntSlider("Длительность, мс", duration.intValue, 10, MaxSegmentMs);
                    }
                    using (new EditorGUI.DisabledScope(segments.arraySize <= 1))
                        if (GUILayout.Button("×", GUILayout.Width(24))) remove = i;
                }
            }
            if (remove >= 0) segments.DeleteArrayElementAtIndex(remove);
            if (GUILayout.Button("Добавить отрезок")) segments.InsertArrayElementAtIndex(segments.arraySize);

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Проба формы", EditorStyles.boldLabel);
            _previewGain = EditorGUILayout.Slider("Сила пробы", _previewGain, 0f, 1f);
            _previewPriority = (UxrHapticPriority)EditorGUILayout.EnumPopup("Приоритет пробы", _previewPriority);
            _previewClip ??= new UxrHapticClip();
            _previewClip.Waveform = waveform;
            _previewClip.WaveformGain = _previewGain;
            _previewClip.Priority = _previewPriority;
            HapticEditorPreview.DrawButtonsLayout(_previewClip, waveform);

            if (GUILayout.Button("Записать ассет сейчас")) AssetDatabase.SaveAssetIfDirty(waveform);
        }

        private void OnDisable() => HapticEditorPreview.StopHeld();
    }
}
