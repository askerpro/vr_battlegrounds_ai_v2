using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Editor.Avatars;
using VrBattlegrounds.Editor.Avatars.ContactIk;
using VrBattlegrounds.Editor.HandPoseReview;

namespace VrBattlegrounds.Editor.GripAuthoring.ContactPoints
{
    /// <summary>Связка одного редакторского хвата с v2. Окно хранит только снимки; каждый расчёт открывает и освобождает свои копии.</summary>
    public static class HandContactIkPreviewCapture
    {
        public static HandContactIkPreviewWindow OpenPrepared(HandPoseFitRequest request, HandPhalanxProbeSet probes,
            float maximumPalmTranslationMeters, int maximumEvaluations = 96, bool seedUnreviewedTargets = false)
        {
            var prepared = Prepare(request, probes, maximumPalmTranslationMeters, maximumEvaluations, seedUnreviewedTargets);
            return HandContactIkPreviewWindow.OpenPrepared(prepared.View, prepared.Run);
        }

        public static PreparedCase Prepare(HandPoseFitRequest supplied, HandPhalanxProbeSet probes,
            float maximumPalmTranslationMeters, int maximumEvaluations = 96, bool seedUnreviewedTargets = false)
        {
            if (supplied == null || probes == null) throw new ArgumentNullException("Нужны конкретный хват и выбранные опорные точки.");
            if (!Finite(maximumPalmTranslationMeters) || maximumPalmTranslationMeters < 0 || maximumEvaluations < 2)
                throw new ArgumentException("Нужны явные конечные предел перемещения ладони и бюджет расчёта.");
            var request = Resolve(supplied);
            var reference = HandContactProbeCapture.Capture(request.AvatarPrefab, request.Side);
            var bindings = reference.CreateIkBindings(probes);
            var anchors = bindings.Select(b => new HandContactIkPreviewAnchor(b.Target.PatchId,
                b.Target.PatchId.EndsWith("/distal", StringComparison.Ordinal) ? ContactIkTargetRole.Distal : ContactIkTargetRole.Intermediate,
                b.Target.PatchId.Split('/')[0], b.Anchor, b.Provenance)).ToList();
            anchors.Add(new HandContactIkPreviewAnchor("palm", ContactIkTargetRole.Palm, null,
                PalmAnchor(reference), "reference SDK palm center → palmar skin ray; unreviewed"));
            using (var setup = new Setup(request))
            {
                string objectFingerprint = ContactIkPatchBinding.ComputeObjectFingerprint(setup.Object);
                var before = ContactIkPreviewSession.CaptureHandTriangles(setup.Rig, request.Side, setup.Renderers, setup.WorldToObject);
                foreach (var anchor in anchors) anchor.SkinAnchor.Evaluate(before);
                var targets = new List<HandContactIkPreviewTarget>();
                if (seedUnreviewedTargets)
                {
                    // Только явный opt-in кандидат: один раз проецируем начальную точку и затем сохраняем материал объекта.
                    // Это не источник клипа, не подтверждённое соответствие и не nearest-цель на шагах IK.
                    foreach (var anchor in anchors.Where(a => a.Role != ContactIkTargetRole.Intermediate))
                    {
                        Vector3 point = anchor.SkinAnchor.Evaluate(before).PositionMeters;
                        targets.Add(new HandContactIkPreviewTarget(anchor.Id, Project(setup.Object, point),
                            "unreviewed one-time initial-skin projection; not source-clip correspondence"));
                    }
                }
                string caseId = System.IO.Path.GetFileNameWithoutExtension(request.ObjectPath) + "-" + request.Side + "-" + request.GrabPoint;
                Vector3 palmar = setup.WorldToObject.MultiplyVector(setup.Rig.PalmOut(request.Side)).normalized;
                Vector3 upward = setup.WorldToObject.MultiplyVector(setup.Rig.Hand(request.Side).Middle.Proximal.position - setup.Rig.Hand(request.Side).Wrist.position);
                upward = Vector3.ProjectOnPlane(upward, palmar).normalized;
                if (upward.sqrMagnitude < 1e-10f) throw new InvalidOperationException("Не определена ладонная рамка камеры.");
                var view = new HandContactIkPreviewView(caseId,
                    "Фактическая поза " + request.PoseOverridePath + "; шесть заданных целей, промежуточные точки — в таблице результата. Цели и кожа требуют проверки. Pistol support исключена.",
                    reference.BindingFingerprint, objectFingerprint, before, setup.Object,
                    anchors.Where(a => a.Role != ContactIkTargetRole.Intermediate).ToArray(), targets.ToArray(), Quaternion.LookRotation(-palmar, upward));
                return new PreparedCase(request, view, anchors.ToArray(), maximumPalmTranslationMeters, maximumEvaluations, Dependencies(request));
            }
        }

