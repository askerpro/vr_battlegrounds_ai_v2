using System.Collections;
using System.Reflection;
using UltimateXR.Mechanics.Weapons;

namespace VrBattlegrounds.Weapons.Sensors
{
    /// <summary>
    /// Чтение закрытых полей SDK-спуска, у которых нет публичного доступа: режим огня, темп, таймер темпа.
    /// Только чтение и только для теневого режима этапа C (редактор): правка SDK ради
    /// диагностики запрещена границами этапа. На этапе D значения придут через порт учёта.
    /// Если поле SDK переименовано, <see cref="IsAvailable"/> = false и тень не включается.
    /// </summary>
    internal static class FirearmIntrospection
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly FieldInfo Triggers = typeof(UxrFirearmWeapon).GetField("_triggers", Flags);
        private static readonly FieldInfo Runtime = typeof(UxrFirearmWeapon).GetField("_runtimeTriggers", Flags);
        private static PropertyInfo s_lastShotTimer;

        public static bool IsAvailable => Triggers != null && Runtime != null;

        /// <summary>Режим огня и темп спуска (<c>UxrFirearmTrigger.CycleType/MaxShotFrequency</c>).</summary>
        public static bool TryGetTriggerCycle(UxrFirearmWeapon weapon, int index, out UxrShotCycle cycle, out int maxFrequency)
        {
            cycle = UxrShotCycle.SemiAutomatic; maxFrequency = 0;
            if (weapon == null || !(Triggers?.GetValue(weapon) is IList list) || index < 0 || index >= list.Count || list[index] == null) return false;
            object trigger = list[index];
            PropertyInfo cycleProperty = trigger.GetType().GetProperty("CycleType", Flags);
            PropertyInfo frequencyProperty = trigger.GetType().GetProperty("MaxShotFrequency", Flags);
            if (cycleProperty == null || frequencyProperty == null) return false;
            cycle = (UxrShotCycle)cycleProperty.GetValue(trigger);
            maxFrequency = (int)frequencyProperty.GetValue(trigger);
            return true;
        }

        /// <summary>Остаток таймера темпа SDK (<c>RuntimeTriggerInfo.LastShotTimer</c>); 0, если прочитать нельзя.</summary>
        public static float GetLastShotTimer(UxrFirearmWeapon weapon, int index)
        {
            if (weapon == null || !(Runtime?.GetValue(weapon) is IDictionary runtime) || !runtime.Contains(index)) return 0f;
            object info = runtime[index];
            if (info == null) return 0f;
            if (s_lastShotTimer == null || s_lastShotTimer.DeclaringType != info.GetType())
                s_lastShotTimer = info.GetType().GetProperty("LastShotTimer", Flags);
            return s_lastShotTimer != null ? (float)s_lastShotTimer.GetValue(info) : 0f;
        }

    }
}
