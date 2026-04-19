using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Weapons;
using UltimateXR.Manipulation;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    ///     Позволяет настраивать параметры затвора в режиме редактирования.
    ///     Рисует прогресс-бар хода затвора прямо в инспекторе.
    /// </summary>
    [CustomEditor(typeof(AutomaticWeaponSlideFeedback))]
    public class AutomaticWeaponSlideFeedbackEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            // Отрисовка стандартных полей
            DrawDefaultInspector();

            AutomaticWeaponSlideFeedback script = (AutomaticWeaponSlideFeedback)target;

            // Находим нужные свойства через SerializedObject
            SerializedProperty slideProp      = serializedObject.FindProperty("_slide");
            SerializedProperty dirProp        = serializedObject.FindProperty("_localSlideDirection");
            SerializedProperty refOffsetProp  = serializedObject.FindProperty("_localSlideReferenceOffset");
            SerializedProperty thresholdProp  = serializedObject.FindProperty("_slideThreshold");

            // Пытаемся достать объект затвора
            UxrGrabbableObject grabbable = slideProp.objectReferenceValue as UxrGrabbableObject;
            if (grabbable == null)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("Назначьте объект Slide (UxrGrabbableObject) для отладки.", MessageType.None);
                return;
            }

            Transform slideTransform = grabbable.transform;

            // Достаем приватное поле _localStart
            var localStartField = typeof(AutomaticWeaponSlideFeedback).GetField("_localStart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (localStartField == null) return;
            
            Vector3 localStart = (Vector3)localStartField.GetValue(script);

            // Параметры для расчета
            Vector3 dir       = dirProp.vector3Value;
            float   denom     = refOffsetProp.vector3Value.magnitude;
            float   threshold = thresholdProp.floatValue;

            if (dir.sqrMagnitude < 1e-8f || denom < 1e-5f)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("Укажите направление (Direction) и эталонный ход (Reference Offset).", MessageType.None);
                return;
            }

            dir.Normalize();

            // Рассчитываем текущее положение как в основном скрипте
            Vector3 delta   = slideTransform.localPosition - localStart;
            float   absDist = Mathf.Abs(Vector3.Dot(delta, dir));
            float   current = absDist / denom;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Editor Visualization", EditorStyles.boldLabel);

            // Рисуем шкалу прогресса
            Rect rect = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.ProgressBar(rect, current, $"Slide Travel: {current:P0}");

            // Доп информация
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Abs Dist: {absDist:F4}m / {denom:F4}m", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Threshold: {threshold:P0}", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            // Цветовая индикация порога
            if (current >= threshold)
            {
                GUI.color = Color.green;
                EditorGUILayout.HelpBox("THRESHOLD REACHED: Reload will trigger on return.", MessageType.None);
                GUI.color = Color.white;
            }
            else
            {
                EditorGUILayout.HelpBox("Pull more to reach threshold...", MessageType.None);
            }

            EditorGUILayout.Space(5);
            
            if (GUILayout.Button("Capture Current Position as Rest (Zero)"))
            {
                localStartField.SetValue(script, slideTransform.localPosition);
                EditorUtility.SetDirty(script);
                serializedObject.ApplyModifiedProperties();
                Debug.Log($"[Editor] Base position (rest) updated for {script.name} at {slideTransform.localPosition:F4}");
            }

            // Заставляем инспектор обновляться, когда мы двигаем объекты в сцене
            if (!Application.isPlaying)
            {
                Repaint();
            }
        }
    }
}
