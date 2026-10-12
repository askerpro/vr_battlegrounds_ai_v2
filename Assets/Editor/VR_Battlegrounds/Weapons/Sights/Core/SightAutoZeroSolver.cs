using System;
using UnityEngine;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Weapons.Sights
{
    /// <summary>Вход решателя: мировые точки прицела, ось хода подвижной детали и поза ShotSource.</summary>
    public struct SightAutoZeroInput
    {
        /// <summary>Мировая точка R (центр отверстия/прорези) с текущей поправкой.</summary>
        public Vector3 Rear;
        /// <summary>Мировая точка F (вершина мушки) с текущей поправкой.</summary>
        public Vector3 Front;
        /// <summary>Какая половина подвижна.</summary>
        public SightPartRole MovingRole;
        /// <summary>Подвижной детали нет (PPK): только измерение ошибки, без сдвига.</summary>
        public bool MeasureOnly;
        /// <summary>Мировое смещение подвижной детали на одну локальную единицу хода (TransformVector её локальной Y).</summary>
        public Vector3 MoveAxisPerUnit;
        /// <summary>Мировые up деталей целика и мушки: проверка, что прицел стоит вертикально.</summary>
        public Vector3 RearUp, FrontUp;
        public Vector3 MuzzlePosition, MuzzleForward, MuzzleUp;
        /// <summary>Дистанция пристрелки Z, м, (0, 20].</summary>
        public double ZeroDistance;
        /// <summary>
        /// Текущий сдвиг подвижной детали и её ход — в локальных единицах родителя детали. Комплект стоит с мировым
        /// масштабом 1 (SightKitScale), поэтому это метры мира; иначе пересчёт идёт через длину MoveAxisPerUnit.
        /// </summary>
        public double CurrentOffset, TravelDown, TravelUp;
    }

    /// <summary>
    /// Ошибка на плоскости дистанции d в системе ShotSource: e(d) = Intercept + Slope·d (м), вертикаль — по up,
    /// горизонталь — по right ShotSource. После сведения вертикаль: e(d) = h·(1 − d/Z), h = Intercept.
    /// </summary>
    [Serializable]
    public struct SightLineError
    {
        public double InterceptVertical, InterceptHorizontal, SlopeVertical, SlopeHorizontal;
        public double VerticalAt(double d) => InterceptVertical + SlopeVertical * d;
        public double HorizontalAt(double d) => InterceptHorizontal + SlopeHorizontal * d;
        public double TotalAt(double d) => Math.Sqrt(VerticalAt(d) * VerticalAt(d) + HorizontalAt(d) * HorizontalAt(d));
    }

    [Serializable]
    public sealed class SightAutoZeroResult
    {
        /// <summary>Сведение возможно: сдвиг в пределах хода, геометрия корректна. Иначе писать нельзя.</summary>
        public bool Ok;
        public string Error;
        /// <summary>Требуемое изменение сдвига (локальные единицы) и итоговый сдвиг подвижной детали.</summary>
        public double RequiredDelta, RequiredOffset;
        /// <summary>Мировое перемещение подвижной детали вдоль её оси, м.</summary>
        public double RequiredWorldShift;
        public double TravelDown, TravelUp;
        public SightLineError Before, After;
        public double ZeroDistance;
    }

    /// <summary>
    /// Автосведение механического прицела только по вертикали (решения 2026-10-12): одна подвижная деталь
    /// двигается по своей оси так, чтобы линия R→F пересекла плоскость дистанции Z в точке, вертикально совпадающей
    /// с T = ShotSource + forward·Z. Горизонталь не исправляется — только измеряется. Чистая double-геометрия:
    /// Transform и ассеты не читает и не пишет.
    /// </summary>
    public static class SightAutoZeroSolver
    {
        public const double MaxTiltDegrees = 5.0;
        public static readonly double[] ReportDistances = { 5, 10, 15, 20 };

        public static SightAutoZeroResult Solve(SightAutoZeroInput input)
        {
            var result = new SightAutoZeroResult
            {
                ZeroDistance = input.ZeroDistance, TravelDown = input.TravelDown, TravelUp = input.TravelUp,
                RequiredOffset = input.CurrentOffset
            };
            if (!WeaponSightCalibrationSettings.IsValidDistance((float)input.ZeroDistance))
                return Fail(result, $"Дистанция пристрелки вне (0, {WeaponSightCalibrationSettings.ArenaMaxDistance}] м: {input.ZeroDistance}");
            if (!Finite(input.Rear) || !Finite(input.Front) || !Finite(input.MoveAxisPerUnit) || !Finite(input.MuzzlePosition)
                || !Finite(input.MuzzleForward) || !Finite(input.MuzzleUp) || !Finite(input.RearUp) || !Finite(input.FrontUp))
                return Fail(result, "Нечисловые входные данные");
            if (input.TravelDown < 0 || input.TravelUp < 0 || double.IsNaN(input.CurrentOffset))
                return Fail(result, "Отрицательный ход или нечисловой текущий сдвиг");

            // Ортонормированная система ShotSource: u — вперёд, v — вверх (перпендикуляр к u), r — вправо.
            D3 u = D3.Of(input.MuzzleForward);
            if (u.Length < 1e-9) return Fail(result, "Нулевой forward у ShotSource");
            u = u / u.Length;
            D3 v = D3.Of(input.MuzzleUp);
            v = v - u * D3.Dot(v, u);
            if (v.Length < 1e-9) return Fail(result, "up ShotSource параллелен forward");
            v = v / v.Length;
            D3 r = D3.Cross(v, u); // В левой системе Unity Cross(up, forward) = right.

            D3 axis = D3.Of(input.MoveAxisPerUnit);
            if (!input.MeasureOnly && axis.Length < 1e-12) return Fail(result, "Нулевая ось хода подвижной детали");
            string tilt = (input.MeasureOnly ? null : Tilt("ось хода подвижной детали", axis, v)) ?? Tilt("up целика", D3.Of(input.RearUp), v)
                ?? Tilt("up мушки", D3.Of(input.FrontUp), v);
            if (tilt != null) return Fail(result, tilt);

            D3 m = D3.Of(input.MuzzlePosition), rear = D3.Of(input.Rear) - m, front = D3.Of(input.Front) - m;
            double z = input.ZeroDistance;
            if (!(D3.Dot(front - rear, u) > 1e-6))
                return Fail(result, "Мушка F не впереди целика R вдоль оси выстрела");
            if (!(z > D3.Dot(front, u)))
                return Fail(result, "Плоскость пристрелки должна быть дальше мушки");
            result.Before = LineError(rear, front, u, v, r);
            if (input.MeasureOnly)
            {
                result.After = result.Before;
                return Fail(result, "Подвижной детали нет: «Свести» только показывает ошибку");
            }

            // Условие коллинеарности R, F, T в плоскости (u, v) линейно по сдвигу любой одной точки:
            // g(δ) = (Fu − Ru)·(−Rv) − (Fv − Rv)·(Z − Ru); квадратичные члены сокращаются.
            bool movesFront = input.MovingRole == SightPartRole.Front;
            double g0 = Collinearity(rear, front, axis, 0, movesFront, u, v, z);
            double g1 = Collinearity(rear, front, axis, 1, movesFront, u, v, z);
            double slope = g1 - g0;
            if (Math.Abs(slope) < 1e-15) return Fail(result, "Ось хода не меняет высоту линии прицела на дистанции Z");
            double delta = -g0 / slope;
            result.RequiredDelta = delta;
            result.RequiredOffset = input.CurrentOffset + delta;
            result.RequiredWorldShift = delta * axis.Length;

            D3 newRear = movesFront ? rear : rear + axis * delta;
            D3 newFront = movesFront ? front + axis * delta : front;
            if (!(D3.Dot(newFront - newRear, u) > 1e-6))
                return Fail(result, "После сдвига мушка оказывается не впереди целика");
            result.After = LineError(newRear, newFront, u, v, r);

            const double eps = 1e-9;
            if (result.RequiredOffset < -input.TravelDown - eps || result.RequiredOffset > input.TravelUp + eps)
                return Fail(result, $"Не хватает хода: нужен сдвиг {Mm(result.RequiredOffset)} мм, доступно от " +
                                    $"{Mm(-input.TravelDown)} до {Mm(input.TravelUp)} мм");
            result.Ok = true;
            return result;
        }

        /// <summary>Ошибка линии R→F относительно оси выстрела на дистанции d: точка на плоскости d минус M + d·u.</summary>
        private static SightLineError LineError(D3 rear, D3 front, D3 u, D3 v, D3 r)
        {
            D3 dir = front - rear;
            double du = D3.Dot(dir, u);
            // P(d) = rear + dir·(d − rear·u)/du; поперечная часть линейна по d.
            D3 slope = dir / du;
            D3 intercept = rear - slope * D3.Dot(rear, u);
            return new SightLineError
            {
                InterceptVertical = D3.Dot(intercept, v), InterceptHorizontal = D3.Dot(intercept, r),
                SlopeVertical = D3.Dot(slope, v), SlopeHorizontal = D3.Dot(slope, r)
            };
        }

        private static double Collinearity(D3 rear, D3 front, D3 axis, double delta, bool movesFront, D3 u, D3 v, double z)
        {
            D3 a = movesFront ? rear : rear + axis * delta;
            D3 b = movesFront ? front + axis * delta : front;
            double au = D3.Dot(a, u), av = D3.Dot(a, v), bu = D3.Dot(b, u), bv = D3.Dot(b, v);
            return (bu - au) * (-av) - (bv - av) * (z - au);
        }

        private static string Tilt(string name, D3 axis, D3 up)
        {
            if (axis.Length < 1e-12) return $"Нулевая {name}";
            double cos = D3.Dot(axis, up) / axis.Length;
            double angle = Math.Acos(Math.Max(-1, Math.Min(1, cos))) * 180 / Math.PI;
            return angle > MaxTiltDegrees
                ? $"Наклон: {name} отклонена от up ShotSource на {angle:0.0}° (допустимо {MaxTiltDegrees}°)" : null;
        }

        private static SightAutoZeroResult Fail(SightAutoZeroResult result, string error)
        {
            result.Ok = false;
            result.Error = error;
            return result;
        }

        private static bool Finite(Vector3 p) => !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)
            || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z));

        public static string Mm(double meters) => (meters * 1000).ToString("+0.00;-0.00;0.00");

        private readonly struct D3
        {
            public readonly double X, Y, Z;
            public D3(double x, double y, double z) { X = x; Y = y; Z = z; }
            public static D3 Of(Vector3 p) => new D3(p.x, p.y, p.z);
            public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
            public static D3 operator +(D3 a, D3 b) => new D3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static D3 operator -(D3 a, D3 b) => new D3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static D3 operator *(D3 a, double k) => new D3(a.X * k, a.Y * k, a.Z * k);
            public static D3 operator /(D3 a, double k) => new D3(a.X / k, a.Y / k, a.Z / k);
            public static double Dot(D3 a, D3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            public static D3 Cross(D3 a, D3 b) => new D3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }
    }
}
