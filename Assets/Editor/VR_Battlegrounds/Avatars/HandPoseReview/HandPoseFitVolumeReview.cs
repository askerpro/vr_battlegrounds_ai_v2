using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Ручная запись привязана к фактически измеренным треугольникам и контексту.</summary>
    public sealed class FitVolumeReview
    {
        public string ReviewId, Scope, Units="meters", Frame="object_root", GeometrySha256, ContextSha256;
        public string EvidenceMode="manual_user_review";
        public bool Accepted;
        public FitVolumeReview Copy() => (FitVolumeReview)MemberwiseClone();
    }

    public sealed class FitVolumeReviewAudit
    {
        public string Status="unknown", Reason, Units="meters", Frame="object_root", GeometrySha256, ContextSha256;
        public FitVolumeReview Draft;
    }

    public static class HandPoseFitVolumeReview
    {
        public static FitVolumeReview CreateDraft(HandPoseFitSnapshot snapshot,HandPoseFitReport report)
        {
            if(snapshot==null||report?.Settings==null) throw new ArgumentException("missing_snapshot_or_settings");
            var r=report.Settings;
            if(snapshot.AnalysisVolume.Count==0) throw new ArgumentException("missing_analysis_volume");
            foreach(var t in snapshot.AnalysisVolume) {RequireFinite(t.A);RequireFinite(t.B);RequireFinite(t.C);}
            RequireFinite(r.AnalysisVolumePositionMeters);RequireFinite(r.AnalysisVolumeEulerDegrees);
            RequirePositive(r.AnalysisVolumeScale);
            if(!report.ObjectScaleAxes.HasValue) throw new ArgumentException("unknown_object_scale_axes");
            RequirePositive(report.ObjectScaleAxes.Value);RequireFinite(report.ObjectScale);RequireFinite(report.AppliedBlend);
            RequireFinite(r.HandOffsetMm);RequireFinite(r.Blend);
            if(string.IsNullOrWhiteSpace(report.CaptureSource)) throw new ArgumentException("unknown_capture_source");
            RequireIdentity(report.Object);RequireIdentity(report.AnalysisVolume);
            string geometry=Hash(writer=> {
                writer.Write("actual_volume_triangles_v1_meters_object_root");writer.Write(snapshot.AnalysisVolume.Count);
                foreach(var t in snapshot.AnalysisVolume) {Vector(writer,t.A);Vector(writer,t.B);Vector(writer,t.C);}
            });
            string context=Hash(writer=> {
                writer.Write("volume_context_v1");Identity(writer,report.Object);Identity(writer,report.AnalysisVolume);
                Vector(writer,report.ObjectScaleAxes.Value);writer.Write(report.ObjectScale);
                Text(writer,r.GrabbableIndexPath);writer.Write(r.GrabPoint);writer.Write((int)r.Side);
                Text(writer,report.GrabbablePath);Text(writer,r.StateName);Text(writer,report.StateName);
                Text(writer,report.CaptureSource);Text(writer,r.AlignmentMode);writer.Write(report.AppliedBlend);
                writer.Write(r.OverrideBlend);writer.Write(r.Blend);Vector(writer,r.HandOffsetMm);
                writer.Write(report.AlignToControllerEnabled);writer.Write(report.ControllerAlignmentApplied);Text(writer,report.ControllerModel);
                Text(writer,r.AnalysisVolumeMeshPath);Text(writer,r.AnalysisVolumeMeshGuid);writer.Write(r.AnalysisVolumeMeshLocalFileId);
                Vector(writer,r.AnalysisVolumePositionMeters);Vector(writer,r.AnalysisVolumeEulerDegrees);Vector(writer,r.AnalysisVolumeScale);
            });
            return new FitVolumeReview {GeometrySha256=geometry,ContextSha256=context};
        }

        public static FitVolumeReviewAudit Evaluate(HandPoseFitSnapshot snapshot,HandPoseFitReport report)
        {
            var audit=new FitVolumeReviewAudit();
            try {
                var draft=CreateDraft(snapshot,report);audit.Draft=draft;
                audit.GeometrySha256=draft.GeometrySha256;audit.ContextSha256=draft.ContextSha256;
                // Топология всегда из тех же фактических треугольников, а не доверенное поле JSON.
                report.AnalysisVolumeTopology=HandPoseFitGeometry.Topology(snapshot.AnalysisVolume.ToArray());
                var r=report.Settings;var review=r.AnalysisVolumeReview;
                string reason=!r.AnalysisVolumeReviewed?"volume_not_reviewed":
                    review==null?"missing_manual_review":
                    !review.Accepted?"review_not_accepted":
                    string.IsNullOrWhiteSpace(review.ReviewId)||string.IsNullOrWhiteSpace(review.Scope)?"missing_review_id_or_scope":
                    review.Units!="meters"||review.Frame!="object_root"?"incompatible_units_or_frame":
                    review.EvidenceMode!="manual_user_review"&&!(review.EvidenceMode=="synthetic_fixture_explicit_assumption"&&report.CaptureSource=="mathematical_fixture")?"invalid_review_evidence_mode":
                    review.GeometrySha256!=draft.GeometrySha256?"stale_geometry_binding":
                    review.ContextSha256!=draft.ContextSha256?"stale_context_binding":
                    !report.AnalysisVolumeTopology.CanDetermineInside?"volume_topology_unknown":null;
                audit.Status=reason==null?"applied":"unknown";audit.Reason=reason??"matching_explicit_review_and_closed_actual_volume";
            } catch(Exception e) {audit.Status="unknown";audit.Reason="invalid_binding_input: "+e.Message;}
            return audit;
        }

        static string Hash(Action<BinaryWriter> write)
        {
            using(var stream=new MemoryStream()) {
                using(var writer=new BinaryWriter(stream,Encoding.UTF8,true)) write(writer);
                using(var sha=SHA256.Create()) return string.Concat(sha.ComputeHash(stream.ToArray()).Select(b=>b.ToString("x2")));
            }
        }
        static void Text(BinaryWriter writer,string value) {writer.Write(value!=null);if(value!=null) writer.Write(value);}
        static void Vector(BinaryWriter writer,Vector3 value) {writer.Write(value.x);writer.Write(value.y);writer.Write(value.z);}
        static void Identity(BinaryWriter writer,FitAssetIdentity value) {Text(writer,value.Path);Text(writer,value.Guid);writer.Write(value.LocalFileId);Text(writer,value.DependencyHash);}
        static void RequireIdentity(FitAssetIdentity value) {if(value==null||string.IsNullOrWhiteSpace(value.Guid)||value.LocalFileId==0||string.IsNullOrWhiteSpace(value.DependencyHash)) throw new ArgumentException("unknown_asset_identity_or_dependencies");}
        static void RequirePositive(Vector3 value) {RequireFinite(value);if(value.x<=0||value.y<=0||value.z<=0) throw new ArgumentException("nonpositive_scale");}
        static void RequireFinite(Vector3 value) {RequireFinite(value.x);RequireFinite(value.y);RequireFinite(value.z);}
        static void RequireFinite(float value) {if(float.IsNaN(value)||float.IsInfinity(value)) throw new ArgumentException("nonfinite_geometry_or_context");}
    }
}
