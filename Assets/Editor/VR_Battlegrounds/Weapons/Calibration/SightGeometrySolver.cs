using System;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Weapons.Calibration
{
    /// <summary>Независимая double-геометрия. Не выбирает цель, не пишет ассеты, не разрешает физические DOF.</summary>
    public static class SightGeometrySolver
    {
        [Serializable]
        public struct Point
        {
            public double x, y, z;
            public Point(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
            public double Length => Math.Sqrt(Dot(this, this));
            public static Point operator +(Point a, Point b) => new Point(a.x+b.x, a.y+b.y, a.z+b.z);
            public static Point operator -(Point a, Point b) => new Point(a.x-b.x, a.y-b.y, a.z-b.z);
            public static Point operator *(Point a, double k) => new Point(a.x*k, a.y*k, a.z*k);
            public static Point operator /(Point a, double k) => a*(1/k);
            public static double Dot(Point a, Point b) => a.x*b.x+a.y*b.y+a.z*b.z;
            public bool IsFinite => !(double.IsNaN(x)||double.IsNaN(y)||double.IsNaN(z)||double.IsInfinity(x)||double.IsInfinity(y)||double.IsInfinity(z));
        }

        [Serializable]
        public sealed class Evaluation
        {
            public double zeroDistance;
            public double zeroResidualMeters;
            public double nearOffsetMeters;
            public double errorAt20Meters;
            public double supremum0To20Meters;
            public Point transverseIntercept;
            public Point transverseSlope;
            public Point proposedFrontPoint;
            public Point proposedFrontTranslation;
            public double proposedZeroResidualMeters;
            public string status = "NeedsReferenceAndMechanismReview";
            public bool physicalApproval;
        }

        public static Evaluation Evaluate(Point muzzle, Point shotDirection, Point rear, Point front, double zeroDistance)
        {
            Validate(muzzle); Validate(shotDirection); Validate(rear); Validate(front);
            if (!(zeroDistance > 0 && zeroDistance <= WeaponSightCalibrationSettings.ArenaMaxDistance))
                throw new ArgumentOutOfRangeException(nameof(zeroDistance));
            if (shotDirection.Length == 0) throw new ArgumentException("Zero shot direction");
            Point u = shotDirection / shotDirection.Length;
            Point v = front - rear;
            double longitudinal = Point.Dot(v, u);
            if (!(longitudinal > 0)) throw new ArgumentException("Sight reference is inverted or parallel to the target plane");
            Point delta = rear - muzzle;
            double rearDepth = Point.Dot(delta, u);
            Point slope = (v - u * longitudinal) / longitudinal;
            Point intercept = delta - u * rearDepth - slope * rearDepth;
            Point target = muzzle + u * zeroDistance;
            double targetDepth = Point.Dot(target - rear, u);
            if (!(targetDepth > longitudinal)) throw new ArgumentException("Zero plane must be beyond the front sight");
            // Сохраняем продольную станцию F. Результат — только геометрическое предложение.
            Point candidateFront = rear + (target - rear) * (longitudinal / targetDepth);
            Point candidateDirection = candidateFront - rear;
            Point candidateAtZero = rear + candidateDirection * (targetDepth / Point.Dot(candidateDirection, u));
            double near = intercept.Length, far = (intercept + slope * WeaponSightCalibrationSettings.ArenaMaxDistance).Length;
            return new Evaluation
            {
                zeroDistance = zeroDistance, zeroResidualMeters = (intercept + slope * zeroDistance).Length,
                nearOffsetMeters = near, errorAt20Meters = far, supremum0To20Meters = Math.Max(near, far),
                transverseIntercept = intercept, transverseSlope = slope, proposedFrontPoint = candidateFront,
                proposedFrontTranslation = candidateFront - front, proposedZeroResidualMeters = (candidateAtZero - target).Length
            };
        }

        public static double ErrorAt(Evaluation evaluation, double distance)
        {
            if (evaluation == null) throw new ArgumentNullException(nameof(evaluation));
            if (!(distance >= 0 && distance <= WeaponSightCalibrationSettings.ArenaMaxDistance)) throw new ArgumentOutOfRangeException(nameof(distance));
            return (evaluation.transverseIntercept + evaluation.transverseSlope * distance).Length;
        }

        public static double Supremum(Evaluation evaluation, double minimum, double maximum)
        {
            if (!(minimum <= maximum)) throw new ArgumentException("Inverted interval");
            return Math.Max(ErrorAt(evaluation, minimum), ErrorAt(evaluation, maximum));
        }

        private static void Validate(Point point)
        {
            if (!point.IsFinite) throw new ArgumentException("Non-finite reference point");
        }
    }
}
