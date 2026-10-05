using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Известные математические fixtures; не игровые хорошие/плохие хваты.</summary>
    public static class HandPoseFitQualityProbe
    {
        public static string Run(string directory="tmp/HandPoseFitQuality20261005")
        {
            Directory.CreateDirectory(directory);var fixtures=new List<object>();
            foreach(var source in new[]{("cube10",.01f,"tuning"),("cube20",.02f,"holdout")})
            foreach(string variant in new[]{"exterior","no_thumb","contained","float","rotated","tilted_base","tilted_inward","boundary","open","unreviewed","stale_transform","stale_identity","stale_geometry","mismatched_topology","nonfinite_context"}) {
                var snapshot=HandPoseFitSnapshot.FromGeometry();
                var request=new HandPoseFitRequest {SampleCount=64,RenderImages=false,AnalysisVolumeReviewed=true,CheckHandSelfIntersections=false,IncludeOtherHand=false};
                string id=source.Item1+"-"+variant;
                var report=new HandPoseFitReport {Settings=request,CaptureSource="mathematical_fixture",EvidenceMode="synthetic_fixture_explicit_assumption",
                    Object=new FitAssetIdentity {Guid="math-object-"+source.Item1,LocalFileId=9000000000000000001L,DependencyHash="explicit_math_object_v1"},
                    AnalysisVolume=new FitAssetIdentity {Guid="math-volume-"+source.Item1,LocalFileId=9000000000000000002L,DependencyHash="explicit_math_cube_v1"},
                    ObjectScale=1,ObjectScaleAxes=Vector3.one,StateName="mathematical_diagnostic"};
                float half=source.Item2;
                snapshot.AnalysisVolume.AddRange(Cube(half));
                if(variant=="open"||variant=="mismatched_topology") snapshot.AnalysisVolume.RemoveAt(0);
                float z=variant=="contained"?0:variant=="float"?half+.03f:variant=="boundary"?half:half+.0005f;
                var hand=new FitTriangle(new Vector3(-.0002f,-.0002f,z),new Vector3(.0002f,-.0002f,z),new Vector3(0,.0002f,z),"index");
                if(variant=="tilted_base"||variant=="tilted_inward") {
                    float delta=variant=="tilted_inward"?-.002f:0;
                    hand=new FitTriangle(new Vector3(-.001f,-.001f,half+.001f+delta),new Vector3(.001f,-.001f,half+.003f+delta),new Vector3(0,.001f,half+.003f+delta),"index");
                }
                if(variant=="rotated") {var rotation=Quaternion.Euler(45,0,0);hand=new FitTriangle(rotation*hand.A,rotation*hand.B,rotation*hand.C,"index");}
                snapshot.Hand.Add(hand);snapshot.ContactHand.Add(hand);
                var remote=new FitTriangle(new Vector3(-1,-1,.2f),new Vector3(1,-1,.2f),new Vector3(0,1,.2f));
                snapshot.ObjectSurface.Add(remote);snapshot.ContactObject.Add(remote);
                // Явное допущение только для кодовых кубов, не пользовательское принятие real volume.
                var review=HandPoseFitVolumeReview.CreateDraft(snapshot,report);
                review.ReviewId="explicit-mathematical-fixture-v1";review.Scope="closed_cube_geometric_diagnostic_only";
                review.EvidenceMode="synthetic_fixture_explicit_assumption";review.Accepted=true;request.AnalysisVolumeReview=review;
                if(variant=="unreviewed") {request.AnalysisVolumeReviewed=false;request.AnalysisVolumeReview=null;}
                if(variant=="stale_transform") request.AnalysisVolumePositionMeters=Vector3.right*.001f;
                if(variant=="stale_identity") report.Object.LocalFileId--;
                if(variant=="stale_geometry") for(int i=0;i<snapshot.AnalysisVolume.Count;i++) {var t=snapshot.AnalysisVolume[i];var d=Vector3.right*.0001f;snapshot.AnalysisVolume[i]=new FitTriangle(t.A+d,t.B+d,t.C+d);}
                if(variant=="nonfinite_context") report.ObjectScaleAxes=new Vector3(float.NaN,1,1);
                report.AnalysisVolumeTopology=variant=="mismatched_topology"?HandPoseFitGeometry.Topology(Cube(half)):HandPoseFitGeometry.Topology(snapshot.AnalysisVolume.ToArray());
                var surface=new FitSurface(new[]{remote});var topology=HandPoseFitGeometry.Topology(surface.Triangles);
                var samples=HandPoseFitGeometry.Sample(snapshot.ContactHand.ToArray(),64,42);
                report.Zones.Add(HandPoseFitAnalyzer.Measure("index",samples,surface,surface,topology,0,2));
                HandPoseFitPrecision.Measure(snapshot,report,surface,surface,topology,samples,new List<FitContactCandidate>());
                // Не сохраняем NaN; audit уже зафиксировал отказ от изменённого контекста.
                if(variant=="nonfinite_context") report.ObjectScaleAxes=null;
                string path=Path.Combine(directory,id+".json");
                File.WriteAllText(path,JsonConvert.SerializeObject(report,Formatting.Indented,FitUnityJsonConverter.Settings));
                string label=variant=="exterior"||variant=="no_thumb"||variant=="tilted_base"?"positive":variant=="contained"||variant=="float"||variant=="rotated"||variant=="tilted_inward"?"negative":null;
                fixtures.Add(new {Case=id,SourceGroup=source.Item1,Split=source.Item3,Label=label,
                    LabelBasis=label==null?"unknown_or_intentionally_invalid_binding":label=="positive"?"known_geometric_exterior_contact_without_thumb_requirement":"known_geometric_containment_or_absent_contact",
                    Domain=label==null?"synthetic_abstention":"synthetic_closed",ReportPath=path.Replace('\\','/'),EvidenceMode=report.EvidenceMode,
                    BaselineCase=variant=="tilted_inward"?source.Item1+"-tilted_base":variant=="rotated"?source.Item1+"-exterior":null,
                    Perturbation=new {translation_mm=variant=="tilted_inward"?new[]{0f,0f,-2f}:new[]{0f,0f,0f},rotation_deg=variant=="rotated"?new[]{45f,0f,0f}:new[]{0f,0f,0f}}});
            }
            string index=Path.Combine(directory,"fixtures.json");
            File.WriteAllText(index,JsonConvert.SerializeObject(new {Fixtures=fixtures,AcceptedQuality=(object)null,Meaning="mathematical diagnostics only; no game grip labels"},Formatting.Indented));
            return index;
        }

        static FitTriangle[] Cube(float half)
        {
            var p=new[]{new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,1,-1),new Vector3(-1,1,-1),new Vector3(-1,-1,1),new Vector3(1,-1,1),new Vector3(1,1,1),new Vector3(-1,1,1)};
            int[] ix={0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5};
            return Enumerable.Range(0,12).Select(i=>new FitTriangle(p[ix[i*3]]*half,p[ix[i*3+1]]*half,p[ix[i*3+2]]*half)).ToArray();
        }
    }
}
