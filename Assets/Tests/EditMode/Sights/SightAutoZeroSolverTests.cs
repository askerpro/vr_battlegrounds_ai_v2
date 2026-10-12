using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Editor.Weapons.Sights;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Tests.Sights
{
    /// <summary>
    /// Инструментальный этап sight-iron-kits: проверяется только геометрия решателя автосведения на синтетических
    /// данных. Механика прицелов в игре пользователем ещё не принята — её тесты пишутся после приёмки.
    /// </summary>
    public sealed class SightAutoZeroSolverTests
    {
        private const double Z = 15;

        // Ствол в начале координат вдоль +Z; R на 7 см выше и 30 см позади дула, F на 7 см выше и 10 см впереди.
        private static SightAutoZeroInput Base(Quaternion frame = default, float axisScale = 1f)
        {
            if (frame == default) frame = Quaternion.identity;
            return new SightAutoZeroInput
            {
                Rear = frame * new Vector3(0f, .07f, -.3f), Front = frame * new Vector3(0f, .07f, .1f),
                MovingRole = SightPartRole.Front, MoveAxisPerUnit = frame * Vector3.up * axisScale,
                RearUp = frame * Vector3.up, FrontUp = frame * Vector3.up,
                MuzzlePosition = Vector3.zero, MuzzleForward = frame * Vector3.forward, MuzzleUp = frame * Vector3.up,
                ZeroDistance = Z, CurrentOffset = 0, TravelDown = .003, TravelUp = .003
            };
        }

        // Высота F, при которой линия из R(−0,3; 0,07) приходит в T(15; 0): 0,07 − 0,07·0,4/15,3.
        private const double ExpectedDelta = -.07 * .4 / 15.3;

        [Test]
        public void FrontPost_ConvergesOnZeroPoint()
        {
            var result = SightAutoZeroSolver.Solve(Base());
            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(ExpectedDelta, result.RequiredDelta, 1e-7);
            Assert.AreEqual(0, result.After.VerticalAt(Z), 1e-9);
            Assert.AreEqual(.07, result.Before.VerticalAt(Z), 1e-6, "до сведения линия параллельна стволу на 70 мм выше");
        }

        [Test]
        public void RearAperture_ConvergesOnZeroPoint()
        {
            var input = Base();
            input.MovingRole = SightPartRole.Rear;
            var result = SightAutoZeroSolver.Solve(input);
            // Целик поднимается на продолжение линии T→F: R.y = 0,07 + 0,07·0,4/14,9.
            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(.07 * .4 / 14.9, result.RequiredDelta, 1e-7);
            Assert.AreEqual(0, result.After.VerticalAt(Z), 1e-9);
        }

        [Test]
        public void InsufficientTravel_RefusesAndReportsRequiredOffset()
        {
            var input = Base();
            input.TravelDown = .001;
            var result = SightAutoZeroSolver.Solve(input);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("хода", result.Error);
            Assert.AreEqual(ExpectedDelta, result.RequiredOffset, 1e-7);
        }

        [Test]
        public void FrontBehindRear_Refuses()
        {
            var input = Base();
            (input.Rear, input.Front) = (input.Front, input.Rear);
            var result = SightAutoZeroSolver.Solve(input);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("не впереди", result.Error);
        }

        [Test]
        public void Horizontal_IsMeasuredButNotCorrected()
        {
            var input = Base();
            input.Front += new Vector3(.0005f, 0f, 0f);
            var result = SightAutoZeroSolver.Solve(input);
            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(0, result.After.VerticalAt(Z), 1e-9);
            Assert.AreEqual(result.Before.HorizontalAt(Z), result.After.HorizontalAt(Z), 1e-12);
            Assert.Greater(result.After.HorizontalAt(Z), .01, "0,5 мм сбоку у мушки на плече 0,4 м дают ≈ 19 мм на 15 м");
        }

        [Test]
        public void ErrorAfterZero_FollowsFormula()
        {
            var result = SightAutoZeroSolver.Solve(Base());
            Assert.IsTrue(result.Ok, result.Error);
            // h — высота линии прицела в плоскости дула: прямая из R(−0,3; 0,07) в T(15; 0).
            double h = .07 * Z / (Z + .3);
            Assert.AreEqual(h, result.After.InterceptVertical, 1e-7);
            foreach (double d in new[] { 5.0, 10.0, 20.0 })
                Assert.AreEqual(h * System.Math.Abs(1 - d / Z), result.After.TotalAt(d), 1e-7, "d = " + d);
        }

        [Test]
        public void RotatedWeapon_GivesSameShift()
        {
            var frame = Quaternion.Euler(-8f, 37f, 4f);
            var result = SightAutoZeroSolver.Solve(Base(frame));
            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(ExpectedDelta, result.RequiredDelta, 1e-6);
            Assert.AreEqual(0, result.After.VerticalAt(Z), 1e-6);
        }

        [Test]
        public void ScaledParent_ReturnsLocalUnits()
        {
            var input = Base(axisScale: .5f);
            input.TravelDown = input.TravelUp = .006; // ход в локальных единицах: при масштабе 0,5 нужен сдвиг ≈ 3,7
            var result = SightAutoZeroSolver.Solve(input);
            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(ExpectedDelta, result.RequiredWorldShift, 1e-7);
            Assert.AreEqual(ExpectedDelta / .5, result.RequiredDelta, 1e-7);
        }

        [Test]
        public void TiltedAxis_Refuses()
        {
            var input = Base();
            input.MoveAxisPerUnit = Quaternion.AngleAxis(10f, Vector3.forward) * Vector3.up;
            var result = SightAutoZeroSolver.Solve(input);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("Наклон", result.Error);
        }

        [Test]
        public void InvalidZeroDistance_Refuses()
        {
            var input = Base();
            input.ZeroDistance = 25;
            Assert.IsFalse(SightAutoZeroSolver.Solve(input).Ok);
        }
    }
}
