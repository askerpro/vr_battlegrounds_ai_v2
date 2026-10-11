using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;
using UltimateXR.Core;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Рамка distal-кости в кадре корня предмета: сустав, направление отпечатка и кончика той же кости.</summary>
    public sealed class FitFingerFrame
    {
        public string Finger;
        public Vector3 DistalJoint, FingerprintDirection, TipDirection;

        /// <summary>Те же оси, что SDK использует для FingerPrintDirection/TipDirection (UxrAvatarFingerInfo).</summary>
        public static FitFingerFrame FromBone(string finger,Transform distal,UltimateXR.Core.Math.UxrUniversalLocalAxes axes,Matrix4x4 worldToFrame) => new FitFingerFrame {
            Finger=finger,DistalJoint=worldToFrame.MultiplyPoint3x4(distal.position),
            FingerprintDirection=-worldToFrame.MultiplyVector(distal.TransformVector(axes.LocalUp)).normalized,
            TipDirection=worldToFrame.MultiplyVector(distal.TransformVector(axes.LocalForward)).normalized
        };
    }

    /// <summary>Сегмент кисти: расстояние со знаком (минус — точка кисти внутри предмета), мм и мм².</summary>
    public sealed class FitSegmentContact
    {
        public string Segment;
        public int Samples, Vertices;
        public double AreaMm2, MinSignedMm, InsideAreaMm2, PenetrationDepthMm, WindowAreaMm2;
        public double? P50SignedMm, WindowOpposition;
    }

    /// <summary>Подушечка distal одного пальца и сводка его сегментов.</summary>
    public sealed class FitFingerContact
    {
        public string Finger, Status;
        public int DistalTriangles, PadTriangles, PadSamples, PadVertices;
        public double DistalAreaMm2, PadAreaMm2, PadAreaFraction;
        // Проверки маски: ось кости против геометрической оси сустав→кончик; направление отпечатка
        // против независимой ладонной стороны из суставов (не для большого пальца); IoU двух подушечек.
        public double RigAxisAgreement;
        public double? JointPalmarAgreement, JointPadIoU;
        public double PadNormalAgreement;
        public Vector3 PadPointMeters, PadNormal;
        public double PadCentroidDepthMm, PadSignedDistanceMm, PadWinding;
        public double PadPointOpposition;
        public double PadMinSignedMm, PadP50SignedMm, PadMaxSignedMm, PadWindowAreaMm2, PadInsideAreaMm2, PadPenetrationDepthMm;
        public double? PadWindowOpposition;
        // Полнота: доля площади distal снаружи в окне 0..+окно (касание), попавшая в подушечку.
        public double DistalNearOutsideAreaMm2;
        public double? NearOutsideInPadFraction;
        public double FingerMinSignedMm, FingerPenetrationDepthMm, FingerInsideAreaMm2;
    }

    public sealed class FitContactMetrics
    {
        public string Status, Reason;
        public string Method="signed_distance=unsigned_nearest_whole_object*(winding>=0.5?-1:+1); generalized_winding_number=sum_solid_angles/4pi, BVH near-exact + first-order dipole far-field";
        public string PadMeaning="distal triangles whose centroid radial direction around the bone tip axis is within PadHalfAngleDeg of the SDK fingerprint direction (-LocalUp of the same distal bone)";
        public string SignMeaning="mm: >0 gap outside object, <0 hand point inside object; not_quality_thresholds";
        public float WindowMm, PadHalfAngleDeg, WindingBeta=FitSurface.WindingBeta;
        public int SampleCount, Seed, PadSamplesPerFinger;
        public double HandSampledAreaMm2, NegativeWindingAreaMm2, AmbiguousWindingFarAreaMm2;
        public int WindingCheckPoints, WindingCheckMismatches;
        public double WindingCheckMaxDeviation;
        public double ElapsedMs;
        public List<FitFingerContact> Fingers=new List<FitFingerContact>();
        public List<FitSegmentContact> Segments=new List<FitSegmentContact>();
        [JsonIgnore] public HashSet<int> PadTriangleIndices=new HashSet<int>(), DistalTriangleIndices=new HashSet<int>();
    }

    /// <summary>
    /// Знаковые метрики контакта по пальцам. Только измерение: без правил «хорошо/плохо» и без подсказок.
    /// Знак — обобщённое число оборотов всей поверхности предмета, работает на открытых мешах.
    /// </summary>
    public static class HandPoseContactMetrics
    {
        public static readonly string[] Fingers={"thumb","index","middle","ring","little"};

        struct Probe {public double Mm,Winding,Signed;public bool Inside;public Vector3 ObjectNormal;}

        sealed class Accumulator
        {
            public double Area,InsideArea,Depth,Window,OppositionSum,OppositionArea,Min=double.PositiveInfinity;
            public int Samples,Vertices;public readonly List<double> Signed=new List<double>();
            public void Sample(Probe p,double area,Vector3 handNormal,double window)
            {
                Samples++;Area+=area;Signed.Add(p.Signed);Min=Math.Min(Min,p.Signed);
                if(p.Inside) {InsideArea+=area;Depth=Math.Max(Depth,p.Mm);}
                if(Math.Abs(p.Signed)<=window) {Window+=area;OppositionSum+=-Vector3.Dot(handNormal,p.ObjectNormal)*area;OppositionArea+=area;}
            }
            public void Vertex(Probe p) {Vertices++;Min=Math.Min(Min,p.Signed);if(p.Inside) Depth=Math.Max(Depth,p.Mm);}
        }

        public static FitContactMetrics Measure(HandPoseFitSnapshot snapshot,HandPoseFitReport report,FitSurface objectSurface)
        {
            var joints=report.Joints.Where(j=>j.Segment=="proximal").GroupBy(j=>j.Zone).ToDictionary(g=>g.Key,g=>g.First().PositionMeters);
            Vector3? lateral=joints.TryGetValue("index",out var index)&&joints.TryGetValue("little",out var little)?index-little:(Vector3?)null;
            return Measure(snapshot.Hand,snapshot.FingerFrames??new List<FitFingerFrame>(),objectSurface,report.Settings,report.Settings.Side==UxrHandSide.Right,lateral);
        }

        /// <param name="hand">Треугольники кисти с метками FingerSegment вида "index/distal".</param>
        /// <param name="lateral">index proximal − little proximal; null отключает независимую проверку маски.</param>
        public static FitContactMetrics Measure(IReadOnlyList<FitTriangle> hand,IReadOnlyList<FitFingerFrame> frames,FitSurface surface,HandPoseFitRequest r,bool rightHand,Vector3? lateral)
        {
            var watch=Stopwatch.StartNew();
            double window=r.ContactWindowMm,cosHalf=Math.Cos(r.PadHalfAngleDeg*Math.PI/180);
            var m=new FitContactMetrics{WindowMm=r.ContactWindowMm,PadHalfAngleDeg=r.PadHalfAngleDeg,SampleCount=r.SampleCount,Seed=r.Seed,PadSamplesPerFinger=r.PadSamplesPerFinger,Status="measured"};
            var handArray=hand.ToArray();
            var vertexCache=new Dictionary<Vector3,Probe>();
            Probe Vertex(Vector3 v) {if(!vertexCache.TryGetValue(v,out var p)) vertexCache.Add(v,p=Evaluate(surface,v));return p;}

            // 1. Вся кисть: площадная выборка по сегментам + все вершины сегмента для минимума и глубины.
            var segments=new Dictionary<string,Accumulator>();
            Accumulator Segment(string name) {if(!segments.TryGetValue(name,out var a)) segments.Add(name,a=new Accumulator());return a;}
            var samples=HandPoseFitGeometry.Sample(handArray,r.SampleCount,r.Seed);var probes=new Probe[samples.Count];
            for(int i=0;i<samples.Count;i++) {
                var s=samples[i];var t=handArray[s.Triangle];var p=probes[i]=Evaluate(surface,s.Position);double area=s.Area*1e6;
                m.HandSampledAreaMm2+=area;Segment(Name(t)).Sample(p,area,t.Normal,window);
                if(p.Winding<=-.5) m.NegativeWindingAreaMm2+=area;
                if(p.Winding>.25&&p.Winding<.75&&p.Mm>window) m.AmbiguousWindingFarAreaMm2+=area;
            }
            foreach(var group in handArray.GroupBy(Name)) {
                var unique=new HashSet<Vector3>();foreach(var t in group) {unique.Add(t.A);unique.Add(t.B);unique.Add(t.C);}
                var a=Segment(group.Key);foreach(var v in unique) a.Vertex(Vertex(v));
            }
            foreach(var pair in segments.OrderBy(x=>x.Key,StringComparer.Ordinal)) m.Segments.Add(Report(pair.Key,pair.Value));

            // 2. Подушечки distal по рамке той же кости.
            if(frames.Count==0) {m.Status="no_finger_frames";m.Reason="Снимок не содержит рамок distal-костей.";}
            var checkPoints=new List<Vector3>();
            foreach(string finger in Fingers) {
                var frame=frames.FirstOrDefault(f=>f.Finger==finger);if(frame==null) continue;
                var fc=new FitFingerContact{Finger=finger,Status="measured"};m.Fingers.Add(fc);
                var distal=Enumerable.Range(0,handArray.Length).Where(i=>handArray[i].FingerSegment==finger+"/distal").ToArray();
                foreach(int i in distal) m.DistalTriangleIndices.Add(i);
                fc.DistalTriangles=distal.Length;fc.DistalAreaMm2=distal.Sum(i=>(double)handArray[i].Area)*1e6;
                var own=m.Segments.Where(x=>x.Segment.StartsWith(finger+"/",StringComparison.Ordinal)).ToArray();
                if(own.Length>0) {fc.FingerMinSignedMm=own.Min(x=>x.MinSignedMm);fc.FingerPenetrationDepthMm=own.Max(x=>x.PenetrationDepthMm);fc.FingerInsideAreaMm2=own.Sum(x=>x.InsideAreaMm2);}
                if(distal.Length==0) {fc.Status="no_distal_triangles";continue;}
                Vector3 joint=frame.DistalJoint,axis=frame.TipDirection.normalized;
                Vector3 tip=joint;float far=-1;
                foreach(int i in distal) foreach(var v in new[]{handArray[i].A,handArray[i].B,handArray[i].C}) {float d=(v-joint).sqrMagnitude;if(d>far) {far=d;tip=v;}}
                Vector3 geometricAxis=(tip-joint).normalized;fc.RigAxisAgreement=Vector3.Dot(axis,geometricAxis);
                Vector3 palmar=Orthogonal(frame.FingerprintDirection,axis);
                var pad=distal.Where(i=>InPad(handArray[i],joint,axis,palmar,cosHalf)).ToArray();
                foreach(int i in pad) m.PadTriangleIndices.Add(i);
                if(finger!="thumb"&&lateral.HasValue) {
                    Vector3 l=lateral.Value;Vector3 jointPalmar=Orthogonal(rightHand?Vector3.Cross(geometricAxis,l):Vector3.Cross(l,geometricAxis),axis);
                    fc.JointPalmarAgreement=Vector3.Dot(palmar,jointPalmar);
                    var other=new HashSet<int>(distal.Where(i=>InPad(handArray[i],joint,axis,jointPalmar,cosHalf)));var mine=new HashSet<int>(pad);
                    double union=mine.Union(other).Sum(i=>(double)handArray[i].Area);
                    fc.JointPadIoU=union>0?mine.Intersect(other).Sum(i=>(double)handArray[i].Area)/union:(double?)null;
                }
                fc.PadTriangles=pad.Length;fc.PadAreaMm2=pad.Sum(i=>(double)handArray[i].Area)*1e6;fc.PadAreaFraction=fc.PadAreaMm2/fc.DistalAreaMm2;
                var padSet=new HashSet<int>(pad);
                for(int i=0;i<samples.Count;i++) {
                    if(handArray[samples[i].Triangle].FingerSegment!=finger+"/distal"||probes[i].Signed<0||probes[i].Signed>window) continue;
                    double area=samples[i].Area*1e6;fc.DistalNearOutsideAreaMm2+=area;if(padSet.Contains(samples[i].Triangle)) fc.NearOutsideInPadFraction=(fc.NearOutsideInPadFraction??0)+area;
                }
                if(fc.DistalNearOutsideAreaMm2>0) fc.NearOutsideInPadFraction=(fc.NearOutsideInPadFraction??0)/fc.DistalNearOutsideAreaMm2;
                if(pad.Length==0) {fc.Status="empty_pad";continue;}

                var padTriangles=pad.Select(i=>handArray[i]).ToArray();
                Vector3 centroid=Vector3.zero;double padArea=0;foreach(var t in padTriangles) {centroid+=(t.A+t.B+t.C)/3*t.Area;padArea+=t.Area;}
                centroid/=(float)padArea;
                var hit=new FitSurface(padTriangles).NearestHit(centroid);
                fc.PadPointMeters=hit.Position;fc.PadNormal=hit.Normal;fc.PadCentroidDepthMm=(hit.Position-centroid).magnitude*1000;fc.PadNormalAgreement=Vector3.Dot(hit.Normal,palmar);
                var point=Evaluate(surface,hit.Position);fc.PadSignedDistanceMm=point.Signed;fc.PadWinding=point.Winding;fc.PadPointOpposition=-Vector3.Dot(hit.Normal,point.ObjectNormal);
                checkPoints.Add(hit.Position);

                var padAccumulator=new Accumulator();double max=double.NegativeInfinity;
                foreach(var s in HandPoseFitGeometry.Sample(padTriangles,r.PadSamplesPerFinger,r.Seed)) {var p=Evaluate(surface,s.Position);padAccumulator.Sample(p,s.Area*1e6,padTriangles[s.Triangle].Normal,window);max=Math.Max(max,p.Signed);}
                var padVertices=new HashSet<Vector3>();foreach(var t in padTriangles) {padVertices.Add(t.A);padVertices.Add(t.B);padVertices.Add(t.C);}
                foreach(var v in padVertices) {var p=Vertex(v);padAccumulator.Vertex(p);max=Math.Max(max,p.Signed);}
                var padReport=Report(finger+"/pad",padAccumulator);
                fc.PadSamples=padAccumulator.Samples;fc.PadVertices=padAccumulator.Vertices;
                fc.PadMinSignedMm=padReport.MinSignedMm;fc.PadP50SignedMm=padReport.P50SignedMm??double.NaN;fc.PadMaxSignedMm=max;
                fc.PadWindowAreaMm2=padReport.WindowAreaMm2;fc.PadInsideAreaMm2=padReport.InsideAreaMm2;fc.PadPenetrationDepthMm=padReport.PenetrationDepthMm;fc.PadWindowOpposition=padReport.WindowOpposition;
            }

            // 3. Самопроверка приближения числа оборотов точной суммой на детерминированном подмножестве.
            int step=Math.Max(1,samples.Count/256);for(int i=0;i<samples.Count;i+=step) checkPoints.Add(samples[i].Position);
            foreach(var p in checkPoints) {
                double fast=surface.WindingNumber(p),exact=surface.WindingNumberExact(p);
                m.WindingCheckPoints++;m.WindingCheckMaxDeviation=Math.Max(m.WindingCheckMaxDeviation,Math.Abs(fast-exact));if(fast>=.5!=exact>=.5) m.WindingCheckMismatches++;
            }
            m.ElapsedMs=watch.Elapsed.TotalMilliseconds;
            return m;
        }

        static string Name(FitTriangle t) => string.IsNullOrEmpty(t.FingerSegment)?"palm_wrist":t.FingerSegment;

        static Probe Evaluate(FitSurface surface,Vector3 p)
        {
            var hit=surface.NearestHit(p);double mm=(hit.Position-p).magnitude*1000.0,w=surface.WindingNumber(p);bool inside=w>=.5;
            return new Probe{Mm=mm,Winding=w,Inside=inside,Signed=inside?-mm:mm,ObjectNormal=hit.Normal};
        }

        /// <summary>Центр грани в полуугле вокруг направления отпечатка, радиально от оси кости.</summary>
        public static bool InPad(FitTriangle t,Vector3 joint,Vector3 axis,Vector3 palmar,double cosHalf)
        {
            Vector3 v=(t.A+t.B+t.C)/3-joint,radial=v-axis*Vector3.Dot(v,axis);
            float length=radial.magnitude;Vector3 direction=length>1e-7f?radial/length:t.Normal;
            return Vector3.Dot(direction,palmar)>=cosHalf;
        }

        static Vector3 Orthogonal(Vector3 v,Vector3 axis) => (v-axis*Vector3.Dot(v,axis)).normalized;

        static FitSegmentContact Report(string name,Accumulator a)
        {
            var sorted=a.Signed.OrderBy(x=>x).ToList();
            return new FitSegmentContact{Segment=name,Samples=a.Samples,Vertices=a.Vertices,AreaMm2=a.Area,
                MinSignedMm=double.IsInfinity(a.Min)?double.NaN:a.Min,P50SignedMm=sorted.Count>0?Quantile(sorted,.5):(double?)null,
                InsideAreaMm2=a.InsideArea,PenetrationDepthMm=a.Depth,WindowAreaMm2=a.Window,WindowOpposition=a.OppositionArea>0?a.OppositionSum/a.OppositionArea:(double?)null};
        }
        static double Quantile(List<double> values,double q) {double x=(values.Count-1)*q;int i=(int)x;return values[i]+(values[Math.Min(i+1,values.Count-1)]-values[i])*(x-i);}
    }
}
