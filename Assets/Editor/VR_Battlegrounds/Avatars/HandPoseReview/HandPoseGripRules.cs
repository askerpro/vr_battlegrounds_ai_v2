using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Полоса эталона SDK одной метрики: [P5 − m; P95 + m], мм.</summary>
    public sealed class FitBand { public int N; public double P50, Lo, Hi; }

    public sealed class FitBandType
    {
        public int Cases;
        public string[] Grips = Array.Empty<string>();
        public Dictionary<string, FitBand> Metrics = new Dictionary<string, FitBand>();
        public FitBand Get(string key) => Metrics != null && Metrics.TryGetValue(key, out var b) ? b : null;
    }

    /// <summary>Версионируемый набор полос (Calibration/contact-bands-v*.json). Владелец данных — калибровочный прогон.</summary>
    public sealed class FitBandSet
    {
        public const string DefaultPath = "Assets/Editor/VR_Battlegrounds/Avatars/HandPoseReview/Calibration/contact-bands-v1.json";
        public string Version;
        public JObject Source;
        public int LowConfidenceMaxCases = 4;
        public Dictionary<string, FitBandType> Types = new Dictionary<string, FitBandType>();
        [JsonIgnore] public string Path, Sha256;

        public static FitBandSet Load(string path = null)
        {
            path = string.IsNullOrEmpty(path) ? DefaultPath : path;
            byte[] bytes = File.ReadAllBytes(path);
            var set = JsonConvert.DeserializeObject<FitBandSet>(new UTF8Encoding(false).GetString(bytes));
            if (set == null || set.Types == null || set.Types.Count == 0) throw new ArgumentException("Пустой набор полос: " + path);
            set.Path = path;
            using (var sha = SHA256.Create()) set.Sha256 = string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
            return set;
        }
    }

    public sealed class FitBandFlag
    {
        public string Metric, Side, Meaning;
        public double Value, Lo, Hi, ExcessMm;
    }

    public sealed class FitFingerVerdict
    {
        public string Finger, Severity = "ok", Direction = "none";
        public double MaxExcessMm;
        public double? PadMm, PadBandLo, PadBandHi, PadBandMedian;
        public List<FitBandFlag> Flags = new List<FitBandFlag>();
    }

    /// <summary>Вердикт грубых нарушений: ok / notable / gross по выходу за полосу эталона SDK своего типа хвата.</summary>
    public sealed class FitGripVerdict
    {
        public string Status, Reason, BandsVersion, BandsPath, BandsSha256, GripType, GripTypeSource;
        public int BandCases;
        public bool LowConfidence;
        public float NotableExcessMm, GrossExcessMm, MissExcessMm;
        public int MissFingerCount;
        public string Overall = "ok";
        public List<FitFingerVerdict> Fingers = new List<FitFingerVerdict>();
        public FitFingerVerdict Palm;
        public List<string> HandFlags = new List<string>();
        public string Meaning = "severity по наибольшему выходу метрики за полосу эталона SDK: notable ≥ NotableExcessMm, gross ≥ GrossExcessMm; " +
            "hand miss — ≥ MissFingerCount пальцев с зазором подушечки выше полосы на ≥ MissExcessMm; palm_off_object — минимум ладони выше полосы на ≥ GrossExcessMm. " +
            "Правило ищет грубые нарушения, не оценивает качество хвата: палец вне поверхности допустим, финальная оценка — человек по листу ревью.";
    }

    /// <summary>Тип хвата из роли точки и правило грубых нарушений. Чистые функции без сцены.</summary>
    public static class HandPoseGripRules
    {
        public static readonly string[] GripTypes = { "pistol_grip", "foregrip", "pump", "pinch_small", "handle" };
        public static readonly string[] Severities = { "ok", "notable", "gross" };
        // Наибольший габарит «мелкого» свободного предмета (батарейка, лампа, граната, магазин), м.
        public const float SmallObjectMaxMeters = .15f;

        /// <summary>
        /// Роль точки: оружие — точка спуска (pistol_grip), другая точка корневого grabbable (foregrip),
        /// магазин (pinch_small), другой дочерний grabbable оружия (pump). Без оружия: магазин/патрон — pinch_small,
        /// механизм с закреплённым перемещением — handle, мелкий предмет (габарит < 15 см) — pinch_small, крупный — handle.
        /// </summary>
        public static string DetectGripType(UxrGrabbableObject grabbable, int point, out string reason)
        {
            if (!grabbable) {reason = "no_grabbable";return null;}
            var weapon = grabbable.GetComponentInParent<UxrFirearmWeapon>(true);
            bool magazine = grabbable.GetComponent<UxrFirearmMag>() || grabbable.GetComponentInChildren<UxrFirearmAmmoUnit>(true);
            if (weapon) {
                var triggers = TriggerPoints(weapon);
                if (triggers.Any(t => t.grabbable == grabbable && t.point == point)) {reason = "weapon_trigger_point";return "pistol_grip";}
                if (grabbable.GetComponent<UxrFirearmMag>()) {reason = "weapon_magazine";return "pinch_small";}
                if (triggers.Any(t => t.grabbable == grabbable) || weapon.GetComponent<UxrGrabbableObject>() == grabbable) {reason = "weapon_other_point";return "foregrip";}
                reason = "weapon_child_grabbable";return "pump";
            }
            if (magazine) {reason = "magazine_or_ammo";return "pinch_small";}
            // Закреплённый на месте механизм (рычаг, ручка генератора): перемещение Locked, допускается только поворот.
            // Ограничение смещения само по себе не делает предмет ручкой: батарейка SDK — RestrictLocalOffset.
            if (grabbable.TranslationConstraint == UxrTranslationConstraintMode.Locked) {reason = "translation_locked_mechanism";return "handle";}
            float size = RendererExtent(grabbable.transform);
            reason = (size < SmallObjectMaxMeters ? "small_object_" : "large_object_") + (size * 1000).ToString("0", CultureInfo.InvariantCulture) + "mm";
            return size < SmallObjectMaxMeters ? "pinch_small" : "handle";
        }

        static List<(UxrGrabbableObject grabbable, int point)> TriggerPoints(UxrFirearmWeapon weapon)
        {
            var result = new List<(UxrGrabbableObject, int)>();
            // UxrFirearmTrigger — internal-класс SDK: читаем сериализованный список и его свойства отражением.
            var field = typeof(UxrFirearmWeapon).GetField("_triggers", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(weapon) is System.Collections.IEnumerable triggers)
                foreach (object t in triggers) {
                    if (t == null) continue;var type = t.GetType();
                    var grabbable = type.GetProperty("TriggerGrabbable")?.GetValue(t) as UxrGrabbableObject;
                    object index = type.GetProperty("GrabbableGrabPointIndex")?.GetValue(t);
                    if (grabbable && index is int point) result.Add((grabbable, point));
                }
            return result;
        }

        static float RendererExtent(Transform root)
        {
            bool any = false;var bounds = new Bounds();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) {
                Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (!mesh) continue;
                Bounds local = mesh.bounds;var matrix = renderer.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++) {
                    var corner = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) {bounds = new Bounds(corner, Vector3.zero);any = true;} else bounds.Encapsulate(corner);
                }
            }
            return any ? Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) : 0;
        }

        public static string Severity(double excessMm, float notableMm, float grossMm) => excessMm >= grossMm ? "gross" : excessMm >= notableMm ? "notable" : "ok";
        static int Rank(string severity) => Array.IndexOf(Severities, severity);
        static string Max(string a, string b) => Rank(a) >= Rank(b) ? a : b;

        /// <summary>Вердикт по метрикам фазы 1. Значения вне полосы, но меньше NotableExcessMm, — ok (шум эталона).</summary>
        public static FitGripVerdict Evaluate(FitContactMetrics metrics, FitBandSet bands, string gripType, string gripTypeSource, HandPoseFitRequest r)
        {
            var v = new FitGripVerdict {GripType = gripType, GripTypeSource = gripTypeSource, BandsVersion = bands?.Version, BandsPath = bands?.Path, BandsSha256 = bands?.Sha256,
                NotableExcessMm = r.NotableExcessMm, GrossExcessMm = r.GrossExcessMm, MissExcessMm = r.MissExcessMm, MissFingerCount = r.MissFingerCount, Status = "evaluated"};
            if (metrics == null) {v.Status = "no_contact_metrics";v.Overall = null;return v;}
            if (bands == null || string.IsNullOrEmpty(gripType) || !bands.Types.TryGetValue(gripType, out var type)) {v.Status = "no_bands_for_type";v.Reason = "Нет полос для типа '" + gripType + "'.";v.Overall = null;return v;}
            v.BandCases = type.Cases;v.LowConfidence = type.Cases <= bands.LowConfidenceMaxCases;
            int missFingers = 0;
            foreach (var fc in metrics.Fingers) {
                var f = new FitFingerVerdict {Finger = fc.Finger};v.Fingers.Add(f);
                bool pad = fc.Status == "measured";
                var padBand = type.Get(fc.Finger + ".pad");
                if (pad) {f.PadMm = fc.PadSignedDistanceMm;if (padBand != null) {f.PadBandLo = padBand.Lo;f.PadBandHi = padBand.Hi;f.PadBandMedian = padBand.P50;}}
                if (pad) {Distance(f, "pad", fc.PadSignedDistanceMm, padBand);Distance(f, "padMin", fc.PadMinSignedMm, type.Get(fc.Finger + ".padMin"));Depth(f, "padDepth", fc.PadPenetrationDepthMm, type.Get(fc.Finger + ".padDepth"));}
                Depth(f, "fingerDepth", fc.FingerPenetrationDepthMm, type.Get(fc.Finger + ".fingerDepth"));
                Classify(f, r);
                if (f.Flags.Any(x => x.Metric == "pad" && x.Meaning == "gap" && x.ExcessMm >= r.MissExcessMm)) missFingers++;
                v.Overall = Max(v.Overall, f.Severity);
            }
            var palm = metrics.Segments.FirstOrDefault(s => s.Segment == "palm_wrist");
            if (palm != null) {
                v.Palm = new FitFingerVerdict {Finger = "palm"};
                var minBand = type.Get("palm.min");
                if (!double.IsNaN(palm.MinSignedMm)) Distance(v.Palm, "palm.min", palm.MinSignedMm, minBand);
                Depth(v.Palm, "palm.depth", palm.PenetrationDepthMm, type.Get("palm.depth"));
                Classify(v.Palm, r);v.Overall = Max(v.Overall, v.Palm.Severity);
                if (minBand != null && palm.MinSignedMm - minBand.Hi >= r.GrossExcessMm) {v.HandFlags.Add("palm_off_object");v.Overall = "gross";}
            }
            if (missFingers >= r.MissFingerCount) {v.HandFlags.Add("miss");v.Overall = "gross";}
            return v;
        }

        static void Distance(FitFingerVerdict f, string metric, double value, FitBand band)
        {
            if (band == null || double.IsNaN(value)) return;
            if (value < band.Lo) f.Flags.Add(new FitBandFlag {Metric = metric, Value = value, Lo = band.Lo, Hi = band.Hi, Side = "below", Meaning = "penetration", ExcessMm = band.Lo - value});
            else if (value > band.Hi) f.Flags.Add(new FitBandFlag {Metric = metric, Value = value, Lo = band.Lo, Hi = band.Hi, Side = "above", Meaning = "gap", ExcessMm = value - band.Hi});
        }

        static void Depth(FitFingerVerdict f, string metric, double value, FitBand band)
        {
            if (band == null || double.IsNaN(value) || value <= band.Hi) return;
            f.Flags.Add(new FitBandFlag {Metric = metric, Value = value, Lo = band.Lo, Hi = band.Hi, Side = "above", Meaning = "penetration", ExcessMm = value - band.Hi});
        }

        static void Classify(FitFingerVerdict f, HandPoseFitRequest r)
        {
            f.MaxExcessMm = f.Flags.Count > 0 ? f.Flags.Max(x => x.ExcessMm) : 0;
            f.Severity = Severity(f.MaxExcessMm, r.NotableExcessMm, r.GrossExcessMm);
            var meanings = new HashSet<string>(f.Flags.Where(x => x.ExcessMm >= r.NotableExcessMm).Select(x => x.Meaning));
            f.Direction = meanings.Contains("gap") && meanings.Contains("penetration") ? "mixed" : meanings.Contains("penetration") ? "penetration" : meanings.Contains("gap") ? "gap" : "none";
        }

        static string Mm(double v) => (v >= 0 ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>Строки Findings отчёта: только нарушения (notable/gross) и оговорки достоверности.</summary>
        public static List<string> Describe(FitGripVerdict v)
        {
            var list = new List<string>();
            if (v == null) return list;
            if (v.Status != "evaluated") {list.Add("Правила хвата не применены: " + v.Status + (string.IsNullOrEmpty(v.Reason) ? "" : " — " + v.Reason));return list;}
            list.Add($"Тип хвата {v.GripType} ({v.GripTypeSource}); полосы {v.BandsVersion}, эталонов SDK {v.BandCases}{(v.LowConfidence ? " — low_confidence" : "")}; итог {v.Overall}.");
            if (v.HandFlags.Contains("miss")) list.Add($"Кисть: gross miss — у ≥{v.MissFingerCount} пальцев подушечка выше полосы на ≥{v.MissExcessMm:0} мм.");
            if (v.HandFlags.Contains("palm_off_object")) list.Add($"Ладонь: gross — минимум ладони выше полосы на ≥{v.GrossExcessMm:0} мм (ладонь вне предмета).");
            foreach (var f in v.Fingers.Concat(v.Palm != null ? new[] {v.Palm} : Array.Empty<FitFingerVerdict>())) {
                if (f.Severity == "ok") continue;
                var worst = f.Flags.OrderByDescending(x => x.ExcessMm).First();
                list.Add($"{f.Finger}: {f.Severity} {f.Direction} — {worst.Metric} {Mm(worst.Value)} мм вне полосы SDK [{Mm(worst.Lo)}; {Mm(worst.Hi)}] на {worst.ExcessMm:0.0} мм" +
                    (f.PadMm.HasValue && worst.Metric != "pad" ? $"; подушечка {Mm(f.PadMm.Value)} мм" : "") + ".");
            }
            return list;
        }
    }
}
