using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Дополнительные измерения и пределы достоверности; автоматического балла нет.</summary>
    public static class HandPoseFitPrecision
    {
        public static void Measure(HandPoseFitSnapshot snapshot,HandPoseFitReport report,FitSurface contact,FitSurface full,FitTopology topology,List<FitSample> samples,List<FitContactCandidate> pairs)
        {
            var r=report.Settings;bool runtime=report.CaptureSource=="runtime_after_sdk_update";
            void Reliability(string metric,string status,string reason) => report.Reliability.Add(new FitReliability{Metric=metric,Status=status,Reason=reason});
            Reliability("capture",runtime?"measured":"approximate",runtime?"Заморожено после обновления SDK; поза/выравнивание не применялись повторно.":"Статический prefab; полный игровой цикл не воспроизводится.");
            Reliability("alignment",report.AlignToControllerEnabled&&!report.ControllerAlignmentApplied?"unknown":"measured",report.AlignToControllerEnabled&&!report.ControllerAlignmentApplied?"Дополнительный поворот контроллера не применён.":runtime?"Включён в наблюдаемую геометрию; модель отдельно не симулируется.":"Сохранённый transform хвата.");
            Reliability("whole_object_sign",topology.CanDetermineInside&&!report.Zones.Any(z=>z.UnknownSignSamples>0)?"measured":"unknown",topology.Reason);
            Reliability("contact_mask",r.MaskReviewed&&r.PalmarOnly&&r.Profile!="exploratory"?"approximate":"unknown","Кости/нормали не доказывают подушечку или наружный слой перчатки; нужна проверка маски.");
            Reliability("normal_opposition","approximate","-dot нормалей пары; ориентация импортированных поверхностей требует проверки.");
            Reliability("contact_patches","approximate","Связность граней с выбранными точками, не точная граница пятна контакта.");
            Reliability("joint_angles","measured","Позиции и local rotation записаны; анатомические ограничения не заданы автоматически.");
            if(r.CheckHandSelfIntersections) report.HandSelfIntersectionPairs=HandPoseFitGeometry.SelfIntersectionPairs(snapshot.Hand.ToArray());
            Reliability("hand_self_intersections",r.CheckHandSelfIntersections?"approximate":"unknown","Касание несоседних граней; общие вершины исключены, соседние складки могут быть пропущены.");
            report.OtherHandTriangles=snapshot.OtherHand.Count;
            if(snapshot.OtherHand.Count>0) {var other=new FitSurface(snapshot.OtherHand.ToArray());report.OtherHandIntersectionPairs=snapshot.Hand.Sum(other.IntersectionCount);}
            Reliability("other_hand",snapshot.OtherHand.Count>0?"approximate":"unknown",runtime?"Другая видимая кисть в том же кадре; пересечение включает касание.":"Другая кисть остаётся в исходной позе prefab; это не двухручный игровой хват.");

            foreach(var region in r.ContactRegions) {
                var triangles=snapshot.ObjectSurface.Where(t=>region.Bounds.Contains((t.A+t.B+t.C)/3) && ((region.RendererPaths?.Length??0)==0||region.RendererPaths.Contains(t.Source))).ToArray();
                if(triangles.Length==0) {report.Findings.Add("Регион "+region.Name+": нет выбранной поверхности.");continue;}
                var surface=new FitSurface(triangles);
                foreach(var group in samples.GroupBy(s=>(snapshot.ContactHand[s.Triangle].Zone,snapshot.ContactHand[s.Triangle].FingerSegment))) {
                    bool expected=!region.Forbidden&&((region.AllowedZones?.Length??0)==0||region.AllowedZones.Contains(group.Key.Zone))&&((region.AllowedSegments?.Length??0)==0||region.AllowedSegments.Contains(group.Key.FingerSegment));
                    var close=group.Where(s=>InRange(surface.Distance(s.Position)*1000,r)).ToArray();
                    report.Regions.Add(new FitRegionReport{Name=region.Name,Zone=group.Key.Zone,FingerSegment=group.Key.FingerSegment,Reviewed=region.Reviewed,Required=region.Required,Expected=expected,NearAreaMm2=close.Sum(s=>(double)s.Area*1e6),Samples=close.Length});
                    if(!expected&&close.Length>0) report.Findings.Add(region.Name+" / "+group.Key.FingerSegment+": близость в области, не назначенной этой зоне кисти; знак проверяется отдельно.");
                }
                if(region.Required&&!report.Regions.Any(x=>x.Name==region.Name&&x.Expected&&x.Samples>0)) report.Findings.Add(region.Name+": в выборке нет ожидаемого контакта 0–"+r.ProximityMm+" мм.");
            }
            foreach(var pair in pairs) pair.TargetRegion=string.Join(",",r.ContactRegions.Where(x=>x.Bounds.Contains(pair.TargetPosition)&&((x.RendererPaths?.Length??0)==0||x.RendererPaths.Contains(contact.Triangles[pair.TargetTriangle].Source))).Select(x=>x.Name));
            Reliability("semantic_regions",r.ContactRegions.Length>0&&r.ContactRegions.All(x=>x.Reviewed)?"approximate":"unknown","Регионы AABB и допустимые зоны заданы пользователем; unsigned близость ещё не доказывает внешний контакт.");

            report.LocalVolumeZones.Clear();report.LocalAnalysisVolumeApplied=false;
            report.LocalVolumeReviewAudit=HandPoseFitVolumeReview.Evaluate(snapshot,report);
            if(samples.Any(s=>float.IsNaN(s.Position.x)||float.IsNaN(s.Position.y)||float.IsNaN(s.Position.z)||float.IsInfinity(s.Position.x)||float.IsInfinity(s.Position.y)||float.IsInfinity(s.Position.z)||float.IsNaN(s.Area)||float.IsInfinity(s.Area)||s.Area<=0)) {
                report.LocalVolumeReviewAudit.Status="unknown";report.LocalVolumeReviewAudit.Reason="nonfinite_or_invalid_hand_samples";
            }
            bool valid=report.LocalVolumeReviewAudit.Status=="applied";
            report.LocalAnalysisVolumeApplied=valid;
            Reliability("local_volume",valid?"approximate":"unknown",report.LocalVolumeReviewAudit.Reason);
            if(valid) {
                var volume=new FitSurface(snapshot.AnalysisVolume.ToArray());
                foreach(var zone in samples.GroupBy(s=>snapshot.ContactHand[s.Triangle].Zone))
                    report.LocalVolumeZones.Add(HandPoseFitAnalyzer.Measure(zone.Key,zone.ToList(),volume,volume,report.AnalysisVolumeTopology,r.ContactDistanceMinMm,r.ProximityMm));
            }

            if(r.CheckSamplingConvergence) {
                var dense=HandPoseFitGeometry.Sample(snapshot.ContactHand.ToArray(),r.SampleCount*2,r.Seed);
                foreach(var group in dense.GroupBy(s=>snapshot.ContactHand[s.Triangle].Zone)) {
                    var measured=HandPoseFitAnalyzer.Measure(group.Key,group.ToList(),contact,full,topology,r.ContactDistanceMinMm,r.ProximityMm);var original=report.Zones.FirstOrDefault(z=>z.Zone==group.Key);
                    if(original!=null) report.SamplingConvergence.Add(new FitConvergence{Zone=group.Key,OriginalSamples=original.SampleCount,DenseSamples=measured.SampleCount,DistanceP50DeltaMm=measured.DistanceP50Mm-original.DistanceP50Mm,NearSurfaceFractionDelta=measured.NearSurfaceFraction-original.NearSurfaceFraction});
                }
            }
            Reliability("sampling",r.CheckSamplingConvergence?"approximate":"unknown",r.CheckSamplingConvergence?"Записаны изменения при удвоении выборки; это не доказательство отсутствия малых дефектов.":"Проверка сходимости не включена.");
            CompareReference(report);
        }

        static bool InRange(float mm,HandPoseFitRequest r) => mm>=r.ContactDistanceMinMm-1e-4f&&mm<=r.ProximityMm+1e-4f;

        public static void CompareReference(HandPoseFitReport report)
        {
            // Повторное сравнение не должно сохранять дельты ранее совместимого эталона.
            report.ReferenceComparison.Clear();
            try {CompareReferenceCore(report);}
            catch(Exception e) {report.ReferenceComparison.Clear();report.ReferenceComparisonStatus="invalid_reference";report.Findings.Add("Не удалось сравнить необязательный эталон: "+e.Message);}
        }

        static void CompareReferenceCore(HandPoseFitReport report)
        {
            var r=report.Settings;report.ReferenceComparisonStatus="not_requested";
            if(string.IsNullOrEmpty(r.ReferenceReportPath)) return;
            if(!r.ReferenceAccepted) {report.ReferenceComparisonStatus="reference_not_accepted";return;}
            var reference=JsonConvert.DeserializeObject<HandPoseFitReport>(File.ReadAllText(r.ReferenceReportPath),FitUnityJsonConverter.Settings);
            if(reference?.Settings==null||reference.Zones==null||reference.Zones.Count==0||reference.Zones.Any(z=>z==null||string.IsNullOrEmpty(z.Zone)||z.SampleCount<=0||double.IsNaN(z.DistanceP50Mm)||double.IsInfinity(z.DistanceP50Mm)||double.IsNaN(z.NearSurfaceFraction)||double.IsInfinity(z.NearSurfaceFraction))||!string.IsNullOrEmpty(reference.Error)) {report.ReferenceComparisonStatus="invalid_reference";return;}
            reference.Settings=reference.Settings.Copy();HandPoseFitAnalyzer.ValidateSettings(reference.Settings);
            var a=reference.Settings;
            if(string.IsNullOrEmpty(reference.Object?.Guid)||string.IsNullOrEmpty(report.Object?.Guid)) {report.ReferenceComparisonStatus="unknown_object_identity";return;}
            // Старый статический snapshot проверял uniform scale; у старого runtime этого инварианта нет.
            Vector3? referenceScale=reference.ObjectScaleAxes??(reference.CaptureSource=="prefab_static"?(Vector3?)(Vector3.one*reference.ObjectScale):null);
            Vector3? currentScale=report.ObjectScaleAxes??(report.CaptureSource=="prefab_static"?(Vector3?)(Vector3.one*report.ObjectScale):null);
            if(!referenceScale.HasValue||!currentScale.HasValue) {report.ReferenceComparisonStatus="unknown_scale_configuration";return;}
            if(referenceScale.Value!=currentScale.Value) {report.ReferenceComparisonStatus="incompatible_capture_or_state";return;}
            if(reference.CaptureSource!=report.CaptureSource||a.AlignmentMode!=r.AlignmentMode||reference.AlignToControllerEnabled!=report.AlignToControllerEnabled||reference.ControllerAlignmentApplied!=report.ControllerAlignmentApplied||reference.ControllerModel!=report.ControllerModel||reference.ObjectScale!=report.ObjectScale||a.StateName!=r.StateName||Math.Abs(reference.AppliedBlend-report.AppliedBlend)>1e-5) {report.ReferenceComparisonStatus="incompatible_capture_or_state";return;}
            if(reference.Object.Guid==report.Object.Guid&&reference.Object.DependencyHash!=report.Object.DependencyHash) {report.ReferenceComparisonStatus="changed_object_geometry_or_dependencies";return;}
            if(a.AnalysisVolumeMeshGuid!=r.AnalysisVolumeMeshGuid||a.AnalysisVolumeMeshLocalFileId!=r.AnalysisVolumeMeshLocalFileId||a.AnalysisVolumeMeshPath!=r.AnalysisVolumeMeshPath||a.AnalysisVolumePositionMeters!=r.AnalysisVolumePositionMeters||a.AnalysisVolumeEulerDegrees!=r.AnalysisVolumeEulerDegrees||a.AnalysisVolumeScale!=r.AnalysisVolumeScale||a.AnalysisVolumeReviewed!=r.AnalysisVolumeReviewed) {report.ReferenceComparisonStatus="incompatible_local_volume";return;}
            if(JsonConvert.SerializeObject(a.AnalysisVolumeReview,FitUnityJsonConverter.Settings)!=JsonConvert.SerializeObject(r.AnalysisVolumeReview,FitUnityJsonConverter.Settings)||JsonConvert.SerializeObject(reference.LocalVolumeReviewAudit,FitUnityJsonConverter.Settings)!=JsonConvert.SerializeObject(report.LocalVolumeReviewAudit,FitUnityJsonConverter.Settings)) {report.ReferenceComparisonStatus="incompatible_local_volume_binding";return;}
            bool compatible=reference.SchemaVersion==report.SchemaVersion&&reference.AlgorithmVersion==report.AlgorithmVersion&&a.Profile==r.Profile&&a.Side==r.Side&&a.PalmarOnly==r.PalmarOnly&&a.PalmNormalSign==r.PalmNormalSign&&a.PalmarMinDot==r.PalmarMinDot&&a.ContactDistanceMinMm==r.ContactDistanceMinMm&&a.ProximityMm==r.ProximityMm&&a.GrabPoint==r.GrabPoint&&a.GrabbableIndexPath==r.GrabbableIndexPath&&a.ContactBoundsEnabled==r.ContactBoundsEnabled&&a.ContactBounds==r.ContactBounds&&JsonConvert.SerializeObject(a.ContactRegions,FitUnityJsonConverter.Settings)==JsonConvert.SerializeObject(r.ContactRegions,FitUnityJsonConverter.Settings)&&a.ContactRendererPaths.SequenceEqual(r.ContactRendererPaths);
            if(!compatible) {report.ReferenceComparisonStatus="incompatible_configuration";return;}
            report.ReferenceComparisonStatus=reference.Object.Guid==report.Object.Guid&&reference.Object.LocalFileId==report.Object.LocalFileId?"same_object_zone_deltas":"different_object_exploratory_zone_deltas";
            foreach(var zone in report.Zones) {var original=reference.Zones.FirstOrDefault(z=>z.Zone==zone.Zone);if(original!=null) report.ReferenceComparison.Add(new FitComparison{Zone=zone.Zone,DistanceP50DeltaMm=zone.DistanceP50Mm-original.DistanceP50Mm,NearSurfaceFractionDelta=zone.NearSurfaceFraction-original.NearSurfaceFraction});}
            report.Limitations.Add("Сравнение зон с принятым эталоном не переносит автоматически допустимые нормы на другой размер кисти/форму предмета.");
        }
    }
}
