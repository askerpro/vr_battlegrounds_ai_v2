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

            // Пытаемся достать объект затвора
            UxrGrabbableObject grabbable = serializedObject.FindProperty("_slide").objectReferenceValue as UxrGrabbableObject;
            if (grabbable == null)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("Назначьте объект Slide (UxrGrabbableObject) для отладки.", MessageType.None);
                return;
            }

            Transform slideTransform = grabbable.transform;

            // Ось и длина хода — из Translation Limits затвора, как в рантайме
            if (!AutomaticWeaponSlideFeedback.TryGetSlideTravel(grabbable, out Vector3 dir, out float length))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox($"У '{grabbable.name}' нет хода: Translation Constraint = Restrict Local Offset и ненулевые Translation Limits.", MessageType.Warning);
                return;
            }

            // Достаем приватное поле _localStart
            var localStartField = typeof(AutomaticWeaponSlideFeedback).GetField("_localStart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (localStartField == null) return;

            float threshold = script.SlideThreshold;
            float current   = script.GetSlideProgress();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Editor Visualization", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Ход из лимитов затвора: {length * 100f:F2} см по оси {dir:F2}, порог {threshold * length * 100f:F2} см", EditorStyles.miniLabel);

            // Рисуем шкалу прогресса
            Rect rect = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.ProgressBar(rect, current, $"Slide Travel: {current:P0}");

            // Доп информация
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Abs Dist: {current * length:F4}m / {length:F4}m", EditorStyles.miniLabel);
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
