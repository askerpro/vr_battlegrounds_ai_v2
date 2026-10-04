using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace MCPForUnity.Editor.Helpers
{
    public static class ExecuteCodeOutputGuard
    {
        public const int MaxInlineCharacters = 6000;
        public const int MaxSummaryCharacters = 2400;
        // Только для внешнего стенда; рабочий каталог всегда принадлежит текущему проекту.
        internal static string ReportDirectoryOverride;
        public static string ReportDirectory
        {
            get { return ReportDirectoryOverride ?? Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../tmp/mcp-reports/execute-code")); }
        }

        // VR Battlegrounds patch: ограничение результата ДО передачи в Python MCP.
        public static object Limit(object result)
        {
            if (result == null) return null;
            var token = result as JToken ?? JToken.FromObject(result);
            var json = token.ToString(Formatting.None);
            if (json.Length <= MaxInlineCharacters) return result;

            Directory.CreateDirectory(ReportDirectory);
            var reportId = Guid.NewGuid().ToString("N");
            var reportPath = Path.Combine(ReportDirectory, reportId + ".json");
            // Ошибка записи должна дойти до вызывающего кода; сводка без полного файла запрещена.
            using (var stream = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(json);
            var output = new JObject
            {
                ["__mcp_output"] = new JObject
                {
                    ["truncated"] = true, ["reportId"] = reportId, ["reportPath"] = reportPath,
                    ["originalCharacters"] = json.Length,
                    ["readHint"] = "execute_code: return MCPForUnity.Editor.Helpers.ExecuteCodeOutputGuard.ReadReport(reportId, selector, offset, count);"
                },
                ["summary"] = Summarize(token)
            };
            if (Size(output) > MaxSummaryCharacters) output["summary"] = Minimal(token);
            if (Size(output) > MaxSummaryCharacters)
                throw new IOException("Report saved, but output metadata exceeds the response budget. Report ID: " + reportId);
            return output;
        }

        // Читается только созданный нами ID. Исходный код и его побочные эффекты не повторяются.
        public static JObject ReadReport(string reportId, string selector = "", int offset = 0, int count = 10)
        {
            if (reportId == null || !Regex.IsMatch(reportId, @"\A[0-9a-f]{32}\z"))
                throw new ArgumentException("Expected a 32-character lowercase report ID.");
            if (offset < 0) throw new ArgumentOutOfRangeException("offset");
            if (selector != null && selector.Length > 256) throw new ArgumentException("Selector is too long.");
            var root = JToken.Parse(File.ReadAllText(Path.Combine(ReportDirectory, reportId + ".json"), Encoding.UTF8));
            var selected = string.IsNullOrEmpty(selector) ? root : root.SelectToken(selector, true);
            if (selected == null) throw new ArgumentException("Selector matched no value.");
            var output = new JObject { ["reportId"] = reportId, ["selector"] = selector ?? "" };
            var array = selected as JArray;
            if (array != null)
            {
                var take = Math.Min(Math.Max(count, 1), 10);
                var available = Math.Max(0, array.Count - offset);
                take = Math.Min(take, available);
                var items = new JArray(array.Skip(offset).Take(take).Select(Summarize));
                output["total"] = array.Count;
                output["offset"] = offset;
                output["items"] = items;
                output["itemsTruncated"] = items.Where((item, i) => !JToken.DeepEquals(item, array[offset + i])).Any();
                while (Size(output) > 1800 && items.Count > 1) items.RemoveAt(items.Count - 1);
                if (Size(output) > 1800 && items.Count == 1)
                {
                    items[0] = Minimal(array[offset]);
                    output["itemsTruncated"] = true;
                }
                int next = offset + items.Count;
                output["nextOffset"] = next < array.Count ? (JToken)new JValue(next) : JValue.CreateNull();
            }
            else if (selected.Type == JTokenType.String)
            {
                var text = (string)selected;
                var start = Math.Min(offset, text.Length);
                var length = Math.Min(Math.Min(Math.Max(count, 1), 1200), text.Length - start);
                // Не разрывать UTF-16 пару на границе страницы.
                if (length > 0 && start + length < text.Length && char.IsHighSurrogate(text[start + length - 1])) length--;
                if (length == 0 && start < text.Length && char.IsHighSurrogate(text[start])) length = Math.Min(2, text.Length - start);
                output["text"] = text.Substring(start, length);
                output["total"] = text.Length;
                output["offset"] = start;
                output["nextOffset"] = start + length < text.Length ? (JToken)new JValue(start + length) : JValue.CreateNull();
                // Управляющие символы увеличивают размер JSON до шести раз.
                while (Size(output) > 1800 && length > 2)
                {
                    length /= 2;
                    if (char.IsHighSurrogate(text[start + length - 1])) length--;
                    output["text"] = text.Substring(start, length);
                    output["nextOffset"] = start + length;
                }
            }
            else
            {
                if (offset != 0) throw new ArgumentException("Use a selector to page an array or string.");
                output["value"] = Summarize(selected);
                output["truncated"] = !JToken.DeepEquals(output["value"], selected);
                if (Size(output) > 1800) { output["value"] = Minimal(selected); output["truncated"] = true; }
            }
            if (Size(output) > MaxSummaryCharacters) throw new InvalidOperationException("Report page exceeds its response budget.");
            return output;
        }

        private static int Size(JToken token) { return token.ToString(Formatting.None).Length; }
        private static string Clip(string text, int limit)
        {
            if (text.Length <= limit) return text;
            if (limit > 0 && char.IsHighSurrogate(text[limit - 1])) limit--;
            return text.Substring(0, limit) + "…";
        }
        private static JToken Summarize(JToken token)
        {
            int nodes = 0;
            var view = Bound(token, 0, ref nodes);
            var source = token as JObject;
            var summary = view as JObject;
            var failures = source == null ? null : source["failures"] as JArray;
            if (failures != null && summary != null)
            {
                summary.Remove("failures");
                long failureCount = ResolveFailureCount(source, failures);
                summary["failureCount"] = failureCount;
                summary["failureEntries"] = failures.Count;
                summary["failureSample"] = new JArray(failures.Take(5).Select(f => new JValue(Clip(f.Type == JTokenType.String ? (string)f : f.ToString(Formatting.None), 120))));
                if (failureCount > 0) summary["passed"] = false;
                summary["failureGroupsScope"] = failureCount > failures.Count ? "sample" : "complete";
                summary["failureGroups"] = new JArray(failures.GroupBy(f => f.Type == JTokenType.String
                    ? Clip(Regex.Replace((string)f, @"\s+-?\d+$", ""), 80) : "[structured failure]")
                    .OrderByDescending(g => g.Count()).Take(5).Select(g => new JObject { ["reason"] = g.Key, ["count"] = g.Count() }));
            }
            return view;
        }
        private static JToken Bound(JToken token, int depth, ref int nodes)
        {
            if (token is JValue) return token.Type == JTokenType.String ? new JValue(Clip((string)token, 120)) : token.DeepClone();
            if (depth >= 4 || nodes++ >= 80) return new JObject { ["omitted"] = true, ["type"] = token.Type.ToString() };
            var array = token as JArray;
            if (array != null)
            {
                var sample = new JArray();
                foreach (var item in array.Take(3)) sample.Add(Bound(item, depth + 1, ref nodes));
                return new JObject { ["total"] = array.Count, ["sample"] = sample, ["truncated"] = array.Count > 3 };
            }
            var obj = token as JObject;
            if (obj == null) return new JObject { ["omitted"] = true };
            var view = new JObject();
            var priority = new[] { "passed", "failureCount", "ready", "components", "assets" };
            var fields = obj.Properties().OrderBy(p => Array.IndexOf(priority, p.Name) < 0 ? 100 : Array.IndexOf(priority, p.Name)).ToList();
            foreach (var field in fields.Take(10)) view[Clip(field.Name, 80)] = Bound(field.Value, depth + 1, ref nodes);
            if (fields.Count > 10) view["omittedFieldCount"] = fields.Count - 10;
            return view;
        }
        private static JObject Minimal(JToken token)
        {
            var view = new JObject { ["omitted"] = true, ["type"] = token.Type.ToString() };
            var obj = token as JObject;
            if (obj != null)
            {
                foreach (var name in new[] { "passed", "ready", "components", "assets", "failureCount" })
                    if (obj[name] is JValue) view[name] = obj[name].Type == JTokenType.String ? new JValue(Clip((string)obj[name], 80)) : obj[name].DeepClone();
                var failures = obj["failures"] as JArray;
                if (failures != null)
                {
                    long failureCount = ResolveFailureCount(obj, failures);
                    view["failureCount"] = failureCount;
                    view["failureEntries"] = failures.Count;
                    if (failureCount > 0) view["passed"] = false;
                    view["failureSample"] = new JArray(failures.Take(3).Select(f => Clip(f.ToString(Formatting.None), 80)));
                }
            }
            else if (token is JValue) view["preview"] = Clip(token.ToString(Formatting.None), 120);
            else if (token is JArray) view["total"] = ((JArray)token).Count;
            return view;
        }
        private static long ResolveFailureCount(JObject source, JArray failures)
        {
            long declared;
            var count = source["failureCount"];
            return count != null && count.Type == JTokenType.Integer && long.TryParse(count.ToString(), out declared) && declared >= 0
                ? Math.Max(declared, failures.Count) : failures.Count;
        }
    }
}
