using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.PhysicalSpaceUtils;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Раздел «Калибровка» (<see cref="MenuScreenType.PhysicalSpaceSync"/>): совмещение VR-пространства
    /// с физической ареной и калибровка роста. Экран только запускает процесс у
    /// <see cref="PhysicalSpaceSyncManager"/>; ход калибровки показывает HUD.
    /// </summary>
    public class MenuPhysicalSpaceSync : MenuScreen
    {
        public override void Show()
        {
            base.Show();
            MenuKit.Clear(Content);
            MenuKit.Title(Content, "Калибровка");

            MenuKit.Section(Content, "Пространство");
            MenuKit.Label(Content, "Совместить виртуальную арену с физической: встаньте на отметку и следуйте подсказкам.",
                          MenuTextRole.Body, MenuColorRole.TextSecondary);
            MenuKit.Button(MenuKit.Row(Content), "Калибровать пространство", OnCalibratePressed);

            MenuKit.Section(Content, "Рост");
            MenuKit.Label(Content, "Подогнать рост аватара под ваш: встаньте прямо.",
                          MenuTextRole.Body, MenuColorRole.TextSecondary);
            MenuKit.Button(MenuKit.Row(Content), "Калибровать рост", OnCalibrateHeightPressed);
        }

        public void OnCalibratePressed()
        {
            if (PhysicalSpaceSyncManager.Instance != null)
                PhysicalSpaceSyncManager.Instance.BeginCalibration();
            else
                GameLog.UI.Warning("[MenuCalibration] PhysicalSpaceSyncManager is not found in the scene! Ensure it is attached to the Global Managers.");
        }

        public void OnCalibrateHeightPressed()
        {
            if (PhysicalSpaceSyncManager.Instance != null)
                PhysicalSpaceSyncManager.Instance.BeginHeightCalibration();
            else
                GameLog.UI.Warning("[MenuCalibration] PhysicalSpaceSyncManager is not found!");
        }
    }
}
