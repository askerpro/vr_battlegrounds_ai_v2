using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UltimateXR.Core;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Запрос предпросмотра: исходный запрос анализа и предложенные правки. Подсказка добавляется правкой "hint".</summary>
    public sealed class HandPoseEditPreviewRequest
    {
        public HandPoseFitRequest Request = new HandPoseFitRequest();
        public List<FitPoseEdit> Edits = new List<FitPoseEdit>();
        public bool PreviewHint = true;
    }

    public sealed class FitFingerDelta
    {
        public string Finger, SeverityBefore, SeverityAfter;
        public float CurlDeg;
        public double BeforeMm, AfterMm, DeltaMm;
        public double? TargetMm, HintExpectedMm;
        // |после − цель| < |до − цель|: правка сдвинула подушечку к медиане эталона.
        public bool? TowardTarget;
    }

    public sealed class FitEditPreviewResult
    {
        public string Name, Status, Error, Overall, Sheet, CompareSheet, Json;
        public FitPoseEdit Edit;
        public FitGripVerdict Verdict;
        public List<FitFingerDelta> Fingers = new List<FitFingerDelta>();
        public double? PalmMinBeforeMm, PalmMinAfterMm;
        public double ElapsedMs;
    }

    public sealed class HandPoseEditPreviewReport
    {
        public string Status, Error, OutputDirectory, BaseReportJson, BaseSheet, Json;
        public string Meaning = "правки применены к временному клону preview-сцены и откатаны; позы/префабы не сохраняются";
        public string BaseOverall;
        public string SourceHashBefore, SourceHashAfter;
        public bool SourceUnchanged, SourceDirtyBefore, SourceDirtyAfter;
        public List<FitEditPreviewResult> Edits = new List<FitEditPreviewResult>();
        public double ElapsedMs;
    }

    /// <summary>
    /// API для LLM: правка → переизмерение → лист «до/после» для человека. Правка — curl пальцев (как SDK CurlFinger),
    /// сдвиг и поворот кисти; применяется только к клону HandPoseFitSnapshot, исходные ассеты проверяются хешем.
    /// </summary>
    public static class HandPoseEditPreview
    {
        public static string RunJson(string json)
        {
            var request = JsonConvert.DeserializeObject<HandPoseEditPreviewRequest>(json, FitUnityJsonConverter.Settings);
            return JsonConvert.SerializeObject(Run(request), FitUnityJsonConverter.Settings);
        }

        public static HandPoseEditPreviewReport Run(HandPoseEditPreviewRequest request)
        {
            var watch = Stopwatch.StartNew();var result = new HandPoseEditPreviewReport {Status = "failed"};
            try {
                if (request?.Request == null) throw new ArgumentException("Не задан Request.");
                var r = request.Request.Copy();r.ApplyGripRules = true;
                var report = HandPoseFitAnalyzer.Analyze(r, (snapshot, baseReport) => {
                    result.SourceHashBefore = SourceHash(baseReport, out bool dirtyBefore);result.SourceDirtyBefore = dirtyBefore;
                    Preview(snapshot, baseReport, request, result);
                });
                result.OutputDirectory = report.OutputDirectory;result.BaseReportJson = report.JsonPath;result.BaseSheet = report.ReviewSheet;result.BaseOverall = report.GripRules?.Overall;
                if (!string.IsNullOrEmpty(report.Error)) throw new InvalidOperationException(report.Status + ": " + report.Error);
                result.SourceHashAfter = SourceHash(report, out bool dirtyAfter);result.SourceDirtyAfter = dirtyAfter;
                result.SourceUnchanged = result.SourceHashBefore != null && result.SourceHashBefore == result.SourceHashAfter && !dirtyAfter;
                result.Status = result.Edits.All(e => e.Status == "previewed") ? "previewed" : "partial";
            } catch (Exception e) {result.Error = e.ToString();}
            result.ElapsedMs = watch.Elapsed.TotalMilliseconds;
            if (!string.IsNullOrEmpty(result.OutputDirectory)) {
                result.Json = Path.Combine(result.OutputDirectory, "edit-preview.json");
                File.WriteAllText(result.Json, JsonConvert.SerializeObject(result, FitUnityJsonConverter.Settings), new UTF8Encoding(false));
            }
            return result;
        }

        static void Preview(HandPoseFitSnapshot snapshot, HandPoseFitReport baseReport, HandPoseEditPreviewRequest request, HandPoseEditPreviewReport result)
        {
            var r = baseReport.Settings;bool right = r.Side == UxrHandSide.Right;
            if (baseReport.ContactMetrics == null) throw new InvalidOperationException("Нет метрик контакта исходной позы.");
            var axes = FitHandAxes.From(snapshot.Hand, baseReport.Joints, right, snapshot.WristPosition);
            var full = new FitSurface(snapshot.ObjectSurface.ToArray());
            FitBandSet bands = null;try {bands = FitBandSet.Load(string.IsNullOrEmpty(r.ContactBandsPath) ? null : r.ContactBandsPath);} catch {}
            var edits = (request.Edits ?? new List<FitPoseEdit>()).Where(e => e != null).ToList();
            if (request.PreviewHint && baseReport.CorrectionHints?.Status == "computed") {var hint = baseReport.CorrectionHints.ToEdit("hint");if (!hint.IsEmpty) edits.Add(hint);}
            var names = new HashSet<string>();
            string title = HandPoseReviewSheet.Title(baseReport);
            var before = new FitReviewState {Label = "BEFORE", Hand = snapshot.Hand, Metrics = baseReport.ContactMetrics, Verdict = baseReport.GripRules};
            foreach (var edit in edits) {
                var watch = Stopwatch.StartNew();
                string name = Safe(string.IsNullOrEmpty(edit.Name) ? "edit" : edit.Name);int n = 2;while (!names.Add(name)) name = Safe(edit.Name) + "-" + n++;
                var item = new FitEditPreviewResult {Name = name, Edit = edit, Status = "failed"};result.Edits.Add(item);
                try {
                    var edited = snapshot.CaptureEdited(edit, axes);
                    var joints = edited.Joints.Where(j => j.Segment == "proximal").GroupBy(j => j.Zone).ToDictionary(g => g.Key, g => g.First().PositionMeters);
                    Vector3? lateral = joints.TryGetValue("index", out var index) && joints.TryGetValue("little", out var little) ? index - little : (Vector3?)null;
                    var metrics = HandPoseContactMetrics.Measure(edited.Hand, edited.Frames, full, r, right, lateral);
                    var verdict = baseReport.GripRules != null && bands != null ? HandPoseGripRules.Evaluate(metrics, bands, baseReport.GripRules.GripType, baseReport.GripRules.GripTypeSource, r) : null;
                    item.Verdict = verdict;item.Overall = verdict?.Overall;
                    foreach (var b in baseReport.ContactMetrics.Fingers) {
                        var a = metrics.Fingers.FirstOrDefault(x => x.Finger == b.Finger);var vb = baseReport.GripRules?.Fingers.FirstOrDefault(x => x.Finger == b.Finger);
                        var hint = baseReport.CorrectionHints?.Fingers.FirstOrDefault(x => x.Finger == b.Finger && x.Severity != null);
                        float curl = edit.FingerCurlDeg != null && edit.FingerCurlDeg.TryGetValue(b.Finger, out var c) ? c : 0;
                        var d = new FitFingerDelta {Finger = b.Finger, CurlDeg = curl, BeforeMm = b.PadSignedDistanceMm, AfterMm = a?.PadSignedDistanceMm ?? double.NaN,
                            SeverityBefore = vb?.Severity, SeverityAfter = verdict?.Fingers.FirstOrDefault(x => x.Finger == b.Finger)?.Severity, TargetMm = vb?.PadBandMedian,
                            HintExpectedMm = name.StartsWith("hint", StringComparison.Ordinal) && hint != null ? hint.ExpectedMm : (double?)null};
                        d.DeltaMm = d.AfterMm - d.BeforeMm;
                        if (d.TargetMm.HasValue && Mathf.Abs(curl) > 1e-3f) d.TowardTarget = Math.Abs(d.AfterMm - d.TargetMm.Value) < Math.Abs(d.BeforeMm - d.TargetMm.Value);
                        item.Fingers.Add(d);
                    }
                    item.PalmMinBeforeMm = baseReport.ContactMetrics.Segments.FirstOrDefault(s => s.Segment == "palm_wrist")?.MinSignedMm;
                    item.PalmMinAfterMm = metrics.Segments.FirstOrDefault(s => s.Segment == "palm_wrist")?.MinSignedMm;
                    if (r.RenderReviewSheet && !string.IsNullOrEmpty(baseReport.OutputDirectory)) {
                        var after = new FitReviewState {Label = "AFTER " + name, Hand = edited.Hand, Metrics = metrics, Verdict = verdict};
                        string describe = "EDIT " + name + ": CURL " + string.Join(" ", (edit.FingerCurlDeg ?? new Dictionary<string, float>()).Select(p => p.Key + (p.Value >= 0 ? "+" : "") + p.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))) + "  TEMP COPY, POSE NOT SAVED";
                        item.Sheet = HandPoseReviewSheet.Render(snapshot, full, snapshot.ObjectSurface, axes, after, HandPoseReviewSheet.Header(title + " AFTER " + name, verdict, describe), Path.Combine(baseReport.OutputDirectory, "review-sheet-" + name + ".png"), r.ReviewPanelSize);
                        item.CompareSheet = HandPoseReviewSheet.RenderCompare(snapshot, full, snapshot.ObjectSurface, axes, before, after, edit,
                            HandPoseReviewSheet.Header(title + "  TOP BEFORE / BOTTOM AFTER " + name, verdict, describe), Path.Combine(baseReport.OutputDirectory, "review-compare-" + name + ".png"), Math.Min(r.ReviewPanelSize, 400));
                        item.Json = Path.Combine(baseReport.OutputDirectory, "review-" + name + ".json");
                        var extra = new JObject {["edit"] = name, ["before"] = "review.json", ["compareSheet"] = Path.GetFileName(item.CompareSheet),
                            ["deltas"] = new JArray(item.Fingers.Select(x => new JObject {["finger"] = x.Finger, ["curlDeg"] = x.CurlDeg, ["beforeMm"] = Math.Round(x.BeforeMm, 1), ["afterMm"] = Math.Round(x.AfterMm, 1), ["towardTarget"] = x.TowardTarget}))};
                        File.WriteAllText(item.Json, HandPoseReviewSheet.SummaryJson(title + " after " + name, metrics, verdict, null, Path.GetFileName(item.Sheet), extra), new UTF8Encoding(false));
                    }
                    item.Status = "previewed";
                } catch (Exception e) {item.Error = e.Message;}
                item.ElapsedMs = watch.Elapsed.TotalMilliseconds;
            }
        }

        static string Safe(string name) => new string((name ?? "edit").Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? char.ToLowerInvariant(ch) : '-').ToArray());

        /// <summary>Хеш исходных ассетов: сериализация позы, локальные TRS всех Transform префаба аватара; dirty-флаги.</summary>
        public static string SourceHash(HandPoseFitReport report, out bool dirty)
        {
            dirty = false;var sb = new StringBuilder();
            var pose = string.IsNullOrEmpty(report.Pose?.Path) ? null : AssetDatabase.LoadAllAssetsAtPath(report.Pose.Path).OfType<UxrHandPoseAsset>().FirstOrDefault();
            if (pose) {sb.Append(EditorJsonUtility.ToJson(pose, false));dirty |= EditorUtility.IsDirty(pose);}
            var avatar = string.IsNullOrEmpty(report.Avatar?.Path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(report.Avatar.Path);
            if (avatar) {
                dirty |= EditorUtility.IsDirty(avatar);
                foreach (var t in avatar.GetComponentsInChildren<Transform>(true)) sb.Append(t.name).Append(t.localPosition.ToString("R")).Append(t.localRotation.ToString("R")).Append(t.localScale.ToString("R"));
            }
            if (sb.Length == 0) return null;
            using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())).Select(b => b.ToString("x2")));
        }
    }
}
