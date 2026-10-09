using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Editor.HandGeometry;

namespace VrBattlegrounds.Editor.HandRigQuality
{
    public sealed class HandRigCalibrationBand
    {
        public string Side, Metric, Subject, Unit;
        public double SdkMaximum, GoodLimit, BadLimit;
        public int Samples;
    }

    public sealed class HandRigCalibration
    {
        public string AlgorithmVersion="hand-rig-static-0.5", Status, Error, CreatedUtc, Path;
        public HandAssetIdentity CommonPose;
        public float EyeHeightMeters;
        public List<HandAssetIdentity> SdkAssets=new List<HandAssetIdentity>();
        public List<HandRigCalibrationBand> Bands=new List<HandRigCalibrationBand>();
        public string[] Reports;
        public string Scope="common_sdk_default_wrist_frame_both_sides; static_geometry_only";
    }

    /// <summary>Калибровка положительными SDK-эталонами. Отсутствующие метрики не получают баллы.</summary>
    public static class HandRigQualityCalibration
    {
        public const string Cyborg="Assets/ThirdParty/UltimateXR/Runtime/Prefabs/Avatars/CyborgAvatar_URP.prefab";
        public const string BigHands="Assets/ThirdParty/UltimateXR/Runtime/Prefabs/Avatars/BigHandsAvatar_URP.prefab";
        public const string CommonPose="Assets/ThirdParty/UltimateXR/Runtime/Art/Avatars/Cyborg/HandPoses/Default.asset";
        static readonly Dictionary<string,double> NoiseFloors=new Dictionary<string,double> {
            {"M1",3},{"M2",3},{"M3",2},{"M4",.15},{"M5",2},{"M7",2},{"M9",2},{"M10",3}
        };

        public static HandRigCalibration CalibrateSdk(float eyeHeightMeters=1.72f)
        {
            var calibration=new HandRigCalibration {CreatedUtc=DateTime.UtcNow.ToString("O"),EyeHeightMeters=eyeHeightMeters,Status="incomplete"};
            var reports=new List<string>();
            try
            {
                var validation=new HandRigQualityRequest{EyeHeightMeters=eyeHeightMeters};HandRigQualityAnalyzer.Validate(validation);
                string directory=HandRigQualityAnalyzer.OutputDirectory(null);Directory.CreateDirectory(directory);
                calibration.Path=System.IO.Path.Combine(directory,"sdk-calibration.json");
                calibration.CommonPose=HandAssetIdentity.Capture(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(CommonPose));
                foreach(string path in new[]{Cyborg,BigHands})
                {
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if(!prefab)throw new ArgumentException("Нет SDK-эталона: "+path);
                    calibration.SdkAssets.Add(HandAssetIdentity.Capture(prefab));
                    foreach(UxrHandSide side in new[]{UxrHandSide.Left,UxrHandSide.Right})
                    {
                        var report=HandRigQualityAnalyzer.Analyze(new HandRigQualityRequest {AvatarPath=path,ReferenceAvatarPath=Cyborg,
                            Side=side,EyeHeightMeters=eyeHeightMeters,RenderImages=false,
                            OutputDirectory=System.IO.Path.Combine(directory,System.IO.Path.GetFileNameWithoutExtension(path)+"-"+side)});
                        reports.Add(System.IO.Path.Combine(report.OutputDirectory??directory,"report.json"));
                        if(report.Status!="measured_static")throw new InvalidOperationException("Не удалось измерить эталон "+path+" / "+side+": "+report.Error);
                        foreach(var metric in report.Metrics.Where(m=>m.Status=="measured"&&m.Value.HasValue&&NoiseFloors.ContainsKey(m.Id)))
                        {
                            var band=calibration.Bands.FirstOrDefault(b=>b.Side==side.ToString()&&b.Metric==metric.Id&&b.Subject==metric.Subject&&b.Unit==metric.Unit);
                            if(band==null) {band=new HandRigCalibrationBand{Side=side.ToString(),Metric=metric.Id,Subject=metric.Subject,Unit=metric.Unit};calibration.Bands.Add(band);}
                            band.Samples++;band.SdkMaximum=Math.Max(band.SdkMaximum,metric.Id=="M9"?Math.Max(0,metric.Value.Value):Math.Abs(metric.Value.Value));
                            // Положительные эталоны получают 100; стартовый допуск защищает от численного шума.
                            band.GoodLimit=Math.Max(NoiseFloors[metric.Id],band.SdkMaximum*1.25);
                            band.BadLimit=band.GoodLimit*3;
                        }
                    }
                }
                if(calibration.Bands.Count==0)throw new InvalidOperationException("SDK-эталоны не дали применимых критериев.");
                calibration.Status="calibrated_static_sdk";
            }
            catch(Exception e) {calibration.Error=e.Message;}
            calibration.Reports=reports.ToArray();
            if(calibration.Path!=null)File.WriteAllText(calibration.Path,JsonConvert.SerializeObject(calibration,HandRigJsonConverter.Settings));
            return calibration;
        }

