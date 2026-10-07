using System;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Переключатель теневого режима WeaponSystem (этап C): личная настройка машины разработчика (EditorPrefs),
    /// по умолчанию выключен; меню <c>Tools/VR Battlegrounds/Debug/Weapon System Shadow</c>.
    /// В сборке игрока тень всегда выключена: настройка живёт только в редакторе, а тень удваивает работу
    /// оружия и пишет диагностику — на Quest это лишняя нагрузка, а проверка в шлеме идёт через Play Mode (Link).
    /// Стенд, меняющий настройку из кода, возвращает её после себя.
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
