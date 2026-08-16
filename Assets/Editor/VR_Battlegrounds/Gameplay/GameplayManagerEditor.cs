using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Managers;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.Managers.Editor
{
    [CustomEditor(typeof(GameplayManager))]
    public class GameplayManagerEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint()
        {
            if (target == null) return false;
            GameplayManager manager = (GameplayManager)target;
            return Application.isPlaying && manager.IsGameplayActive;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            GameplayManager manager = (GameplayManager)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Состояние матча (только чтение)", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(true);

            EditorGUILayout.Toggle("Gameplay Active", manager.IsGameplayActive);

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
