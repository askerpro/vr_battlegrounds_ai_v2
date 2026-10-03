using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Editor.LevelDesign;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Tests.Maps
{
    public class PositionImpactTests
    {
        private static MapGrid Grid() => new MapGrid(60, 60, 0.1f, Vector2.zero);

        private static ImpactState State(float x, float z, float height = 1.7f) => new ImpactState
        {
            id = "standing", center = new Vector2(x, z),
            eyeOffset = new Vector3(0, height, 0), muzzleOffset = new Vector3(0, height, 0),
            body = new[] { new ImpactBodySample { offset = new Vector3(0, height, 0), weight = 1 } }
        };

        private static ImpactPosition Position(string id, ImpactState state) => new ImpactPosition
        {
            id = id, min = state.center - Vector2.one * 0.5f, max = state.center + Vector2.one * 0.5f,
            states = new[] { state }
        };

        [Test]
        public void RejectsDuplicateIds()
        {
            var layout = new PositionImpactLayout { positions = new[] { Position("P", State(1, 1)), Position("P", State(4, 4)) } };
            Assert.IsNotEmpty(PositionImpactValidation.Validate(Grid(), layout));
        }

        [Test]
        public void RejectsNonFiniteCoordinates()
        {
            var layout = new PositionImpactLayout { positions = new[] { Position("P", State(float.NaN, 1)) } };
            Assert.IsNotEmpty(PositionImpactValidation.Validate(Grid(), layout));
        }

        [Test]
        public void RejectsStateOutsideArea()
        {
            ImpactPosition p = Position("P", State(1, 1));
            p.states[0].center = new Vector2(4, 4);
            Assert.IsNotEmpty(PositionImpactValidation.Validate(Grid(), new PositionImpactLayout { positions = new[] { p } }));
        }

        [Test]
        public void SoftSeparatesVisibilityAndShot()
        {
            MapGrid g = Grid();
            g.FillRect(new Vector2(2.9f, 0), new Vector2(3.1f, 6), 2.5f, "Soft", CoverClass.Soft);
            ImpactPairState r = PositionImpactAnalyzer.EvaluatePair(g, Position("P", State(1, 3)), Position("Q", State(5, 3))).Single();
            Assert.AreEqual(0, r.visibleShare);
            Assert.AreEqual(1, r.shotShare);
            Assert.AreEqual(0, r.visibleShotShare);
            Assert.AreEqual(1, r.hiddenShotShare);
        }

        [Test]
        public void MuzzleDiffersFromEye()
        {
            MapGrid g = Grid();
            g.FillRect(new Vector2(2.9f, 0), new Vector2(3.1f, 6), 1.2f, "Low");
            ImpactState p = State(1, 3); p.muzzleOffset.y = 0.3f;
            ImpactState q = State(5, 3, 0.3f);
            var r = PositionImpactAnalyzer.EvaluatePair(g, Position("P", p), Position("Q", q)).Single();
            // Луч глаз к низкой цели на стене ниже 1.2; поднятая цель отдельно проверяет расхождение.
            q.body[0].offset.y = 1.7f;
            r = PositionImpactAnalyzer.EvaluatePair(g, Position("P", p), Position("Q", q)).Single();
            Assert.AreEqual(1, r.visibleShare);
            Assert.AreEqual(0, r.shotShare);
        }

        [Test]
        public void DirectedBodyExposureAndProtectionUseSameState()
        {
            MapGrid g = Grid();
            g.FillRect(new Vector2(2.9f, 0), new Vector2(3.1f, 6), 1.2f, "Low");
            ImpactState p = State(1, 3); p.body[0].offset.y = 0.3f;
            ImpactState q = State(5, 3);
            var a = PositionImpactAnalyzer.EvaluatePair(g, Position("P", p), Position("Q", q)).Single();
            var b = PositionImpactAnalyzer.EvaluatePair(g, Position("Q", q), Position("P", p)).Single();
            Assert.AreEqual(1, a.shotShare);
            Assert.AreEqual(0, b.shotShare);
            Assert.AreEqual(0, a.sourceExposure);
            Assert.AreEqual(0, a.targetProtection);
        }

        [Test]
        public void RouteDetoursAroundWall()
        {
            MapGrid g = Grid();
            g.FillRect(new Vector2(2.8f, 2), new Vector2(3.2f, 4), 2.5f, "Wall");
            ImpactRoute r = PositionRouteAnalyzer.Build(g, new Vector2(1.05f, 3.05f), new Vector2(5.05f, 3.05f), null, 0.25f, 1);
            Assert.IsTrue(r.reachable);
            Assert.Greater(r.length, 4.4f);
            Assert.AreEqual(r.length, r.duration, 0.0001f);
        }

        // LD-53: одна центральная точка скрывается и за тонким торцом; боковые образцы
        // отличают полноценную фронтальную защиту от продольной стенки.
        private static ImpactPosition FrontCoverDefender()
        {
            var state = State(3.05f, 1);
            state.body = new[]
            {
                new ImpactBodySample { offset = new Vector3(0, 1.7f, 0), weight = 1 },
                new ImpactBodySample { offset = new Vector3(-.35f, 1.2f, 0), weight = 2 },
                new ImpactBodySample { offset = new Vector3(.35f, 1.2f, 0), weight = 2 }
            };
            return Position("Defender", state);
        }

        [Test]
        public void FrontalCoverProtectsLateralBodySamplesUnlikeLongitudinalWall()
        {
            var threat = Position("OppositeBase", State(3.05f, 5));
            var defender = FrontCoverDefender();
            var along = Grid();
            along.FillRect(new Vector2(3, 1.8f), new Vector2(3.1f, 3.8f), 2.5f, "Along");
            var front = Grid();
            front.FillRect(new Vector2(2.1f, 2.4f), new Vector2(4, 2.5f), 2.5f, "Front");
            var bad = PositionImpactAnalyzer.EvaluatePair(along, threat, defender).Single();
            var good = PositionImpactAnalyzer.EvaluatePair(front, threat, defender).Single();
            Assert.Greater(bad.shotShare, .5f, "Торец скрывает центр, но боковые части тела доступны.");
            Assert.AreEqual(0, good.shotShare);
            Assert.AreEqual(1, good.targetProtection);
            Assert.Less(bad.targetProtection, good.targetProtection);
        }

        [Test]
        public void CornerCoverAddsFrontProtectionToLongitudinalWall()
        {
            var threat = Position("OppositeBase", State(3.05f, 5));
            var defender = FrontCoverDefender();
            var grid = Grid();
            grid.FillRect(new Vector2(3, 1.8f), new Vector2(3.1f, 3.8f), 2.5f, "Along");
            Assert.Greater(PositionImpactAnalyzer.EvaluatePair(grid, threat, defender).Single().shotShare, .5f);
            grid.FillRect(new Vector2(2.1f, 2.4f), new Vector2(4, 2.5f), 2.5f, "Front");
            Assert.AreEqual(0, PositionImpactAnalyzer.EvaluatePair(grid, threat, defender).Single().shotShare);
        }

        [Test]
        public void RouteCannotCrossClosedWall()
        {
            MapGrid g = Grid();
            g.FillRect(new Vector2(2.8f, 0), new Vector2(3.2f, 6), 2.5f, "Wall");
            Assert.IsFalse(PositionRouteAnalyzer.Build(g, new Vector2(1, 3), new Vector2(5, 3), null, 0.25f, 1).reachable);
        }

        [Test]
        public void RouteHonorsRadius()
        {
            MapGrid g = Grid();
            g.FillRect(new Vector2(2.8f, 0), new Vector2(3.2f, 2.7f), 2.5f, "Left");
            g.FillRect(new Vector2(2.8f, 3.3f), new Vector2(3.2f, 6), 2.5f, "Right");
            Assert.IsFalse(PositionRouteAnalyzer.Build(g, new Vector2(1, 3), new Vector2(5, 3), null, 0.5f, 1).reachable);
            Assert.IsTrue(PositionRouteAnalyzer.Build(g, new Vector2(1, 3), new Vector2(5, 3), null, 0.15f, 1).reachable);
        }

        [Test]
        public void HardStopsVisibilityAndShot()
        {
            MapGrid g = Grid();
            g.FillRect(new Vector2(2.9f, 0), new Vector2(3.1f, 6), 2.5f, "Hard");
            var row = PositionImpactAnalyzer.EvaluatePair(g, Position("P", State(1, 3)), Position("Q", State(5, 3))).Single();
            Assert.AreEqual(0, row.visibleShare);
            Assert.AreEqual(0, row.shotShare);
            Assert.AreEqual(1, row.targetProtection);
        }

        [Test]
        public void OpeningCostComparedToExplicitProtectedState()
        {
            MapGrid g = Grid();
            g.FillRect(new Vector2(2.9f, 0), new Vector2(3.1f, 6), 1.2f, "Low");
            ImpactPosition p = Position("P", State(1, 3));
            ImpactState closed = State(1, 3, 0.3f); closed.id = "hidden";
            p.states = new[] { p.states[0], closed }; p.protectedState = "hidden";
            var row = PositionImpactAnalyzer.EvaluatePair(g, p, Position("Q", State(5, 3))).Single(r => r.fromState == "standing");
            Assert.IsTrue(row.hasProtectedBaseline);
            Assert.AreEqual(0, row.sourceBaselineExposure);
            Assert.AreEqual(1, row.sourceExposure);
            Assert.AreEqual(1, row.openingExposureDelta);
        }

        [Test]
        public void BodySamplesUseWeights()
        {
            MapGrid g = Grid();
            g.FillRect(new Vector2(2.9f, 0), new Vector2(3.1f, 6), 1.2f, "Low");
            ImpactState q = State(5, 3);
            q.body = new[] { new ImpactBodySample { offset = new Vector3(0, 1.7f, 0), weight = 1 },
                new ImpactBodySample { offset = new Vector3(0, 0.3f, 0), weight = 2 } };
            var row = PositionImpactAnalyzer.EvaluatePair(g, Position("P", State(1, 3)), Position("Q", q)).Single();
            Assert.AreEqual(1f / 3f, row.shotShare, 0.0001f);
        }

        [Test]
        public void ViaPortalChangesRoute()
        {
            MapGrid g = Grid();
            var route = PositionRouteAnalyzer.Build(g, new Vector2(1.05f, 1.05f), new Vector2(5.05f, 1.05f),
                new[] { new Vector2(3.05f, 5.05f) }, 0.25f, 1);
            Assert.IsTrue(route.reachable);
            Assert.IsTrue(route.points.Any(p => (p - new Vector2(3.05f, 5.05f)).magnitude < 0.001f));
            Assert.Greater(route.length, 8);
        }

        [Test]
        public void ValidLayoutAcceptedAndUnknownRouteStateRejected()
        {
            var layout = new PositionImpactLayout { positions = new[] { Position("P", State(1, 1)), Position("Q", State(5, 5)) } };
            Assert.IsEmpty(PositionImpactValidation.Validate(Grid(), layout));
            layout.routes = new[] { new ImpactRouteSpec { id = "R", from = "P", to = "Q", fromState = "missing", toState = "standing" } };
            Assert.IsNotEmpty(PositionImpactValidation.Validate(Grid(), layout));
        }

        [Test]
        public void ReportHasBothDirectionsAndRejectsInvalidInput()
        {
            var layout = new PositionImpactLayout { positions = new[] { Position("P", State(1, 1)), Position("Q", State(5, 5)) } };
            var report = PositionImpactAnalysis.Analyze(Grid(), layout);
            Assert.IsTrue(report.complete);
            Assert.AreEqual(2, report.pairs.Length);
            Assert.IsTrue(report.pairs.Any(p => p.from == "P" && p.to == "Q"));
            Assert.IsTrue(report.pairs.Any(p => p.from == "Q" && p.to == "P"));
            layout.speed = 0;
            report = PositionImpactAnalysis.Analyze(Grid(), layout);
            Assert.IsFalse(report.complete);
            Assert.IsNotEmpty(report.problems);
            Assert.IsEmpty(report.pairs);
        }

        [Test]
        public void ReportRoutesKeepOneRowPerFixedShooterState()
        {
            ImpactPosition p = Position("P", State(1, 1));
            ImpactState crouch = State(1, 1, 1.1f); crouch.id = "crouching";
            p.states = new[] { p.states[0], crouch };
            var layout = new PositionImpactLayout
            {
                positions = new[] { p, Position("Q", State(5, 5)) },
                routes = new[] { new ImpactRouteSpec { id = "R", from = "P", to = "Q", fromState = "standing", toState = "standing" } }
            };
            var report = PositionImpactAnalysis.Analyze(Grid(), layout);
            Assert.IsTrue(report.complete);
            Assert.AreEqual(3, report.influences.Length);
            Assert.AreEqual(2, report.influences.Count(r => r.position == "P"));
            Assert.IsTrue(report.influences.Any(r => r.position == "P" && r.state == "crouching"));
            Assert.IsTrue(report.routes.Single().reachable);
        }

        [Test]
        public void BudgetExceededLeavesMissingRowsExplicitlyIncomplete()
        {
            var positions = Enumerable.Range(0, 32).Select(i =>
            {
                ImpactPosition p = Position("P" + i, State(i % 2 == 0 ? 1 : 5, 3));
                ImpactState other = State(p.states[0].center.x, 3, 1.1f); other.id = "crouching";
                p.states = new[] { p.states[0], other }; return p;
            }).ToArray();
            var routes = Enumerable.Range(0, 32).Select(i => new ImpactRouteSpec
                { id = "R" + i, from = "P0", to = "P1", fromState = "standing", toState = "standing" }).ToArray();
            var report = PositionImpactAnalysis.Analyze(Grid(), new PositionImpactLayout { positions = positions, routes = routes });
            Assert.IsFalse(report.complete);
            Assert.IsNotEmpty(report.problems);
            Assert.Less(report.influences.Length, 2048);
            Assert.Greater(report.influences.Length, 0);
        }

        [Test]
        public void FixedShooterDoesNotCombineStates()
        {
            MapGrid g = Grid();
            // Контролируемая геометрия лучей: каждый источник достигает только половины пути.
            g.ShotLine = (a, b) => a.x < 2 ? b.x < 3 : b.x >= 3;
            g.LineOfSight = g.ShotLine;
            var route = new ImpactRoute { reachable = true, length = 4, duration = 4,
                points = new[] { new Vector2(1, 3), new Vector2(3, 3), new Vector2(5, 3) } };
            var left = PositionRouteAnalyzer.Evaluate(g, State(1, 1), route, State(0, 0), 1);
            var right = PositionRouteAnalyzer.Evaluate(g, State(5, 1), route, State(0, 0), 1);
            Assert.AreEqual(2, left.shotLength, 0.0001f);
            Assert.AreEqual(2, right.shotLength, 0.0001f);
            Assert.AreEqual(2, left.longestShotLength, 0.0001f);
        }

        [Test]
        public void ExposureLengthsAreSegmentWeighted()
        {
            MapGrid g = Grid();
            g.ShotLine = (a, b) => b.x > 2;
            g.LineOfSight = g.ShotLine;
            var route = new ImpactRoute { reachable = true, length = 4, duration = 2,
                points = new[] { new Vector2(1, 3), new Vector2(2, 3), new Vector2(5, 3) } };
            var r = PositionRouteAnalyzer.Evaluate(g, State(1, 1), route, State(0, 0), 2);
            Assert.AreEqual(3, r.shotLength, 0.0001f);
            Assert.AreEqual(1.5f, r.shotDuration, 0.0001f);
        }
    }
}
