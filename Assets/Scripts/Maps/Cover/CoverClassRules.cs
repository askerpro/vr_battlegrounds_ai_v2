using System.Text.RegularExpressions;
using UnityEngine;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Единственное место, где решается, какой класс укрытия положен объекту (LD-31): суффикс имени
    /// <c>_Hard</c>, <c>_Soft</c> или <c>_Visual</c> — <c>LD_Wall_Mid_Soft</c>, <c>Fence_Soft (2)</c>.
    ///
    /// <para>
    /// Имя — источник правды, <see cref="CoverSurface"/> — его копия для луча пули. По правилу работают и
    /// инструмент разметки, и тест согласованности. Суффикс — только через подчёркивание: голое
    /// <c>Visual</c> (частое имя дочернего меша) классом не считается.
    /// </para>
    /// </summary>
    public static class CoverClassRules
    {
        private static readonly Regex Suffix = new Regex(@"_(Hard|Soft|Visual)(?=$|[_\s(.])", RegexOptions.CultureInvariant);

        /// <summary>Класс по имени; false — в имени класса нет, компонента быть не должно.</summary>
        public static bool TryParse(string name, out CoverClass coverClass)
        {
            coverClass = CoverClass.Hard;
            if (string.IsNullOrEmpty(name)) return false;

            Match match = Suffix.Match(name);
            if (!match.Success) return false;

            switch (match.Groups[1].Value)
            {
                case "Soft":   coverClass = CoverClass.Soft;   break;
                case "Visual": coverClass = CoverClass.Visual; break;
                default:       coverClass = CoverClass.Hard;   break;
            }

            return true;
        }

        public static bool TryExpectedClass(GameObject go, out CoverClass coverClass) => TryParse(go.name, out coverClass);
    }
}
