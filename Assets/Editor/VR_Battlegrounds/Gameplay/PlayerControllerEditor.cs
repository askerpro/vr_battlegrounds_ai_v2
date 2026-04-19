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
            if (player == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debug State (Runtime)", EditorStyles.boldLabel);

            // В edit mode сетевые/рантайм-ссылки часто null. Не даем инспектору падать.
            var actor = player._actor != null ? player._actor : player.GetComponent<UltimateXR.Mechanics.Weapons.UxrActor>();
            bool isAlive = actor != null && !actor.IsDead;
            float health = actor != null ? actor.Life : 0f;

            var session = player.Session;
            var team = session != null ? session.Team : null;
            string teamName = team != null ? team.displayName : "No Team";

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Статус жизни
                GUI.color = isAlive ? Color.green : Color.red;
                EditorGUILayout.LabelField("Status", isAlive ? "ALIVE" : "DEAD", EditorStyles.boldLabel);
                GUI.color = Color.white;

                // Здоровье
                Color healthColor = Color.Lerp(Color.red, Color.green, health / 100f);

                Rect rect = EditorGUILayout.GetControlRect(false, 20);
                EditorGUI.ProgressBar(rect, health / 100f, $"Health: {health:F1}");

                Rect overlayRect = rect;
                EditorGUI.DrawRect(new Rect(overlayRect.x, overlayRect.yMax - 2, overlayRect.width * Mathf.Clamp01(health / 100f), 2), healthColor);

                EditorGUILayout.Space(2);

                // Команда
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
