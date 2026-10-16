using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.Editor.HandGeometry;
using VrBattlegrounds.Editor.HandPoseReview;

namespace VrBattlegrounds.Editor.Avatars.ContactIk
{
    /// <summary>Editor-only связка ограниченного IK с фактическим BakeMesh и фиксированной контактной маской.</summary>
    public sealed class ContactIkPreviewSession : IDisposable
    {
        internal static readonly string[] RegionNames = { "palm", "thumb", "index", "middle", "ring", "little" };
        private readonly GripTargetRig _rig;
        private readonly UxrHandSide _side;
        private readonly SkinnedMeshRenderer[] _renderers;
        private readonly Mesh[] _sourceMeshes;
        private readonly bool _rendererSelectionReviewed;
        private readonly Matrix4x4 _worldToObject;
        private readonly FitTriangle[] _object;
        private readonly FitSurface _surface;
        private readonly FitTopology _topology;
        private readonly HandPosePotentialContactMask _mask;
        private readonly Dictionary<string, double> _referenceAreas = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly string _fingerprint, _sampleSetId;
        private readonly ContactIkDof[] _dofs;
        private readonly ContactIkPatchBinding[] _bindings;
        private readonly ContactIkPreviewPolicy _policy;
        private readonly ContactIkObjectEvidence _objectEvidence;
        private readonly Func<Vector3, FitNearestHit, HandPosePotentialContactSign> _sign;
        private readonly Func<ContactIkCollisionInput, ContactIkCollisionValidation> _collisionValidator;
        private readonly ContactIkRigDriver _driver;
        private bool _disposed, _finalApplied;
        public ContactIkPreviewObservation LastObservation { get; private set; }

