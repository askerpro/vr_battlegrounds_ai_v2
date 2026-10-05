using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation.HandPoses;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    [Serializable]
    public sealed class HandPoseFitRequest
    {
        [JsonIgnore] public UxrAvatar AvatarPrefab;
        [JsonIgnore] public GameObject ObjectPrefab;
        [JsonIgnore] public UxrHandPoseAsset PoseOverride;
        public string AvatarPath, ObjectPath, PoseOverridePath;
        // GameObject внутри prefab-файла: позволяет повторить анализ встроенного предмета без всей комнаты.
        public long ObjectPrefabLocalFileId;
        // Индексы дочерних Transform от корня. Пустая строка означает корень.
        public string GrabbableIndexPath = "";
        public int GrabPoint;
        public UxrHandSide Side = UxrHandSide.Right;
        public bool OverrideBlend, AllowDefaultGrip;
        public float Blend;
        public string Profile = "exploratory";
        public string AlignmentMode = "grip_reference";
        public bool ContactBoundsEnabled, PalmarOnly, MaskReviewed;
        public Bounds ContactBounds = new Bounds(Vector3.zero, Vector3.one * .15f);
        public string[] ContactRendererPaths = Array.Empty<string>();
        public float PalmNormalSign = -1, PalmarMinDot = .1f;
        // Заданный для исследования допуск близости, не норма качества.
        public float ProximityMm = 2;
        public float ContactDistanceMinMm;
        public bool ShowContactMarkers = true;
        public int MaxContactMarkers = 300;
        public float ContactMarkerRadiusPixels = 3;
        public int SampleCount = 6000, Seed = 42, ImageSize = 1024;
        public bool RenderImages = true;
        public bool OverrideFrameCenter;
        public Vector3 FrameCenterMeters;
        public float FrameSizeMeters = .35f;
        public Vector3 HandOffsetMm;
        public string OutputDirectory;
        public string StateName="hold", SeriesName="", ReferenceReportPath="";
        public bool ReferenceAccepted, CheckSamplingConvergence;
        public FitContactRegion[] ContactRegions=Array.Empty<FitContactRegion>();
        public string AnalysisVolumeMeshPath;
        public string AnalysisVolumeMeshGuid;
        public long AnalysisVolumeMeshLocalFileId;
        public Vector3 AnalysisVolumePositionMeters,AnalysisVolumeEulerDegrees,AnalysisVolumeScale=Vector3.one;
        public bool AnalysisVolumeReviewed, IncludeOtherHand=true, CheckHandSelfIntersections=true;
        public FitVolumeReview AnalysisVolumeReview;
        public HandPoseFitRequest Copy() {var copy=(HandPoseFitRequest)MemberwiseClone();copy.ContactRendererPaths=(ContactRendererPaths??Array.Empty<string>()).ToArray();copy.ContactRegions=(ContactRegions??Array.Empty<FitContactRegion>()).Select(x=>x?.Copy()).ToArray();copy.AnalysisVolumeReview=AnalysisVolumeReview?.Copy();return copy;}
    }

    public sealed class FitContactRegion
    {
        public string Name="grip";
        public Bounds Bounds=new Bounds(Vector3.zero,Vector3.one*.1f);
        public string[] RendererPaths=Array.Empty<string>(),AllowedZones=Array.Empty<string>(),AllowedSegments=Array.Empty<string>();
        public bool Reviewed,Required,Forbidden;
        public FitContactRegion Copy() => new FitContactRegion{Name=Name,Bounds=Bounds,RendererPaths=(RendererPaths??Array.Empty<string>()).ToArray(),AllowedZones=(AllowedZones??Array.Empty<string>()).ToArray(),AllowedSegments=(AllowedSegments??Array.Empty<string>()).ToArray(),Reviewed=Reviewed,Required=Required,Forbidden=Forbidden};
    }

    public sealed class FitJointReport {public string Zone,Segment,Path;public Vector3 PositionMeters;public Quaternion LocalRotation;}
    public sealed class FitReliability {public string Metric,Status,Reason;}
    public sealed class FitRegionReport {public string Name,Zone,FingerSegment;public bool Reviewed,Required,Expected;public double NearAreaMm2;public int Samples;}
    public sealed class FitComparison {public string Zone;public double DistanceP50DeltaMm,NearSurfaceFractionDelta;}
    public sealed class FitConvergence {public string Zone;public int OriginalSamples,DenseSamples;public double DistanceP50DeltaMm,NearSurfaceFractionDelta;}

    public sealed class FitAssetIdentity
    { public string Path, Guid, DependencyHash; public long LocalFileId; }

    public sealed class FitZoneReport
    {
        public string Zone;
        public int SampleCount;
        public double SampledAreaMm2, DistanceP10Mm, DistanceP50Mm, DistanceP95Mm;
        public double NearSurfaceFraction;
        public double? ExteriorNearAreaMm2, PenetrationAreaMm2, PenetrationMaxMm;
        public int UnknownSignSamples;
        public string SignReason;
        public double[] DistanceBinAreaMm2;
        public double SamplesPerMm2;
        public double? MeanNearNormalOpposition;
        public double KnownExteriorNearAreaMm2,KnownPenetrationAreaMm2,UnknownSignAreaMm2;
    }

    public sealed class FitSurfaceReport
    { public string Path, Kind; public int Triangles; public FitTopology Topology; }

    public sealed class FitCameraReport
    {
        public string View, Mode, Image;
        public Vector3 Position, Forward, Up;
        public float OrthographicSizeMeters;
        public Vector3 SectionNormal;
        public float SectionOffsetMeters;
    }

    public sealed class HandPoseFitReport
    {
        public int SchemaVersion = 2;
        public string AlgorithmVersion = "mesh-fit-0.4-local-volume", CreatedUtc, UnityVersion, Status, Error, OutputDirectory;
        public string EvidenceMode;
        public FitVolumeReviewAudit LocalVolumeReviewAudit;
        public string LocalVolumeDistanceMeaning="unsigned_distance_and_bins_to_actual_analysis_volume_meters_object_root; not_grip_acceptance";
        public FitAssetIdentity Avatar, Object, Pose;
        public HandPoseFitRequest Settings;
        public string GrabbablePath, GripAvatarGuid, PoseType;
        public string[] AvatarGuidChain;
        public bool DefaultGrip, PoseInherited;
        public bool AlignToControllerEnabled, ControllerAlignmentApplied;
        public string ControllerModel;
        public float AppliedBlend, ObjectScale;
        public Vector3? ObjectScaleAxes;
        public int HandTriangles, ObjectTriangles, ContactTriangles, IntersectingHandTriangles, IntersectionPairs;
        public string IntersectionMeaning = "surface_touch_or_crossing_not_penetration_depth";
        public List<string> Limitations = new List<string>();
        public List<FitZoneReport> Zones = new List<FitZoneReport>();
        public List<FitSurfaceReport> Surfaces = new List<FitSurfaceReport>();
        public List<FitCameraReport> Cameras = new List<FitCameraReport>();
        public int ContactCandidateCount;
        public string ContactMarkerMeaning = "pairs_within_distance_range; inside_red_outside_green_unknown_yellow; object_endpoint_cyan";
        public List<FitContactCandidate> ContactMarkers = new List<FitContactCandidate>();
        public string JsonPath;
        public string CaptureSource="prefab_static", StateName,SeriesName,RuntimePoseName,ReferenceComparisonStatus;
        public string EditorSnapshotHash,EditorPoseStateJson,LivePreviewMethod;
        public string EditorAppliedPoseStateJson,EditorObjectSnapshotHash,EditorContactObjectHash;
        public bool EditorPoseDirty;
        public float? LiveFieldVoxelMm,LiveFieldErrorMm;
        public int CaptureFrame=-1,HandSelfIntersectionPairs,OtherHandIntersectionPairs,OtherHandTriangles;
        public float CaptureGameTime;
        public List<FitJointReport> Joints=new List<FitJointReport>();
        public List<FitZoneReport> Segments=new List<FitZoneReport>();
        public List<FitZoneReport> LocalVolumeZones=new List<FitZoneReport>();
        public List<string> Findings=new List<string>();
        public List<FitRegionReport> Regions=new List<FitRegionReport>();
        public List<FitContactPatch> ContactPatches=new List<FitContactPatch>();
        public List<FitReliability> Reliability=new List<FitReliability>();
        public List<FitComparison> ReferenceComparison=new List<FitComparison>();
        public List<FitConvergence> SamplingConvergence=new List<FitConvergence>();
        public FitAssetIdentity AnalysisVolume;
        public FitTopology AnalysisVolumeTopology;
        public bool LocalAnalysisVolumeApplied;
        public string DistanceBinMeaning="unsigned_mm: [0,.5], (.5,1], (1,2], (2,5], (5,infinity); not_quality_thresholds";
    }

    // Unity-векторы имеют вычисляемые свойства normalized; обычная JSON-рефлексия рекурсивна.
    public sealed class FitUnityJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t==typeof(Vector3) || t==typeof(Quaternion) || t==typeof(Bounds);
        public override void WriteJson(JsonWriter w, object value, JsonSerializer s)
        {
            if(value is Bounds b) { w.WriteStartObject();w.WritePropertyName("center");s.Serialize(w,b.center);w.WritePropertyName("size");s.Serialize(w,b.size);w.WriteEndObject();return; }
            w.WriteStartArray();
            if(value is Vector3 v) {w.WriteValue(v.x);w.WriteValue(v.y);w.WriteValue(v.z);}
            else {var q=(Quaternion)value;w.WriteValue(q.x);w.WriteValue(q.y);w.WriteValue(q.z);w.WriteValue(q.w);}
            w.WriteEndArray();
        }
        public override object ReadJson(JsonReader r,Type t,object existing,JsonSerializer s)
        {
            var token=JToken.Load(r);
            if(t==typeof(Bounds)) return new Bounds(token["center"].ToObject<Vector3>(s),token["size"].ToObject<Vector3>(s));
            var a=token.ToObject<float[]>();
            if(t==typeof(Vector3)) return new Vector3(a[0],a[1],a[2]);
            return new Quaternion(a[0],a[1],a[2],a[3]);
        }
        public static JsonSerializerSettings Settings => new JsonSerializerSettings {
            Formatting=Formatting.Indented, NullValueHandling=NullValueHandling.Include,
            Converters=new List<JsonConverter>{new FitUnityJsonConverter()}
        };
    }
}
