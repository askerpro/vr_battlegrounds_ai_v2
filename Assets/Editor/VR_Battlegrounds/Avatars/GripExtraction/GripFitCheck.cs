using System;
using System.Collections.Generic;
using System.IO;
using UltimateXR.Core;
using VrBattlegrounds.Editor.HandPoseReview;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Hand Pose Fit по сохранённой точке (после Apply): анализатор читает сохранённые префабы, поэтому в Measure/Preview
    /// не вызывается. Возвращает вердикт, лист ревью и подсказки следующей итерации — сдвиг по нормали ладони
    /// (<c>FitCorrectionHints.Hand.ShiftMm.z</c>, «+ — в ладонь») и curl пальцев (<c>ToEdit().FingerCurlDeg</c>).
    /// Автоматический цикл доводки (шаги 2–3 проекта, не более двух итераций) — TODO следующего этапа: здесь только хук.
    /// </summary>
    public static class GripFitCheck
    {
        public static GripFitSummary Run(GripTargetProfile profile, GripExtractionResult point, UxrHandSide side, double appliedNormalOffsetMm, string outDir)
        {
            var summary = new GripFitSummary();
            try
            {
                var request = new HandPoseFitRequest
                {
                    AvatarPath      = profile.RigPrefabPath,
                    ObjectPath      = point.Weapon,
                    GrabPoint       = point.GrabPoint,
                    Side            = side,
                    OutputDirectory = Path.Combine(outDir, "fit", $"{point.Pose}-{side}"),
                    RenderImages    = false,
                    ReviewTitle     = $"{point.Pose} {side} → {profile.Name} ({profile.Anchor})"
                };
                HandPoseFitReport report = HandPoseFitAnalyzer.Analyze(request);
                summary.Status       = report.Status;
                summary.Error        = report.Error;
                summary.Overall      = report.GripRules?.Overall;
                summary.PalmSeverity = report.GripRules?.Palm?.Severity;
                summary.ReportPath   = report.JsonPath;
                summary.ReviewSheet  = report.ReviewSheet;

                FitHandShiftHint shift = report.CorrectionHints?.Hand;
                if (shift != null)
                {
                    summary.SuggestedNormalOffsetMm      = GripMath.Round(shift.ShiftMm.z);
                    summary.SuggestedTotalNormalOffsetMm = GripMath.Round(appliedNormalOffsetMm + shift.ShiftMm.z);
                    summary.NormalOffsetRecommended      = shift.Recommended;
                }

                FitPoseEdit edit = report.CorrectionHints?.ToEdit();
                if (edit != null && edit.FingerCurlDeg.Count > 0)
                {
                    summary.SuggestedCurlDeg = new Dictionary<string, float>(edit.FingerCurlDeg);
                }
            }
            catch (Exception e)
            {
                summary.Status = "error";
                summary.Error  = e.Message;
            }

            return summary;
        }
    }
}