        public ContactIkPreviewSession(GripTargetRig rig, UxrHandSide side, IReadOnlyList<SkinnedMeshRenderer> renderers,
            bool rendererSelectionReviewed, Matrix4x4 worldToObject, FitTriangle[] fullObject,
            HandPosePotentialContactMask mask, string bindingFingerprint, IReadOnlyList<ContactIkDof> dofs,
            IReadOnlyList<ContactIkPatchBinding> bindings, ContactIkPreviewPolicy policy, ContactIkObjectEvidence objectEvidence,
            Func<Vector3, FitNearestHit, HandPosePotentialContactSign> signClassifier = null,
            Func<ContactIkCollisionInput, ContactIkCollisionValidation> collisionValidator = null)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig)); _side = side;
            ContactIkSurfaceAnchor.ValidateUniformFrame(worldToObject, true);
            _worldToObject = worldToObject;
            _renderers = ValidateRenderers(rig, renderers);
            _sourceMeshes = _renderers.Select(r => r.sharedMesh).ToArray();
            _rendererSelectionReviewed = rendererSelectionReviewed;
            if (fullObject == null || fullObject.Length == 0 || dofs == null || bindings == null || policy == null || objectEvidence == null)
                throw new ArgumentException("Нужны полный предмет, DOF, явные patch bindings, policy и object evidence.");
            _object = (FitTriangle[])fullObject.Clone();
            foreach (FitTriangle t in _object)
                if (!ContactIkSurfaceAnchor.Finite(t.A) || !ContactIkSurfaceAnchor.Finite(t.B) || !ContactIkSurfaceAnchor.Finite(t.C) || t.Area <= 1e-12f)
                    throw new ArgumentException("Полная поверхность предмета содержит нечисловую/вырожденную грань.");
            _surface = new FitSurface(_object); _topology = HandPoseFitGeometry.Topology(_object);
            _mask = CopyMask(mask, bindingFingerprint); _fingerprint = bindingFingerprint;
            _sampleSetId = MaskId(_mask);
            foreach (string region in RegionNames)
            {
                var entries = _mask.Triangles.Where(t => t.Region == region && t.Weight > 0).ToArray();
                if (entries.Length == 0 || entries.Any(t => !t.ReferenceAreaMm2.HasValue || !ContactIkSurfaceAnchor.Finite(t.ReferenceAreaMm2.Value) || t.ReferenceAreaMm2 <= 0)) continue;
                double reference = entries.Sum(t => t.ReferenceAreaMm2.Value * t.Weight);
                if (ContactIkSurfaceAnchor.Finite(reference) && reference > 0) _referenceAreas.Add(region, reference);
            }
            _policy = CopyPolicy(policy);
            _objectEvidence = new ContactIkObjectEvidence
            { GeometryReviewed = objectEvidence.GeometryReviewed, FullAssembly = objectEvidence.FullAssembly, OutwardOrientationReviewed = objectEvidence.OutwardOrientationReviewed, Evidence = objectEvidence.Evidence };
            _sign = signClassifier; _collisionValidator = collisionValidator;
            _dofs = dofs.Select(d => d == null ? throw new ArgumentException("Пустой DOF.") : new ContactIkDof
            { Id = d.Id, Bone = d.Bone, LocalAxis = d.LocalAxis, MinDegrees = d.MinDegrees, MaxDegrees = d.MaxDegrees, PriorScaleDegrees = d.PriorScaleDegrees, PriorWeight = d.PriorWeight }).ToArray();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            _bindings = bindings.Select(b =>
            {
                if (b?.Anchor == null || b.Target == null || string.IsNullOrWhiteSpace(b.Target.PatchId) || !ids.Add(b.Target.PatchId))
                    throw new ArgumentException("Нужны уникальные PatchId и явные материальные anchors.");
                b.Anchor.Validate();
                return new ContactIkPatchBinding { Anchor = b.Anchor.Copy(), Target = CopyTarget(b.Target), CorrespondenceReviewed = b.CorrespondenceReviewed, Provenance = b.Provenance };
            }).ToArray();
            // Создаём writer последним: все проверки входов выше не меняют позу.
            _driver = new ContactIkRigDriver(rig, side, _dofs);
        }

        public ContactIkResult Solve(ContactIkSettings settings = null)
        {
            CheckAlive(); _finalApplied = false;
            return ContactIkSolver.Solve(_dofs, _bindings.Select(b => b.Target).ToArray(), q => Evaluate(q).Evaluation, settings);
        }

        public ContactIkPreviewObservation Evaluate(IReadOnlyList<float> degrees)
        {
            CheckAlive(); _finalApplied = false;
            _driver.Apply(degrees);
            for (int i = 0; i < _renderers.Length; i++)
                if (!_renderers[i] || _renderers[i].sharedMesh != _sourceMeshes[i]) throw new InvalidOperationException("Во время IK заменён mesh выбранного renderer.");
            FitTriangle[] hand = CaptureHandTriangles(_rig, _side, _renderers, _worldToObject);
            bool signsKnown = _sign != null;
            HandPosePotentialContactSign Sign(Vector3 p, FitNearestHit h)
            {
                HandPosePotentialContactSign value = _sign == null ? new HandPosePotentialContactSign { Reason = "sign_classifier_not_supplied" } : _sign(p, h);
                if (value.Confidence.HasValue && (!ContactIkSurfaceAnchor.Finite(value.Confidence.Value) || value.Confidence < 0 || value.Confidence > 1))
                    throw new ArgumentException("Неверная sign confidence.");
                if (!value.Inside.HasValue) signsKnown = false;
                return value;
            }
            var coverage = HandPosePotentialContactCoverage.Evaluate(hand, _surface, _mask, _fingerprint, _policy.Coverage, Sign);
            ContactIkPreviewCollisionDiagnostics collision = ContactIkPreviewCollision.Measure(hand, _surface, _policy, Sign);
            var patches = new ContactIkPatchObservation[_bindings.Length];
            var anchors = new ContactIkPreviewAnchorObservation[_bindings.Length];
            for (int i = 0; i < _bindings.Length; i++)
            {
                ContactIkAnchorSample p = _bindings[i].Anchor.Evaluate(hand);
                FitNearestHit hit = _surface.NearestHit(p.PositionMeters);
                float distance = (hit.Position - p.PositionMeters).magnitude;
                HandPosePotentialContactSign sign = Sign(p.PositionMeters, hit);
                float? signed = sign.Inside.HasValue ? (sign.Inside.Value ? -distance : distance) : (float?)null;
                patches[i] = new ContactIkPatchObservation
                { PatchId = _bindings[i].Target.PatchId, PositionMeters = p.PositionMeters, Normal = p.Normal, SignedClearanceMeters = signed, ClearanceReliable = signed.HasValue };
                anchors[i] = new ContactIkPreviewAnchorObservation
                { PatchId = patches[i].PatchId, PositionMeters = p.PositionMeters, Normal = p.Normal, UnsignedDistanceMm = distance * 1000, SignedDistanceMm = signed.HasValue ? signed.Value * 1000 : (double?)null, Sign = sign };
            }
            ContactIkCollisionValidation validation = _collisionValidator?.Invoke(new ContactIkCollisionInput
            { Hand = Array.AsReadOnly(hand), Object = Array.AsReadOnly(_object), Sampled = collision, Policy = CopyPolicy(_policy) });
            ValidateCollisionResult(validation);
            double depth = Math.Max(collision.KnownSampledMaxDepthMm, validation?.MaximumPenetrationMm ?? 0);
            // Выборочная площадь — оценка, не нижняя граница; полный validator при наличии имеет приоритет.
            double area = validation?.PenetrationAreaMm2 ?? collision.KnownSampledPenetrationAreaMm2;
            long pairs = Math.Max(collision.IntersectionPairs, validation?.IntersectionPairs ?? 0);
            double violation = Math.Max(0, depth - _policy.MaximumPenetrationMm) / Math.Max(1, _policy.MaximumPenetrationMm)
                + Math.Max(0, area - _policy.MaximumPenetrationAreaMm2) / Math.Max(1, _policy.MaximumPenetrationAreaMm2)
                + Math.Max(0, pairs - _policy.MaximumIntersectionPairs);
            bool coverageSatisfied = coverage.ValidNumericCoverage && coverage.ReadyForComparison && coverage.Total?.ReferencePotentialAreaMm2 > 0;
            double coveragePenalty = 0;
            var regions = new List<ContactIkRegionObservation>();
            foreach (string name in RegionNames)
            {
                HandPosePotentialContactCoverageRegion r = coverage.Regions.FirstOrDefault(v => v.Region == name);
                double fraction = r?.ReferenceWeightedContactPercent / 100 ?? 0;
                double deficit = Math.Max(0, _policy.MinimumCoverage[name] - fraction);
                coveragePenalty += deficit * deficit;
                coverageSatisfied &= deficit <= 1e-12;
                if (_referenceAreas.TryGetValue(name, out double referenceArea))
                    regions.Add(new ContactIkRegionObservation
                    {
                        RegionId = name, SampleSetId = _sampleSetId + ":" + name, FixedAreaMm2 = referenceArea,
                        NearAreaMm2 = referenceArea * fraction,
                        // Текущие площади/выборочные глубины остаются в Coverage; полной эталонно взвешенной оценки здесь нет.
                        PenetrationAreaMm2 = null, MaxPenetrationMm = null
                    });
                else if (_policy.MinimumCoverage[name] > 0) coverageSatisfied = false;
            }
            bool correspondenceReviewed = _bindings.Length > 0 && _bindings.All(b => b.CorrespondenceReviewed && !string.IsNullOrWhiteSpace(b.Provenance));
            bool objectReviewed = _objectEvidence.GeometryReviewed && _objectEvidence.FullAssembly && _objectEvidence.OutwardOrientationReviewed && !string.IsNullOrWhiteSpace(_objectEvidence.Evidence);
            bool complete = validation != null && validation.Complete && _rendererSelectionReviewed && _objectEvidence.FullAssembly;
            bool consistent = validation != null && (!validation.MaximumPenetrationMm.HasValue || validation.MaximumPenetrationMm.Value + 1e-6 >= collision.KnownSampledMaxDepthMm) &&
                (!validation.IntersectionPairs.HasValue || validation.IntersectionPairs.Value >= collision.IntersectionPairs);
            bool reliable = complete && validation.Reliable && objectReviewed && _topology.CanDetermineInside && signsKnown &&
                coverage.ValidNumericCoverage && collision.DegenerateHandTriangles == 0 && correspondenceReviewed && consistent;
            string diagnostic = "BakeMesh всей выбранной кисти; coverage=" + coverage.Status + "; reviewed-mask=" + coverage.ReadyForComparison +
                "; object=" + _topology.Reason + "; object-reviewed=" + objectReviewed + "; sign-known=" + signsKnown +
                "; collision-samples=" + collision.Samples + "; intersection-tests=" + collision.IntersectionPairTests + "/" + collision.IntersectionPairBudget +
                "; intersection-pass-complete=" + collision.IntersectionPassComplete + "; external-complete=" + (validation?.Complete ?? false) +
                "; correspondence-reviewed=" + correspondenceReviewed + "; validator-consistent=" + consistent + "; выборочная collision не гарантирует полную оценку проникновения.";
            if (coverage.Errors.Count > 0) diagnostic += " Coverage errors: " + string.Join(" | ", coverage.Errors);
            var evaluation = new ContactIkEvaluation
            {
                Patches = patches, Regions = regions.ToArray(), FullSurfaceChecked = complete, Reliable = reliable,
                Feasible = complete && violation <= 1e-12,
                ContactCoverageSatisfied = coverageSatisfied, ConstraintViolation = violation,
                ContactCoveragePenalty = _policy.CoveragePenaltyWeight * coveragePenalty,
                SurfaceCost = _policy.CollisionPenaltyWeight * violation, Diagnostic = diagnostic
            };
            LastObservation = new ContactIkPreviewObservation
            {
                Evaluation = evaluation, Coverage = coverage, Collision = collision, ExternalValidation = validation, ObjectTopology = _topology,
                CapturedRenderers = _renderers.Length, HandTriangles = hand.Length, AllQueriedSignsKnown = signsKnown,
                RendererSelectionReviewed = _rendererSelectionReviewed, CorrespondencesReviewed = correspondenceReviewed,
                CurrentPotentialAreaMm2 = coverage.Total?.CurrentPotentialAreaMm2 ?? 0,
                ReferencePotentialAreaMm2 = coverage.Total?.ReferencePotentialAreaMm2, Anchors = anchors
            };
            return LastObservation;
        }

        public void ApplyFinal(IReadOnlyList<float> degrees) { CheckAlive(); _driver.Apply(degrees); _finalApplied = true; }

        public UxrHandDescriptor CaptureDescriptor()
        {
            CheckAlive();
            if (!_finalApplied) throw new InvalidOperationException("Перед descriptor capture нужен явный ApplyFinal выбранного q.");
            return _driver.CaptureDescriptor();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _driver.Dispose(); _disposed = true;
            // HandMeshCapture уничтожает каждый временный Mesh в собственном finally; сохранённых native mesh здесь нет.
        }

        public static FitTriangle[] CaptureHandTriangles(GripTargetRig rig, UxrHandSide side,
            IReadOnlyList<SkinnedMeshRenderer> renderers, Matrix4x4 worldToObject)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig));
            ContactIkSurfaceAnchor.ValidateUniformFrame(worldToObject, true);
            var selected = ValidateRenderers(rig, renderers);
            UxrAvatarHand hand = rig.Hand(side);
            var allowed = new HashSet<Transform>(hand.Transforms);
            foreach (Transform bone in hand.Transforms) foreach (Transform child in bone.GetComponentsInChildren<Transform>(true)) allowed.Add(child);
            var zones = new Dictionary<Transform, string>(); var segments = new Dictionary<Transform, string>();
            UxrAvatarFinger[] fingers = { hand.Thumb, hand.Index, hand.Middle, hand.Ring, hand.Little };
            for (int f = 0; f < fingers.Length; f++)
            {
                Transform[] nodes = { fingers[f].Metacarpal, fingers[f].Proximal, fingers[f].Intermediate, fingers[f].Distal };
                string[] names = { "metacarpal", "proximal", "intermediate", "distal" };
                for (int i = 0; i < nodes.Length; i++) if (nodes[i])
                    foreach (Transform child in nodes[i].GetComponentsInChildren<Transform>(true)) { zones[child] = RegionNames[f + 1]; segments[child] = RegionNames[f + 1] + "/" + names[i]; }
            }
            var result = new List<FitTriangle>();
            foreach (SkinnedMeshRenderer skin in selected)
            {
                HandMeshSample sample = HandMeshCapture.Capture(skin, worldToObject, rig.Avatar.transform, false);
                var weights = new float[sample.Vertices.Length]; var dominant = new int[sample.Vertices.Length];
                for (int i = 0; i < weights.Length; i++)
                {
                    dominant[i] = -1; float best = 0;
                    for (int j = sample.WeightOffsets[i]; j < sample.WeightOffsets[i + 1]; j++)
                    {
                        HandBoneWeight w = sample.Weights[j]; Transform bone = sample.Bones[w.BoneIndex];
                        if (!bone || !allowed.Contains(bone)) continue;
                        weights[i] += w.Weight;
                        if (w.Weight > best) { best = w.Weight; dominant[i] = w.BoneIndex; }
                    }
                }
                string path = HandPoseFitSnapshot.HumanPath(skin.transform, rig.Avatar.transform);
                for (int sub = 0; sub < sample.SubmeshTriangles.Length; sub++)
                {
                    int[] ix = sample.SubmeshTriangles[sub];
                    for (int i = 0; i < ix.Length; i += 3)
                    {
                        int a = ix[i], b = ix[i + 1], c = ix[i + 2];
                        if ((weights[a] + weights[b] + weights[c]) / 3 < .5f) continue;
                        int di = new[] { a, b, c }.Where(v => dominant[v] >= 0).GroupBy(v => dominant[v])
                            .OrderByDescending(g => g.Sum(v => weights[v])).Select(g => g.Key).DefaultIfEmpty(-1).First();
                        if (di < 0) continue;
                        Transform bone = sample.Bones[di];
                        result.Add(new FitTriangle(sample.Vertices[a], sample.Vertices[b], sample.Vertices[c], zones.TryGetValue(bone, out string zone) ? zone : "palm_wrist")
                        { Source = path, SourceSubMesh = sub, SourceTriangle = i / 3, FingerSegment = segments.TryGetValue(bone, out string segment) ? segment : "palm_wrist" });
                    }
                }
            }
            if (result.Count == 0 || !result.Any(t => t.Area > 1e-12f)) throw new InvalidOperationException("Выбранные renderer не дали кожи заданной руки.");
            return result.ToArray();
        }

        private static SkinnedMeshRenderer[] ValidateRenderers(GripTargetRig rig, IReadOnlyList<SkinnedMeshRenderer> renderers)
        {
            if (rig == null || renderers == null || renderers.Count == 0 || !rig.Avatar || EditorUtility.IsPersistent(rig.Avatar) ||
                !EditorSceneManager.IsPreviewScene(rig.Avatar.gameObject.scene) || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new ArgumentException("Нужны явно выбранные renderer одной editor preview-копии вне Play.");
            var unique = new HashSet<SkinnedMeshRenderer>(); var paths = new HashSet<string>(StringComparer.Ordinal);
            var commonLods = new Dictionary<LODGroup, HashSet<int>>();
            LODGroup[] groups = rig.Avatar.GetComponentsInChildren<LODGroup>(true);
            foreach (SkinnedMeshRenderer skin in renderers)
            {
                if (!skin || !skin.sharedMesh || !skin.transform.IsChildOf(rig.Avatar.transform) || !unique.Add(skin) ||
                    !paths.Add(HandPoseFitSnapshot.HumanPath(skin.transform, rig.Avatar.transform)))
                    throw new ArgumentException("Renderer должен быть уникальным потомком выбранного avatar с уникальным HumanPath.");
                foreach (LODGroup group in groups)
                {
                    LOD[] levels = group.GetLODs();
                    int[] memberships = Enumerable.Range(0, levels.Length).Where(i => levels[i].renderers.Contains(skin)).ToArray();
                    if (memberships.Length == 0) continue;
                    // Один меш кисти штатно переиспользуется в нескольких LOD. Проверяем совместимость
                    // выбранных renderer внутри каждой группы; один и тот же renderer запекается один раз.
                    if (!commonLods.TryGetValue(group, out HashSet<int> available))
                    {
                        available = new HashSet<int>(memberships);
                        commonLods.Add(group, available);
                    }
                    else available.IntersectWith(memberships);
                    if (available.Count == 0) throw new ArgumentException("Выбранные renderer требуют разных LOD одной группы.");
                }
            }
            return renderers.ToArray();
        }

        private static ContactIkTarget CopyTarget(ContactIkTarget t) => new ContactIkTarget
        { PatchId = t.PatchId, Mode = t.Mode, PositionMeters = t.PositionMeters, Normal = t.Normal, MatchNormal = t.MatchNormal, PositionToleranceMeters = t.PositionToleranceMeters, NormalToleranceDegrees = t.NormalToleranceDegrees, MinimumClearanceMeters = t.MinimumClearanceMeters, Weight = t.Weight };

        private static ContactIkPreviewPolicy CopyPolicy(ContactIkPreviewPolicy p) => new ContactIkPreviewPolicy(p.MaximumPenetrationMm,
            p.MaximumPenetrationAreaMm2, p.MaximumIntersectionPairs, p.MinimumCoverage.ToDictionary(v => v.Key, v => v.Value), p.Coverage,
            p.CollisionSamples, p.CollisionSeed, p.IntersectionPairBudget, p.CoveragePenaltyWeight, p.CollisionPenaltyWeight);

        private static HandPosePotentialContactMask CopyMask(HandPosePotentialContactMask mask, string fingerprint)
        {
            if (mask?.Metadata == null || mask.Regions == null || mask.Triangles == null || string.IsNullOrWhiteSpace(fingerprint) || mask.Metadata.BindingFingerprint != fingerprint)
                throw new ArgumentException("Нет фиксированной маски с совпадающим binding fingerprint.");
            return new HandPosePotentialContactMask
            {
                Version = mask.Version,
                Metadata = new HandPosePotentialContactMaskMetadata { BindingFingerprint = fingerprint, Reviewed = mask.Metadata.Reviewed, Provenance = mask.Metadata.Provenance },
                Regions = mask.Regions.Select(r => r == null ? throw new ArgumentException("Пустой регион маски.") : new HandPosePotentialContactMaskRegion { Region = r.Region }).ToList(),
                Triangles = mask.Triangles.Select(t => t == null ? throw new ArgumentException("Пустая грань маски.") : new HandPosePotentialContactMaskTriangle
                { Source = t.Source, SourceSubMesh = t.SourceSubMesh, SourceTriangle = t.SourceTriangle, Region = t.Region, Weight = t.Weight, ReferenceAreaMm2 = t.ReferenceAreaMm2 }).ToList()
            };
        }

        private static string MaskId(HandPosePotentialContactMask mask)
        {
            var text = new StringBuilder(mask.Metadata.BindingFingerprint);
            foreach (var t in mask.Triangles.OrderBy(t => t.Source, StringComparer.Ordinal).ThenBy(t => t.SourceSubMesh).ThenBy(t => t.SourceTriangle))
                text.Append('\n').Append(t.Source?.Length ?? 0).Append(':').Append(t.Source).Append(':').Append(t.SourceSubMesh).Append(':').Append(t.SourceTriangle)
                    .Append(':').Append(t.Region).Append(':').Append(t.Weight.ToString("R", CultureInfo.InvariantCulture))
                    .Append(':').Append(t.ReferenceAreaMm2?.ToString("R", CultureInfo.InvariantCulture) ?? "unknown");
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
        }

        private static void ValidateCollisionResult(ContactIkCollisionValidation v)
        {
            if (v == null) return;
            if ((v.MaximumPenetrationMm.HasValue && (!ContactIkSurfaceAnchor.Finite(v.MaximumPenetrationMm.Value) || v.MaximumPenetrationMm < 0)) ||
                (v.PenetrationAreaMm2.HasValue && (!ContactIkSurfaceAnchor.Finite(v.PenetrationAreaMm2.Value) || v.PenetrationAreaMm2 < 0)) || v.IntersectionPairs < 0 ||
                (v.Complete && (!v.MaximumPenetrationMm.HasValue || !v.PenetrationAreaMm2.HasValue || !v.IntersectionPairs.HasValue || string.IsNullOrWhiteSpace(v.Evidence))))
                throw new ArgumentException("Complete collision validator должен вернуть явные конечные границы и evidence.");
        }

        private void CheckAlive() { if (_disposed) throw new ObjectDisposedException(nameof(ContactIkPreviewSession)); }
    }
}
