using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Maps
{
    /// <summary>Грань лазерной сетки зоны в её осях: <c>Back</c> — +Z, <c>Front</c> — −Z, <c>Right</c> — +X, <c>Left</c> — −X.</summary>
    public enum LaserGridFace
    {
        Back = 0,
        Front = 1,
        Right = 2,
        Left = 3
    }

    public enum LaserGridScreenKind
    {
        /// <summary>Общее табло — по одному на каждую из 4 граней сетки.</summary>
        Common,
        /// <summary>Персональный кусок — напротив стены арсенала игрока, на грани, противоположной заслонённой стеной.</summary>
        Personal
    }

    /// <summary>
    /// Коробка лазерной сетки в мире: центр в плане, поворот вокруг вертикали, половины размеров
    /// в плане, пол и верх. Масштаб уже учтён — это метры мира.
    /// </summary>
    public readonly struct LaserGridBox
    {
        public readonly Vector3 Center;
        public readonly float Yaw;
        public readonly float HalfWidth;
        public readonly float HalfDepth;
        public readonly float FloorY;
        public readonly float TopY;

        public LaserGridBox(Vector3 center, float yaw, float halfWidth, float halfDepth, float floorY, float topY)
        {
            Center = center;
            Yaw = yaw;
            HalfWidth = Mathf.Abs(halfWidth);
            HalfDepth = Mathf.Abs(halfDepth);
            FloorY = floorY;
            TopY = topY;
        }

        /// <summary>Поворот вокруг вертикали. Считается вручную, без нативных вызовов Unity, — под юнит-тест вне редактора.</summary>
        public Quaternion Rotation => LaserGridLayout.YawRotation(Yaw);
        public Vector3 Right => new Vector3(Mathf.Cos(Yaw * Mathf.Deg2Rad), 0f, -Mathf.Sin(Yaw * Mathf.Deg2Rad));
        public Vector3 Forward => new Vector3(Mathf.Sin(Yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(Yaw * Mathf.Deg2Rad));

        /// <summary>Точка мира в плане коробки: x — вдоль <see cref="Right"/>, y — вдоль <see cref="Forward"/>.</summary>
        public Vector2 ToPlan(Vector3 world)
        {
            Vector3 d = world - Center;
            return new Vector2(Vector3.Dot(d, Right), Vector3.Dot(d, Forward));
        }
    }

    /// <summary>Стена арсенала глазами раскладки: где стоит, сколько занимает вдоль грани, где её верх.</summary>
    public readonly struct LaserGridWall
    {
        public readonly int Id;
        public readonly Vector3 Center;
        public readonly float HalfWidth;
        public readonly float TopY;

        public LaserGridWall(int id, Vector3 center, float halfWidth, float topY)
        {
            Id = id;
            Center = center;
            HalfWidth = Mathf.Abs(halfWidth);
            TopY = topY;
        }
    }

    /// <summary>Где стоит одно табло: грань, положение вдоль неё, высота центра, размер и поза в мире.</summary>
    public readonly struct LaserGridScreenPose
    {
        public readonly LaserGridScreenKind Kind;
        /// <summary>Стена персонального куска; −1 у общего табло.</summary>
        public readonly int WallId;
        public readonly LaserGridFace Face;
        /// <summary>Центр вдоль грани, м от её середины.</summary>
        public readonly float Along;
        public readonly float CenterY;
        public readonly float Width;
        public readonly float Height;
        public readonly Vector3 Position;
        /// <summary>Вперёд (+Z) — наружу из зоны: текст TMP читается изнутри.</summary>
        public readonly Quaternion Rotation;

        public LaserGridScreenPose(LaserGridScreenKind kind, int wallId, LaserGridFace face, float along, float centerY,
                                   float width, float height, Vector3 position, Quaternion rotation)
        {
            Kind = kind;
            WallId = wallId;
            Face = face;
            Along = along;
            CenterY = centerY;
            Width = width;
            Height = height;
            Position = position;
            Rotation = rotation;
        }

        public float MinAlong => Along - Width * 0.5f;
        public float MaxAlong => Along + Width * 0.5f;
        public float Bottom => CenterY - Height * 0.5f;
        public float Top => CenterY + Height * 0.5f;
    }

    /// <summary>
    /// Раскладка табло по лазерной сетке зоны спавна (T-47) — чистая геометрия, без сцены и без нативных
    /// вызовов Unity (тестируется и вне редактора).
    ///
    /// <para>
    /// <b>Стены арсенала заслоняют свою грань.</b> Стена стоит у ближайшей к ней грани сетки, а на картах она
    /// выше самой сетки (4 м против 3,7): грань за стенами не видно. Поэтому стена — препятствие своей грани, и
    /// табло этой грани встают только мимо стен (или над ними, если сетка выше стен).
    /// </para>
    ///
    /// <para>
    /// <b>Персональный кусок</b> — на противоположной грани, напротив своей стены: зона делится на дорожки
    /// «стена → кусок сетки», игрок покупает лицом к стене и, развернувшись к арене, видит свой отсчёт.
    /// Куски одной грани — одним рядом на уровне глаз. <b>Общие табло</b> — по одному на каждую из 4 граней:
    /// по центру над головой, а на грани с рядом кусков или стенами — в свободном промежутке ближе к середине;
    /// нет промежутка — над препятствиями; нет места и над ними — в самом широком промежутке, сузившись.
    /// Всё — с отступом внутрь зоны, чтобы не спорить по глубине с лазерами.
    /// </para>
    /// </summary>
    public static class LaserGridLayout
    {
        public const float PersonalWidth = 1.2f;
        public const float PersonalHeight = 0.6f;
        public const float CommonWidth = 1.6f;
        public const float CommonHeight = 0.85f;

        /// <summary>Самое узкое общее табло, когда промежутки тесные.</summary>
        public const float MinCommonWidth = 0.6f;

        /// <summary>Зазор между табло, от края грани и от стены.</summary>
        public const float Margin = 0.1f;

        /// <summary>Зазор между верхом стены и низом табло над ней.</summary>
        public const float GapAboveWall = 0.1f;

        /// <summary>Центр общего табло на свободной грани — чуть выше головы: не закрывает вид наружу.</summary>
        public const float FreeCenterAboveFloor = 2.3f;

        /// <summary>Центр ряда персональных кусков — на уровне глаз: свой отсчёт читается, не задирая головы.</summary>
        public const float PersonalCenterAboveFloor = 1.8f;

        /// <summary>Отступ внутрь от плоскости сетки.</summary>
        public const float Inset = 0.03f;

        public static readonly LaserGridFace[] Faces =
            { LaserGridFace.Back, LaserGridFace.Front, LaserGridFace.Right, LaserGridFace.Left };

        /// <summary>Наружная нормаль грани в мире.</summary>
        public static Vector3 Outward(LaserGridBox box, LaserGridFace face)
        {
            switch (face)
            {
                case LaserGridFace.Back: return box.Forward;
                case LaserGridFace.Front: return -box.Forward;
                case LaserGridFace.Right: return box.Right;
                default: return -box.Right;
            }
        }

        /// <summary>Противоположная грань.</summary>
        public static LaserGridFace Opposite(LaserGridFace face)
        {
            switch (face)
            {
                case LaserGridFace.Back: return LaserGridFace.Front;
                case LaserGridFace.Front: return LaserGridFace.Back;
                case LaserGridFace.Right: return LaserGridFace.Left;
                default: return LaserGridFace.Right;
            }
        }

        /// <summary>Ось «вдоль грани» в мире (у противоположных граней — общая).</summary>
        public static Vector3 Tangent(LaserGridBox box, LaserGridFace face) =>
            face == LaserGridFace.Back || face == LaserGridFace.Front ? box.Right : box.Forward;

        /// <summary>Половина длины грани.</summary>
        public static float HalfLength(LaserGridBox box, LaserGridFace face) =>
            face == LaserGridFace.Back || face == LaserGridFace.Front ? box.HalfWidth : box.HalfDepth;

        private static float HalfThickness(LaserGridBox box, LaserGridFace face) =>
            face == LaserGridFace.Back || face == LaserGridFace.Front ? box.HalfDepth : box.HalfWidth;

        /// <summary>Координата точки вдоль грани.</summary>
        public static float AlongFace(LaserGridBox box, LaserGridFace face, Vector3 world)
        {
            Vector2 plan = box.ToPlan(world);
            return face == LaserGridFace.Back || face == LaserGridFace.Front ? plan.x : plan.y;
        }

        /// <summary>Грань, к которой точка ближе всего в плане.</summary>
        public static LaserGridFace NearestFace(LaserGridBox box, Vector3 world)
        {
            Vector2 p = box.ToPlan(world);
            LaserGridFace best = LaserGridFace.Back;
            float bestDistance = box.HalfDepth - p.y;

            Consider(LaserGridFace.Front, box.HalfDepth + p.y, ref best, ref bestDistance);
            Consider(LaserGridFace.Right, box.HalfWidth - p.x, ref best, ref bestDistance);
            Consider(LaserGridFace.Left, box.HalfWidth + p.x, ref best, ref bestDistance);
            return best;
        }

        private static void Consider(LaserGridFace face, float distance, ref LaserGridFace best, ref float bestDistance)
        {
            if (distance >= bestDistance) return;
            best = face;
            bestDistance = distance;
        }

        /// <summary>Грань персонального куска стены — напротив грани, которую стена заслоняет.</summary>
        public static LaserGridFace PersonalFace(LaserGridBox box, LaserGridWall wall) => Opposite(NearestFace(box, wall.Center));

        /// <summary>Поза табло в мире по грани, положению вдоль неё и высоте центра.</summary>
        public static Vector3 PositionOn(LaserGridBox box, LaserGridFace face, float along, float centerY)
        {
            Vector3 outward = Outward(box, face);
            Vector3 p = box.Center + outward * (HalfThickness(box, face) - Inset) + Tangent(box, face) * along;
            p.y = centerY;
            return p;
        }

        /// <summary>Отрезок грани, занятый снизу до <see cref="Top"/>: стена или табло.</summary>
        private readonly struct Span
        {
            public readonly float Min, Max, Top;
            public Span(float min, float max, float top) { Min = min; Max = max; Top = top; }
        }

        /// <summary>
        /// Все табло зоны: 4 общих (по грани) и по персональному на каждую стену. Порядок: сначала
        /// персональные в порядке стен, потом общие в порядке <see cref="Faces"/>.
        /// </summary>
        public static List<LaserGridScreenPose> Build(LaserGridBox box, IReadOnlyList<LaserGridWall> walls)
        {
            var result = new List<LaserGridScreenPose>();
            var obstacles = new Dictionary<LaserGridFace, List<Span>>();
            var personalWalls = new Dictionary<LaserGridFace, List<LaserGridWall>>();
            var rows = new Dictionary<LaserGridFace, List<Span>>();
            foreach (LaserGridFace face in Faces)
            {
                obstacles[face] = new List<Span>();
                personalWalls[face] = new List<LaserGridWall>();
                rows[face] = new List<Span>();
            }

            if (walls != null)
            {
                foreach (LaserGridWall wall in walls)
                {
                    LaserGridFace near = NearestFace(box, wall.Center);
                    float u = AlongFace(box, near, wall.Center);
                    obstacles[near].Add(new Span(u - wall.HalfWidth, u + wall.HalfWidth, wall.TopY));
                    personalWalls[Opposite(near)].Add(wall);
                }

                // Персональные: ряд напротив своих стен, на уровне глаз, над препятствиями своей грани.
                foreach (LaserGridWall wall in walls)
                {
                    LaserGridFace face = PersonalFace(box, wall);
                    float halfLength = HalfLength(box, face);
                    float width = Mathf.Min(PersonalWidth, MinSpacing(box, face, personalWalls[face]) - Margin,
                                            2f * (halfLength - Margin));
                    float along = ClampAlong(AlongFace(box, face, wall.Center), width, halfLength);
                    float centerY = ClampHeight(box, box.FloorY + PersonalCenterAboveFloor, PersonalHeight);
                    centerY = LiftAbove(box, obstacles[face], along - width * 0.5f, along + width * 0.5f, centerY, PersonalHeight);

                    LaserGridScreenPose pose = Pose(box, LaserGridScreenKind.Personal, wall.Id, face, along, centerY, width, PersonalHeight);
                    result.Add(pose);
                    rows[face].Add(new Span(pose.MinAlong, pose.MaxAlong, pose.Top));
                }
            }

            // Общие: по одному на грань, мимо ряда кусков и стен.
            foreach (LaserGridFace face in Faces)
            {
                float halfLength = HalfLength(box, face);
                float width = Mathf.Min(CommonWidth, 2f * (halfLength - Margin));
                bool hasRow = rows[face].Count > 0;
                float centerY = ClampHeight(box, box.FloorY + (hasRow ? PersonalCenterAboveFloor : FreeCenterAboveFloor), CommonHeight);
                float bottom = centerY - CommonHeight * 0.5f;

                var blocked = new List<Span>(rows[face]);
                foreach (Span wall in obstacles[face])
                    if (wall.Top + GapAboveWall > bottom) blocked.Add(wall);

                if (!TryFindGap(blocked, width, halfLength, out float along))
                {
                    float above = box.FloorY;
                    foreach (Span span in blocked) above = Mathf.Max(above, span.Top);
                    float liftedY = above + GapAboveWall + CommonHeight * 0.5f;

                    if (liftedY + CommonHeight * 0.5f <= box.TopY - Margin)
                    {
                        along = 0f;
                        centerY = liftedY;
                    }
                    else if (TryWidestGap(blocked, halfLength, out float gapAlong, out float gapWidth) && gapWidth >= MinCommonWidth)
                    {
                        along = gapAlong;
                        width = Mathf.Min(width, gapWidth);
                    }
                    else
                    {
                        along = 0f;
                    }
                }

                result.Add(Pose(box, LaserGridScreenKind.Common, -1, face, along, centerY, width, CommonHeight));
            }

            return result;
        }

        /// <summary>Поднимает табло над препятствиями грани, которые оно задевает по длине (если сетка позволяет).</summary>
        private static float LiftAbove(LaserGridBox box, List<Span> obstacles, float min, float max, float centerY, float height)
        {
            float top = float.NegativeInfinity;
            foreach (Span span in obstacles)
                if (span.Max > min && span.Min < max) top = Mathf.Max(top, span.Top);

            if (float.IsNegativeInfinity(top) || centerY - height * 0.5f >= top + GapAboveWall) return centerY;
            return ClampHeight(box, top + GapAboveWall + height * 0.5f, height);
        }

        private static float ClampHeight(LaserGridBox box, float centerY, float height)
        {
            float max = box.TopY - Margin - height * 0.5f;
            float min = box.FloorY + height * 0.5f;
            return max < min ? min : Mathf.Clamp(centerY, min, max);
        }

        private static float ClampAlong(float along, float width, float halfLength)
        {
            float limit = halfLength - Margin - width * 0.5f;
            return limit <= 0f ? 0f : Mathf.Clamp(along, -limit, limit);
        }

        /// <summary>Наименьшее расстояние между центрами соседних стен грани (одна стена — бесконечность).</summary>
        private static float MinSpacing(LaserGridBox box, LaserGridFace face, List<LaserGridWall> walls)
        {
            var positions = new List<float>(walls.Count);
            foreach (LaserGridWall wall in walls) positions.Add(AlongFace(box, face, wall.Center));
            positions.Sort();

            float min = float.PositiveInfinity;
            for (int i = 1; i < positions.Count; i++) min = Mathf.Min(min, positions[i] - positions[i - 1]);
            return min;
        }

        /// <summary>Свободные промежутки грани между занятыми отрезками (с зазорами), слева направо.</summary>
        private static List<Vector2> FreeIntervals(List<Span> taken, float halfLength)
        {
            var sorted = new List<Span>(taken);
            sorted.Sort((a, b) => a.Min.CompareTo(b.Min));

            var free = new List<Vector2>();
            float start = -halfLength + Margin;
            foreach (Span span in sorted)
            {
                float end = span.Min - Margin;
                if (end > start) free.Add(new Vector2(start, end));
                start = Mathf.Max(start, span.Max + Margin);
            }
            float last = halfLength - Margin;
            if (last > start) free.Add(new Vector2(start, last));
            return free;
        }

        /// <summary>Промежуток под табло ширины <paramref name="width"/>, ближайший к середине грани.</summary>
        private static bool TryFindGap(List<Span> taken, float width, float halfLength, out float along)
        {
            along = 0f;
            float best = float.PositiveInfinity;
            foreach (Vector2 gap in FreeIntervals(taken, halfLength))
            {
                if (gap.y - gap.x < width) continue;
                float candidate = Mathf.Clamp(0f, gap.x + width * 0.5f, gap.y - width * 0.5f);
                if (Mathf.Abs(candidate) >= Mathf.Abs(best)) continue;
                best = candidate;
                along = candidate;
            }
            return !float.IsPositiveInfinity(best);
        }

        /// <summary>Самый широкий свободный промежуток грани.</summary>
        private static bool TryWidestGap(List<Span> taken, float halfLength, out float along, out float width)
        {
            along = 0f;
            width = 0f;
            foreach (Vector2 gap in FreeIntervals(taken, halfLength))
            {
                if (gap.y - gap.x <= width) continue;
                width = gap.y - gap.x;
                along = (gap.x + gap.y) * 0.5f;
            }
            return width > 0f;
        }

        /// <summary>Поворот на <paramref name="yaw"/> градусов вокруг вертикали (как <c>Quaternion.Euler(0, yaw, 0)</c>).</summary>
        public static Quaternion YawRotation(float yaw)
        {
            float half = yaw * Mathf.Deg2Rad * 0.5f;
            return new Quaternion(0f, Mathf.Sin(half), 0f, Mathf.Cos(half));
        }

        /// <summary>Поворот, при котором +Z смотрит вдоль горизонтального <paramref name="direction"/>.</summary>
        public static Quaternion FacingRotation(Vector3 direction) =>
            YawRotation(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);

        private static LaserGridScreenPose Pose(LaserGridBox box, LaserGridScreenKind kind, int wallId, LaserGridFace face,
                                                float along, float centerY, float width, float height)
        {
            return new LaserGridScreenPose(kind, wallId, face, along, centerY, width, height,
                PositionOn(box, face, along, centerY),
                FacingRotation(Outward(box, face)));
        }
    }
}
