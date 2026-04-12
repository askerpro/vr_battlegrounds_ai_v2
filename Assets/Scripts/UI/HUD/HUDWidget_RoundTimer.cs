using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Виджет таймера раунда для EliminationMode.
    /// </summary>
    public class HUDWidget_RoundTimer : MonoBehaviour
    {
        [SerializeField] private Text _timerText;
        [SerializeField] private Color _activeColor = Color.white;
        [SerializeField] private Color _countdownColor = Color.yellow;
        [SerializeField] private Color _endedColor = Color.red;

        private void Update()
        {
            if (_timerText == null) return;

            EliminationMode mode = FindFirstObjectByType<EliminationMode>();
            if (mode == null)
            {
                _timerText.text = "--:--";
                return;
            }

            float time = mode.CurrentRoundState == RoundState.Countdown
                ? mode.CountdownTimeRemaining
                : mode.RoundTimeRemaining;

            int minutes = Mathf.FloorToInt(time / 60f);
            int seconds = Mathf.FloorToInt(time % 60f);

            _timerText.text = $"{minutes:00}:{seconds:00}";

            // Меняем цвет в зависимости от стейта раунда
            switch (mode.CurrentRoundState)
            {
                case RoundState.Equipment:
                case RoundState.Countdown:
                    _timerText.color = _countdownColor;
                    break;
                case RoundState.Combat:
                    _timerText.color = _activeColor;
                    break;
                case RoundState.Setup:
                case RoundState.Resolution:
                case RoundState.Scoreboard:
                    _timerText.color = _endedColor;
                    break;
            }
        }
    }
}