        internal static void Apply(HandRigQualityReport report)
        {
            if(string.IsNullOrEmpty(report.Settings.CalibrationPath))return;
            try
            {
                var calibration=JsonConvert.DeserializeObject<HandRigCalibration>(File.ReadAllText(report.Settings.CalibrationPath),HandRigJsonConverter.Settings);
                if(calibration==null||calibration.Status!="calibrated_static_sdk"||calibration.AlgorithmVersion!=report.AlgorithmVersion)
                    throw new ArgumentException("Несовместимая или неполная калибровка.");
                if(Mathf.Abs(calibration.EyeHeightMeters-report.Settings.EyeHeightMeters)>1e-5f || report.Settings.Frame!="wrist" || !report.Settings.CommonSdkPose || report.Settings.Blend!=0)
                    throw new ArgumentException("Калибровка относится к общей SDK Default, Blend=0, рамке запястья и записанной высоте глаз.");
                var pose=calibration.CommonPose;
                if(pose==null || AssetDatabase.AssetPathToGUID(CommonPose)!=pose.Guid || AssetDatabase.GetAssetDependencyHash(CommonPose).ToString()!=pose.DependencyHash)
                    throw new ArgumentException("Общая SDK-поза изменилась: повторите калибровку.");
                foreach(var asset in calibration.SdkAssets)
                    if(AssetDatabase.AssetPathToGUID(asset.Path)!=asset.Guid || AssetDatabase.GetAssetDependencyHash(asset.Path).ToString()!=asset.DependencyHash)
                        throw new ArgumentException("SDK-источник изменился: повторите калибровку.");
                var canonical=calibration.SdkAssets.FirstOrDefault(a=>a.Path==Cyborg);
                if(canonical==null||report.Reference?.Avatar==null||report.Reference.Avatar.Guid!=canonical.Guid||
                    report.Reference.Avatar.LocalFileId!=canonical.LocalFileId||report.Reference.Avatar.DependencyHash!=canonical.DependencyHash)
                    throw new ArgumentException("Калибровка сравнительных критериев использует точный канонический SDK Cyborg; вариант с другим источником не считается эквивалентным.");
                var scores=new List<(string metric,double score)>();
                foreach(var metric in report.Metrics.Where(m=>m.Status=="measured"&&m.Value.HasValue))
                {
                    var band=calibration.Bands.FirstOrDefault(b=>b.Side==report.Target.Side&&b.Metric==metric.Id&&b.Subject==metric.Subject&&b.Unit==metric.Unit);
                    if(band==null)continue;
                    // M9 штрафует только излишек толщины, прочие критерии — модуль отклонения.
                    double value=metric.Id=="M9"?Math.Max(0,metric.Value.Value):Math.Abs(metric.Value.Value);
                    scores.Add((metric.Id,Score(value,band.GoodLimit,band.BadLimit)));
                }
                foreach(var group in scores.GroupBy(s=>s.metric))
                    report.CriterionScores.Add(new HandRigCriterionScore {Metric=group.Key,Score=group.Average(s=>s.score),Samples=group.Count()});
                report.ScoredMetrics=scores.Count;
                if(report.CriterionScores.Count>0)report.StaticScore=report.CriterionScores.Average(s=>s.Score);
                report.CalibrationStatus=report.StaticScore.HasValue?"calibrated_static_sdk":"no_covered_metrics";
                report.Limitations.Add("Шкала 0–100: среднее по семействам применимых метрик; SDK-envelope +25%, стартовые допуски, плохая граница ×3. M2/M3/M5/M6/M13, неподтверждённая манжета и динамика в балл не входят.");
            }
            catch(Exception e) {report.CalibrationStatus="invalid_calibration: "+e.Message;}
        }

        public static double Score(double error, double goodLimit, double badLimit)
        {
            if(double.IsNaN(error)||double.IsInfinity(error)||double.IsNaN(goodLimit)||double.IsInfinity(goodLimit)||double.IsNaN(badLimit)||double.IsInfinity(badLimit)||goodLimit<0||badLimit<=goodLimit)throw new ArgumentException("Некорректные границы шкалы.");
            if(error<=goodLimit)return 100;
            return Math.Max(0,100*(badLimit-error)/(badLimit-goodLimit));
        }
    }
}
