using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Единый редакторный вход: синхронный анализ, без изменения игровых ассетов.</summary>
    public static class HandPoseFitAnalyzer
    {
        public static HandPoseFitReport Analyze(HandPoseFitRequest request)
        {
            request=request?.Copy();
            var report=new HandPoseFitReport {CreatedUtc=DateTime.UtcNow.ToString("O"),UnityVersion=Application.unityVersion,Settings=request,Status="invalid_configuration"};
            try {
                if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Редактор занят Play/compile/import.");
                Resolve(request);
                report.Avatar=Identity(request.AvatarPrefab);report.Object=Identity(request.ObjectPrefab);
                string directory=request.OutputDirectory;
                if(string.IsNullOrEmpty(directory)) directory=Path.Combine("Temp/HandPoseFit",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
                directory=Path.GetFullPath(directory);
                string assets=Path.GetFullPath("Assets").TrimEnd(Path.DirectorySeparatorChar);
                if(directory.Equals(assets,StringComparison.OrdinalIgnoreCase)||directory.StartsWith(assets+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Отчёт хранится вне Assets, чтобы не запускать импорт.");
                Directory.CreateDirectory(directory);report.OutputDirectory=directory;
                using(var snapshot=new HandPoseFitSnapshot(request,report)) {
                    ProcessSnapshot(snapshot,report);
                }
            }
            catch(NotSupportedException e) {report.Status="unsupported_context";report.Error=e.Message;}
            catch(ArgumentException e) {report.Status="invalid_configuration";report.Error=e.Message;}
            catch(Exception e) {report.Status="analysis_incomplete";report.Error=e.ToString();}
            if(!string.IsNullOrEmpty(report.OutputDirectory)) {
                report.JsonPath=Path.Combine(report.OutputDirectory,"report.json");
                File.WriteAllText(report.JsonPath,JsonConvert.SerializeObject(report,FitUnityJsonConverter.Settings),new UTF8Encoding(false));
            }
            return report;
        }

        /// <summary>Обработка уже замороженного игрового снимка, без чтения живых Transform.</summary>
        public static HandPoseFitReport AnalyzeCaptured(HandPoseFitSnapshot snapshot,HandPoseFitReport report)
        {
            try {
                ValidateSettings(report.Settings);
                string directory=report.Settings.OutputDirectory;
                if(string.IsNullOrEmpty(directory)) directory=Path.Combine("Temp/HandPoseFit",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-runtime-"+Guid.NewGuid().ToString("N").Substring(0,6));
                directory=Path.GetFullPath(directory);string assets=Path.GetFullPath("Assets").TrimEnd(Path.DirectorySeparatorChar);
                if(directory.Equals(assets,StringComparison.OrdinalIgnoreCase)||directory.StartsWith(assets+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Отчёт хранится вне Assets.");
                Directory.CreateDirectory(directory);report.OutputDirectory=directory;ProcessSnapshot(snapshot,report);
            } catch(Exception e) {report.Status="analysis_incomplete";report.Error=e.ToString();}
            if(!string.IsNullOrEmpty(report.OutputDirectory)) {report.JsonPath=Path.Combine(report.OutputDirectory,"report.json");File.WriteAllText(report.JsonPath,JsonConvert.SerializeObject(report,FitUnityJsonConverter.Settings),new UTF8Encoding(false));}
            return report;
        }

        static void ProcessSnapshot(HandPoseFitSnapshot snapshot,HandPoseFitReport report)
        {
            var request=report.Settings;report.StateName=request.StateName;report.SeriesName=request.SeriesName;
            report.HandTriangles=snapshot.Hand.Count;report.ObjectTriangles=snapshot.ObjectSurface.Count;report.ContactTriangles=snapshot.ContactObject.Count;
            var full=new FitSurface(snapshot.ObjectSurface.ToArray());var contact=new FitSurface(snapshot.ContactObject.ToArray());
            FitTopology topology=HandPoseFitGeometry.Topology(full.Triangles);
            report.Surfaces.Add(new FitSurfaceReport{Path="whole_object",Kind="combined_object",Triangles=full.Triangles.Length,Topology=topology});
            foreach(FitTriangle t in snapshot.Hand) {int n=full.IntersectionCount(t);if(n>0) report.IntersectingHandTriangles++;report.IntersectionPairs+=n;}
            var samples=HandPoseFitGeometry.Sample(snapshot.ContactHand.ToArray(),request.SampleCount,request.Seed);
            foreach(var zone in samples.GroupBy(s=>snapshot.ContactHand[s.Triangle].Zone)) report.Zones.Add(Measure(zone.Key,zone.ToList(),contact,full,topology,request.ContactDistanceMinMm,request.ProximityMm));
            foreach(var zone in samples.GroupBy(s=>snapshot.ContactHand[s.Triangle].FingerSegment)) report.Segments.Add(Measure(zone.Key,zone.ToList(),contact,full,topology,request.ContactDistanceMinMm,request.ProximityMm));
            var candidates=HandPoseFitGeometry.ContactCandidates(snapshot.ContactHand.ToArray(),contact,full,topology,request.SampleCount,request.Seed,request.ContactDistanceMinMm,request.ProximityMm);
            HandPoseFitPrecision.Measure(snapshot,report,contact,full,topology,samples,candidates);
            report.ContactCandidateCount=candidates.Count;report.ContactPatches=HandPoseFitGeometry.ContactPatches(snapshot.ContactHand.ToArray(),candidates);
            foreach(var z in report.Zones) {var pairs=candidates.Where(p=>p.Zone==z.Zone).ToArray();if(pairs.Length>0) z.MeanNearNormalOpposition=pairs.Sum(p=>p.NormalOppositionDot*p.AreaMm2)/pairs.Sum(p=>p.AreaMm2);}
            int markerCount=Math.Min(request.MaxContactMarkers,candidates.Count);
            for(int i=0;i<markerCount;i++) report.ContactMarkers.Add(candidates[(int)((long)i*candidates.Count/markerCount)]);
            report.Status=report.Reliability.Any(x=>x.Status!="measured")?"analysis_incomplete":"analysis_complete";
            report.Limitations.Add("Диапазон контакта не задаёт норму качества; отдельные показатели и неизвестность не заменяются итоговым баллом.");
            report.Limitations.Add("Связность контактных участков и распределение площади оцениваются по выборке; локальные дефекты между точками могут быть пропущены.");
            if(request.RenderImages) HandPoseFitRenderer.Render(snapshot,report,contact,full,topology);
        }

        /// <summary>JSON-вход для execute_code. Объекты указываются AssetDatabase-путями.</summary>
        public static string RunJson(string requestJson)
        {
            var request=JsonConvert.DeserializeObject<HandPoseFitRequest>(requestJson,FitUnityJsonConverter.Settings);
            return JsonConvert.SerializeObject(Analyze(request),FitUnityJsonConverter.Settings);
        }

        /// <summary>Серия статических кандидатов/ухудшений без назначения игровых поз.</summary>
        public static string RunBatchJson(string requestsJson)
        {
            var requests=JsonConvert.DeserializeObject<HandPoseFitRequest[]>(requestsJson,FitUnityJsonConverter.Settings);
            if(requests==null||requests.Length==0||requests.Length>30) throw new ArgumentException("Пакет: 1..30 запросов.");
            var reports=new List<HandPoseFitReport>();foreach(var request in requests) reports.Add(Analyze(request));
            return JsonConvert.SerializeObject(reports,FitUnityJsonConverter.Settings);
        }

        static void Resolve(HandPoseFitRequest r)
        {
            if(r==null) throw new ArgumentException("Не задан запрос.");
            if(!r.AvatarPrefab && !string.IsNullOrEmpty(r.AvatarPath)) r.AvatarPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(r.AvatarPath)?.GetComponent<UltimateXR.Avatar.UxrAvatar>();
            if(!r.ObjectPrefab && !string.IsNullOrEmpty(r.ObjectPath)) r.ObjectPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(r.ObjectPath);
            if(r.ObjectPrefab && r.ObjectPrefabLocalFileId!=0) {
                var root=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(r.ObjectPrefab));
                r.ObjectPrefab=root.GetComponentsInChildren<Transform>(true).Select(t=>t.gameObject).FirstOrDefault(go=>{string guid;long id;return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(go,out guid,out id)&&id==r.ObjectPrefabLocalFileId;});
                if(!r.ObjectPrefab) throw new ArgumentException("Не найден GameObject с ObjectPrefabLocalFileId.");
            }
            if(!r.PoseOverride && !string.IsNullOrEmpty(r.PoseOverridePath)) r.PoseOverride=AssetDatabase.LoadAssetAtPath<UltimateXR.Manipulation.HandPoses.UxrHandPoseAsset>(r.PoseOverridePath);
            if(!r.AvatarPrefab || !r.ObjectPrefab) throw new ArgumentException("Нужны префабы аватара и предмета.");
            if(!EditorUtility.IsPersistent(r.AvatarPrefab)||!EditorUtility.IsPersistent(r.ObjectPrefab)) throw new ArgumentException("Входы должны быть сохранёнными ассетами, не сценовыми экземплярами.");
            if(!string.IsNullOrEmpty(r.PoseOverridePath) && !r.PoseOverride) throw new ArgumentException("Не найден PoseOverridePath.");
            if(r.AlignmentMode!="grip_reference" && r.AlignmentMode!="controller_reference") throw new ArgumentException("AlignmentMode: grip_reference или controller_reference.");
            if(r.AlignmentMode=="controller_reference") throw new NotSupportedException("Controller alignment пока не воспроизводится изолированным snapshot. Используйте grip_reference; дополнительный игровой поворот не проверяется.");
            ValidateSettings(r);
            r.AvatarPath=AssetDatabase.GetAssetPath(r.AvatarPrefab);r.ObjectPath=AssetDatabase.GetAssetPath(r.ObjectPrefab);
            string objectGuid;long objectId;AssetDatabase.TryGetGUIDAndLocalFileIdentifier(r.ObjectPrefab,out objectGuid,out objectId);r.ObjectPrefabLocalFileId=objectId;
        }

        public static void ValidateSettings(HandPoseFitRequest r)
        {
            if(r==null) throw new ArgumentException("Не задан запрос.");
            if(r.SampleCount<100 || r.SampleCount>100000 || r.ImageSize<256 || r.ImageSize>2048) throw new ArgumentException("SampleCount: 100..100000; ImageSize: 256..2048.");
            if(float.IsNaN(r.ProximityMm)||float.IsInfinity(r.ProximityMm)||r.ProximityMm<=0) throw new ArgumentException("ProximityMm должен быть конечным и >0.");
            if(float.IsNaN(r.ContactDistanceMinMm)||float.IsInfinity(r.ContactDistanceMinMm)||r.ContactDistanceMinMm<0||r.ContactDistanceMinMm>r.ProximityMm) throw new ArgumentException("Нижняя граница контакта: 0..ProximityMm.");
            if(r.MaxContactMarkers<1||r.MaxContactMarkers>2000||r.ContactMarkerRadiusPixels<1||r.ContactMarkerRadiusPixels>10||float.IsNaN(r.ContactMarkerRadiusPixels)) throw new ArgumentException("MaxContactMarkers: 1..2000; радиус: 1..10 пикселей.");
            if(float.IsNaN(r.FrameSizeMeters)||float.IsInfinity(r.FrameSizeMeters)||r.FrameSizeMeters<.01f||r.FrameSizeMeters>10) throw new ArgumentException("FrameSizeMeters: .01..10.");
            if(r.ContactRendererPaths==null) r.ContactRendererPaths=Array.Empty<string>();
            if(r.ContactRegions==null) r.ContactRegions=Array.Empty<FitContactRegion>();
            if(r.CheckSamplingConvergence && r.SampleCount>50000) throw new ArgumentException("Для двойной выборки SampleCount не больше 50000.");
            foreach(var region in r.ContactRegions) if(region==null||string.IsNullOrWhiteSpace(region.Name)||!Finite(region.Bounds.center)||!Finite(region.Bounds.size)||region.Bounds.size.x<=0||region.Bounds.size.y<=0||region.Bounds.size.z<=0) throw new ArgumentException("Неверный контактный регион.");
            if(!Finite(r.AnalysisVolumePositionMeters)||!Finite(r.AnalysisVolumeEulerDegrees)||!Finite(r.AnalysisVolumeScale)||r.AnalysisVolumeScale.x<=0||r.AnalysisVolumeScale.y<=0||r.AnalysisVolumeScale.z<=0) throw new ArgumentException("Неверное преобразование аналитического объёма.");
        }
        static bool Finite(Vector3 v) => !(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z));

        internal static FitZoneReport Measure(string name,List<FitSample> samples,FitSurface contact,FitSurface full,FitTopology topology,float minimum,float tolerance)
        {
            var z=new FitZoneReport{Zone=name,SampleCount=samples.Count,SignReason=topology.Reason,DistanceBinAreaMm2=new double[5]};
            var distances=new List<double>();double near=0,exterior=0,penetration=0,max=0;
            foreach(FitSample sample in samples) {
                double distance=contact.Distance(sample.Position)*1000,area=sample.Area*1e6;distances.Add(distance);z.SampledAreaMm2+=area;
                int bin=distance<=.5?0:distance<=1?1:distance<=2?2:distance<=5?3:4;z.DistanceBinAreaMm2[bin]+=area;
                bool? inside=HandPoseFitGeometry.IsInside(sample.Position,full,topology);
                bool inRange=distance>=minimum-1e-4 && distance<=tolerance+1e-4;
                if(inRange) near+=area;
                if(!inside.HasValue) {z.UnknownSignSamples++;z.UnknownSignAreaMm2+=area;}
                else if(inside.Value) {penetration+=area;max=Math.Max(max,full.Distance(sample.Position)*1000);}
                else if(inRange) exterior+=area;
            }
            distances.Sort();z.DistanceP10Mm=Quantile(distances,.1);z.DistanceP50Mm=Quantile(distances,.5);z.DistanceP95Mm=Quantile(distances,.95);
            z.NearSurfaceFraction=near/z.SampledAreaMm2;
            z.SamplesPerMm2=z.SampleCount/z.SampledAreaMm2;
            z.KnownExteriorNearAreaMm2=exterior;z.KnownPenetrationAreaMm2=penetration;
            if(z.UnknownSignSamples==0) {z.ExteriorNearAreaMm2=exterior;z.PenetrationAreaMm2=penetration;z.PenetrationMaxMm=max;}
            return z;
        }
        static double Quantile(List<double> values,double q)
        {double x=(values.Count-1)*q;int i=(int)x;return values[i]+(values[Math.Min(i+1,values.Count-1)]-values[i])*(x-i);}
        public static FitAssetIdentity Identity(UnityEngine.Object asset)
        {
            string path=asset?AssetDatabase.GetAssetPath(asset):null;
            long localId=0;string guid=null;if(asset&&EditorUtility.IsPersistent(asset)) AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset,out guid,out localId);
            return new FitAssetIdentity {Path=path,Guid=guid??(string.IsNullOrEmpty(path)?null:AssetDatabase.AssetPathToGUID(path)),DependencyHash=string.IsNullOrEmpty(path)?null:AssetDatabase.GetAssetDependencyHash(path).ToString(),LocalFileId=localId};
        }
    }
}