        public sealed class PreparedCase
        {
            public HandContactIkPreviewView View { get; }
            readonly HandPoseFitRequest _request;
            readonly HandContactIkPreviewAnchor[] _anchors;
            readonly float _maximumPalmTranslation;
            readonly int _maximumEvaluations;
            readonly string _dependencies;

            internal PreparedCase(HandPoseFitRequest request, HandContactIkPreviewView view, HandContactIkPreviewAnchor[] anchors,
                float maximumPalmTranslation, int maximumEvaluations, string dependencies)
            {
                _request = request.Copy(); View = view; _anchors = anchors;
                _maximumPalmTranslation = maximumPalmTranslation; _maximumEvaluations = maximumEvaluations; _dependencies = dependencies;
            }

            public HandContactIkPreviewResult Run(float priority, HandContactIkPreviewTarget[] targets)
            {
                if (!Finite(priority) || priority < 0 || priority > 1) throw new ArgumentOutOfRangeException(nameof(priority));
                if (Dependencies(_request) != _dependencies) throw new InvalidOperationException("Входные assets изменились; заново подготовьте снимок.");
                if (targets == null || targets.Any(t => t == null) || targets.Select(t => t.Id).Distinct().Count() != targets.Length)
                    throw new ArgumentException("Нужны уникальные явные цели объекта.");
                var primaryIds = _anchors.Where(a => a.Role != ContactIkTargetRole.Intermediate).Select(a => a.Id).OrderBy(x => x).ToArray();
                if (!primaryIds.SequenceEqual(targets.Select(t => t.Id).OrderBy(x => x)))
                    throw new ArgumentException("Назначьте все пять конечных фаланг и ладонь; лишние цели не допускаются.");
                using (var setup = new Setup(_request))
                {
                    string fingerprint = ContactIkPatchBinding.ComputeObjectFingerprint(setup.Object);
                    if (fingerprint != View.ObjectFingerprint) throw new InvalidOperationException("Геометрия объекта изменилась; surface anchors устарели.");
                    var surfaceKeys = setup.Object.Select(t => new HandPosePotentialContactTriangleKey(t.Source, t.SourceSubMesh, t.SourceTriangle)).ToArray();
                    var bindings = _anchors.Select(a => new ContactIkPatchBinding
                    {
                        Anchor = a.SkinAnchor, ObjectFingerprint = fingerprint,
                        ObjectAnchor = a.Role == ContactIkTargetRole.Intermediate ? null : targets.Single(t => t.Id == a.Id).ObjectAnchor,
                        PermittedSurface = a.Role == ContactIkTargetRole.Intermediate ? surfaceKeys : Array.Empty<HandPosePotentialContactTriangleKey>(),
                        Target = new ContactIkTarget { PatchId = a.Id, Role = a.Role, FingerId = a.FingerId,
                            Mode = a.Role == ContactIkTargetRole.Intermediate ? ContactIkTargetMode.FixedPointBand : ContactIkTargetMode.Contact,
                            MatchNormal = false, Weight = 1, PositionToleranceMeters = .002f },
                        CorrespondenceReviewed = false, Provenance = a.Provenance + "; object targets unreviewed"
                    }).ToArray();
                    var policy = ContactIkPreviewPolicy.ForDistalPalmTargets(8, 17015, 2000);
                    using (var session = new ContactIkPreviewSession(setup.Rig, _request.Side, setup.Renderers, false,
                        setup.WorldToObject, setup.Object, null, View.BindingFingerprint, Dofs(setup.Rig, _request.Side), bindings,
                        policy, new ContactIkObjectEvidence { Evidence = "unreviewed editor assembly; fullskin is diagnostics only" },
                        objectFingerprint: fingerprint, palmPlacementRoot: setup.Rig.Avatar.transform,
                        maxPalmTranslationMeters: _maximumPalmTranslation))
                    {
                        var result = session.Solve(new ContactIkSettings { Criterion = ContactIkCriterion.DistalPalmTargets,
                            DistalPriority = priority, MaxEvaluations = _maximumEvaluations });
                        session.ApplyFinal(result.Degrees);
                        var after = ContactIkPreviewSession.CaptureHandTriangles(setup.Rig, _request.Side, setup.Renderers, setup.WorldToObject);
                        var diagnostics = result.TargetErrors.Select(e => new HandContactIkPreviewDiagnostic(e.PatchId, e.Role,
                            e.Role == ContactIkTargetRole.Intermediate ? e.UnsignedDistanceMeters * 1000 : e.PositionErrorMeters * 1000,
                            e.EffectiveWeight, e.Ignored, e.Satisfied ? "Численная цель достигнута; качество не принято" : "Осталась ошибка цели")).ToArray();
                        return new HandContactIkPreviewResult(after, diagnostics, result.PalmTranslationObjectMeters,
                            Newtonsoft.Json.JsonConvert.SerializeObject(new { result.Status, result.Evaluations, result.AtLimits,
                                result.RequiresReview, result.Cost, observation = session.LastObservation }, FitUnityJsonConverter.Settings),
                            result.NumericalTargetsReached);
                    }
                }
            }
        }

