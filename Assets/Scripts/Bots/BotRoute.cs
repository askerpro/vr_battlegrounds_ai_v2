using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Шаг по ломаной маршрута (T-48) — чистая функция, тесты <c>BotRouteTests</c>. Маршрут — углы пути NavMesh
    /// (<see cref="BotNavMesh"/>) или одна точка назначения, если сетки нет.
    /// </summary>
    public static class BotRoute
    {
        /// <summary>Ближе этого к углу — угол пройден.</summary>
        public const float CornerReach = 0.05f;

        /// <summary>
        /// Сдвигает <paramref name="position"/> вдоль углов на <paramref name="distance"/>, начиная с угла
        /// <paramref name="next"/>; пройденные углы пропускаются, остаток шага уходит на следующий отрезок.
        /// </summary>
        /// <param name="arrived">Дошли до последнего угла.</param>
        /// <returns>Новая позиция.</returns>
        public static Vector3 Step(Vector3 position, IReadOnlyList<Vector3> corners, ref int next, float distance, out bool arrived)
        {
            if (corners == null || corners.Count == 0)
            {
                arrived = true;
                return position;
            }

            if (next < 0) next = 0;
            float left = Mathf.Max(0f, distance);

            while (next < corners.Count)
            {
                Vector3 target = corners[next];
                float toCorner = Vector3.Distance(position, target);

                if (toCorner <= left || toCorner <= CornerReach)
                {
                    // Угол достигнут: остаток шага — на следующий отрезок.
                    position = target;
                    left = Mathf.Max(0f, left - toCorner);
                    next++;
                    continue;
                }

                arrived = false;
                return position + (target - position) / toCorner * left;
            }

            arrived = true;
            return position;
        }
    }

    /// <summary>
    /// Место бота на базе: точки вокруг центра зоны спавна, чтобы боты одной команды не стояли друг в друге
    /// (T-48). Чистая функция, тесты <c>BotRouteTests</c>.
    /// </summary>
    public static class BotHomeSlot
    {
        /// <summary>Шаг между местами, метры.</summary>
        public const float Spacing = 0.8f;

        /// <summary>Смещение места <paramref name="slot"/> от центра зоны в плоскости XZ: 0 — центр, дальше — кольцом.</summary>
        public static Vector3 Offset(int slot)
        {
            if (slot <= 0) return Vector3.zero;

            // Первое кольцо — 6 мест на шаге от центра, второе — 12 на двух шагах (дальше — по второму кругу).
            int ring = slot <= 6 ? 1 : 2;
            int count = ring == 1 ? 6 : 12;
            int index = ring == 1 ? slot - 1 : (slot - 7) % count;

            float angle = index * (2f * Mathf.PI / count);
            float radius = Spacing * ring;
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }
    }
}
