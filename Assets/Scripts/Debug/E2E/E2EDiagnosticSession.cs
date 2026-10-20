// Неизменяемые входы и единственный writer исходных GPU diagnostic evidence.
#if !VRBG_NO_E2E
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools.E2E
{
    public readonly struct DiagnosticReportResult
    {
        public readonly bool Accepted;
        public readonly string EventId;
        public readonly string Reason;
        public DiagnosticReportResult(bool accepted, string eventId, string reason)
        { Accepted = accepted; EventId = eventId; Reason = reason; }
    }

    public readonly struct DiagnosticObservationStamp
    {
        public readonly string ClockId;
        public readonly long Frame;
        public readonly long MonotonicTicks;
        public readonly long TickFrequency;
        public readonly double RealtimeSeconds;
        public readonly string UtcAnchor;
        public DiagnosticObservationStamp(string clockId, long frame, long monotonicTicks, long tickFrequency,
            double realtimeSeconds, string utcAnchor)
        { ClockId = clockId; Frame = frame; MonotonicTicks = monotonicTicks; TickFrequency = tickFrequency;
            RealtimeSeconds = realtimeSeconds; UtcAnchor = utcAnchor; }
    }

    public readonly struct DiagnosticTimeBounds
    {
        public readonly string ClockId;
        public readonly double StartSeconds;
        public readonly double EndSeconds;
        public readonly double? UncertaintySeconds;
        public readonly bool Verified;
        public readonly string Method;
        public DiagnosticTimeBounds(string clockId, double startSeconds, double endSeconds,
            double? uncertaintySeconds, bool verified, string method)
        { ClockId = clockId; StartSeconds = startSeconds; EndSeconds = endSeconds;
            UncertaintySeconds = uncertaintySeconds; Verified = verified; Method = method; }
    }

    public enum DiagnosticPhaseTransition { Begin = 0, End = 1 }

    public sealed class PhaseObservation
    {
        public readonly string PhaseId;
        public readonly long PhaseGeneration;
        public readonly DiagnosticPhaseTransition Transition;
        public readonly string ObservedRole;
        public readonly string Source;
        public readonly bool IsMeasured;
        public readonly double? RemainingWindowSeconds;
        public readonly DiagnosticObservationStamp Stamp;
        public PhaseObservation(string phaseId, long phaseGeneration, DiagnosticPhaseTransition transition,
            string observedRole, string source, bool isMeasured, double? remainingWindowSeconds,
            DiagnosticObservationStamp stamp)
        { PhaseId = phaseId; PhaseGeneration = phaseGeneration; Transition = transition;
            ObservedRole = observedRole; Source = source; IsMeasured = isMeasured;
            RemainingWindowSeconds = remainingWindowSeconds; Stamp = stamp; }
    }

    public sealed class ViewObservation
    {
        public readonly string ViewId;
        public readonly string PhaseId;
        public readonly long PhaseGeneration;
        public readonly bool Succeeded;
        public readonly Vector3? ActualPosition;
        public readonly Quaternion? ActualRotation;
        public readonly float? DeviationXZMetres;
        public readonly float? DeviationYawDegrees;
        public readonly string Refusal;
        public readonly string Source;
        public readonly DiagnosticObservationStamp Stamp;
        public ViewObservation(string viewId, string phaseId, long phaseGeneration, bool succeeded,
            Vector3? actualPosition, Quaternion? actualRotation, float? deviationXZMetres,
            float? deviationYawDegrees, string refusal, string source, DiagnosticObservationStamp stamp)
        { ViewId = viewId; PhaseId = phaseId; PhaseGeneration = phaseGeneration; Succeeded = succeeded;
            ActualPosition = actualPosition; ActualRotation = actualRotation; DeviationXZMetres = deviationXZMetres;
            DeviationYawDegrees = deviationYawDegrees; Refusal = refusal; Source = source; Stamp = stamp; }
    }

    public readonly struct DiagnosticConditionValue
    {
        public readonly object Value;
        public readonly string Source;
        public readonly string Reason;
        public DiagnosticConditionValue(object value, string source, string reason)
        { Value = DiagnosticJson.Snapshot(value); Source = source; Reason = reason; }
    }

    public sealed class ConditionsObservation
    {
        public readonly string ActualSceneId;
        public readonly string PhaseId;
        public readonly long PhaseGeneration;
        public readonly string ActualFingerprint;
        public readonly IReadOnlyDictionary<string, DiagnosticConditionValue> Values;
        public readonly DiagnosticObservationStamp Stamp;
        public ConditionsObservation(string actualSceneId, string phaseId, long phaseGeneration,
            string actualFingerprint, IReadOnlyDictionary<string, DiagnosticConditionValue> values,
            DiagnosticObservationStamp stamp)
        {
            ActualSceneId = actualSceneId; PhaseId = phaseId; PhaseGeneration = phaseGeneration;
            ActualFingerprint = actualFingerprint; Stamp = stamp;
            if (values == null) return;
            var copy = new Dictionary<string, DiagnosticConditionValue>(StringComparer.Ordinal);
            foreach (var pair in values)
                copy.Add(pair.Key, new DiagnosticConditionValue(pair.Value.Value, pair.Value.Source, pair.Value.Reason));
            DiagnosticJson.Bound(copy);
            Values = new ReadOnlyDictionary<string, DiagnosticConditionValue>(copy);
        }
    }

    public sealed class WorkloadReadyObservation
    {
        public readonly string ReadyId;
        public readonly string PhaseId;
        public readonly long PhaseGeneration;
        public readonly string ActualFingerprint;
        public readonly string ConditionsEventId;
        public readonly string ViewEventId;
        public readonly double RemainingWindowSeconds;
        public readonly DiagnosticTimeBounds TimeBounds;
        public readonly DiagnosticObservationStamp Stamp;
        public WorkloadReadyObservation(string readyId, string phaseId, long phaseGeneration,
            string actualFingerprint, string conditionsEventId, string viewEventId, double remainingWindowSeconds,
            DiagnosticTimeBounds timeBounds, DiagnosticObservationStamp stamp)
        { ReadyId = readyId; PhaseId = phaseId; PhaseGeneration = phaseGeneration;
            ActualFingerprint = actualFingerprint; ConditionsEventId = conditionsEventId; ViewEventId = viewEventId;
            RemainingWindowSeconds = remainingWindowSeconds; TimeBounds = timeBounds; Stamp = stamp; }
    }

    internal static class DiagnosticJson
    {
        internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        { Culture = CultureInfo.InvariantCulture, ReferenceLoopHandling = ReferenceLoopHandling.Error,
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
            { NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy
                { ProcessDictionaryKeys = false, OverrideSpecifiedNames = false } } };
        internal static string Encode(object value) => JsonConvert.SerializeObject(value, Formatting.None, Settings);
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static bool Hash(string value, int length = 64) => value != null &&
            Regex.IsMatch(value, "\\A[0-9a-fA-F]{" + length + "}\\z", RegexOptions.CultureInvariant);
        internal static bool Id(string value) => value != null &&
            Regex.IsMatch(value, "\\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\\z", RegexOptions.CultureInvariant) && value != "." && value != "..";
        internal static bool Text(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            foreach (char c in value) if (char.IsControl(c)) return false;
            return true;
        }
        internal static string Sha(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        internal static void Bound(object value)
        {
            if (Utf8.GetByteCount(Encode(value)) > 1024 * 1024)
                throw new ArgumentException("Diagnostic snapshot превышает 1 MiB.");
        }
        internal static object Snapshot(object value)
        {
            int count = 0;
            object copy = Copy(value, 0, new HashSet<object>(ReferenceIdentity.Instance), ref count);
            Bound(copy);
            return copy;
        }
        private sealed class ReferenceIdentity : IEqualityComparer<object>
        {
            internal static readonly ReferenceIdentity Instance = new ReferenceIdentity();
            public new bool Equals(object left, object right) => ReferenceEquals(left, right);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
        private static object Copy(object value, int depth, HashSet<object> active, ref int count)
        {
            if (depth > 64 || ++count > 1048576) throw new ArgumentException("Diagnostic snapshot слишком глубокий или большой.");
            if (value == null || value is string || value is bool || value is long) return value;
            if (value is double number && Finite(number)) return number;
            if (!active.Add(value)) throw new ArgumentException("Цикл в diagnostic snapshot.");
            try
            {
                if (value is IReadOnlyDictionary<string, object> readOnly)
                {
                    var copy = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (var pair in readOnly) copy.Add(pair.Key, Copy(pair.Value, depth + 1, active, ref count));
                    return new ReadOnlyDictionary<string, object>(copy);
                }
                if (value is IDictionary dictionary)
                {
                    var copy = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (DictionaryEntry pair in dictionary)
                    {
                        if (!(pair.Key is string key)) throw new ArgumentException("Diagnostic object key должен быть строкой.");
                        copy.Add(key, Copy(pair.Value, depth + 1, active, ref count));
                    }
                    return new ReadOnlyDictionary<string, object>(copy);
                }
                if (value is IReadOnlyList<object> readList)
                {
                    var copy = new List<object>();
                    foreach (object item in readList) copy.Add(Copy(item, depth + 1, active, ref count));
                    return new ReadOnlyCollection<object>(copy);
                }
                if (value is IList list)
                {
                    var copy = new List<object>();
                    foreach (object item in list) copy.Add(Copy(item, depth + 1, active, ref count));
                    return new ReadOnlyCollection<object>(copy);
                }
                throw new ArgumentException("Неподдерживаемый CLR тип diagnostic value: " + value.GetType().FullName);
            }
            finally { active.Remove(value); }
        }

        internal static JObject Parse(byte[] bytes)
        {
            if (bytes.Length > 16 * 1024 * 1024) throw new FormatException("JSON больше 16 MiB.");
            string text = Utf8.GetString(bytes);
            if (text.Length > 0 && text[0] == '\ufeff') text = text.Substring(1);
            ValidateSyntax(text);
            using (var input = new StringReader(text))
            using (var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None, MaxDepth = 64 })
            {
                JObject result = JObject.Load(reader, new JsonLoadSettings
                { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error, CommentHandling = CommentHandling.Load });
                if (reader.Read()) throw new FormatException("Данные после JSON object.");
                foreach (JToken token in result.Descendants())
                {
                    if (token.Type == JTokenType.Comment || token.Type == JTokenType.Undefined || token.Type == JTokenType.Constructor)
                        throw new FormatException("Недопустимый JSON token.");
                    if (token.Type == JTokenType.Float && !Finite(token.Value<double>())) throw new FormatException("Nonfinite JSON number.");
                }
                return result;
            }
        }
        // Json.NET принимает расширения JSON; wire contract разрешает только строгий JSON.
        private static void ValidateSyntax(string text)
        {
            int index = 0;
            JsonValue(text, ref index, 0);
            White(text, ref index);
            if (index != text.Length) throw new FormatException("Данные после JSON.");
        }
        private static void White(string text, ref int index)
        { while (index < text.Length && (text[index] == ' ' || text[index] == '\t' || text[index] == '\r' || text[index] == '\n')) ++index; }
        private static void JsonValue(string text, ref int index, int depth)
        {
            if (depth > 64) throw new FormatException("JSON слишком глубокий.");
            White(text, ref index);
            if (index >= text.Length) throw new FormatException("Оборванный JSON.");
            char first = text[index++];
            if (first == '"') { JsonString(text, ref index); return; }
            if (first == '{' || first == '[')
            {
                char end = first == '{' ? '}' : ']';
                White(text, ref index);
                if (index < text.Length && text[index] == end) { ++index; return; }
                while (true)
                {
                    if (first == '{')
                    {
                        White(text, ref index);
                        if (index >= text.Length || text[index++] != '"') throw new FormatException("JSON object key не строка.");
                        JsonString(text, ref index); White(text, ref index);
                        if (index >= text.Length || text[index++] != ':') throw new FormatException("Нет JSON colon.");
                    }
                    JsonValue(text, ref index, depth + 1); White(text, ref index);
                    if (index >= text.Length) throw new FormatException("Оборванный JSON object/array.");
                    char delimiter = text[index++];
                    if (delimiter == end) return;
                    if (delimiter != ',') throw new FormatException("Нет JSON delimiter.");
                }
            }
            if (first == 't' || first == 'f' || first == 'n')
            {
                string literal = first == 't' ? "rue" : first == 'f' ? "alse" : "ull";
                if (text.Length - index < literal.Length || string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0) throw new FormatException("Неверный JSON literal.");
                index += literal.Length; return;
            }
            --index;
            int start = index;
            if (text[index] == '-') ++index;
            if (index >= text.Length) throw new FormatException("Оборванное JSON число.");
            if (text[index] == '0') ++index;
            else
            {
                if (text[index] < '1' || text[index] > '9') throw new FormatException("Неверный JSON token.");
                while (index < text.Length && text[index] >= '0' && text[index] <= '9') ++index;
            }
            if (index < text.Length && text[index] == '.')
            { ++index; Digits(text, ref index); }
            if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
            {
                ++index;
                if (index < text.Length && (text[index] == '+' || text[index] == '-')) ++index;
                Digits(text, ref index);
            }
            if (!double.TryParse(text.Substring(start, index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !Finite(number))
                throw new FormatException("JSON число вне finite диапазона.");
        }
        private static void Digits(string text, ref int index)
        {
            int begin = index;
            while (index < text.Length && text[index] >= '0' && text[index] <= '9') ++index;
            if (begin == index) throw new FormatException("Нет цифр JSON числа.");
        }
        private static void JsonString(string text, ref int index)
        {
            while (index < text.Length)
            {
                char c = text[index++];
                if (c == '"') return;
                if (c < 32) throw new FormatException("Control character в JSON строке.");
                if (c != '\\') continue;
                if (index >= text.Length) break;
                char escaped = text[index++];
                if (escaped == 'u')
                {
                    for (int n = 0; n < 4; ++n)
                        if (index >= text.Length || !Uri.IsHexDigit(text[index++])) throw new FormatException("Неверный JSON unicode escape.");
                }
                else if ("\"\\/bfnrt".IndexOf(escaped) < 0) throw new FormatException("Неверный JSON escape.");
            }
            throw new FormatException("Оборванная JSON строка.");
        }
        internal static string String(JObject obj, string key)
        {
            if (obj[key]?.Type != JTokenType.String || !Text((string)obj[key])) throw new FormatException("Нужна строка " + key);
            return (string)obj[key];
        }
        internal static JObject Object(JObject obj, string key) => obj[key] as JObject ?? throw new FormatException("Нужен object " + key);
        internal static void Keys(JObject obj, params string[] keys)
        {
            var allowed = new HashSet<string>(keys, StringComparer.Ordinal);
            foreach (var p in obj.Properties()) if (!allowed.Contains(p.Name)) throw new FormatException("Неизвестное поле " + p.Name);
            foreach (string key in keys) if (obj.Property(key) == null) throw new FormatException("Нет поля " + key);
        }
        internal static long Integer(JObject obj, string key, long min)
        {
            if (obj[key]?.Type != JTokenType.Integer) throw new FormatException("Нужно целое " + key);
            long result = obj[key].Value<long>();
            if (result < min) throw new FormatException("Неверное " + key);
            return result;
        }
        internal static double Number(JObject obj, string key, double min)
        {
            var token = obj[key];
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) throw new FormatException("Нужно число " + key);
            double result = token.Value<double>();
            if (!Finite(result) || result < min) throw new FormatException("Неверное " + key);
            return result;
        }
        internal static string BundlePath(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(":"))
                throw new FormatException("Нужен относительный bundle path.");
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
            string prefix = root + Path.DirectorySeparatorChar;
            var comparison = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!full.StartsWith(prefix, comparison)) throw new FormatException("Путь выходит из bundle.");
            for (string path = full; path != null; path = Path.GetDirectoryName(path))
            {
                if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new FormatException("Symlink/reparse point в bundle.");
                if (string.Equals(path, root, comparison)) break;
            }
            return full;
        }
    }

    // Доступен только runner/context: замороженный intent не становится публичным settings API.
    internal sealed class E2EDiagnosticInput
    {
        internal readonly string RunId, StageRoot, SpecHash, BuildHash, RequestedSceneId, WorkloadId,
            RequestedFingerprint, ViewsPath;
        internal string Variant => (string)_spec["variant"];
        internal string SourceSha => (string)_spec["sourceSha"];
        internal string Mode => (string)_spec["mode"];
        internal bool DiscoveryOnly => _spec["quality"]["discoveryOnly"]?.Value<bool>() ?? false;
        internal readonly bool HeadPoseEnabled;
        internal readonly float HeadPoseEye;
        private readonly JObject _spec;
        private E2EDiagnosticInput(JObject spec, string stageRoot, string specHash, string buildHash, string viewsPath)
        {
            _spec = (JObject)spec.DeepClone(); StageRoot = stageRoot; SpecHash = specHash; BuildHash = buildHash;
            RunId = (string)spec["runId"]; RequestedSceneId = (string)spec["requestedSceneId"];
            WorkloadId = (string)spec["workloadId"]; RequestedFingerprint = (string)spec["quality"]["requestedFingerprint"];
            ViewsPath = viewsPath; HeadPoseEnabled = (string)spec["headPose"]["mode"] == "eye";
            HeadPoseEye = HeadPoseEnabled ? spec["headPose"]["eyeMeters"].Value<float>() : float.NaN;
        }
        internal object IntentReference => new { runId = RunId, pairId = (string)_spec["pairId"],
            variant = (string)_spec["variant"], repetition = _spec["repetition"].Value<long>(),
            mode = (string)_spec["mode"], sourceSha = (string)_spec["sourceSha"],
            phasePlanSha256 = (string)_spec["phasePlanSha256"], requestedSceneId = RequestedSceneId,
            workloadId = WorkloadId, quality = _spec["quality"].DeepClone(), capture = _spec["capture"].DeepClone() };

        internal static string TryRead(string[] args, E2EContext context, out E2EDiagnosticInput input)
        {
            input = null;
            bool hasSpec = Has(args, "-e2eRunSpec"), hasHash = Has(args, "-e2eRunSpecSha256");
            if (!hasSpec && !hasHash) return null;
            try
            {
                if (!hasSpec || !hasHash) throw new FormatException("Нужны оба -e2eRunSpec/-e2eRunSpecSha256.");
                string rawPath = Flag(args, "-e2eRunSpec"), expectedHash = Flag(args, "-e2eRunSpecSha256");
                if (!DiagnosticJson.Hash(expectedHash)) throw new FormatException("Неверный spec SHA256.");
                string path = Path.IsPathRooted(rawPath) ? Path.GetFullPath(rawPath) : DiagnosticJson.BundlePath(Application.persistentDataPath, rawPath);
                string root = Path.GetDirectoryName(path);
                string persistent = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string relative = path.StartsWith(persistent + Path.DirectorySeparatorChar,
                    Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor
                        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    ? path.Substring(persistent.Length + 1) : throw new FormatException("RunSpec вне persistentDataPath.");
                DiagnosticJson.BundlePath(persistent, relative);
                byte[] bytes = File.ReadAllBytes(path);
                string hash = DiagnosticJson.Sha(bytes);
                if (!string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase)) throw new FormatException("Spec SHA256 mismatch.");
                JObject spec = DiagnosticJson.Parse(bytes);
                DiagnosticJson.Keys(spec, "schemaVersion", "runId", "pairId", "variant", "repetition", "mode", "sourceSha",
                    "buildManifestSha256", "scenario", "requestedSceneId", "workloadId", "phasePlanSha256", "views", "headPose", "windows", "quality", "capture");
                if (DiagnosticJson.Integer(spec, "schemaVersion", 1) != 1) throw new FormatException("Неизвестная schemaVersion RunSpec.");
                string runId = DiagnosticJson.String(spec, "runId");
                if (!DiagnosticJson.Id(runId) || !DiagnosticJson.Id(context.Role)) throw new FormatException("Небезопасный runId/role.");
                foreach (string key in new[] { "pairId", "variant", "scenario", "workloadId" }) DiagnosticJson.String(spec, key);
                DiagnosticJson.Integer(spec, "repetition", 1);
                string mode = DiagnosticJson.String(spec, "mode");
                if (mode != "live" && mode != "instrumented") throw new FormatException("Неверный mode.");
                foreach (string key in new[] { "sourceSha", "buildManifestSha256", "phasePlanSha256" })
                    if (!DiagnosticJson.Hash(DiagnosticJson.String(spec, key), key == "sourceSha" ? 40 : 64)) throw new FormatException("Неверный hash " + key);
                string scene = DiagnosticJson.String(spec, "requestedSceneId");
                if (!CanonicalScene(scene)) throw new FormatException("Нужен canonical Assets/...unity requestedSceneId.");
                if ((string)spec["scenario"] != context.Scenario) throw new FormatException("Scenario identity mismatch.");
                if (Has(args, "-e2eMap") && Flag(args, "-e2eMap") != scene && Flag(args, "-e2eMap") != Path.GetFileNameWithoutExtension(scene))
                    throw new FormatException("CLI map конфликтует с RunSpec.");
                // Этот layout исключает смешение ролей даже при общем persistentDataPath.
                string[] parts = relative.Replace('\\', '/').Split('/');
                if (parts.Length != 5 || parts[0] != "e2e-diagnostics" || parts[1] != runId || parts[2] != context.Role ||
                    !DiagnosticJson.Id(parts[3]) || parts[4] != "run-spec.json") throw new FormatException("RunSpec staging identity mismatch.");
                JObject head = DiagnosticJson.Object(spec, "headPose"); DiagnosticJson.Keys(head, "mode", "eyeMeters");
                string headMode = DiagnosticJson.String(head, "mode");
                if (headMode == "off")
                { if (head["eyeMeters"].Type != JTokenType.Null) throw new FormatException("headPose off требует null eyeMeters."); }
                else if (headMode == "eye")
                {
                    double eye = DiagnosticJson.Number(head, "eyeMeters", PhysicalSpaceUtils.PlayerCalibrationRules.MinEyeHeight);
                    if (eye > PhysicalSpaceUtils.PlayerCalibrationRules.MaxEyeHeight) throw new FormatException("eyeMeters вне calibration limits.");
                }
                else throw new FormatException("Неверный headPose mode.");
                if (Has(args, "-e2eHeadPose") && (context.HeadPoseError != null || headMode != "eye" ||
                    !context.HeadPoseEnabled || context.HeadPoseEye != head["eyeMeters"].Value<float>())) throw new FormatException("CLI head pose конфликтует с RunSpec.");
                JObject windows = DiagnosticJson.Object(spec, "windows"); DiagnosticJson.Keys(windows, "width", "height", "windowed");
                if (DiagnosticJson.Integer(windows, "width", 1) > int.MaxValue || DiagnosticJson.Integer(windows, "height", 1) > int.MaxValue ||
                    windows["windowed"].Type != JTokenType.Boolean) throw new FormatException("Неверный windows.");
                CheckWindow(args, windows);
                JObject quality = DiagnosticJson.Object(spec, "quality");
                bool qualityDiscovery = false;
                if (quality.Property("discoveryOnly") != null)
                {
                    DiagnosticJson.Keys(quality, "profileId", "requestedFingerprint", "discoveryOnly");
                    if (quality["discoveryOnly"].Type != JTokenType.Boolean) throw new FormatException("Неверный discoveryOnly.");
                    qualityDiscovery = quality["discoveryOnly"].Value<bool>();
                }
                else DiagnosticJson.Keys(quality, "profileId", "requestedFingerprint");
                DiagnosticJson.String(quality, "profileId");
                if (qualityDiscovery ? quality["requestedFingerprint"].Type != JTokenType.Null :
                    !DiagnosticJson.Hash(DiagnosticJson.String(quality, "requestedFingerprint"))) throw new FormatException("Неверный requestedFingerprint.");
                JObject capture = DiagnosticJson.Object(spec, "capture"); DiagnosticJson.Keys(capture, "kind", "minimumWindowSeconds");
                string kind = DiagnosticJson.String(capture, "kind");
                if (kind != "none" && kind != "stage" && kind != "draw") throw new FormatException("Неверный capture kind.");
                if (qualityDiscovery && (kind != "none" || (string)spec["mode"] != "live"))
                    throw new FormatException("Discovery требует live/capture none.");
                DiagnosticJson.Number(capture, "minimumWindowSeconds", 0);
                string viewsPath = null;
                if (spec["views"].Type != JTokenType.Null)
                {
                    JObject views = DiagnosticJson.Object(spec, "views"); DiagnosticJson.Keys(views, "path", "sha256");
                    string viewHash = DiagnosticJson.String(views, "sha256");
                    if (!DiagnosticJson.Hash(viewHash)) throw new FormatException("Неверный views SHA256.");
                    viewsPath = DiagnosticJson.BundlePath(root, DiagnosticJson.String(views, "path"));
                    if (!string.Equals(viewHash, DiagnosticJson.Sha(File.ReadAllBytes(viewsPath)), StringComparison.OrdinalIgnoreCase)) throw new FormatException("Views SHA256 mismatch.");
                    // Формат точек проверяет существующий владелец, без переноса камеры.
                    if (E2EViews.Load(viewsPath, out string viewError) == null) throw new FormatException("Views: " + viewError);
                }
                if (context.Args.TryGetValue(E2EViews.ArgKey, out string cliViews) &&
                    (viewsPath == null || !string.Equals(Path.GetFullPath(Path.IsPathRooted(cliViews) ? cliViews : Path.Combine(Application.persistentDataPath, cliViews)),
                        viewsPath, Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor
                            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))) throw new FormatException("CLI views конфликтует с RunSpec.");
                string buildPath = DiagnosticJson.BundlePath(root, "build.json");
                byte[] buildBytes = File.ReadAllBytes(buildPath);
                string buildHash = DiagnosticJson.Sha(buildBytes);
                if (!string.Equals(buildHash, (string)spec["buildManifestSha256"], StringComparison.OrdinalIgnoreCase)) throw new FormatException("Build manifest SHA256 mismatch.");
                ValidateBuild(DiagnosticJson.Parse(buildBytes), spec, context);
                input = new E2EDiagnosticInput(spec, root, hash, buildHash, viewsPath);
                return null;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException ||
                error is JsonException || error is FormatException || error is OverflowException || error is NotSupportedException || error is System.Security.SecurityException)
            { return "invalid-input: " + error.Message; }
        }

        private static void ValidateBuild(JObject build, JObject spec, E2EContext context)
        {
            if (DiagnosticJson.Integer(build, "schemaVersion", 1) != 1) throw new FormatException("Неизвестная build schemaVersion.");
            string source = DiagnosticJson.String(build, "sourceSha");
            if (!DiagnosticJson.Hash(source, 40) || !string.Equals(source, (string)spec["sourceSha"], StringComparison.OrdinalIgnoreCase)) throw new FormatException("Build source identity mismatch.");
            // Unknown sourceKind сохраняется в исходном manifest; accepted не выводится из имени файла.
            if (build.Property("sourceKind") == null || build.Property("dirty") == null) throw new FormatException("Нет build source evidence.");
            if (build["sourceKind"].Type != JTokenType.Null && build["sourceKind"].Type != JTokenType.String) throw new FormatException("Неверный sourceKind.");
            JObject dirty = DiagnosticJson.Object(build, "dirty");
            if (dirty["value"] == null || (dirty["value"].Type != JTokenType.Null && dirty["value"].Type != JTokenType.Boolean) ||
                !(dirty["entries"] is JArray) || dirty["reason"] == null ||
                (dirty["reason"].Type != JTokenType.Null && dirty["reason"].Type != JTokenType.String) ||
                (dirty["value"].Type == JTokenType.Null && !DiagnosticJson.Text((string)dirty["reason"]))) throw new FormatException("Неверный dirty evidence.");
            DiagnosticJson.String(dirty, "scope");
            if (!(build["profiles"] is JArray profiles) || profiles.Count == 0) throw new FormatException("Нет build profiles.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            JObject match = null;
            foreach (JToken token in profiles)
            {
                if (!(token is JObject profile)) throw new FormatException("Неверный profile.");
                string name = DiagnosticJson.String(profile, "profile");
                if (!names.Add(name) || (name != "server" && name != "quest" && name != "tablet" && name != "e2e")) throw new FormatException("Неверный/повторный profile.");
                bool fits = context.IsServerRole ? name == "server" || name == "e2e" :
                    Application.platform == RuntimePlatform.Android ? name == "quest" : name == "e2e";
                if (fits)
                {
                    if (match != null) throw new FormatException("Неоднозначный role/build profile.");
                    match = profile;
                }
            }
            if (match == null || match["success"]?.Type != JTokenType.Boolean || !match["success"].Value<bool>()) throw new FormatException("Нет успешного build profile для роли.");
            DiagnosticJson.String(match, "outputRoot");
            JObject actual = DiagnosticJson.Object(match, "actual");
            JObject unavailable = DiagnosticJson.Object(actual, "unavailableReasons");
            foreach (string key in new[] { "developmentBuild", "scenePaths", "buildGuid", "appVersion", "unityVersion", "companyName", "productName", "buildTarget", "subtarget", "scriptingBackend", "graphicsApis", "buildOptions" })
            {
                if (actual.Property(key) == null) throw new FormatException("Нет build actual " + key);
                if (actual[key].Type == JTokenType.Null && (unavailable[key]?.Type != JTokenType.String || !DiagnosticJson.Text((string)unavailable[key])))
                    throw new FormatException("Нет причины unknown build actual " + key);
            }
            if (actual["developmentBuild"]?.Type != JTokenType.Boolean || actual["developmentBuild"].Value<bool>() != UnityEngine.Debug.isDebugBuild)
                throw new FormatException("Build Development identity mismatch/unknown.");
            if (!(actual["scenePaths"] is JArray scenes)) throw new FormatException("Нет build scenePaths evidence.");
            bool containsScene = false;
            foreach (JToken scene in scenes)
            {
                if (scene.Type != JTokenType.String || !CanonicalScene((string)scene)) throw new FormatException("Неверный build scene path.");
                if ((string)scene == (string)spec["requestedSceneId"]) containsScene = true;
            }
            if (!containsScene) throw new FormatException("Requested scene отсутствует в build profile.");
            CheckBuildString(actual, "buildGuid", Application.buildGUID);
            CheckBuildString(actual, "appVersion", Application.version);
            CheckBuildString(actual, "unityVersion", Application.unityVersion);
            CheckBuildString(actual, "companyName", Application.companyName);
            CheckBuildString(actual, "productName", Application.productName);
            if (actual["buildGuid"].Type == JTokenType.Null) throw new FormatException("Build GUID неизвестен: runtime identity не подтверждена.");
            foreach (string key in new[] { "buildTarget", "scriptingBackend", "buildOptions" })
                if (actual[key].Type != JTokenType.Null && (actual[key].Type != JTokenType.String || !DiagnosticJson.Text((string)actual[key])))
                    throw new FormatException("Неверный build actual " + key);
            if (actual["graphicsApis"].Type != JTokenType.Null)
            {
                if (!(actual["graphicsApis"] is JArray apis)) throw new FormatException("Неверный build graphicsApis.");
                foreach (JToken api in apis) if (api.Type != JTokenType.String || !DiagnosticJson.Text((string)api)) throw new FormatException("Неверный graphics API.");
            }
            JObject terminal = DiagnosticJson.Object(match, "terminal");
            if (!DateTimeOffset.TryParseExact(DiagnosticJson.String(terminal, "startedUtc"), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset start) ||
                !DateTimeOffset.TryParseExact(DiagnosticJson.String(terminal, "endedUtc"), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset end) ||
                end < start || (string)terminal["result"] != "Succeeded") throw new FormatException("Неверный successful build terminal.");
            if (!(match["files"] is JArray files) || files.Count == 0) throw new FormatException("Нет build artifact inventory.");
            var artifactPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (JToken item in files)
            {
                if (!(item is JObject file)) throw new FormatException("Неверный build artifact.");
                string path = DiagnosticJson.String(file, "path");
                string canonicalPath = path.Replace('\\', '/');
                if (Path.IsPathRooted(path) || canonicalPath.StartsWith("/", StringComparison.Ordinal) || path.Contains(":") ||
                    Array.Exists(canonicalPath.Split('/'), p => p == "." || p == ".." || p.Length == 0) ||
                    string.Equals(canonicalPath, "build.json", StringComparison.OrdinalIgnoreCase) || !artifactPaths.Add(canonicalPath))
                    throw new FormatException("Небезопасный/повторный artifact path или self-inclusion.");
                DiagnosticJson.Integer(file, "bytes", 0);
                if (!DiagnosticJson.Hash(DiagnosticJson.String(file, "sha256"))) throw new FormatException("Неверный artifact SHA256.");
            }
        }
        private static void CheckBuildString(JObject actual, string key, string runtime)
        {
            JToken token = actual[key];
            if (token == null) throw new FormatException("Нет build actual " + key);
            if (token.Type == JTokenType.Null) return;
            if (token.Type != JTokenType.String || !DiagnosticJson.Text((string)token) || (string)token != runtime)
                throw new FormatException("Build identity mismatch: " + key);
        }
        internal static bool CanonicalScene(string value) => DiagnosticJson.Text(value) && value.StartsWith("Assets/", StringComparison.Ordinal) &&
            value.EndsWith(".unity", StringComparison.Ordinal) && !value.Contains("\\") && !value.Contains(":") &&
            !Array.Exists(value.Split('/'), p => p == "." || p == ".." || p.Length == 0);
        private static bool Has(string[] args, string key) => Array.Exists(args, a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
        private static string Flag(string[] args, string key)
        {
            string result = null;
            for (int i = 0; i < args.Length; ++i)
            {
                if (!string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) continue;
                if (result != null || i + 1 == args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    throw new FormatException("Повторный/пустой CLI flag " + key);
                result = args[++i];
            }
            return result ?? throw new FormatException("Нет CLI flag " + key);
        }
        private static void CheckWindow(string[] args, JObject windows)
        {
            foreach (var pair in new[] { new KeyValuePair<string, string>("-screen-width", "width"), new KeyValuePair<string, string>("-screen-height", "height") })
                if (Has(args, pair.Key) && (!long.TryParse(Flag(args, pair.Key), NumberStyles.None, CultureInfo.InvariantCulture, out long value) || value != windows[pair.Value].Value<long>()))
                    throw new FormatException("CLI window конфликтует с RunSpec.");
            if (Has(args, "-screen-fullscreen") && Flag(args, "-screen-fullscreen") != (windows["windowed"].Value<bool>() ? "0" : "1")) throw new FormatException("CLI fullscreen конфликтует с RunSpec.");
        }
    }

    /// <summary>Один синхронный main-thread writer исходных наблюдений; не исполняет workload и не меняет настройки.</summary>
    public sealed class E2EDiagnosticSession
    {
        public string RunId => _input.RunId;
        public string ProcessInstanceId { get; }
        public string Role { get; }
        public string ClockId { get; }
        public long TickFrequency => Stopwatch.Frequency;
        public bool IsActive => _active && !_ioFailed;

        /// <summary>Каталог этого процесса в уже зарегистрированном diagnostic artifact.</summary>
        internal string ArtifactDirectory => _directory;

        private readonly E2EDiagnosticInput _input;
        private readonly E2EResult _result;
        private readonly int _threadId;
        private readonly string _directory, _actualPath, _eventsPath, _resultPath, _startedUtc;
        private readonly Dictionary<string, ReadyReceipt> _ready = new Dictionary<string, ReadyReceipt>(StringComparer.Ordinal);
        private readonly Dictionary<string, DiagnosticReportResult> _invalidated = new Dictionary<string, DiagnosticReportResult>(StringComparer.Ordinal);
        private StreamWriter _writer;
        private bool _active, _ioFailed, _subscribed, _ended, _generationInvalidated;
        private string _ioReason;
        private long _sequence, _lastTicks = -1, _lastFrame = -1;
        private double _lastRealtime = -1;
        private PhaseObservation _phase;
        private ViewObservation _view;
        private ConditionsObservation _conditions;
        private string _viewEventId, _conditionsEventId, _activeReady;

        private sealed class ReadyReceipt
        {
            internal readonly string Json;
            internal readonly DiagnosticReportResult Result;
            internal ReadyReceipt(string json, DiagnosticReportResult result) { Json = json; Result = result; }
        }
        private static readonly string[] RequiredConditions = { "deviceModel", "deviceId", "graphicsApi", "developmentBuild", "sceneId", "role", "viewId", "phaseId",
            "workloadId", "refreshHz", "eyeResolution", "ffr", "cpuLevel", "gpuLevel", "qualityHash", "qualityFingerprintMethod" };

        private E2EDiagnosticSession(E2EDiagnosticInput input, E2EContext context, E2EResult result)
        {
            _input = input; _result = result; _threadId = Thread.CurrentThread.ManagedThreadId;
            ProcessInstanceId = Guid.NewGuid().ToString("N"); Role = context.Role;
            ClockId = "process:" + ProcessInstanceId + ":stopwatch";
            _startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture); _resultPath = context.ResultPath;
            _directory = DiagnosticJson.BundlePath(input.StageRoot, "source/processes/" + ProcessInstanceId);
            _actualPath = Path.Combine(_directory, "actual.json"); _eventsPath = Path.Combine(_directory, "events.jsonl");
        }

        internal static string TryCreate(E2EContext context, E2EResult result, out E2EDiagnosticSession session)
        {
            session = null;
            if (context.DiagnosticInput == null) return null;
            E2EDiagnosticSession candidate = null;
            try
            {
                candidate = new E2EDiagnosticSession(context.DiagnosticInput, context, result);
                if (!result.AddArtifact(context.DiagnosticInput.StageRoot)) return "invalid-input: diagnostic artifact вне persistentDataPath";
                Directory.CreateDirectory(candidate._directory);
                candidate._writer = new StreamWriter(new FileStream(candidate._eventsPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), DiagnosticJson.Utf8);
                candidate._active = true;
                candidate.WriteActual("started", null, false);
                DiagnosticReportResult start = candidate.Emit("process.started", new { source = "E2ERunner", intent = candidate._input.IntentReference });
                if (!start.Accepted) { candidate.CloseWriter(); return start.Reason; }
                SceneManager.sceneLoaded += candidate.OnSceneLoaded;
                SceneManager.sceneUnloaded += candidate.OnSceneUnloaded;
                SceneManager.activeSceneChanged += candidate.OnActiveSceneChanged;
                candidate._subscribed = true;
                session = candidate;
                return null;
            }
            catch (Exception error) when (Expected(error))
            {
                candidate?.Fault(error);
                return "io-error: " + error.Message;
            }
        }

        public DiagnosticObservationStamp CaptureStamp()
        {
            if (!MainThread || !IsActive) return default;
            return Now();
        }
        private bool MainThread => Thread.CurrentThread.ManagedThreadId == _threadId;
        private DiagnosticObservationStamp Now() => new DiagnosticObservationStamp(ClockId, Time.frameCount,
            Stopwatch.GetTimestamp(), TickFrequency, Time.realtimeSinceStartupAsDouble, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        public DiagnosticReportResult ReportPhase(PhaseObservation value)
        {
            if (!MainThread) return Rejected("wrong-thread");
            if (value == null || !DiagnosticJson.Text(value.PhaseId) || value.PhaseGeneration < 1 ||
                !DiagnosticJson.Text(value.Source) || value.ObservedRole == null ||
                (value.Transition != DiagnosticPhaseTransition.Begin && value.Transition != DiagnosticPhaseTransition.End) ||
                (value.RemainingWindowSeconds.HasValue && (!DiagnosticJson.Finite(value.RemainingWindowSeconds.Value) || value.RemainingWindowSeconds.Value < 0)) ||
                (value.Transition == DiagnosticPhaseTransition.End && value.RemainingWindowSeconds != 0)) return Reject("invalid-input: phase");
            if (value.ObservedRole != Role) return Reject("identity-mismatch: observedRole");
            string error = Guard(value.Stamp);
            if (error != null) return Reject(error);
            bool begin = value.Transition == DiagnosticPhaseTransition.Begin;
            if (begin ? _phase != null && value.PhaseGeneration <= _phase.PhaseGeneration : _phase == null || value.PhaseGeneration != _phase.PhaseGeneration)
                return Reject("stale-generation: phase");
            if (!begin && (_phase.Transition != DiagnosticPhaseTransition.Begin || _phase.PhaseId != value.PhaseId || _phase.IsMeasured != value.IsMeasured))
                return Reject("identity-mismatch: phase end");
            if (!InvalidateActive(begin ? "phase-generation-changed" : "phase-ended")) return Rejected("io-error");
            DiagnosticReportResult written = Emit(begin ? "phase.begin" : "phase.end", value, value.PhaseId, value.PhaseGeneration);
            if (written.Accepted)
            {
                _phase = value; Observe(value.Stamp);
                if (begin) { _generationInvalidated = false; ClearObservations(); }
            }
            return written;
        }

        public DiagnosticReportResult ReportView(ViewObservation value)
        {
            if (!MainThread) return Rejected("wrong-thread");
            if (value == null || !DiagnosticJson.Text(value.ViewId) || !DiagnosticJson.Text(value.PhaseId) || value.PhaseGeneration < 1 ||
                !DiagnosticJson.Text(value.Source) || value.Refusal == null ||
                (value.Succeeded ? !value.ActualPosition.HasValue || !value.ActualRotation.HasValue || value.Refusal.Length != 0 : !DiagnosticJson.Text(value.Refusal)) ||
                (value.ActualPosition.HasValue && !Finite(value.ActualPosition.Value)) || (value.ActualRotation.HasValue && !Finite(value.ActualRotation.Value)) ||
                (value.DeviationXZMetres.HasValue && (!DiagnosticJson.Finite(value.DeviationXZMetres.Value) || value.DeviationXZMetres.Value < 0)) ||
                (value.DeviationYawDegrees.HasValue && (!DiagnosticJson.Finite(value.DeviationYawDegrees.Value) || value.DeviationYawDegrees.Value < 0))) return Reject("invalid-input: view");
            string error = Guard(value.Stamp) ?? Generation(value.PhaseId, value.PhaseGeneration);
            if (error != null) return Reject(error);
            if (_activeReady != null && (_view == null || _view.ViewId != value.ViewId || _view.Succeeded != value.Succeeded ||
                !_view.ActualPosition.Equals(value.ActualPosition) || !_view.ActualRotation.Equals(value.ActualRotation)) && !InvalidateActive("view-changed")) return Rejected("io-error");
            DiagnosticReportResult written = Emit("view.observed", ViewPayload(value), value.PhaseId, value.PhaseGeneration, value.ViewId);
            if (written.Accepted) { _view = value; _viewEventId = written.EventId; Observe(value.Stamp); }
            return written;
        }

        public DiagnosticReportResult ReportConditions(ConditionsObservation value)
        {
            if (!MainThread) return Rejected("wrong-thread");
            string shape = ValidateConditions(value);
            if (shape != null) return Reject(shape);
            string error = Guard(value.Stamp) ?? Generation(value.PhaseId, value.PhaseGeneration);
            if (error != null) return Reject(error);
            if (value.ActualSceneId.Length != 0 && !SceneLoaded(value.ActualSceneId)) return Reject("identity-mismatch: scene not loaded");
            if (_activeReady != null && (_conditions == null || value.ActualSceneId != _conditions.ActualSceneId ||
                value.ActualFingerprint != _conditions.ActualFingerprint || !SameConditions(_conditions, value)) &&
                !InvalidateActive("conditions-changed")) return Rejected("io-error");
            DiagnosticReportResult written = Emit("conditions.observed", value, value.PhaseId, value.PhaseGeneration, actualSceneId: value.ActualSceneId);
            if (written.Accepted) { _conditions = value; _conditionsEventId = written.EventId; Observe(value.Stamp); }
            return written;
        }

        public DiagnosticReportResult ReportWorkloadReady(WorkloadReadyObservation value)
        {
            if (!MainThread) return Rejected("wrong-thread");
            if (_input.DiscoveryOnly) return Reject("missing-evidence: discovery-only session cannot publish workload.ready");
            if (value == null || !ReadyId(value.ReadyId) || !DiagnosticJson.Text(value.PhaseId) || value.PhaseGeneration < 1 ||
                !DiagnosticJson.Hash(value.ActualFingerprint) || !DiagnosticJson.Finite(value.RemainingWindowSeconds) || value.RemainingWindowSeconds <= 0 ||
                !OwnEvent(value.ConditionsEventId) || !OwnEvent(value.ViewEventId)) return Reject("invalid-input: ready");
            // Replay доказывает прежнюю запись, но не восстанавливает active readiness после End/cleanup.
            string membership = StampShape(value.Stamp) ?? Bounds(value.TimeBounds, value.Stamp, false);
            if (membership != null) return Reject(membership);
            string json = DiagnosticJson.Encode(value);
            if (_ready.TryGetValue(value.ReadyId, out ReadyReceipt prior))
                return prior.Json == json ? prior.Result : Reject("identity-mismatch: readyId reused");
            string error = Guard(value.Stamp) ?? Bounds(value.TimeBounds, value.Stamp, true) ?? Generation(value.PhaseId, value.PhaseGeneration);
            if (error != null) return Reject(error);
            if (!_phase.IsMeasured) return Reject("missing-evidence: phase not measured");
            if (_view == null || !_view.Succeeded || _conditions == null || _viewEventId != value.ViewEventId || _conditionsEventId != value.ConditionsEventId)
                return Reject("missing-evidence: latest view/conditions required");
            if (_view.PhaseGeneration != value.PhaseGeneration || _conditions.PhaseGeneration != value.PhaseGeneration)
                return Reject("stale-generation: ready observations");
            if (_view.PhaseId != value.PhaseId || _conditions.PhaseId != value.PhaseId) return Reject("identity-mismatch: ready phase");
            if (_view.Stamp.MonotonicTicks < _phase.Stamp.MonotonicTicks) return Reject("missing-evidence: view predates measured begin");
            foreach (string key in RequiredConditions)
                if (!_conditions.Values.TryGetValue(key, out DiagnosticConditionValue condition) || condition.Value == null)
                {
                    if (!WindowsNotApplicable(key, condition)) return Reject("unknown-conditions: " + key);
                }
            if (!ConditionEquals("phaseId", value.PhaseId) || !ConditionEquals("viewId", _view.ViewId) || !ConditionEquals("role", Role) ||
                !ConditionEquals("sceneId", _conditions.ActualSceneId) || !ConditionEquals("workloadId", _input.WorkloadId) ||
                _conditions.ActualSceneId != _input.RequestedSceneId || !SceneLoaded(_conditions.ActualSceneId) ||
                _conditions.ActualFingerprint != value.ActualFingerprint || !string.Equals(value.ActualFingerprint, _input.RequestedFingerprint, StringComparison.OrdinalIgnoreCase))
                return Reject("identity-mismatch: ready scene/view/role/workload/fingerprint");
            if (!Application.isFocused) return Reject("missing-evidence: application not focused");
            if (_activeReady != null && !InvalidateActive("ready-replaced")) return Rejected("io-error");
            DiagnosticReportResult written = Emit("workload.ready", value, value.PhaseId, value.PhaseGeneration, _view.ViewId);
            if (written.Accepted)
            { _ready.Add(value.ReadyId, new ReadyReceipt(json, written)); _activeReady = value.ReadyId; Observe(value.Stamp); }
            return written;
        }

        public DiagnosticReportResult InvalidateWorkload(string readyId, string reason)
        {
            if (!MainThread) return Rejected("wrong-thread");
            if (!ReadyId(readyId) || !DiagnosticJson.Text(reason)) return Reject("invalid-input: invalidation");
            if (_invalidated.TryGetValue(readyId, out DiagnosticReportResult prior)) return prior;
            if (!_ready.ContainsKey(readyId)) return Reject("missing-evidence: unknown readyId");
            if (!IsActive) return Rejected(_ioFailed ? "io-error" : "inactive-session");
            DiagnosticReportResult written = Emit("workload.invalidated", new { readyId, reason });
            if (written.Accepted)
            {
                _invalidated.Add(readyId, written);
                if (_activeReady == readyId) _activeReady = null;
            }
            return written;
        }

        private string Guard(DiagnosticObservationStamp stamp)
        {
            if (!IsActive) return _ioFailed ? "io-error" : "inactive-session";
            string error = StampShape(stamp);
            if (error != null) return error;
            if (stamp.MonotonicTicks > Stopwatch.GetTimestamp() || stamp.Frame > Time.frameCount || stamp.RealtimeSeconds > Time.realtimeSinceStartupAsDouble)
                return "wrong-clock: future observation";
            if (DateTimeOffset.ParseExact(stamp.UtcAnchor, "O", CultureInfo.InvariantCulture).UtcDateTime > DateTime.UtcNow)
                return "wrong-clock: future UTC anchor";
            if (stamp.MonotonicTicks < _lastTicks || stamp.Frame < _lastFrame || stamp.RealtimeSeconds < _lastRealtime)
                return "wrong-clock: stale observation";
            if (_activeReady != null && !SceneLoaded(_input.RequestedSceneId) && !InvalidateActive("scene-changed")) return "io-error";
            return null;
        }
        private string StampShape(DiagnosticObservationStamp stamp)
        {
            if (stamp.ClockId != ClockId || stamp.TickFrequency != TickFrequency) return "wrong-clock: identity/frequency";
            if (stamp.MonotonicTicks < 0 || stamp.Frame < 0 || !DiagnosticJson.Finite(stamp.RealtimeSeconds) || stamp.RealtimeSeconds < 0 ||
                !DateTimeOffset.TryParseExact(stamp.UtcAnchor, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset utc) || utc.Offset != TimeSpan.Zero)
                return "invalid-input: stamp";
            return null;
        }
        private string Bounds(DiagnosticTimeBounds bounds, DiagnosticObservationStamp stamp, bool current)
        {
            if (bounds.ClockId != ClockId) return "wrong-clock: bounds identity";
            if (!DiagnosticJson.Finite(bounds.StartSeconds) || !DiagnosticJson.Finite(bounds.EndSeconds) || bounds.StartSeconds < 0 ||
                bounds.EndSeconds < bounds.StartSeconds || !DiagnosticJson.Text(bounds.Method) ||
                (bounds.UncertaintySeconds.HasValue && (!DiagnosticJson.Finite(bounds.UncertaintySeconds.Value) || bounds.UncertaintySeconds.Value < 0)) ||
                (bounds.Verified && !bounds.UncertaintySeconds.HasValue)) return "invalid-input: bounds";
            double observation = (double)stamp.MonotonicTicks / TickFrequency;
            if (observation < bounds.StartSeconds || observation > bounds.EndSeconds) return "wrong-clock: bounds do not contain observation";
            if (current && bounds.EndSeconds > (double)Stopwatch.GetTimestamp() / TickFrequency) return "wrong-clock: future bounds";
            return null;
        }
        private string Generation(string phaseId, long generation)
        {
            if (_phase == null) return "missing-evidence: phase begin";
            if (_generationInvalidated) return "stale-generation: scene/focus invalidated phase";
            if (_phase.PhaseGeneration != generation) return "stale-generation";
            if (_phase.PhaseId != phaseId) return "identity-mismatch: phaseId";
            if (_phase.Transition != DiagnosticPhaseTransition.Begin) return "missing-evidence: phase ended";
            return null;
        }
        private void Observe(DiagnosticObservationStamp stamp)
        { _lastTicks = stamp.MonotonicTicks; _lastFrame = stamp.Frame; _lastRealtime = stamp.RealtimeSeconds; }

        private static string ValidateConditions(ConditionsObservation value)
        {
            if (value == null || value.ActualSceneId == null || value.ActualFingerprint == null || !DiagnosticJson.Text(value.PhaseId) || value.PhaseGeneration < 1 || value.Values == null)
                return "invalid-input: conditions";
            if ((value.ActualSceneId.Length != 0 && !E2EDiagnosticInput.CanonicalScene(value.ActualSceneId)) ||
                (value.ActualFingerprint.Length != 0 && !DiagnosticJson.Hash(value.ActualFingerprint))) return "invalid-input: conditions identity";
            foreach (var pair in value.Values)
            {
                DiagnosticConditionValue condition = pair.Value;
                if (!DiagnosticJson.Text(pair.Key) || !DiagnosticJson.Text(condition.Source) || condition.Reason == null ||
                    (condition.Value == null ? !DiagnosticJson.Text(condition.Reason) : condition.Reason.Length != 0)) return "invalid-input: condition " + pair.Key;
                object item = condition.Value;
                if (item == null) continue;
                switch (pair.Key)
                {
                    case "deviceModel": case "deviceId": case "graphicsApi": case "sceneId": case "role": case "viewId": case "phaseId":
                    case "workloadId": case "qualityHash": case "qualityFingerprintMethod":
                        if (!(item is string)) return "invalid-input: string condition " + pair.Key;
                        break;
                    case "developmentBuild": if (!(item is bool)) return "invalid-input: developmentBuild"; break;
                    case "refreshHz": case "ffr": case "cpuLevel": case "gpuLevel":
                        if (!(item is long) && !(item is double)) return "invalid-input: numeric condition " + pair.Key;
                        break;
                    case "eyeResolution": if (!EyeResolution(item)) return "invalid-input: eyeResolution"; break;
                }
            }
            if (value.ActualSceneId.Length == 0 && (!value.Values.TryGetValue("sceneId", out DiagnosticConditionValue scene) || scene.Value != null || !DiagnosticJson.Text(scene.Reason)))
                return "invalid-input: unknown scene requires reason";
            if (value.ActualSceneId.Length != 0 && value.Values.TryGetValue("sceneId", out DiagnosticConditionValue actualScene) &&
                actualScene.Value != null && !Equals(actualScene.Value, value.ActualSceneId)) return "identity-mismatch: conditions sceneId";
            if (value.ActualFingerprint.Length != 0 && (!value.Values.TryGetValue("qualityFingerprintMethod", out DiagnosticConditionValue method) ||
                !(method.Value is string methodName) || !DiagnosticJson.Text(methodName))) return "invalid-input: fingerprint method unknown";
            return null;
        }
        private static bool EyeResolution(object value)
        {
            if (!(value is IReadOnlyDictionary<string, object> eye) || !eye.TryGetValue("mode", out object mode) ||
                (!(mode is string name) || (name != "mono" && name != "stereo")) || !Dimensions(eye)) return false;
            if (eye.TryGetValue("eyes", out object eyes))
            {
                if (!(eyes is IReadOnlyList<object> list) || list.Count == 0) return false;
                foreach (object item in list) if (!(item is IReadOnlyDictionary<string, object> child) || !Dimensions(child)) return false;
            }
            return true;
        }
        private static bool Dimensions(IReadOnlyDictionary<string, object> value) => value.TryGetValue("width", out object width) && width is long w && w > 0 &&
            value.TryGetValue("height", out object height) && height is long h && h > 0;
        private bool ConditionEquals(string key, string value) => _conditions.Values.TryGetValue(key, out DiagnosticConditionValue condition) && Equals(condition.Value, value);

        private bool WindowsNotApplicable(string key, DiagnosticConditionValue condition)
        {
            // Политика применяется по фактическому runtime, а не по заявлению caller.
            if (key != "ffr" && key != "cpuLevel" && key != "gpuLevel") return false;
            return Application.platform == RuntimePlatform.WindowsPlayer && !Application.isBatchMode &&
                !UnityEngine.XR.XRSettings.enabled && Role.StartsWith("client-", StringComparison.Ordinal) &&
                SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null &&
                ConditionEquals("applicabilityPolicy", "vrb-gpu-platform-v1") &&
                condition.Value == null && condition.Reason == "not-applicable: rendered Windows client with XR disabled";
        }
        private static bool SameConditions(ConditionsObservation left, ConditionsObservation right) => DiagnosticJson.Encode(left.Values) == DiagnosticJson.Encode(right.Values);
        private bool OwnEvent(string value) => value != null && value.StartsWith(ProcessInstanceId + ":", StringComparison.Ordinal) &&
            long.TryParse(value.Substring(ProcessInstanceId.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out long sequence) && sequence > 0;
        private static bool ReadyId(string value) => value != null && Regex.IsMatch(value, "\\A[A-Za-z0-9._-]{1,128}\\z", RegexOptions.CultureInvariant);
        private static bool Finite(Vector3 value) => DiagnosticJson.Finite(value.x) && DiagnosticJson.Finite(value.y) && DiagnosticJson.Finite(value.z);
        private static bool Finite(Quaternion value) => DiagnosticJson.Finite(value.x) && DiagnosticJson.Finite(value.y) && DiagnosticJson.Finite(value.z) && DiagnosticJson.Finite(value.w) &&
            (double)value.x * value.x + (double)value.y * value.y + (double)value.z * value.z + (double)value.w * value.w > 0;
        private static object ViewPayload(ViewObservation value) => new
        {
            viewId = value.ViewId, phaseId = value.PhaseId, phaseGeneration = value.PhaseGeneration, succeeded = value.Succeeded,
            actualPosition = value.ActualPosition.HasValue ? new { x = value.ActualPosition.Value.x, y = value.ActualPosition.Value.y, z = value.ActualPosition.Value.z } : null,
            actualRotation = value.ActualRotation.HasValue ? new { x = value.ActualRotation.Value.x, y = value.ActualRotation.Value.y, z = value.ActualRotation.Value.z, w = value.ActualRotation.Value.w } : null,
            deviationXZMetres = value.DeviationXZMetres, deviationYawDegrees = value.DeviationYawDegrees, refusal = value.Refusal, source = value.Source, stamp = value.Stamp
        };

        private DiagnosticReportResult Emit(string kind, object payload, string phaseId = null, long? generation = null, string viewId = null, string actualSceneId = null)
        {
            if (!IsActive) return Rejected(_ioFailed ? "io-error" : "inactive-session");
            try
            {
                long sequence = checked(_sequence + 1);
                DiagnosticObservationStamp emission = Now();
                bool sameGeneration = generation == null || (_phase != null && generation == _phase.PhaseGeneration);
                string eventId = ProcessInstanceId + ":" + sequence.ToString(CultureInfo.InvariantCulture);
                string line = DiagnosticJson.Encode(new
                {
                    schemaVersion = 1, runId = RunId, specSha256 = _input.SpecHash, buildManifestSha256 = _input.BuildHash,
                    processInstanceId = ProcessInstanceId, role = Role, sequence, eventId, kind,
                    frame = emission.Frame, monotonicTicks = emission.MonotonicTicks, tickFrequency = emission.TickFrequency, utcAnchor = emission.UtcAnchor,
                    actualSceneId = actualSceneId ?? ActualScene(), viewId = viewId ?? (sameGeneration ? _view?.ViewId : null), phaseId = phaseId ?? _phase?.PhaseId,
                    phaseGeneration = generation ?? _phase?.PhaseGeneration, payload
                });
                _writer.Write(line); _writer.Write('\n'); _writer.Flush();
                _sequence = sequence;
                return new DiagnosticReportResult(true, eventId, string.Empty);
            }
            catch (Exception error) when (Expected(error)) { Fault(error); return Rejected("io-error: " + error.Message); }
        }
        private DiagnosticReportResult Reject(string reason)
        {
            if (IsActive)
            {
                DiagnosticReportResult written = Emit("diagnostic.rejected", new { reason });
                if (!written.Accepted) return written;
            }
            return Rejected(_ioFailed ? "io-error: " + reason : reason);
        }
        private static DiagnosticReportResult Rejected(string reason) => new DiagnosticReportResult(false, string.Empty, reason);
        private bool InvalidateActive(string reason) => _activeReady == null || InvalidateWorkload(_activeReady, reason).Accepted;

        internal void Poll()
        {
            if (!MainThread || !IsActive || _activeReady == null) return;
            if (!Application.isFocused) InvalidateGeneration("focus-lost");
            else if (!SceneLoaded(_input.RequestedSceneId)) InvalidateGeneration("scene-changed");
        }
        internal void FocusChanged(bool focused)
        { if (MainThread && IsActive && !focused) InvalidateGeneration("focus-lost"); }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Poll(); }
        private void OnSceneUnloaded(Scene scene)
        { if (IsActive && (_conditions == null || scene.path == _conditions.ActualSceneId || scene.path == _input.RequestedSceneId)) InvalidateGeneration("scene-unloaded"); }
        private void OnActiveSceneChanged(Scene previous, Scene current)
        { if (IsActive && previous.path != current.path) InvalidateGeneration("active-scene-changed"); }
        private void InvalidateGeneration(string reason)
        {
            InvalidateActive(reason);
            if (_phase != null) _generationInvalidated = true;
            ClearObservations();
        }
        private void ClearObservations()
        { _view = null; _conditions = null; _viewEventId = _conditionsEventId = null; }
        private static bool SceneLoaded(string path)
        {
            if (!E2EDiagnosticInput.CanonicalScene(path)) return false;
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            { Scene scene = SceneManager.GetSceneAt(i); if (scene.isLoaded && scene.path == path) return true; }
            return false;
        }
        private string ActualScene() => _conditions != null && SceneLoaded(_conditions.ActualSceneId)
            ? _conditions.ActualSceneId : SceneManager.GetActiveScene().path;

        internal void End(string reason, bool completed = true)
        {
            if (!MainThread || _ended) return;
            _ended = true;
            if (IsActive)
            {
                if (InvalidateActive("process-ended: " + reason)) Emit("process.ended", new { source = "E2ERunner", reason, status = _result.Status });
            }
            _active = false; Unsubscribe(); CloseWriter();
            try { WriteActual(_ioFailed ? "error" : completed ? "completed" : "aborted", _ioFailed ? _ioReason : completed ? null : reason, completed && !_ioFailed); }
            catch (Exception error) when (Expected(error)) { Fault(error); }
        }
        private void WriteActual(string status, string error, bool completed)
        {
            File.WriteAllText(_actualPath, DiagnosticJson.Encode(new
            {
                schemaVersion = 1, runId = RunId, specSha256 = _input.SpecHash, buildManifestSha256 = _input.BuildHash,
                processInstanceId = ProcessInstanceId, role = Role, clockId = ClockId, tickFrequency = TickFrequency,
                startedUtc = _startedUtc, endedUtc = status == "started" ? null : DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                status, completed, error, lastSequence = _sequence, actualSceneId = ActualScene(), e2eResult = _resultPath,
                e2eStatus = _result.Status, e2ePassed = _result.Passed,
                // Build conditions уже экспортирует E2EResult; здесь только ссылка, без второго snapshot.
                runSpec = "../../../run-spec.json", buildManifest = "../../../build.json", events = "events.jsonl",
                clockMapping = new { verified = false, uncertaintySeconds = (double?)null, method = "process-local-stopwatch; native mapping unavailable" }
            }), DiagnosticJson.Utf8);
        }
        private void Fault(Exception error)
        {
            _ioFailed = true; _active = false; Unsubscribe();
            // Не переписываем оборванный tail. Ошибка остаётся в обычном E2E result и логе.
            CloseWriter();
            string reason = "io-error: " + error.GetType().Name + ": " + error.Message;
            _ioReason = reason;
            _result.Set("diagnostic evidence записан", false, reason);
            GameLog.Error("[E2E] Diagnostic канал закрыт: " + reason);
            try { WriteActual("error", reason, false); }
            catch (Exception failure) when (Expected(failure)) { GameLog.Error("[E2E] Diagnostic actual.json недоступен: " + failure.Message); }
        }
        private void Unsubscribe()
        {
            if (!_subscribed) return;
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged; _subscribed = false;
        }
        private void CloseWriter()
        {
            StreamWriter writer = _writer; _writer = null;
            try { writer?.Dispose(); }
            catch (Exception error) when (Expected(error))
            {
                _ioFailed = true; _active = false; _ioReason = "io-error: close: " + error.Message;
                _result.Set("diagnostic evidence записан", false, _ioReason);
                GameLog.Error("[E2E] Diagnostic flush/close: " + error.Message);
            }
        }
        private static bool Expected(Exception error) => error is IOException || error is UnauthorizedAccessException || error is JsonException ||
            error is ArgumentException || error is NotSupportedException || error is System.Security.SecurityException || error is OverflowException;
    }
}
#endif
