using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.Editor.HandGeometry;

namespace VrBattlegrounds.Editor.HandRigQuality
{
    public sealed class HandRigQualityRequest
    {
        public string AvatarPath, ReferenceAvatarPath;
        public UxrHandSide Side=UxrHandSide.Right;
        public string PoseName="", ReferencePoseName="";
        public bool CommonSdkPose=true;
        public float Blend;
        public float EyeHeightMeters=1.72f;
        public string Frame="wrist", SensorPath="", ReferenceSensorPath="";
        public bool CuffRegionReviewed;
        public float CuffStartMeters=.02f, CuffEndMeters=.08f, CuffRadiusMeters=.05f;
        public bool RenderImages=true;
        public string OutputDirectory;
        public string CalibrationPath;
        public HandRigQualityRequest Copy()=>(HandRigQualityRequest)MemberwiseClone();
    }

    public sealed class HandRigMetric
    {
        public string Id, Subject, Unit, Status, Reason;
        public double? Value;
        public double? ReferenceValue;
    }

    public sealed class HandRigJoint
    {
        public string Segment, ParentSegment, Path;
        public Vector3 Position, Forward, Up;
        public float? LengthMeters;
    }

    public sealed class HandRigSnapshot
    {
        public HandAssetIdentity Avatar, Pose;
        public string Side, FrameKind, PoseName;
        public float EyeHeightMeters, EyesBaseHeightMeters, AppliedScale;
        public Vector3 SourceScale, Wrist, SdkForward, SdkUp;
        public Vector3? Forearm;
        public Vector3? MeshForward, MeshUp;
        public List<HandRigJoint> Joints=new List<HandRigJoint>();
        public List<HandRigSurface> Surfaces=new List<HandRigSurface>();
        public List<string> Limitations=new List<string>();
    }

    public sealed class HandRigSurface
    {
        public string RendererPath;
        public HandAssetIdentity Mesh;
        public int Vertices, Triangles, Influences, BoneCount;
        public string[] BoneSegments;
    }

    public sealed class HandRigQualityReport
    {
        public int SchemaVersion=1;
        public string AlgorithmVersion="hand-rig-static-0.5", CreatedUtc, UnityVersion;
        public string Status, Error, OutputDirectory;
        public string EvidenceMode="editor_posed_mesh_no_runtime_IK";
        public HandRigQualityRequest Settings;
        public HandRigSnapshot Target, Reference;
        public List<HandRigMetric> Metrics=new List<HandRigMetric>();
        public List<string> Limitations=new List<string>();
        public string[] Images=Array.Empty<string>();
        public double? StaticScore;
        public string CalibrationStatus="not_calibrated";
        public int ScoredMetrics;
        public List<HandRigCriterionScore> CriterionScores=new List<HandRigCriterionScore>();
    }

    public sealed class HandRigCriterionScore {public string Metric;public double Score;public int Samples;}

    /// <summary>Компактная сериализация Unity-векторов без рекурсивных normalized/magnitude.</summary>
    public sealed class HandRigJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type type)
        {type=Nullable.GetUnderlyingType(type)??type;return type==typeof(Vector3)||type==typeof(Quaternion);}
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if(value==null){writer.WriteNull();return;}
            writer.WriteStartArray();
            if(value is Vector3 v) {writer.WriteValue(v.x);writer.WriteValue(v.y);writer.WriteValue(v.z);}
            else {var q=(Quaternion)value;writer.WriteValue(q.x);writer.WriteValue(q.y);writer.WriteValue(q.z);writer.WriteValue(q.w);}
            writer.WriteEndArray();
        }
        public override object ReadJson(JsonReader reader, Type type, object value, JsonSerializer serializer)
        {
            if(reader.TokenType==JsonToken.Null)return null;
            type=Nullable.GetUnderlyingType(type)??type;
            var a=JArray.Load(reader).ToObject<float[]>();
            return type==typeof(Vector3)?(object)new Vector3(a[0],a[1],a[2]):new Quaternion(a[0],a[1],a[2],a[3]);
        }
        public static JsonSerializerSettings Settings => new JsonSerializerSettings {
            Formatting=Formatting.Indented,Converters=new List<JsonConverter>{new HandRigJsonConverter()},
            FloatFormatHandling=FloatFormatHandling.String
        };
    }
}
