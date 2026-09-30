using UnityEditor;
using VrBattlegrounds.DevTools;

namespace VrBattlegrounds.Editor
{
    /// <summary>
    /// Галочка «скриншот по кнопке B в Play» (<see cref="VRScreenshotCapture"/>). Личная настройка этой машины
    /// (<see cref="DebugBootstrapSettings.ScreenshotOnButtonB"/>), в git не попадает; действует со следующего Play.
    /// </summary>
    public static class ScreenshotOnButtonMenu
    {
        private const string MenuPath = "Tools/VR Battlegrounds/Debug/Screenshot on B Button";

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            DebugBootstrapSettings.ScreenshotOnButtonB = !DebugBootstrapSettings.ScreenshotOnButtonB;
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, DebugBootstrapSettings.ScreenshotOnButtonB);
            return true;
        }
    }
}
