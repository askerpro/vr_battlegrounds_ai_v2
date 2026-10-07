using UnityEditor;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Editor
{
    /// <summary>
    /// Галочка теневого режима WeaponSystem (этап C): машина оружия работает рядом со старым контроллером на
    /// стволах с учётом готовности (Herrington, FABARM) и пишет расхождения в лог. Личная настройка этой машины
    /// (EditorPrefs), по умолчанию выключена; в сборке игрока не действует. Включение во время Play подключает
    /// тень к уже настроенным стволам, выключение — отключает с итоговой сводкой.
    /// </summary>
    public static class WeaponSystemShadowMenu
    {
        private const string MenuPath = "Tools/VR Battlegrounds/Debug/Weapon System Shadow";

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            WeaponShadowSettings.Enabled = !WeaponShadowSettings.Enabled;
            GameLog.Debug.Info($"[WeaponSystemShadowMenu] Теневой режим WeaponSystem: {(WeaponShadowSettings.Enabled ? "включён" : "выключен")}.");
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, WeaponShadowSettings.Enabled);
            return true;
        }
    }
}
