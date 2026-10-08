using UltimateXR.Haptics;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.EditorTools.Haptics
{
    /// <summary>
    /// Инспектор <see cref="UxrHapticClip" /> везде — в компонентах UltimateXR (хват, выстрел, помпа, UI), в ролях и
    /// переопределениях игры. Первым — поле формы (SDK-патч 66). Форма задана: сила, приоритет, пауза повтора, кулдаун,
    /// вторая рука, картинка формы и проба на руках в Play; прежние поля SDK скрыты. Формы нет: прежние поля SDK как раньше.
    /// </summary>
    [CustomPropertyDrawer(typeof(UxrHapticClip))]
    internal sealed class UxrHapticClipDrawer : PropertyDrawer
    {
        private static readonly string[] LegacyFields =
            { "_clip", "_clipAmplitude", "_hapticMode", "_fallbackClipType", "_fallbackAmplitude", "_fallbackDurationSeconds" };

        private const float GraphHeight = 36f;

        private static float Line => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded) return Line;
            bool hasWaveform = property.FindPropertyRelative("_waveform").objectReferenceValue != null;
            float height = Line * 2f;                                  // заголовок + поле формы
            if (!hasWaveform) return height + Line * LegacyFields.Length;
            height += Line * 5f + GraphHeight + 4f + Line;              // параметры, картинка, проба
            if (IsSdkComponent(property)) height += Line * 2f;
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            SerializedProperty waveform = property.FindPropertyRelative("_waveform");
            var shape = waveform.objectReferenceValue as UxrHapticWaveform;

            Rect line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            string summary = shape != null
                ? $"{shape.name} · ×{property.FindPropertyRelative("_waveformGain").floatValue:0.00} · " +
                  $"{(UxrHapticPriority)property.FindPropertyRelative("_priority").intValue}"
                : $"SDK: {(UxrHapticClipType)property.FindPropertyRelative("_fallbackClipType").intValue}";
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, new GUIContent($"{label.text}  ({summary})", label.tooltip), true);
            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                line.y += Line;
                EditorGUI.PropertyField(line, waveform, new GUIContent("Форма", "Общий ассет формы. Пусто — прежнее поведение SDK."));

                if (shape == null)
                {
                    foreach (string field in LegacyFields)
                    {
                        line.y += Line;
                        EditorGUI.PropertyField(line, property.FindPropertyRelative(field));
                    }
                    EditorGUI.EndProperty();
                    return;
                }

                line.y += Line;
                Slider(line, property.FindPropertyRelative("_waveformGain"), "Сила", 0f, 1f);
                line.y += Line;
                EditorGUI.PropertyField(line, property.FindPropertyRelative("_priority"),
                    new GUIContent("Приоритет", "Low — «можно», Normal — физика, High — отказ/предупреждение, Critical — гибель"));
                line.y += Line;
                SerializedProperty gap = property.FindPropertyRelative("_repeatGapMs");
                gap.intValue = EditorGUI.IntSlider(line, new GUIContent("Пауза повтора, мс", "Для непрерывного: пауза между повторами формы"),
                                                   gap.intValue, 0, 3000);
                line.y += Line;
                Slider(line, property.FindPropertyRelative("_cooldownSeconds"), "Кулдаун, с", 0f, 2f);
                line.y += Line;
                Slider(line, property.FindPropertyRelative("_secondaryHandGain"), "Вторая рука, ×", 0f, 1f);

                line.y += Line;
                Rect graph = EditorGUI.IndentedRect(new Rect(line.x, line.y, line.width, GraphHeight));
                HapticEditorPreview.DrawWaveform(graph, shape);
                line.y += GraphHeight + 4f;

                UxrHapticClip clip = GetClip(property);
                HapticEditorPreview.DrawButtons(EditorGUI.IndentedRect(line), clip, property.serializedObject.targetObject);

                if (IsSdkComponent(property))
                {
                    line.y += Line;
                    EditorGUI.HelpBox(new Rect(line.x, line.y, line.width, Line * 2f - 2f),
                        "В компонентах UltimateXR форма начнёт играть после перехвата SDK (этап sdk-routing); до этого SDK играет прежние поля.",
                        MessageType.Info);
                }
            }

            EditorGUI.EndProperty();
        }

        private static void Slider(Rect rect, SerializedProperty property, string label, float min, float max) =>
            property.floatValue = EditorGUI.Slider(rect, label, property.floatValue, min, max);

        private static bool IsSdkComponent(SerializedProperty property)
        {
            Object target = property.serializedObject.targetObject;
            return target != null && (target.GetType().Namespace ?? "").StartsWith("UltimateXR");
        }

        /// <summary>Сам клип из инспектируемого объекта — для пробы (форма — тот же ассет, параметры — текущие).</summary>
        private static UxrHapticClip GetClip(SerializedProperty property)
        {
            try
            {
                return property.boxedValue as UxrHapticClip;
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
