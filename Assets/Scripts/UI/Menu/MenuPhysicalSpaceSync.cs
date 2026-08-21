using UnityEngine;
using VrBattlegrounds.Player;
using VrBattlegrounds.Core;
using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Экран калибровки VR-шлема.
    /// Перенесен из устаревшего PlayerMenuController в отдельный MenuScreen.
    /// </summary>
    public class MenuPhysicalSpaceSync : MenuScreen
    {
        public void OnCalibratePressed()
        {
            if (PhysicalSpaceSyncManager.Instance != null)
            {
                PhysicalSpaceSyncManager.Instance.BeginCalibration();
            }
            else
            {
                GameLog.UI.Warning("[MenuCalibration] PhysicalSpaceSyncManager is not found in the scene! Ensure it is attached to the Global Managers.");
            }
        }

        public void OnCalibrateHeightPressed()
        {
            if (PhysicalSpaceSyncManager.Instance != null)
            {
                PhysicalSpaceSyncManager.Instance.BeginHeightCalibration();
            }
            else
            {
                GameLog.UI.Warning("[MenuCalibration] PhysicalSpaceSyncManager is not found!");
            }
        }
    }
}