        sealed class Setup : IDisposable
        {
            public readonly GripTargetRig Rig;
            public readonly Matrix4x4 WorldToObject;
            public readonly SkinnedMeshRenderer[] Renderers;
            public readonly FitTriangle[] Object;
            GameObject _objectRoot;

            public Setup(HandPoseFitRequest request)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                    throw new InvalidOperationException("Редактор занят Play/compile/import.");
                try
                {
                    Rig = new GripTargetRig(request.AvatarPath);
                    foreach (var behaviour in Rig.Avatar.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                    _objectRoot = PrefabUtility.LoadPrefabContents(request.ObjectPath);
                    foreach (var behaviour in _objectRoot.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                    _objectRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var grabbable = _objectRoot.GetComponent<UxrGrabbableObject>();
                    if (!grabbable || request.GrabPoint < 0 || request.GrabPoint >= grabbable.GrabPointCount)
                        throw new ArgumentException("Нужен конкретный GrabPoint корневого grabbable.");
                    var point = grabbable.GetGrabPoint(request.GrabPoint);
                    if (point.SnapMode != UxrSnapToHandMode.PositionAndRotation || (!point.BothHandsCompatible && point.HandSide != request.Side))
                        throw new NotSupportedException("Нужен совместимый PositionAndRotation GrabPoint.");
                    var align = grabbable.Editor_GetGrabPointGrabAlignTransform(Rig.Avatar, request.GrabPoint, request.Side);
                    if (!align || !align.IsChildOf(_objectRoot.transform)) throw new ArgumentException("Нет align выбранной стороны.");
                    var grabber = Rig.Grabber(request.Side);
                    var root = Rig.Avatar.transform;
                    root.rotation = align.rotation * Quaternion.Inverse(grabber.rotation) * root.rotation;
                    root.position += align.position - grabber.position;
                    Rig.Avatar.SetCurrentHandPoseImmediately(request.Side, request.PoseOverride);
                    var lower = HandPoseFitSnapshot.LowerLodRenderers(root.gameObject);
                    var main = root.GetComponentsInChildren<UxrGrabber>(true).Single(g => g.Side == request.Side).HandRenderer;
                    var allowed = new HashSet<Transform>(Rig.Hand(request.Side).Transforms);
                    Renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh && !lower.Contains(s) &&
                        (s == main || (s.enabled && s.gameObject.activeInHierarchy)) && s.bones.Any(allowed.Contains)).ToArray();
                    WorldToObject = Matrix4x4.TRS(_objectRoot.transform.position, _objectRoot.transform.rotation, Vector3.one).inverse;
                    using (var snapshot = new HandPoseFitSnapshot(Rig.Avatar, request.PoseOverride, 0, _objectRoot,
                        request.Copy(), new HandPoseFitReport()))
                        Object = snapshot.ObjectSurface.Where(t => t.Area > 1e-12f).ToArray();
                    if (Object.Length == 0) throw new ArgumentException("Нет невырожденной поверхности объекта.");
                }
                catch { Dispose(); throw; }
            }

