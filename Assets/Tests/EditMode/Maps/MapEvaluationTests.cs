using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Editor.LevelDesign;

namespace VrBattlegrounds.Tests.Maps
{
    public class MapEvaluationTests
    {
        private static MapGridBuilder.Result Built()
        {
            var grid = new MapGrid(60, 60, .1f, Vector2.zero);
            grid.FillZone(Vector2.zero, new Vector2(6, 1), MapGrid.ZoneA);
            grid.FillZone(new Vector2(0, 5), new Vector2(6, 6), MapGrid.ZoneB);
            return new MapGridBuilder.Result { Grid = grid };
        }
        private static PositionImpactLayout Layout()
        {
            ImpactPosition P(string id, float x) => new ImpactPosition
            {
                id = id, min = new Vector2(x - .5f, 2.5f), max = new Vector2(x + .5f, 3.5f),
                states = new[] { new ImpactState { id = "standing", center = new Vector2(x, 3),
                    eyeOffset = new Vector3(0, 1.7f, 0), muzzleOffset = new Vector3(0, 1.7f, 0),
                    body = new[] { new ImpactBodySample { offset = new Vector3(0, 1.7f, 0) } } } }
            };
            return new PositionImpactLayout { map = "Fixture", positions = new[] { P("P", 1), P("Q", 5) },
                routes = new[] { new ImpactRouteSpec { id = "R", from = "P", to = "Q", fromState = "standing", toState = "standing" } } };
        }
        // Решение пользователя 2026-10-02: стартовая дуэль допустима, линии баз — диагностика.
        [Test] public void BaseSightlineIsMeasuredButNotHardViolation()
        {
            var r = MapEvaluation.Evaluate("Fixture", Built(), Layout(), MapEvaluationProfile.Open, false);
            Assert.IsNotEmpty(r.sightlines);
            Assert.IsTrue(r.diagnostics.Any(v => v.rule == "LD-15"));
            Assert.IsFalse(r.violations.Any(v => v.rule == "LD-15"));
        }
        [Test] public void MissingLayoutNeverPasses()
        {
            var r = MapEvaluation.Evaluate("Fixture", Built(), null, MapEvaluationProfile.Open, false);
            Assert.IsFalse(r.measurementsComplete);
            Assert.IsTrue(r.uncheckedRequirements.Any(v => v.rule == "CONTACTS"));
        }
        [Test] public void UnknownProfileAndManualChecksRemainUnchecked()
        {
            var r = MapEvaluation.Evaluate("Fixture", Built(), Layout(), MapEvaluationProfile.Unspecified, false);
            Assert.IsTrue(r.uncheckedRequirements.Any(v => v.rule == "PROFILE"));
            Assert.IsTrue(r.uncheckedRequirements.Any(v => v.rule == "LD-49"));
            Assert.IsTrue(r.uncheckedRequirements.Any(v => v.rule == "ARENA"));
            Assert.IsFalse(r.readyForPlaytest);
        }
        [Test] public void OpenCoverQuotaIsOnlyDiagnostic()
        {
            var r = MapEvaluation.Evaluate("Fixture", Built(), Layout(), MapEvaluationProfile.Open, false);
            Assert.IsTrue(r.diagnostics.Any(v => v.rule == "LD-51"));
            Assert.IsFalse(r.violations.Any(v => v.rule == "LD-51"));
        }
        [Test] public void UnreachableRequestedRouteIsViolation()
        {
            var built = Built();
            built.Grid.FillRect(new Vector2(2.9f, 0), new Vector2(3.1f, 6), 2.5f, "Hard");
            var r = MapEvaluation.Evaluate("Fixture", built, Layout(), MapEvaluationProfile.Open, false);
            Assert.IsTrue(r.violations.Any(v => v.rule == "ROUTE"));
        }
        [Test] public void ExhaustedBudgetIsIncompleteNotZeroInfluence()
        {
            var r = MapEvaluation.Evaluate("Fixture", Built(), Layout(), MapEvaluationProfile.Open, false, 0);
            Assert.IsFalse(r.measurementsComplete);
            Assert.IsTrue(r.uncheckedRequirements.Any(v => v.rule == "CONTACTS"));
        }
        [Test] public void ZeroContactIsExplicitDiagnostic()
        {
            var built = Built();
            built.Grid.FillRect(new Vector2(2.9f, 0), new Vector2(3.1f, 6), 2.5f, "Hard");
            var r = MapEvaluation.Evaluate("Fixture", built, Layout(), MapEvaluationProfile.Open, false);
            Assert.IsTrue(r.diagnostics.Any(v => v.rule == "ZERO-CONTACT"));
        }
        [Test] public void EmptyArenaHasZeroDensityAndClosure()
        {
            var r = MapSpatialMetrics.Measure(Built().Grid);
            Assert.IsTrue(r.complete);
            Assert.AreEqual(0, r.movementObstacleShare);
            Assert.AreEqual(0, r.standingClosure, .001f);
            Assert.AreEqual(0, r.crouchingClosure, .001f);
        }
        [Test] public void LowCoverClosesCrouchingViewOnlyAndOverlapDoesNotDoubleDensity()
        {
            var grid = Built().Grid;
            grid.FillRect(new Vector2(2.9f, 0), new Vector2(3.1f, 6), 1.2f, "Low");
            var a = MapSpatialMetrics.Measure(grid);
            grid.FillRect(new Vector2(2.9f, 0), new Vector2(3.1f, 6), 1.2f, "Duplicate");
            var b = MapSpatialMetrics.Measure(grid);
            Assert.AreEqual(a.movementObstacleShare, b.movementObstacleShare);
            Assert.Greater(a.movementObstacleShare, 0);
            Assert.AreEqual(0, a.standingClosure, .001f);
            Assert.Greater(a.crouchingClosure, 0);
        }
        [Test] public void TallWallRaisesClosureWithoutChangingLowWallDensity()
        {
            var grid = Built().Grid;
            grid.FillRect(new Vector2(2.9f, 0), new Vector2(3.1f, 6), 1.2f, "Low");
            var low = MapSpatialMetrics.Measure(grid);
            grid.FillRect(new Vector2(2.9f, 0), new Vector2(3.1f, 6), 2.5f, "Tall");
            var tall = MapSpatialMetrics.Measure(grid);
            Assert.AreEqual(low.movementObstacleShare, tall.movementObstacleShare);
            Assert.Greater(tall.standingClosure, low.standingClosure);
        }
        [Test] public void NoCombatAreaIsUnknownNotOpen()
        {
            var grid = Built().Grid;
            grid.FillZone(Vector2.zero, new Vector2(6, 6), MapGrid.ZoneA);
            Assert.IsFalse(MapSpatialMetrics.Measure(grid).complete);
        }
    }
}
