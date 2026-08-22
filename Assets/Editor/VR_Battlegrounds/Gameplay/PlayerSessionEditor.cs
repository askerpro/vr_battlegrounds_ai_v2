using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Player
{
    [CustomEditor(typeof(PlayerSession))]
    public class PlayerSessionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PlayerSession session = (PlayerSession)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Готовность к раунду", EditorStyles.boldLabel);

            GUI.enabled = false;
            // Готовность — явное состояние; зона и жетон стоят рядом как условие и жест,
            // из которых она больше не выводится (T-29).
            EditorGUILayout.Toggle("Готов к раунду (ReadyState)", session.ReadyState);
            EditorGUILayout.Toggle("В зоне спавна (условие)", session.IsInSpawnZone);
            EditorGUILayout.Toggle("Жетон взят (жест)", session.HasGrabbedDogTag);
            GUI.enabled = true;
        }
    }
}
