using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.Editor
{
    [CustomEditor(typeof(EliminationMode))]
    public class EliminationModeEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint()
        {
            // Обновляем инспектор каждый кадр в Play Mode для отображения таймеров
            return Application.isPlaying;
        }

        public override void OnInspectorGUI()
        {
            // Отрисовываем базовый инспектор (настройки и т.д.)
            DrawDefaultInspector();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Debug информация доступна только в Play Mode.", MessageType.Info);
                return;
            }

            EliminationMode mode = (EliminationMode)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("🚀 Realtime Debug Game State", EditorStyles.boldLabel);
            
            // 1. Статус общей игры (GameplayManager)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Общий статус матча", EditorStyles.boldLabel);
            if (GameplayManager.Instance != null)
            {
                EditorGUILayout.LabelField("Gameplay Active:", GameplayManager.Instance.IsMatchActive.ToString());
            }
            else
            {
                EditorGUILayout.LabelField("GameplayManager не найден.");
            }
            EditorGUILayout.EndVertical();

            // 2. Статус раунда
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            // Active Timers
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Phase Timers", EditorStyles.boldLabel);
            
            if (mode.RoundManager != null)
            {
                string pendingConditions = mode.RoundManager.GetPendingReadinessStatus();
                EditorGUILayout.HelpBox(pendingConditions, MessageType.Info);
                
                if (mode.CurrentRoundState == RoundState.Setup || mode.CurrentRoundState == RoundState.Equipment || 
                    mode.CurrentRoundState == RoundState.Countdown || mode.CurrentRoundState == RoundState.Scoreboard || mode.CurrentRoundState == RoundState.Resolution)
                {
                    EditorGUILayout.LabelField($"Time Left: {mode.RoundManager.CountdownTimeRemaining:F1}s", EditorStyles.label);
                }
                else if (mode.CurrentRoundState == RoundState.Combat)
                {
                    EditorGUILayout.LabelField($"Combat Time Remaining: {mode.RoundManager.RoundTimeRemaining:F1}s", EditorStyles.label);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("RoundManager is missing or not initialized yet.", MessageType.Warning);
            }
            EditorGUILayout.EndVertical();

            // 3. Статистика раундов в сете
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Счёт по раундам в текущем сете", EditorStyles.boldLabel);
            foreach (var teamState in mode.TeamStates.Values)
            {
                int roundScore = mode.GetRoundScore(teamState.Team);
                EditorGUILayout.LabelField($"{teamState.Team.displayName}:", $"{roundScore} побед");
            }
            EditorGUILayout.EndVertical();

            // 4. Статистика сетов и команд
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Счёт по сетам в матче", EditorStyles.boldLabel);
            
            int totalSetsPlayed = 0;
            foreach (var teamState in mode.TeamStates.Values)
            {
                totalSetsPlayed += teamState.Score;
                EditorGUILayout.LabelField($"{teamState.Team.displayName} (Побед в сетах):", teamState.Score.ToString());
                EditorGUILayout.LabelField($"   - Живых игроков:", teamState.AliveSessions.Count().ToString());
            }

            EditorGUILayout.LabelField("Текущий сет:", (totalSetsPlayed + 1).ToString());
            EditorGUILayout.EndVertical();
        }
    }
}
