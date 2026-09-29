using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using System.Text;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Счёт карты Elimination: выигранные раунды команд за обе половины
    /// (<see cref="GameMode.GetScore"/> — базовый счёт режима, реплицируется).
    /// </summary>
    public class HUDWidget_EliminationMode_TeamRoundScore : MonoBehaviour
    {
        [SerializeField] private Text _scoreText;

        private void Update()
        {
            if (_scoreText == null) return;

            EliminationMode mode = FindFirstObjectByType<EliminationMode>();
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