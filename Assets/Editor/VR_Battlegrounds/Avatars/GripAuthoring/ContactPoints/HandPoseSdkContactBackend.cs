using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Editor.Manipulation.HandPoses;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Editor.Avatars;
using VrBattlegrounds.Editor.Avatars.ContactIk;
using VrBattlegrounds.Editor.HandPoseReview;

namespace VrBattlegrounds.Editor.GripAuthoring.ContactPoints
{
    /// <summary>Общий frozen SDK кадр → частный cooperative rig. Сценовые пальцы/предмет и pose assets не записываются.</summary>
    public static class HandPoseSdkContactBackend
    {
        sealed class Reference
        {
            public HandContactProbeCapture Capture;
            public HandPhalanxProbeSet Probes;
            public HandContactIkPreviewAnchor[] Anchors;
            public string[] SkinSources;
            public string FullSkinSignature, QuickSkinSignature;
        }
        static readonly Dictionary<string, Reference> References = new Dictionary<string, Reference>(StringComparer.Ordinal);
        static readonly ConditionalWeakTable<HandPoseSdkContactContext, Reference> ReferenceByContext = new ConditionalWeakTable<HandPoseSdkContactContext, Reference>();

        public static HandPoseSdkContactContext PrepareContext(UxrHandPoseEditorWindow window, UxrAvatar avatar,
            UxrHandPoseAsset pose, float blend, GameObject root, UxrGrabbableObject grabbable, int point,
            UxrHandSide side, HandPoseSdkPreviewSession session)
        {
            if (!window || !avatar || !pose || !root || !grabbable || session?.Snapshot == null || session.Metadata == null)
                throw new ArgumentException("Нужен текущий общий SDK snapshot и выбранный scene grabbable.");
            if (!window.TryCaptureContactEditContext(side, out var sdk, out string reason)) throw new InvalidOperationException(reason);
            if (sdk.Avatar != avatar || sdk.Pose != pose || sdk.Blend != blend) throw new InvalidOperationException("SDK и frozen context не совпадают.");
            var reference = GetReference(avatar, side);
            var before = session.Snapshot.Hand.ToArray();
            ValidateSceneBinding(avatar, before, reference, true);
            var surface = session.Snapshot.ObjectSurface.Where(t => Finite(t.A) && Finite(t.B) && Finite(t.C) && t.Area > 1e-12f).ToArray();
            if (before.Length == 0 || surface.Length == 0) throw new ArgumentException("Нет hand/object geometry.");
            foreach (var anchor in reference.Anchors) anchor.SkinAnchor.Evaluate(before);
            string fingerprint = ContactIkPatchBinding.ComputeObjectFingerprint(surface);
            var primary = reference.Anchors.Where(a => a.Role == ContactIkTargetRole.Palm || a.Role == ContactIkTargetRole.Distal).ToArray();
            var view = new HandContactIkPreviewView(root.name + "/" + side + "/" + point,
                "SDK working fingers (включая несохранённые); нейтральные fixed material anchors; object targets назначаются явно; review=false",
                reference.Capture.BindingFingerprint, fingerprint, before, surface, primary);
            string key = session.InputKey;
            var settings = session.Metadata.Settings.Copy();
            Func<bool> current = () => window && avatar && pose && root && grabbable && session.Snapshot != null && session.InputKey == key &&
                !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
                HandPoseSdkPreviewSession.BuildInputKey(avatar, pose, blend, root, settings) == key && SameSceneBinding(avatar, before, reference);
            var context = new HandPoseSdkContactContext(window, sdk, side, root, grabbable, point, key, view,
                request => BeginJob(request, reference), current);
            ReferenceByContext.Add(context, reference);
            return context;
        }

