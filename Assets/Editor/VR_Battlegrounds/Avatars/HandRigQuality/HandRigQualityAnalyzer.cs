using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandRigQuality
{
    /// <summary>Статический анализ двух калиброванных кистей; не меняет исходные аватары.</summary>
    public static class HandRigQualityAnalyzer
    {
        public static HandRigQualityReport Analyze(HandRigQualityRequest request)
        {
            request=request?.Copy();
            var report=new HandRigQualityReport {CreatedUtc=DateTime.UtcNow.ToString("O"),UnityVersion=Application.unityVersion,Settings=request,Status="invalid_configuration"};
            try
            {
                if(request==null)throw new ArgumentNullException(nameof(request));
                if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                    throw new InvalidOperationException("Редактор занят Play/compile/import.");
                Validate(request);
                report.OutputDirectory=OutputDirectory(request.OutputDirectory);
                Directory.CreateDirectory(report.OutputDirectory);
                using(var target=new HandRigCapture(request.AvatarPath,request.PoseName,request.SensorPath,request))
                using(var reference=string.IsNullOrEmpty(request.ReferenceAvatarPath)?null:new HandRigCapture(request.ReferenceAvatarPath,request.ReferencePoseName,request.ReferenceSensorPath,request))
                {
                    report.Target=target.Data;report.Reference=reference?.Data;
                    Measure(target,reference,request,report);
                    HandRigQualityCalibration.Apply(report);
                    File.WriteAllText(Path.Combine(report.OutputDirectory,"geometry-target.json"),JsonConvert.SerializeObject(target.Meshes.Select(m=>m.Sample),HandRigJsonConverter.Settings),new UTF8Encoding(false));
                    if(reference!=null)File.WriteAllText(Path.Combine(report.OutputDirectory,"geometry-reference.json"),JsonConvert.SerializeObject(reference.Meshes.Select(m=>m.Sample),HandRigJsonConverter.Settings),new UTF8Encoding(false));
                    if(request.RenderImages)report.Images=HandRigQualityRenderer.Render(target,reference,report.OutputDirectory);
                    report.Status="measured_static";
                }
                report.Limitations.Add("measured означает полученное число. StaticScore использует только покрытые калибровкой статические критерии; это не полная приёмка кисти.");
                report.Limitations.Add("PCA ладони и сечения — геометрические приближения; разметка зон требует проверки по изображениям.");
                report.Limitations.Add("M8/M11/M14 требуют серии вращений/штатного IK; M12 требует реальной рукояти и Hand Pose Fit.");
            }
            catch(NotSupportedException e) {report.Status="unsupported_context";report.Error=e.Message;}
            catch(ArgumentException e) {report.Status="invalid_configuration";report.Error=e.Message;}
            catch(Exception e) {report.Status="analysis_incomplete";report.Error=e.ToString();}
            if(!string.IsNullOrEmpty(report.OutputDirectory))
            {
                File.WriteAllText(Path.Combine(report.OutputDirectory,"report.json"),JsonConvert.SerializeObject(report,HandRigJsonConverter.Settings),new UTF8Encoding(false));
                HandRigQualityRenderer.WriteIndex(report);
            }
            return report;
        }

        internal static void Validate(HandRigQualityRequest request)
        {
            if(!Finite(request.EyeHeightMeters)||request.EyeHeightMeters<=0||request.EyeHeightMeters>3)throw new ArgumentException("Высота глаз должна быть в (0;3] м.");
            if(!Finite(request.Blend)||request.Blend<0||request.Blend>1)throw new ArgumentException("Blend должен быть в [0;1].");
            if(!Finite(request.CuffStartMeters)||!Finite(request.CuffEndMeters)||!Finite(request.CuffRadiusMeters)||request.CuffStartMeters<.02f||request.CuffEndMeters<=request.CuffStartMeters||request.CuffRadiusMeters<=0)
                throw new ArgumentException("Задайте корректную область манжеты не ближе 2 см от запястья.");
        }

        internal static string OutputDirectory(string requested)
        {
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../Docs/tasks/report/hand-rig-quality"));
            string path=string.IsNullOrEmpty(requested)?Path.Combine(root,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)):Path.GetFullPath(requested);
            if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Пакет отчёта должен лежать в отдельной папке Docs/tasks/report/hand-rig-quality/.");
            return path;
        }

        internal static void Measure(HandRigCapture target, HandRigCapture reference, HandRigQualityRequest settings, HandRigQualityReport report)
        {
            void Metric(string id,string subject,string unit,double? value,string reason="",double? refValue=null,string status=null)
            {
                if(value.HasValue&&(double.IsNaN(value.Value)||double.IsInfinity(value.Value))) {value=null;reason="Расчёт дал нечисловой результат.";}
                report.Metrics.Add(new HandRigMetric {Id=id,Subject=subject,Unit=unit,Value=value,ReferenceValue=refValue,Reason=reason,Status=status??(value.HasValue?"measured":"unavailable")});
            }
            var data=target.Data;var baseline=reference?.Data;
            if(baseline!=null && data.FrameKind=="configured_sensor_rest" && baseline.FrameKind==data.FrameKind)
                Metric("M1","wrist","mm",Vector3.Distance(data.Wrist,baseline.Wrist)*1000,"Авторская рамка S; физический контроллер не проверен.");
            else Metric("M1","wrist","mm",null,"В рамке запястья смещение обнулено по определению; для M1 нужна явная рамка S и эталон.");

            CapturePalmAxes(target);if(reference!=null)CapturePalmAxes(reference);
            Metric("M2","palm_PCA","degrees",data.MeshForward.HasValue&&baseline?.MeshForward!=null?
                (double?)Quaternion.Angle(Quaternion.LookRotation(data.MeshForward.Value,data.MeshUp.Value),Quaternion.LookRotation(baseline.MeshForward.Value,baseline.MeshUp.Value)):null,
                "PCA по поверхности с весом Wrist ≥0,6; сопоставимость зоны ладони не подтверждена, оценка исключена из балла.",null,
                data.MeshForward.HasValue&&baseline?.MeshForward!=null?"unreviewed":null);
            double? AxisError(HandRigSnapshot s)=>s?.MeshForward.HasValue==true?(double?)Math.Max(Vector3.Angle(s.SdkForward,s.MeshForward.Value),Vector3.Angle(s.SdkUp,s.MeshUp.Value)):null;
            Metric("M3","palm_PCA","degrees",AxisError(data),"PCA-ось ориентирована по SDK; зона ладони не подтверждена, угловая шкала SDK нечувствительна. Оценка исключена из балла.",AxisError(baseline),AxisError(data).HasValue?"unreviewed":null);

            foreach(var joint in data.Joints)
            {
                var refJoint=baseline?.Joints.FirstOrDefault(j=>j.Segment==joint.Segment);
                var region=Region(target,joint);var section=Section(region,joint.Position,joint.Forward);
                var refRegion=reference!=null&&refJoint!=null?Region(reference,refJoint):null;
                var refSection=refRegion!=null?Section(refRegion,refJoint.Position,refJoint.Forward):null;
                double? Offset(HandSection s,HandRigJoint j)=>s?.Valid==true?(double?)Vector3.Distance(s.Center,j.Position)/s.Radius:null;
                Metric("M4",joint.Segment,"radius_ratio",Offset(section,joint),section.Reason,refSection!=null?Offset(refSection,refJoint):null);
                double? transition=WeightTransition(region,joint,out string why);
                double? refTransition=null;if(refRegion!=null)refTransition=WeightTransition(refRegion,refJoint,out _);
                Metric("M5",joint.Segment,"mm",transition,why,refTransition,transition.HasValue?"unreviewed":null);
                if(joint.Segment.EndsWith("/proximal",StringComparison.Ordinal))
                {
                    // Fist — только явно выбранная поза. Оценка не подменяет анатомическую дорсальную вершину.
                    bool fist=data.PoseName.IndexOf("fist",StringComparison.OrdinalIgnoreCase)>=0;
                    double? depth=fist&&section.Valid?(double?)section.Points.Max(p=>Vector3.Dot(p-joint.Position,data.SdkUp))*1000:null;
                    Metric("M6",joint.Segment,"mm",depth,fist?"Секционный максимум вдоль SDK Up; дорсальная вершина требует визуальной разметки.":"Нужна явно выбранная поза Fist.",null,depth.HasValue?"unreviewed":"unavailable");
                }
                Metric("M9",joint.Segment,"mm",section.Valid&&refSection?.Valid==true?(double?)(section.Radius-refSection.Radius)*1000:null,
                    "Разность эквивалентных радиусов сечений sqrt(A/pi); не точная толщина перчатки над кожей.");
                Metric("M10",joint.Segment,"percent",joint.LengthMeters.HasValue&&refJoint?.LengthMeters>1e-8f?
                    (double?)(100*(joint.LengthMeters.Value/refJoint.LengthMeters.Value-1)):null,
                    "Длина между размеченными суставами; конец distal без следующего сустава не восстанавливается.");
            }
            var middle=data.Joints.FirstOrDefault(j=>j.Segment=="middle/proximal");
            var refMiddle=baseline?.Joints.FirstOrDefault(j=>j.Segment=="middle/proximal");
            Metric("M10","wrist_to_middle_proximal","percent",middle!=null&&refMiddle!=null?
                (double?)(100*(Vector3.Distance(data.Wrist,middle.Position)/Vector3.Distance(baseline.Wrist,refMiddle.Position)-1)):null,
                "Размер ладони по скелету, а не полная длина руки до кончика пальца.");

            double cuffArea=0,cuffWeighted=0;int cuffTriangles=0;
            foreach(var mesh in target.Meshes)
            {
                var leakage=data.Forearm.HasValue?HandRigQualityMath.CuffLeakage(mesh.Sample.Vertices,mesh.Sample.Triangles,mesh.HandWeights,data.Wrist,data.Forearm.Value-data.Wrist,settings.CuffStartMeters,settings.CuffEndMeters,settings.CuffRadiusMeters):new HandCuffLeakage();
                if(leakage.Valid){cuffArea+=leakage.Area;cuffWeighted+=leakage.Area*leakage.WeightMean;cuffTriangles+=leakage.Triangles;}
                var weights=new float[mesh.Sample.Vertices.Length][];
                for(int i=0;i<weights.Length;i++)
                {
                    weights[i]=new float[mesh.Sample.Bones.Length];
                    for(int j=mesh.Sample.WeightOffsets[i];j<mesh.Sample.WeightOffsets[i+1];j++)weights[i][mesh.Sample.Weights[j].BoneIndex]+=mesh.Sample.Weights[j].Weight;
                }
                Metric("M13",mesh.Sample.RendererPath,"weight_energy_per_edge",HandRigQualityMath.WeightEnergy(mesh.Sample.Vertices,mesh.HandTriangles,weights),
                    "Средняя по уникальным индексированным рёбрам; зависит от плотности сетки/LOD, универсальный порог не задан.");
            }
            Metric("M7","cuff","percent",cuffArea>1e-12?(double?)100*cuffWeighted/cuffArea:null,
                cuffArea>1e-12?"Цилиндр рукава, отбор по центрам треугольников; площадь="+(cuffArea*1e6).ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+" мм², треугольников="+cuffTriangles:"В выбранной области нет поверхности манжеты.",null,
                cuffArea>1e-12?(settings.CuffRegionReviewed?"measured":"unreviewed"):"unavailable");
            foreach(string id in new[]{"M8","M11","M14"})Metric(id,"wrist_sequence",id=="M14"?"degrees":"",null,"Нужен динамический прогон запястья; статический снимок не доказывает эту метрику.");
            Metric("M12","grip","mm",null,"Нужен реальный Grabbable/GrabPoint и Hand Pose Fit.");
        }

        static void CapturePalmAxes(HandRigCapture capture)
        {
            var vertices=new List<Vector3>();var indices=new List<int>();var weights=new List<float>();
            foreach(var mesh in capture.Meshes)
            {
                int offset=vertices.Count;vertices.AddRange(mesh.Sample.Vertices);weights.AddRange(mesh.PalmWeights);
                indices.AddRange(mesh.HandTriangles.Select(i=>i+offset));
            }
            if(HandRigQualityMath.PalmAxes(vertices.ToArray(),indices.ToArray(),weights.ToArray(),capture.Data.SdkForward,capture.Data.SdkUp,out var f,out var u))
            {capture.Data.MeshForward=f;capture.Data.MeshUp=u;}
        }

        sealed class JointRegion {public Vector3[] Vertices;public int[] Triangles,SurfaceIds;public float[] ChildWeights,FingerWeights;}

        static HandSection Section(JointRegion region,Vector3 position,Vector3 normal)=>
            HandRigQualityMath.FingerSectionBySurface(region.Vertices,region.Triangles,region.ChildWeights,region.FingerWeights,region.SurfaceIds,position,normal);

        static JointRegion Region(HandRigCapture capture, HandRigJoint joint)
        {
            var vertices=new List<Vector3>();var indices=new List<int>();var weights=new List<float>();var fingerWeights=new List<float>();var surfaces=new List<int>();int surface=0;
            string finger=joint.Segment.Split('/')[0];
            var first=capture.Data.Joints.First(j=>j.Segment.StartsWith(finger+"/",StringComparison.Ordinal));
            foreach(var mesh in capture.Meshes)
            {
                if(!mesh.HandWeights.Any(w=>w>0))continue;
                int offset=vertices.Count;vertices.AddRange(mesh.Sample.Vertices);
                surfaces.AddRange(Enumerable.Repeat(surface++,mesh.Sample.Vertices.Length));
                indices.AddRange(mesh.Sample.Triangles.Select(i=>i+offset));
                var bones=mesh.DescendantBones[joint.Segment];var owners=mesh.DescendantBones[first.Segment];
                for(int i=0;i<mesh.Sample.Vertices.Length;i++)
                {weights.Add(mesh.Sample.WeightOf(i,bones));fingerWeights.Add(mesh.Sample.WeightOf(i,owners));}
            }
            return new JointRegion{Vertices=vertices.ToArray(),Triangles=indices.ToArray(),ChildWeights=weights.ToArray(),FingerWeights=fingerWeights.ToArray(),SurfaceIds=surfaces.ToArray()};
        }

        static double? WeightTransition(JointRegion region, HandRigJoint joint, out string reason)
        {
            var samples=new float?[21];
            for(int i=-10;i<=10;i++)
            {
                float t=i*.001f;Vector3 point=joint.Position+joint.Forward*t;
                var section=Section(region,point,joint.Forward);
                if(section.Valid&&Finite(section.MeanWeight))samples[i+10]=section.MeanWeight;
            }
            var crossing=HandRigQualityMath.SampledWeightCrossing(samples,-.01f,.001f);
            reason=crossing.Reason+" Поиск ±10 мм, шаг1 мм; оценка исключена из балла до проверки профиля между сэмплами.";
            return crossing.Valid?(double?)crossing.PositionMeters*1000:null;
        }

        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
