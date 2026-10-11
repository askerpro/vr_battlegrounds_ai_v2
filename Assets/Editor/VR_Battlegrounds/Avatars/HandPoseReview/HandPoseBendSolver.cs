using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Оси кисти в кадре предмета: к пальцам (F), радиально к указательному/большому (L), в ладонь (N).</summary>
    public sealed class FitHandAxes
    {
        public Vector3 Fingers, Radial, Palmar, PalmCenter, Wrist;
        public string Meaning = "F: центр ладони → средняя точка proximal-суставов index..little; L: index proximal − little proximal ⟂ F; N: ладонная сторона (правая F×L, левая L×F)";

        public static FitHandAxes From(IReadOnlyList<FitTriangle> hand, IReadOnlyList<FitJointReport> joints, bool right, Vector3 wrist)
        {
            var proximal = joints.Where(j => j.Segment == "proximal").GroupBy(j => j.Zone).ToDictionary(g => g.Key, g => g.First().PositionMeters);
            foreach (string f in new[] {"index", "middle", "ring", "little"}) if (!proximal.ContainsKey(f)) throw new ArgumentException("Нет proximal-сустава: " + f);
            Vector3 center = Vector3.zero;double area = 0;
            foreach (var t in hand) if (t.FingerSegment == "palm_wrist") {center += (t.A + t.B + t.C) / 3 * t.Area;area += t.Area;}
            center = area > 0 ? center / (float)area : wrist;
            Vector3 knuckles = (proximal["index"] + proximal["middle"] + proximal["ring"] + proximal["little"]) / 4;
            Vector3 f0 = (knuckles - center).normalized, l = proximal["index"] - proximal["little"];
            l = (l - f0 * Vector3.Dot(l, f0)).normalized;
            return new FitHandAxes {Fingers = f0, Radial = l, Palmar = (right ? Vector3.Cross(f0, l) : Vector3.Cross(l, f0)).normalized, PalmCenter = center, Wrist = wrist};
        }

        /// <summary>Вектор в осях кисти (x — L, y — F, z — N) → кадр предмета.</summary>
        public Vector3 ToFrame(Vector3 v) => Radial * v.x + Fingers * v.y + Palmar * v.z;
        /// <summary>Поворот кисти x→y→z вокруг L, F, N (градусы) в кадре предмета.</summary>
        public Quaternion Rotation(Vector3 degrees) => Quaternion.AngleAxis(degrees.z, Palmar) * Quaternion.AngleAxis(degrees.y, Fingers) * Quaternion.AngleAxis(degrees.x, Radial);
    }

    /// <summary>
    /// Цепочка сгиба пальца: суставы proximal/intermediate/distal и мировые оси LocalRight тех же костей (ось curl SDK).
    /// Распределение одного угла — как UxrAvatarRig.CurlFinger во входе SteamVR SDK: пальцы (1;1;1), большой (0,1;0,3;1).
    /// </summary>
    public sealed class FitFingerChain
    {
        public string Finger;
        public Vector3[] Pivots = new Vector3[3], Axes = new Vector3[3];
        public float[] Weights = new float[3];
        public Vector3 FingerprintDirection;

        public static float[] SdkWeights(string finger) => finger == "thumb" ? new[] {.1f, .3f, 1f} : new[] {1f, 1f, 1f};

        /// <summary>Жёсткая модель: точка сегмента s (0 proximal, 1 intermediate, 2 distal) после curl на угол, градусы.</summary>
        public Vector3 Apply(Vector3 p, int segment, float curlDeg)
        {
            for (int i = Mathf.Clamp(segment, 0, 2); i >= 0; i--) p = Pivots[i] + Quaternion.AngleAxis(Weights[i] * curlDeg, Axes[i]) * (p - Pivots[i]);
            return p;
        }

        public static int SegmentIndex(string fingerSegment)
        {
            int slash = fingerSegment?.IndexOf('/') ?? -1;if (slash < 0) return -1;
            switch (fingerSegment.Substring(slash + 1)) {case "proximal": return 0;case "intermediate": return 1;case "distal": return 2;default: return -1;}
        }
    }

    /// <summary>Правка позы для предпросмотра на временной копии. Позы и префабы не меняются.</summary>
    public sealed class FitPoseEdit
    {
        public string Name = "edit";
        // Угол curl по пальцу, градусы (SDK CurlFinger: кость поворачивается вокруг своей LocalRight, Space.Self; вес фаланги — SdkWeights).
        public Dictionary<string, float> FingerCurlDeg = new Dictionary<string, float>();
        // Сдвиг кисти, мм, в осях кисти: x — радиально (к указательному), y — к пальцам, z — в ладонь.
        public Vector3 HandShiftMm;
        // Поворот кисти вокруг запястья, градусы, вокруг тех же осей x→y→z.
        public Vector3 HandRotationDeg;
        public bool IsEmpty => (FingerCurlDeg == null || FingerCurlDeg.Values.All(v => Mathf.Abs(v) < 1e-4f)) && HandShiftMm == Vector3.zero && HandRotationDeg == Vector3.zero;
    }

    public sealed class FitBendSolution
    {
        public string Finger, Severity, Note;
        public bool Recommended, Converged, DepthLimited;
        public float CurlDeg;
        public double StartMm, TargetMm, ExpectedMm, StartDepthMm, ExpectedDepthMm, DepthLimitMm;
        // Смещение подушечки на +10° в направлении отпечатка, мм: >0 — положительный curl сгибает к ладони.
        public double CurlTowardPalmMmPer10Deg;
    }

    public sealed class FitHandShiftHint
    {
        public bool Recommended;
        public Vector3 ShiftMm;
        public double PalmMinMm, TargetMm;
        public string Note;
    }

    public sealed class FitCorrectionHints
    {
        public string Status, Reason;
        public string Method = "жёсткая модель пальца: подушечка и вершины фаланг поворачиваются вокруг суставов по осям curl SDK; цель — P50 полосы pad своего типа; глубина пальца не выше max(hi полосы fingerDepth, текущей)";
        public string Meaning = "числовая отправная точка для правки LLM/человеком, не исправление; проверка — предпросмотр на копии (HandPoseEditPreview)";
        public List<FitBendSolution> Fingers = new List<FitBendSolution>();
        public FitHandShiftHint Hand;
        public double ElapsedMs;

        public FitPoseEdit ToEdit(string name = "hint")
        {
            var edit = new FitPoseEdit {Name = name};
            foreach (var f in Fingers.Where(x => x.Recommended && Mathf.Abs(x.CurlDeg) >= .5f)) edit.FingerCurlDeg[f.Finger] = f.CurlDeg;
            if (Hand != null && Hand.Recommended) edit.HandShiftMm = Hand.ShiftMm;
            return edit;
        }
    }

    /// <summary>Подсказки сгиба к медиане эталона SDK. Только расчёт; применение — HandPoseEditPreview на временной копии.</summary>
    public static class HandPoseBendSolver
    {
        public const float MinDeg = -45, MaxDeg = 60, ScanStepDeg = 2;

        /// <summary>
        /// Угол curl, приводящий знаковое расстояние подушечки к цели (зазор — сгибом, проникновение — разгибом,
        /// первое пересечение от текущей позы); если по пути глубина пальца выходит за предел —
        /// наибольший по модулю угол в сторону цели без превышения (DepthLimited).
        /// </summary>
        public static FitBendSolution Solve(FitFingerChain chain, Vector3 padPoint, IReadOnlyList<(Vector3 point, int segment)> depthPoints, Func<Vector3, double> signedMm, double targetMm, double depthLimitMm)
        {
            double Pad(float a) => signedMm(chain.Apply(padPoint, 2, a));
            double Depth(float a) {double d = 0;foreach (var x in depthPoints) d = Math.Max(d, -signedMm(chain.Apply(x.point, x.segment, a)));return d;}
            var s = new FitBendSolution {Finger = chain.Finger, StartMm = Pad(0), TargetMm = targetMm, StartDepthMm = Depth(0)};
            s.DepthLimitMm = Math.Max(depthLimitMm, s.StartDepthMm);
            Vector3 moved = chain.Apply(padPoint, 2, 10) - padPoint;s.CurlTowardPalmMmPer10Deg = Vector3.Dot(moved, chain.FingerprintDirection.normalized) * 1000;

            // Направление по физике хвата: зазор закрывается сгибом к ладони, проникновение — разгибом. Иначе ближайшее
            // пересечение цели может оказаться «сквозь предмет» (подушечка выходит с другой стороны рукояти).
            int toward = s.CurlTowardPalmMmPer10Deg >= 0 ? 1 : -1, direction = s.StartMm > targetMm ? toward : -toward;
            // Скан от 0 в выбранную сторону с шагом 2°, бисекция на первом пересечении цели.
            var angles = new List<float>();float limit = direction > 0 ? MaxDeg : -MinDeg;
            for (float a = 0; a <= limit + 1e-3f; a += ScanStepDeg) angles.Add(direction * a);
            var values = angles.Select(Pad).ToArray();
            float best = 0;double bestError = Math.Abs(s.StartMm - targetMm);bool crossed = false;
            for (int i = 0; i + 1 < angles.Count && !crossed; i++) {
                double e0 = values[i] - targetMm, e1 = values[i + 1] - targetMm;
                if (e0 == 0 || e0 * e1 < 0) {
                    float lo = angles[i], hi = angles[i + 1];
                    for (int k = 0; k < 24; k++) {float mid = (lo + hi) / 2;if ((Pad(lo) - targetMm) * (Pad(mid) - targetMm) <= 0) hi = mid;else lo = mid;}
                    best = (lo + hi) / 2;bestError = Math.Abs(Pad(best) - targetMm);crossed = true;
                }
                else if (Math.Abs(e1) < bestError) {best = angles[i + 1];bestError = Math.Abs(e1);}
            }
            s.Converged = crossed;
            if (!crossed) s.Note = "цель не достигается " + (direction * toward > 0 ? "сгибом" : "разгибом") + " в пределах " + limit + "°: взят угол с наименьшей ошибкой";
            // Ограничение глубины: бисекция по доле пути 0..best (глубина считается монотонной по пути).
            if (Depth(best) > s.DepthLimitMm + .05) {
                float lo = 0, hi = best;
                for (int k = 0; k < 20; k++) {float mid = (lo + hi) / 2;if (Depth(mid) <= s.DepthLimitMm + .05) lo = mid;else hi = mid;}
                best = lo;s.DepthLimited = true;s.Note = (s.Note == null ? "" : s.Note + "; ") + "угол ограничен глубиной пальца " + s.DepthLimitMm.ToString("0.0") + " мм";
            }
            s.CurlDeg = (float)Math.Round(best, 1);s.ExpectedMm = Pad(s.CurlDeg);s.ExpectedDepthMm = Depth(s.CurlDeg);
            return s;
        }

        /// <summary>Подушечка — точка PadPointMeters фазы 1; глубина — до 240 вершин proximal..distal того же пальца.</summary>
        public static FitCorrectionHints Hints(IReadOnlyList<FitTriangle> hand, IReadOnlyList<FitFingerChain> chains, FitContactMetrics metrics, FitGripVerdict verdict, FitBandSet bands, FitSurface surface, FitHandAxes axes)
        {
            var watch = Stopwatch.StartNew();var hints = new FitCorrectionHints {Status = "computed"};
            if (metrics == null || verdict == null || verdict.Status != "evaluated" || bands == null || !bands.Types.TryGetValue(verdict.GripType, out var type)) {hints.Status = "no_rules";hints.Reason = "Нет вердикта правил или полос типа.";return hints;}
            if (chains == null || chains.Count == 0) {hints.Status = "no_finger_chains";hints.Reason = "Снимок не содержит цепочек сгиба.";return hints;}
            Func<Vector3, double> signed = p => HandPoseContactMetrics.SignedMm(surface, p);
            foreach (var fc in metrics.Fingers) {
                var chain = chains.FirstOrDefault(c => c.Finger == fc.Finger);var band = type.Get(fc.Finger + ".pad");var depth = type.Get(fc.Finger + ".fingerDepth");
                var fv = verdict.Fingers.FirstOrDefault(x => x.Finger == fc.Finger);
                if (chain == null || band == null || fc.Status != "measured") {hints.Fingers.Add(new FitBendSolution {Finger = fc.Finger, Note = "нет цепочки/полосы/подушечки"});continue;}
                var points = new List<(Vector3, int)>();var seen = new HashSet<Vector3>();
                foreach (var t in hand) {
                    if (t.FingerSegment == null || !t.FingerSegment.StartsWith(fc.Finger + "/", StringComparison.Ordinal)) continue;
                    int segment = FitFingerChain.SegmentIndex(t.FingerSegment);if (segment < 0) continue;
                    foreach (var v in new[] {t.A, t.B, t.C}) if (seen.Add(v)) points.Add((v, segment));
                }
                int stride = Math.Max(1, points.Count / 240);var subset = points.Where((_, i) => i % stride == 0).ToList();
                var solution = Solve(chain, fc.PadPointMeters, subset, signed, band.P50, depth?.Hi ?? 0);
                solution.Severity = fv?.Severity;solution.Recommended = fv != null && fv.Severity != "ok";
                hints.Fingers.Add(solution);
            }
            var palm = verdict.Palm;var palmBand = type.Get("palm.min");
            var palmSegment = metrics.Segments.FirstOrDefault(s => s.Segment == "palm_wrist");
            if (palm != null && palmBand != null && palmSegment != null && axes != null) {
                // Сдвиг вдоль нормали ладони к P50 минимума ладони эталона: + — в ладонь (к предмету).
                double shift = palmSegment.MinSignedMm - palmBand.P50;
                hints.Hand = new FitHandShiftHint {PalmMinMm = palmSegment.MinSignedMm, TargetMm = palmBand.P50, ShiftMm = new Vector3(0, 0, (float)Math.Round(shift, 1)),
                    Recommended = palm.Severity != "ok" && palm.Flags.Any(x => x.Metric == "palm.min" || x.Metric == "palm.depth"),
                    Note = "только вдоль нормали ладони; сдвиг вдоль рукояти не рассчитывается"};
            }
            hints.ElapsedMs = watch.Elapsed.TotalMilliseconds;
            return hints;
        }
    }
}