        public static HandPhalanxProbeReport GetPointReport(HandPoseSdkContactContext context, FitTriangle[] hand)
        {
            if (context == null || !ReferenceByContext.TryGetValue(context, out var reference)) throw new ArgumentException("Неизвестный SDK context.");
            ValidateSceneBinding(context.SdkContext.Avatar, hand, reference, true);
            return HandPhalanxContactProbes.Evaluate(hand, new FitSurface(context.View.ObjectSurface),
                context.View.BindingFingerprint, CloneProbes(reference.Probes), 2);
        }

        public static void ValidateMaterialBinding(HandPoseSdkContactContext context)
        {
            if (context == null || !ReferenceByContext.TryGetValue(context, out var reference)) throw new ArgumentException("Неизвестный SDK context.");
            ValidateSceneBinding(context.SdkContext.Avatar, context.View.BeforeHand, reference, true);
        }

        static Reference GetReference(UxrAvatar avatar, UxrHandSide side)
        {
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(avatar.gameObject);
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Для нейтральной material привязки нужен prefab сценового аватара.");
            string dependency = AssetDatabase.GetAssetDependencyHash(path).ToString();
            string key = path + "|" + dependency + "|" + side + "|" + QualitySettings.skinWeights;
            if (References.TryGetValue(key, out var cached)) return cached;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponent<UxrAvatar>();
            if (!prefab || !prefab.DefaultHandPose || prefab.DefaultHandPose.PoseType != UxrHandPoseType.Fixed)
                throw new ArgumentException("Эталону нужен явный SDK Default Fixed pose.");
            var reference = HandContactProbeCapture.Capture(prefab, side, prefab.DefaultHandPose);
            HandPhalanxProbeSet probes;
            if (side == UxrHandSide.Left)
            {
                var right = GetReference(avatar, UxrHandSide.Right);
                probes = HandContactProbeCapture.Mirror(right.Capture, reference, right.Probes);
            }
            else probes = reference.CreateProbes();
            if (!probes.Valid) throw new InvalidOperationException("Не все нейтральные опорные точки доступны: " + string.Join("; ", probes.Errors));
            var anchors = reference.CreateIkBindings(probes).Select(b => new HandContactIkPreviewAnchor(b.Target.PatchId,
                b.Target.PatchId.EndsWith("/distal", StringComparison.Ordinal) ? ContactIkTargetRole.Distal : ContactIkTargetRole.Intermediate,
                b.Target.PatchId.Split('/')[0], b.Anchor, b.Provenance)).ToList();
            anchors.Add(new HandContactIkPreviewAnchor("palm", ContactIkTargetRole.Palm, null,
                HandContactIkPreviewCapture.PalmAnchor(reference), "neutral SDK palm material ray; review=false"));
            var sources = reference.Hand.Select(t => t.Source).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            cached = new Reference { Capture = reference, Probes = CloneProbes(probes), Anchors = anchors.ToArray(), SkinSources = sources,
                FullSkinSignature = SkinSignature(prefab, sources, true), QuickSkinSignature = SkinSignature(prefab, sources, false) };
            References[key] = cached; return cached;
        }

