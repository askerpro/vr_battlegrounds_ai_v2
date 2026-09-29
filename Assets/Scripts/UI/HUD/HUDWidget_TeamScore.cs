using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using System.Text;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Счёт команд активного режима (<see cref="GameMode.GetScore"/>): раунды за карту
    /// в Elimination, фраги в Respawn.
    /// </summary>
    public class HUDWidget_TeamScore : MonoBehaviour
    {
        [SerializeField] private Text _scoreText;

        private void Update()
        {
            if (_scoreText == null) return;

            GameMode mode = FindFirstObjectByType<GameMode>();
            if (mode == null || mode.Teams == null)
            {
                _scoreText.text = "Счет: --";
                return;
            }

            StringBuilder sb = new StringBuilder();
            
            foreach (TeamData team in mode.Teams)
            {
                if (team == null) continue;
                int score = mode.GetScore(team);
                sb.Append($"{team.Name}: {score}  ");
            }

            _scoreText.text = sb.ToString();
        }
    }
}