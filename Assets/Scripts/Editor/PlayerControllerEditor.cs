using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Player;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor
{
    /// <summary>
    /// Кастомный редактор для PlayerController.
    /// Выводит отладочную информацию (здоровье, команду, живой ли игрок) в удобном виде.
    /// </summary>
    [CustomEditor(typeof(PlayerController))]
    public class PlayerControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            // Рисуем дефолтные поля (например, события или другие сериализованные поля)
            DrawDefaultInspector();

            PlayerController player = (PlayerController)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debug State (Runtime)", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Статус жизни
                GUI.color = player.IsAlive ? Color.green : Color.red;
                EditorGUILayout.LabelField("Status", player.IsAlive ? "ALIVE" : "DEAD", EditorStyles.boldLabel);
                GUI.color = Color.white;

                // Здоровье
                float health = player.Health;
                Color healthColor = Color.Lerp(Color.red, Color.green, health / 100f);
                
                Rect rect = EditorGUILayout.GetControlRect(false, 20);
                EditorGUI.ProgressBar(rect, health / 100f, $"Health: {health:F1}");
                
                EditorGUILayout.Space(2);

                // Команда
                var team = player.Team;
                string teamName = team != null ? team.displayName : "No Team";
                EditorGUILayout.LabelField("Team", teamName);
                
                if (team != null)
                {
                    EditorGUILayout.LabelField("Team Index", player.TeamIndex.ToString());
                }
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(player);
            }
        }
    }
}
