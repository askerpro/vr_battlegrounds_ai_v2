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
            EditorGUILayout.LabelField("Debug Readiness", EditorStyles.boldLabel);
            
            GUI.enabled = false;
            EditorGUILayout.Toggle("Is In Spawn Zone", session.IsInSpawnZone);
            EditorGUILayout.Toggle("Has Grabbed Dog Tag", session.HasGrabbedDogTag);
            EditorGUILayout.Toggle("Is Ready For Round", session.IsReadyForRound);
            GUI.enabled = true;
        }
    }
}
