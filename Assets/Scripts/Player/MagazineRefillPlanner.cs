using System;
using System.Collections.Generic;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Решает, чем пополнить карман магазинов: сколько магазинов к какому оружию выдать
    /// и какие лишние выкинуть, чтобы уложиться в вместимость.
    ///
    /// <para>
    /// Чистая логика без Mirror и UltimateXR: оружие и магазины — непрозрачные значения,
    /// совместимость и нужное количество приходят функциями. Благодаря этому правило
    /// проверяется EditMode-тестом, а <see cref="PlayerLoadoutManager"/> остаётся
    /// тонкой сетевой оболочкой.
    /// </para>
    /// </summary>
    public static class MagazineRefillPlanner
    {
        public readonly struct Plan
        {
            /// <summary>Индексы оружия, к которому выдать магазин; индекс повторяется по числу магазинов.</summary>
            public readonly List<int> SpawnFor;

            /// <summary>Индексы магазинов кармана, которые нужно убрать, чтобы освободить место.</summary>
            public readonly List<int> Discard;

            public Plan(List<int> spawnFor, List<int> discard)
            {
                SpawnFor = spawnFor;
                Discard = discard;
            }
        }

        /// <param name="weapons">Экипированное оружие, по одному на тип.</param>
        /// <param name="stored">Магазины в кармане, от старых к новым.</param>
        /// <param name="fits">Встаёт ли магазин в оружие.</param>
        /// <param name="wanted">Сколько магазинов держать к этому оружию.</param>
        /// <param name="capacity">Вместимость кармана.</param>
        public static Plan Compute<TWeapon, TMagazine>(
            IReadOnlyList<TWeapon> weapons,
            IReadOnlyList<TMagazine> stored,
            Func<TMagazine, TWeapon, bool> fits,
            Func<TWeapon, int> wanted,
            int capacity)
        {
            var spawnFor = new List<int>();
            var discard = new List<int>();
            var assigned = new bool[stored.Count];
            var need = new int[weapons.Count];

            // Уже лежащие магазины засчитываются оружию по порядку: один магазин —
            // одному оружию, даже если подходит к нескольким.
            for (int w = 0; w < weapons.Count; w++)
            {
                need[w] = Math.Max(0, wanted(weapons[w]));

                for (int m = 0; m < stored.Count && need[w] > 0; m++)
                {
                    if (assigned[m] || !fits(stored[m], weapons[w])) continue;

                    assigned[m] = true;
                    need[w]--;
                }
            }

            // Недостающее — по кругу, по одному на ствол за проход: если места на все нормы не
            // хватит, срез хвоста ниже убирает поровну, а не всю норму последнего ствола. Раньше
            // норма набиралась стволом подряд, и первый (в руке во время закупки) забирал весь
            // карман — второй оставался без магазинов.
            for (bool added = true; added;)
            {
                added = false;
                for (int w = 0; w < weapons.Count; w++)
                {
                    if (need[w] <= 0) continue;
                    spawnFor.Add(w);
                    need[w]--;
                    added = true;
                }
            }

            // Места не хватает — сначала уходят магазины, не нужные ни одному оружию
            // (старые первыми), потом урезается выдача.
            int overflow = stored.Count + spawnFor.Count - Math.Max(0, capacity);

            for (int m = 0; m < stored.Count && overflow > 0; m++)
            {
                if (assigned[m]) continue;

                discard.Add(m);
                overflow--;
            }

            if (overflow > 0)
                spawnFor.RemoveRange(Math.Max(0, spawnFor.Count - overflow), Math.Min(overflow, spawnFor.Count));

            return new Plan(spawnFor, discard);
        }
    }
}