        static HandPoseSdkContactJob BeginJob(HandPoseSdkContactRequest request, Reference reference)
        {
            if (request == null || !request.Context.IsCurrent()) throw new InvalidOperationException("Входное поколение устарело.");
            var context = request.Context;
            ValidateMaterialBinding(context);
            var rig = new GripTargetRig(request.SdkContext.Avatar);
            ContactIkPreviewSession session = null;
            try
            {
                rig.Apply(context.Side, request.SdkContext.WorkingDescriptor);
                var align = context.Grabbable.Editor_GetGrabPointGrabAlignTransform(rig.Avatar, context.GrabPoint, context.Side);
                if (!align) throw new ArgumentException("Нет snap transform выбранной стороны.");
                var grabber = rig.Grabber(context.Side); var placement = rig.RootTransform;
                placement.rotation = align.rotation * Quaternion.Inverse(grabber.rotation) * placement.rotation;
                placement.position += align.position - grabber.position;
                Matrix4x4 worldToObject = context.ObjectToWorld.inverse;
                var surface = context.View.ObjectSurface;
                var lower = HandPoseFitSnapshot.LowerLodRenderers(rig.Avatar.gameObject);
                var selectedPaths = new HashSet<string>(context.View.BeforeHand.Select(t => t.Source), StringComparer.Ordinal);
                var renderers = rig.Avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh && !lower.Contains(r) &&
                    selectedPaths.Contains(HandPoseFitSnapshot.HumanPath(r.transform, rig.Avatar.transform))).ToArray();
                var dofs = HandContactIkPreviewCapture.Dofs(rig, context.Side);
                var targets = request.Targets.ToDictionary(t => t.Id, StringComparer.Ordinal);
                bool forward = request.Mode == UxrHandPoseContactEditMode.ForwardWrap;
                var surfaceKeys = surface.Select(t => new HandPosePotentialContactTriangleKey(t.Source, t.SourceSubMesh, t.SourceTriangle)).ToArray();
                var bindings = reference.Anchors.Select(a =>
                {
                    bool primary = a.Role == ContactIkTargetRole.Palm || (!forward && a.Role == ContactIkTargetRole.Distal);
                    if (primary && !targets.ContainsKey(a.Id)) throw new ArgumentException("Отсутствует явная цель " + a.Id);
                    return new ContactIkPatchBinding
                    {
                        Anchor = a.SkinAnchor, ObjectAnchor = primary ? targets[a.Id].ObjectAnchor : null,
                        ObjectFingerprint = context.View.ObjectFingerprint, PermittedSurface = primary ? Array.Empty<HandPosePotentialContactTriangleKey>() : surfaceKeys,
                        CorrespondenceReviewed = false, Provenance = a.Provenance + "; SDK explicit target draft; review=false",
                        Target = new ContactIkTarget { PatchId = a.Id, FingerId = a.FingerId,
                            Role = forward && a.Role != ContactIkTargetRole.Palm ? ContactIkTargetRole.Unspecified : a.Role,
                            Mode = primary ? ContactIkTargetMode.Contact : ContactIkTargetMode.FixedPointBand,
                            PositionToleranceMeters = .002f, Weight = 1 }
                    };
                }).ToArray();
                var policy = forward ? ContactIkPreviewPolicy.ForForwardSurfaceWrap(8, 17015, 2000) : ContactIkPreviewPolicy.ForDistalPalmTargets(8, 17015, 2000);
                session = new ContactIkPreviewSession(rig, context.Side, renderers, false, worldToObject, surface, null,
                    context.View.BindingFingerprint, dofs, bindings, policy,
                    new ContactIkObjectEvidence { Evidence = "SDK shared frozen object; review=false; fullskin diagnostics only" },
                    objectFingerprint: context.View.ObjectFingerprint, palmPlacementRoot: rig.RootTransform, maxPalmTranslationMeters: .03f);
                var owned = session;
                Action cleanup = () => { try { owned.Dispose(); } finally { rig.Dispose(); } };
                if (!forward)
                {
                    var job = session.BeginSolve(new ContactIkSettings { Criterion = ContactIkCriterion.DistalPalmTargets,
                        DistalPriority = request.DistalPriority, MaxEvaluations = 96 }, request.Generation);
                    return new HandPoseSdkContactJob(job, () =>
                    {
                        var result = job.Result; owned.ApplyFinal(result.Degrees);
                        var diagnostics = result.TargetErrors.Select(e => new HandContactIkPreviewDiagnostic(e.PatchId, e.Role,
                            e.Role == ContactIkTargetRole.Intermediate ? e.UnsignedDistanceMeters * 1000 : e.PositionErrorMeters * 1000,
                            e.EffectiveWeight, e.Ignored, e.Satisfied ? "Численная цель достигнута; review=false" : "Остаточная ошибка")).ToArray();
                        return Result(rig, context, renderers, worldToObject, owned, diagnostics, result.PalmTranslationObjectMeters,
                            result.NumericalTargetsReached, new { result.Status, result.Evaluations, result.AtLimits, observation = owned.LastObservation });
                    }, cleanup);
                }
                var fingers = new[] { "thumb", "index", "middle", "ring", "little" }.Select(finger => new ContactIkForwardWrapFinger
                {
                    FingerId = finger,
                    Stages = new[] { "proximal", "intermediate", "distal" }.Select((segment, i) => new ContactIkForwardWrapStage
                    { PatchId = finger + "/" + segment, DofId = finger + "/" + i, Segment = (ContactIkWrapSegment)i, ClosingSign = 1 }).ToArray()
                }).ToArray();
                var settings = new ContactIkForwardWrapSettings
                {
                    Mode = request.WrapProfile == HandPoseSdkWrapProfile.MoreContacts ? ContactIkForwardWrapMode.PreferThreeContacts : ContactIkForwardWrapMode.AvoidPointPenetration,
                    InitialDegrees = new float[dofs.Length], StepDegrees = 6, AngularToleranceDegrees = .25f,
                    MaxEvaluations = 128, MaxRepairPasses = 1,
                    MaximumPointPenetrationMeters = request.WrapProfile == HandPoseSdkWrapProfile.MoreContacts ? request.MaximumPenetrationMm * .001f : (float?)0
                };
                var wrapJob = session.BeginForwardWrap(fingers, settings, request.Generation);
                return new HandPoseSdkContactJob(wrapJob, () =>
                {
                    var result = wrapJob.Result; owned.ApplyFinal(result.Degrees);
                    var diagnostics = result.FinalObservation.Evaluation.Patches.Select(p => new HandContactIkPreviewDiagnostic(p.PatchId,
                        p.PatchId == "palm" ? ContactIkTargetRole.Palm : ContactIkTargetRole.Unspecified,
                        p.UnsignedDistanceReliable ? p.UnsignedDistanceMeters * 1000 : null, 1, false,
                        p.ClearanceReliable ? "Известный знак; см. point constraints" : "Знак unknown; отсутствие проникновений не подтверждено")).ToArray();
                    return Result(rig, context, renderers, worldToObject, owned, diagnostics, owned.LastObservation.PalmTranslationObjectMeters,
                        result.NumericalTargetsReached, new { result.StopReason, result.Evaluations, result.ExpectedContactCount,
                            result.NumericalContactCount, result.ConfirmedContactCount, result.PointConstraintsSatisfied, result.HasUnknownPointSign,
                            result.PalmTargetReached, result.PalmResidualMeters, result.Fingers, observation = owned.LastObservation });
                }, cleanup);
            }
            catch { try { session?.Dispose(); } finally { rig.Dispose(); } throw; }
        }

