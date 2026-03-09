using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using System.Text;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Виджет отображения счета.
    /// Читает счетчики из GameplayManager.Instance.TeamScores (для Respawn) 
    /// или использует SetManager / RoundManager если нужно.
    /// В данном примере показывает глобальный счет команд в матче.
    /// </summary>
    public class HUDWidget_EliminationMode_TeamRoundScore : MonoBehaviour
    {
        [SerializeField] private Text _scoreText;

        private void Update()
        {
            if (_scoreText == null) return;

            EliminationMode mode = FindObjectOfType<EliminationMode>();
            if (mode == null || mode.Teams == null)
            {
                _scoreText.text = "Счет: --";
                return;
            }

            StringBuilder sb = new StringBuilder();
            
            foreach (TeamData team in mode.Teams)
            {
                if (team == null) continue;
                int score = mode.GetRoundScore(team);
                sb.Append($"{team.displayName}: {score}  ");
            }

            _scoreText.text = sb.ToString();
        }
    }
}