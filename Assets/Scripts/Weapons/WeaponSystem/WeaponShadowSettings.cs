using System;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Переключатель бывшего теневого режима WeaponSystem (этап C). <b>Потребителя нет с этапа D:</b> тень сравнивала
    /// машину со старым контроллером, а стволы на учёте (Herrington, FABARM) теперь управляются самой машиной; у стволов
    /// на старом коде учёта нет, сравнивать там нечего. Класс оставлен только потому, что его читает меню
    /// <c>Editor/VR_Battlegrounds/Debug/WeaponSystemShadowMenu.cs</c> вне области этапа; удалить вместе с меню (cleanup-h).
    /// Настройка — EditorPrefs, в сборке игрока всегда выключена.
    /// </summary>
    public static class WeaponShadowSettings
    {
        public const string PrefKey = "VrBattlegrounds.WeaponSystemShadow";

#if UNITY_EDITOR
        private static bool? s_enabled;
#endif

        /// <summary>Значение изменилось (подключение к уже настроенным стволам во время Play).</summary>
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
