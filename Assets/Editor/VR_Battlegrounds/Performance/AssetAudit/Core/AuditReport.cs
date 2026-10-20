using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace VrBattlegrounds.Editor.Performance.AssetAudit
{
    /// <summary>Итог прогона: правила с порогами, слитые находки (серьёзные первыми), ошибки правил. JSON — LF, UTF-8 без BOM.</summary>
    public sealed class AuditReport
    {
        public const int SchemaVersion = 1;

        internal AuditReport(AuditRequest request, IReadOnlyList<AuditRule> rules, List<AuditFinding> findings,
            AuditInventory inventory, List<string> errors, DateTime startedUtc, double elapsedSeconds)
        {
            Request = request;
            Rules = rules;
            Findings = findings;
            Inventory = inventory;
            Errors = errors;
            StartedUtc = startedUtc;
            ElapsedSeconds = elapsedSeconds;
        }

        public AuditRequest Request { get; }
        public IReadOnlyList<AuditRule> Rules { get; }
        public List<AuditFinding> Findings { get; }
        public AuditInventory Inventory { get; }
        /// <summary>Исключения правил и проблемы охвата: прогон не валится, а сообщает.</summary>
        public List<string> Errors { get; }
        public DateTime StartedUtc { get; }
        public double ElapsedSeconds { get; }
        public string OutputPath { get; internal set; }

        public int Count(AuditSeverity severity) => Findings.Count(f => f.Severity == severity);

        public AuditRule FindRule(string id) => Rules.FirstOrDefault(r => r.Id == id);

        /// <summary>Короткая сводка для MCP/лога: счётчики, до <paramref name="maxExamples"/> примеров, путь к JSON.</summary>
        public string Summary(int maxExamples = 10)
        {
            var builder = new StringBuilder();
            builder.Append("asset-audit ").Append(Request.Describe())
                .Append(" | critical ").Append(Count(AuditSeverity.Critical))
                .Append(", warning ").Append(Count(AuditSeverity.Warning))
                .Append(", info ").Append(Count(AuditSeverity.Info))
                .Append(", errors ").Append(Errors.Count)
                .Append(" | assets ").Append(Inventory.TotalAssets)
                .Append(" | ").Append(ElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture)).Append(" s");
            if (!string.IsNullOrEmpty(OutputPath))
                builder.Append(" | ").Append(OutputPath);
            foreach (IGrouping<string, AuditFinding> rule in Findings.GroupBy(f => f.RuleId))
                builder.Append('\n').Append("  ").Append(rule.Key).Append(": ").Append(rule.Count()).Append(" (×").Append(rule.Sum(f => f.Count)).Append(')');
            foreach (AuditFinding finding in Findings.Take(maxExamples))
            {
                builder.Append('\n').Append("- [").Append(finding.Severity).Append("] ").Append(finding.RuleId).Append(' ')
                    .Append(finding.Asset);
                if (finding.Count > 1)
                    builder.Append(" ×").Append(finding.Count);
                builder.Append(": ").Append(finding.Message);
            }
            foreach (string error in Errors.Take(maxExamples))
                builder.Append('\n').Append("! ").Append(error);
            return builder.ToString();
        }

        public void WriteJson(string path)
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, ToJson(), new UTF8Encoding(false));
            OutputPath = path.Replace('\\', '/');
        }

        public string ToJson()
        {
            var json = new JsonOut();
            json.BeginObject();
            json.Field("schemaVersion", SchemaVersion);
            json.Field("tool", "VR Battlegrounds Asset Audit");
            json.Field("generatedAtUtc", StartedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            json.Field("unityVersion", Application.unityVersion);
            json.Field("elapsedSeconds", Math.Round(ElapsedSeconds, 2));

            json.Name("scope").BeginObject();
            json.Field("kind", Request.Kind.ToString());
            json.Name("paths").BeginArray();
            foreach (string path in Request.Paths)
                json.Value(path);
            json.EndArray();
            json.Field("includeDependencies", Request.IncludeDependencies);
            json.Name("excludePrefixes").BeginArray();
            foreach (string prefix in Request.ExcludePrefixes)
                json.Value(prefix);
            json.EndArray();
            json.Name("assets").BeginObject();
            json.Field("total", Inventory.TotalAssets);
            json.Field("materialSources", Inventory.MaterialSources.Count);
            json.Field("textures", Inventory.Textures.Count);
            json.Field("shaders", Inventory.Shaders.Count);
            json.Field("gameObjectAssets", Inventory.GameObjects.Count);
            json.EndObject();
            json.EndObject();

            json.Name("summary").BeginObject();
            json.Field("critical", Count(AuditSeverity.Critical));
            json.Field("warning", Count(AuditSeverity.Warning));
            json.Field("info", Count(AuditSeverity.Info));
            json.Field("findings", Findings.Count);
            json.Field("errors", Errors.Count);
            json.Name("byRule").BeginArray();
            foreach (AuditRule rule in Rules)
            {
                List<AuditFinding> own = Findings.Where(f => f.RuleId == rule.Id).ToList();
                if (own.Count == 0)
                    continue;
                json.BeginObject();
                json.Field("rule", rule.Id);
                json.Field("findings", own.Count);
                json.Field("instances", own.Sum(f => f.Count));
                json.Field("worst", own.Max(f => f.Severity).ToString().ToLowerInvariant());
                json.EndObject();
            }
            json.EndArray();
            json.EndObject();

            json.Name("rules").BeginArray();
            foreach (AuditRule rule in Rules)
            {
                json.BeginObject();
                json.Field("id", rule.Id);
                json.Field("title", rule.Title);
                json.Field("category", rule.Category);
                json.Field("defaultSeverity", rule.DefaultSeverity.ToString().ToLowerInvariant());
                json.Field("whyBadOnQuest", rule.WhyBadOnQuest);
                json.Field("evidence", rule.Evidence);
                json.Field("fixHint", rule.FixHint);
                json.Name("thresholds").BeginObject();
                foreach (FieldInfo field in AuditRuleCatalog.ThresholdFields(rule))
                    json.Field(field.Name, field.GetValue(rule));
                json.EndObject();
                json.EndObject();
            }
            json.EndArray();

            json.Name("findings").BeginArray();
            foreach (AuditFinding finding in Findings)
            {
                json.BeginObject();
                json.Field("rule", finding.RuleId);
                json.Field("severity", finding.Severity.ToString().ToLowerInvariant());
                json.Field("asset", finding.Asset);
                json.Field("message", finding.Message);
                json.Field("count", finding.Count);
                json.Name("metrics").BeginObject();
                foreach (AuditMetric metric in finding.Metrics)
                    json.Field(metric.Name, metric.Value);
                json.EndObject();
                json.Name("items").BeginArray();
                foreach (string item in finding.Items)
                    json.Value(item);
                json.EndArray();
                if (finding.FixHint != finding.Rule.FixHint)
                    json.Field("fixHint", finding.FixHint);
                json.EndObject();
            }
            json.EndArray();

            json.Name("errors").BeginArray();
            foreach (string error in Errors)
                json.Value(error);
            json.EndArray();
            json.EndObject();
            return json.ToString();
        }

        /// <summary>Минимальный писатель JSON с отступами и LF — без зависимостей от сторонних сборок.</summary>
        private sealed class JsonOut
        {
            private readonly StringBuilder _builder = new StringBuilder();
            private readonly Stack<bool> _hasItems = new Stack<bool>();
            private bool _afterName;

            public JsonOut BeginObject() => Open('{');
            public JsonOut EndObject() => Close('}');
            public JsonOut BeginArray() => Open('[');
            public JsonOut EndArray() => Close(']');

            public JsonOut Name(string name)
            {
                Separator();
                WriteString(name);
                _builder.Append(": ");
                _afterName = true;
                return this;
            }

            public void Field(string name, object value) => Name(name).Value(value);

            public void Value(object value)
            {
                if (value is System.Collections.IEnumerable sequence && !(value is string))
                {
                    BeginArray();
                    foreach (object item in sequence)
                        Value(item);
                    EndArray();
                    return;
                }

                Separator();
                switch (value)
                {
                    case null:
                        _builder.Append("null");
                        break;
                    case bool flag:
                        _builder.Append(flag ? "true" : "false");
                        break;
                    case string text:
                        WriteString(text);
                        break;
                    case float number:
                        _builder.Append(float.IsFinite(number) ? number.ToString("0.###", CultureInfo.InvariantCulture) : "null");
                        break;
                    case double number:
                        _builder.Append(double.IsFinite(number) ? number.ToString("0.###", CultureInfo.InvariantCulture) : "null");
                        break;
                    case int _:
                    case long _:
                        _builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                        break;
                    case Enum enumValue:
                        WriteString(enumValue.ToString());
                        break;
                    default:
                        WriteString(Convert.ToString(value, CultureInfo.InvariantCulture));
                        break;
                }
            }

            public override string ToString() => _builder.Append('\n').ToString();

            private JsonOut Open(char bracket)
            {
                Separator();
                _builder.Append(bracket);
                _hasItems.Push(false);
                return this;
            }

            private JsonOut Close(char bracket)
            {
                bool had = _hasItems.Pop();
                if (had)
                    NewLine();
                _builder.Append(bracket);
                return this;
            }

            private void Separator()
            {
                if (_afterName)
                {
                    _afterName = false;
                    return;
                }
                if (_hasItems.Count == 0)
                    return;
                if (_hasItems.Peek())
                    _builder.Append(',');
                _hasItems.Pop();
                _hasItems.Push(true);
                NewLine();
            }

            private void NewLine()
            {
                _builder.Append('\n');
                _builder.Append(' ', _hasItems.Count * 2);
            }

            private void WriteString(string text)
            {
                _builder.Append('"');
                foreach (char c in text)
                {
                    switch (c)
                    {
                        case '"': _builder.Append("\\\""); break;
                        case '\\': _builder.Append("\\\\"); break;
                        case '\n': _builder.Append("\\n"); break;
                        case '\r': _builder.Append("\\r"); break;
                        case '\t': _builder.Append("\\t"); break;
                        default:
                            if (c < 0x20)
                                _builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else
                                _builder.Append(c);
                            break;
                    }
                }
                _builder.Append('"');
            }
        }
    }
}
