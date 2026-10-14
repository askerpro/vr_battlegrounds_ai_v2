using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Чистая математика расстановки кукол: выбор скина, ряды перед игроком, кольцо по карте, пол арены.
    /// Без сцены и сети — проверяется юнит-тестами (<c>PerfLogicTests</c>).
    /// Смещения рядов и кольца — в системе координат стартового корня инициатора: +Z — куда он смотрел;
    /// места на арене — в мировых X/Z.
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

        // ── Арена: куклы по полу физической арены ───────────────────────────

        /// <summary>
        /// Отступ кукол от стен арены, м: кукла ростом с человека не упирается в стену и не торчит
        /// сквозь неё руками при повторе движений игрока.
        /// </summary>
        public const float ArenaWallMargin = 1.0f;

        /// <summary>
        /// Запас вокруг запретной зоны (препятствие, станция арсенала, место игрока, точка обзора), м: полширины
        /// плеч куклы плюс зазор, чтобы руки не входили в препятствие.
        /// </summary>
        public const float ArenaExclusionMargin = 0.5f;

        /// <summary>
        /// Seed раскладки «арена» по умолчанию: одна и та же арена и число кукол дают одну и ту же
        /// расстановку от прогона к прогону — замеры сравнимы.
        /// </summary>
        public const int ArenaDefaultSeed = 20261013;

        /// <summary>Шаг сетки точек-кандидатов, м: мельче шага кукол в несколько раз — раскладка не «клетчатая».</summary>
        public const float ArenaCandidateStep = 0.25f;

        /// <summary>
        /// Сколько свободных кандидатов сравнивается на каждую куклу (выбор «лучшего из k» по Митчеллу):
        /// больше — ровнее по полу и сильнее тяга к стенам, меньше — случайнее.
        /// </summary>
        public const int ArenaCandidatesPerPuppet = 12;

        /// <summary>Место куклы на полу арены: точка в мировых X/Z и поворот вокруг вертикали.</summary>
        public readonly struct ArenaSpot
        {
            /// <summary>Мировые координаты: <c>x</c> — X, <c>y</c> — Z.</summary>
            public readonly Vector2 Position;
            public readonly float YawDeg;

            public ArenaSpot(Vector2 position, float yawDeg)
            {
                Position = position;
                YawDeg = yawDeg;
            }
        }

        /// <summary>Проекция мировых габаритов на пол: прямоугольник X/Z (<c>Rect.y</c> — мировой Z).</summary>
        public static Rect FloorRect(Bounds bounds) => new Rect(bounds.min.x, bounds.min.z, bounds.size.x, bounds.size.z);

        /// <summary>
        /// Радиус пола вокруг точки обзора без кукол, м: камера обхода не оказывается внутри куклы и не упирается в неё
        /// взглядом.
        /// </summary>
        public const float ViewPointClearance = 1.5f;

        /// <summary>
        /// Запретная зона точки обзора для <see cref="ArenaPlacement"/>: квадрат с центром в
        /// <paramref name="floorXZ"/> (мировые X/Z) и полустороной <see cref="ViewPointClearance"/> минус
        /// <see cref="ArenaExclusionMargin"/> — после запаса раскладки квадрат накрывает весь круг радиуса
        /// <see cref="ViewPointClearance"/>.
        /// </summary>
        public static Rect ViewPointExclusion(Vector2 floorXZ)
        {
            float half = Mathf.Max(0f, ViewPointClearance - ArenaExclusionMargin);
            return new Rect(floorXZ.x - half, floorXZ.y - half, half * 2f, half * 2f);
        }

        /// <summary>
        /// Расстановка кукол по полу арены — без сцены, детерминированно по <paramref name="seed"/>.
        ///
        /// <para>
        /// Кандидаты — сетка с шагом <see cref="ArenaCandidateStep"/> со случайным сдвигом каждой точки внутри
        /// <paramref name="floor"/>, сжатого на <paramref name="wallMargin"/>, кроме точек в запретных зонах
        /// (<paramref name="exclusions"/>, каждая расширена на <paramref name="exclusionMargin"/>). Порядок
        /// кандидатов перемешан. Для каждой куклы берутся первые <see cref="ArenaCandidatesPerPuppet"/> годных
        /// (не ближе <paramref name="spacing"/> к уже поставленным и свободных по <paramref name="isFree"/>), из них
        /// ставится самый далёкий от уже поставленных («лучший из k» Митчелла): куклы расходятся по всему полу,
        /// но без строгой решётки. Поворот — случайный, куклы смотрят в разные стороны.
        /// </para>
        ///
        /// <para>
        /// Жадно: если годного кандидата не осталось, раскладка останавливается — поместилось меньше, чем
        /// просили, наложений нет. Недостачу (<paramref name="count"/> минус результат) сообщает вызывающий.
        /// </para>
        /// </summary>
        /// <param name="floor">Пол арены в мировых X/Z (<see cref="FloorRect"/>).</param>
        /// <param name="isFree">
        /// Дополнительная проверка точки (например, капсула роста свободна от геометрии сцены); null — все свободны.
        /// Вызывается лениво, не больше раза на кандидата.
        /// </param>
        /// <param name="result">Очищается и заполняется местами кукол по порядку.</param>
        /// <returns>Сколько кукол поставлено.</returns>
        public static int ArenaPlacement(Rect floor, float wallMargin, IReadOnlyList<Rect> exclusions, float exclusionMargin,
                                         int count, float spacing, int seed, List<ArenaSpot> result,
                                         Func<Vector2, bool> isFree = null)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            result.Clear();
            if (count <= 0) return 0;

            float margin = Mathf.Max(0f, wallMargin);
            var inner = Rect.MinMaxRect(floor.xMin + margin, floor.yMin + margin, floor.xMax - margin, floor.yMax - margin);
            if (inner.width < 0f || inner.height < 0f) return 0;

            var rng = new System.Random(seed);
            List<Vector2> candidates = ArenaCandidates(inner, exclusions, Mathf.Max(0f, exclusionMargin), rng);

            // Перемешивание Фишера — Йетса: обход по порядку — это и есть случайный выбор без повторов.
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            // 0 — не проверялся, 1 — свободен, 2 — занят геометрией или уже отдан кукле.
            var state = new byte[candidates.Count];
            float minSqr = Mathf.Max(0.01f, spacing) * Mathf.Max(0.01f, spacing);

            for (int k = 0; k < count; k++)
            {
                int best = -1;
                float bestSqr = -1f;
                int seen = 0;

                for (int c = 0; c < candidates.Count && seen < ArenaCandidatesPerPuppet; c++)
                {
                    if (state[c] == 2) continue;

                    float nearestSqr = NearestSqr(candidates[c], result);
                    if (nearestSqr < minSqr) continue;

                    if (state[c] == 0) state[c] = isFree == null || isFree(candidates[c]) ? (byte)1 : (byte)2;
                    if (state[c] == 2) continue;

                    seen++;
                    if (nearestSqr > bestSqr)
                    {
                        bestSqr = nearestSqr;
                        best = c;
                    }
                }

                if (best < 0) break;

                state[best] = 2;
                result.Add(new ArenaSpot(candidates[best], (float)rng.NextDouble() * 360f));
            }

            return result.Count;
        }

        private static List<Vector2> ArenaCandidates(Rect inner, IReadOnlyList<Rect> exclusions, float exclusionMargin, System.Random rng)
        {
            int nx = Mathf.FloorToInt(inner.width / ArenaCandidateStep) + 1;
            int nz = Mathf.FloorToInt(inner.height / ArenaCandidateStep) + 1;
            float jitter = ArenaCandidateStep * 0.4f;
            var candidates = new List<Vector2>(nx * nz);

            for (int ix = 0; ix < nx; ix++)
            {
                for (int iz = 0; iz < nz; iz++)
                {
                    // Сдвиг тянется из rng для каждой точки сетки, даже отброшенной: раскладка зависит только от seed.
                    float dx = ((float)rng.NextDouble() * 2f - 1f) * jitter;
                    float dz = ((float)rng.NextDouble() * 2f - 1f) * jitter;
                    var point = new Vector2(Mathf.Clamp(inner.xMin + ix * ArenaCandidateStep + dx, inner.xMin, inner.xMax),
                                            Mathf.Clamp(inner.yMin + iz * ArenaCandidateStep + dz, inner.yMin, inner.yMax));
                    if (!InsideAny(point, exclusions, exclusionMargin)) candidates.Add(point);
                }
            }
            return candidates;
        }

        private static bool InsideAny(Vector2 point, IReadOnlyList<Rect> exclusions, float margin)
        {
            if (exclusions == null) return false;
            for (int i = 0; i < exclusions.Count; i++)
            {
                Rect r = exclusions[i];
                if (point.x >= r.xMin - margin && point.x <= r.xMax + margin &&
                    point.y >= r.yMin - margin && point.y <= r.yMax + margin) return true;
            }
            return false;
        }

        private static float NearestSqr(Vector2 point, List<ArenaSpot> placed)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < placed.Count; i++)
            {
                float d = (placed[i].Position - point).sqrMagnitude;
                if (d < nearest) nearest = d;
            }
            return nearest;
        }
    }
}
