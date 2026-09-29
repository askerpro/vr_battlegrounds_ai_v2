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

            float time = RoundClock.SelectEliminationTime(mode.CurrentRoundPhase,
                mode.CountdownTimeRemaining, mode.EquipmentTimeRemaining, mode.RoundTimeRemaining);

            int minutes = Mathf.FloorToInt(time / 60f);
            int seconds = Mathf.FloorToInt(time % 60f);

            _timerText.text = $"{minutes:00}:{seconds:00}";

            // Меняем цвет в зависимости от стейта раунда
            switch (mode.CurrentRoundPhase)
            {
                case RoundPhase.Equipment:
                case RoundPhase.Countdown:
                    _timerText.color = _countdownColor;
                    break;
                case RoundPhase.Combat:
                    _timerText.color = _activeColor;
                    break;
                case RoundPhase.Setup:
                case RoundPhase.Resolution:
                case RoundPhase.Scoreboard:
                    _timerText.color = _endedColor;
                    break;
            }
        }
    }
}