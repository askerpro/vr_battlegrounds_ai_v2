using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview.Tests
{
    // Фаза 2b contact-metrics: правило severity по полосам, солвер сгиба на синтетике, правка только на временной копии.
    public class HandPoseGripRulesTests
    {
        static FitBandSet Bands(int cases = 12)
        {
            var type = new FitBandType {Cases = cases};
            foreach (string f in HandPoseContactMetrics.Fingers) {
                type.Metrics[f + ".pad"] = new FitBand {N = cases, P50 = 2, Lo = -4, Hi = 14};
                type.Metrics[f + ".padMin"] = new FitBand {N = cases, P50 = 0, Lo = -10, Hi = 12};
                type.Metrics[f + ".padDepth"] = new FitBand {N = cases, P50 = 0, Lo = 0, Hi = 10};
                type.Metrics[f + ".fingerDepth"] = new FitBand {N = cases, P50 = 2, Lo = 0, Hi = 12};
            }
            type.Metrics["palm.min"] = new FitBand {N = cases, P50 = -5, Lo = -13, Hi = 15};
            type.Metrics["palm.depth"] = new FitBand {N = cases, P50 = 5, Lo = 0, Hi = 13};
            return new FitBandSet {Version = "test", LowConfidenceMaxCases = 4, Types = new Dictionary<string, FitBandType> {["pistol_grip"] = type}};
        }

        // Подушечка pad со значением value, остальные метрики внутри полос.
        static FitContactMetrics Metrics(Dictionary<string, double> pads, double palmMin = 0, double fingerDepth = 0)
        {
            var m = new FitContactMetrics {Status = "measured"};
            foreach (string f in HandPoseContactMetrics.Fingers) {
                double pad = pads != null && pads.TryGetValue(f, out var v) ? v : 2;
                m.Fingers.Add(new FitFingerContact {Finger = f, Status = "measured", PadSignedDistanceMm = pad, PadMinSignedMm = Mathf.Clamp((float)pad, -10, 12), PadPenetrationDepthMm = 0, FingerPenetrationDepthMm = fingerDepth});
            }
            m.Segments.Add(new FitSegmentContact {Segment = "palm_wrist", MinSignedMm = palmMin, PenetrationDepthMm = palmMin < 0 ? -palmMin : 0});
            return m;
        }

        static FitGripVerdict Evaluate(FitContactMetrics m, HandPoseFitRequest r = null, int cases = 12) => HandPoseGripRules.Evaluate(m, Bands(cases), "pistol_grip", "test", r ?? new HandPoseFitRequest());
        static FitFingerVerdict Index(FitGripVerdict v) => v.Fingers.Single(f => f.Finger == "index");

        [Test] public void SeverityFollowsExcessOverBand()
        {
            Assert.That(Index(Evaluate(Metrics(new Dictionary<string, double> {["index"] = 17}))).Severity, Is.EqualTo("ok"), "Выход 3 мм — шум эталона.");
            var notable = Index(Evaluate(Metrics(new Dictionary<string, double> {["index"] = 20})));
            Assert.That(notable.Severity, Is.EqualTo("notable"));Assert.That(notable.Direction, Is.EqualTo("gap"));Assert.That(notable.MaxExcessMm, Is.EqualTo(6).Within(1e-6));
            var gross = Evaluate(Metrics(new Dictionary<string, double> {["index"] = 25}));
            Assert.That(Index(gross).Severity, Is.EqualTo("gross"));Assert.That(gross.Overall, Is.EqualTo("gross"));
            var inside = Index(Evaluate(Metrics(new Dictionary<string, double> {["index"] = -10})));
            Assert.That(inside.Severity, Is.EqualTo("notable"));Assert.That(inside.Direction, Is.EqualTo("penetration"));
            var lower = new HandPoseFitRequest {NotableExcessMm = 2, GrossExcessMm = 4};
            Assert.That(Index(Evaluate(Metrics(new Dictionary<string, double> {["index"] = 17}), lower)).Severity, Is.EqualTo("notable"), "Порог — параметр запроса.");
            Assert.That(HandPoseGripRules.Describe(gross).Any(x => x.StartsWith("index: gross gap")), Is.True);
        }

        [Test] public void MixedDirectionWhenGapAndDeepFinger()
        {
            var v = Index(Evaluate(Metrics(new Dictionary<string, double> {["index"] = 20}, fingerDepth: 18)));
            Assert.That(v.Direction, Is.EqualTo("mixed"));Assert.That(v.Severity, Is.EqualTo("notable"));
        }

        [Test] public void MissNeedsThreeFingersFarAboveBand()
        {
            var two = Evaluate(Metrics(new Dictionary<string, double> {["middle"] = 30, ["ring"] = 31}));
            Assert.That(two.HandFlags, Does.Not.Contain("miss"));
            var three = Evaluate(Metrics(new Dictionary<string, double> {["middle"] = 30, ["ring"] = 31, ["little"] = 40}));
            Assert.That(three.HandFlags, Does.Contain("miss"));Assert.That(three.Overall, Is.EqualTo("gross"));
        }

        [Test] public void PalmOffObjectIsGross()
        {
            var v = Evaluate(Metrics(null, palmMin: 26));
            Assert.That(v.HandFlags, Does.Contain("palm_off_object"));Assert.That(v.Overall, Is.EqualTo("gross"));
            Assert.That(Evaluate(Metrics(null, palmMin: 10)).Overall, Is.EqualTo("ok"));
        }

        [Test] public void LowConfidenceForSmallTypes()
        {
            Assert.That(Evaluate(Metrics(null), cases: 4).LowConfidence, Is.True);
            Assert.That(Evaluate(Metrics(null), cases: 12).LowConfidence, Is.False);
            var bands = FitBandSet.Load();
            Assert.That(bands.Version, Is.EqualTo("contact-bands-v1"));
            Assert.That(bands.Types.Keys, Is.EquivalentTo(HandPoseGripRules.GripTypes));
            var lowTypes = bands.Types.Where(t => t.Value.Cases <= bands.LowConfidenceMaxCases).Select(t => t.Key).ToArray();
            Assert.That(lowTypes, Is.EquivalentTo(new[] {"foregrip", "pump"}));
            Assert.That(bands.Types["pistol_grip"].Get("index.pad").N, Is.EqualTo(12));
            Assert.That(bands.Sha256, Has.Length.EqualTo(64));
        }

        [Test] public void UnknownTypeIsNotEvaluated()
        {
            var v = HandPoseGripRules.Evaluate(Metrics(null), Bands(), "pump", "test", new HandPoseFitRequest());
            Assert.That(v.Status, Is.EqualTo("no_bands_for_type"));Assert.That(v.Overall, Is.Null);
        }

        // Палец вдоль +Z: суставы 0 / 40 / 65 мм, кончик 85 мм; ось curl +X сгибает к −Y (ладонь). Коробка ниже: верх y = −30 мм.
        static FitFingerChain Chain() => new FitFingerChain {Finger = "index", Weights = FitFingerChain.SdkWeights("index"), FingerprintDirection = Vector3.down,
            Pivots = new[] {Vector3.zero, new Vector3(0, 0, .04f), new Vector3(0, 0, .065f)}, Axes = new[] {Vector3.right, Vector3.right, Vector3.right}};

        static FitSurface Box()
        {
            Vector3 min = new Vector3(-.1f, -.1f, -.05f), max = new Vector3(.1f, -.03f, .2f), center = (min + max) / 2, half = (max - min) / 2;
            return new FitSurface(HandPoseFitGeometryTests.Cube().Select(t => new FitTriangle(center + Vector3.Scale(t.A, half), center + Vector3.Scale(t.B, half), center + Vector3.Scale(t.C, half))).ToArray());
        }

        [Test] public void BendSolverReachesTargetOnSyntheticFinger()
        {
            var surface = Box();var pad = new Vector3(0, -.008f, .08f);
            var s = HandPoseBendSolver.Solve(Chain(), pad, new[] {(pad, 2)}, p => HandPoseContactMetrics.SignedMm(surface, p), 2, 10);
            Assert.That(s.StartMm, Is.EqualTo(22).Within(.01));
            Assert.That(s.Converged, Is.True);Assert.That(s.CurlDeg, Is.GreaterThan(0), "Положительный curl сгибает к ладони.");
            Assert.That(s.CurlTowardPalmMmPer10Deg, Is.GreaterThan(0));
            Assert.That(s.ExpectedMm, Is.EqualTo(2).Within(.3));Assert.That(s.DepthLimited, Is.False);
        }

        [Test] public void BendSolverStopsAtDepthLimit()
        {
            var surface = Box();var pad = new Vector3(0, -.008f, .08f);
            var depth = new[] {(pad, 2), (new Vector3(0, -.008f, .07f), 2), (new Vector3(0, -.009f, .05f), 1)};
            var s = HandPoseBendSolver.Solve(Chain(), pad, depth, p => HandPoseContactMetrics.SignedMm(surface, p), -12, 3);
            Assert.That(s.DepthLimited, Is.True);
            Assert.That(s.ExpectedDepthMm, Is.LessThanOrEqualTo(3.1));
            Assert.That(s.ExpectedMm, Is.GreaterThan(-12), "Цель не достигнута — ограничение глубины важнее.");
            Assert.That(s.ExpectedMm, Is.LessThan(s.StartMm));
        }

        [Test] public void BendSolverExtendsPenetratingPad()
        {
            // Подушечка на 5 мм внутри коробки: к цели +2 мм ведёт разгиб, а не сгиб «сквозь предмет».
            var surface = Box();var pad = new Vector3(0, -.035f, .08f);
            var s = HandPoseBendSolver.Solve(Chain(), pad, new[] {(pad, 2)}, p => HandPoseContactMetrics.SignedMm(surface, p), 2, 10);
            Assert.That(s.StartMm, Is.EqualTo(-5).Within(.01));
            Assert.That(s.CurlDeg, Is.LessThan(0));Assert.That(s.Converged, Is.True);
            Assert.That(s.ExpectedMm, Is.EqualTo(2).Within(.3));
        }

        [Test] public void ChainApplyKeepsSegmentHierarchy()
        {
            var chain = Chain();var tip = new Vector3(0, 0, .085f);
            Assert.That(Vector3.Distance(chain.Apply(tip, 2, 0), tip), Is.LessThan(1e-6f));
            Assert.That(Vector3.Distance(chain.Apply(new Vector3(0, 0, .02f), 0, 30), Quaternion.AngleAxis(30, Vector3.right) * new Vector3(0, 0, .02f)), Is.LessThan(1e-6f), "Proximal только вокруг своего сустава.");
            var thumb = FitFingerChain.SdkWeights("thumb");Assert.That(thumb, Is.EqualTo(new[] {.1f, .3f, 1f}));
        }

        const string Cyborg = "Assets/ThirdParty/UltimateXR/Samples/FullScene/Prefabs/Avatars/CyborgAvatarExample.prefab";
        const string Gun = "Assets/Prefabs/Weapons/Gun/Gun.prefab";

        [TestCase(Gun, "", 0, "pistol_grip")]
        [TestCase("Assets/Prefabs/Weapons/Machinegun/Machinegun.prefab", "", 1, "foregrip")]
        [TestCase("Assets/Prefabs/Weapons/Shotgun/Shotgun.prefab", "0/3", 0, "pump")]
        [TestCase("Assets/Prefabs/Weapons/AR15/AR15_Magazine.prefab", "", 0, "pinch_small")]
        [TestCase("Assets/Prefabs/Weapons/Scar/Scar.prefab", "", 0, "pistol_grip")]
        public void GripTypeFollowsPointRole(string path, string indexPath, int point, string expected)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);Assert.That(root, Is.Not.Null, path);
            var grabbable = HandPoseFitSnapshot.ResolveIndexPath(root.transform, indexPath).GetComponent<UxrGrabbableObject>();
            Assert.That(HandPoseGripRules.DetectGripType(grabbable, point, out string reason), Is.EqualTo(expected), reason);
        }

        [Test] public void EditPreviewChangesOnlyTemporaryCopy()
        {
            var avatar = AssetDatabase.LoadAssetAtPath<GameObject>(Cyborg);Assert.That(avatar, Is.Not.Null);
            string Rotations() => string.Join("|", avatar.GetComponentsInChildren<Transform>(true).Select(t => t.localRotation.ToString("R")));
            string before = Rotations();
            string output = Path.Combine("Temp", "HandPoseFitTests", "edit-preview-" + System.Guid.NewGuid().ToString("N").Substring(0, 6));
            var request = new HandPoseEditPreviewRequest {PreviewHint = false, Request = new HandPoseFitRequest {AvatarPath = Cyborg, ObjectPath = Gun, GrabPoint = 0, Side = UxrHandSide.Right,
                SampleCount = 2000, RenderImages = false, RenderReviewSheet = false, IncludeOtherHand = false, OutputDirectory = output}};
            request.Edits.Add(new FitPoseEdit {Name = "middle-curl", FingerCurlDeg = new Dictionary<string, float> {["middle"] = 20}});
            var result = HandPoseEditPreview.Run(request);
            Assert.That(result.Error, Is.Null);Assert.That(result.Status, Is.EqualTo("previewed"));
            Assert.That(result.SourceUnchanged, Is.True, "Хеш позы и префаба аватара не изменился.");
            Assert.That(Rotations(), Is.EqualTo(before));
            var edit = result.Edits.Single();
            Assert.That(edit.Fingers.Single(f => f.Finger == "middle").DeltaMm, Is.LessThan(-.5), "Сгиб приближает подушечку к рукояти.");
            foreach (var f in edit.Fingers.Where(x => x.Finger != "middle")) Assert.That(System.Math.Abs(f.DeltaMm), Is.LessThan(.05), f.Finger);
        }
    }
}
