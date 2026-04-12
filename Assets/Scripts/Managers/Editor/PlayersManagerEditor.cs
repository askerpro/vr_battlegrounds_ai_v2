using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Managers;
using System.Linq;

namespace VrBattlegrounds.Managers
{
    [CustomEditor(typeof(PlayersManager))]
    public class PlayersManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PlayersManager manager = (PlayersManager)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Player Readiness Debug", EditorStyles.boldLabel);

            if (Application.isPlaying && manager.Sessions != null)
            {
                var readyPlayers = manager.Sessions.Where(s => s.IsReadyForRound).ToList();
                var unreadyPlayers = manager.Sessions.Where(s => !s.IsReadyForRound).ToList();

                EditorGUILayout.LabelField($"Ready Players ({readyPlayers.Count}):");
                EditorGUI.indentLevel++;
                foreach (var session in readyPlayers)
                {
                    EditorGUILayout.LabelField($"► {session.PlayerName}", EditorStyles.label);
                }
                EditorGUI.indentLevel--;

                EditorGUILayout.Space();

                EditorGUILayout.LabelField($"Unready Players ({unreadyPlayers.Count}):");
                EditorGUI.indentLevel++;
                foreach (var session in unreadyPlayers)
                {
                    string reasons = "";
                    if (!session.IsInSpawnZone) reasons += "[Not in Zone] ";
                    if (!session.HasGrabbedDogTag) reasons += "[DogTag Not Grabbed] ";
                    EditorGUILayout.LabelField($"► {session.PlayerName} - {reasons}", EditorStyles.wordWrappedLabel);
                }
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.HelpBox("Play mode required to view readiness states.", MessageType.Info);
            }
        }
    }
}
