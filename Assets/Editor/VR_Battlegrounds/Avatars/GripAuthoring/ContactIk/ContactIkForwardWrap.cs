using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars.ContactIk
{
    public enum ContactIkForwardWrapMode { AvoidPointPenetration, PreferThreeContacts }
    public enum ContactIkWrapSegment { Proximal, Intermediate, Distal }

    public sealed class ContactIkForwardWrapStage
    {
        public string PatchId, DofId;
        public ContactIkWrapSegment Segment;
        public int ClosingSign;
    }

    public sealed class ContactIkForwardWrapFinger
    {
        public string FingerId;
        public ContactIkForwardWrapStage[] Stages;
    }

    public sealed class ContactIkForwardWrapSettings
    {
        public ContactIkForwardWrapMode Mode;
        public float[] InitialDegrees;
        public float StepDegrees, AngularToleranceDegrees;
        public int MaxEvaluations, MaxRepairPasses;
        public float? MaximumPointPenetrationMeters;
    }

    public sealed class ContactIkForwardWrapPoint
    {
        public string PatchId, FingerId, DofId;
        public ContactIkWrapSegment Segment;
        public float? UnsignedDistanceMeters, SignedDistanceMeters;
        public bool DistanceReliable, SignReliable, WithinBand;
        public bool? ClearanceSatisfied;
        public string Diagnostic;
        public string SearchDiagnostic;
    }

    public sealed class ContactIkForwardWrapFingerResult
    {
        public string FingerId;
        public int NumericalContacts, ConfirmedContacts;
        public ContactIkForwardWrapPoint[] Points;
    }

    /// <summary>Подтверждение относится только к опорным точкам. Fullskin и визуальное качество требуют отдельного review.</summary>
    public sealed class ContactIkForwardWrapResult
    {
        public float[] Degrees;
        public string StopReason;
        public int Evaluations, RepairPasses, NumericalContactCount, ConfirmedContactCount, ExpectedContactCount;
        public bool NumericalTargetsReached, RequiresReview = true;
        /// <summary>Разрешённая глубина всех опорных точек и сохранённая palm цель; неизвестный знак не становится true.</summary>
        public bool? PointConstraintsSatisfied;
        public bool HasUnknownPointSign, PalmTargetReached;
        public float PalmResidualMeters, AngularResolutionDegrees;
        public Vector3 PalmTranslationObjectMeters;
        public string[] AtLimits;
        public ContactIkForwardWrapFingerResult[] Fingers;
        public ContactIkPreviewObservation FinalObservation;
    }

    /// <summary>Последовательное первое наблюдённое касание. Не доказывает непрерывное столкновение или глобальную недостижимость.</summary>
    public static class ContactIkForwardWrap
    {
        private sealed class Candidate
        {
            public float[] Q;
            public ContactIkPreviewObservation Observation;
            public ContactIkForwardWrapFingerResult[] Fingers;
            public bool? Clearance;
            public bool PalmReached, HasUnknown;
            public float PalmResidual;
            public int MinimumContacts, TotalContacts, ConfirmedContacts, Rank;
            public double Residual, Prior;
        }

        public static ContactIkCooperativeJob<ContactIkForwardWrapResult> BeginSolve(IReadOnlyList<ContactIkDof> dofs,
            IReadOnlyList<ContactIkForwardWrapFinger> fingers, string palmPatchId,
            Func<float[], ContactIkPreviewObservation> evaluate, ContactIkForwardWrapSettings settings, long generation = 0)
        {
            Validate(dofs, fingers, palmPatchId, evaluate, settings);
            var d = dofs.Select(v => new ContactIkDof { Id = v.Id, MinDegrees = v.MinDegrees, MaxDegrees = v.MaxDegrees,
                PriorScaleDegrees = v.PriorScaleDegrees, PriorWeight = v.PriorWeight }).ToArray();
            var f = fingers.Select(v => new ContactIkForwardWrapFinger { FingerId = v.FingerId,
                Stages = v.Stages.Select(t => new ContactIkForwardWrapStage { PatchId = t.PatchId, DofId = t.DofId, Segment = t.Segment, ClosingSign = t.ClosingSign }).ToArray() }).ToArray();
            var s = new ContactIkForwardWrapSettings { Mode = settings.Mode, InitialDegrees = settings.InitialDegrees == null ? new float[d.Length] : (float[])settings.InitialDegrees.Clone(),
                StepDegrees = settings.StepDegrees, AngularToleranceDegrees = settings.AngularToleranceDegrees, MaxEvaluations = settings.MaxEvaluations,
                MaxRepairPasses = settings.MaxRepairPasses, MaximumPointPenetrationMeters = settings.MaximumPointPenetrationMeters };
            return new ContactIkCooperativeJob<ContactIkForwardWrapResult>(generation, complete => Search(d, f, palmPatchId, evaluate, s, complete));
        }

        public static ContactIkForwardWrapResult Solve(IReadOnlyList<ContactIkDof> dofs,
            IReadOnlyList<ContactIkForwardWrapFinger> fingers, string palmPatchId,
            Func<float[], ContactIkPreviewObservation> evaluate, ContactIkForwardWrapSettings settings)
        {
            using (var job = BeginSolve(dofs, fingers, palmPatchId, evaluate, settings))
            { while (!job.Step(64)) { } return job.Result; }
        }

        private static IEnumerator<int> Search(ContactIkDof[] dofs, ContactIkForwardWrapFinger[] fingers, string palmPatchId,
            Func<float[], ContactIkPreviewObservation> evaluate, ContactIkForwardWrapSettings settings, Action<ContactIkForwardWrapResult> complete)
        {
            int evaluations = 0, repairs = 0;
            float allowedDepth = settings.Mode == ContactIkForwardWrapMode.AvoidPointPenetration ? 0 : settings.MaximumPointPenetrationMeters.Value;
            var indices = dofs.Select((d, i) => new { d.Id, Index = i }).ToDictionary(d => d.Id, d => d.Index, StringComparer.Ordinal);
            var stageDiagnostics = new Dictionary<string, string>(StringComparer.Ordinal);
            Candidate Measure(float[] q)
            {
                ContactIkPreviewObservation observation = evaluate((float[])q.Clone());
                evaluations++;
                return Describe(q, observation, dofs, fingers, palmPatchId, allowedDepth);
            }
            Candidate best = Measure(settings.InitialDegrees);
            yield return 1;
            Candidate current = best;
            string reason = best.Clearance == false || !best.PalmReached ? "initial_point_constraints_require_review" : "partial_contacts";
            // Исходное подтверждённое проникновение не получает автоматического разрешения по baseline.
            if (best.Clearance != false && best.PalmReached)
            {
                for (int pass = 0; pass <= settings.MaxRepairPasses && evaluations < settings.MaxEvaluations - 1; pass++)
                {
                    if (pass > 0) repairs++;
                    Candidate passStart = best;
                    current = best;
                    foreach (ContactIkForwardWrapFinger finger in fingers)
                    foreach (ContactIkForwardWrapStage stage in finger.Stages)
                    {
                        if (evaluations >= settings.MaxEvaluations - 1) break;
                        if (Point(current, stage.PatchId).WithinBand) { stageDiagnostics[stage.PatchId] = "already_in_band"; continue; }
                        stageDiagnostics[stage.PatchId] = "closing_scan";
                        int axis = indices[stage.DofId];
                        float bound = stage.ClosingSign > 0 ? dofs[axis].MaxDegrees : dofs[axis].MinDegrees;
                        Candidate cursor = current, stageBest = current;
                        while (evaluations < settings.MaxEvaluations - 1 && Mathf.Abs(cursor.Q[axis] - bound) > 1e-6f)
                        {
                            float[] trialQ = (float[])cursor.Q.Clone();
                            trialQ[axis] = stage.ClosingSign > 0 ? Mathf.Min(bound, trialQ[axis] + settings.StepDegrees) : Mathf.Max(bound, trialQ[axis] - settings.StepDegrees);
                            Candidate trial = Measure(trialQ);
                            yield return 1;
                            bool blocked = trial.Clearance == false || !trial.PalmReached;
                            bool contact = !blocked && Point(trial, stage.PatchId).WithinBand;
                            if (blocked || contact)
                            {
                                Candidate low = cursor, high = trial;
                                var bracket = new List<Candidate> { trial };
                                float blockedDistance = blocked ? Mathf.Abs(trial.Q[axis] - cursor.Q[axis]) : float.PositiveInfinity;
                                // Уточняем только реально наблюдённый bracket; монотонность деформируемой кожи не предполагается.
                                while (evaluations < settings.MaxEvaluations - 1 && Mathf.Abs(high.Q[axis] - low.Q[axis]) > settings.AngularToleranceDegrees)
                                {
                                    float[] middleQ = (float[])low.Q.Clone();
                                    middleQ[axis] = (low.Q[axis] + high.Q[axis]) * .5f;
                                    Candidate middle = Measure(middleQ);
                                    yield return 1;
                                    bracket.Add(middle);
                                    bool middleBlocked = middle.Clearance == false || !middle.PalmReached;
                                    if (middleBlocked)
                                    { blockedDistance = Mathf.Min(blockedDistance, Mathf.Abs(middle.Q[axis] - cursor.Q[axis])); high = middle; }
                                    else if (Point(middle, stage.PatchId).WithinBand) high = middle;
                                    else low = middle;
                                }
                                // Поздняя endpoint-проба не может перепрыгнуть уже обнаруженный внутри bracket запрет.
                                Candidate firstContact = null;
                                foreach (Candidate candidate in bracket.OrderBy(c => Mathf.Abs(c.Q[axis] - cursor.Q[axis])))
                                {
                                    if (candidate.Clearance == false || !candidate.PalmReached || Mathf.Abs(candidate.Q[axis] - cursor.Q[axis]) >= blockedDistance) continue;
                                    if (Better(candidate, best)) best = candidate;
                                    if (Better(candidate, stageBest)) stageBest = candidate;
                                    if (firstContact == null && Point(candidate, stage.PatchId).WithinBand) firstContact = candidate;
                                }
                                current = firstContact ?? stageBest;
                                stageDiagnostics[stage.PatchId] = firstContact != null ? "first_sampled_bracketed_contact" : !float.IsPositiveInfinity(blockedDistance) ? "point_constraint_blocked_before_contact" : "refinement_budget";
                                break;
                            }
                            if (Better(trial, best)) best = trial;
                            if (Better(trial, stageBest)) stageBest = trial;
                            cursor = trial;
                            current = stageBest;
                        }
                        if (stageDiagnostics[stage.PatchId] == "closing_scan")
                            stageDiagnostics[stage.PatchId] = evaluations >= settings.MaxEvaluations - 1 ? "evaluation_budget" : "closing_limit_without_contact";
                    }
                    if (Reached(best)) { reason = "numerical_contacts_reached_requires_review"; break; }
                    if (!Better(best, passStart)) { reason = "stalled_partial_or_unknown_contacts"; break; }
                    reason = "repair_limit_partial_contacts";
                }
            }
            if (evaluations >= settings.MaxEvaluations - 1 && !Reached(best)) reason = "budget_exhausted_partial_or_unknown_contacts";
            if (evaluations > 1) { best = Measure(best.Q); yield return 1; }
            if (Reached(best)) reason = "numerical_contacts_reached_requires_review";
            foreach (ContactIkForwardWrapPoint point in best.Fingers.SelectMany(f => f.Points))
            {
                point.SearchDiagnostic = stageDiagnostics.TryGetValue(point.PatchId, out string detail) ? detail : "not_visited";
                if (!point.WithinBand && (point.SearchDiagnostic == "already_in_band" || point.SearchDiagnostic == "first_sampled_bracketed_contact"))
                    point.SearchDiagnostic += "; not_in_band_in_selected_final_pose";
            }
            complete(new ContactIkForwardWrapResult
            {
                Degrees = (float[])best.Q.Clone(), StopReason = reason, Evaluations = evaluations, RepairPasses = repairs,
                NumericalContactCount = best.TotalContacts, ConfirmedContactCount = best.ConfirmedContacts, ExpectedContactCount = fingers.Length * 3,
                NumericalTargetsReached = Reached(best), PointConstraintsSatisfied = best.PalmReached ? best.Clearance : false, HasUnknownPointSign = best.HasUnknown,
                PalmTargetReached = best.PalmReached, PalmResidualMeters = best.PalmResidual, AngularResolutionDegrees = settings.AngularToleranceDegrees,
                PalmTranslationObjectMeters = best.Observation.PalmTranslationObjectMeters,
                AtLimits = dofs.Where((d, i) => Mathf.Abs(best.Q[i] - d.MinDegrees) <= 1e-4f || Mathf.Abs(best.Q[i] - d.MaxDegrees) <= 1e-4f).Select(d => d.Id).ToArray(),
                Fingers = best.Fingers, FinalObservation = best.Observation
            });
        }

        private static bool Reached(Candidate c) => c.PalmReached && c.TotalContacts == c.Fingers.Length * 3 && c.Clearance != false;
        private static ContactIkForwardWrapPoint Point(Candidate c, string patch) => c.Fingers.SelectMany(f => f.Points).Single(p => p.PatchId == patch);

        private static bool Better(Candidate a, Candidate b)
        {
            if (a.Rank != b.Rank) return a.Rank > b.Rank;
            if (a.MinimumContacts != b.MinimumContacts) return a.MinimumContacts > b.MinimumContacts;
            if (a.TotalContacts != b.TotalContacts) return a.TotalContacts > b.TotalContacts;
            if (Math.Abs(a.Residual - b.Residual) > 1e-15) return a.Residual < b.Residual;
            return a.Prior + 1e-12 < b.Prior;
        }

        private static Candidate Describe(float[] q, ContactIkPreviewObservation observation, ContactIkDof[] dofs,
            ContactIkForwardWrapFinger[] fingers, string palmPatchId, float allowedDepth)
        {
            if (observation?.Evaluation?.Patches == null || observation.Anchors == null || observation.Criterion != ContactIkCriterion.ForwardSurfaceWrap)
                throw new ArgumentException("Forward callback должен вернуть actual point observations своего criterion.");
            var patches = observation.Evaluation.Patches.ToDictionary(p => p.PatchId, StringComparer.Ordinal);
            if (!patches.TryGetValue(palmPatchId, out ContactIkPatchObservation palm)) throw new ArgumentException("Нет fixed palm observation.");
            ContactIkPreviewAnchorObservation palmAnchor = observation.Anchors.SingleOrDefault(p => p.PatchId == palmPatchId);
            if (palmAnchor?.ObjectTargetPositionMeters == null || !Finite(palm.PositionMeters) || !Finite(palmAnchor.ObjectTargetPositionMeters.Value))
                throw new ArgumentException("Palm требует finite actual position и явную material target position.");
            var c = new Candidate { Q = (float[])q.Clone(), Observation = observation,
                PalmResidual = Vector3.Distance(palm.PositionMeters, palmAnchor.ObjectTargetPositionMeters.Value), Fingers = new ContactIkForwardWrapFingerResult[fingers.Length] };
            c.PalmReached = c.PalmResidual <= ContactIkTarget.FixedPointBandMeters;
            c.Residual = (double)c.PalmResidual * c.PalmResidual;
            bool unknown = false, violation = false;
            void Clearance(ContactIkPatchObservation p)
            {
                bool reliable = p.ClearanceReliable && p.SignedClearanceMeters.HasValue && Finite(p.SignedClearanceMeters.Value);
                if (!reliable) unknown = true;
                else
                {
                    float depth = Mathf.Max(0, -p.SignedClearanceMeters.Value);
                    if (depth > allowedDepth) violation = true;
                    c.Residual += (double)depth * depth;
                }
            }
            Clearance(palm);
            for (int f = 0; f < fingers.Length; f++)
            {
                var result = new ContactIkForwardWrapFingerResult { FingerId = fingers[f].FingerId, Points = new ContactIkForwardWrapPoint[3] };
                c.Fingers[f] = result;
                for (int i = 0; i < 3; i++)
                {
                    ContactIkForwardWrapStage stage = fingers[f].Stages[i];
                    if (!patches.TryGetValue(stage.PatchId, out ContactIkPatchObservation p) || !Finite(p.PositionMeters)) throw new ArgumentException("Нет finite fixed skin point: " + stage.PatchId);
                    Clearance(p);
                    bool measured = p.UnsignedDistanceReliable && p.UnsignedDistanceMeters.HasValue && Finite(p.UnsignedDistanceMeters.Value) && p.UnsignedDistanceMeters.Value >= 0;
                    bool sign = p.ClearanceReliable && p.SignedClearanceMeters.HasValue && Finite(p.SignedClearanceMeters.Value);
                    bool band = measured && p.UnsignedDistanceMeters.Value <= ContactIkTarget.FixedPointBandMeters;
                    bool? clearance = sign ? p.SignedClearanceMeters.Value >= -allowedDepth : (bool?)null;
                    result.Points[i] = new ContactIkForwardWrapPoint { PatchId = stage.PatchId, DofId = stage.DofId, FingerId = fingers[f].FingerId, Segment = stage.Segment,
                        UnsignedDistanceMeters = measured ? p.UnsignedDistanceMeters : null, SignedDistanceMeters = sign ? p.SignedClearanceMeters : null,
                        DistanceReliable = measured, SignReliable = sign, WithinBand = band, ClearanceSatisfied = clearance,
                        Diagnostic = !measured ? "unknown_distance" : !sign ? "unknown_sign_provisional" : clearance == false ? "point_penetration_limit" : band ? "contact" : "gap" };
                    if (band) result.NumericalContacts++;
                    if (band && clearance == true) result.ConfirmedContacts++;
                    double excess = measured ? Math.Max(0, p.UnsignedDistanceMeters.Value - ContactIkTarget.FixedPointBandMeters) : 1;
                    c.Residual += excess * excess;
                }
            }
            c.HasUnknown = unknown;
            c.Clearance = violation ? false : unknown ? (bool?)null : true;
            c.MinimumContacts = c.Fingers.Min(f => f.NumericalContacts); c.TotalContacts = c.Fingers.Sum(f => f.NumericalContacts);
            c.ConfirmedContacts = c.Fingers.Sum(f => f.ConfirmedContacts);
            c.Rank = !c.PalmReached || violation ? 0 : unknown ? 1 : 2;
            for (int i = 0; i < q.Length; i++) c.Prior += dofs[i].PriorWeight * Math.Pow(q[i] / dofs[i].PriorScaleDegrees, 2);
            if (!Finite(c.Residual) || !Finite(c.Prior)) throw new ArgumentException("Нечисловая стоимость forward candidate.");
            return c;
        }

        private static bool Finite(double value) => ContactIkSurfaceAnchor.Finite(value);
        private static bool Finite(Vector3 value) => ContactIkSurfaceAnchor.Finite(value);

        private static void Validate(IReadOnlyList<ContactIkDof> dofs, IReadOnlyList<ContactIkForwardWrapFinger> fingers, string palmPatchId,
            Func<float[], ContactIkPreviewObservation> evaluate, ContactIkForwardWrapSettings s)
        {
            if (dofs == null || fingers == null || fingers.Count != 5 || evaluate == null || s == null || string.IsNullOrWhiteSpace(palmPatchId))
                throw new ArgumentException("Нужны explicit DOFs, пять finger stage profiles, palm PatchId, callback и settings.");
            if (!Enum.IsDefined(typeof(ContactIkForwardWrapMode), s.Mode) || !Finite(s.StepDegrees) || !Finite(s.AngularToleranceDegrees) ||
                s.AngularToleranceDegrees <= 0 || s.StepDegrees < s.AngularToleranceDegrees || s.MaxEvaluations < 2 || s.MaxRepairPasses < 0 ||
                (s.Mode == ContactIkForwardWrapMode.PreferThreeContacts && (!s.MaximumPointPenetrationMeters.HasValue || !Finite(s.MaximumPointPenetrationMeters.Value) || s.MaximumPointPenetrationMeters <= 0)) ||
                (s.Mode == ContactIkForwardWrapMode.AvoidPointPenetration && s.MaximumPointPenetrationMeters.HasValue && s.MaximumPointPenetrationMeters != 0))
                throw new ArgumentException("Нужны конечные budgets/разрешение; Avoid допускает 0, PreferThree требует явный малый max depth > 0.");
            if (s.InitialDegrees != null && s.InitialDegrees.Length != dofs.Count) throw new ArgumentException("InitialDegrees не совпадает с DOFs.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < dofs.Count; i++)
            {
                ContactIkDof d = dofs[i]; float q = s.InitialDegrees == null ? 0 : s.InitialDegrees[i];
                if (d == null || string.IsNullOrWhiteSpace(d.Id) || !ids.Add(d.Id) || !Finite(d.MinDegrees) || !Finite(d.MaxDegrees) || d.MinDegrees > d.MaxDegrees ||
                    !Finite(q) || q < d.MinDegrees || q > d.MaxDegrees || !Finite(d.PriorScaleDegrees) || d.PriorScaleDegrees <= 0 || !Finite(d.PriorWeight) || d.PriorWeight < 0)
                    throw new ArgumentException("DOF/initial q вне явных ограничений; implicit clamp не выполняется.");
            }
            var patches = new HashSet<string>(StringComparer.Ordinal) { palmPatchId };
            var usedDofs = new HashSet<string>(StringComparer.Ordinal); var fingerIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ContactIkForwardWrapFinger finger in fingers)
            {
                if (finger == null || string.IsNullOrWhiteSpace(finger.FingerId) || !fingerIds.Add(finger.FingerId) || finger.Stages == null || finger.Stages.Length != 3)
                    throw new ArgumentException("У каждого distinct finger нужны три stage.");
                for (int i = 0; i < 3; i++)
                {
                    ContactIkForwardWrapStage stage = finger.Stages[i];
                    if (stage == null || stage.Segment != (ContactIkWrapSegment)i || string.IsNullOrWhiteSpace(stage.PatchId) || !patches.Add(stage.PatchId) ||
                        string.IsNullOrWhiteSpace(stage.DofId) || !ids.Contains(stage.DofId) || !usedDofs.Add(stage.DofId) || (stage.ClosingSign != -1 && stage.ClosingSign != 1))
                        throw new ArgumentException("Stage требует distinct fixed PatchId/DOF и явный closing sign в порядке proximal→intermediate→distal.");
                }
            }
        }
    }
}
