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
                EditorGUILayout.LabelField("Gameplay Active:", GameplayManager.Instance.IsGameplayActive.ToString());
            }
            else
            {
                EditorGUILayout.LabelField("GameplayManager не найден.");
            }
            EditorGUILayout.EndVertical();

            // 2. Статус раунда
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Статус раунда (Elimination)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Текущее состояние:", mode.CurrentRoundState.ToString());
            
            if (mode.CurrentRoundState == RoundState.Countdown)
            {
                EditorGUILayout.LabelField("Обратный отсчёт:", mode.CountdownTimeRemaining.ToString("F1") + " сек");
            }
            else if (mode.CurrentRoundState == RoundState.Active)
            {
                EditorGUILayout.LabelField("Осталось времени:", mode.RoundTimeRemaining.ToString("F1") + " сек");
            }
            EditorGUILayout.EndVertical();

            // 3. Статистика сетов и команд
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Счёт по сетам", EditorStyles.boldLabel);
            
            int totalSetsPlayed = 0;
            foreach (var teamState in mode.TeamStates.Values)
            {
                totalSetsPlayed += teamState.Score;
                EditorGUILayout.LabelField($"{teamState.Team.displayName} (Побед в сетах):", teamState.Score.ToString());
                EditorGUILayout.LabelField($"   - Живых игроков:", teamState.AlivePlayers.Count().ToString());
            }

            EditorGUILayout.LabelField("Текущий сет:", (totalSetsPlayed + 1).ToString());
            EditorGUILayout.EndVertical();
        }
    }
}
