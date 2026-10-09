using UltimateXR.Core;
using UnityEditor;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor
{
    /// <summary>
    /// Галочка «ставить UltimateXR на паузу, пока окно редактора не в фокусе» (патч 33 UltimateXR). Личная настройка
    /// этой машины (EditorPrefs), в git не попадает. Выключить — чтобы Play, тесты и стенды считали IK и двигали
    /// аватары, пока работаешь в другом окне.
    /// </summary>
    public static class EditorFocusPauseMenu
    {
        private const string MenuPath = "Tools/VR Battlegrounds/Debug/Pause XR When Editor Unfocused";

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            var profile = VrBattlegrounds.DevTools.PlayLaunchSettings.ReadProfile();
            profile.PauseOnFocusLoss = !profile.PauseOnFocusLoss;
            VrBattlegrounds.DevTools.PlayLaunchSettings.SaveProfile(profile);
            GameLog.Debug.Info($"[EditorFocusPauseMenu] Пауза UltimateXR без фокуса редактора: {(UxrManager.EditorFocusPauseEnabled ? "включена" : "выключена")}.");
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, UxrManager.EditorFocusPauseEnabled);
            return true;
        }
    }
}
