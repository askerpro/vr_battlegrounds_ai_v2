#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Editor.Arsenal
{
    [CustomEditor(typeof(ArsenalWallController))]
    public class ArsenalWallControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            // Отрисовка стандартных полей (_allSlots, _dogTagController, _animator и т.д.)
            DrawDefaultInspector();

            ArsenalWallController controller = (ArsenalWallController)target;

            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField("Runtime Arsenal State", EditorStyles.boldLabel);

            // Показываем текущее состояние арсенала (read-only)
            GUI.enabled = false;
            EditorGUILayout.EnumPopup("Current State", controller.CurrentState);
            GUI.enabled = true;

            EditorGUILayout.Space(5);

            // Кнопки для управления (работают только в Play Mode)
            if (Application.isPlaying)
            {
                EditorGUILayout.BeginHorizontal();
                
                if (GUILayout.Button("Open Arsenal", GUILayout.Height(30)))
                {
                    controller.OpenArsenal();
                }
                
                if (GUILayout.Button("Force Close", GUILayout.Height(30)))
                {
                    controller.ForceClose();
                }
                
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("Управление состояниями доступно только в режиме Play Mode.", MessageType.Info);
            }
        }
        
        public override bool RequiresConstantRepaint()
        {
            // Заставляет инспектор обновляться каждый кадр (чтобы мы видели смену состояний в реальном времени)
            return Application.isPlaying;
        }
    }
}
#endif
