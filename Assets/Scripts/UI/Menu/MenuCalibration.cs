using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Экран калибровки VR-шлема.
    /// Перенесен из устаревшего PlayerMenuController в отдельный MenuScreen.
    /// </summary>
    public class MenuCalibration : MenuScreen
    {
        [Header("Зависимости")]
        [Tooltip("Необходимый компонент для калибровки аватара.")]
        [SerializeField] private VrCalibrationController _calibration;

        /// <summary>
        /// Запускает процедуру калибровки VR-шлема.
        /// Вызывается кнопкой UI (UnityEvent).
        /// </summary>
        public void OnCalibratePressed()
        {
            if (_calibration != null)
            {
                _calibration.Calibrate();
            }
            else
            {
                Debug.LogWarning("[MenuCalibration] VrCalibrationController не назначен!");
            }
        }
    }
}