            public void Dispose()
            {
                if (_objectRoot) { PrefabUtility.UnloadPrefabContents(_objectRoot); _objectRoot = null; }
                Rig?.Dispose();
            }
        }

        static HandPoseFitRequest Resolve(HandPoseFitRequest supplied)
        {
            var r = supplied.Copy();
            if (r.GrabbableIndexPath != null && r.GrabbableIndexPath.Length != 0)
                throw new NotSupportedException("Первый preview v2 поддерживает только корневой grabbable; вложенный выберите отдельным объектом.");
            if (!r.AvatarPrefab && !string.IsNullOrEmpty(r.AvatarPath)) r.AvatarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(r.AvatarPath)?.GetComponent<UxrAvatar>();
            if (!r.ObjectPrefab && !string.IsNullOrEmpty(r.ObjectPath)) r.ObjectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(r.ObjectPath);
            if (!r.PoseOverride && !string.IsNullOrEmpty(r.PoseOverridePath)) r.PoseOverride = AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(r.PoseOverridePath);
            if (!r.AvatarPrefab || !r.ObjectPrefab || !r.PoseOverride || r.PoseOverride.PoseType != UxrHandPoseType.Fixed)
                throw new ArgumentException("Для первого preview v2 явно выберите avatar, объект и Fixed pose.");
            r.AvatarPath = AssetDatabase.GetAssetPath(r.AvatarPrefab); r.ObjectPath = AssetDatabase.GetAssetPath(r.ObjectPrefab);
            r.PoseOverridePath = AssetDatabase.GetAssetPath(r.PoseOverride);
            if (string.IsNullOrEmpty(r.AvatarPath) || string.IsNullOrEmpty(r.ObjectPath) || string.IsNullOrEmpty(r.PoseOverridePath))
                throw new ArgumentException("Входы должны быть сохранёнными assets.");
            return r;
        }

        static string Dependencies(HandPoseFitRequest r) => string.Join(";", new[] { r.AvatarPath, r.ObjectPath, r.PoseOverridePath }
            .Select(p => p + "=" + AssetDatabase.GetAssetDependencyHash(p)));

        internal static ContactIkDof[] Dofs(GripTargetRig rig, UxrHandSide side)
        {
            var hand = rig.Hand(side); var fingers = new[] { hand.Thumb, hand.Index, hand.Middle, hand.Ring, hand.Little };
            string[] names = { "thumb", "index", "middle", "ring", "little" }; var result = new List<ContactIkDof>();
            for (int f = 0; f < fingers.Length; f++)
            {
                var bones = new[] { fingers[f].Proximal, fingers[f].Intermediate, fingers[f].Distal };
                for (int i = 0; i < bones.Length; i++)
                    result.Add(new ContactIkDof { Id = names[f] + "/" + i, Bone = bones[i],
                        LocalAxis = rig.Avatar.AvatarRigInfo.GetArmInfo(side).FingerUniversalLocalAxes.LocalRight,
                        MinDegrees = -35, MaxDegrees = 65 });
            }
            return result.ToArray();
        }

