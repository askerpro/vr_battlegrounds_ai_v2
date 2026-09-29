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
            
            // 1. Статус общей игры (MapReferee)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Общий статус матча", EditorStyles.boldLabel);
            if (MapReferee.Instance != null)
            {
                EditorGUILayout.LabelField("Gameplay Active:", MapReferee.Instance.IsMatchActive.ToString());
            }
            else
            {
                EditorGUILayout.LabelField("MapReferee не найден.");
            }
            EditorGUILayout.EndVertical();

            // 2. Статус раунда
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            // Active Timers
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Phase Timers", EditorStyles.boldLabel);
            
            if (mode.RoundPhases != null)
            {
                string pendingConditions = mode.RoundPhases.GetPendingReadinessStatus();
                EditorGUILayout.HelpBox(pendingConditions, MessageType.Info);
                
                if (mode.CurrentRoundPhase == RoundPhase.Setup || mode.CurrentRoundPhase == RoundPhase.Equipment || 
                    mode.CurrentRoundPhase == RoundPhase.Countdown || mode.CurrentRoundPhase == RoundPhase.Scoreboard || mode.CurrentRoundPhase == RoundPhase.Resolution)
                {
                    EditorGUILayout.LabelField($"Time Left: {mode.RoundPhases.CountdownTimeRemaining:F1}s", EditorStyles.label);
                }
                else if (mode.CurrentRoundPhase == RoundPhase.Combat)
                {
                    EditorGUILayout.LabelField($"Combat Time Remaining: {mode.RoundPhases.RoundTimeRemaining:F1}s", EditorStyles.label);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("RoundPhases is missing or not initialized yet.", MessageType.Warning);
            }
            EditorGUILayout.EndVertical();

            // 3. Счёт карты: раунды за обе половины
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Счёт карты", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Раунд:", $"{mode.CurrentRoundNumber} / {mode.TotalRounds} (до победы {mode.RoundsToWin})");
            EditorGUILayout.LabelField("Половина:", mode.SidesSwapped ? "вторая (стороны поменялись)" : "первая");

            foreach (var teamState in mode.TeamStates.Values)
            {
                EditorGUILayout.LabelField($"{teamState.Team.displayName} (раундов):", teamState.Score.ToString());
                EditorGUILayout.LabelField($"   - Живых игроков:", teamState.AliveSessions.Count().ToString());
            }
            EditorGUILayout.EndVertical();
        }
    }
}
