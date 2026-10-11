using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Состояние кисти для листа ревью: геометрия, метрики фазы 1, вердикт правил и (необязательно) подсказки.</summary>
    public sealed class FitReviewState
    {
        public string Label;
        public IReadOnlyList<FitTriangle> Hand;
        public FitContactMetrics Metrics;
        public FitGripVerdict Verdict;
        public FitCorrectionHints Hints;
    }

    /// <summary>
    /// Компактный лист ревью для LLM и человека: один PNG на кейс — ладонь (рентген), вид от глаз, сторона большого
    /// пальца (рентген), тыл; кадр по кисти, линейка 20 мм, подписи пальцев (мм со знаком и severity), легенда.
    /// Сравнение «до/после» — две строки панелей с одинаковым кадром. Источник — те же треугольники, что в измерениях.
    /// </summary>
    public static class HandPoseReviewSheet
    {
        static readonly Color Background = new Color(.08f, .10f, .13f), Band = new Color(.04f, .05f, .07f);
        static readonly Color Inside = new Color(1, .12f, .1f), Touch = new Color(.15f, .95f, .3f), GapNear = new Color(.55f, .8f, 1f), GapFar = new Color(.1f, .2f, .8f), Far = new Color(.86f, .66f, .5f);
        static readonly Color ObjectColor = new Color(.6f, .62f, .66f);
        public const float FarMm = 20, MarginMeters = .025f, ScaleBarMm = 20;

        public static Color SeverityColor(string severity) => severity == "gross" ? new Color(1, .25f, .2f) : severity == "notable" ? new Color(1, .65f, .1f) : new Color(.35f, .95f, .45f);

        struct View {public string Caption;public Vector3 Forward, Up;public bool Solid;}

        // Ракурсы как в audit-d3536425-03: palmar −N, eye F+N (над запястьем со стороны предплечья, 45°), thumb −L, back +N.
        static View[] Views(FitHandAxes a) => new[] {
            new View {Caption = "PALM X-RAY", Forward = -a.Palmar, Up = a.Fingers},
            new View {Caption = "EYE SOLID", Forward = (a.Fingers + a.Palmar).normalized, Up = (a.Fingers - a.Palmar).normalized, Solid = true},
            new View {Caption = "THUMB SIDE X-RAY", Forward = -a.Radial, Up = a.Fingers},
            new View {Caption = "BACK SOLID", Forward = a.Palmar, Up = a.Fingers, Solid = true}};

        public static Color DistanceColor(double signedMm, double windowMm)
        {
            if (signedMm < 0 && -signedMm > windowMm) return Inside;
            if (Math.Abs(signedMm) <= windowMm) return signedMm < 0 ? Color.Lerp(Touch, Inside, .35f) : Touch;
            if (signedMm <= FarMm) return Color.Lerp(GapNear, GapFar, Mathf.Clamp01((float)((signedMm - windowMm) / (FarMm - windowMm))));
            return Far;
        }

        static Color[] Colors(IReadOnlyList<FitTriangle> hand, FitSurface surface, double windowMm)
        {
            var colors = new Color[hand.Count];
            for (int i = 0; i < hand.Count; i++) {var t = hand[i];colors[i] = DistanceColor(HandPoseContactMetrics.SignedMm(surface, (t.A + t.B + t.C) / 3), windowMm);}
            return colors;
        }

        /// <summary>Лист одного состояния. Возвращает путь PNG.</summary>
        public static string Render(HandPoseFitSnapshot host, FitSurface surface, IReadOnlyList<FitTriangle> objectTriangles, FitHandAxes axes, FitReviewState state, IList<string> header, string path, int panel)
        {
            var lines = new List<(string, Color)>();
            foreach (var f in state.Metrics?.Fingers ?? new List<FitFingerContact>()) lines.Add(FingerLine(f, state.Verdict));
            if (state.Verdict?.Palm != null) lines.Add(PalmLine(state.Metrics, state.Verdict));
            string hint = HintLine(state.Hints);if (hint != null) lines.Add((hint, new Color(.8f, .85f, 1f)));
            return Compose(host, surface, objectTriangles, axes, new[] {state}, header, lines, path, panel);
        }

        /// <summary>Сравнение «до/после» с одинаковым кадром по объединению кистей.</summary>
        public static string RenderCompare(HandPoseFitSnapshot host, FitSurface surface, IReadOnlyList<FitTriangle> objectTriangles, FitHandAxes axes, FitReviewState before, FitReviewState after, FitPoseEdit edit, IList<string> header, string path, int panel)
        {
            var lines = new List<(string, Color)>();
            foreach (var b in before.Metrics?.Fingers ?? new List<FitFingerContact>()) {
                var a = after.Metrics?.Fingers.FirstOrDefault(x => x.Finger == b.Finger);
                var vb = before.Verdict?.Fingers.FirstOrDefault(x => x.Finger == b.Finger);var va = after.Verdict?.Fingers.FirstOrDefault(x => x.Finger == b.Finger);
                float curl = edit?.FingerCurlDeg != null && edit.FingerCurlDeg.TryGetValue(b.Finger, out var c) ? c : 0;
                string text = $"{b.Finger.ToUpperInvariant(),-6} PAD {Mm(b.PadSignedDistanceMm)} -> {(a != null ? Mm(a.PadSignedDistanceMm) : "--")} MM" +
                    (vb?.PadBandMedian != null ? $"  MED {Mm(vb.PadBandMedian.Value)}" : "") + $"  {vb?.Severity ?? "-"} -> {va?.Severity ?? "-"}  CURL {Sign(curl)} DEG";
                lines.Add((text.ToUpperInvariant(), SeverityColor(va?.Severity)));
            }
            if (edit != null && (edit.HandShiftMm != Vector3.zero || edit.HandRotationDeg != Vector3.zero))
                lines.Add(($"HAND SHIFT MM X{Sign(edit.HandShiftMm.x)} Y{Sign(edit.HandShiftMm.y)} Z{Sign(edit.HandShiftMm.z)}  ROT DEG X{Sign(edit.HandRotationDeg.x)} Y{Sign(edit.HandRotationDeg.y)} Z{Sign(edit.HandRotationDeg.z)}  (X RADIAL Y FINGERS Z PALMAR)", new Color(.8f, .85f, 1f)));
            return Compose(host, surface, objectTriangles, axes, new[] {before, after}, header, lines, path, panel);
        }

        static string Mm(double v) => double.IsNaN(v) ? "--" : (v >= 0 ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture);
        static string Sign(float v) => (v >= 0 ? "+" : "") + v.ToString("0.#", CultureInfo.InvariantCulture);
        static string Short(string finger) => finger == "little" ? "L" : finger.Substring(0, 1).ToUpperInvariant();

        static (string, Color) FingerLine(FitFingerContact f, FitGripVerdict verdict)
        {
            var v = verdict?.Fingers.FirstOrDefault(x => x.Finger == f.Finger);
            string band = v?.PadBandLo != null ? $" BAND [{Mm(v.PadBandLo.Value)} {Mm(v.PadBandHi.Value)}] MED {Mm(v.PadBandMedian.Value)}" : "";
            string severity = v == null ? "NO RULES" : v.Severity == "ok" ? "OK" : $"{v.Severity} {v.Direction} {v.MaxExcessMm.ToString("0.0", CultureInfo.InvariantCulture)} MM OUT";
            string worst = v != null && v.Severity != "ok" ? " (" + v.Flags.OrderByDescending(x => x.ExcessMm).First().Metric + ")" : "";
            return ($"{f.Finger,-6} PAD {Mm(f.Status == "measured" ? f.PadSignedDistanceMm : double.NaN)} MM{band}  DEPTH {f.FingerPenetrationDepthMm.ToString("0.0", CultureInfo.InvariantCulture)}  -> {severity}{worst}".ToUpperInvariant(), SeverityColor(v?.Severity));
        }

        static (string, Color) PalmLine(FitContactMetrics m, FitGripVerdict v)
        {
            var palm = m.Segments.FirstOrDefault(s => s.Segment == "palm_wrist");
            string flags = v.HandFlags.Count > 0 ? "  HAND: " + string.Join(" ", v.HandFlags) : "";
            return ($"PALM   MIN {Mm(palm?.MinSignedMm ?? double.NaN)} MM  DEPTH {(palm?.PenetrationDepthMm ?? 0).ToString("0.0", CultureInfo.InvariantCulture)}  -> {v.Palm.Severity} {(v.Palm.Severity == "ok" ? "" : v.Palm.Direction)}{flags}".ToUpperInvariant(),
                SeverityColor(v.HandFlags.Count > 0 ? "gross" : v.Palm.Severity));
        }

        static string HintLine(FitCorrectionHints hints)
        {
            if (hints == null || hints.Status != "computed") return null;
            var parts = hints.Fingers.Where(f => f.Recommended).Select(f => $"{Short(f.Finger)}{Sign(f.CurlDeg)}{(f.DepthLimited ? "*" : "")}").ToList();
            if (parts.Count == 0 && (hints.Hand == null || !hints.Hand.Recommended)) return "HINT: NONE (ALL FINGERS OK)";
            string hand = hints.Hand != null && hints.Hand.Recommended ? $"  PALM SHIFT Z {Sign(hints.Hand.ShiftMm.z)} MM" : "";
            return ("HINT CURL DEG " + string.Join(" ", parts) + hand + "  (* DEPTH LIMITED; START POINT, NOT A FIX)").ToUpperInvariant();
        }

        static string Compose(HandPoseFitSnapshot host, FitSurface surface, IReadOnlyList<FitTriangle> objectTriangles, FitHandAxes axes, FitReviewState[] rows, IList<string> header, List<(string text, Color color)> footer, string path, int panel)
        {
            Shader shader = Shader.Find("Hidden/VRBattlegrounds/HandPoseFit");
            if (!shader) throw new InvalidOperationException("Не импортирован shader анализатора.");
            var views = Views(axes);var resources = new List<Object>();var objects = new List<GameObject>();
            var panels = new Texture2D[rows.Length, views.Length];
            try {
                Material solid = HandPoseFitRenderer.Material(shader, false, false), ghost = HandPoseFitRenderer.Material(shader, true, false), line = HandPoseFitRenderer.Material(shader, true, true);
                resources.Add(solid);resources.Add(ghost);resources.Add(line);
                Mesh item = HandPoseFitRenderer.Mesh(objectTriangles, _ => ObjectColor), wire = HandPoseFitRenderer.Wire(objectTriangles, new Color(.85f, .86f, .89f, .18f));
                resources.Add(item);resources.Add(wire);
                GameObject itemGo = HandPoseFitRenderer.Add(host, item, solid), wireGo = HandPoseFitRenderer.Add(host, wire, line);objects.Add(itemGo);objects.Add(wireGo);
                var hands = new List<GameObject>();
                foreach (var row in rows) {
                    var colors = Colors(row.Hand, surface, row.Metrics?.WindowMm ?? 2);int index = 0;
                    var mesh = HandPoseFitRenderer.Mesh(row.Hand, _ => colors[index++]);resources.Add(mesh);
                    var go = HandPoseFitRenderer.Add(host, mesh, solid);objects.Add(go);hands.Add(go);
                }
                var all = rows.SelectMany(r => r.Hand).ToList();
                var camera = host.Preview.camera;camera.orthographic = true;camera.nearClipPlane = .001f;camera.farClipPlane = 20;
                camera.clearFlags = CameraClearFlags.SolidColor;camera.backgroundColor = Background;camera.cullingMask = -1;
                for (int v = 0; v < views.Length; v++) {
                    var view = views[v];Vector3 forward = view.Forward.normalized, up = (view.Up - forward * Vector3.Dot(view.Up, forward)).normalized, right = Vector3.Cross(up, forward).normalized;
                    float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                    foreach (var t in all) foreach (var p in new[] {t.A, t.B, t.C}) {
                        float x = Vector3.Dot(p, right), y = Vector3.Dot(p, up), z = Vector3.Dot(p, forward);
                        x0 = Mathf.Min(x0, x);x1 = Mathf.Max(x1, x);y0 = Mathf.Min(y0, y);y1 = Mathf.Max(y1, y);z0 = Mathf.Min(z0, z);z1 = Mathf.Max(z1, z);
                    }
                    float size = Mathf.Max(x1 - x0, y1 - y0) + 2 * MarginMeters;
                    Vector3 center = right * ((x0 + x1) / 2) + up * ((y0 + y1) / 2) + forward * ((z0 + z1) / 2);
                    camera.orthographicSize = size / 2;camera.transform.position = center - forward * 2;camera.transform.rotation = Quaternion.LookRotation(forward, up);
                    itemGo.GetComponent<MeshRenderer>().sharedMaterial = view.Solid ? solid : ghost;wireGo.SetActive(!view.Solid);
                    for (int r = 0; r < rows.Length; r++) {
                        for (int h = 0; h < hands.Count; h++) hands[h].SetActive(h == r);
                        host.Preview.BeginStaticPreview(new Rect(0, 0, panel, panel));host.Preview.Render(true);
                        var texture = host.Preview.EndStaticPreview();panels[r, v] = texture;
                        Annotate(texture, camera, center, size, view.Caption + (rows.Length > 1 ? "  " + rows[r].Label : ""), rows[r]);
                    }
                }
                return Write(panels, header, footer, path);
            } finally {
                foreach (var go in objects) Object.DestroyImmediate(go);foreach (var o in resources) Object.DestroyImmediate(o);
                foreach (var t in panels) if (t) Object.DestroyImmediate(t);
            }
        }

        static void Fill(Texture2D t, int x0, int y0, int x1, int y1, Color c)
        {
            for (int y = Math.Max(0, y0); y < Math.Min(t.height, y1); y++) for (int x = Math.Max(0, x0); x < Math.Min(t.width, x1); x++) t.SetPixel(x, y, c);
        }

        static void Annotate(Texture2D t, Camera camera, Vector3 center, float size, string caption, FitReviewState state)
        {
            int w = t.width, scale = Math.Max(1, Mathf.RoundToInt(w / 256f)), line = 9 * scale;
            Fill(t, 0, t.height - line - 4, w, t.height, Band);FitRasterLabels.Text(t, 6, t.height - 3, caption, scale, Color.white);
            // Линейка 20 мм внизу слева.
            int bar = Mathf.RoundToInt(w * ScaleBarMm * .001f / size);
            Fill(t, 8, 8, 8 + bar, 8 + 2 * scale, Color.white);FitRasterLabels.Text(t, 10 + bar + 4, 10 + 7 * scale, ((int)ScaleBarMm) + " MM", scale, Color.white);
            if (state.Metrics == null) return;
            Vector3 right = camera.transform.right, up = camera.transform.up;
            var placed = new List<RectInt>();
            var measured = state.Metrics.Fingers.Where(x => x.Status == "measured").OrderByDescending(x => Vector3.Dot(x.PadPointMeters - center, up)).ToList();
            int X(Vector3 p) => Mathf.RoundToInt((Vector3.Dot(p - center, right) / size + .5f) * w);
            int Y(Vector3 p) => Mathf.RoundToInt((Vector3.Dot(p - center, up) / size + .5f) * t.height);
            // Сначала все точки подушечек, затем подписи: подпись не закрывается чужой точкой.
            foreach (var f in measured) {
                var v = state.Verdict?.Fingers.FirstOrDefault(x => x.Finger == f.Finger);int px = X(f.PadPointMeters), py = Y(f.PadPointMeters);
                Fill(t, px - 2 * scale - 1, py - 2 * scale - 1, px + 2 * scale + 2, py + 2 * scale + 2, Band);Fill(t, px - 2 * scale, py - 2 * scale, px + 2 * scale + 1, py + 2 * scale + 1, SeverityColor(v?.Severity));
                placed.Add(new RectInt(px - 2 * scale - 1, py - 2 * scale - 1, 4 * scale + 3, 4 * scale + 3));
            }
            foreach (var f in measured) {
                var v = state.Verdict?.Fingers.FirstOrDefault(x => x.Finger == f.Finger);Color color = SeverityColor(v?.Severity);
                int px = X(f.PadPointMeters), py = Y(f.PadPointMeters);
                string text = Short(f.Finger) + " " + Mm(f.PadSignedDistanceMm) + (v != null && v.Severity != "ok" ? " " + (v.Severity == "gross" ? "G" : "N") : "");
                int tw = FitRasterLabels.Width(text, scale) + 4, th = 7 * scale + 4;
                int tx = px + 4 * scale + tw < w ? px + 4 * scale : px - 4 * scale - tw, ty = Mathf.Clamp(py + th / 2, th + 2, t.height - line - 6);
                var rect = new RectInt(tx, ty - th, tw, th);
                for (int guard = 0; guard < 12 && placed.Any(o => o.Overlaps(rect)); guard++) {ty -= th + 2;rect = new RectInt(tx, ty - th, tw, th);}
                placed.Add(rect);Fill(t, rect.xMin, rect.yMin, rect.xMax, rect.yMax, Band);FitRasterLabels.Text(t, rect.xMin + 2, rect.yMax - 2, text, scale, color);
            }
            t.Apply();
        }

        static string Write(Texture2D[,] panels, IList<string> header, List<(string text, Color color)> footer, string path)
        {
            int rows = panels.GetLength(0), cols = panels.GetLength(1), size = panels[0, 0].width;
            int width = cols * size, scale = width >= 1400 ? 2 : Math.Max(1, width / 600), line = 9 * scale + 4;
            var legend = new List<(Color, string)> {(Inside, "INSIDE"), (Touch, "TOUCH +-2MM"), (GapNear, "GAP 2MM"), (GapFar, "GAP 20MM"), (Far, "HAND FAR >20MM"), (ObjectColor, "OBJECT")};
            int headerHeight = header.Count * line + 6, footerHeight = (footer.Count + 2) * line + 8;
            int height = headerHeight + rows * size + footerHeight;
            var sheet = new Texture2D(width, height, TextureFormat.RGB24, false);
            try {
                var pixels = Enumerable.Repeat(Band, width * height).ToArray();sheet.SetPixels(pixels);
                for (int i = 0; i < header.Count; i++) FitRasterLabels.Text(sheet, 8, height - 4 - i * line, header[i], scale, i == 0 ? Color.white : new Color(.85f, .88f, .95f));
                for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) {
                    var p = panels[r, c];sheet.SetPixels(c * size, height - headerHeight - (r + 1) * size, p.width, p.height, p.GetPixels());
                }
                for (int i = 0; i < footer.Count; i++) FitRasterLabels.Text(sheet, 8, footerHeight - 6 - i * line, footer[i].text, scale, footer[i].color);
                int y = footerHeight - 6 - footer.Count * line, x = 8;
                foreach (var (color, text) in legend) {
                    Fill(sheet, x, y - 7 * scale, x + 7 * scale, y, color);FitRasterLabels.Text(sheet, x + 9 * scale, y, text, scale, Color.white);
                    x += 9 * scale + FitRasterLabels.Width(text, scale) + 6 * scale;
                }
                y -= line;x = 8;
                foreach (var (sev, text) in new[] {("ok", "OK"), ("notable", "NOTABLE >=5MM OUT OF SDK BAND (N)"), ("gross", "GROSS >=10MM / MISS (G)")}) {
                    Fill(sheet, x, y - 7 * scale, x + 7 * scale, y, SeverityColor(sev));FitRasterLabels.Text(sheet, x + 9 * scale, y, text, scale, SeverityColor(sev));
                    x += 9 * scale + FitRasterLabels.Width(text, scale) + 6 * scale;
                }
                FitRasterLabels.Text(sheet, x, y, "LABEL = PAD MM SIGNED (- INSIDE)", scale, Color.white);
                sheet.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path, sheet.EncodeToPNG());
                return path;
            } finally {Object.DestroyImmediate(sheet);}
        }

        /// <summary>Заголовок листа: кейс, тип хвата, полосы, итог.</summary>
        public static List<string> Header(string title, FitGripVerdict v, string extra = null)
        {
            var list = new List<string> {title.ToUpperInvariant()};
            if (v == null || v.Status != "evaluated") list.Add(("RULES: " + (v?.Status ?? "NONE")).ToUpperInvariant());
            else {
                list.Add($"TYPE {v.GripType} ({v.GripTypeSource})  BANDS {v.BandsVersion} SDK N={v.BandCases}{(v.LowConfidence ? "  LOW CONFIDENCE" : "")}".ToUpperInvariant());
                list.Add($"RESULT {v.Overall}{(v.HandFlags.Count > 0 ? "  HAND " + string.Join(" ", v.HandFlags) : "")}  NOTABLE>={v.NotableExcessMm:0}MM GROSS>={v.GrossExcessMm:0}MM OUT OF BAND".ToUpperInvariant());
            }
            if (!string.IsNullOrEmpty(extra)) list.Add(extra.ToUpperInvariant());
            return list;
        }

        public static string Title(HandPoseFitReport report)
        {
            var r = report.Settings;if (!string.IsNullOrEmpty(r.ReviewTitle)) return r.ReviewTitle;
            string item = Path.GetFileNameWithoutExtension(report.Object?.Path ?? r.ObjectPath ?? "object");
            string avatar = Path.GetFileNameWithoutExtension(report.Avatar?.Path ?? r.AvatarPath ?? "avatar");
            return $"{avatar} {item}{(string.IsNullOrEmpty(report.GrabbablePath) ? "" : "/" + report.GrabbablePath)} P{r.GrabPoint} {r.Side}";
        }

        /// <summary>JSON-сводка в несколько строк: одно поле или палец — одна строка.</summary>
        public static string SummaryJson(string title, FitContactMetrics m, FitGripVerdict v, FitCorrectionHints hints, string sheet, JObject extra = null)
        {
            var top = new JObject {["case"] = title, ["gripType"] = v?.GripType, ["gripTypeSource"] = v?.GripTypeSource, ["lowConfidence"] = v?.LowConfidence,
                ["bands"] = v == null ? null : $"{v.BandsVersion} n={v.BandCases}", ["overall"] = v?.Overall, ["handFlags"] = new JArray(v?.HandFlags ?? new List<string>()),
                ["thresholdsMm"] = v == null ? null : new JObject {["notable"] = v.NotableExcessMm, ["gross"] = v.GrossExcessMm, ["miss"] = v.MissExcessMm, ["missFingers"] = v.MissFingerCount}};
            var fingers = new JObject();
            foreach (var f in m?.Fingers ?? new List<FitFingerContact>()) {
                var fv = v?.Fingers.FirstOrDefault(x => x.Finger == f.Finger);var h = hints?.Fingers.FirstOrDefault(x => x.Finger == f.Finger);
                var o = new JObject {["padMm"] = f.Status == "measured" ? Math.Round(f.PadSignedDistanceMm, 1) : (JToken)null, ["depthMm"] = Math.Round(f.FingerPenetrationDepthMm, 1),
                    ["band"] = fv?.PadBandLo == null ? null : new JArray(Math.Round(fv.PadBandLo.Value, 1), Math.Round(fv.PadBandHi.Value, 1)), ["median"] = fv?.PadBandMedian == null ? null : (JToken)Math.Round(fv.PadBandMedian.Value, 1),
                    ["severity"] = fv?.Severity, ["direction"] = fv?.Direction, ["excessMm"] = fv == null ? null : (JToken)Math.Round(fv.MaxExcessMm, 1)};
                if (h?.Severity != null) {o["hintCurlDeg"] = h.CurlDeg;o["hintExpectedMm"] = Math.Round(h.ExpectedMm, 1);o["hintRecommended"] = h.Recommended;}
                fingers[f.Finger] = o;
            }
            top["fingers"] = fingers;
            if (v?.Palm != null) {
                var palm = m.Segments.FirstOrDefault(s => s.Segment == "palm_wrist");
                top["palm"] = new JObject {["minMm"] = palm == null ? null : (JToken)Math.Round(palm.MinSignedMm, 1), ["depthMm"] = palm == null ? null : (JToken)Math.Round(palm.PenetrationDepthMm, 1), ["severity"] = v.Palm.Severity, ["direction"] = v.Palm.Direction};
            }
            if (hints != null && hints.Status == "computed") top["hintEdit"] = JObject.FromObject(hints.ToEdit(), JsonSerializer.Create(FitUnityJsonConverter.Settings));
            top["editAxes"] = "HandShiftMm/HandRotationDeg: x radial (index side), y fingers, z palmar; FingerCurlDeg: + flex (SDK CurlFinger weights)";
            top["sheet"] = sheet;
            if (extra != null) foreach (var p in extra.Properties()) top[p.Name] = p.Value;
            var sb = new StringBuilder("{\n");var props = top.Properties().ToList();
            for (int i = 0; i < props.Count; i++) {
                var p = props[i];sb.Append(" \"").Append(p.Name).Append("\": ");
                if (p.Value is JObject inner && (p.Name == "fingers")) {
                    sb.Append("{\n");var ip = inner.Properties().ToList();
                    for (int k = 0; k < ip.Count; k++) sb.Append("  \"").Append(ip[k].Name).Append("\": ").Append(ip[k].Value.ToString(Formatting.None)).Append(k + 1 < ip.Count ? ",\n" : "\n");
                    sb.Append(" }");
                } else sb.Append(p.Value.ToString(Formatting.None));
                sb.Append(i + 1 < props.Count ? ",\n" : "\n");
            }
            return sb.Append("}\n").ToString();
        }
    }
}