        static HandPoseSdkContactResult Result(GripTargetRig rig, HandPoseSdkContactContext context, SkinnedMeshRenderer[] renderers,
            Matrix4x4 frame, ContactIkPreviewSession session, HandContactIkPreviewDiagnostic[] diagnostics, Vector3 palm, bool reached, object detail)
        {
            ValidateMaterialBinding(context);
            var hand = ContactIkPreviewSession.CaptureHandTriangles(rig, context.Side, renderers, frame);
            return new HandPoseSdkContactResult(session.CaptureDescriptor(), new HandContactIkPreviewResult(hand, diagnostics, palm,
                JsonConvert.SerializeObject(detail, FitUnityJsonConverter.Settings), reached));
        }

        static bool SameSceneBinding(UxrAvatar avatar, FitTriangle[] hand, Reference reference)
        { try { ValidateSceneBinding(avatar, hand, reference, false); return true; } catch (ArgumentException) { return false; } catch (InvalidOperationException) { return false; } }

        static void ValidateSceneBinding(UxrAvatar avatar, FitTriangle[] hand, Reference reference, bool full)
        {
            var sources = hand.Select(t => t.Source).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (!sources.SequenceEqual(reference.SkinSources, StringComparer.Ordinal) ||
                SkinSignature(avatar, sources, full) != (full ? reference.FullSkinSignature : reference.QuickSkinSignature))
                throw new ArgumentException("Scene skin binding отличается от нейтрального prefab: mesh/topology/weights/bone order устарели; исходные индексы не переиспользуются.");
        }

