using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview.Tests
{
    // Синтетика без preview/SDK: distal-капсула над коробкой; знак — обобщённое число оборотов.
    public class HandPoseContactMetricsTests
    {
        const float Radius = .008f, Length = .02f;

        static FitTriangle Oriented(Vector3 a, Vector3 b, Vector3 c, Vector3 inner)
        {
            var t = new FitTriangle(a, b, c, "index") { FingerSegment = "index/distal", Source = "synthetic" };
            if (Vector3.Dot(t.Normal, (a + b + c) / 3 - inner) < 0) t = new FitTriangle(a, c, b, "index") { FingerSegment = "index/distal", Source = "synthetic" };
            return t;
        }

        // Цилиндр вдоль +Z от сустава (0,0,0) с полусферой на кончике, без донышка. Угол 0 — низ (−Y, ладонь).
        static List<FitTriangle> DistalCapsule(int segments = 24, int slices = 8, int rings = 6)
        {
            var list = new List<FitTriangle>();
            Vector3 P(float angle, float z, float r) { float a = angle * Mathf.Deg2Rad; return new Vector3(r * Mathf.Sin(a), -r * Mathf.Cos(a), z); }
            void Quad(Vector3 p00, Vector3 p01, Vector3 p11, Vector3 p10, Vector3 inner)
            {
                foreach (var t in new[] { Oriented(p00, p01, p11, inner), Oriented(p00, p11, p10, inner) }) if (t.Area > 1e-12f) list.Add(t);
            }
            for (int i = 0; i < slices; i++) for (int j = 0; j < segments; j++) {
                float z0 = Length * i / slices, z1 = Length * (i + 1) / slices, a0 = 360f * j / segments, a1 = 360f * (j + 1) / segments;
                Quad(P(a0, z0, Radius), P(a1, z0, Radius), P(a1, z1, Radius), P(a0, z1, Radius), new Vector3(0, 0, (z0 + z1) / 2));
            }
            for (int k = 0; k < rings; k++) for (int j = 0; j < segments; j++) {
                float f0 = Mathf.PI / 2 * k / rings, f1 = Mathf.PI / 2 * (k + 1) / rings, a0 = 360f * j / segments, a1 = 360f * (j + 1) / segments;
                float r0 = Radius * Mathf.Cos(f0), r1 = Radius * Mathf.Cos(f1), z0 = Length + Radius * Mathf.Sin(f0), z1 = Length + Radius * Mathf.Sin(f1);
                Quad(P(a0, z0, r0), P(a1, z0, r0), P(a1, z1, r1), P(a0, z1, r1), new Vector3(0, 0, Length));
            }
            return list;
        }

        static FitTriangle[] Box(Vector3 min, Vector3 max, bool withoutBottom = false)
        {
            Vector3 center = (min + max) / 2, half = (max - min) / 2;
            var cube = HandPoseFitGeometryTests.Cube().Select(t => new FitTriangle(center + Vector3.Scale(t.A, half), center + Vector3.Scale(t.B, half), center + Vector3.Scale(t.C, half))).ToList();
            if (withoutBottom) cube.RemoveRange(4, 2); // грани y=min
            return cube.ToArray();
        }

        // Верх коробки на расстоянии gap от нижней образующей пальца (gap<0 — проникновение).
        static FitTriangle[] ObjectBelow(float gapMeters, bool open = false) =>
            Box(new Vector3(-.05f, -Radius - gapMeters - .04f, -.05f), new Vector3(.05f, -Radius - gapMeters, .08f), open);

        static FitFingerFrame Frame() => new FitFingerFrame { Finger = "index", DistalJoint = Vector3.zero, TipDirection = Vector3.forward, FingerprintDirection = Vector3.down };
        static HandPoseFitRequest Request() => new HandPoseFitRequest { SampleCount = 2000, Seed = 42 };

        static FitContactMetrics Measure(IList<FitTriangle> hand, FitTriangle[] item, FitFingerFrame frame = null, Vector3? lateral = null) =>
            HandPoseContactMetrics.Measure(hand.ToArray(), new[] { frame ?? Frame() }, new FitSurface(item), Request(), true, lateral ?? Vector3.left);

        [Test] public void MillimeterTriangleHasUnitNormal()
        {
            var t = new FitTriangle(Vector3.zero, new Vector3(.001f, 0, 0), new Vector3(0, .001f, 0));
            Assert.That(t.Normal.magnitude, Is.EqualTo(1).Within(1e-5), "Грань 1 мм: |Cross| 1e-6 м² меньше порога Vector3.normalized.");
            Assert.That(t.Normal.z, Is.EqualTo(1).Within(1e-5));
        }

        [Test] public void OpenMeshWindingSeparatesInsideAndOutside()
        {
            var open = HandPoseFitGeometryTests.Cube().Take(10).ToArray(); // нет грани x=+1
            var surface = new FitSurface(open);
            Assert.That(HandPoseFitGeometry.IsInside(Vector3.zero, surface, HandPoseFitGeometry.Topology(open)), Is.Null, "Прежний знак на открытом меше неизвестен.");
            Assert.That(surface.WindingNumberExact(Vector3.zero), Is.EqualTo(5.0 / 6).Within(1e-6));
            Assert.That(surface.WindingNumber(Vector3.zero), Is.GreaterThanOrEqualTo(.5));
            Assert.That(surface.WindingNumber(new Vector3(.7f, .2f, -.3f)), Is.GreaterThanOrEqualTo(.5), "Внутри у открытой грани.");
            Assert.That(surface.WindingNumber(new Vector3(3, 0, 0)), Is.LessThan(.5), "Снаружи напротив дыры.");
            Assert.That(surface.WindingNumber(new Vector3(0, 0, -3)), Is.LessThan(.5));
        }

        [Test] public void NonManifoldOverlapStaysInsideAndInvertedMeshIsNotInside()
        {
            var a = HandPoseFitGeometryTests.Cube();var b = a.Select(t => new FitTriangle(t.A + Vector3.right, t.B + Vector3.right, t.C + Vector3.right)).ToArray();
            var union = new FitSurface(a.Concat(b).ToArray());
            Assert.That(union.WindingNumberExact(new Vector3(.5f, 0, 0)), Is.EqualTo(2).Within(1e-6));
            Assert.That(union.WindingNumber(new Vector3(-.5f, 0, 0)), Is.GreaterThanOrEqualTo(.5));
            Assert.That(union.WindingNumber(new Vector3(5, 0, 0)), Is.LessThan(.5));
            var inverted = new FitSurface(a.Select(t => new FitTriangle(t.A, t.C, t.B)).ToArray());
            Assert.That(inverted.WindingNumber(Vector3.zero), Is.EqualTo(-1).Within(.03), "Обращённая ориентация даёт −1: метрики показывают это в NegativeWindingAreaMm2.");
        }

        [Test] public void FastWindingMatchesExactSum()
        {
            var sphere = DistalCapsule(48, 24, 12).Select(t => new FitTriangle(t.A * 10, t.B * 10, t.C * 10)).ToArray();
            var surface = new FitSurface(sphere);var random = new System.Random(7);double worst = 0;int mismatches = 0;
            for (int i = 0; i < 200; i++) {
                var p = new Vector3((float)random.NextDouble() * .24f - .12f, (float)random.NextDouble() * .24f - .12f, (float)random.NextDouble() * .5f - .1f);
                double fast = surface.WindingNumber(p), exact = surface.WindingNumberExact(p);
                worst = System.Math.Max(worst, System.Math.Abs(fast - exact));if (System.Math.Abs(exact - .5) > .05 && fast >= .5 != exact >= .5) mismatches++;
            }
            Assert.That(worst, Is.LessThan(.03));Assert.That(mismatches, Is.Zero);
        }

        [Test] public void PadMaskCoversPalmarSectorOfDistal()
        {
            var finger = DistalCapsule();var m = Measure(finger, ObjectBelow(.01f));var f = m.Fingers.Single();
            Assert.That(f.PadAreaFraction, Is.EqualTo(1.0 / 3).Within(.01), "Полуугол 60° — треть боковой поверхности и кончика.");
            Assert.That(m.PadTriangleIndices.All(i => (finger[i].A + finger[i].B + finger[i].C).y / 3 < 0), Is.True, "Подушечка только с ладонной стороны.");
            Assert.That(f.JointPalmarAgreement, Is.EqualTo(1).Within(1e-4));Assert.That(f.JointPadIoU, Is.EqualTo(1).Within(1e-6));
            Assert.That(f.RigAxisAgreement, Is.GreaterThan(.99));Assert.That(f.PadNormalAgreement, Is.GreaterThan(.95));
        }

        [Test] public void PadInTenMillimeterGapIsPositive()
        {
            var f = Measure(DistalCapsule(), ObjectBelow(.01f)).Fingers.Single();
            Assert.That(f.PadSignedDistanceMm, Is.EqualTo(10).Within(.3));Assert.That(f.PadMinSignedMm, Is.EqualTo(10).Within(.3));
            Assert.That(f.PadPenetrationDepthMm, Is.Zero);Assert.That(f.PadInsideAreaMm2, Is.Zero);Assert.That(f.PadWindowAreaMm2, Is.Zero);
            Assert.That(f.PadWindowOpposition, Is.Null);Assert.That(f.PadPointOpposition, Is.EqualTo(1).Within(.02), "Подушечка смотрит на верх коробки.");
        }

        [Test] public void TouchingPadHasWindowAreaWithoutPenetration()
        {
            var m = Measure(DistalCapsule(), ObjectBelow(0));var f = m.Fingers.Single();
            Assert.That(f.PadSignedDistanceMm, Is.EqualTo(0).Within(.3));Assert.That(f.PadMinSignedMm, Is.EqualTo(0).Within(.3));
            Assert.That(f.PadPenetrationDepthMm, Is.LessThan(.1));Assert.That(f.PadWindowAreaMm2, Is.GreaterThan(10));
            Assert.That(f.PadWindowOpposition, Is.GreaterThan(.8));
            Assert.That(f.NearOutsideInPadFraction, Is.GreaterThanOrEqualTo(.95), "Полнота: касание distal попадает в подушечку.");
        }

        [TestCase(false)] [TestCase(true)]
        public void ThreeMillimeterPenetrationIsNegativeOnClosedAndOpenMesh(bool open)
        {
            var item = ObjectBelow(-.003f, open);var m = Measure(DistalCapsule(), item);var f = m.Fingers.Single();
            Assert.That(f.PadMinSignedMm, Is.EqualTo(-3).Within(.3));Assert.That(f.PadPenetrationDepthMm, Is.EqualTo(3).Within(.3));
            Assert.That(f.PadSignedDistanceMm, Is.LessThan(-2.5));Assert.That(f.PadInsideAreaMm2, Is.GreaterThan(0));
            Assert.That(f.FingerPenetrationDepthMm, Is.EqualTo(3).Within(.3));
            Assert.That(m.WindingCheckMismatches, Is.Zero);Assert.That(m.NegativeWindingAreaMm2, Is.Zero);
        }

        [Test] public void MetricsAreInvariantToRigidMotion()
        {
            var rotation = Quaternion.Euler(31, -57, 112);var offset = new Vector3(.3f, -1.2f, .7f);
            FitTriangle Move(FitTriangle t) => new FitTriangle(rotation * t.A + offset, rotation * t.B + offset, rotation * t.C + offset, t.Zone) { FingerSegment = t.FingerSegment, Source = t.Source };
            var frame = new FitFingerFrame { Finger = "index", DistalJoint = offset, TipDirection = rotation * Vector3.forward, FingerprintDirection = rotation * Vector3.down };
            var baseline = Measure(DistalCapsule(), ObjectBelow(-.003f)).Fingers.Single();
            var moved = Measure(DistalCapsule().Select(Move).ToList(), ObjectBelow(-.003f).Select(Move).ToArray(), frame, rotation * Vector3.left).Fingers.Single();
            Assert.That(moved.PadSignedDistanceMm, Is.EqualTo(baseline.PadSignedDistanceMm).Within(.05));
            Assert.That(moved.PadPenetrationDepthMm, Is.EqualTo(baseline.PadPenetrationDepthMm).Within(.05));
            Assert.That(moved.PadAreaFraction, Is.EqualTo(baseline.PadAreaFraction).Within(1e-3));
        }

        [Test] public void LowerLodRenderersExcludeLodZeroAndSharedRenderers()
        {
            var root = new GameObject("lod-root");
            try {
                Renderer Make(string name) {var go = new GameObject(name);go.transform.SetParent(root.transform);return go.AddComponent<MeshRenderer>();}
                Renderer lod0 = Make("Body_LOD0"), lod1 = Make("Body_LOD1"), lod2 = Make("Body_LOD2"), shared = Make("Shared"), free = Make("Free");
                root.AddComponent<LODGroup>().SetLODs(new[] { new LOD(.5f, new[] { lod0, shared }), new LOD(.2f, new[] { lod1, shared }), new LOD(.05f, new[] { lod2 }) });
                var lower = HandPoseFitSnapshot.LowerLodRenderers(root);
                Assert.That(lower, Is.EquivalentTo(new[] { lod1, lod2 }));Assert.That(lower.Contains(free), Is.False);
            } finally {Object.DestroyImmediate(root);}
        }
    }
}
