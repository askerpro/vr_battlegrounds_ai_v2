using System;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Переключатель дебаг-панели состояния оружия (<see cref="WeaponStatePanels"/>): личная настройка машины
    /// разработчика (EditorPrefs), по умолчанию выключена, не зависит от теневого режима; меню
    /// <c>Tools/VR Battlegrounds/Debug/Weapon State Panel</c>. В сборке игрока всегда выключена.
    /// Стенд, меняющий настройку из кода, возвращает её после себя.
    /// </summary>
    public static class WeaponStatePanelSettings
    {
        public const string PrefKey = "VrBattlegrounds.WeaponStatePanel";

#if UNITY_EDITOR
        private static bool? s_enabled;
#endif

        public static event Action<bool> Changed;

        public static bool Enabled
        {
#if UNITY_EDITOR
            get => s_enabled ??= UnityEditor.EditorPrefs.GetBool(PrefKey, false);
            set
            {
                if (Enabled == value) return;
                s_enabled = value;
                UnityEditor.EditorPrefs.SetBool(PrefKey, value);
                Changed?.Invoke(value);
            }
#else
            get => false;
            set { }
#endif
        }
    }
}
