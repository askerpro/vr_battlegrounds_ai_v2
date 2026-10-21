using System.Collections.Generic;

namespace VrBattlegrounds.Editor.Performance.AssetAudit
{
    /// <summary>Метрика находки: число, флаг или строка — в JSON пишется своим типом.</summary>
    public readonly struct AuditMetric
    {
        public AuditMetric(string name, object value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; }
        public object Value { get; }
    }

    /// <summary>
    /// Находка. Правило создаёт её через <see cref="AuditContext.Report"/> и дополняет цепочкой With*.
    /// Находки с одинаковым ключом группы сливаются: <see cref="Count"/> растёт, <see cref="Items"/> копит примеры
    /// (52 одинаковые ели — одна строка «×52», а не 52 строки).
    /// </summary>
    public sealed class AuditFinding
    {
        public const int MaxItems = 25;

        internal AuditFinding(AuditRule rule, AuditSeverity severity, string asset, string message)
        {
            Rule = rule;
            RuleId = rule.Id;
            Severity = severity;
            Asset = asset ?? string.Empty;
            Message = message ?? string.Empty;
            FixHint = rule.FixHint;
        }

        public AuditRule Rule { get; }
        public string RuleId { get; }
        public AuditSeverity Severity { get; internal set; }

        /// <summary>Главный ассет находки (путь; для встроенных — имя). Окно делает его кликабельным.</summary>
        public string Asset { get; }

        public string Message { get; }
        public string FixHint { get; private set; }
        public string GroupKey { get; private set; }

        /// <summary>Сколько исходных находок слито в эту.</summary>
        public int Count { get; internal set; } = 1;

        /// <summary>Примеры: пути ассетов или объектов иерархии (не больше <see cref="MaxItems"/>).</summary>
        public List<string> Items { get; } = new List<string>();

        public List<AuditMetric> Metrics { get; } = new List<AuditMetric>();

        /// <summary>
        /// Ассеты, которые исправляет фиксер правила: все экземпляры группы, без ограничения <see cref="MaxItems"/>
        /// (в отчёт не пишутся — там <see cref="Items"/>). Пусто — фиксер берёт <see cref="Asset"/>.
        /// </summary>
        public List<string> FixTargets { get; } = new List<string>();

        /// <summary>Объект или связанный ассет этого экземпляра (материал шейдера, путь в сцене).</summary>
        public AuditFinding WithItem(string item)
        {
            if (!string.IsNullOrEmpty(item) && Items.Count < MaxItems && !Items.Contains(item))
                Items.Add(item);
            return this;
        }

        /// <summary>Ключ слияния одинаковых находок правила; по умолчанию — ассет + сообщение.</summary>
        public AuditFinding WithGroup(string groupKey)
        {
            GroupKey = groupKey;
            return this;
        }

        /// <summary>Ассет для фиксера правила (см. <see cref="FixTargets"/>).</summary>
        public AuditFinding WithFixTarget(string assetPath)
        {
            if (!string.IsNullOrEmpty(assetPath) && !FixTargets.Contains(assetPath))
                FixTargets.Add(assetPath);
            return this;
        }

        public AuditFinding WithMetric(string name, object value)
        {
            for (int i = 0; i < Metrics.Count; i++)
            {
                if (Metrics[i].Name == name)
                {
                    Metrics[i] = new AuditMetric(name, value);
                    return this;
                }
            }
            Metrics.Add(new AuditMetric(name, value));
            return this;
        }

        /// <summary>Подсказка для этого случая вместо общей подсказки правила.</summary>
        public AuditFinding WithFix(string fixHint)
        {
            FixHint = fixHint;
            return this;
        }

        internal string EffectiveGroupKey => RuleId + "|" + (GroupKey ?? Asset + "|" + Message);

        internal void MergeFrom(AuditFinding other)
        {
            Count += other.Count;
            if (other.Severity > Severity)
                Severity = other.Severity;
            foreach (string item in other.Items)
                WithItem(item);
            foreach (string target in other.FixTargets)
                WithFixTarget(target);
        }
    }
}
