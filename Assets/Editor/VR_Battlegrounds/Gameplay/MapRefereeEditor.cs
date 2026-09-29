using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Managers;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.Managers.Editor
{
    [CustomEditor(typeof(MapReferee))]
    public class MapRefereeEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint()
        {
            if (target == null) return false;
            MapReferee manager = (MapReferee)target;
            return Application.isPlaying && manager.IsLiveOrPaused;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            MapReferee manager = (MapReferee)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Состояние матча (только чтение)", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(true);

            EditorGUILayout.Toggle("Match Active", manager.IsLiveOrPaused);

            if (manager.ActiveGameMode != null)
            {
                EditorGUILayout.TextField("Active Game Mode", manager.ActiveGameMode.GetType().Name);

                var teams = manager.ActiveGameMode.Teams;
                if (teams != null)
                {
                    if (teams.Length > 0 && teams[0] != null)
                        EditorGUILayout.IntField($"Score {teams[0].displayName}", manager.ActiveGameMode.GetScore(teams[0]));
                    if (teams.Length > 1 && teams[1] != null)
                        EditorGUILayout.IntField($"Score {teams[1].displayName}", manager.ActiveGameMode.GetScore(teams[1]));
                }
            }
            else
            {
                EditorGUILayout.TextField("Active Game Mode", "—");
            }

            EditorGUI.EndDisabledGroup();
        }
    }
}
