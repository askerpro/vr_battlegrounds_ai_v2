using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars.ContactIk
{
    /// <summary>
    /// Ограниченный поиск по независимым суставам. Каждая проба проходит внешний BakeMesh validator;
    /// решения жёсткой модели пальца и запись ассетов здесь отсутствуют.
    /// </summary>
    public static class ContactIkSolver
    {
        private sealed class Candidate
        {
            public float[] Q;
            public ContactIkEvaluation Evaluation;
            public ContactIkTargetError[] Errors;
            public bool TargetsReached, Safe;
            public double Cost;
        }

        /// <summary>
        /// evaluate применяет q к временной копии и возвращает фактическую кожу в системе предмета.
        /// Последний вызов всегда получает итоговый q; исключение callback передаётся вызывающему коду.
        /// </summary>
        public static ContactIkResult Solve(IReadOnlyList<ContactIkDof> dofs, IReadOnlyList<ContactIkTarget> targets,
                                             Func<float[], ContactIkEvaluation> evaluate, ContactIkSettings settings = null)
        {
            using (var job = BeginSolve(dofs, targets, evaluate, settings))
            {
                while (!job.Step(64)) { }
                return job.Result;
            }
        }

        public static ContactIkCooperativeJob<ContactIkResult> BeginSolve(IReadOnlyList<ContactIkDof> dofs, IReadOnlyList<ContactIkTarget> targets,
            Func<float[], ContactIkEvaluation> evaluate, ContactIkSettings settings = null, long generation = 0)
        {
            settings = settings ?? new ContactIkSettings();
            Validate(dofs, targets, evaluate, settings);
            var s = new ContactIkSettings { Criterion = settings.Criterion, DistalPriority = settings.DistalPriority, MaxEvaluations = settings.MaxEvaluations,
                InitialStepDegrees = settings.InitialStepDegrees, MinimumStepDegrees = settings.MinimumStepDegrees, PriorWeight = settings.PriorWeight, ImprovementEpsilon = settings.ImprovementEpsilon };
            var d = new ContactIkDof[dofs.Count]; var t = new ContactIkTarget[targets.Count];
            for (int i = 0; i < d.Length; i++) d[i] = new ContactIkDof { Id = dofs[i].Id, Bone = dofs[i].Bone, LocalAxis = dofs[i].LocalAxis,
                MinDegrees = dofs[i].MinDegrees, MaxDegrees = dofs[i].MaxDegrees, PriorScaleDegrees = dofs[i].PriorScaleDegrees, PriorWeight = dofs[i].PriorWeight };
            for (int i = 0; i < t.Length; i++) t[i] = new ContactIkTarget { PatchId = targets[i].PatchId, Mode = targets[i].Mode, Role = targets[i].Role,
                FingerId = targets[i].FingerId, PositionMeters = targets[i].PositionMeters, Normal = targets[i].Normal, MatchNormal = targets[i].MatchNormal,
                PositionToleranceMeters = targets[i].PositionToleranceMeters, NormalToleranceDegrees = targets[i].NormalToleranceDegrees,
                MinimumClearanceMeters = targets[i].MinimumClearanceMeters, Weight = targets[i].Weight };
            return new ContactIkCooperativeJob<ContactIkResult>(generation, complete => Search(d, t, evaluate, s, complete));
        }

        private static IEnumerator<int> Search(IReadOnlyList<ContactIkDof> dofs, IReadOnlyList<ContactIkTarget> targets,
            Func<float[], ContactIkEvaluation> evaluate, ContactIkSettings settings, Action<ContactIkResult> complete)
        {
            settings = settings ?? new ContactIkSettings();
            Validate(dofs, targets, evaluate, settings);
            int evaluations = 0, sweeps = 0;
            var regionBaseline = new Dictionary<string, ContactIkRegionObservation>(StringComparer.Ordinal);
            Candidate Measure(float[] q)
            {
                // Callback не получает внутренний вектор поиска, чтобы не мог незаметно изменить границы.
                ContactIkEvaluation observation = Snapshot(evaluate((float[])q.Clone()));
                evaluations++;
                ValidateEvaluation(observation, regionBaseline, evaluations == 1, settings.Criterion);
                if (settings.Criterion == ContactIkCriterion.DistalPalmTargets && observation.DistalPriority != settings.DistalPriority)
                    throw new ArgumentException("Callback изменил DistalPriority относительно настроек поиска.");
                return Score(q, observation, dofs, targets, settings);
            }

            var initial = new float[dofs.Count];
            for (int i = 0; i < initial.Length; i++) initial[i] = Mathf.Clamp(0f, dofs[i].MinDegrees, dofs[i].MaxDegrees);
            Candidate best = Measure(initial);
            yield return 1;
            float step = settings.InitialStepDegrees;
            string status = "budget_exhausted";
            while (targets.Count > 0 && evaluations < settings.MaxEvaluations - 1)
            {
                if (SearchSatisfied(best, targets.Count)) { status = Solved(best, targets.Count) ? "solved" : "requires_review"; break; }
                bool improved = false;
                sweeps++;
                for (int i = 0; i < dofs.Count && evaluations < settings.MaxEvaluations - 1; i++)
                {
                    Candidate axisBest = best;
                    // Обе стороны оси сравниваются с одной исходной точкой, порядок детерминирован.
                    for (int sign = -1; sign <= 1 && evaluations < settings.MaxEvaluations - 1; sign += 2)
                    {
                        float angle = Mathf.Clamp(best.Q[i] + sign * step, dofs[i].MinDegrees, dofs[i].MaxDegrees);
                        if (Mathf.Abs(angle - best.Q[i]) < 1e-6f) continue;
                        float[] q = (float[])best.Q.Clone();
                        q[i] = angle;
                        Candidate trial = Measure(q);
                        yield return 1;
                        if (Better(trial, axisBest, settings.ImprovementEpsilon)) axisBest = trial;
                    }
                    if (!ReferenceEquals(axisBest, best)) { best = axisBest; improved = true; }
                    if (SearchSatisfied(best, targets.Count)) break;
                }
                if (!improved)
                {
                    if (step <= settings.MinimumStepDegrees) { status = "stalled"; break; }
                    step = Mathf.Max(step * .5f, settings.MinimumStepDegrees);
                }
            }

            // Восстанавливает выбранную позу после последней отвергнутой пробы и повторяет её реальную проверку.
            if (evaluations > 1) { best = Measure(best.Q); yield return 1; }
            bool solved = Solved(best, targets.Count);
            if (solved) status = "solved";
            else if (targets.Count == 0) status = "no_targets";
            else if (settings.Criterion == ContactIkCriterion.DistalPalmTargets) status = best.TargetsReached ? "numerical_targets_reached_requires_review" : "numerical_targets_not_reached_requires_review";
            else if (!best.Evaluation.Reliable || !best.Evaluation.FullSurfaceChecked) status = "requires_review";
            else if (!best.Safe) status = "no_feasible_pose";
            else if (settings.Criterion == ContactIkCriterion.FixedPointBand && best.TargetsReached) status = "requires_review";
            var limits = new List<string>();
            for (int i = 0; i < dofs.Count; i++)
                if (Mathf.Abs(best.Q[i] - dofs[i].MinDegrees) <= 1e-4f || Mathf.Abs(best.Q[i] - dofs[i].MaxDegrees) <= 1e-4f)
                    limits.Add(dofs[i].Id);
            complete(new ContactIkResult
            {
                Status = status, Converged = solved, RequiresReview = !solved,
                HasReliableFeasiblePose = best.Safe, TargetsReached = targets.Count > 0 && best.TargetsReached,
                NumericalTargetsReached = targets.Count > 0 && best.TargetsReached, PalmTranslationObjectMeters = best.Evaluation.PalmTranslationObjectMeters,
                Evaluations = evaluations, Sweeps = sweeps, Degrees = (float[])best.Q.Clone(),
                AtLimits = limits.ToArray(), Cost = best.Cost, TargetErrors = best.Errors, FinalEvaluation = best.Evaluation
            });
        }

        private static bool Solved(Candidate c, int targetCount) => c.Evaluation.Criterion != ContactIkCriterion.DistalPalmTargets && targetCount > 0 && c.TargetsReached && c.Safe &&
            (c.Evaluation.Criterion == ContactIkCriterion.FixedPointBand ? c.Evaluation.FixedPointsReviewed : c.Evaluation.ContactCoverageSatisfied);

        // Недостающий человеческий review/полное collision-доказательство не появляется от новых вращений q.
        private static bool SearchSatisfied(Candidate c, int targetCount) => Solved(c, targetCount) ||
            (c.Evaluation.Criterion == ContactIkCriterion.DistalPalmTargets && targetCount > 0 && c.TargetsReached) ||
            (c.Evaluation.Criterion == ContactIkCriterion.FixedPointBand && targetCount > 0 && c.TargetsReached && c.Evaluation.ConstraintViolation == 0);

        private static bool Better(Candidate a, Candidate b, double epsilon)
        {
            // В v2 fullskin — только диагностика, без скрытого лексикографического возврата collision guard.
            if (a.Evaluation.Criterion == ContactIkCriterion.DistalPalmTargets) return a.Cost + epsilon < b.Cost;
            // Надёжно безопасная поза никогда не заменяется кандидатом с недоказанной/плохой коллизией.
            if (a.Safe != b.Safe) return a.Safe;
            if (!a.Safe)
            {
                bool ar = a.Evaluation.Reliable && a.Evaluation.FullSurfaceChecked;
                bool br = b.Evaluation.Reliable && b.Evaluation.FullSurfaceChecked;
                if (ar != br) return ar;
                // У недостоверных измерений penalty не является доказанным ограничением, но поиск не обнуляется.
                if (ar && Math.Abs(a.Evaluation.ConstraintViolation - b.Evaluation.ConstraintViolation) > epsilon)
                    return a.Evaluation.ConstraintViolation < b.Evaluation.ConstraintViolation;
            }
            return a.Cost + epsilon < b.Cost;
        }

        private static Candidate Score(float[] q, ContactIkEvaluation evaluation, IReadOnlyList<ContactIkDof> dofs,
                                        IReadOnlyList<ContactIkTarget> targets, ContactIkSettings settings)
        {
            var c = new Candidate
            {
                Q = (float[])q.Clone(), Evaluation = evaluation, TargetsReached = true,
                Safe = settings.Criterion != ContactIkCriterion.DistalPalmTargets && evaluation.FullSurfaceChecked && evaluation.Reliable && evaluation.Feasible,
                Cost = settings.Criterion == ContactIkCriterion.DistalPalmTargets ? 0 : evaluation.SurfaceCost + (settings.Criterion == ContactIkCriterion.FixedPointBand ? 0 : evaluation.ContactCoveragePenalty),
                Errors = new ContactIkTargetError[targets.Count]
            };
            var patches = new Dictionary<string, ContactIkPatchObservation>(StringComparer.Ordinal);
            foreach (ContactIkPatchObservation p in evaluation.Patches) patches.Add(p.PatchId, p);
            for (int i = 0; i < targets.Count; i++)
            {
                ContactIkTarget t = targets[i];
                var e = new ContactIkTargetError { PatchId = t.PatchId, Satisfied = true };
                c.Errors[i] = e;
                if (settings.Criterion == ContactIkCriterion.DistalPalmTargets)
                {
                    e.Role = t.Role;
                    e.EffectiveWeight = t.Weight * (t.Role == ContactIkTargetRole.Intermediate ? 1 - settings.DistalPriority : 1);
                    // Отключённая secondary не превращает null/NaN в ноль или ложный PASS.
                    e.Ignored = t.Role == ContactIkTargetRole.Intermediate && settings.DistalPriority == 1;
                    e.Satisfied = false;
                    if (e.Ignored) continue;
                    if (!patches.TryGetValue(t.PatchId, out ContactIkPatchObservation v2Point))
                    { c.TargetsReached = false; c.Cost += e.EffectiveWeight * 1e6; continue; }
                    if (t.Role == ContactIkTargetRole.Intermediate)
                    {
                        bool measured = v2Point.UnsignedDistanceReliable && v2Point.UnsignedDistanceMeters.HasValue && Finite(v2Point.UnsignedDistanceMeters.Value) && v2Point.UnsignedDistanceMeters.Value >= 0;
                        e.UnsignedDistanceMeters = measured ? v2Point.UnsignedDistanceMeters : null;
                        e.BandExcessMeters = measured ? Mathf.Max(0, v2Point.UnsignedDistanceMeters.Value - ContactIkTarget.FixedPointBandMeters) : (float?)null;
                        e.Satisfied = measured && e.BandExcessMeters.Value == 0;
                        c.Cost += e.EffectiveWeight * (measured ? Square(e.BandExcessMeters.Value / ContactIkTarget.FixedPointBandMeters) : 1e6);
                    }
                    else
                    {
                        e.PositionErrorMeters = Vector3.Distance(v2Point.PositionMeters, t.PositionMeters);
                        e.Satisfied = e.PositionErrorMeters <= t.PositionToleranceMeters;
                        c.Cost += e.EffectiveWeight * Square(e.PositionErrorMeters / t.PositionToleranceMeters);
                    }
                    c.TargetsReached &= e.Satisfied;
                    continue;
                }
                if (settings.Criterion == ContactIkCriterion.FixedPointBand)
                {
                    bool measured = patches.TryGetValue(t.PatchId, out ContactIkPatchObservation point) && point.UnsignedDistanceReliable &&
                        point.UnsignedDistanceMeters.HasValue && Finite(point.UnsignedDistanceMeters.Value) && point.UnsignedDistanceMeters.Value >= 0;
                    e.Satisfied = measured && point.UnsignedDistanceMeters.Value <= ContactIkTarget.FixedPointBandMeters;
                    e.UnsignedDistanceMeters = measured ? point.UnsignedDistanceMeters : null;
                    e.BandExcessMeters = measured ? Mathf.Max(0, point.UnsignedDistanceMeters.Value - ContactIkTarget.FixedPointBandMeters) : (float?)null;
                    // Внутри полосы притяжение прекращается: нет цели XYZ, нормали или площади.
                    c.Cost += t.Weight * (measured ? Square(e.BandExcessMeters.Value / ContactIkTarget.FixedPointBandMeters) : 1e6);
                    if (!measured) c.Safe = false;
                    c.TargetsReached &= e.Satisfied;
                    continue;
                }
                if (!patches.TryGetValue(t.PatchId, out ContactIkPatchObservation p))
                    throw new ArgumentException("Callback не вернул стабильный участок: " + t.PatchId);
                if (t.Mode == ContactIkTargetMode.NoContact)
                {
                    e.ClearanceDeficitMeters = p.SignedClearanceMeters.HasValue ? Mathf.Max(0, t.MinimumClearanceMeters - p.SignedClearanceMeters.Value) : t.MinimumClearanceMeters;
                    e.Satisfied = p.ClearanceReliable && p.SignedClearanceMeters.HasValue && e.ClearanceDeficitMeters <= 1e-6f;
                    c.Cost += t.Weight * Square(e.ClearanceDeficitMeters / t.PositionToleranceMeters);
                    if (!p.ClearanceReliable || !p.SignedClearanceMeters.HasValue) c.Safe = false;
                }
                else if (t.Mode == ContactIkTargetMode.Contact)
                {
                    e.PositionErrorMeters = Vector3.Distance(p.PositionMeters, t.PositionMeters);
                    e.Satisfied = e.PositionErrorMeters <= t.PositionToleranceMeters;
                    c.Cost += t.Weight * Square(e.PositionErrorMeters / t.PositionToleranceMeters);
                    if (t.MatchNormal)
                    {
                        if (p.Normal.sqrMagnitude < 1e-10f) throw new ArgumentException("Нет измеренной нормали участка: " + t.PatchId);
                        e.NormalErrorDegrees = Vector3.Angle(p.Normal, t.Normal);
                        e.Satisfied &= e.NormalErrorDegrees <= t.NormalToleranceDegrees;
                        c.Cost += t.Weight * Square(e.NormalErrorDegrees / t.NormalToleranceDegrees);
                    }
                }
                c.TargetsReached &= e.Satisfied;
            }
            for (int i = 0; i < dofs.Count; i++)
                c.Cost += settings.PriorWeight * dofs[i].PriorWeight * Square(q[i] / dofs[i].PriorScaleDegrees);
            if (!Finite(c.Cost)) throw new ArgumentException("Стоимость IK не является конечным числом.");
            return c;
        }

        private static double Square(double value) => value * value;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

        private static void Validate(IReadOnlyList<ContactIkDof> dofs, IReadOnlyList<ContactIkTarget> targets,
                                     Func<float[], ContactIkEvaluation> evaluate, ContactIkSettings s)
        {
            if (dofs == null || targets == null || evaluate == null) throw new ArgumentNullException("Входы IK не заданы.");
            if (s.Criterion == ContactIkCriterion.ForwardSurfaceWrap) throw new ArgumentException("ForwardSurfaceWrap требует явный stage profile и ContactIkForwardWrap.BeginSolve.");
            if (!Enum.IsDefined(typeof(ContactIkCriterion), s.Criterion) || !Finite(s.DistalPriority) || s.DistalPriority < 0 || s.DistalPriority > 1 || s.MaxEvaluations < 2 || !Finite(s.InitialStepDegrees) || !Finite(s.MinimumStepDegrees) ||
                s.MinimumStepDegrees <= 0 || s.InitialStepDegrees < s.MinimumStepDegrees ||
                !Finite(s.PriorWeight) || s.PriorWeight < 0 || !Finite(s.ImprovementEpsilon) || s.ImprovementEpsilon < 0)
                throw new ArgumentException("Некорректный бюджет или параметры поиска IK.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ContactIkDof d in dofs)
            {
                if (d == null || string.IsNullOrEmpty(d.Id) || !ids.Add(d.Id) || !Finite(d.MinDegrees) || !Finite(d.MaxDegrees) ||
                    d.MinDegrees > d.MaxDegrees || !Finite(d.PriorScaleDegrees) || d.PriorScaleDegrees <= 0 ||
                    !Finite(d.PriorWeight) || d.PriorWeight < 0)
                    throw new ArgumentException("DOF должен иметь уникальный ID, конечные упорядоченные границы и масштаб prior > 0.");
            }
            ids.Clear();
            foreach (ContactIkTarget t in targets)
            {
                if (s.Criterion == ContactIkCriterion.DistalPalmTargets)
                {
                    if (t == null || string.IsNullOrEmpty(t.PatchId) || !ids.Add(t.PatchId) || !Enum.IsDefined(typeof(ContactIkTargetRole), t.Role) || t.Role == ContactIkTargetRole.Unspecified ||
                        (t.Role != ContactIkTargetRole.Palm && string.IsNullOrWhiteSpace(t.FingerId)) || !Finite(t.Weight) || t.Weight <= 0 ||
                        (t.Role == ContactIkTargetRole.Intermediate ? t.Mode != ContactIkTargetMode.FixedPointBand :
                         t.Mode != ContactIkTargetMode.Contact || !Finite(t.PositionMeters) || !Finite(t.PositionToleranceMeters) || t.PositionToleranceMeters <= 0))
                        throw new ArgumentException("v2 требует явную роль/finger, положительный вес и primary XYZ либо intermediate band.");
                    continue;
                }
                if (s.Criterion == ContactIkCriterion.FixedPointBand)
                {
                    if (t == null || string.IsNullOrEmpty(t.PatchId) || !ids.Add(t.PatchId) || t.Mode != ContactIkTargetMode.FixedPointBand || !Finite(t.Weight) || t.Weight < 0)
                        throw new ArgumentException("FixedPointBand требует уникальный material PatchId, одноимённый mode и конечный вес.");
                    continue;
                }
                if (t == null || string.IsNullOrEmpty(t.PatchId) || !ids.Add(t.PatchId) || !Finite(t.PositionMeters) ||
                    !Finite(t.PositionToleranceMeters) || t.PositionToleranceMeters <= 0 || !Finite(t.Weight) || t.Weight < 0 ||
                    !Finite(t.MinimumClearanceMeters) || t.MinimumClearanceMeters < 0 || !Enum.IsDefined(typeof(ContactIkTargetMode), t.Mode) || t.Mode == ContactIkTargetMode.FixedPointBand ||
                    (t.MatchNormal && (!Finite(t.Normal) || t.Normal.sqrMagnitude < 1e-10f ||
                     !Finite(t.NormalToleranceDegrees) || t.NormalToleranceDegrees <= 0 || t.NormalToleranceDegrees > 180)))
                    throw new ArgumentException("Некорректная цель IK или повторный материальный участок.");
            }
        }

        private static void ValidateEvaluation(ContactIkEvaluation e, Dictionary<string, ContactIkRegionObservation> baseline, bool first, ContactIkCriterion criterion)
        {
            if (e == null || e.Patches == null || e.Regions == null || !Finite(e.ConstraintViolation) || e.ConstraintViolation < 0 ||
                !Finite(e.SurfaceCost) || e.SurfaceCost < 0 || e.Criterion != criterion ||
                !Finite(e.DistalPriority) || e.DistalPriority < 0 || e.DistalPriority > 1 || !Finite(e.PalmTranslationObjectMeters) ||
                (criterion == ContactIkCriterion.LegacyAreaContact && (!Finite(e.ContactCoveragePenalty) || e.ContactCoveragePenalty < 0)))
                throw new ArgumentException("Некорректный результат полного геометрического validator.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ContactIkPatchObservation p in e.Patches)
                if (p == null || string.IsNullOrEmpty(p.PatchId) || !ids.Add(p.PatchId) || !Finite(p.PositionMeters) ||
                    (criterion == ContactIkCriterion.LegacyAreaContact && !Finite(p.Normal)) || (p.SignedClearanceMeters.HasValue && !Finite(p.SignedClearanceMeters.Value)) ||
                    (p.ClearanceReliable && !p.SignedClearanceMeters.HasValue))
                    throw new ArgumentException("Некорректное наблюдение материального участка.");
            if (criterion != ContactIkCriterion.LegacyAreaContact) return;
            ids.Clear();
            foreach (ContactIkRegionObservation r in e.Regions)
            {
                if (r == null || string.IsNullOrEmpty(r.RegionId) || string.IsNullOrEmpty(r.SampleSetId) || !ids.Add(r.RegionId) ||
                    !Finite(r.FixedAreaMm2) || r.FixedAreaMm2 <= 0 || !Finite(r.NearAreaMm2) || r.NearAreaMm2 < 0 ||
                    r.NearAreaMm2 > r.FixedAreaMm2 + 1e-6 ||
                    (r.PenetrationAreaMm2.HasValue && (!Finite(r.PenetrationAreaMm2.Value) || r.PenetrationAreaMm2.Value < 0 || r.PenetrationAreaMm2.Value > r.FixedAreaMm2 + 1e-6)) ||
                    (r.MaxPenetrationMm.HasValue && (!Finite(r.MaxPenetrationMm.Value) || r.MaxPenetrationMm.Value < 0)))
                    throw new ArgumentException("Некорректная площадь/идентичность потенциальной контактной области.");
                if (baseline.TryGetValue(r.RegionId, out ContactIkRegionObservation prior))
                {
                    if (prior.SampleSetId != r.SampleSetId || Math.Abs(prior.FixedAreaMm2 - r.FixedAreaMm2) > 1e-6)
                        throw new ArgumentException("Validator изменил материальную маску/знаменатель: " + r.RegionId);
                }
                else if (first) baseline.Add(r.RegionId, new ContactIkRegionObservation { SampleSetId = r.SampleSetId, FixedAreaMm2 = r.FixedAreaMm2 });
                else throw new ArgumentException("Validator добавил новую контактную область: " + r.RegionId);
            }
            if (ids.Count != baseline.Count) throw new ArgumentException("Validator изменил набор контактных областей.");
            // Области могут отсутствовать лишь в недоказанном preview: Solved без площади запрещён.
            if (e.ContactCoverageSatisfied && e.Regions.Length == 0)
                throw new ArgumentException("Покрытие контакта заявлено без измеренных потенциальных областей.");
        }

        private static ContactIkEvaluation Snapshot(ContactIkEvaluation source)
        {
            if (source == null || source.Patches == null || source.Regions == null)
                throw new ArgumentException("Callback не вернул полный результат измерения.");
            var e = new ContactIkEvaluation
            {
                Criterion = source.Criterion, FixedPointsReviewed = source.FixedPointsReviewed,
                DistalPriority = source.DistalPriority, PalmTranslationObjectMeters = source.PalmTranslationObjectMeters,
                FullSurfaceChecked = source.FullSurfaceChecked, Reliable = source.Reliable, Feasible = source.Feasible,
                ContactCoverageSatisfied = source.ContactCoverageSatisfied, ConstraintViolation = source.ConstraintViolation,
                SurfaceCost = source.SurfaceCost, ContactCoveragePenalty = source.ContactCoveragePenalty, Diagnostic = source.Diagnostic,
                Patches = new ContactIkPatchObservation[source.Patches.Length], Regions = new ContactIkRegionObservation[source.Regions.Length]
            };
            for (int i = 0; i < e.Patches.Length; i++)
            {
                ContactIkPatchObservation p = source.Patches[i];
                if (p != null) e.Patches[i] = new ContactIkPatchObservation
                {
                    PatchId = p.PatchId, PositionMeters = p.PositionMeters, Normal = p.Normal,
                    SignedClearanceMeters = p.SignedClearanceMeters, ClearanceReliable = p.ClearanceReliable,
                    UnsignedDistanceMeters = p.UnsignedDistanceMeters, UnsignedDistanceReliable = p.UnsignedDistanceReliable
                };
            }
            for (int i = 0; i < e.Regions.Length; i++)
            {
                ContactIkRegionObservation r = source.Regions[i];
                if (r != null) e.Regions[i] = new ContactIkRegionObservation
                {
                    RegionId = r.RegionId, SampleSetId = r.SampleSetId, FixedAreaMm2 = r.FixedAreaMm2,
                    NearAreaMm2 = r.NearAreaMm2, PenetrationAreaMm2 = r.PenetrationAreaMm2, MaxPenetrationMm = r.MaxPenetrationMm
                };
            }
            return e;
        }
    }
}
