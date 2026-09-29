using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Отображает общее прошедшее время матча (MatchTimer),
    /// опираясь на состояние MapReferee.
    /// </summary>
    public class HUDWidget_MapTimer : MonoBehaviour
    {
        [SerializeField] private Text _timerText;
        
        private float _startTime;
        private bool _matchWasActive;

        private void Update()
        {
            if (_timerText == null) return;

            if (MapReferee.Instance != null && MapReferee.Instance.IsMatchActive)
            {
                if (!_matchWasActive)
                {
                    _matchWasActive = true;
                    _startTime = Time.time;
                }

                float elapsed = Time.time - _startTime;
                int minutes = Mathf.FloorToInt(elapsed / 60f);
                int seconds = Mathf.FloorToInt(elapsed % 60f);
                _timerText.text = $"{minutes:00}:{seconds:00}";
            }
            else
            {
                if (_matchWasActive)
                {
                    // Матч остановился - фиксируем время
                    _matchWasActive = false;
                }
                else
                {
                    _timerText.text = "00:00";
                }
            }
        }
    }
}