        /// <summary>Полная подпись на prepare/start/result/commit; лёгкая revision/path подпись на update, без BakeMesh.</summary>
        static string SkinSignature(UxrAvatar avatar, string[] sources, bool full)
        {
            if (!avatar) throw new ArgumentException("Нет аватара для проверки skin binding.");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write((int)QualitySettings.skinWeights);
                var renderers = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (string path in sources)
                {
                    var matches = renderers.Where(r => RelativePath(r.transform, avatar.transform) == path).ToArray();
                    if (matches.Length != 1 || !matches[0].sharedMesh) throw new ArgumentException("Неоднозначный skin renderer: " + path);
                    var skin = matches[0]; var mesh = skin.sharedMesh;
                    if (!EditorUtility.IsPersistent(mesh) || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long id))
                        throw new ArgumentException("Skin использует неподтверждённый scene mesh: " + path);
                    writer.Write(path); writer.Write(guid); writer.Write(id);
                    writer.Write(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(mesh)).ToString());
                    writer.Write(EditorUtility.GetDirtyCount(mesh)); writer.Write((int)skin.quality);
                    writer.Write(RelativePath(skin.rootBone, avatar.transform));
                    var bones = skin.bones; writer.Write(bones.Length);
                    foreach (var bone in bones) writer.Write(RelativePath(bone, avatar.transform));
                    writer.Write(mesh.blendShapeCount);
                    for (int i = 0; i < mesh.blendShapeCount; i++) writer.Write(skin.GetBlendShapeWeight(i));
                    if (!full) continue;
                    var vertices = mesh.vertices; writer.Write(vertices.Length);
                    foreach (var vertex in vertices) { writer.Write(vertex.x); writer.Write(vertex.y); writer.Write(vertex.z); }
                    var normals = mesh.normals; writer.Write(normals.Length);
                    foreach (var normal in normals) { writer.Write(normal.x); writer.Write(normal.y); writer.Write(normal.z); }
                    writer.Write(mesh.subMeshCount);
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    { writer.Write((int)mesh.GetTopology(sub)); var indices = mesh.GetIndices(sub); writer.Write(indices.Length); foreach (int index in indices) writer.Write(index); }
                    using (var counts = mesh.GetBonesPerVertex())
                    using (var weights = mesh.GetAllBoneWeights())
                    {
                        writer.Write(counts.Length); foreach (byte count in counts) writer.Write(count);
                        writer.Write(weights.Length); foreach (var weight in weights) { writer.Write(weight.boneIndex); writer.Write(weight.weight); }
                    }
                    var bindposes = mesh.bindposes; writer.Write(bindposes.Length);
                    foreach (var bindpose in bindposes) for (int i = 0; i < 16; i++) writer.Write(bindpose[i]);
                }
                writer.Flush(); stream.Position = 0;
                using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }

        static string RelativePath(Transform transform, Transform root)
        {
            if (!transform) return "<null>";
            var parts = new List<string>();
            while (transform != root)
            {
                if (!transform) throw new ArgumentException("Skin bone/renderer находится вне аватара.");
                parts.Add(transform.name); transform = transform.parent;
            }
            parts.Reverse(); return string.Join("/", parts);
        }
        static HandPhalanxProbeSet CloneProbes(HandPhalanxProbeSet probes) => JsonConvert.DeserializeObject<HandPhalanxProbeSet>(JsonConvert.SerializeObject(probes));
        static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);
    }
}
