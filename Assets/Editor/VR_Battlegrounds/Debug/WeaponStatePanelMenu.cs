using UnityEditor;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Editor
{
    /// <summary>
    /// Галочка дебаг-панели состояния оружия в шлеме (<see cref="WeaponStatePanels"/>): учёт SDK, ход Action и
    /// состояние теневой машины над стволами рядом с игроком. Личная настройка этой машины (EditorPrefs), по умолчанию
    /// выключена, не зависит от теневого режима; в сборке игрока не действует. Переключается и во время Play.
    /// </summary>
    public static class WeaponStatePanelMenu
    {
        private const string MenuPath = "Tools/VR Battlegrounds/Debug/Weapon State Panel";

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            WeaponStatePanelSettings.Enabled = !WeaponStatePanelSettings.Enabled;
            GameLog.Debug.Info($"[WeaponStatePanelMenu] Панель состояния оружия: {(WeaponStatePanelSettings.Enabled ? "включена" : "выключена")}.");
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, WeaponStatePanelSettings.Enabled);
            return true;
        }
    }
}