        internal static ContactIkSurfaceAnchor PalmAnchor(HandContactProbeCapture reference)
        {
            var candidates = reference.Hand.Where(t => t.Zone == "palm_wrist" || (t.FingerSegment ?? "").EndsWith("/metacarpal", StringComparison.Ordinal));
            FitTriangle selected = default; Vector3 bary = default; float best = float.PositiveInfinity;
            foreach (var triangle in candidates)
                if (Vector3.Dot(triangle.Normal, reference.ReferencePalmOut) > 0 &&
                    Ray(reference.ReferencePalmCenter, reference.ReferencePalmOut, triangle, out float distance, out Vector3 b) && distance < best && distance <= .05f)
                { selected = triangle; bary = b; best = distance; }
            if (float.IsPositiveInfinity(best)) throw new InvalidOperationException("Не найден ладонный material anchor; требуется диагностика.");
            return Anchor(selected, bary);
        }

        internal static ContactIkSurfaceAnchor Project(FitTriangle[] surface, Vector3 point)
        {
            var best = surface.Select(t => new { Triangle = t, Point = HandPoseFitGeometry.ClosestPoint(point, t) })
                .OrderBy(x => (x.Point - point).sqrMagnitude).First();
            Vector3 u = best.Triangle.B - best.Triangle.A, v = best.Triangle.C - best.Triangle.A, w = best.Point - best.Triangle.A;
            double nx = (double)u.y * v.z - (double)u.z * v.y, ny = (double)u.z * v.x - (double)u.x * v.z, nz = (double)u.x * v.y - (double)u.y * v.x;
            double d = nx * nx + ny * ny + nz * nz;
            if (!(d > 0) || double.IsInfinity(d)) throw new InvalidOperationException("Вырожденная грань цели.");
            double y = (((double)w.y * v.z - (double)w.z * v.y) * nx + ((double)w.z * v.x - (double)w.x * v.z) * ny + ((double)w.x * v.y - (double)w.y * v.x) * nz) / d;
            double z = (((double)u.y * w.z - (double)u.z * w.y) * nx + ((double)u.z * w.x - (double)u.x * w.z) * ny + ((double)u.x * w.y - (double)u.y * w.x) * nz) / d;
            if (double.IsNaN(y) || double.IsNaN(z) || y < -.00001 || z < -.00001 || y + z > 1.00001)
                throw new InvalidOperationException("Проекция не даёт допустимой материальной точки цели.");
            var bary = new Vector3(Mathf.Max(0, (float)(1 - y - z)), Mathf.Max(0, (float)y), Mathf.Max(0, (float)z)); bary /= bary.x + bary.y + bary.z;
            return Anchor(best.Triangle, bary);
        }

        static ContactIkSurfaceAnchor Anchor(FitTriangle t, Vector3 bary) => new ContactIkSurfaceAnchor
        { Source = t.Source, SourceSubMesh = t.SourceSubMesh, SourceTriangle = t.SourceTriangle, Barycentric = bary };

        static bool Ray(Vector3 origin, Vector3 direction, FitTriangle t, out float distance, out Vector3 bary)
        {
            distance = 0; bary = default; Vector3 e1 = t.B - t.A, e2 = t.C - t.A, p = Vector3.Cross(direction, e2);
            float det = Vector3.Dot(e1, p); if (Mathf.Abs(det) < 1e-12f) return false;
            Vector3 s = origin - t.A; float u = Vector3.Dot(s, p) / det;
            Vector3 q = Vector3.Cross(s, e1); float v = Vector3.Dot(direction, q) / det;
            distance = Vector3.Dot(e2, q) / det;
            if (!Finite(distance) || distance < 0 || u < 0 || v < 0 || u + v > 1) return false;
            bary = new Vector3(1 - u - v, u, v); return true;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
