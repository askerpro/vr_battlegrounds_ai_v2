using System;
using UnityEngine;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Чистая математика расстановки кукол: выбор скина, ряды перед игроком, кольцо по карте.
    /// Без сцены и сети — проверяется юнит-тестами (<c>PerfLogicTests</c>).
    /// Все смещения — в системе координат стартового корня инициатора: +Z — куда он смотрел.
    /// </summary>
    public static class StressTestLayout
    {
        /// <summary><see cref="StressTestConfig.puppetSkin"/> «все скины вперемешку».</summary>
        public const int MixedSkins = -1;

        /// <summary>
        /// Индекс префаба для куклы <paramref name="puppetIndex"/> в списке из
        /// <paramref name="prefabCount"/> скинов. <paramref name="puppetSkin"/> вне списка
        /// (в том числе <see cref="MixedSkins"/>) — скины чередуются. -1, если скинов нет.
        /// </summary>
        public static int SkinIndex(int prefabCount, int puppetSkin, int puppetIndex)
        {
            if (prefabCount <= 0) return -1;
            if (IsSingleSkin(prefabCount, puppetSkin)) return puppetSkin;
            return ((puppetIndex % prefabCount) + prefabCount) % prefabCount;
        }

        /// <summary>Выбран один конкретный скин, а не смесь.</summary>
        public static bool IsSingleSkin(int prefabCount, int puppetSkin) => puppetSkin >= 0 && puppetSkin < prefabCount;

        /// <summary>
        /// Ряды перед игроком: по <paramref name="perRow"/> в ряду, неполный последний ряд
        /// центрируется. Весь прогон в поле зрения — худший случай для рендера.
        /// </summary>
        public static Vector3 RowOffset(int index, int count, int perRow, float spacing, float firstRowDistance)
        {
            perRow = Mathf.Max(1, perRow);
            int row   = index / perRow;
            int col   = index % perRow;
            int inRow = Mathf.Min(perRow, count - row * perRow);

            return new Vector3((col - (inRow - 1) * 0.5f) * spacing, 0f, firstRowDistance + row * spacing);
        }

        /// <summary>
        /// Кольцо «по карте»: кукла <paramref name="index"/> из <paramref name="count"/> получает
        /// свой сектор 360°/count (со случайным сдвигом всего кольца <paramref name="ringPhaseDeg"/>
        /// и случайным углом внутри сектора), радиус — случайный в
        /// [<paramref name="minRadius"/>, <paramref name="maxRadius"/>], лицом — в случайную сторону.
        ///
        /// <para>
        /// Секторы, а не чистый случай: при 9 куклах и поле зрения ~100° в кадре гарантированно
        /// 1–4 куклы (дуга 100° целиком накрывает минимум один сектор 40° и задевает не больше
        /// четырёх) — чистый случай иногда сваливал бы всех в одну сторону.
        /// </para>
        /// </summary>
        public static void RingPlacement(int index, int count, float minRadius, float maxRadius, float ringPhaseDeg,
                                         System.Random rng, out Vector3 offset, out float yawDeg)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            count = Mathf.Max(1, count);

            float sector = 360f / count;
            float angle  = ringPhaseDeg + (index + (float)rng.NextDouble()) * sector;
            float radius = Mathf.Lerp(Mathf.Min(minRadius, maxRadius), Mathf.Max(minRadius, maxRadius), (float)rng.NextDouble());

            offset = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, radius);
            yawDeg = (float)rng.NextDouble() * 360f;
        }

        /// <summary>
        /// Радиусы, которые пробуются по очереди, если на исходном нет пола (точка за стеной
        /// или за краем карты): от <paramref name="radius"/> к <paramref name="minRadius"/>,
        /// шагом ×0,75. Первый всегда исходный.
        /// </summary>
        public static int FallbackRadii(float radius, float minRadius, float[] buffer)
        {
            if (buffer == null || buffer.Length == 0) return 0;

            int n = 0;
            float r = radius;
            buffer[n++] = r;
            while (n < buffer.Length)
            {
                r *= 0.75f;
                if (r < minRadius) break;
                buffer[n++] = r;
            }
            return n;
        }
    }
}
