using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace VrBattlegrounds.Editor.Performance.AssetAudit
{
    /// <summary>Находит правила (все неабстрактные наследники <see cref="AuditRule"/>) и применяет переопределения порогов.</summary>
    public static class AuditRuleCatalog
    {
        /// <summary>Свежие экземпляры всех правил, отсортированные по Id; выключенные запросом пропущены.</summary>
        public static List<AuditRule> CreateRules(AuditRequest request, List<string> errors)
        {
            var rules = new List<AuditRule>();
            var ids = new HashSet<string>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<AuditRule>())
            {
                if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
                    continue;
                AuditRule rule;
                try
                {
                    rule = (AuditRule)Activator.CreateInstance(type);
                }
                catch (Exception exception)
                {
                    errors.Add("правило " + type.Name + " не создано: " + exception.GetBaseException().Message);
                    continue;
                }
                if (!ids.Add(rule.Id))
                {
                    errors.Add("повтор id правила " + rule.Id + " (" + type.FullName + ") — второе пропущено");
                    continue;
                }
                if (request != null && request.DisabledRules.Contains(rule.Id))
                    continue;
                rules.Add(rule);
            }
            rules.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            if (request != null)
                ApplyOverrides(rules, request, errors);
            return rules;
        }

        public static IEnumerable<FieldInfo> ThresholdFields(AuditRule rule) =>
            rule.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.GetCustomAttribute<AuditThresholdAttribute>() != null);

        private static void ApplyOverrides(List<AuditRule> rules, AuditRequest request, List<string> errors)
        {
            foreach (KeyValuePair<string, string> entry in request.ThresholdOverrides)
            {
                int dot = entry.Key.LastIndexOf('.');
                string ruleId = dot > 0 ? entry.Key.Substring(0, dot) : entry.Key;
                string fieldName = dot > 0 ? entry.Key.Substring(dot + 1) : string.Empty;
                AuditRule rule = rules.FirstOrDefault(r => r.Id == ruleId);
                FieldInfo field = rule != null ? ThresholdFields(rule).FirstOrDefault(f => f.Name == fieldName) : null;
                if (field == null)
                {
                    errors.Add("порог не найден: " + entry.Key);
                    continue;
                }
                try
                {
                    field.SetValue(rule, Parse(field.FieldType, entry.Value));
                }
                catch (Exception exception)
                {
                    errors.Add("порог " + entry.Key + "=" + entry.Value + ": " + exception.GetBaseException().Message);
                }
            }
        }

        private static object Parse(Type type, string value)
        {
            if (type == typeof(string[]))
                return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
            if (type == typeof(string))
                return value;
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }
    }
}
