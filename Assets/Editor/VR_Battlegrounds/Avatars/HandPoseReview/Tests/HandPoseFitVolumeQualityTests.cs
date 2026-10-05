using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview.Tests
{
    // Только математическая геометрия: нет preview, SDK-захвата и игрового принятия поз.
    public class HandPoseFitVolumeQualityTests
    {
        sealed class Fixture
        {
            public HandPoseFitSnapshot Snapshot;
            public HandPoseFitReport Report;
            public List<FitSample> Samples;
            public FitSurface Contact;
        }

        static Fixture Make(float sampleZ, bool open=false)
        {
            var request=new HandPoseFitRequest {AnalysisVolumeReviewed=true, CheckHandSelfIntersections=false, IncludeOtherHand=false};
            var report=new HandPoseFitReport {
                Settings=request, Object=new FitAssetIdentity {Guid="mathematical-object",LocalFileId=9223372036854770000L,DependencyHash="object-v1"},
                AnalysisVolume=new FitAssetIdentity {Guid="mathematical-volume",LocalFileId=42,DependencyHash="volume-v1"},
                ObjectScale=1, ObjectScaleAxes=Vector3.one, CaptureSource="mathematical_fixture"
            };
            // Не вызываем конструктор, создающий PreviewRenderUtility; инициализируем только списки.
            var snapshot=(HandPoseFitSnapshot)FormatterServices.GetUninitializedObject(typeof(HandPoseFitSnapshot));
            foreach(string name in new[]{"Hand","ContactHand","ObjectSurface","ContactObject","OtherHand","AnalysisVolume"})
                typeof(HandPoseFitSnapshot).GetField(name).SetValue(snapshot,new List<FitTriangle>());
            var cube=HandPoseFitGeometryTests.Cube().Select(t=>new FitTriangle(t.A*.01f,t.B*.01f,t.C*.01f)).ToArray();
            snapshot.AnalysisVolume.AddRange(open?cube.Take(11):cube);
            report.AnalysisVolumeTopology=HandPoseFitGeometry.Topology(snapshot.AnalysisVolume.ToArray());
            var hand=new FitTriangle(new Vector3(-.001f,-.001f,sampleZ),new Vector3(.001f,-.001f,sampleZ),new Vector3(0,.001f,sampleZ),"index");
            snapshot.Hand.Add(hand); snapshot.ContactHand.Add(hand);
            // Посторонняя contact surface далеко от локального объёма.
            var distant=new FitTriangle(new Vector3(-1,-1,.1f),new Vector3(1,-1,.1f),new Vector3(0,1,.1f));
            snapshot.ObjectSurface.Add(distant);snapshot.ContactObject.Add(distant);
            return new Fixture {Snapshot=snapshot,Report=report,Contact=new FitSurface(new[]{distant}),Samples=new List<FitSample> {
                new FitSample {Position=new Vector3(0,0,sampleZ),Area=.000001f,Triangle=0}
            }};
        }

        static Type ReviewApi => typeof(HandPoseFitPrecision).Assembly.GetType("VrBattlegrounds.Editor.HandPoseReview.HandPoseFitVolumeReview");
        static object Draft(Fixture f)
        {
            Assert.That(ReviewApi,Is.Not.Null,"Нужен явный API ручной привязки локального объёма.");
            var method=ReviewApi.GetMethod("CreateDraft",BindingFlags.Public|BindingFlags.Static);
            Assert.That(method,Is.Not.Null);
            return method.Invoke(null,new object[]{f.Snapshot,f.Report});
        }
        static void Set(object value,string name,object fieldValue)
        {
            var field=value.GetType().GetField(name);Assert.That(field,Is.Not.Null,"Поле review: "+name);field.SetValue(value,fieldValue);
        }
        static object Bind(Fixture f, bool required=true)
        {
            // На старом коде сначала воспроизводим смешанную поверхность, а не ошибку компиляции.
            if(!required&&ReviewApi==null) return null;
            var draft=Draft(f);Set(draft,"ReviewId","manual-math-regression");Set(draft,"Scope","mathematical_cube_only");Set(draft,"Accepted",true);
            var field=typeof(HandPoseFitRequest).GetField("AnalysisVolumeReview");Assert.That(field,Is.Not.Null);field.SetValue(f.Report.Settings,draft);
            return draft;
        }
        static void Measure(Fixture f)
        {
            HandPoseFitPrecision.Measure(f.Snapshot,f.Report,f.Contact,f.Contact,HandPoseFitGeometry.Topology(f.Contact.Triangles),f.Samples,new List<FitContactCandidate>());
        }

        [TestCase(.0105f,.5)] [TestCase(.012f,2)]
        public void LocalUnsignedDistanceAndNearAreaUseAnalysisVolume(float z,double expectedMm)
        {
            var f=Make(z);Bind(f,false);Measure(f);
            Assert.That(f.Report.LocalAnalysisVolumeApplied,Is.True);
            var local=f.Report.LocalVolumeZones.Single();
            Assert.That(local.DistanceP50Mm,Is.EqualTo(expectedMm).Within(.0001),"Локальная distance должна быть до volume, а не contact object.");
            Assert.That(local.NearSurfaceFraction,Is.EqualTo(1));
            Assert.That(local.KnownExteriorNearAreaMm2,Is.EqualTo(1).Within(.00001));
        }

        [Test] public void LegacyReviewedFlagWithoutManualBindingNeverAppliesSign()
        {
            var f=Make(0);Measure(f);Assert.That(f.Report.LocalAnalysisVolumeApplied,Is.False,"Reviewed=true без ручной привязки недостаточно.");
            Assert.That(f.Report.LocalVolumeZones.All(z=>!z.PenetrationAreaMm2.HasValue),Is.True);
        }

        [Test] public void StaleActualTriangleGeometryInvalidatesReview()
        {
            var f=Make(0);Bind(f);
            for(int i=0;i<f.Snapshot.AnalysisVolume.Count;i++) {var t=f.Snapshot.AnalysisVolume[i];var d=Vector3.right*.0001f;f.Snapshot.AnalysisVolume[i]=new FitTriangle(t.A+d,t.B+d,t.C+d);}
            f.Report.AnalysisVolumeTopology=HandPoseFitGeometry.Topology(f.Snapshot.AnalysisVolume.ToArray());
            Assert.That(f.Report.AnalysisVolumeTopology.CanDetermineInside,Is.True,"Hash regression должен сохранить замкнутость.");
            Measure(f);Assert.That(f.Report.LocalAnalysisVolumeApplied,Is.False);
        }

        [TestCase("transform")] [TestCase("fileidentity")] [TestCase("side")] [TestCase("state")] [TestCase("dependency")]
        public void StaleContextInvalidatesReview(string change)
        {
            var f=Make(0);Bind(f);
            if(change=="transform") f.Report.Settings.AnalysisVolumePositionMeters=Vector3.right*.001f;
            if(change=="fileidentity") f.Report.Object.LocalFileId--;
            if(change=="side") {var side=typeof(HandPoseFitRequest).GetField("Side");side.SetValue(f.Report.Settings,Enum.Parse(side.FieldType,"Left"));}
            if(change=="state") f.Report.Settings.StateName="alternate";
            if(change=="dependency") f.Report.Object.DependencyHash="object-v2";
            Measure(f);Assert.That(f.Report.LocalAnalysisVolumeApplied,Is.False,"Изменённый контекст: "+change);
        }

        [Test] public void ClosedVolumeSeparatesKnownExteriorFromContainedHand()
        {
            var outer=Make(.0105f);Bind(outer);Measure(outer);
            var inner=Make(0);Bind(inner);Measure(inner);
            Assert.That(outer.Report.LocalVolumeZones.Single().UnknownSignSamples,Is.Zero);
            Assert.That(outer.Report.LocalVolumeZones.Single().KnownPenetrationAreaMm2,Is.Zero);
            Assert.That(inner.Report.LocalVolumeZones.Single().KnownPenetrationAreaMm2,Is.EqualTo(1).Within(.00001));
            Assert.That(inner.Report.LocalVolumeZones.Single().PenetrationMaxMm,Is.EqualTo(10).Within(.0001));
        }

        [Test] public void BoundaryRemainsUnknownEvenWithManualBinding()
        {
            var f=Make(.01f);Bind(f);Measure(f);
            var local=f.Report.LocalVolumeZones.Single();Assert.That(local.UnknownSignSamples,Is.EqualTo(1));Assert.That(local.PenetrationAreaMm2,Is.Null);
        }

        [Test] public void OpenVolumeRemainsUnknownEvenWithManualBinding()
        {
            var f=Make(0,true);Bind(f);Measure(f);Assert.That(f.Report.LocalAnalysisVolumeApplied,Is.False);
            Assert.That(f.Report.LocalVolumeZones.All(z=>!z.PenetrationAreaMm2.HasValue),Is.True);
        }

        [Test] public void CopyDoesNotShareMutableManualReview()
        {
            var f=Make(0);var review=Bind(f);var copied=f.Report.Settings.Copy();
            var field=typeof(HandPoseFitRequest).GetField("AnalysisVolumeReview");var copiedReview=field.GetValue(copied);
            Assert.That(copiedReview,Is.Not.SameAs(review));Set(copiedReview,"Scope","changed");
            Assert.That(review.GetType().GetField("Scope").GetValue(review),Is.EqualTo("mathematical_cube_only"));
        }

        [TestCase("ReviewId","")] [TestCase("Scope","")] [TestCase("Units","millimeters")] [TestCase("Frame","world")]
        public void InvalidManualReviewFieldsFailClosed(string field,string value)
        {
            var f=Make(0);var review=Bind(f);Set(review,field,value);Measure(f);Assert.That(f.Report.LocalAnalysisVolumeApplied,Is.False);
        }

        [Test] public void UnacceptedDraftFailsClosed()
        {
            var f=Make(0);var review=Bind(f);Set(review,"Accepted",false);Measure(f);Assert.That(f.Report.LocalAnalysisVolumeApplied,Is.False);
        }

        [TestCase("triangle")] [TestCase("scale")] [TestCase("transform")]
        public void NonfiniteGeometryOrContextFailsClosed(string source)
        {
            var f=Make(0);Bind(f);
            if(source=="triangle") {var t=f.Snapshot.AnalysisVolume[0];t.A.x=float.NaN;f.Snapshot.AnalysisVolume[0]=t;}
            if(source=="scale") f.Report.ObjectScaleAxes=new Vector3(float.NaN,1,1);
            if(source=="transform") f.Report.Settings.AnalysisVolumePositionMeters=new Vector3(float.PositiveInfinity,0,0);
            Measure(f);Assert.That(f.Report.LocalAnalysisVolumeApplied,Is.False);
        }
    }
}
